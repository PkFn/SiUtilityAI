using System;
using System.ComponentModel;
using System.Xml.Serialization;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;

namespace Si.UtilityAI
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiSpottingSystemDefinition : MyObjectBuilder_DefinitionBase
    {
        [DefaultValue(1f)]
        public float Constant = 1f;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiSpottingSystemDefinition))]
    public class SiSpottingSystemDefinition : MyDefinitionBase
    {
        public float Constant { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiSpottingSystemDefinition)builder;
            Constant = Math.Max(0, ob.Constant);
        }
    }
}
