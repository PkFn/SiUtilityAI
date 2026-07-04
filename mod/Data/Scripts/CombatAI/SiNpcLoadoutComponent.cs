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
        public SerializableDefinitionId? Uniform;
        public SerializableDefinitionId? Parachute;
        public SerializableDefinitionId? ParatrooperVariant;
        public bool IsParatrooper;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiNpcLoadoutComponentDefinition))]
    public class SiNpcLoadoutComponentDefinition : MyEntityComponentDefinition
    {
        public SerializableDefinitionId? Webbing { get; private set; }
        public SerializableDefinitionId? Uniform { get; private set; }
        public SerializableDefinitionId? Parachute { get; private set; }
        public SerializableDefinitionId? ParatrooperVariant { get; private set; }
        public bool IsParatrooper => Parachute.HasValue || _isParatrooper;

        private bool _isParatrooper;

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiNpcLoadoutComponentDefinition)builder;
            Webbing = ob.Webbing;
            Uniform = ob.Uniform;
            Parachute = ob.Parachute;
            ParatrooperVariant = ob.ParatrooperVariant;
            _isParatrooper = ob.IsParatrooper;
        }
    }

    [MyComponent(typeof(MyObjectBuilder_SiNpcLoadoutComponent))]
    [MyDefinitionRequired(typeof(SiNpcLoadoutComponentDefinition))]
    public class SiNpcLoadoutComponent : MyEntityComponent
    {
        private SiNpcLoadoutComponentDefinition _definition;
        private SiNpcLoadoutComponentDefinition _runtimeDefinition;
        private MyDefinitionId? _runtimeWebbingId;

        public override bool IsSerialized => false;
        public string CurrentWebbingSubtype => _runtimeWebbingId?.SubtypeName ?? ActiveDefinition?.Webbing?.SubtypeId;

        private SiNpcLoadoutComponentDefinition ActiveDefinition => _runtimeDefinition ?? _definition;

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

        internal bool ApplyRuntimeDefinition(MyDefinitionId definitionId)
        {
            SiNpcLoadoutComponentDefinition runtimeDefinition;
            if (!MyDefinitionManager.TryGet(definitionId, out runtimeDefinition) || runtimeDefinition == null)
                return false;

            _runtimeDefinition = runtimeDefinition;
            _runtimeWebbingId = runtimeDefinition.Webbing.HasValue
                ? (MyDefinitionId?)runtimeDefinition.Webbing.Value
                : null;
            if (Entity != null && Entity.InScene)
                AddScheduledCallback(EquipLoadout, 1);
            return true;
        }

        internal bool ApplyRuntimeWebbing(MyDefinitionId webbingId)
        {
            _runtimeDefinition = null;
            _runtimeWebbingId = webbingId;
            if (Entity != null && Entity.InScene)
                AddScheduledCallback(EquipLoadout, 1);
            return true;
        }

        [Update(false)]
        private void EquipLoadout(long delta)
        {
            var definition = ActiveDefinition;
            var itemId = _runtimeWebbingId
                         ?? (definition != null && definition.Webbing.HasValue
                             ? (MyDefinitionId?)definition.Webbing.Value
                             : null);
            if (Entity == null || Entity.Closed || Entity.MarkedForClose || !itemId.HasValue)
                return;

            if (SiNpcEquipmentHelper.HasEquippedSubtype(Entity, itemId.Value.SubtypeName))
                return;

            string failure;
            SiNpcEquipmentHelper.TryEnsureEquipmentItemEquipped(Entity, itemId.Value, out failure);
        }
    }
}
