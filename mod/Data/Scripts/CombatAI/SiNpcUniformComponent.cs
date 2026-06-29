using System;
using System.Xml.Serialization;
using Pax.Equipment;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Network;
using VRage.ObjectBuilders;

namespace Si.UtilityAI
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiNpcUniformComponent : MyObjectBuilder_EntityComponent
    {
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiNpcUniformComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public SerializableDefinitionId? Uniform;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiNpcUniformComponentDefinition))]
    public class SiNpcUniformComponentDefinition : MyEntityComponentDefinition
    {
        public SerializableDefinitionId? Uniform { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiNpcUniformComponentDefinition)builder;
            Uniform = ob.Uniform;
        }
    }

    [MyComponent(typeof(MyObjectBuilder_SiNpcUniformComponent))]
    [MyDefinitionRequired(typeof(SiNpcUniformComponentDefinition))]
    public class SiNpcUniformComponent : MyEntityComponent
    {
        private SiNpcUniformComponentDefinition _definition;

        public override bool IsSerialized => false;

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            _definition = (SiNpcUniformComponentDefinition)definition;
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();

            if (MyAPIGateway.Multiplayer != null && !MyAPIGateway.Multiplayer.IsServer)
                return;

            AddScheduledCallback(ApplyUniform, 16);
        }

        [Update(false)]
        private void ApplyUniform(long delta)
        {
            if (Entity == null || Entity.Closed || Entity.MarkedForClose || !_definition.Uniform.HasValue)
                return;

            var uniformEquipment = Entity.Components.Get<MyPAX_CharacterUniformEquipment>();
            if (uniformEquipment == null)
                return;

            MyPAX_UniformEquipmentDefinition uniformDefinition;
            if (!MyDefinitionManager.TryGet(_definition.Uniform.Value, out uniformDefinition)
                || uniformDefinition == null
                || string.IsNullOrWhiteSpace(uniformDefinition.Material))
                return;

            if (uniformEquipment.IsEquipped(uniformDefinition.Material))
                return;

            uniformEquipment.EquipMaterial(
                uniformDefinition.Material,
                uniformDefinition.ColorMetal,
                uniformDefinition.NormalGloss,
                uniformDefinition.AddOrAlpha,
                uniformDefinition.OriginalColorMetal,
                uniformDefinition.OriginalNormalGloss,
                uniformDefinition.OriginalAddOrAlpha,
                uniformDefinition.IsAlpha,
                uniformDefinition.RequiredCharacter);
        }
    }
}
