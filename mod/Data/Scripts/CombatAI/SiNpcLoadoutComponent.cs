using System.Xml.Serialization;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;

namespace Si.UtilityAI
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiNpcLoadoutComponent : MyObjectBuilder_EntityComponent
    {
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiNpcLoadoutComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public SerializableDefinitionId? Webbing;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiNpcLoadoutComponentDefinition))]
    public class SiNpcLoadoutComponentDefinition : MyEntityComponentDefinition
    {
        public SerializableDefinitionId? Webbing { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiNpcLoadoutComponentDefinition)builder;
            Webbing = ob.Webbing;
        }
    }

    [MyComponent(typeof(MyObjectBuilder_SiNpcLoadoutComponent))]
    [MyDefinitionRequired(typeof(SiNpcLoadoutComponentDefinition))]
    public class SiNpcLoadoutComponent : MyEntityComponent
    {
        private SiNpcLoadoutComponentDefinition _definition;

        public override bool IsSerialized => false;

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            _definition = (SiNpcLoadoutComponentDefinition)definition;
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();

            if (MyAPIGateway.Multiplayer != null && !MyAPIGateway.Multiplayer.IsServer)
                return;

            AddScheduledCallback(EquipLoadout, 1);
        }

        [Update(false)]
        private void EquipLoadout(long delta)
        {
            if (Entity == null || Entity.Closed || Entity.MarkedForClose || !_definition.Webbing.HasValue)
                return;

            var itemId = (MyDefinitionId)_definition.Webbing.Value;
            if (SiNpcEquipmentHelper.HasEquippedSubtype(Entity, itemId.SubtypeName))
                return;

            string failure;
            SiNpcEquipmentHelper.TryEnsureEquipmentItemEquipped(Entity, itemId, out failure);
        }
    }
}
