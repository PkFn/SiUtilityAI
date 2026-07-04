using System.Collections.Generic;
using System.Xml.Serialization;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.ObjectBuilders;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;

namespace Pax.Equipment
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_PAX_ItemStorageEquipment : MyObjectBuilder_EntityComponent
    {
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_PAX_ItemStorageEquipmentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_PAX_ItemStorageEquipmentDefinition))]
    public class MyPAX_ItemStorageEquipmentDefinition : MyEntityComponentDefinition
    {
        public Dictionary<MyDefinitionId, int> Items { get; set; }
        public Dictionary<string, string> Helmets { get; set; }
        public string DefaultHelmet { get; set; }
    }
}
