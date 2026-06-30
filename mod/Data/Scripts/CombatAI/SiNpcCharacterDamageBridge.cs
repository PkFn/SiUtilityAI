using System;
using System.Linq;
using System.Xml.Serialization;
using Sandbox.Game.Entities.Entity.Stats;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Entities.Components;
using Sandbox.ModAPI;
using VRage.Components.Interfaces;
using VRage.Game.Components;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Logging;
using VRage.ObjectBuilders;
using VRage.Session;
using VRage.Utils;

namespace Si.UtilityAI
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiNpcCharacterDamageBridgeComponent : MyObjectBuilder_EntityComponent
    {
    }

    /// <summary>
    /// Some spawned NPC characters receive vanilla hit events but never have
    /// their Health stat reduced. Apply the missing stat decrement without
    /// double-counting when vanilla already handled it.
    /// </summary>
    [MyComponent(typeof(MyObjectBuilder_SiNpcCharacterDamageBridgeComponent))]
    public class SiNpcCharacterDamageBridgeComponent : MyEntityComponent
    {
        private static readonly MyStringHash HealthStat = MyStringHash.GetOrCompute("Health");
        private const float DeathThreshold = 0.001f;

        private MyCharacterDamageComponent _damage;
        private float? _pendingExpectedHealth;
        private bool _deathGuardrailsApplied;
        private NamedLogger _log;
        private bool _logInitialized;

        public override bool IsSerialized => false;

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();

            _damage = Entity?.Components.Get<MyCharacterDamageComponent>();
            if (_damage != null)
                _damage.DamageTaken += OnDamageTaken;
        }

        public override void OnRemovedFromScene()
        {
            Unregister();
            base.OnRemovedFromScene();
        }

        public override void OnBeforeRemovedFromContainer()
        {
            Unregister();
            base.OnBeforeRemovedFromContainer();
        }

        private void Unregister()
        {
            if (_damage != null)
                _damage.DamageTaken -= OnDamageTaken;

            _damage = null;
            _pendingExpectedHealth = null;
            _deathGuardrailsApplied = false;
        }

        private void OnDamageTaken(MyDamageInformation damageInformation)
        {
            Log($"OnDamageTaken amount={damageInformation.Amount:0.###} type={damageInformation.Type.String} authoritative={IsAuthoritative()}.");
            if (!IsAuthoritative())
            {
                SiNpcSessionComponent.ReportNpcDamageBridgeHit(Entity?.EntityId ?? 0, damageInformation);
                return;
            }

            if (!QueueDamageApplication(damageInformation))
                return;

            ApplyPendingDamage();
        }

        internal void ApplyReplicatedDamage(MyDamageInformation damageInformation)
        {
            if (!IsAuthoritative())
                return;

            Log($"ApplyReplicatedDamage amount={damageInformation.Amount:0.###} type={damageInformation.Type.String}.");
            if (!QueueDamageApplication(damageInformation))
                return;

            ApplyPendingDamage();
        }

        private void ApplyPendingDamage()
        {
            var expectedHealth = _pendingExpectedHealth;
            if (!expectedHealth.HasValue)
                return;

            MyEntityStat health;
            if (!TryResolveHealthStat(out health))
            {
                Log("ApplyPendingDamage aborted; failed to resolve health stat.");
                return;
            }

            _pendingExpectedHealth = null;

            if (health.Current <= expectedHealth.Value + DeathThreshold)
            {
                Log($"ApplyPendingDamage skipped; currentHealth={health.Current:0.###} expectedHealth={expectedHealth.Value:0.###}.");
                return;
            }

            // These NPCs already treat a zeroed health stat as dead in the mod's
            // own lifecycle. Avoid forcing the vanilla lethal DoDamage path
            // because it currently crashes when the equipped visual hand item is
            // torn down during death.
            if (expectedHealth.Value <= DeathThreshold)
            {
                LogDeathState($"About to apply lethal bridged health change. currentHealth={health.Current:0.###} expectedHealth={expectedHealth.Value:0.###}");
                ApplyDeathGuardrails();
                LogDeathState("Death guardrails applied.");
            }

            health.Current = expectedHealth.Value;
            Log($"ApplyPendingDamage wrote health to {expectedHealth.Value:0.###}.");
        }

        private bool QueueDamageApplication(MyDamageInformation damageInformation)
        {
            var damageAmount = Math.Max(0, damageInformation.Amount);
            if (damageAmount <= 0)
            {
                Log($"QueueDamageApplication ignored non-positive damage amount={damageInformation.Amount:0.###}.");
                return false;
            }

            MyEntityStat health;
            if (!TryResolveHealthStat(out health) || health.Current <= 0)
            {
                Log($"QueueDamageApplication aborted; healthResolved={health != null} currentHealth={(health != null ? health.Current.ToString("0.###") : "null")}.");
                return false;
            }

            var expectedHealth = Math.Max(0, health.Current - damageAmount);
            if (!_pendingExpectedHealth.HasValue || expectedHealth < _pendingExpectedHealth.Value)
            {
                _pendingExpectedHealth = expectedHealth;
                Log($"Queued damage amount={damageAmount:0.###}; currentHealth={health.Current:0.###}; expectedHealth={expectedHealth:0.###}.");
            }

            return true;
        }

        private bool TryResolveHealthStat(out MyEntityStat health)
        {
            health = null;

            var stats = Entity?.Components.Get<MyEntityStatComponent>();
            return stats != null && stats.TryGetStat(HealthStat, out health) && health != null;
        }

        private void ApplyDeathGuardrails()
        {
            if (_deathGuardrailsApplied || Entity?.Components == null)
            {
                if (_deathGuardrailsApplied)
                    Log("ApplyDeathGuardrails skipped; already applied.");
                else
                    Log("ApplyDeathGuardrails skipped; entity or components missing.");
                return;
            }

            _deathGuardrailsApplied = true;

            var handItems = Entity.Components.Get<MyCharacterHandItemsComponent>();
            if (handItems != null)
            {
                Entity.Components.Remove(handItems);
                Log("Removed MyCharacterHandItemsComponent from dying NPC.");
            }
            else
            {
                Log("No MyCharacterHandItemsComponent found on dying NPC.");
            }
        }

        private void LogDeathState(string message)
        {
            if (Entity?.Components == null)
            {
                Log($"{message} Components unavailable.");
                return;
            }

            var equipment = Entity.Components.Get<MyEntityEquipmentComponent>();
            var components = string.Join(", ", Entity.Components.GetComponents<MyEntityComponent>().Select(x => x.GetType().Name));
            var equippedSummary = equipment == null
                ? "no-equipment"
                : $"Main={DescribeSlot(equipment, "MainHand")}, Off={DescribeSlot(equipment, "OffHand")}, Ghost={DescribeSlot(equipment, "GhostHand")}";

            Log($"{message} equipped={equippedSummary} components=[{components}]");
        }

        private static string DescribeSlot(MyEntityEquipmentComponent equipment, string slotName)
        {
            var item = equipment.GetItemForSlot(MyStringHash.GetOrCompute(slotName));
            return item == null ? "empty" : item.DefinitionId.SubtypeName;
        }

        private void Log(string message)
        {
            if (!_logInitialized && MySession.Static?.Log != null)
            {
                _log = new NamedLogger(MySession.Static.Log, nameof(SiNpcCharacterDamageBridgeComponent));
                _logInitialized = true;
            }

            if (_logInitialized)
                _log.Warning($"[SiNpcDamageBridge] entityId={Entity?.EntityId ?? 0} name={Entity?.Name ?? "null"} {message}");
        }

        private static bool IsAuthoritative()
        {
            return MyMultiplayerModApi.Static == null || MyMultiplayerModApi.Static.IsServer;
        }
    }
}
