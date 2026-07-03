using System.Xml.Serialization;
using VRage.Game;
using VRage.Game.Definitions;
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
        public bool InternallyLoaded { get; set; }
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
        public string ShootEffect { get; set; }
        public float ShootEffectScale { get; set; }
        public float FrontOfBarrelOffset { get; set; }
        public Vector2 ProjectilePositionOffset { get; set; }
        public Vector3 ShootEffectPositionOffset { get; set; }
        public float MaxSyncedCreationDistance { get; set; }
    }
}
