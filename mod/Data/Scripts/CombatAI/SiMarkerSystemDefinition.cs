using System;
using System.ComponentModel;
using System.Xml.Serialization;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;

namespace Si.UtilityAI
{
    public enum SiSquadMapMarkerVisibility
    {
        AlliedOnly,
        All,
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiMarkerSystemDefinition : MyObjectBuilder_DefinitionBase
    {
        [DefaultValue(2.2)]
        public double MarkerHeight = 2.2;

        [DefaultValue(75.0)]
        public double MarkerMaxDistance = 75.0;

        [DefaultValue(0.65f)]
        public float MarkerTextScale = 0.65f;

        [DefaultValue(SiSquadMapMarkerVisibility.AlliedOnly)]
        public SiSquadMapMarkerVisibility SquadVisibility = SiSquadMapMarkerVisibility.AlliedOnly;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiMarkerSystemDefinition))]
    public class SiMarkerSystemDefinition : MyDefinitionBase
    {
        private static readonly MyDefinitionId DefaultDefinitionId =
            new MyDefinitionId(typeof(MyObjectBuilder_SiMarkerSystemDefinition), "SiDefaultMarkerSystem");

        public double MarkerHeight { get; private set; }
        public double MarkerMaxDistance { get; private set; }
        public float MarkerTextScale { get; private set; }
        public SiSquadMapMarkerVisibility SquadVisibility { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiMarkerSystemDefinition)builder;
            MarkerHeight = Math.Max(0, ob.MarkerHeight);
            MarkerMaxDistance = Math.Max(0, ob.MarkerMaxDistance);
            MarkerTextScale = Math.Max(0, ob.MarkerTextScale);
            SquadVisibility = ob.SquadVisibility;
        }

        internal static SiMarkerSystemDefinition Load()
        {
            SiMarkerSystemDefinition definition;
            if (MyDefinitionManager.TryGet(DefaultDefinitionId, out definition))
                return definition;

            foreach (var candidate in MyDefinitionManager.GetOfType<SiMarkerSystemDefinition>())
                return candidate;
            return null;
        }
    }
}
