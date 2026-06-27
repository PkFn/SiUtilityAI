using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Medieval.GameSystems.Factions;
using Pax.Cannons;
using Sandbox.Game.Players;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ModAPI;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace Si.UtilityAI
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiShootOpposingNpcBehavior : MyObjectBuilder_EntityComponent
    {
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiShootOpposingNpcBehaviorDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public SerializableDefinitionId? Balance;

        public float SearchRadius;
        public float BaseScore;
        public float DistanceScore;
        public float DistanceExponent;

        public int FireCooldownMilliseconds;
        public string Projectile;
        public float ProjectileVelocityMultiplier;
        public float ProjectileAccuracyMultiplier;
        public float ProjectileSyncDistance;
        public float CharacterDamageMultiplier;

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

        public bool RequireLineOfSight;
        public bool RotateToTarget;
        public string EngageSpeech;
        public int EngageSpeechCooldownMilliseconds;
        public string SpotTargetName;
        public int SpotSpeechCooldownMilliseconds;

        [XmlArrayItem("Archetype")]
        public string[] TargetArchetypes;
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiShootOpposingNpcBehaviorBalanceDefinition : MyObjectBuilder_DefinitionBase
    {
        public float SearchRadius;
        public float BaseScore;
        public float DistanceScore;
        public float DistanceExponent;

        public int FireCooldownMilliseconds;
        public string Projectile;
        public float ProjectileVelocityMultiplier;
        public float ProjectileAccuracyMultiplier;
        public float ProjectileSyncDistance;
        public float CharacterDamageMultiplier;

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

        public bool RequireLineOfSight;
        public bool RotateToTarget;
        public string EngageSpeech;
        public int EngageSpeechCooldownMilliseconds;
        public string SpotTargetName;
        public int SpotSpeechCooldownMilliseconds;

        [XmlArrayItem("Archetype")]
        public string[] TargetArchetypes;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiShootOpposingNpcBehaviorBalanceDefinition))]
    public class SiShootOpposingNpcBehaviorBalanceDefinition : MyDefinitionBase
    {
        private static readonly string[] EmptyArchetypes = new string[0];

        public float SearchRadius { get; private set; }
        public float BaseScore { get; private set; }
        public float DistanceScore { get; private set; }
        public float DistanceExponent { get; private set; }

        public int FireCooldownMilliseconds { get; private set; }
        public string Projectile { get; private set; }
        public float ProjectileVelocityMultiplier { get; private set; }
        public float ProjectileAccuracyMultiplier { get; private set; }
        public float ProjectileSyncDistance { get; private set; }
        public float CharacterDamageMultiplier { get; private set; }

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

        public bool RequireLineOfSight { get; private set; }
        public bool RotateToTarget { get; private set; }
        public string EngageSpeech { get; private set; }
        public int EngageSpeechCooldownMilliseconds { get; private set; }
        public string SpotTargetName { get; private set; }
        public int SpotSpeechCooldownMilliseconds { get; private set; }
        public string[] TargetArchetypes { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiShootOpposingNpcBehaviorBalanceDefinition)builder;

            SearchRadius = Math.Max(0, ob.SearchRadius);
            BaseScore = Math.Max(0, ob.BaseScore);
            DistanceScore = Math.Max(0, ob.DistanceScore);
            DistanceExponent = Math.Max(0.01f, ob.DistanceExponent);

            FireCooldownMilliseconds = Math.Max(1, ob.FireCooldownMilliseconds);
            Projectile = ob.Projectile;
            ProjectileVelocityMultiplier = Math.Max(0, ob.ProjectileVelocityMultiplier);
            ProjectileAccuracyMultiplier = Math.Max(0, ob.ProjectileAccuracyMultiplier);
            ProjectileSyncDistance = Math.Max(0, ob.ProjectileSyncDistance);
            CharacterDamageMultiplier = Math.Max(0, ob.CharacterDamageMultiplier);

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

            RequireLineOfSight = ob.RequireLineOfSight;
            RotateToTarget = ob.RotateToTarget;
            EngageSpeech = ob.EngageSpeech;
            EngageSpeechCooldownMilliseconds = Math.Max(0, ob.EngageSpeechCooldownMilliseconds);
            SpotTargetName = ob.SpotTargetName;
            SpotSpeechCooldownMilliseconds = Math.Max(0, ob.SpotSpeechCooldownMilliseconds);
            TargetArchetypes = ob.TargetArchetypes ?? EmptyArchetypes;
        }
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiShootOpposingNpcBehaviorDefinition))]
    public class SiShootOpposingNpcBehaviorDefinition : MyEntityComponentDefinition
    {
        public float SearchRadius { get; private set; }
        public float BaseScore { get; private set; }
        public float DistanceScore { get; private set; }
        public float DistanceExponent { get; private set; }

        public int FireCooldownMilliseconds { get; private set; }
        public string Projectile { get; private set; }
        public float ProjectileVelocityMultiplier { get; private set; }
        public float ProjectileAccuracyMultiplier { get; private set; }
        public float ProjectileSyncDistance { get; private set; }
        public float CharacterDamageMultiplier { get; private set; }

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

        public bool RequireLineOfSight { get; private set; }
        public bool RotateToTarget { get; private set; }
        public string EngageSpeech { get; private set; }
        public int EngageSpeechCooldownMilliseconds { get; private set; }
        public string SpotTargetName { get; private set; }
        public int SpotSpeechCooldownMilliseconds { get; private set; }
        public string[] TargetArchetypes { get; private set; }
        private SerializableDefinitionId? _balanceId;
        private bool _balanceResolved;

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiShootOpposingNpcBehaviorDefinition)builder;

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

        private void InitFromBuilder(MyObjectBuilder_SiShootOpposingNpcBehaviorDefinition ob)
        {
            SearchRadius = Math.Max(0, ob.SearchRadius);
            BaseScore = Math.Max(0, ob.BaseScore);
            DistanceScore = Math.Max(0, ob.DistanceScore);
            DistanceExponent = Math.Max(0.01f, ob.DistanceExponent);

            FireCooldownMilliseconds = Math.Max(1, ob.FireCooldownMilliseconds);
            Projectile = ob.Projectile;
            ProjectileVelocityMultiplier = Math.Max(0, ob.ProjectileVelocityMultiplier);
            ProjectileAccuracyMultiplier = Math.Max(0, ob.ProjectileAccuracyMultiplier);
            ProjectileSyncDistance = Math.Max(0, ob.ProjectileSyncDistance);
            CharacterDamageMultiplier = Math.Max(0, ob.CharacterDamageMultiplier);

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

            RequireLineOfSight = ob.RequireLineOfSight;
            RotateToTarget = ob.RotateToTarget;
            EngageSpeech = ob.EngageSpeech;
            EngageSpeechCooldownMilliseconds = Math.Max(0, ob.EngageSpeechCooldownMilliseconds);
            SpotTargetName = ob.SpotTargetName;
            SpotSpeechCooldownMilliseconds = Math.Max(0, ob.SpotSpeechCooldownMilliseconds);
            TargetArchetypes = ob.TargetArchetypes ?? EmptyArchetypes;
        }

        private void InitFromBalance(SiShootOpposingNpcBehaviorBalanceDefinition balance)
        {
            SearchRadius = balance.SearchRadius;
            BaseScore = balance.BaseScore;
            DistanceScore = balance.DistanceScore;
            DistanceExponent = balance.DistanceExponent;

            FireCooldownMilliseconds = balance.FireCooldownMilliseconds;
            Projectile = balance.Projectile;
            ProjectileVelocityMultiplier = balance.ProjectileVelocityMultiplier;
            ProjectileAccuracyMultiplier = balance.ProjectileAccuracyMultiplier;
            ProjectileSyncDistance = balance.ProjectileSyncDistance;
            CharacterDamageMultiplier = balance.CharacterDamageMultiplier;

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

            RequireLineOfSight = balance.RequireLineOfSight;
            RotateToTarget = balance.RotateToTarget;
            EngageSpeech = balance.EngageSpeech;
            EngageSpeechCooldownMilliseconds = balance.EngageSpeechCooldownMilliseconds;
            SpotTargetName = balance.SpotTargetName;
            SpotSpeechCooldownMilliseconds = balance.SpotSpeechCooldownMilliseconds;
            TargetArchetypes = balance.TargetArchetypes ?? EmptyArchetypes;
        }

        private static SiShootOpposingNpcBehaviorBalanceDefinition LoadBalance(SerializableDefinitionId? balanceId)
        {
            if (!balanceId.HasValue)
                return null;

            SiShootOpposingNpcBehaviorBalanceDefinition balance;
            if (MyDefinitionManager.TryGet(balanceId.Value, out balance))
                return balance;

            var subtype = balanceId.Value.SubtypeId;
            if (string.IsNullOrWhiteSpace(subtype))
                return null;

            foreach (var candidate in MyDefinitionManager.GetOfType<SiShootOpposingNpcBehaviorBalanceDefinition>())
                if (string.Equals(candidate.Id.SubtypeName, subtype, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            return null;
        }

        private static readonly string[] EmptyArchetypes = new string[0];
    }

    /// <summary>
    /// Scores opposing NPCs and players and fires PAX defender rifle projectiles at the
    /// selected target.  Weapon tuning is supplied by the attached definition.
    /// </summary>
    [MyComponent(typeof(MyObjectBuilder_SiShootOpposingNpcBehavior))]
    [MyDefinitionRequired(typeof(SiShootOpposingNpcBehaviorDefinition))]
    [StaticEventOwner]
    public class SiShootOpposingNpcBehaviorComponent : MyEntityComponent, ISiUtilityBehavior
    {
        private static readonly MyStringHash HostileRelationship = MyStringHash.GetOrCompute("War");
        private readonly List<PendingShotSound> _pendingShotSounds = new List<PendingShotSound>();
        private readonly List<PendingWeaponSound> _pendingWeaponSounds = new List<PendingWeaponSound>();
        private SiShootOpposingNpcBehaviorDefinition _definition;
        private ShootTarget _target;
        private long _fireCooldown;
        private int _shotsRemainingInMagazine;
        private long _lastEngageSpeechTime = -1;
        private long _lastSpotSpeechTime = -1;
        private long _lastSpottedTargetId;

        public string BehaviorName => DefinitionId.ToString();

        public override bool IsSerialized => false;

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            _definition = (SiShootOpposingNpcBehaviorDefinition)definition;
            _definition.ResolveBalance();
        }

        float ISiUtilityBehavior.Evaluate(SiUtilityContext context)
        {
            if (!CanShoot)
            {
                _target = null;
                return 0;
            }

            var target = FindBestTarget(context, out var distance);
            _target = target;
            if (target == null)
            {
                _lastSpottedTargetId = 0;
                return 0;
            }

            TryReportSpotting(context, target, distance);

            var normalizedDistance = _definition.SearchRadius > 0
                ? MathHelper.Clamp(1f - (float)(distance / _definition.SearchRadius), 0, 1)
                : 1;
            return _definition.BaseScore
                   + _definition.DistanceScore
                   * (float)Math.Pow(normalizedDistance, _definition.DistanceExponent);
        }

        void ISiUtilityBehavior.Begin(SiUtilityContext context)
        {
            _fireCooldown = 0;
            ResetMagazine();
            TrySpeakWithCooldown(
                context,
                _definition.EngageSpeech,
                ref _lastEngageSpeechTime,
                _definition.EngageSpeechCooldownMilliseconds);
        }

        void ISiUtilityBehavior.Tick(SiUtilityContext context, long elapsedMilliseconds)
        {
            var session = SiNpcSessionComponent.Instance;
            if (!CanShoot
                || session?.GetEngagementStance(context.Agent) == SiSquadEngagementStance.HoldFire
                || !IsValidTarget(context.Agent, _target))
                return;

            var targetEntity = _target.Entity;
            if (_definition.RotateToTarget)
                FaceTarget(context.Entity, targetEntity);

            _fireCooldown -= elapsedMilliseconds;
            if (_fireCooldown > 0)
                return;

            if (!TryCreateShot(context, _target, out var projectileMatrix))
                return;

            if (SiPaxProjectileSpawner.TryCreateSyncedProjectile(
                    _definition.Projectile,
                    projectileMatrix,
                    _definition.ProjectileVelocityMultiplier,
                    _definition.ProjectileAccuracyMultiplier,
                    Vector3.Zero,
                    _definition.ProjectileSyncDistance,
                    _definition.CharacterDamageMultiplier,
                    context.EntityId))
            {
                var shotFeedback = ConsumeShot();
                _fireCooldown = shotFeedback.CooldownMilliseconds;
                PlayShotFeedback(
                    context.EntityId,
                    projectileMatrix,
                    shotFeedback.PlayReloadSound,
                    shotFeedback.PlayMagazineReloadSound);
            }
        }

        void ISiUtilityBehavior.End(SiUtilityContext context)
        {
            _target = null;
            _fireCooldown = 0;
            ResetMagazine();
            _lastSpottedTargetId = 0;
            _pendingShotSounds.Clear();
            _pendingWeaponSounds.Clear();
        }

        private void TryReportSpotting(SiUtilityContext context, ShootTarget target, double distance)
        {
            if (target == null)
                return;

            if (_lastSpottedTargetId == target.EntityId
                && !IsSpeechDue(_lastSpotSpeechTime, _definition.SpotSpeechCooldownMilliseconds))
                return;

            if (TrySpeakWithCooldown(
                    context,
                    CreateSpottingReport(context, target, distance),
                    ref _lastSpotSpeechTime,
                    _definition.SpotSpeechCooldownMilliseconds))
                _lastSpottedTargetId = target.EntityId;
        }

        private string CreateSpottingReport(SiUtilityContext context, ShootTarget target, double distance)
        {
            var targetName = string.IsNullOrWhiteSpace(_definition.SpotTargetName)
                ? "target"
                : _definition.SpotTargetName.Trim();
            return targetName
                   + ", "
                   + RoundedDistanceMeters(distance)
                   + " meters, "
                   + RelativeBearing(context, target)
                   + ".";
        }

        private static int RoundedDistanceMeters(double distance)
        {
            var rounded = (int)(Math.Round(Math.Max(0, distance) / 10.0) * 10);
            return Math.Max(10, rounded);
        }

        private static string RelativeBearing(SiUtilityContext context, ShootTarget target)
        {
            var self = context?.Entity;
            var targetEntity = target?.Entity;
            if (self == null || targetEntity == null)
                return "front";

            var world = self.WorldMatrix;
            var up = NormalizedOrFallback(world.Up, Vector3D.Up);
            var toTarget = Vector3D.Reject(targetEntity.WorldMatrix.Translation - world.Translation, up);
            var distanceSquared = toTarget.LengthSquared();
            if (distanceSquared <= 0.0001)
                return "front";

            var direction = toTarget / Math.Sqrt(distanceSquared);
            var forward = NormalizedOrFallback(
                Vector3D.Reject(world.Forward, up),
                Vector3D.CalculatePerpendicularVector(up));
            var right = NormalizedOrFallback(Vector3D.Cross(forward, up), world.Right);
            var angle = Math.Atan2(
                Vector3D.Dot(direction, right),
                Vector3D.Dot(direction, forward)) * 180.0 / Math.PI;
            if (angle < 0)
                angle += 360;

            if (angle < 22.5 || angle >= 337.5)
                return "front";
            if (angle < 67.5)
                return "front-right";
            if (angle < 112.5)
                return "right";
            if (angle < 157.5)
                return "rear-right";
            if (angle < 202.5)
                return "rear";
            if (angle < 247.5)
                return "rear-left";
            if (angle < 292.5)
                return "left";
            return "front-left";
        }

        private static bool TrySpeakWithCooldown(
            SiUtilityContext context,
            string message,
            ref long lastSpeechTime,
            int cooldownMilliseconds)
        {
            if (context == null
                || string.IsNullOrWhiteSpace(message)
                || !IsSpeechDue(lastSpeechTime, cooldownMilliseconds))
                return false;

            if (!context.TrySpeak(message.Trim()))
                return false;

            lastSpeechTime = CurrentTimeMilliseconds();
            return true;
        }

        private static bool IsSpeechDue(long lastSpeechTime, int cooldownMilliseconds)
        {
            if (lastSpeechTime < 0 || cooldownMilliseconds <= 0)
                return true;

            return CurrentTimeMilliseconds() - lastSpeechTime >= cooldownMilliseconds;
        }

        private ShotFeedback ConsumeShot()
        {
            if (_shotsRemainingInMagazine <= 0)
                ResetMagazine();

            _shotsRemainingInMagazine = Math.Max(0, _shotsRemainingInMagazine - 1);
            var magazineEmpty = _shotsRemainingInMagazine <= 0;
            if (magazineEmpty)
                ResetMagazine();

            return new ShotFeedback
            {
                CooldownMilliseconds = magazineEmpty
                    ? _definition.MagazineReloadMilliseconds
                    : _definition.FireCooldownMilliseconds,
                PlayReloadSound = !magazineEmpty && !string.IsNullOrWhiteSpace(_definition.ReloadSoundName),
                PlayMagazineReloadSound = magazineEmpty && !string.IsNullOrWhiteSpace(_definition.MagazineReloadSoundName),
            };
        }

        private void ResetMagazine()
        {
            _shotsRemainingInMagazine = Math.Max(1, _definition?.MagazineCount ?? 1);
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
                .Get<SiShootOpposingNpcBehaviorComponent>()
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
                var shotDirection = NormalizedOrFallback(projectileMatrix.Forward, Vector3D.Forward);
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

        private bool CanShoot
        {
            get
            {
                _definition.ResolveBalance();
                return !string.IsNullOrWhiteSpace(_definition.Projectile)
                       && _definition.SearchRadius > 0
                       && _definition.ProjectileVelocityMultiplier > 0
                       && _definition.ProjectileAccuracyMultiplier > 0
                       && _definition.ProjectileSyncDistance > 0
                       && SiPaxProjectileSpawner.IsAvailable
                       && ProjectileDefinitionExists(_definition.Projectile);
            }
        }

        private ShootTarget FindBestTarget(SiUtilityContext context, out double bestDistance)
        {
            bestDistance = 0;
            var session = SiNpcSessionComponent.Instance;
            var manager = session?.Npcs;
            if (manager == null)
                return null;

            var stance = session.GetEngagementStance(context.Agent);
            if (stance == SiSquadEngagementStance.HoldFire)
                return null;

            ShootTarget best = null;
            var bestDistanceSquared = (double)_definition.SearchRadius * _definition.SearchRadius;
            foreach (var candidate in manager.Npcs.Values)
            {
                var target = new ShootTarget(candidate);
                if (!IsValidTarget(context.Agent, target))
                    continue;
                if (!IsOpposing(context.Agent, candidate, session.Squads, stance))
                    continue;
                if (!CanTargetArchetype(candidate.Archetype))
                    continue;

                var distanceSquared = Vector3D.DistanceSquared(
                    context.Position,
                    target.Entity.WorldMatrix.Translation);
                if (distanceSquared > bestDistanceSquared)
                    continue;
                if (_definition.RequireLineOfSight
                    && !HasLineOfSight(context.Entity, target.Entity))
                    continue;

                best = target;
                bestDistanceSquared = distanceSquared;
            }

            if (MyPlayers.Static != null)
            {
                foreach (var entry in MyPlayers.Static.GetAllPlayers())
                {
                    var player = entry.Value;
                    var controlled = player?.ControlledEntity;
                    var target = new ShootTarget(player, controlled);
                    if (!IsValidTarget(context.Agent, target))
                        continue;
                    if (!IsOpposingPlayer(context.Agent, player, session.Squads, stance))
                        continue;

                    var distanceSquared = Vector3D.DistanceSquared(
                        context.Position,
                        target.Entity.WorldMatrix.Translation);
                    if (distanceSquared > bestDistanceSquared)
                        continue;
                    if (_definition.RequireLineOfSight
                        && !HasLineOfSight(context.Entity, target.Entity))
                        continue;

                    best = target;
                    bestDistanceSquared = distanceSquared;
                }
            }

            bestDistance = best != null ? Math.Sqrt(bestDistanceSquared) : 0;
            return best;
        }

        private bool TryCreateShot(
            SiUtilityContext context,
            ShootTarget target,
            out MatrixD projectileMatrix)
        {
            projectileMatrix = MatrixD.Identity;
            var shooter = context.Entity;
            var targetEntity = target?.Entity;
            if (shooter == null || targetEntity == null)
                return false;
            if (_definition.RequireLineOfSight && !HasLineOfSight(shooter, targetEntity))
                return false;

            var shooterWorld = shooter.WorldMatrix;
            var shooterUp = NormalizedOrFallback(shooterWorld.Up, Vector3D.Up);
            var targetWorld = targetEntity.WorldMatrix;
            var targetUp = NormalizedOrFallback(targetWorld.Up, shooterUp);

            var initialMuzzle = shooterWorld.Translation + shooterUp * _definition.AimTargetHeight;
            var aimPoint = targetWorld.Translation + targetUp * _definition.AimTargetHeight;
            var distance = (initialMuzzle - aimPoint).Length();

            var closeRangeOffset = distance < _definition.AimCloseRangeDistance
                ? _definition.AimCloseRangeHeightOffset
                : 0;
            aimPoint += targetUp * (_definition.AimExtraHeight
                                    + closeRangeOffset
                                    + distance * distance / _definition.ElevationAiming);
            aimPoint += target.Velocity * (distance / _definition.ExpectedProjectileVelocity);

            var shotDirection = NormalizedOrFallback(aimPoint - initialMuzzle, shooterWorld.Forward);
            var muzzlePosition = shooterWorld.Translation
                                 + shotDirection * _definition.MuzzleForwardOffset
                                 + shooterUp * _definition.MuzzleUpOffset;
            var shotUp = RejectOrFallback(shooterUp, shotDirection, Vector3D.CalculatePerpendicularVector(shotDirection));
            projectileMatrix = MatrixD.CreateWorld(muzzlePosition, shotDirection, shotUp);
            return true;
        }

        private bool IsOpposing(SiNpc self, SiNpc candidate, SiSquadBook squads, SiSquadEngagementStance stance)
        {
            if (stance == SiSquadEngagementStance.HoldFire)
                return false;

            SiAssignedNpc selfAssignment = null;
            SiAssignedNpc candidateAssignment = null;
            var hasSelfAssignment = squads != null && squads.TryGetAssignment(self.EntityId, out selfAssignment);
            var hasCandidateAssignment = squads != null && squads.TryGetAssignment(candidate.EntityId, out candidateAssignment);
            if (hasSelfAssignment && hasCandidateAssignment)
            {
                if (selfAssignment.Leader.Army.Equals(candidateAssignment.Leader.Army))
                    return false;
                if (stance == SiSquadEngagementStance.EnemiesNeutrals)
                    return true;

                return HasHostileRelationship(
                    self,
                    selfAssignment,
                    candidate,
                    candidateAssignment);
            }

            if (stance == SiSquadEngagementStance.Enemies)
                return HasHostileRelationship(
                    self,
                    hasSelfAssignment ? selfAssignment : null,
                    candidate,
                    hasCandidateAssignment ? candidateAssignment : null);

            return !string.Equals(self.Archetype, candidate.Archetype, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsOpposingPlayer(
            SiNpc self,
            MyPlayer player,
            SiSquadBook squads,
            SiSquadEngagementStance stance)
        {
            if (stance == SiSquadEngagementStance.HoldFire
                || self == null
                || player?.Identity == null)
                return false;

            SiAssignedNpc selfAssignment = null;
            var hasSelfAssignment = squads != null && squads.TryGetAssignment(self.EntityId, out selfAssignment);
            if (!hasSelfAssignment && self.DiplomaticIdentityId == 0)
                return false;

            var playerArmy = SiSquadBook.ArmyForPlayerIdentity(player.Identity.Id);
            if (hasSelfAssignment && selfAssignment.Leader.Army.Equals(playerArmy))
                return false;

            return stance == SiSquadEngagementStance.EnemiesNeutrals
                   || HasHostileRelationship(
                       self,
                       hasSelfAssignment ? selfAssignment : null,
                       player);
        }

        private static bool HasHostileRelationship(
            SiNpc self,
            SiAssignedNpc selfAssignment,
            SiNpc candidate,
            SiAssignedNpc candidateAssignment)
        {
            MyDiplomaticParty selfParty;
            MyDiplomaticParty candidateParty;
            return TryCreateNpcDiplomaticParty(self, selfAssignment, out selfParty)
                   && TryCreateNpcDiplomaticParty(candidate, candidateAssignment, out candidateParty)
                   && HasHostileRelationship(selfParty, candidateParty);
        }

        private static bool HasHostileRelationship(
            SiNpc self,
            SiAssignedNpc selfAssignment,
            MyPlayer player)
        {
            MyDiplomaticParty selfParty;
            MyDiplomaticParty playerParty;
            return TryCreateNpcDiplomaticParty(self, selfAssignment, out selfParty)
                   && TryCreatePlayerDiplomaticParty(player, out playerParty)
                   && HasHostileRelationship(selfParty, playerParty);
        }

        private static bool TryCreateNpcDiplomaticParty(
            SiNpc npc,
            SiAssignedNpc assignment,
            out MyDiplomaticParty party)
        {
            party = default(MyDiplomaticParty);
            if (assignment != null
                && SiSquadBook.TryCreateDiplomaticParty(assignment.Leader.Army, out party))
                return true;

            if (npc != null && npc.DiplomaticIdentityId != 0)
            {
                var faction = PlayerFaction(npc.DiplomaticIdentityId);
                party = faction != null
                    ? new MyDiplomaticParty(faction)
                    : new MyDiplomaticParty(DiplomaticPartyType.Player, npc.DiplomaticIdentityId);
                return true;
            }

            return false;
        }

        private static bool TryCreatePlayerDiplomaticParty(MyPlayer player, out MyDiplomaticParty party)
        {
            party = default(MyDiplomaticParty);
            if (player?.Identity == null)
                return false;

            return SiSquadBook.TryCreateDiplomaticParty(
                SiSquadBook.ArmyForPlayerIdentity(player.Identity.Id),
                out party);
        }

        private static MyFaction PlayerFaction(long identityId)
        {
            try
            {
                return MyFactionManager.GetPlayerFaction(identityId);
            }
            catch
            {
                return null;
            }
        }

        private static bool HasHostileRelationship(
            MyDiplomaticParty selfParty,
            MyDiplomaticParty candidateParty)
        {
            var diplomacy = MyDiplomacyManager.Instance;
            if (diplomacy == null)
                return false;

            return IsHostileRelationship(diplomacy, selfParty, candidateParty)
                   || IsHostileRelationship(diplomacy, candidateParty, selfParty);
        }

        private static bool IsHostileRelationship(
            MyDiplomacyManager diplomacy,
            MyDiplomaticParty selfParty,
            MyDiplomaticParty candidateParty)
        {
            if (diplomacy == null)
                return false;

            try
            {
                return diplomacy.GetRelationshipBetweenParties(selfParty, candidateParty).Status == HostileRelationship;
            }
            catch
            {
                return false;
            }
        }

        private bool CanTargetArchetype(string archetype)
        {
            if (_definition.TargetArchetypes.Length == 0)
                return true;

            for (var i = 0; i < _definition.TargetArchetypes.Length; i++)
                if (string.Equals(_definition.TargetArchetypes[i], archetype, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static bool IsValidTarget(SiNpc self, ShootTarget target)
        {
            if (self == null || target?.Entity == null)
                return false;
            if (target.Npc?.IsDead ?? false)
                return false;

            var entity = target.Entity;
            return entity != self.Entity
                   && entity.EntityId != self.EntityId
                   && entity.InScene
                   && !entity.Closed
                   && !entity.MarkedForClose;
        }

        private static Vector3D TargetVelocity(ShootTarget target)
        {
            if (target == null)
                return Vector3D.Zero;
            if (target.Npc != null)
                return TargetVelocity(target.Npc);
            return target.Entity?.Physics != null
                ? target.Entity.Physics.LinearVelocity
                : Vector3D.Zero;
        }

        private static Vector3D TargetVelocity(SiNpc target)
        {
            if (target is SiGroundedNpc grounded)
                return grounded.Velocity;
            return target?.Entity?.Physics != null
                ? target.Entity.Physics.LinearVelocity
                : Vector3D.Zero;
        }

        private void FaceTarget(MyEntity shooter, MyEntity target)
        {
            if (shooter == null || target == null)
                return;

            var world = shooter.WorldMatrix;
            var up = NormalizedOrFallback(world.Up, Vector3D.Up);
            var toTarget = Vector3D.Reject(target.WorldMatrix.Translation - world.Translation, up);
            if (toTarget.LengthSquared() <= 0.0001)
                return;

            var forward = toTarget / Math.Sqrt(toTarget.LengthSquared());
            shooter.WorldMatrix = MatrixD.CreateWorld(world.Translation, forward, up);
        }

        private bool HasLineOfSight(MyEntity shooter, MyEntity target)
        {
            if (shooter == null || target == null)
                return false;

            var shooterUp = NormalizedOrFallback(shooter.WorldMatrix.Up, Vector3D.Up);
            var targetUp = NormalizedOrFallback(target.WorldMatrix.Up, shooterUp);
            var start = shooter.WorldMatrix.Translation + shooterUp * _definition.AimTargetHeight;
            var end = target.WorldMatrix.Translation + targetUp * _definition.AimTargetHeight;

            IHitInfo hit;
            if (!MyAPIGateway.Physics.CastRay(start, end, out hit))
                return true;

            return hit == null
                   || hit.HitEntity == null
                   || hit.HitEntity == target
                   || hit.HitEntity == shooter;
        }

        private sealed class ShootTarget
        {
            public ShootTarget(SiNpc npc)
            {
                Npc = npc;
                Entity = npc?.Entity;
                EntityId = npc?.EntityId ?? 0;
            }

            public ShootTarget(MyPlayer player, MyEntity entity)
            {
                Player = player;
                Entity = entity;
                EntityId = entity?.EntityId ?? 0;
            }

            public SiNpc Npc { get; }
            public MyPlayer Player { get; }
            public MyEntity Entity { get; }
            public long EntityId { get; }
            public Vector3D Velocity => TargetVelocity(this);
        }

        private static Vector3D NormalizedOrFallback(in Vector3D value, in Vector3D fallback)
        {
            var lengthSquared = value.LengthSquared();
            return lengthSquared > 0.0001
                ? value / Math.Sqrt(lengthSquared)
                : fallback;
        }

        private static Vector3D RejectOrFallback(
            in Vector3D value,
            in Vector3D direction,
            in Vector3D fallback)
        {
            var rejected = Vector3D.Reject(value, direction);
            return NormalizedOrFallback(rejected, fallback);
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
