using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Pax.Cannons;
using Sandbox.ModAPI;
using SiCore.Core.Debug;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Inventory;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Inventory;
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
        public SerializableDefinitionId? HeldItem;
        public SerializableDefinitionId? WeaponBehavior;

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
        private static readonly string[] EmptyStrings = new string[0];
        private SerializableDefinitionId? _balanceId;
        private SerializableDefinitionId? _weaponBehaviorId;
        private bool _balanceResolved;
        private bool _weaponBehaviorResolved;

        public SerializableDefinitionId? HeldItem { get; private set; }
        public SerializableDefinitionId? WeaponBehavior { get; private set; }
        public bool ConsumeAmmo { get; private set; }
        public bool InternallyLoaded { get; private set; }
        public string[] AcceptedCartridges { get; private set; }
        public string[] AcceptedMagazines { get; private set; }
        public string ShootEffectName { get; private set; }
        public float ShootEffectScale { get; private set; }
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
            _weaponBehaviorId = ob.WeaponBehavior;
            _balanceResolved = false;
            _weaponBehaviorResolved = false;
            HeldItem = ob.HeldItem;
            WeaponBehavior = ob.WeaponBehavior;
            InitFromBuilder(ob);
            ResolveBalance();
            ResolveWeaponBehavior();

            if (ob.MagazineCount > 0)
                MagazineCount = Math.Max(1, ob.MagazineCount);
            if (ob.MagazineReloadMilliseconds > 0)
                MagazineReloadMilliseconds = Math.Max(0, ob.MagazineReloadMilliseconds);
            if (!string.IsNullOrWhiteSpace(ob.ReloadSoundName))
                ReloadSoundName = ob.ReloadSoundName;
            if (!string.IsNullOrWhiteSpace(ob.MagazineReloadSoundName))
                MagazineReloadSoundName = ob.MagazineReloadSoundName;
            if (!string.IsNullOrWhiteSpace(ob.ShootSoundName))
                ShootSoundName = ob.ShootSoundName;
            if (!string.IsNullOrWhiteSpace(ob.ShootSoundMid))
                ShootSoundMid = ob.ShootSoundMid;
            if (!string.IsNullOrWhiteSpace(ob.ShootSoundMidFront))
                ShootSoundMidFront = ob.ShootSoundMidFront;
            if (!string.IsNullOrWhiteSpace(ob.ShootSoundFar))
                ShootSoundFar = ob.ShootSoundFar;
            if (!string.IsNullOrWhiteSpace(ob.ShootSoundFarFront))
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

        internal void ResolveWeaponBehavior()
        {
            if (_weaponBehaviorResolved || !_weaponBehaviorId.HasValue)
                return;

            var behavior = LoadWeaponBehavior(_weaponBehaviorId);
            if (behavior == null)
                return;

            InitFromWeaponBehavior(behavior);
            _weaponBehaviorResolved = true;
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
            MagazineCount = Math.Max(1, ob.MagazineCount);
            MagazineReloadMilliseconds = Math.Max(0, ob.MagazineReloadMilliseconds);
            ReloadSoundName = ob.ReloadSoundName;
            MagazineReloadSoundName = ob.MagazineReloadSoundName;
            ShootSoundName = ob.ShootSoundName;
            ShootSoundMid = ob.ShootSoundMid;
            ShootSoundMidFront = ob.ShootSoundMidFront;
            ShootSoundFar = ob.ShootSoundFar;
            ShootSoundFarFront = ob.ShootSoundFarFront;
            ConsumeAmmo = false;
            InternallyLoaded = false;
            AcceptedCartridges = EmptyStrings;
            AcceptedMagazines = EmptyStrings;
            ShootEffectName = null;
            ShootEffectScale = 1f;
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

        private void InitFromWeaponBehavior(MyPAX_HandheldGunDefinition behavior)
        {
            if (behavior == null)
                return;

            ConsumeAmmo = behavior.ConsumeAmmo;
            InternallyLoaded = behavior.InternallyLoaded;
            AcceptedCartridges = behavior.AcceptedCartridges ?? EmptyStrings;
            AcceptedMagazines = behavior.AcceptedMagazines ?? EmptyStrings;
            ShootEffectName = string.IsNullOrWhiteSpace(behavior.ShootEffect) ? null : behavior.ShootEffect;
            ShootEffectScale = behavior.ShootEffectScale > 0 ? behavior.ShootEffectScale : 1f;

            if (behavior.TimeBetweenShots > 0)
                FireCooldownMilliseconds = (int)behavior.TimeBetweenShots;
            if (behavior.ReloadTime > 0)
                MagazineReloadMilliseconds = (int)behavior.ReloadTime;
            if (behavior.ClipSize > 0)
                MagazineCount = behavior.ClipSize;
            if (!string.IsNullOrWhiteSpace(behavior.GunCycleSound))
                ReloadSoundName = behavior.GunCycleSound;
            if (!string.IsNullOrWhiteSpace(behavior.LauncherReloadSoundName))
                MagazineReloadSoundName = behavior.LauncherReloadSoundName;
            if (!string.IsNullOrWhiteSpace(behavior.ShootSoundMid))
                ShootSoundMid = behavior.ShootSoundMid;
            if (!string.IsNullOrWhiteSpace(behavior.ShootSoundMidFront))
                ShootSoundMidFront = behavior.ShootSoundMidFront;
            if (!string.IsNullOrWhiteSpace(behavior.ShootSoundFar))
                ShootSoundFar = behavior.ShootSoundFar;
            if (!string.IsNullOrWhiteSpace(behavior.ShootSoundFarFront))
                ShootSoundFarFront = behavior.ShootSoundFarFront;
            if (string.IsNullOrWhiteSpace(ShootSoundName) && !string.IsNullOrWhiteSpace(ShootSoundMid))
                ShootSoundName = ShootSoundMid;
            if (behavior.MaxSyncedCreationDistance > 0)
                ProjectileSyncDistance = behavior.MaxSyncedCreationDistance;
            if (behavior.VelocityMultiplier > 0)
                ProjectileVelocityMultiplier = behavior.VelocityMultiplier;
            if (behavior.AccuracyMultiplier > 0)
                ProjectileAccuracyMultiplier = behavior.AccuracyMultiplier;
            if (behavior.CharacterDamageMultiplier > 0)
                CharacterDamageMultiplier = behavior.CharacterDamageMultiplier;
            if (AcceptedCartridges.Length > 0)
                Projectile = AcceptedCartridges[0];
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

        private static MyPAX_HandheldGunDefinition LoadWeaponBehavior(SerializableDefinitionId? weaponBehaviorId)
        {
            if (!weaponBehaviorId.HasValue)
                return null;

            MyPAX_HandheldGunDefinition behavior;
            if (MyDefinitionManager.TryGet(weaponBehaviorId.Value, out behavior))
                return behavior;

            var subtype = weaponBehaviorId.Value.SubtypeId;
            if (string.IsNullOrWhiteSpace(subtype))
                return null;

            foreach (var candidate in MyDefinitionManager.GetOfType<MyPAX_HandheldGunDefinition>())
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
        private const long FireDenyLogCooldownMilliseconds = 1000;
        private static readonly Random ShotSpreadRandom = new Random();
        private static readonly object ShotSpreadRandomLock = new object();

        private readonly List<PendingShotSound> _pendingShotSounds = new List<PendingShotSound>();
        private readonly List<PendingWeaponSound> _pendingWeaponSounds = new List<PendingWeaponSound>();
        private readonly SiGameLog _log = new SiGameLog(nameof(SiNpcRangedWeaponComponent), "[SiShoot]");

        private SiNpcRangedWeaponComponentDefinition _definition;
        private SiNpcRangedWeaponComponentDefinition _runtimeDefinition;
        private long _fireCooldown;
        private long _lastFireDenyLogTime = -1;
        private int _shotsRemainingInMagazine;
        private int _shotsRemainingInBurst;

        public override bool IsSerialized => false;
        public SiNpcRangedWeaponComponentDefinition Definition => _runtimeDefinition ?? _definition;

        public bool IsOperational
        {
            get
            {
                Definition.ResolveBalance();
                return !string.IsNullOrWhiteSpace(Definition.Projectile)
                       && Definition.ProjectileVelocityMultiplier > 0
                       && Definition.ProjectileAccuracyMultiplier > 0
                       && Definition.ProjectileSyncDistance > 0
                       && SiPaxProjectileSpawner.IsAvailable
                       && ProjectileDefinitionExists(Definition.Projectile);
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

            runtimeDefinition.ResolveBalance();
            runtimeDefinition.ResolveWeaponBehavior();
            _runtimeDefinition = runtimeDefinition;
            ResetState();
            if (Entity != null && Entity.InScene && (MyAPIGateway.Multiplayer == null || MyAPIGateway.Multiplayer.IsServer))
                AddScheduledCallback(EnsureHeldWeaponEquipped, 1);
            return true;
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();

            if (MyAPIGateway.Multiplayer != null && !MyAPIGateway.Multiplayer.IsServer)
                return;

            AddScheduledCallback(EnsureHeldWeaponEquipped, 1);
        }

        internal void ResetState()
        {
            _fireCooldown = 0;
            _shotsRemainingInMagazine = 0;
            _shotsRemainingInBurst = Math.Max(1, Definition?.BurstCount ?? 1);
            _pendingShotSounds.Clear();
            _pendingWeaponSounds.Clear();
            TryReloadMagazineFromInventory();
        }

        internal void Advance(long elapsedMilliseconds)
        {
            if (elapsedMilliseconds <= 0)
                return;

            _fireCooldown = Math.Max(0, _fireCooldown - elapsedMilliseconds);
        }

        internal bool TryFire(
            SiUtilityContext context,
            MyEntity targetEntity,
            Vector3D targetVelocity,
            float detectionScore,
            float detectionAccuracyWorseningMultiplier)
        {
            if (!IsOperational)
            {
                LogFireDeniedWithCooldown("weapon-not-operational", context, targetEntity, detectionScore, detectionAccuracyWorseningMultiplier);
                return false;
            }

            if (_fireCooldown > 0)
            {
                LogFireDeniedWithCooldown("weapon-cooldown", context, targetEntity, detectionScore, detectionAccuracyWorseningMultiplier);
                return false;
            }

            if (!EnsureLoadedAmmo())
            {
                LogFireDeniedWithCooldown("out-of-ammo", context, targetEntity, detectionScore, detectionAccuracyWorseningMultiplier);
                return false;
            }

            if (context?.Entity == null || targetEntity == null)
            {
                LogFireDeniedWithCooldown("missing-entity-context", context, targetEntity, detectionScore, detectionAccuracyWorseningMultiplier);
                return false;
            }

            if (!TryCreateShot(context.Entity, targetEntity, targetVelocity, out var projectileMatrix))
            {
                LogFireDeniedWithCooldown("shot-creation-failed", context, targetEntity, detectionScore, detectionAccuracyWorseningMultiplier);
                return false;
            }

            var projectileAccuracyMultiplier = ComputeProjectileAccuracyMultiplier(
                detectionScore,
                detectionAccuracyWorseningMultiplier);

            if (!SiPaxProjectileSpawner.TryCreateSyncedProjectile(
                    Definition.Projectile,
                    projectileMatrix,
                    Definition.ProjectileVelocityMultiplier,
                    projectileAccuracyMultiplier,
                    Vector3.Zero,
                    Definition.ProjectileSyncDistance,
                    Definition.CharacterDamageMultiplier,
                    context.EntityId))
            {
                LogFireDeniedWithCooldown("projectile-spawn-failed", context, targetEntity, detectionScore, detectionAccuracyWorseningMultiplier);
                return false;
            }

            var shotFeedback = ConsumeShot();
            _fireCooldown = shotFeedback.CooldownMilliseconds;
            SiNpcSessionComponent.Instance?.ReportNpcFiredShot(context.EntityId);
            SiNpcSessionComponent.Instance?.Spotting?.ReportShot(context.EntityId, context.Entity);
            PlayShotFeedback(
                context.EntityId,
                projectileMatrix,
                shotFeedback.PlayReloadSound,
                shotFeedback.PlayMagazineReloadSound);
            return true;
        }

        private void LogFireDeniedWithCooldown(
            string outcome,
            SiUtilityContext context,
            MyEntity targetEntity,
            float detectionScore,
            float detectionAccuracyWorseningMultiplier)
        {
            var now = CurrentTimeMilliseconds();
            if (_lastFireDenyLogTime >= 0 && now - _lastFireDenyLogTime < FireDenyLogCooldownMilliseconds)
                return;

            _lastFireDenyLogTime = now;
            _log.Warning($"entityId={Entity?.EntityId ?? 0} name={Entity?.Name ?? "null"} definition={DefinitionId.SubtypeName} debug fire-denied outcome={outcome} targetId={targetEntity?.EntityId ?? 0} targetName={targetEntity?.Name ?? "null"} cooldownMs={_fireCooldown} magazineRemaining={_shotsRemainingInMagazine} burstRemaining={_shotsRemainingInBurst} projectile={_definition?.Projectile ?? "null"} projectileVelocityMultiplier={_definition?.ProjectileVelocityMultiplier ?? 0:0.000} projectileAccuracyMultiplier={_definition?.ProjectileAccuracyMultiplier ?? 0:0.000} projectileSyncDistance={_definition?.ProjectileSyncDistance ?? 0:0.000} detectionScore={detectionScore:0.000} detectionAccuracyWorseningMultiplier={detectionAccuracyWorseningMultiplier:0.000} contextEntityId={context?.EntityId ?? 0}"); // AGENT-DEBUG-LOG
        }

        private float ComputeProjectileAccuracyMultiplier(
            float detectionScore,
            float detectionAccuracyWorseningMultiplier)
        {
            var clampedScore = MathHelper.Clamp(detectionScore, 0, 1);
            var worseningMultiplier = Math.Max(1, detectionAccuracyWorseningMultiplier);
            var blendedMultiplier = MathHelper.Lerp(worseningMultiplier, 1f, clampedScore);
            return Definition.ProjectileAccuracyMultiplier * blendedMultiplier;
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

            var initialMuzzle = shooterWorld.Translation + shooterUp * Definition.AimTargetHeight;
            var aimPoint = targetWorld.Translation + targetUp * Definition.AimTargetHeight;
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
            shotDirection = ApplySpread(shotDirection, shooterUp);
            var muzzlePosition = shooterWorld.Translation
                                 + shotDirection * Definition.MuzzleForwardOffset
                                 + shooterUp * Definition.MuzzleUpOffset;
            var shotUp = RejectOrFallback(
                shooterUp,
                shotDirection,
                Vector3D.CalculatePerpendicularVector(shotDirection));
            projectileMatrix = MatrixD.CreateWorld(muzzlePosition, shotDirection, shotUp);
            return true;
        }

        private Vector3D ApplySpread(in Vector3D shotDirection, in Vector3D fallbackUp)
        {
            var spreadRadians = MathHelper.ToRadians(Definition.ShootingSpreadDegrees);
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
            if (_shotsRemainingInBurst <= 0)
                _shotsRemainingInBurst = Math.Max(1, Definition.BurstCount);

            _shotsRemainingInMagazine = Math.Max(0, _shotsRemainingInMagazine - 1);
            _shotsRemainingInBurst = Math.Max(0, _shotsRemainingInBurst - 1);

            var magazineEmpty = _shotsRemainingInMagazine <= 0;
            var burstFinished = _shotsRemainingInBurst <= 0;

            if (burstFinished || magazineEmpty)
                _shotsRemainingInBurst = Math.Max(1, Definition.BurstCount);

            if (magazineEmpty)
            {
                var reloaded = TryReloadMagazineFromInventory();
                return new ShotFeedback
                {
                    CooldownMilliseconds = reloaded ? Definition.MagazineReloadMilliseconds : Definition.FireCooldownMilliseconds,
                    PlayMagazineReloadSound = reloaded && !string.IsNullOrWhiteSpace(Definition.MagazineReloadSoundName),
                };
            }

            if (Definition.BurstCount > 1 && !burstFinished)
                return new ShotFeedback { CooldownMilliseconds = Definition.FireCooldownMilliseconds };

            return new ShotFeedback
            {
                CooldownMilliseconds = Definition.BurstCount > 1
                    ? Definition.BurstCooldownMilliseconds
                    : Definition.FireCooldownMilliseconds,
                PlayReloadSound = Definition.BurstCount <= 1
                                  && !string.IsNullOrWhiteSpace(Definition.ReloadSoundName),
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
                    Definition.ReloadSoundName,
                    position,
                    Math.Min(500, Definition.FireCooldownMilliseconds / 3));
            if (playMagazineReloadSound)
                QueueWeaponSound(
                    Definition.MagazineReloadSoundName,
                    position,
                    Math.Min(900, Definition.MagazineReloadMilliseconds / 4));
        }

        private void PlayMuzzleEffect(MatrixD projectileMatrix)
        {
            if (!string.IsNullOrWhiteSpace(Definition.ShootEffectName))
            {
                MyParticleEffect directEffect;
                if (MyParticlesManager.TryCreateParticleEffect(MyStringHash.GetOrCompute(Definition.ShootEffectName), out directEffect, false)
                    && directEffect != null)
                {
                    directEffect.WorldMatrix = projectileMatrix;
                    directEffect.UserScale *= Definition.ShootEffectScale;
                }

                return;
            }

            if (!Definition.ShootEffect.HasValue)
                return;

            MyEffectDefinition effectDefinition;
            try
            {
                effectDefinition = MyDefinitionManager.Get<MyEffectDefinition>(Definition.ShootEffect.Value);
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
                || Definition.ShootSoundSpeedMetersPerSecond <= 0
                || Definition.ShootSoundFalloffMilliseconds <= 0)
                return;

            var camera = MyAPIGateway.Session?.Camera;
            if (camera == null)
                return;

            var position = projectileMatrix.Translation;
            var toCamera = camera.WorldMatrix.Translation - position;
            var distanceSquared = toCamera.LengthSquared();
            var distance = distanceSquared > 0.0001 ? Math.Sqrt(distanceSquared) : 0;
            var delayMilliseconds = (long)(distance * 1000 / Definition.ShootSoundSpeedMetersPerSecond);
            if (Definition.ShootSoundMaxDelayMilliseconds > 0
                && delayMilliseconds >= Definition.ShootSoundMaxDelayMilliseconds)
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

            var distancePower = 1f - pending.DelayMilliseconds / Definition.ShootSoundFalloffMilliseconds;
            if (distancePower > 0)
                distancePower = distancePower * distancePower * distancePower;
            if (distancePower <= 0)
                return;

            if (pending.DelayMilliseconds > Definition.ShootSoundDirectMaximumDelayMilliseconds
                && HasDistanceShootSounds)
            {
                var angleRange = 2 * Definition.ShootSoundFrontAngleBlendRange;
                var angleWeight = MathHelper.Clamp(
                    (pending.FrontAngle
                     - Definition.ShootSoundFrontAngleThreshold
                     + Definition.ShootSoundFrontAngleBlendRange)
                    / angleRange,
                    0,
                    1);
                var distanceWeight = MathHelper.Clamp(
                    (pending.DelayMilliseconds - Definition.ShootSoundDistanceBlendStartMilliseconds)
                    / Definition.ShootSoundDistanceBlendRangeMilliseconds,
                    0,
                    1);

                var closeVolume = MathHelper.Clamp(1f - distanceWeight, 0, 1);
                var farVolume = MathHelper.Clamp(distanceWeight, 0, 1);
                var frontVolume = MathHelper.Clamp(angleWeight, 0, 1);
                var backVolume = MathHelper.Clamp(1f - angleWeight, 0, 1);

                PlayWorldSound(Definition.ShootSoundMidFront, pending.Position, distancePower * closeVolume * frontVolume);
                PlayWorldSound(Definition.ShootSoundMid, pending.Position, distancePower * closeVolume * backVolume);
                PlayWorldSound(Definition.ShootSoundFarFront, pending.Position, distancePower * farVolume * frontVolume);
                PlayWorldSound(Definition.ShootSoundFar, pending.Position, distancePower * farVolume * backVolume);
                return;
            }

            PlayWorldSound(Definition.ShootSoundName, pending.Position, distancePower);
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
            !string.IsNullOrEmpty(Definition.ShootSoundName)
            || HasDistanceShootSounds;

        private bool HasDistanceShootSounds =>
            !string.IsNullOrEmpty(Definition.ShootSoundMid)
            && !string.IsNullOrEmpty(Definition.ShootSoundMidFront)
            && !string.IsNullOrEmpty(Definition.ShootSoundFar)
            && !string.IsNullOrEmpty(Definition.ShootSoundFarFront)
            && Definition.ShootSoundFrontAngleBlendRange > 0
            && Definition.ShootSoundDistanceBlendRangeMilliseconds > 0;

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
            if (Definition == null || Definition.ShootSoundSpeedMetersPerSecond <= 0)
                return 0;

            var camera = MyAPIGateway.Session?.Camera;
            if (camera == null)
                return 0;

            var distanceSquared = Vector3D.DistanceSquared(position, camera.WorldMatrix.Translation);
            var distance = distanceSquared > 0.0001 ? Math.Sqrt(distanceSquared) : 0;
            return (long)(distance * 1000 / Definition.ShootSoundSpeedMetersPerSecond);
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

        [Update(false)]
        private void EnsureHeldWeaponEquipped(long delta)
        {
            if (Entity == null || Entity.Closed || Entity.MarkedForClose || !Definition.HeldItem.HasValue)
                return;

            if (!TryGetInventory(out var inventory))
                return;

            var heldItemId = (MyDefinitionId)Definition.HeldItem.Value;
            var equipment = Entity.Components.Get<Sandbox.Entities.Components.MyEntityEquipmentComponent>();
            if (equipment != null && equipment.IsEquipped(heldItemId) && inventory.FindItem(heldItemId) != null)
                return;

            string failure;
            SiNpcEquipmentHelper.TryEnsureEquipmentItemEquipped(Entity, heldItemId, out failure, 2);
        }

        private bool EnsureLoadedAmmo()
        {
            if (_shotsRemainingInMagazine > 0)
                return true;

            return TryReloadMagazineFromInventory();
        }

        private bool TryReloadMagazineFromInventory()
        {
            if (_definition == null)
                return false;

            if (!Definition.ConsumeAmmo)
            {
                _shotsRemainingInMagazine = Math.Max(1, Definition.MagazineCount);
                return true;
            }

            if (!TryGetInventory(out var inventory))
                return false;

            if (Definition.InternallyLoaded || Definition.AcceptedMagazines.Length == 0)
                return TryLoadLooseCartridges(inventory);

            return TryLoadMagazineItem(inventory);
        }

        private bool TryLoadLooseCartridges(MyInventoryBase inventory)
        {
            var clipSize = Math.Max(1, Definition.MagazineCount);
            var available = 0;
            for (var i = 0; i < Definition.AcceptedCartridges.Length; i++)
            {
                var id = new MyDefinitionId(typeof(MyObjectBuilder_InventoryItem), Definition.AcceptedCartridges[i]);
                available += Math.Max(0, inventory.GetItemAmount(id));
                if (available >= clipSize)
                    break;
            }

            if (available <= 0)
                return false;

            var toLoad = Math.Min(clipSize, available);
            for (var i = 0; i < Definition.AcceptedCartridges.Length && toLoad > 0; i++)
            {
                var id = new MyDefinitionId(typeof(MyObjectBuilder_InventoryItem), Definition.AcceptedCartridges[i]);
                var count = Math.Max(0, inventory.GetItemAmount(id));
                if (count <= 0)
                    continue;

                var take = Math.Min(toLoad, count);
                if (!inventory.RemoveItems(id, take))
                    continue;

                toLoad -= take;
            }

            var loaded = Math.Min(clipSize, available);
            _shotsRemainingInMagazine = loaded - toLoad;
            return _shotsRemainingInMagazine > 0;
        }

        private bool TryLoadMagazineItem(MyInventoryBase inventory)
        {
            for (var i = 0; i < Definition.AcceptedMagazines.Length; i++)
            {
                var subtype = Definition.AcceptedMagazines[i];
                var id = new MyDefinitionId(typeof(Sandbox.Game.EntityComponents.MyObjectBuilder_MagazineItem), subtype);
                if (!inventory.RemoveItems(id, 1))
                    continue;

                _shotsRemainingInMagazine = Math.Max(1, Definition.MagazineCount);
                return true;
            }

            return false;
        }

        private bool TryGetInventory(out MyInventoryBase inventory)
        {
            string ignored;
            inventory = SiNpcEquipmentHelper.FindInventory(Entity, out ignored);
            return inventory != null;
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
