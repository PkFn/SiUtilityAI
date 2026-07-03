using System.Xml.Serialization;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Inventory;

namespace Sandbox.Game.EntityComponents
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_MagazineItem : MyObjectBuilder_DurableItem
    {
    }
}
