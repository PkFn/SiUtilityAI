using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Pax.Cannons;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace Si.UtilityAI
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiNpcRangedWeaponComponent : MyObjectBuilder_EntityComponent
    {
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiNpcRangedWeaponComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public SerializableDefinitionId? Balance;

        public int FireCooldownMilliseconds;
        public int BurstCount;
        public int BurstCooldownMilliseconds;
        public string Projectile;
        public float ProjectileVelocityMultiplier;
        public float ProjectileAccuracyMultiplier;
        public float ProjectileSyncDistance;
        public float CharacterDamageMultiplier;
        public float ShootingSpreadDegrees;

        public SerializableDefinitionId? ShootEffect;
        public int MagazineCount;
        public int MagazineReloadMilliseconds;
        public string ReloadSoundName;
        public string MagazineReloadSoundName;
        public string ShootSoundName;
        public string ShootSoundMid;
        public string ShootSoundMidFront;
        public string ShootSoundFar;
        public string ShootSoundFarFront;
        public float ShootSoundSpeedMetersPerSecond;
        public float ShootSoundMaxDelayMilliseconds;
        public float ShootSoundFalloffMilliseconds;
        public float ShootSoundDirectMaximumDelayMilliseconds;
        public float ShootSoundFrontAngleThreshold;
        public float ShootSoundFrontAngleBlendRange;
        public float ShootSoundDistanceBlendStartMilliseconds;
        public float ShootSoundDistanceBlendRangeMilliseconds;

        public float AimTargetHeight;
        public float AimExtraHeight;
        public float AimCloseRangeDistance;
        public float AimCloseRangeHeightOffset;
        public float ExpectedProjectileVelocity;
        public float ElevationAiming;
        public float MuzzleForwardOffset;
        public float MuzzleUpOffset;
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiNpcRangedWeaponBalanceDefinition : MyObjectBuilder_DefinitionBase
    {
        public int FireCooldownMilliseconds;
        public int BurstCount;
        public int BurstCooldownMilliseconds;
        public string Projectile;
        public float ProjectileVelocityMultiplier;
        public float ProjectileAccuracyMultiplier;
        public float ProjectileSyncDistance;
        public float CharacterDamageMultiplier;
        public float ShootingSpreadDegrees;

        public SerializableDefinitionId? ShootEffect;
        public float ShootSoundSpeedMetersPerSecond;
        public float ShootSoundMaxDelayMilliseconds;
        public float ShootSoundFalloffMilliseconds;
        public float ShootSoundDirectMaximumDelayMilliseconds;
        public float ShootSoundFrontAngleThreshold;
        public float ShootSoundFrontAngleBlendRange;
        public float ShootSoundDistanceBlendStartMilliseconds;
        public float ShootSoundDistanceBlendRangeMilliseconds;

        public float AimTargetHeight;
        public float AimExtraHeight;
        public float AimCloseRangeDistance;
        public float AimCloseRangeHeightOffset;
        public float ExpectedProjectileVelocity;
        public float ElevationAiming;
        public float MuzzleForwardOffset;
        public float MuzzleUpOffset;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiNpcRangedWeaponBalanceDefinition))]
    public class SiNpcRangedWeaponBalanceDefinition : MyDefinitionBase
    {
        public int FireCooldownMilliseconds { get; private set; }
        public int BurstCount { get; private set; }
        public int BurstCooldownMilliseconds { get; private set; }
        public string Projectile { get; private set; }
        public float ProjectileVelocityMultiplier { get; private set; }
        public float ProjectileAccuracyMultiplier { get; private set; }
        public float ProjectileSyncDistance { get; private set; }
        public float CharacterDamageMultiplier { get; private set; }
        public float ShootingSpreadDegrees { get; private set; }
        public SerializableDefinitionId? ShootEffect { get; private set; }
        public float ShootSoundSpeedMetersPerSecond { get; private set; }
        public float ShootSoundMaxDelayMilliseconds { get; private set; }
        public float ShootSoundFalloffMilliseconds { get; private set; }
        public float ShootSoundDirectMaximumDelayMilliseconds { get; private set; }
        public float ShootSoundFrontAngleThreshold { get; private set; }
        public float ShootSoundFrontAngleBlendRange { get; private set; }
        public float ShootSoundDistanceBlendStartMilliseconds { get; private set; }
        public float ShootSoundDistanceBlendRangeMilliseconds { get; private set; }
        public float AimTargetHeight { get; private set; }
        public float AimExtraHeight { get; private set; }
        public float AimCloseRangeDistance { get; private set; }
        public float AimCloseRangeHeightOffset { get; private set; }
        public float ExpectedProjectileVelocity { get; private set; }
        public float ElevationAiming { get; private set; }
        public float MuzzleForwardOffset { get; private set; }
        public float MuzzleUpOffset { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiNpcRangedWeaponBalanceDefinition)builder;

            FireCooldownMilliseconds = Math.Max(1, ob.FireCooldownMilliseconds);
            BurstCount = Math.Max(1, ob.BurstCount);
            BurstCooldownMilliseconds = Math.Max(0, ob.BurstCooldownMilliseconds);
            Projectile = ob.Projectile;
            ProjectileVelocityMultiplier = Math.Max(0, ob.ProjectileVelocityMultiplier);
            ProjectileAccuracyMultiplier = Math.Max(0, ob.ProjectileAccuracyMultiplier);
            ProjectileSyncDistance = Math.Max(0, ob.ProjectileSyncDistance);
            CharacterDamageMultiplier = Math.Max(0, ob.CharacterDamageMultiplier);
            ShootingSpreadDegrees = Math.Max(0, ob.ShootingSpreadDegrees);
            ShootEffect = ob.ShootEffect;
            ShootSoundSpeedMetersPerSecond = Math.Max(0, ob.ShootSoundSpeedMetersPerSecond);
            ShootSoundMaxDelayMilliseconds = Math.Max(0, ob.ShootSoundMaxDelayMilliseconds);
            ShootSoundFalloffMilliseconds = Math.Max(0, ob.ShootSoundFalloffMilliseconds);
            ShootSoundDirectMaximumDelayMilliseconds = Math.Max(0, ob.ShootSoundDirectMaximumDelayMilliseconds);
            ShootSoundFrontAngleThreshold = ob.ShootSoundFrontAngleThreshold;
            ShootSoundFrontAngleBlendRange = Math.Max(0, ob.ShootSoundFrontAngleBlendRange);
            ShootSoundDistanceBlendStartMilliseconds = Math.Max(0, ob.ShootSoundDistanceBlendStartMilliseconds);
            ShootSoundDistanceBlendRangeMilliseconds = Math.Max(0, ob.ShootSoundDistanceBlendRangeMilliseconds);
            AimTargetHeight = Math.Max(0, ob.AimTargetHeight);
            AimExtraHeight = ob.AimExtraHeight;
            AimCloseRangeDistance = Math.Max(0, ob.AimCloseRangeDistance);
            AimCloseRangeHeightOffset = ob.AimCloseRangeHeightOffset;
            ExpectedProjectileVelocity = Math.Max(0.01f, ob.ExpectedProjectileVelocity);
            ElevationAiming = Math.Max(0.01f, ob.ElevationAiming);
            MuzzleForwardOffset = ob.MuzzleForwardOffset;
            MuzzleUpOffset = ob.MuzzleUpOffset;
        }
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiNpcRangedWeaponComponentDefinition))]
    public class SiNpcRangedWeaponComponentDefinition : MyEntityComponentDefinition
    {
        private SerializableDefinitionId? _balanceId;
        private bool _balanceResolved;

        public int FireCooldownMilliseconds { get; private set; }
        public int BurstCount { get; private set; }
        public int BurstCooldownMilliseconds { get; private set; }
        public string Projectile { get; private set; }
        public float ProjectileVelocityMultiplier { get; private set; }
        public float ProjectileAccuracyMultiplier { get; private set; }
        public float ProjectileSyncDistance { get; private set; }
        public float CharacterDamageMultiplier { get; private set; }
        public float ShootingSpreadDegrees { get; private set; }
        public SerializableDefinitionId? ShootEffect { get; private set; }
        public int MagazineCount { get; private set; }
        public int MagazineReloadMilliseconds { get; private set; }
        public string ReloadSoundName { get; private set; }
        public string MagazineReloadSoundName { get; private set; }
        public string ShootSoundName { get; private set; }
        public string ShootSoundMid { get; private set; }
        public string ShootSoundMidFront { get; private set; }
        public string ShootSoundFar { get; private set; }
        public string ShootSoundFarFront { get; private set; }
        public float ShootSoundSpeedMetersPerSecond { get; private set; }
        public float ShootSoundMaxDelayMilliseconds { get; private set; }
        public float ShootSoundFalloffMilliseconds { get; private set; }
        public float ShootSoundDirectMaximumDelayMilliseconds { get; private set; }
        public float ShootSoundFrontAngleThreshold { get; private set; }
        public float ShootSoundFrontAngleBlendRange { get; private set; }
        public float ShootSoundDistanceBlendStartMilliseconds { get; private set; }
        public float ShootSoundDistanceBlendRangeMilliseconds { get; private set; }
        public float AimTargetHeight { get; private set; }
        public float AimExtraHeight { get; private set; }
        public float AimCloseRangeDistance { get; private set; }
        public float AimCloseRangeHeightOffset { get; private set; }
        public float ExpectedProjectileVelocity { get; private set; }
        public float ElevationAiming { get; private set; }
        public float MuzzleForwardOffset { get; private set; }
        public float MuzzleUpOffset { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiNpcRangedWeaponComponentDefinition)builder;

            _balanceId = ob.Balance;
            _balanceResolved = false;
            InitFromBuilder(ob);
            ResolveBalance();

            MagazineCount = Math.Max(1, ob.MagazineCount);
            MagazineReloadMilliseconds = Math.Max(0, ob.MagazineReloadMilliseconds);
            ReloadSoundName = ob.ReloadSoundName;
            MagazineReloadSoundName = ob.MagazineReloadSoundName;
            ShootSoundName = ob.ShootSoundName;
            ShootSoundMid = ob.ShootSoundMid;
            ShootSoundMidFront = ob.ShootSoundMidFront;
            ShootSoundFar = ob.ShootSoundFar;
            ShootSoundFarFront = ob.ShootSoundFarFront;
        }

        internal void ResolveBalance()
        {
            if (_balanceResolved || !_balanceId.HasValue)
                return;

            var balance = LoadBalance(_balanceId);
            if (balance == null)
                return;

            InitFromBalance(balance);
            _balanceResolved = true;
        }

        private void InitFromBuilder(MyObjectBuilder_SiNpcRangedWeaponComponentDefinition ob)
        {
            FireCooldownMilliseconds = Math.Max(1, ob.FireCooldownMilliseconds);
            BurstCount = Math.Max(1, ob.BurstCount);
            BurstCooldownMilliseconds = Math.Max(0, ob.BurstCooldownMilliseconds);
            Projectile = ob.Projectile;
            ProjectileVelocityMultiplier = Math.Max(0, ob.ProjectileVelocityMultiplier);
            ProjectileAccuracyMultiplier = Math.Max(0, ob.ProjectileAccuracyMultiplier);
            ProjectileSyncDistance = Math.Max(0, ob.ProjectileSyncDistance);
            CharacterDamageMultiplier = Math.Max(0, ob.CharacterDamageMultiplier);
            ShootingSpreadDegrees = Math.Max(0, ob.ShootingSpreadDegrees);
            ShootEffect = ob.ShootEffect;
            ShootSoundSpeedMetersPerSecond = Math.Max(0, ob.ShootSoundSpeedMetersPerSecond);
            ShootSoundMaxDelayMilliseconds = Math.Max(0, ob.ShootSoundMaxDelayMilliseconds);
            ShootSoundFalloffMilliseconds = Math.Max(0, ob.ShootSoundFalloffMilliseconds);
            ShootSoundDirectMaximumDelayMilliseconds = Math.Max(0, ob.ShootSoundDirectMaximumDelayMilliseconds);
            ShootSoundFrontAngleThreshold = ob.ShootSoundFrontAngleThreshold;
            ShootSoundFrontAngleBlendRange = Math.Max(0, ob.ShootSoundFrontAngleBlendRange);
            ShootSoundDistanceBlendStartMilliseconds = Math.Max(0, ob.ShootSoundDistanceBlendStartMilliseconds);
            ShootSoundDistanceBlendRangeMilliseconds = Math.Max(0, ob.ShootSoundDistanceBlendRangeMilliseconds);
            AimTargetHeight = Math.Max(0, ob.AimTargetHeight);
            AimExtraHeight = ob.AimExtraHeight;
            AimCloseRangeDistance = Math.Max(0, ob.AimCloseRangeDistance);
            AimCloseRangeHeightOffset = ob.AimCloseRangeHeightOffset;
            ExpectedProjectileVelocity = Math.Max(0.01f, ob.ExpectedProjectileVelocity);
            ElevationAiming = Math.Max(0.01f, ob.ElevationAiming);
            MuzzleForwardOffset = ob.MuzzleForwardOffset;
            MuzzleUpOffset = ob.MuzzleUpOffset;
        }

        private void InitFromBalance(SiNpcRangedWeaponBalanceDefinition balance)
        {
            FireCooldownMilliseconds = balance.FireCooldownMilliseconds;
            BurstCount = balance.BurstCount;
            BurstCooldownMilliseconds = balance.BurstCooldownMilliseconds;
            Projectile = balance.Projectile;
            ProjectileVelocityMultiplier = balance.ProjectileVelocityMultiplier;
            ProjectileAccuracyMultiplier = balance.ProjectileAccuracyMultiplier;
            ProjectileSyncDistance = balance.ProjectileSyncDistance;
            CharacterDamageMultiplier = balance.CharacterDamageMultiplier;
            ShootingSpreadDegrees = balance.ShootingSpreadDegrees;
            ShootEffect = balance.ShootEffect;
            ShootSoundSpeedMetersPerSecond = balance.ShootSoundSpeedMetersPerSecond;
            ShootSoundMaxDelayMilliseconds = balance.ShootSoundMaxDelayMilliseconds;
            ShootSoundFalloffMilliseconds = balance.ShootSoundFalloffMilliseconds;
            ShootSoundDirectMaximumDelayMilliseconds = balance.ShootSoundDirectMaximumDelayMilliseconds;
            ShootSoundFrontAngleThreshold = balance.ShootSoundFrontAngleThreshold;
            ShootSoundFrontAngleBlendRange = balance.ShootSoundFrontAngleBlendRange;
            ShootSoundDistanceBlendStartMilliseconds = balance.ShootSoundDistanceBlendStartMilliseconds;
            ShootSoundDistanceBlendRangeMilliseconds = balance.ShootSoundDistanceBlendRangeMilliseconds;
            AimTargetHeight = balance.AimTargetHeight;
            AimExtraHeight = balance.AimExtraHeight;
            AimCloseRangeDistance = balance.AimCloseRangeDistance;
            AimCloseRangeHeightOffset = balance.AimCloseRangeHeightOffset;
            ExpectedProjectileVelocity = balance.ExpectedProjectileVelocity;
            ElevationAiming = balance.ElevationAiming;
            MuzzleForwardOffset = balance.MuzzleForwardOffset;
            MuzzleUpOffset = balance.MuzzleUpOffset;
        }

        private static SiNpcRangedWeaponBalanceDefinition LoadBalance(SerializableDefinitionId? balanceId)
        {
            if (!balanceId.HasValue)
                return null;

            SiNpcRangedWeaponBalanceDefinition balance;
            if (MyDefinitionManager.TryGet(balanceId.Value, out balance))
                return balance;

            var subtype = balanceId.Value.SubtypeId;
            if (string.IsNullOrWhiteSpace(subtype))
                return null;

            foreach (var candidate in MyDefinitionManager.GetOfType<SiNpcRangedWeaponBalanceDefinition>())
                if (string.Equals(candidate.Id.SubtypeName, subtype, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            return null;
        }
    }

    [MyComponent(typeof(MyObjectBuilder_SiNpcRangedWeaponComponent))]
    [MyDefinitionRequired(typeof(SiNpcRangedWeaponComponentDefinition))]
    [StaticEventOwner]
    public class SiNpcRangedWeaponComponent : MyEntityComponent
    {
        private static readonly Random ShotSpreadRandom = new Random();
        private static readonly object ShotSpreadRandomLock = new object();

        private readonly List<PendingShotSound> _pendingShotSounds = new List<PendingShotSound>();
        private readonly List<PendingWeaponSound> _pendingWeaponSounds = new List<PendingWeaponSound>();

        private SiNpcRangedWeaponComponentDefinition _definition;
        private long _fireCooldown;
        private int _shotsRemainingInMagazine;
        private int _shotsRemainingInBurst;

        public override bool IsSerialized => false;
        public SiNpcRangedWeaponComponentDefinition Definition => _definition;

        public bool IsOperational
        {
            get
            {
                _definition.ResolveBalance();
                return !string.IsNullOrWhiteSpace(_definition.Projectile)
                       && _definition.ProjectileVelocityMultiplier > 0
                       && _definition.ProjectileAccuracyMultiplier > 0
                       && _definition.ProjectileSyncDistance > 0
                       && SiPaxProjectileSpawner.IsAvailable
                       && ProjectileDefinitionExists(_definition.Projectile);
            }
        }

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            _definition = (SiNpcRangedWeaponComponentDefinition)definition;
            _definition.ResolveBalance();
            ResetState();
        }

        internal void ResetState()
        {
            _fireCooldown = 0;
            _shotsRemainingInMagazine = Math.Max(1, _definition?.MagazineCount ?? 1);
            _shotsRemainingInBurst = Math.Max(1, _definition?.BurstCount ?? 1);
            _pendingShotSounds.Clear();
            _pendingWeaponSounds.Clear();
        }

        internal void Advance(long elapsedMilliseconds)
        {
            if (elapsedMilliseconds <= 0)
                return;

            _fireCooldown = Math.Max(0, _fireCooldown - elapsedMilliseconds);
        }

        internal bool TryFire(SiUtilityContext context, MyEntity targetEntity, Vector3D targetVelocity)
        {
            if (!IsOperational
                || _fireCooldown > 0
                || context?.Entity == null
                || targetEntity == null)
                return false;

            if (!TryCreateShot(context.Entity, targetEntity, targetVelocity, out var projectileMatrix))
                return false;

            if (!SiPaxProjectileSpawner.TryCreateSyncedProjectile(
                    _definition.Projectile,
                    projectileMatrix,
                    _definition.ProjectileVelocityMultiplier,
                    _definition.ProjectileAccuracyMultiplier,
                    Vector3.Zero,
                    _definition.ProjectileSyncDistance,
                    _definition.CharacterDamageMultiplier,
                    context.EntityId))
                return false;

            var shotFeedback = ConsumeShot();
            _fireCooldown = shotFeedback.CooldownMilliseconds;
            SiNpcSessionComponent.Instance?.Spotting?.ReportShot(context.EntityId, context.Entity);
            PlayShotFeedback(
                context.EntityId,
                projectileMatrix,
                shotFeedback.PlayReloadSound,
                shotFeedback.PlayMagazineReloadSound);
            return true;
        }

        private bool TryCreateShot(
            MyEntity shooter,
            MyEntity targetEntity,
            Vector3D targetVelocity,
            out MatrixD projectileMatrix)
        {
            projectileMatrix = MatrixD.Identity;
            if (shooter == null || targetEntity == null)
                return false;

            var shooterWorld = shooter.WorldMatrix;
            var shooterUp = SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(shooterWorld.Up, Vector3D.Up);
            var targetWorld = targetEntity.WorldMatrix;
            var targetUp = SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(targetWorld.Up, shooterUp);

            var initialMuzzle = shooterWorld.Translation + shooterUp * _definition.AimTargetHeight;
            var aimPoint = targetWorld.Translation + targetUp * _definition.AimTargetHeight;
            var distance = (initialMuzzle - aimPoint).Length();

            var closeRangeOffset = distance < _definition.AimCloseRangeDistance
                ? _definition.AimCloseRangeHeightOffset
                : 0;
            aimPoint += targetUp * (_definition.AimExtraHeight
                                    + closeRangeOffset
                                    + distance * distance / _definition.ElevationAiming);
            aimPoint += targetVelocity * (distance / _definition.ExpectedProjectileVelocity);

            var shotDirection = SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(
                aimPoint - initialMuzzle,
                shooterWorld.Forward);
            shotDirection = ApplySpread(shotDirection, shooterUp);
            var muzzlePosition = shooterWorld.Translation
                                 + shotDirection * _definition.MuzzleForwardOffset
                                 + shooterUp * _definition.MuzzleUpOffset;
            var shotUp = RejectOrFallback(
                shooterUp,
                shotDirection,
                Vector3D.CalculatePerpendicularVector(shotDirection));
            projectileMatrix = MatrixD.CreateWorld(muzzlePosition, shotDirection, shotUp);
            return true;
        }

        private Vector3D ApplySpread(in Vector3D shotDirection, in Vector3D fallbackUp)
        {
            var spreadRadians = MathHelper.ToRadians(_definition.ShootingSpreadDegrees);
            if (spreadRadians <= 0)
                return shotDirection;

            double yaw;
            double pitch;
            lock (ShotSpreadRandomLock)
            {
                yaw = (ShotSpreadRandom.NextDouble() * 2 - 1) * spreadRadians;
                pitch = (ShotSpreadRandom.NextDouble() * 2 - 1) * spreadRadians;
            }

            var spreadRight = RejectOrFallback(
                Vector3D.CalculatePerpendicularVector(shotDirection),
                shotDirection,
                Vector3D.Right);
            var spreadUp = RejectOrFallback(
                fallbackUp,
                shotDirection,
                Vector3D.Cross(spreadRight, shotDirection));
            var spreadDirection = shotDirection
                                  + spreadRight * Math.Tan(yaw)
                                  + spreadUp * Math.Tan(pitch);
            return SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(spreadDirection, shotDirection);
        }

        private ShotFeedback ConsumeShot()
        {
            if (_shotsRemainingInMagazine <= 0)
                _shotsRemainingInMagazine = Math.Max(1, _definition.MagazineCount);
            if (_shotsRemainingInBurst <= 0)
                _shotsRemainingInBurst = Math.Max(1, _definition.BurstCount);

            _shotsRemainingInMagazine = Math.Max(0, _shotsRemainingInMagazine - 1);
            _shotsRemainingInBurst = Math.Max(0, _shotsRemainingInBurst - 1);

            var magazineEmpty = _shotsRemainingInMagazine <= 0;
            var burstFinished = _shotsRemainingInBurst <= 0;

            if (magazineEmpty)
                _shotsRemainingInMagazine = Math.Max(1, _definition.MagazineCount);
            if (burstFinished || magazineEmpty)
                _shotsRemainingInBurst = Math.Max(1, _definition.BurstCount);

            if (magazineEmpty)
            {
                return new ShotFeedback
                {
                    CooldownMilliseconds = _definition.MagazineReloadMilliseconds,
                    PlayMagazineReloadSound = !string.IsNullOrWhiteSpace(_definition.MagazineReloadSoundName),
                };
            }

            if (_definition.BurstCount > 1 && !burstFinished)
                return new ShotFeedback { CooldownMilliseconds = _definition.FireCooldownMilliseconds };

            return new ShotFeedback
            {
                CooldownMilliseconds = _definition.BurstCount > 1
                    ? _definition.BurstCooldownMilliseconds
                    : _definition.FireCooldownMilliseconds,
                PlayReloadSound = _definition.BurstCount <= 1
                                  && !string.IsNullOrWhiteSpace(_definition.ReloadSoundName),
            };
        }

        private void PlayShotFeedback(
            long entityId,
            MatrixD projectileMatrix,
            bool playReloadSound,
            bool playMagazineReloadSound)
        {
            PlayShotFeedbackLocal(projectileMatrix, playReloadSound, playMagazineReloadSound);
            if (MyMultiplayerModApi.Static != null && MyMultiplayerModApi.Static.IsServer)
                MyMultiplayerModApi.Static.RaiseStaticEvent(
                    x => PlayShotFeedbackClient,
                    entityId,
                    projectileMatrix,
                    playReloadSound,
                    playMagazineReloadSound);
        }

        [Event, Reliable, Broadcast]
        private static void PlayShotFeedbackClient(
            long entityId,
            MatrixD projectileMatrix,
            bool playReloadSound,
            bool playMagazineReloadSound)
        {
            if (MyMultiplayerModApi.Static != null && MyMultiplayerModApi.Static.IsServer)
                return;

            var manager = SiNpcSessionComponent.Instance?.Npcs;
            if (manager == null)
                return;
            if (!manager.Npcs.TryGetValue(entityId, out var npc))
                return;

            npc.Entity?.Components
                .Get<SiNpcRangedWeaponComponent>()
                ?.PlayShotFeedbackLocal(projectileMatrix, playReloadSound, playMagazineReloadSound);
        }

        private void PlayShotFeedbackLocal(
            MatrixD projectileMatrix,
            bool playReloadSound,
            bool playMagazineReloadSound)
        {
            if (_definition == null)
                return;

            PlayMuzzleEffect(projectileMatrix);
            QueueShotSound(projectileMatrix);

            var position = projectileMatrix.Translation;
            if (playReloadSound)
                QueueWeaponSound(
                    _definition.ReloadSoundName,
                    position,
                    Math.Min(500, _definition.FireCooldownMilliseconds / 3));
            if (playMagazineReloadSound)
                QueueWeaponSound(
                    _definition.MagazineReloadSoundName,
                    position,
                    Math.Min(900, _definition.MagazineReloadMilliseconds / 4));
        }

        private void PlayMuzzleEffect(MatrixD projectileMatrix)
        {
            if (!_definition.ShootEffect.HasValue)
                return;

            MyEffectDefinition effectDefinition;
            try
            {
                effectDefinition = MyDefinitionManager.Get<MyEffectDefinition>(_definition.ShootEffect.Value);
            }
            catch
            {
                return;
            }

            if (effectDefinition == null || effectDefinition.ParticleId == MyStringHash.NullOrEmpty)
                return;

            MyParticleEffect effect;
            if (!MyParticlesManager.TryCreateParticleEffect(effectDefinition.ParticleId, out effect, false)
                || effect == null)
                return;

            effect.WorldMatrix = projectileMatrix;
            effect.UserScale *= effectDefinition.ParticleScale;
        }

        private void QueueShotSound(MatrixD projectileMatrix)
        {
            if (!HasAnyShootSound
                || _definition.ShootSoundSpeedMetersPerSecond <= 0
                || _definition.ShootSoundFalloffMilliseconds <= 0)
                return;

            var camera = MyAPIGateway.Session?.Camera;
            if (camera == null)
                return;

            var position = projectileMatrix.Translation;
            var toCamera = camera.WorldMatrix.Translation - position;
            var distanceSquared = toCamera.LengthSquared();
            var distance = distanceSquared > 0.0001 ? Math.Sqrt(distanceSquared) : 0;
            var delayMilliseconds = (long)(distance * 1000 / _definition.ShootSoundSpeedMetersPerSecond);
            if (_definition.ShootSoundMaxDelayMilliseconds > 0
                && delayMilliseconds >= _definition.ShootSoundMaxDelayMilliseconds)
                return;

            var frontAngle = 0f;
            if (distance > 0.0001)
            {
                var shotDirection = SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(
                    projectileMatrix.Forward,
                    Vector3D.Forward);
                frontAngle = (float)Vector3D.Dot(shotDirection, toCamera / distance);
            }

            _pendingShotSounds.Add(new PendingShotSound
            {
                Position = position,
                DelayMilliseconds = delayMilliseconds,
                DueTimeMilliseconds = CurrentTimeMilliseconds() + delayMilliseconds,
                FrontAngle = frontAngle,
            });

            if (delayMilliseconds <= 0)
                PlayDelayedShotSound(0);
            else
                AddScheduledCallback(PlayDelayedShotSound, delayMilliseconds);
        }

        private void QueueWeaponSound(string cue, Vector3D position, long actionDelayMilliseconds)
        {
            if (string.IsNullOrWhiteSpace(cue))
                return;

            var delayMilliseconds = Math.Max(0, actionDelayMilliseconds + SoundTravelDelayMilliseconds(position));
            _pendingWeaponSounds.Add(new PendingWeaponSound
            {
                Cue = cue,
                Position = position,
                DueTimeMilliseconds = CurrentTimeMilliseconds() + delayMilliseconds,
            });

            if (delayMilliseconds <= 0)
                PlayDelayedWeaponSound(0);
            else
                AddScheduledCallback(PlayDelayedWeaponSound, delayMilliseconds);
        }

        [Update(false)]
        private void PlayDelayedWeaponSound(long elapsedMilliseconds)
        {
            if (_pendingWeaponSounds.Count == 0)
                return;

            var index = PendingWeaponSoundIndex();
            var pending = _pendingWeaponSounds[index];
            _pendingWeaponSounds.RemoveAt(index);
            PlayWorldSound(pending.Cue, pending.Position, 1f);
        }

        [Update(false)]
        private void PlayDelayedShotSound(long elapsedMilliseconds)
        {
            if (_pendingShotSounds.Count == 0)
                return;

            var index = PendingShotSoundIndex();
            var pending = _pendingShotSounds[index];
            _pendingShotSounds.RemoveAt(index);

            var distancePower = 1f - pending.DelayMilliseconds / _definition.ShootSoundFalloffMilliseconds;
            if (distancePower > 0)
                distancePower = distancePower * distancePower * distancePower;
            if (distancePower <= 0)
                return;

            if (pending.DelayMilliseconds > _definition.ShootSoundDirectMaximumDelayMilliseconds
                && HasDistanceShootSounds)
            {
                var angleRange = 2 * _definition.ShootSoundFrontAngleBlendRange;
                var angleWeight = MathHelper.Clamp(
                    (pending.FrontAngle
                     - _definition.ShootSoundFrontAngleThreshold
                     + _definition.ShootSoundFrontAngleBlendRange)
                    / angleRange,
                    0,
                    1);
                var distanceWeight = MathHelper.Clamp(
                    (pending.DelayMilliseconds - _definition.ShootSoundDistanceBlendStartMilliseconds)
                    / _definition.ShootSoundDistanceBlendRangeMilliseconds,
                    0,
                    1);

                var closeVolume = MathHelper.Clamp(1f - distanceWeight, 0, 1);
                var farVolume = MathHelper.Clamp(distanceWeight, 0, 1);
                var frontVolume = MathHelper.Clamp(angleWeight, 0, 1);
                var backVolume = MathHelper.Clamp(1f - angleWeight, 0, 1);

                PlayWorldSound(_definition.ShootSoundMidFront, pending.Position, distancePower * closeVolume * frontVolume);
                PlayWorldSound(_definition.ShootSoundMid, pending.Position, distancePower * closeVolume * backVolume);
                PlayWorldSound(_definition.ShootSoundFarFront, pending.Position, distancePower * farVolume * frontVolume);
                PlayWorldSound(_definition.ShootSoundFar, pending.Position, distancePower * farVolume * backVolume);
                return;
            }

            PlayWorldSound(_definition.ShootSoundName, pending.Position, distancePower);
        }

        private int PendingShotSoundIndex()
        {
            var bestIndex = 0;
            var bestDueTime = _pendingShotSounds[0].DueTimeMilliseconds;
            for (var i = 1; i < _pendingShotSounds.Count; i++)
            {
                var dueTime = _pendingShotSounds[i].DueTimeMilliseconds;
                if (dueTime >= bestDueTime)
                    continue;

                bestIndex = i;
                bestDueTime = dueTime;
            }

            return bestIndex;
        }

        private int PendingWeaponSoundIndex()
        {
            var bestIndex = 0;
            var bestDueTime = _pendingWeaponSounds[0].DueTimeMilliseconds;
            for (var i = 1; i < _pendingWeaponSounds.Count; i++)
            {
                var dueTime = _pendingWeaponSounds[i].DueTimeMilliseconds;
                if (dueTime >= bestDueTime)
                    continue;

                bestIndex = i;
                bestDueTime = dueTime;
            }

            return bestIndex;
        }

        private bool HasAnyShootSound =>
            !string.IsNullOrEmpty(_definition.ShootSoundName)
            || HasDistanceShootSounds;

        private bool HasDistanceShootSounds =>
            !string.IsNullOrEmpty(_definition.ShootSoundMid)
            && !string.IsNullOrEmpty(_definition.ShootSoundMidFront)
            && !string.IsNullOrEmpty(_definition.ShootSoundFar)
            && !string.IsNullOrEmpty(_definition.ShootSoundFarFront)
            && _definition.ShootSoundFrontAngleBlendRange > 0
            && _definition.ShootSoundDistanceBlendRangeMilliseconds > 0;

        private static void PlayWorldSound(string cue, Vector3D position, float volume)
        {
            if (string.IsNullOrEmpty(cue) || volume <= 0)
                return;

            var audio = Sandbox.Game.World.MyAudioComponent.Instance;
            if (audio == null)
                return;

            audio.TryPlayOneOffSound(new VRage.Audio.MyCueId(cue), position, volume, null, null);
        }

        private long SoundTravelDelayMilliseconds(Vector3D position)
        {
            if (_definition == null || _definition.ShootSoundSpeedMetersPerSecond <= 0)
                return 0;

            var camera = MyAPIGateway.Session?.Camera;
            if (camera == null)
                return 0;

            var distanceSquared = Vector3D.DistanceSquared(position, camera.WorldMatrix.Translation);
            var distance = distanceSquared > 0.0001 ? Math.Sqrt(distanceSquared) : 0;
            return (long)(distance * 1000 / _definition.ShootSoundSpeedMetersPerSecond);
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

        private static bool ProjectileDefinitionExists(string subtype)
        {
            MyContainerDefinition ignored;
            return MyDefinitionManager.TryGet(
                new MyDefinitionId(typeof(MyObjectBuilder_EntityBase), subtype),
                out ignored);
        }

        private struct PendingShotSound
        {
            public Vector3D Position;
            public long DelayMilliseconds;
            public long DueTimeMilliseconds;
            public float FrontAngle;
        }

        private struct PendingWeaponSound
        {
            public string Cue;
            public Vector3D Position;
            public long DueTimeMilliseconds;
        }

        private struct ShotFeedback
        {
            public long CooldownMilliseconds;
            public bool PlayReloadSound;
            public bool PlayMagazineReloadSound;
        }
    }

    internal static class SiPaxProjectileSpawner
    {
        public static bool IsAvailable => true;

        public static bool TryCreateSyncedProjectile(
            string projectile,
            MatrixD matrix,
            float velocity,
            float accuracy,
            Vector3 gridVelocity,
            float maxDistance,
            float characterDamageMultiplier,
            long ownerId)
        {
            try
            {
                PAX_Projectile_Spawner.ServerCreateSyncedProjectile(
                    projectile,
                    matrix,
                    velocity,
                    accuracy,
                    gridVelocity,
                    maxDistance,
                    characterDamageMultiplier,
                    ownerId);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
