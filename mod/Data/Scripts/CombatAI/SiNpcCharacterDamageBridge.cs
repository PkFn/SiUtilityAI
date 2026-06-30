using System;
using System.Xml.Serialization;
using Sandbox.Game.Entities.Entity.Stats;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Entities.Components;
using Sandbox.ModAPI;
using VRage.Components.Interfaces;
using VRage.Game.Components;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
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
            ApplyAlreadyDeadGuardrails();
            if (!IsAuthoritative())
            {
                SiNpcSessionComponent.ReportNpcDamageBridgeHit(Entity?.EntityId ?? 0, damageInformation);
                return;
            }

            if (!QueueDamageApplication(damageInformation))
                return;

            SiNpcSessionComponent.Instance?.ReportNpcShotAt(Entity?.EntityId ?? 0);
            ApplyPendingDamage();
        }

        internal void ApplyReplicatedDamage(MyDamageInformation damageInformation)
        {
            if (!IsAuthoritative())
                return;

            ApplyAlreadyDeadGuardrails();
            if (!QueueDamageApplication(damageInformation))
                return;

            SiNpcSessionComponent.Instance?.ReportNpcShotAt(Entity?.EntityId ?? 0);
            ApplyPendingDamage();
        }

        private void ApplyPendingDamage()
        {
            var expectedHealth = _pendingExpectedHealth;
            if (!expectedHealth.HasValue)
                return;

            MyEntityStat health;
            if (!TryResolveHealthStat(out health))
                return;

            _pendingExpectedHealth = null;

            if (health.Current <= expectedHealth.Value + DeathThreshold)
                return;

            // These NPCs already treat a zeroed health stat as dead in the mod's
            // own lifecycle. Avoid forcing the vanilla lethal DoDamage path
            // because it currently crashes when the equipped visual hand item is
            // torn down during death.
            if (expectedHealth.Value <= DeathThreshold)
                ApplyDeathGuardrails();

            health.Current = expectedHealth.Value;
        }

        private bool QueueDamageApplication(MyDamageInformation damageInformation)
        {
            var damageAmount = Math.Max(0, damageInformation.Amount);
            if (damageAmount <= 0)
                return false;

            MyEntityStat health;
            if (!TryResolveHealthStat(out health) || health.Current <= 0)
            {
                if (health != null && health.Current <= DeathThreshold)
                    ApplyDeathGuardrails();
                return false;
            }

            var expectedHealth = Math.Max(0, health.Current - damageAmount);
            if (!_pendingExpectedHealth.HasValue || expectedHealth < _pendingExpectedHealth.Value)
                _pendingExpectedHealth = expectedHealth;

            return true;
        }

        private bool TryResolveHealthStat(out MyEntityStat health)
        {
            health = null;

            var stats = Entity?.Components.Get<MyEntityStatComponent>();
            return stats != null && stats.TryGetStat(HealthStat, out health) && health != null;
        }

        private void ApplyAlreadyDeadGuardrails()
        {
            MyEntityStat health;
            if (!TryResolveHealthStat(out health) || health == null || health.Current > DeathThreshold)
                return;

            ApplyDeathGuardrails();
        }

        private void ApplyDeathGuardrails()
        {
            if (_deathGuardrailsApplied || Entity?.Components == null)
                return;

            _deathGuardrailsApplied = true;

            var handItems = Entity.Components.Get<MyCharacterHandItemsComponent>();
            if (handItems != null)
                Entity.Components.Remove(handItems);
        }

        private static bool IsAuthoritative()
        {
            return MyMultiplayerModApi.Static == null || MyMultiplayerModApi.Static.IsServer;
        }
    }
}
