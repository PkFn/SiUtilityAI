using System.Xml.Serialization;
using VRage.Components;
using VRage.Game;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;

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
}
