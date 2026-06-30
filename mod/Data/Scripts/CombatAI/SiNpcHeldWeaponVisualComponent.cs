using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using Medieval.Constants;
using Sandbox.Entities.Components;
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
        private const int MainHandSlotIndex = 2;
        private const int RetryDelayFrames = 16;
        private const int MaxAttempts = 20;

        private SiNpcHeldWeaponVisualComponentDefinition _definition;
        private int _attempts;

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

            _attempts = 0;
            AddScheduledCallback(EnsureHeldWeaponVisual, RetryDelayFrames);
        }

        [Update(false)]
        private void EnsureHeldWeaponVisual(long delta)
        {
            if (Entity == null || Entity.Closed || Entity.MarkedForClose || !_definition.HeldItem.HasValue)
                return;

            var equipment = Entity.Components.Get<MyEntityEquipmentComponent>();
            var inventory = Entity.Components.Get<MyInventoryBase>(MyCharacterConstants.MainInventory);
            if (equipment == null || inventory == null)
            {
                Retry();
                return;
            }

            var heldItemId = (MyDefinitionId)_definition.HeldItem.Value;
            if (!MyDefinitionManager.TryGet(heldItemId, out MyInventoryItemDefinition itemDefinition) || itemDefinition == null)
            {
                if (!EquiDefinitions.TryGetItemDefinition(heldItemId.SubtypeName, out itemDefinition) || itemDefinition == null)
                    return;
                heldItemId = itemDefinition.Id;
            }

            var item = inventory.FindItem(heldItemId);
            if (item == null && !inventory.AddItems(heldItemId, 1, MyInventoryBase.NewItemParams.ForcedInsertion))
                return;

            item = inventory.FindItem(heldItemId);
            if (item == null)
            {
                Retry();
                return;
            }

            if (equipment.IsEquipped(heldItemId))
                return;

            var equipmentItem = item as Sandbox.Game.Inventory.MyEquipmentItem;
            if (equipmentItem == null)
            {
                Retry();
                return;
            }

            if (!equipment.EquipItem(equipmentItem, MainHandSlotIndex))
            {
                var activateHandler = equipment as IMyItemActivateHandler;
                if (activateHandler == null || !activateHandler.CanHandle(item) || !activateHandler.Activate(Entity, inventory, item, true))
                    Retry();
            }
        }

        private void Retry()
        {
            if (++_attempts < MaxAttempts)
                AddScheduledCallback(EnsureHeldWeaponVisual, RetryDelayFrames);
        }
    }
}
