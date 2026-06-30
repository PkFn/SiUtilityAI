using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Inventory;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace Si.UtilityAI
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiNpcHeldWeaponVisualComponent : MyObjectBuilder_EntityComponent
    {
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiNpcHeldWeaponVisualComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public SerializableDefinitionId? HeldItem;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiNpcHeldWeaponVisualComponentDefinition))]
    public class SiNpcHeldWeaponVisualComponentDefinition : MyEntityComponentDefinition
    {
        public SerializableDefinitionId? HeldItem { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiNpcHeldWeaponVisualComponentDefinition)builder;
            HeldItem = ob.HeldItem;
        }
    }

    [MyComponent(typeof(MyObjectBuilder_SiNpcHeldWeaponVisualComponent))]
    [MyDefinitionRequired(typeof(SiNpcHeldWeaponVisualComponentDefinition))]
    public class SiNpcHeldWeaponVisualComponent : MyEntityComponent
    {
        private static readonly MyStringHash InternalInventory = MyStringHash.GetOrCompute("Internal");

        private SiNpcHeldWeaponVisualComponentDefinition _definition;

        public override bool IsSerialized => false;

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            _definition = (SiNpcHeldWeaponVisualComponentDefinition)definition;
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();

            if (MyAPIGateway.Multiplayer != null && !MyAPIGateway.Multiplayer.IsServer)
                return;

            AddScheduledCallback(EnsureHeldWeaponVisual, 20);
        }

        [Update(false)]
        private void EnsureHeldWeaponVisual(long delta)
        {
            if (Entity == null || Entity.Closed || Entity.MarkedForClose || !_definition.HeldItem.HasValue)
                return;

            var handItems = Entity.Components.Get<MyCharacterHandItemsComponent>();
            var inventory = Entity.Components.Get<MyInventoryBase>(InternalInventory);
            if (handItems == null || inventory == null)
                return;

            var heldItemId = (MyDefinitionId)_definition.HeldItem.Value;
            if (!MyDefinitionManager.TryGet(heldItemId, out MyInventoryItemDefinition itemDefinition) || itemDefinition == null)
            {
                if (!EquiDefinitions.TryGetItemDefinition(heldItemId.SubtypeName, out itemDefinition) || itemDefinition == null)
                    return;
                heldItemId = itemDefinition.Id;
            }

            var item = inventory.FindItem(heldItemId);
            if (item == null && !inventory.AddItems(heldItemId, 1))
                return;

            item = inventory.FindItem(heldItemId);
            if (item == null)
                return;

            var activeMainHand = handItems.MainHand;
            if (activeMainHand != null && activeMainHand.DefinitionId == heldItemId)
                return;

            var activateHandler = handItems as IMyItemActivateHandler;
            if (activateHandler == null || !activateHandler.CanHandle(item))
                return;

            activateHandler.Activate(Entity, inventory, item, true);
        }
    }
}
