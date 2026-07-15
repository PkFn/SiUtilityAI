using System;
using Pax.Cannons;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Network;
using VRage.Utils;
using VRageMath;

namespace Si.UtilityAI
{
    [MyComponent(typeof(MyObjectBuilder_SiNpcRangedWeaponComponent))]
    [MyDefinitionRequired(typeof(SiNpcRangedWeaponComponentDefinition))]
    [StaticEventOwner]
    public partial class SiNpcRangedWeaponComponent : MyEntityComponent
    {
        private const long FireIntentGraceMilliseconds = 500;
        private const long InitialEquipmentRetryMilliseconds = 100;
        private const long EquipmentIntegrityCheckMilliseconds = 5000;

        private SiNpcRangedWeaponComponentDefinition _definition;
        private SiNpcRangedWeaponComponentDefinition _runtimeDefinition;
        private long _fireCooldown;
        private long _burstCooldown;
        private int _burstShotsRemaining;
        private long _lastFireIntentTime = long.MinValue;
        private bool _scheduledFireQueued;
        private bool _maintenanceQueued;
        private bool _heldWeaponEquipQueued;
        private ReloadMaintenanceState _reloadMaintenanceState;
        private AmmoSpeechState _lastAmmoSpeechState;
        private MyEntity _fireIntentTarget;
        private Vector3D? _fireIntentTargetPosition;
        private Vector3D _fireIntentTargetVelocity;
        private float _fireIntentAimSwayDegrees;
        private bool _aimDownSightsActive;

        public override bool IsSerialized => false;
        public SiNpcRangedWeaponComponentDefinition Definition => _runtimeDefinition ?? _definition;
        internal MyDefinitionId? HeldItemId => Definition.HeldItem.HasValue
            ? (MyDefinitionId?)Definition.HeldItem.Value
            : null;

        public bool IsOperational
        {
            get
            {
                Definition.ResolveBalance();
                Definition.ResolveWeaponBehavior();
                return Definition.HeldItem.HasValue
                       && Definition.WeaponBehavior.HasValue
                       && GetHeldGunBehavior() != null;
            }
        }

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            _definition = (SiNpcRangedWeaponComponentDefinition)definition;
            _definition.ResolveBalance();
            _definition.ResolveWeaponBehavior();
            ResetState();
        }

        internal bool ApplyRuntimeDefinition(MyDefinitionId definitionId)
        {
            SiNpcRangedWeaponComponentDefinition runtimeDefinition;
            if (!MyDefinitionManager.TryGet(definitionId, out runtimeDefinition) || runtimeDefinition == null)
                return false;

            return ApplyRuntimeDefinition(runtimeDefinition);
        }

        internal bool ApplyRuntimeDefinition(SiNpcRangedWeaponComponentDefinition runtimeDefinition)
        {
            if (runtimeDefinition == null)
                return false;

            runtimeDefinition.ResolveBalance();
            runtimeDefinition.ResolveWeaponBehavior();
            _runtimeDefinition = runtimeDefinition;
            ResetState();
            if (Entity != null && Entity.InScene && (MyAPIGateway.Multiplayer == null || MyAPIGateway.Multiplayer.IsServer))
                QueueHeldWeaponEquipmentCheck(1);
            return true;
        }

        internal void ClearRuntimeDefinition()
        {
            SetAimDownSights(false);
            _runtimeDefinition = null;
            ResetState();
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();

            if (MyAPIGateway.Multiplayer != null && !MyAPIGateway.Multiplayer.IsServer)
                return;

            QueueHeldWeaponEquipmentCheck(1);
        }

        internal void ResetState()
        {
            SetAimDownSights(false);
            _fireCooldown = 0;
            _burstCooldown = 0;
            _burstShotsRemaining = 0;
            _lastFireIntentTime = long.MinValue;
            _reloadMaintenanceState = ReloadMaintenanceState.None;
            _lastAmmoSpeechState = AmmoSpeechState.Unknown;
            _fireIntentTarget = null;
            _fireIntentTargetPosition = null;
            _fireIntentTargetVelocity = Vector3D.Zero;
            _fireIntentAimSwayDegrees = 0;
        }

        internal void Advance(long elapsedMilliseconds)
        {
            if (elapsedMilliseconds <= 0)
                return;

            _fireCooldown = Math.Max(0, _fireCooldown - elapsedMilliseconds);
            _burstCooldown = Math.Max(0, _burstCooldown - elapsedMilliseconds);
        }

        internal bool TryFire(
            SiUtilityContext context,
            MyEntity targetEntity,
            Vector3D targetVelocity,
            float aimSwayDegrees,
            Vector3D? targetPosition = null)
        {
            if (!IsOperational || _fireCooldown > 0 || _burstCooldown > 0)
                return false;
            if (context?.Entity == null || targetEntity == null)
                return false;

            var heldGun = GetHeldGunBehavior();
            if (heldGun == null)
                return false;

            SetAimDownSights(true, heldGun);
            _fireIntentTarget = targetEntity;
            _fireIntentTargetPosition = targetPosition;
            _fireIntentTargetVelocity = targetVelocity;
            _fireIntentAimSwayDegrees = Math.Max(0, aimSwayDegrees);
            _lastFireIntentTime = CurrentTimeMilliseconds();
            UpdateAmmoSpeechState();

            if (!TryFireSingleShot(context.Entity, targetEntity, targetVelocity, _fireIntentAimSwayDegrees, targetPosition))
                return false;

            StartScheduledFiring();
            return true;
        }

        internal void ClearFireIntent()
        {
            SetAimDownSights(false);
            _lastFireIntentTime = long.MinValue;
            _reloadMaintenanceState = ReloadMaintenanceState.None;
            _fireIntentTarget = null;
            _fireIntentTargetPosition = null;
            _fireIntentTargetVelocity = Vector3D.Zero;
            _fireIntentAimSwayDegrees = 0;
        }

        internal bool TryEquipHeldWeapon()
        {
            if (!Definition.HeldItem.HasValue || Entity == null)
                return false;

            string failure;
            return SiNpcEquipmentHelper.TryEnsureEquipmentItemEquipped(
                Entity,
                (MyDefinitionId)Definition.HeldItem.Value,
                out failure,
                2);
        }

        private bool TryCreateShotDirection(
            MyEntity shooter,
            MyEntity targetEntity,
            Vector3D targetVelocity,
            float aimSwayDegrees,
            Vector3D? targetPosition,
            out Quaternion direction)
        {
            direction = Quaternion.Identity;
            if (shooter == null || targetEntity == null)
                return false;

            var shooterWorld = shooter.WorldMatrix;
            var shooterUp = SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(shooterWorld.Up, Vector3D.Up);
            var targetWorld = targetEntity.WorldMatrix;
            var targetUp = SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(targetWorld.Up, shooterUp);

            var initialMuzzle = GetNpcMuzzlePosition(shooterWorld, shooterUp);
            var aimPoint = targetPosition ?? (targetWorld.Translation + targetUp * Definition.AimTargetHeight);
            var distance = (initialMuzzle - aimPoint).Length();

            var closeRangeOffset = distance < Definition.AimCloseRangeDistance
                ? Definition.AimCloseRangeHeightOffset
                : 0;
            aimPoint += targetUp * (Definition.AimExtraHeight
                                    + closeRangeOffset
                                    + distance * distance / Definition.ElevationAiming);
            aimPoint += targetVelocity * (distance / Definition.ExpectedProjectileVelocity);

            var shotDirection = SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(
                aimPoint - initialMuzzle,
                shooterWorld.Forward);
            shotDirection = ApplyAimSway(shotDirection, shooterUp, Math.Max(0, aimSwayDegrees));
            var shotUp = RejectOrFallback(
                shooterUp,
                shotDirection,
                Vector3D.CalculatePerpendicularVector(shotDirection));
            direction = Quaternion.CreateFromRotationMatrix(MatrixD.CreateWorld(Vector3D.Zero, shotDirection, shotUp));
            return true;
        }

        private bool TryFireSingleShot(
            MyEntity shooter,
            MyEntity targetEntity,
            Vector3D targetVelocity,
            float aimSwayDegrees,
            Vector3D? targetPosition)
        {
            Quaternion direction;
            if (!TryCreateShotDirection(shooter, targetEntity, targetVelocity, aimSwayDegrees, targetPosition, out direction))
                return false;

            FireFromNpcMuzzle(shooter, direction);
            ConsumeBurstShot();
            _fireCooldown = EffectiveFireIntervalMilliseconds;
            SiNpcSessionComponent.Instance?.ReportNpcFiredShot(shooter.EntityId);
            SiNpcSessionComponent.Instance?.Spotting?.ReportShot(shooter.EntityId, shooter);
            if (NeedsReloadMaintenanceAfterShot)
                BeginReloadMaintenance();
            UpdateAmmoSpeechState();
            return true;
        }

        private Vector3D GetNpcMuzzlePosition(MatrixD shooterWorld, Vector3D shooterUp)
        {
            return shooterWorld.Translation
                   + shooterUp * Definition.MuzzleUpOffset
                   + shooterWorld.Forward * Definition.MuzzleForwardOffset;
        }

        private void FireFromNpcMuzzle(MyEntity shooter, Quaternion direction)
        {
            var heldGun = GetHeldGunBehavior();
            var heldItem = heldGun?.GetItemEntity();
            if (heldItem == null
                || (Definition.MuzzleForwardOffset == 0 && Definition.MuzzleUpOffset == 0))
            {
                MyPAX_HandheldGun.ServerGunShootEvent(shooter.EntityId, direction);
                return;
            }

            var itemWorld = heldItem.WorldMatrix;
            var shotWorld = MatrixD.CreateFromQuaternion(direction);
            var shooterWorld = shooter.WorldMatrix;
            var shooterUp = SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(
                shooterWorld.Up,
                Vector3D.Up);
            var muzzleWorld = itemWorld;
            muzzleWorld.Translation = shooterWorld.Translation
                                       + shooterUp * Definition.MuzzleUpOffset
                                       + shotWorld.Forward * Definition.MuzzleForwardOffset;

            try
            {
                heldItem.WorldMatrix = muzzleWorld;
                MyPAX_HandheldGun.ServerGunShootEvent(shooter.EntityId, direction);
            }
            finally
            {
                heldItem.WorldMatrix = itemWorld;
            }
        }

        private void StartScheduledFiring()
        {
            if (_reloadMaintenanceState != ReloadMaintenanceState.None || _scheduledFireQueued)
                return;

            _scheduledFireQueued = true;
            AddScheduledCallback(ContinueScheduledFiring, GetNextFireDelayMilliseconds());
        }

        private long GetNextFireDelayMilliseconds()
        {
            if (_burstCooldown > 0)
                return _burstCooldown;

            return Math.Max(1L, EffectiveFireIntervalMilliseconds);
        }

        private void ConsumeBurstShot()
        {
            if (_burstShotsRemaining <= 0)
                _burstShotsRemaining = Math.Max(1, Definition.BurstCount);

            _burstShotsRemaining--;
            if (_burstShotsRemaining <= 0)
                _burstCooldown = Math.Max(0, Definition.BurstCooldownMilliseconds);
        }

        [Update(false)]
        private void ContinueScheduledFiring(long _)
        {
            _scheduledFireQueued = false;
            if (Entity == null || Entity.Closed || Entity.MarkedForClose)
                return;
            if (!IsOperational || _reloadMaintenanceState != ReloadMaintenanceState.None)
                return;

            var now = CurrentTimeMilliseconds();
            if (Definition.SemiAuto && now - _lastFireIntentTime > FireIntentGraceMilliseconds)
                return;

            if (_burstCooldown > 0)
            {
                _burstCooldown = 0;
                _fireCooldown = 0;
            }

            var target = _fireIntentTarget;
            if (target == null || target.Closed || target.MarkedForClose)
                return;
            if (NeedsReloadMaintenanceNow)
            {
                BeginReloadMaintenance();
                return;
            }

            if (_fireCooldown > 0)
                _fireCooldown = 0;

            if (!TryFireSingleShot(Entity, target, _fireIntentTargetVelocity, _fireIntentAimSwayDegrees, _fireIntentTargetPosition))
                return;

            StartScheduledFiring();
        }

        private static Vector3D ApplyAimSway(Vector3D shotDirection, Vector3D shooterUp, float aimSwayDegrees)
        {
            if (aimSwayDegrees <= 0)
                return shotDirection;

            var forward = SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(shotDirection, Vector3D.Forward);
            var up = RejectOrFallback(
                SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(shooterUp, Vector3D.Up),
                forward,
                Vector3D.CalculatePerpendicularVector(forward));
            var right = SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(Vector3D.Cross(forward, up), Vector3D.Right);
            up = SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(Vector3D.Cross(right, forward), up);

            var yawRadians = MathHelper.ToRadians(MyUtils.GetRandomFloat(-aimSwayDegrees, aimSwayDegrees));
            var pitchRadians = MathHelper.ToRadians(MyUtils.GetRandomFloat(-aimSwayDegrees, aimSwayDegrees));
            var swayed = forward
                         + right * Math.Tan(yawRadians)
                         + up * Math.Tan(pitchRadians);
            return SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(swayed, forward);
        }

        private void SetAimDownSights(bool enabled, MyPAX_HandheldGun heldGun = null)
        {
            if (_aimDownSightsActive == enabled)
                return;

            heldGun = heldGun ?? GetHeldGunBehavior();
            if (heldGun == null)
            {
                if (!enabled)
                    _aimDownSightsActive = false;
                return;
            }

            if (enabled)
                heldGun.StartAction(MyHandItemActionEnum.Secondary);
            else
                heldGun.EndAction(MyHandItemActionEnum.Secondary);

            _aimDownSightsActive = enabled;
        }

        private static long CurrentTimeMilliseconds()
        {
            var session = MyAPIGateway.Session;
            return session != null
                ? (long)session.ElapsedPlayTime.TotalMilliseconds
                : 0;
        }

        private static Vector3D RejectOrFallback(
            in Vector3D value,
            in Vector3D direction,
            in Vector3D fallback)
        {
            var rejected = Vector3D.Reject(value, direction);
            return SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(rejected, fallback);
        }

        [Update(false)]
        private void EnsureHeldWeaponEquipped(long _)
        {
            _heldWeaponEquipQueued = false;
            if (Entity == null || Entity.Closed || Entity.MarkedForClose || !Definition.HeldItem.HasValue)
                return;

            string ignored;
            var inventory = SiNpcEquipmentHelper.FindInventory(Entity, out ignored);
            if (inventory == null)
            {
                QueueHeldWeaponEquipmentCheck(InitialEquipmentRetryMilliseconds);
                return;
            }

            var heldItemId = (MyDefinitionId)Definition.HeldItem.Value;
            var equipment = Entity.Components.Get<Sandbox.Entities.Components.MyEntityEquipmentComponent>();
            if (!SiNpcEquipmentHelper.IsEquipmentItemEquipped(equipment, inventory, heldItemId))
                TryEquipHeldWeapon();

            QueueHeldWeaponEquipmentCheck(
                SiNpcEquipmentHelper.IsEquipmentItemEquipped(equipment, inventory, heldItemId)
                    ? EquipmentIntegrityCheckMilliseconds
                    : InitialEquipmentRetryMilliseconds);
        }

        private void QueueHeldWeaponEquipmentCheck(long delayMilliseconds)
        {
            if (_heldWeaponEquipQueued || Entity == null || Entity.Closed || Entity.MarkedForClose)
                return;

            _heldWeaponEquipQueued = true;
            AddScheduledCallback(EnsureHeldWeaponEquipped, delayMilliseconds);
        }

        private MyPAX_HandheldGun GetHeldGunBehavior()
        {
            return Entity?.Components
                .Get<MyCharacterHandItemsComponent>()
                ?.GetBehavior<MyPAX_HandheldGun>();
        }
    }
}
