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
        public float PaxHorseSteeringMultiplier;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiVehicleSystemDefinition))]
    public class SiVehicleSystemDefinition : MyDefinitionBase
    {
        private static readonly MyDefinitionId DefaultDefinitionId =
            new MyDefinitionId(typeof(MyObjectBuilder_SiVehicleSystemDefinition), "SiDefaultVehicleSystem");

        public float PaxHorseSteeringMultiplier { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiVehicleSystemDefinition)builder;
            PaxHorseSteeringMultiplier = Math.Max(0, ob.PaxHorseSteeringMultiplier);
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
