using System;
using System.Xml.Serialization;
using Sandbox.Game.Entities.Entity.Stats;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.ModAPI;
using VRage.Components;
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
    [MyDependency(typeof(MyEntityStatComponent), Critical = false)]
    [MyDependency(typeof(MyCharacterDamageComponent), Critical = false)]
    public class SiNpcCharacterDamageBridgeComponent : MyEntityComponent
    {
        private static readonly MyStringHash HealthStat = MyStringHash.GetOrCompute("Health");
        private const float MinimumDeathDamage = 0.001f;

        [Automatic]
        private readonly MyEntityStatComponent _automaticStatComponent = null;

        [Automatic]
        private readonly MyCharacterDamageComponent _automaticDamageComponent = null;

        private MyCharacterDamageComponent _damage;
        private float? _pendingExpectedHealth;
        private MyDamageInformation _pendingDamageInformation;
        private bool _hasPendingDamageInformation;
        private bool _hasScheduledApply;
        private bool _suppressBridgeDamage;
        private int _queuedApplyCount;
        private int _appliedCount;
        private float _lastWriteBefore;
        private float _lastWriteTarget;
        private float _lastWriteAfterSame;
        private float _lastWriteAfterFresh;

        public override bool IsSerialized => false;

        public bool HasAutomaticStatComponent => _automaticStatComponent != null;
        public bool HasAutomaticDamageComponent => _automaticDamageComponent != null;
        public bool HasResolvedStatComponent => Entity?.Components.Get<MyEntityStatComponent>() != null;
        public bool HasResolvedDamageComponent => Entity?.Components.Get<MyCharacterDamageComponent>() != null;
        public bool HasResolvedHealthStat
        {
            get
            {
                MyEntityStat health;
                return TryResolveHealthStat(out health);
            }
        }
        public bool IsAuthoritativeNow => IsAuthoritative();
        public int QueuedApplyCount => _queuedApplyCount;
        public int AppliedCount => _appliedCount;
        public float LastWriteBefore => _lastWriteBefore;
        public float LastWriteTarget => _lastWriteTarget;
        public float LastWriteAfterSame => _lastWriteAfterSame;
        public float LastWriteAfterFresh => _lastWriteAfterFresh;

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
            _hasScheduledApply = false;
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

            ApplyPendingDamage(0);
        }

        internal void ApplyReplicatedDamage(MyDamageInformation damageInformation)
        {
            if (!IsAuthoritative())
                return;

            if (!QueueDamageApplication(damageInformation))
                return;

            ApplyPendingDamage(0);
        }

        [Update(false)]
        private void ApplyPendingDamage(long _)
        {
            _hasScheduledApply = false;

            var expectedHealth = _pendingExpectedHealth;
            if (!expectedHealth.HasValue)
                return;

            MyEntityStat health;
            if (!TryResolveHealthStat(out health))
            {
                RescheduleApply();
                return;
            }

            var pendingDamageInformation = _pendingDamageInformation;
            var hasPendingDamageInformation = _hasPendingDamageInformation;
            _pendingExpectedHealth = null;
            _hasPendingDamageInformation = false;

            if (health.Current <= expectedHealth.Value + 0.001f)
                return;

            _lastWriteBefore = health.Current;
            _lastWriteTarget = expectedHealth.Value;
            health.Current = expectedHealth.Value;
            _lastWriteAfterSame = health.Current;
            MyEntityStat freshHealth;
            _lastWriteAfterFresh = TryResolveHealthStat(out freshHealth) && freshHealth != null
                ? freshHealth.Current
                : float.NaN;
            _appliedCount++;

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

            _queuedApplyCount++;

            if (_hasScheduledApply)
                return true;

            _hasScheduledApply = true;
            AddScheduledCallback(ApplyPendingDamage, 1);
            return true;
        }

        private void RescheduleApply()
        {
            if (_hasScheduledApply || Entity == null)
                return;

            _hasScheduledApply = true;
            AddScheduledCallback(ApplyPendingDamage, 1);
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
