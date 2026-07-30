using System.Xml.Serialization;
using Medieval.GUI.ContextMenu;
using Medieval.GUI.ContextMenu.Attributes;
using VRage.ObjectBuilders;

namespace Si.K9
{
    [MyContextMenuContextType(typeof(MyObjectBuilder_SiK9CommandMenuContext))]
    public sealed class SiK9CommandMenuContext : MyContextMenuContext
    {
        private SiK9CommandMenuSessionComponent _session;

        public override void Init(object[] contextParams)
        {
            _session = contextParams != null && contextParams.Length > 0
                ? contextParams[0] as SiK9CommandMenuSessionComponent
                : null;
        }

        public void CommandMotionStop()
        {
            _session?.CommandMotionStop();
        }

        public void CommandMotionFollow()
        {
            _session?.CommandMotionFollow();
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiK9CommandMenuContext : MyObjectBuilder_Base
    {
    }
}
