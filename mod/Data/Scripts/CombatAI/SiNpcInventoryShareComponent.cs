using System.Xml.Serialization;
using Medieval.Entities.UseObject;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Inventory;
using Sandbox.Game.Players;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
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
    public class MyObjectBuilder_SiNpcInventoryShareComponent : MyObjectBuilder_EntityComponent
    {
    }

    [MyComponent(typeof(MyObjectBuilder_SiNpcInventoryShareComponent))]
    public class SiNpcInventoryShareComponent : MyEntityComponent, IMyGenericUseObjectInterface
    {
        private static readonly MyStringId InteractionText =
            MyStringId.GetOrCompute("Open squad inventory");

        private MyUseObjectGeneric _useObject;

        public UseActionEnum SupportedActions => UseActionEnum.Manipulate;
        public UseActionEnum PrimaryAction => UseActionEnum.Manipulate;
        public UseActionEnum SecondaryAction => UseActionEnum.None;
        public bool ContinuousUsage => false;

        public override bool IsSerialized => false;

        public bool AppliesTo(string dummyName) => dummyName == "Generic";

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
            if (Entity == null || actionEnum != UseActionEnum.Manipulate)
                return;

            var localPlayerEntity = MySession.Static?.PlayerEntity;
            var localPlayer = MyAPIGateway.Session?.Player as MyPlayer;
            if (localPlayerEntity == null
                || user == null
                || user.EntityId != localPlayerEntity.EntityId
                || localPlayer?.Identity == null)
                return;

            var session = SiNpcSessionComponent.Instance;
            if (session == null
                || !session.CanPlayerAccessNpcInventory(Entity, localPlayer.Identity.Id))
                return;

            string ignored;
            var inventoryBase = SiNpcEquipmentHelper.FindInventory(Entity, out ignored);
            var inventory = inventoryBase as MyInventory;
            if (inventory == null)
                return;

#if !VRAGE_VERSION_0
            Medieval.GUI.Hud.MyGuiScreenHudMedieval.Static.ShowInventory(inventory);
#else
            Sandbox.Game.Gui.MyGuiScreenHudBase.Static.ShowInventory(inventory);
#endif
        }
    }
}
