using System;
using Pax.Cannons;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders;
using VRage.ObjectBuilders;

namespace Si.UtilityAI
{
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
            if (!_weaponBehaviorResolved && _weaponBehaviorId.HasValue)
            {
                var behavior = LoadWeaponBehavior(_weaponBehaviorId);
                if (behavior != null)
                {
                    InitFromWeaponBehavior(behavior);
                    _weaponBehaviorResolved = true;
                }
            }

            if (_weaponBehaviorResolved)
                ApplyProjectileEstimates();
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
            if (behavior.LauncherReloadTime > 0)
                MagazineReloadMilliseconds = (int)behavior.LauncherReloadTime;
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

        private void ApplyProjectileEstimates()
        {
            var projectileDefinition = LoadProjectileDefinition(Projectile);
            if (projectileDefinition == null)
                return;

            var velocityMultiplier = ProjectileVelocityMultiplier > 0 ? ProjectileVelocityMultiplier : 1f;
            var mass = projectileDefinition.Mass > 0.0001f ? projectileDefinition.Mass : 1f;
            var expectedVelocity = projectileDefinition.ProjectilePower > 0
                ? projectileDefinition.ProjectilePower / mass * velocityMultiplier
                : mass * velocityMultiplier;

            if (ExpectedProjectileVelocity <= 0.011f)
                ExpectedProjectileVelocity = Math.Max(0.01f, expectedVelocity);

            if (ElevationAiming <= 0.011f)
            {
                var drag = Math.Max(0.35f, projectileDefinition.DragMultiplier);
                var compensation = (2f * expectedVelocity * expectedVelocity) / (9.81f * drag);
                ElevationAiming = Math.Max(0.01f, compensation);
            }
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

        private static MyPAX_CustomProjectileDefinition LoadProjectileDefinition(string projectileSubtype)
        {
            if (string.IsNullOrWhiteSpace(projectileSubtype))
                return null;

            MyContainerDefinition projectileContainer;
            if (MyDefinitionManager.TryGet(
                    new MyDefinitionId(typeof(MyObjectBuilder_EntityBase), projectileSubtype),
                    out projectileContainer)
                && projectileContainer?.Components != null)
            {
                foreach (var component in projectileContainer.Components)
                    if (component.Definition is MyPAX_CustomProjectileDefinition projectileDefinition)
                        return projectileDefinition;
            }

            foreach (var candidate in MyDefinitionManager.GetOfType<MyPAX_CustomProjectileDefinition>())
                if (string.Equals(candidate?.Id.SubtypeName, projectileSubtype, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            return null;
        }
    }
}
