using System;
using System.Xml.Serialization;
using Medieval.GUI.ContextMenu;
using Medieval.Entities.UseObject;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.Entity.UseObject;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRage.Session;
using VRage.Utils;

namespace Si.UtilityAI
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiBaseCampComponent : MyObjectBuilder_EntityComponent
    {
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiBaseCampComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public float NearbySquadRadius;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiBaseCampComponentDefinition))]
    public class SiBaseCampComponentDefinition : MyEntityComponentDefinition
    {
        public float NearbySquadRadius { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiBaseCampComponentDefinition)builder;
            NearbySquadRadius = Math.Max(0, ob.NearbySquadRadius);
        }
    }

    [MyComponent(typeof(MyObjectBuilder_SiBaseCampComponent))]
    [MyDefinitionRequired(typeof(SiBaseCampComponentDefinition))]
    public sealed class SiBaseCampComponent : MyEntityComponent, IMyGenericUseObjectInterface
    {
        private static readonly MyStringId InteractionText =
            MyStringId.GetOrCompute("Open base camp");

        private MyUseObjectGeneric _useObject;
        private SiBaseCampComponentDefinition _definition;

        public float NearbySquadRadius => _definition?.NearbySquadRadius ?? 0;

        public UseActionEnum SupportedActions => UseActionEnum.Manipulate;
        public UseActionEnum PrimaryAction => UseActionEnum.Manipulate;
        public UseActionEnum SecondaryAction => UseActionEnum.None;
        public bool ContinuousUsage => false;

        public override bool IsSerialized => false;

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            _definition = definition as SiBaseCampComponentDefinition;
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();

            var useObjects = Entity?.Components.Get<MyUseObjectsComponentBase>();
            _useObject = useObjects?.GetInteractiveObject("Generic") as MyUseObjectGeneric;
            if (_useObject != null)
                _useObject.Interface = this;
        }

        public override void OnRemovedFromScene()
        {
            if (_useObject != null && _useObject.Interface == this)
                _useObject.Interface = null;
            _useObject = null;
            base.OnRemovedFromScene();
        }

        public bool AppliesTo(string dummyName) => dummyName == "Generic";

        public MyActionDescription GetActionInfo(string dummyName, UseActionEnum actionEnum)
        {
            return new MyActionDescription
            {
                Text = InteractionText,
                IsTextControlHint = false,
            };
        }

        public void Use(string dummyName, UseActionEnum actionEnum, MyEntity user)
        {
            if (Entity == null || actionEnum != UseActionEnum.Manipulate || user == null)
                return;

            var localPlayer = MySession.Static?.PlayerEntity;
            if (localPlayer == null || user.EntityId != localPlayer.EntityId)
                return;

            MyContextMenuScreen.OpenMenu(
                Entity,
                "SiBaseCampMenu",
                new SiBaseCampMenuSession(Entity, NearbySquadRadius));
        }
    }
}
