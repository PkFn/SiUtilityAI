using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Pax.Cannons;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.Inventory;
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
        public bool NewMagazineMethod { get; private set; }
        public bool InternallyLoaded { get; private set; }
        public bool LoadSingleRounds { get; private set; }
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
            NewMagazineMethod = false;
            InternallyLoaded = false;
            LoadSingleRounds = false;
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
            NewMagazineMethod = behavior.NewMagazineMethod;
            InternallyLoaded = behavior.InternallyLoaded;
            LoadSingleRounds = behavior.LoadSingleRounds;
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
        private const long FireIntentGraceMilliseconds = 500;
        private readonly SiGameLog _log = new SiGameLog(nameof(SiNpcRangedWeaponComponent), "[SiShoot]");

        private SiNpcRangedWeaponComponentDefinition _definition;
        private SiNpcRangedWeaponComponentDefinition _runtimeDefinition;
        private long _fireCooldown;
        private long _lastFireDenyLogTime = -1;
        private long _lastFireIntentTime = long.MinValue;
        private bool _scheduledFireQueued;
        private bool _maintenanceQueued;
        private int _estimatedRoundsInMagazine;
        private ReloadMaintenanceState _reloadMaintenanceState;
        private MyEntity _fireIntentTarget;
        private Vector3D _fireIntentTargetVelocity;

        public override bool IsSerialized => false;
        public SiNpcRangedWeaponComponentDefinition Definition => _runtimeDefinition ?? _definition;

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
            _lastFireIntentTime = long.MinValue;
            _estimatedRoundsInMagazine = InitialEstimatedMagazineRounds();
            _reloadMaintenanceState = ReloadMaintenanceState.None;
            _fireIntentTarget = null;
            _fireIntentTargetVelocity = Vector3D.Zero;
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

            if (context?.Entity == null || targetEntity == null)
            {
                LogFireDeniedWithCooldown("missing-entity-context", context, targetEntity, detectionScore, detectionAccuracyWorseningMultiplier);
                return false;
            }

            var heldGun = GetHeldGunBehavior();
            if (heldGun == null)
            {
                LogFireDeniedWithCooldown("held-gun-missing", context, targetEntity, detectionScore, detectionAccuracyWorseningMultiplier);
                return false;
            }

            _fireIntentTarget = targetEntity;
            _fireIntentTargetVelocity = targetVelocity;
            _lastFireIntentTime = CurrentTimeMilliseconds();

            if (_fireCooldown > 0)
                return true;

            if (!TryFireSingleShot(context.Entity, targetEntity, targetVelocity))
            {
                LogFireDeniedWithCooldown("shot-creation-failed", context, targetEntity, detectionScore, detectionAccuracyWorseningMultiplier);
                return false;
            }

            StartScheduledFiring();
            return true;
        }

        internal void ClearFireIntent()
        {
            _lastFireIntentTime = long.MinValue;
            _reloadMaintenanceState = ReloadMaintenanceState.None;
            _fireIntentTarget = null;
            _fireIntentTargetVelocity = Vector3D.Zero;
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
            _log.Warning($"entityId={Entity?.EntityId ?? 0} name={Entity?.Name ?? "null"} definition={DefinitionId.SubtypeName} debug fire-denied outcome={outcome} targetId={targetEntity?.EntityId ?? 0} targetName={targetEntity?.Name ?? "null"} cooldownMs={_fireCooldown} heldItem={Definition?.HeldItem?.SubtypeId ?? "null"} weaponBehavior={Definition?.WeaponBehavior?.SubtypeId ?? "null"} detectionScore={detectionScore:0.000} detectionAccuracyWorseningMultiplier={detectionAccuracyWorseningMultiplier:0.000} contextEntityId={context?.EntityId ?? 0}"); // AGENT-DEBUG-LOG
        }

        private bool TryCreateShotDirection(
            MyEntity shooter,
            MyEntity targetEntity,
            Vector3D targetVelocity,
            out Quaternion direction)
        {
            direction = Quaternion.Identity;
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
            Vector3D targetVelocity)
        {
            Quaternion direction;
            if (!TryCreateShotDirection(shooter, targetEntity, targetVelocity, out direction))
                return false;

            MyPAX_HandheldGun.ServerGunShootEvent(shooter.EntityId, direction);
            _fireCooldown = EffectiveFireIntervalMilliseconds;
            if (Definition.ConsumeAmmo && _estimatedRoundsInMagazine > 0)
                _estimatedRoundsInMagazine = Math.Max(0, _estimatedRoundsInMagazine - 1);
            SiNpcSessionComponent.Instance?.ReportNpcFiredShot(shooter.EntityId);
            SiNpcSessionComponent.Instance?.Spotting?.ReportShot(shooter.EntityId, shooter);
            if (NeedsReloadMaintenanceAfterShot)
                BeginReloadMaintenance();
            return true;
        }

        private void StartScheduledFiring()
        {
            if (_reloadMaintenanceState != ReloadMaintenanceState.None)
                return;
            if (_scheduledFireQueued)
                return;

            _scheduledFireQueued = true;
            var delay = Math.Max(1L, EffectiveFireIntervalMilliseconds);
            AddScheduledCallback(ContinueScheduledFiring, delay);
        }

        [Update(false)]
        private void ContinueScheduledFiring(long _)
        {
            _scheduledFireQueued = false;
            if (Entity == null || Entity.Closed || Entity.MarkedForClose)
                return;
            if (!IsOperational)
                return;
            if (_reloadMaintenanceState != ReloadMaintenanceState.None)
                return;

            var now = CurrentTimeMilliseconds();
            if (now - _lastFireIntentTime > FireIntentGraceMilliseconds)
                return;

            var target = _fireIntentTarget;
            if (target == null || target.Closed || target.MarkedForClose)
                return;

            if (_fireCooldown > 0)
                _fireCooldown = 0;

            if (!TryFireSingleShot(Entity, target, _fireIntentTargetVelocity))
                return;

            StartScheduledFiring();
        }

        private void BeginReloadMaintenance()
        {
            if (!UsesDetachableMagazineMaintenance || Entity == null)
                return;

            _reloadMaintenanceState = ReloadMaintenanceState.RemovingEmptyMagazine;
            _estimatedRoundsInMagazine = 0;
            MyPAX_HandheldGun.RequestTertiary(Entity.EntityId, false);
            _fireCooldown = Math.Max(_fireCooldown, EffectiveReloadIntervalMilliseconds);
            ScheduleReloadMaintenance(EffectiveReloadIntervalMilliseconds);
        }

        private void ScheduleReloadMaintenance(long delay)
        {
            if (_maintenanceQueued)
                return;

            _maintenanceQueued = true;
            AddScheduledCallback(ContinueReloadMaintenance, Math.Max(1L, delay));
        }

        [Update(false)]
        private void ContinueReloadMaintenance(long _)
        {
            _maintenanceQueued = false;
            if (_reloadMaintenanceState == ReloadMaintenanceState.None)
                return;
            if (Entity == null || Entity.Closed || Entity.MarkedForClose)
            {
                _reloadMaintenanceState = ReloadMaintenanceState.None;
                return;
            }

            if (!TryGetInventory(out var inventory))
            {
                _reloadMaintenanceState = ReloadMaintenanceState.None;
                return;
            }

            switch (_reloadMaintenanceState)
            {
                case ReloadMaintenanceState.RemovingEmptyMagazine:
                    if (HasCompatibleLoadedMagazine(inventory))
                    {
                        TriggerMagazineLoad();
                        _reloadMaintenanceState = ReloadMaintenanceState.LoadingMagazine;
                        ScheduleReloadMaintenance(EffectiveReloadIntervalMilliseconds);
                        return;
                    }

                    if (HasCompatibleLooseAmmo(inventory) && HasCompatibleMagazineShell(inventory))
                    {
                        MyPAX_HandheldGun.RequestTertiary(Entity.EntityId, true);
                        _reloadMaintenanceState = ReloadMaintenanceState.FillingMagazines;
                        ScheduleReloadMaintenance(EffectiveReloadIntervalMilliseconds);
                        return;
                    }

                    _reloadMaintenanceState = ReloadMaintenanceState.None;
                    return;

                case ReloadMaintenanceState.FillingMagazines:
                    if (HasCompatibleLoadedMagazine(inventory))
                    {
                        TriggerMagazineLoad();
                        _reloadMaintenanceState = ReloadMaintenanceState.LoadingMagazine;
                        ScheduleReloadMaintenance(EffectiveReloadIntervalMilliseconds);
                        return;
                    }

                    if (HasCompatibleLooseAmmo(inventory) && HasCompatibleMagazineShell(inventory))
                    {
                        ScheduleReloadMaintenance(EffectiveReloadIntervalMilliseconds);
                        return;
                    }

                    _reloadMaintenanceState = ReloadMaintenanceState.None;
                    return;

                case ReloadMaintenanceState.LoadingMagazine:
                    _estimatedRoundsInMagazine = InitialEstimatedMagazineRounds();
                    _reloadMaintenanceState = ReloadMaintenanceState.None;
                    return;
            }
        }

        private void TriggerMagazineLoad()
        {
            MyPAX_HandheldGun.ServerGunShootEvent(Entity.EntityId, Quaternion.Identity);
            _fireCooldown = Math.Max(_fireCooldown, EffectiveReloadIntervalMilliseconds);
        }

        private int EffectiveFireIntervalMilliseconds
        {
            get
            {
                var interval = Definition.FireCooldownMilliseconds;
                if (interval > 0)
                    return interval;
                return 1;
            }
        }

        private int EffectiveReloadIntervalMilliseconds =>
            Math.Max(600, Definition.MagazineReloadMilliseconds > 0 ? Definition.MagazineReloadMilliseconds : 600);

        private bool UsesDetachableMagazineMaintenance =>
            Definition != null
            && Definition.ConsumeAmmo
            && Definition.NewMagazineMethod
            && !Definition.InternallyLoaded
            && Definition.AcceptedMagazines != null
            && Definition.AcceptedMagazines.Length > 0;

        private bool NeedsReloadMaintenanceAfterShot =>
            UsesDetachableMagazineMaintenance
            && _estimatedRoundsInMagazine <= 0;

        private int InitialEstimatedMagazineRounds()
        {
            return Math.Max(0, Definition?.MagazineCount ?? 0);
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

        private bool TryGetInventory(out MyInventoryBase inventory)
        {
            string ignored;
            inventory = SiNpcEquipmentHelper.FindInventory(Entity, out ignored);
            return inventory != null;
        }

        private bool HasCompatibleLoadedMagazine(MyInventoryBase inventory)
        {
            if (inventory == null || Definition.AcceptedMagazines == null)
                return false;

            foreach (var item in inventory.Items)
            {
                if (item == null)
                    continue;

                var durable = item as MyDurableItem;
                if (durable == null || durable.Durability <= 0)
                    continue;

                if (IsCompatibleMagazineSubtype(item.Subtype.String))
                    return true;
            }

            return false;
        }

        private bool HasCompatibleMagazineShell(MyInventoryBase inventory)
        {
            if (inventory == null || Definition.AcceptedMagazines == null)
                return false;

            foreach (var item in inventory.Items)
            {
                if (item == null)
                    continue;

                if (IsCompatibleMagazineSubtype(item.Subtype.String))
                    return true;
            }

            return false;
        }

        private bool HasCompatibleLooseAmmo(MyInventoryBase inventory)
        {
            if (inventory == null || Definition.AcceptedCartridges == null)
                return false;

            for (var i = 0; i < Definition.AcceptedCartridges.Length; i++)
            {
                var ammoId = new MyDefinitionId(typeof(MyObjectBuilder_InventoryItem), Definition.AcceptedCartridges[i]);
                if (inventory.GetItemAmount(ammoId) > 0)
                    return true;
            }

            return false;
        }

        private bool IsCompatibleMagazineSubtype(string subtype)
        {
            if (string.IsNullOrWhiteSpace(subtype) || Definition.AcceptedMagazines == null)
                return false;

            for (var i = 0; i < Definition.AcceptedMagazines.Length; i++)
            {
                if (subtype.StartsWith(Definition.AcceptedMagazines[i], StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private enum ReloadMaintenanceState
        {
            None,
            RemovingEmptyMagazine,
            FillingMagazines,
            LoadingMagazine,
        }

        private MyPAX_HandheldGun GetHeldGunBehavior()
        {
            return Entity?.Components
                .Get<MyCharacterHandItemsComponent>()
                ?.GetBehavior<MyPAX_HandheldGun>();
        }
    }
}
