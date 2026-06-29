using System;
using System.Xml.Serialization;
using Sandbox.Game.Entities.Entity.Stats;
using Sandbox.Game.EntityComponents.Character;
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
        private const float MinimumDeathDamage = 0.001f;

        private MyCharacterDamageComponent _damage;
        private float? _pendingExpectedHealth;
        private MyDamageInformation _pendingDamageInformation;
        private bool _hasPendingDamageInformation;
        private bool _suppressBridgeDamage;

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
            _hasPendingDamageInformation = false;
            _suppressBridgeDamage = false;
        }

        private void OnDamageTaken(MyDamageInformation damageInformation)
        {
            if (_suppressBridgeDamage)
                return;

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
                return;

            var pendingDamageInformation = _pendingDamageInformation;
            var hasPendingDamageInformation = _hasPendingDamageInformation;
            _pendingExpectedHealth = null;
            _hasPendingDamageInformation = false;

            if (health.Current <= expectedHealth.Value + 0.001f)
                return;

            health.Current = expectedHealth.Value;

            if (expectedHealth.Value <= 0.001f && _damage != null)
            {
                var finalDamage = hasPendingDamageInformation
                    ? pendingDamageInformation
                    : new MyDamageInformation(MinimumDeathDamage, HealthStat);

                if (finalDamage.Amount < MinimumDeathDamage)
                    finalDamage = new MyDamageInformation(MinimumDeathDamage, finalDamage.Type);

                _suppressBridgeDamage = true;
                try
                {
                    _damage.DoDamage(finalDamage);
                }
                finally
                {
                    _suppressBridgeDamage = false;
                }
            }
        }

        private bool QueueDamageApplication(MyDamageInformation damageInformation)
        {
            var damageAmount = Math.Max(0, damageInformation.Amount);
            if (damageAmount <= 0)
                return false;

            MyEntityStat health;
            if (!TryResolveHealthStat(out health) || health.Current <= 0)
                return false;

            var expectedHealth = Math.Max(0, health.Current - damageAmount);
            if (!_pendingExpectedHealth.HasValue || expectedHealth < _pendingExpectedHealth.Value)
            {
                _pendingExpectedHealth = expectedHealth;
                _pendingDamageInformation = damageInformation;
                _hasPendingDamageInformation = true;
            }

            return true;
        }

        private bool TryResolveHealthStat(out MyEntityStat health)
        {
            health = null;

            var stats = Entity?.Components.Get<MyEntityStatComponent>();
            return stats != null && stats.TryGetStat(HealthStat, out health) && health != null;
        }

        private static bool IsAuthoritative()
        {
            return MyMultiplayerModApi.Static == null || MyMultiplayerModApi.Static.IsServer;
        }
    }
}
