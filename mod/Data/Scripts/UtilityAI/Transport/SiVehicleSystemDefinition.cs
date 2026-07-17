using System;
using System.Xml.Serialization;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;

namespace Si.UtilityAI
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiVehicleSystemDefinition : MyObjectBuilder_DefinitionBase
    {
        [XmlElement]
        public float PaxHorseCatchUpThrottle;

        [XmlElement]
        public float PaxHorseThrottleHysteresisRadius;

        [XmlElement]
        public float PaxHorseCheckpointForwardOffset;

        [XmlElement]
        public bool PaxHorseCanShoot;

        [XmlElement]
        public float MountedFormationWidthMultiplier;

        [XmlElement]
        public float MountedFormationDepthMultiplier;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiVehicleSystemDefinition))]
    public class SiVehicleSystemDefinition : MyDefinitionBase
    {
        private static readonly MyDefinitionId DefaultDefinitionId =
            new MyDefinitionId(typeof(MyObjectBuilder_SiVehicleSystemDefinition), "SiDefaultVehicleSystem");

        public float PaxHorseCatchUpThrottle { get; private set; }
        public float PaxHorseThrottleHysteresisRadius { get; private set; }
        public float PaxHorseCheckpointForwardOffset { get; private set; }
        public bool PaxHorseCanShoot { get; private set; }
        public float MountedFormationWidthMultiplier { get; private set; }
        public float MountedFormationDepthMultiplier { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiVehicleSystemDefinition)builder;
            PaxHorseCatchUpThrottle = Math.Max(0, ob.PaxHorseCatchUpThrottle);
            PaxHorseThrottleHysteresisRadius = Math.Max(0, ob.PaxHorseThrottleHysteresisRadius);
            PaxHorseCheckpointForwardOffset = Math.Max(0, ob.PaxHorseCheckpointForwardOffset);
            PaxHorseCanShoot = ob.PaxHorseCanShoot;
            MountedFormationWidthMultiplier = Math.Max(0, ob.MountedFormationWidthMultiplier);
            MountedFormationDepthMultiplier = Math.Max(0, ob.MountedFormationDepthMultiplier);
        }

        internal static SiVehicleSystemDefinition Load()
        {
            SiVehicleSystemDefinition definition;
            if (MyDefinitionManager.TryGet(DefaultDefinitionId, out definition))
                return definition;

            foreach (var candidate in MyDefinitionManager.GetOfType<SiVehicleSystemDefinition>())
                return candidate;
            return null;
        }
    }
}
