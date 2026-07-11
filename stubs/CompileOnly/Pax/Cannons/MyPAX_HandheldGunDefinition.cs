using System.Xml.Serialization;
using Sandbox.Definitions.Equipment;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.Inventory;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRageMath;

namespace Pax.Cannons
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_PAX_HandheldGunDefinition : MyObjectBuilder_DefinitionBase
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_PAX_HandheldGunDefinition))]
    public class MyPAX_HandheldGunDefinition : MyDefinitionBase
    {
        public bool ConsumeAmmo { get; set; }
        public bool NewMagazineMethod { get; set; }
        public bool InternallyLoaded { get; set; }
        public bool LoadSingleRounds { get; set; }
        public bool AutoUnloadMagazine { get; set; }
        public bool SemiAuto { get; set; }
        public int ClipSize { get; set; }
        public long ReloadTime { get; set; }
        public long TimeBetweenShots { get; set; }
        public float VelocityMultiplier { get; set; }
        public float AccuracyMultiplier { get; set; }
        public float CharacterDamageMultiplier { get; set; }
        public string[] AcceptedCartridges { get; set; }
        public string[] AcceptedMagazines { get; set; }
        public string ShootSoundMid { get; set; }
        public string ShootSoundMidFront { get; set; }
        public string ShootSoundFar { get; set; }
        public string ShootSoundFarFront { get; set; }
        public string GunCycleSound { get; set; }
        public string LauncherReloadSoundName { get; set; }
        public long LauncherReloadTime { get; set; }
        public string ShootEffect { get; set; }
        public float ShootEffectScale { get; set; }
        public float FrontOfBarrelOffset { get; set; }
        public Vector2 ProjectilePositionOffset { get; set; }
        public Vector3 ShootEffectPositionOffset { get; set; }
        public float MaxSyncedCreationDistance { get; set; }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_PAX_CustomProjectileDefinition : MyObjectBuilder_EntityComponentDefinition
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_PAX_CustomProjectileDefinition))]
    public class MyPAX_CustomProjectileDefinition : MyEntityComponentDefinition
    {
        public float Mass { get; set; }
        public float DragMultiplier { get; set; }
        public float ProjectilePower { get; set; }
        public float ExplosivePower { get; set; }
        public string BombEntity { get; set; }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_PAX_MortarBombDefinition : MyObjectBuilder_EntityComponentDefinition
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_PAX_MortarBombDefinition))]
    public class MyPAX_MortarBombDefinition : MyEntityComponentDefinition
    {
        public float ExplosivePower { get; set; }
    }

    public class MyPAX_HandheldGun : MyHandItemBehaviorBase
    {
        public override float TargetingDistance => -1;

        public override bool SetSecondary(MyHandItem secondaryItem, MyHandItemBehaviorDefinition secondaryDefinition)
        {
            return false;
        }

        public override bool SetTarget()
        {
            return true;
        }

        public override StartActionResponse StartAction(MyHandItemActionEnum action)
        {
            return StartActionResponse.Handled;
        }

        public override void EndAction(MyHandItemActionEnum action)
        {
        }

        public static void ServerGunShootEvent(long holderId, Quaternion direction)
        {
        }

        public static void RequestTertiary(long holderId, bool topUp)
        {
        }
    }
}
