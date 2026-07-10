using Pax.Cannons;
using Sandbox.Game.Inventory;
using Sandbox.ModAPI;
using System;
using System.Xml.Serialization;
using VRage.Components;
using VRage.Entities.Gravity;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Inventory;
using VRage.ObjectBuilders;
using VRageMath;

namespace Si.UtilityAI
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiThrowGrenadeBehavior : MyObjectBuilder_EntityComponent
    {
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiThrowGrenadeBehaviorDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public float MinimumDistance;
        public float MaximumDistance;
        public float BaseScore;
        public float DistanceScore;
        public float DistanceExponent;
        public float FriendlySafetyRadius;
        public float FriendlyTrajectoryRadius;
        public bool RequireLineOfSight;
        public bool RotateToTarget;
        public int EquipDelayMilliseconds;
        public int RecoveryMilliseconds;
        public int ThrowCooldownMilliseconds;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiThrowGrenadeBehaviorDefinition))]
    public class SiThrowGrenadeBehaviorDefinition : MyEntityComponentDefinition
    {
        public float MinimumDistance { get; private set; }
        public float MaximumDistance { get; private set; }
        public float BaseScore { get; private set; }
        public float DistanceScore { get; private set; }
        public float DistanceExponent { get; private set; }
        public float FriendlySafetyRadius { get; private set; }
        public float FriendlyTrajectoryRadius { get; private set; }
        public bool RequireLineOfSight { get; private set; }
        public bool RotateToTarget { get; private set; }
        public int EquipDelayMilliseconds { get; private set; }
        public int RecoveryMilliseconds { get; private set; }
        public int ThrowCooldownMilliseconds { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiThrowGrenadeBehaviorDefinition)builder;
            MinimumDistance = Math.Max(0, ob.MinimumDistance);
            MaximumDistance = Math.Max(MinimumDistance, ob.MaximumDistance);
            BaseScore = Math.Max(0, ob.BaseScore);
            DistanceScore = Math.Max(0, ob.DistanceScore);
            DistanceExponent = Math.Max(0.01f, ob.DistanceExponent);
            FriendlySafetyRadius = Math.Max(0, ob.FriendlySafetyRadius);
            FriendlyTrajectoryRadius = Math.Max(0, ob.FriendlyTrajectoryRadius);
            RequireLineOfSight = ob.RequireLineOfSight;
            RotateToTarget = ob.RotateToTarget;
            EquipDelayMilliseconds = Math.Max(0, ob.EquipDelayMilliseconds);
            RecoveryMilliseconds = Math.Max(0, ob.RecoveryMilliseconds);
            ThrowCooldownMilliseconds = Math.Max(0, ob.ThrowCooldownMilliseconds);
        }
    }

    [MyComponent(typeof(MyObjectBuilder_SiThrowGrenadeBehavior))]
    [MyDefinitionRequired(typeof(SiThrowGrenadeBehaviorDefinition))]
    public class SiThrowGrenadeBehaviorComponent : MyEntityComponent, ISiUtilityBehavior, ISiContinuousUtilityBehavior
    {
        private SiThrowGrenadeBehaviorDefinition _definition;
        private SiShootOpposingNpcBehaviorComponent _shootBehavior;
        private SiNpcRangedWeaponComponent _weapon;
        private SiNpcCombatStateComponent _combatState;
        private ThrowPhase _phase;
        private long _phaseRemainingMilliseconds;
        private long _nextThrowAllowedMilliseconds;
        private MyEntity _targetEntity;
        private Vector3D _targetPosition;
        private ThrowableInventoryItem _selectedGrenade;

        public string BehaviorName => DefinitionId.ToString();
        public override bool IsSerialized => false;

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            _definition = (SiThrowGrenadeBehaviorDefinition)definition;
        }

        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            _shootBehavior = Entity?.Components?.Get<SiShootOpposingNpcBehaviorComponent>();
            _weapon = Entity?.Components?.Get<SiNpcRangedWeaponComponent>();
            _combatState = Entity?.Components?.Get<SiNpcCombatStateComponent>();
        }

        float ISiUtilityBehavior.Evaluate(SiUtilityContext context)
        {
            if (SiNpcSessionComponent.Instance?.IsRearming(context?.Agent) == true)
                return 0;

            if (!CanEvaluate())
                return 0;

            if (!TrySelectGrenade(out _selectedGrenade))
                return 0;

            if (!_shootBehavior.TryGetCurrentThreat(context, out var targetEntity, out var targetPosition, out var distance) || targetEntity == null)
                return 0;

            if (distance < _definition.MinimumDistance || distance > _definition.MaximumDistance)
                return 0;

            if (_definition.RequireLineOfSight
                && !HasGrenadeLineOfSight(targetEntity, targetPosition))
                return 0;

            if (IsUnsafeForSquadmates(context.Agent, targetPosition))
                return 0;

            var distanceSpan = Math.Max(0.01f, _definition.MaximumDistance - _definition.MinimumDistance);
            var normalizedDistance = MathHelper.Clamp(
                (_definition.MaximumDistance - (float)distance) / distanceSpan,
                0,
                1);
            
            var score = _definition.BaseScore
                   + _definition.DistanceScore
                   * (float)Math.Pow(normalizedDistance, _definition.DistanceExponent);

            return score;
        }

        void ISiUtilityBehavior.Begin(SiUtilityContext context)
        {
            if (SiNpcSessionComponent.Instance?.IsRearming(context?.Agent) == true)
            {
                AbortThrow();
                return;
            }

            if (!CanEvaluate() || !TrySelectGrenade(out _selectedGrenade))
                return;

            if (!_shootBehavior.TryGetCurrentThreat(context, out _targetEntity, out _targetPosition, out _))
                return;

            if (IsUnsafeForSquadmates(context.Agent, _targetPosition))
                return;

            if (!_combatState.TryBeginThrow())
                return;

            _weapon?.ClearFireIntent();
            if (_definition.RotateToTarget)
                FaceTarget(_targetEntity);

            _phase = ThrowPhase.Equipping;
            _phaseRemainingMilliseconds = _definition.EquipDelayMilliseconds;
        }

        void ISiUtilityBehavior.Tick(SiUtilityContext context, long elapsedMilliseconds)
        {
            if (SiNpcSessionComponent.Instance?.IsRearming(context?.Agent) == true)
            {
                AbortThrow();
                return;
            }

            if (_phase == ThrowPhase.None)
                return;

            if (Entity == null || Entity.Closed || Entity.MarkedForClose)
            {
                AbortThrow();
                return;
            }

            if (_targetEntity == null || _targetEntity.Closed || _targetEntity.MarkedForClose)
            {
                AbortThrow();
                return;
            }

            if (_definition.RotateToTarget)
                FaceTarget(_targetEntity);

            _phaseRemainingMilliseconds = Math.Max(0, _phaseRemainingMilliseconds - Math.Max(0, elapsedMilliseconds));
            if (_phase == ThrowPhase.Equipping && _phaseRemainingMilliseconds == 0)
                ExecuteThrow();
            else if (_phase == ThrowPhase.Recovering && _phaseRemainingMilliseconds == 0)
                FinishRecovery();
        }

        void ISiUtilityBehavior.End(SiUtilityContext context)
        {
            if (_phase == ThrowPhase.None || _phase == ThrowPhase.Recovering)
                return;

            AbortThrow();
        }

        private bool CanEvaluate()
        {
            if (_shootBehavior == null || _weapon == null || _combatState == null || Entity == null)
                return false;
            if (!_combatState.CanBeginThrow && _phase == ThrowPhase.None)
                return false;
            return CurrentTimeMilliseconds() >= _nextThrowAllowedMilliseconds;
        }

        private bool TrySelectGrenade(out ThrowableInventoryItem selected)
        {
            selected = default(ThrowableInventoryItem);
            if (!TryGetInventory(out var inventory))
                return false;

            foreach (var item in inventory.Items)
            {
                if (item == null || item.Amount <= 0)
                    continue;

                var subtype = item.Subtype.String;
                if (string.IsNullOrWhiteSpace(subtype))
                    continue;

                if (!TryGetThrowableDefinition(subtype, out var throwableDefinition))
                    continue;

                if (IsSmokeGrenade(subtype))
                    continue;

                selected = new ThrowableInventoryItem(item, subtype, throwableDefinition);
                return true;
            }
            return false;
        }

        private void ExecuteThrow()
        {
            if (_targetEntity == null || _selectedGrenade.Item == null || _selectedGrenade.Definition == null)
            {
                AbortThrow();
                return;
            }

            var throwableDefinition = _selectedGrenade.Definition;
            var up = ResolveUpVector();
            var forward = ResolveForwardToTarget(_targetPosition, up);
            var position = Entity.WorldMatrix.Translation + up * 1.0 + forward * 0.75;
            if (IsUnsafeForSquadmates(Entity, position, _targetPosition))
            {
                AbortThrow();
                return;
            }

            var distance = Vector3D.Distance(Entity.WorldMatrix.Translation, _targetPosition);
            var gravityMagnitude = Math.Max(1f, (float)MyGravityProviderSystem.CalculateTotalGravityInPoint(Entity.WorldMatrix.Translation).Length());
            var requiredPower = (float)Math.Sqrt(Math.Max(0, distance * gravityMagnitude / 1.5));
            var clampedPower = MathHelper.Clamp(requiredPower, throwableDefinition.ThrowPower * 0.1f, throwableDefinition.ThrowPower);
            var holderVelocity = Entity.Physics != null ? (Vector3)Entity.Physics.LinearVelocity : Vector3.Zero;
            var velocity = holderVelocity
                           + (Vector3)forward * (clampedPower * 1.5f)
                           + (Vector3)up * (clampedPower * 0.5f);
            var fuseTime = throwableDefinition.FuseTime > 0 ? throwableDefinition.FuseTime : -1f;
            var createdEntityId = MyPAX_ThrowableItem.Throw(
                position,
                (Vector3)forward,
                (Vector3)up,
                new Vector4(velocity, fuseTime),
                throwableDefinition.ThrowItemId,
                -1,
                Entity.EntityId);

            if (createdEntityId == -1)
            {
                AbortThrow();
                return;
            }

            if (TryGetInventory(out var inventory))
                inventory.RemoveItems(_selectedGrenade.Item.DefinitionId, 1);

            TrySpeak("Grenade out");
            _combatState.BeginRecovery(_definition.RecoveryMilliseconds);
            _nextThrowAllowedMilliseconds = CurrentTimeMilliseconds() + _definition.ThrowCooldownMilliseconds;
            _phase = ThrowPhase.Recovering;
            _phaseRemainingMilliseconds = _definition.RecoveryMilliseconds;
        }

        private void FinishRecovery()
        {
            _combatState.SetFiring(false);
            ResetThrowState();
        }

        private void AbortThrow()
        {
            _combatState.CancelThrow();
            ResetThrowState();
        }

        private void ResetThrowState()
        {
            _phase = ThrowPhase.None;
            _phaseRemainingMilliseconds = 0;
            _targetEntity = null;
            _targetPosition = Vector3D.Zero;
            _selectedGrenade = default(ThrowableInventoryItem);
        }

        private bool TryGetInventory(out MyInventoryBase inventory)
        {
            string ignored;
            inventory = SiNpcEquipmentHelper.FindInventory(Entity, out ignored);
            return inventory != null;
        }

        private void TrySpeak(string message)
        {
            var entityId = Entity?.EntityId ?? 0;
            if (entityId == 0 || string.IsNullOrWhiteSpace(message))
                return;

            SiNpcSessionComponent.Instance?.Npcs?.TrySpeak(entityId, message);
        }

        private bool TryGetThrowableDefinition(string subtype, out MyPAX_ThrowableItemDefinition definition)
        {
            definition = null;
            if (string.IsNullOrWhiteSpace(subtype))
                return false;

            var definitionId = new MyDefinitionId(typeof(MyObjectBuilder_PAX_ThrowableItemDefinition), subtype);
            if (MyDefinitionManager.TryGet(definitionId, out definition) && definition != null)
                return !string.IsNullOrWhiteSpace(definition.ThrowItemId) && definition.ThrowPower > 0;

            foreach (var candidate in MyDefinitionManager.GetOfType<MyPAX_ThrowableItemDefinition>())
            {
                if (!string.Equals(candidate?.Id.SubtypeName, subtype, StringComparison.OrdinalIgnoreCase))
                    continue;

                definition = candidate;
                return !string.IsNullOrWhiteSpace(definition.ThrowItemId) && definition.ThrowPower > 0;
            }

            return false;
        }

        private float ResolveAimHeight() =>
            _weapon?.Definition?.AimTargetHeight ?? 0.9f;

        private Vector3D ResolveUpVector()
        {
            var gravity = (Vector3D)MyGravityProviderSystem.CalculateTotalGravityInPoint(Entity.WorldMatrix.Translation);
            if (gravity.LengthSquared() > 0.0001)
                return -Vector3D.Normalize(gravity);
            return SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(Entity.WorldMatrix.Up, Vector3D.Up);
        }

        private Vector3D ResolveForwardToTarget(Vector3D targetPosition, Vector3D up)
        {
            var toTarget = Vector3D.Reject(targetPosition - Entity.WorldMatrix.Translation, up);
            return SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(toTarget, Entity.WorldMatrix.Forward);
        }

        private void FaceTarget(MyEntity target)
        {
            if (target == null)
                return;

            FaceTarget(target == _targetEntity ? _targetPosition : target.WorldMatrix.Translation);
        }

        private void FaceTarget(in Vector3D targetPosition)
        {
            var world = Entity.WorldMatrix;
            var up = ResolveUpVector();
            var toTarget = Vector3D.Reject(targetPosition - world.Translation, up);
            if (toTarget.LengthSquared() <= 0.0001)
                return;

            Entity.WorldMatrix = MatrixD.CreateWorld(world.Translation, Vector3D.Normalize(toTarget), up);
        }

        private bool HasGrenadeLineOfSight(MyEntity targetEntity, in Vector3D targetPosition)
        {
            if (Entity == null || targetEntity == null)
                return false;

            var shooterUp = SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(Entity.WorldMatrix.Up, Vector3D.Up);
            var start = Entity.WorldMatrix.Translation + shooterUp * ResolveAimHeight();
            IHitInfo hit;
            if (!MyAPIGateway.Physics.CastRay(start, targetPosition, out hit))
                return true;

            if (hit == null)
                return true;
            if (hit.HitEntity == null)
                return false;
            return true;
        }

        private static bool IsSmokeGrenade(string subtype)
        {
            return subtype != null
                   && subtype.IndexOf("smoke", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsUnsafeForSquadmates(SiNpc agent, in Vector3D targetPosition)
        {
            return IsUnsafeForSquadmates(Entity, Entity?.WorldMatrix.Translation ?? Vector3D.Zero, targetPosition, agent);
        }

        private bool IsUnsafeForSquadmates(MyEntity throwerEntity, in Vector3D throwOrigin, in Vector3D targetPosition, SiNpc agent = null)
        {
            var session = SiNpcSessionComponent.Instance;
            if (session == null
                || (_definition.FriendlySafetyRadius <= 0 && _definition.FriendlyTrajectoryRadius <= 0))
                return false;

            return session.HasSquadmateInThrowDanger(
                agent,
                throwerEntity?.EntityId ?? 0,
                throwOrigin,
                targetPosition,
                _definition.FriendlySafetyRadius,
                _definition.FriendlyTrajectoryRadius,
                out _,
                out _);
        }

        private static long CurrentTimeMilliseconds()
        {
            var session = MyAPIGateway.Session;
            return session != null
                ? (long)session.ElapsedPlayTime.TotalMilliseconds
                : 0;
        }

        private enum ThrowPhase
        {
            None,
            Equipping,
            Recovering,
        }

        private struct ThrowableInventoryItem
        {
            public ThrowableInventoryItem(
                MyInventoryItem item,
                string subtype,
                MyPAX_ThrowableItemDefinition definition)
            {
                Item = item;
                Subtype = subtype;
                Definition = definition;
            }

            public readonly MyInventoryItem Item;
            public readonly string Subtype;
            public readonly MyPAX_ThrowableItemDefinition Definition;
        }
    }
}
