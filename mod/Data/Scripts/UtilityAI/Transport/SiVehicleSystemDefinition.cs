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
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiVehicleSystemDefinition))]
    public class SiVehicleSystemDefinition : MyDefinitionBase
    {
        private static readonly MyDefinitionId DefaultDefinitionId =
            new MyDefinitionId(typeof(MyObjectBuilder_SiVehicleSystemDefinition), "SiDefaultVehicleSystem");

        public float PaxHorseCatchUpThrottle { get; private set; }
        public float PaxHorseThrottleHysteresisRadius { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiVehicleSystemDefinition)builder;
            PaxHorseCatchUpThrottle = Math.Max(0, ob.PaxHorseCatchUpThrottle);
            PaxHorseThrottleHysteresisRadius = Math.Max(0, ob.PaxHorseThrottleHysteresisRadius);
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
