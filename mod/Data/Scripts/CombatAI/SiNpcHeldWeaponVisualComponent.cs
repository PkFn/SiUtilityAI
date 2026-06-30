using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using Medieval.Constants;
using Sandbox.Entities.Components;
using Sandbox.Game.Inventory;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Logging;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Inventory;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Session;
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
        private static readonly MyStringHash MainHandSlot = MyStringHash.GetOrCompute("MainHand");
        private static readonly MyStringHash OffHandSlot = MyStringHash.GetOrCompute("OffHand");
        private static readonly MyStringHash GhostHandSlot = MyStringHash.GetOrCompute("GhostHand");
        private static readonly MyStringHash InternalInventory = MyStringHash.GetOrCompute("Internal");

        private const int MainHandSlotIndex = 2;
        private const int RetryDelayFrames = 16;
        private const int MaxAttempts = 20;

        private SiNpcHeldWeaponVisualComponentDefinition _definition;
        private int _attempts;
        private NamedLogger _log;
        private bool _logInitialized;
        private bool _loggedComponentDump;

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
            Log("OnAddedToScene; scheduling initial held-weapon check.");
            AddScheduledCallback(EnsureHeldWeaponVisual, RetryDelayFrames);
        }

        [Update(false)]
        private void EnsureHeldWeaponVisual(long delta)
        {
            if (Entity == null || Entity.Closed || Entity.MarkedForClose || !_definition.HeldItem.HasValue)
                return;

            var equipment = Entity.Components.Get<MyEntityEquipmentComponent>();
            var inventory = FindInventory(out var inventorySource);
            if (equipment == null || inventory == null)
            {
                LogMissingComponents(equipment != null, inventory != null, inventorySource);
                Retry();
                return;
            }

            var heldItemId = (MyDefinitionId)_definition.HeldItem.Value;
            if (!MyDefinitionManager.TryGet(heldItemId, out MyInventoryItemDefinition itemDefinition) || itemDefinition == null)
            {
                if (!EquiDefinitions.TryGetItemDefinition(heldItemId.SubtypeName, out itemDefinition) || itemDefinition == null)
                {
                    Log($"Failed to resolve held item definition {heldItemId.TypeId}/{heldItemId.SubtypeName}.");
                    return;
                }

                heldItemId = itemDefinition.Id;
                Log($"Resolved held item via fuzzy lookup to {heldItemId.TypeId}/{heldItemId.SubtypeName}.");
            }

            var item = inventory.FindItem(heldItemId);
            if (item == null)
            {
                var added = inventory.AddItems(heldItemId, 1, MyInventoryBase.NewItemParams.ForcedInsertion);
                Log($"Item absent in inventory; add attempted for {heldItemId.SubtypeName}, success={added}, itemCount={inventory.ItemCount}.");
                if (!added)
                    return;
            }

            item = inventory.FindItem(heldItemId);
            if (item == null)
            {
                Log($"Inventory still does not contain {heldItemId.SubtypeName} after add attempt.");
                Retry();
                return;
            }

            if (equipment.IsEquipped(heldItemId))
            {
                Log($"Item {heldItemId.SubtypeName} already equipped. Slots: {DescribeSlots(equipment)}");
                return;
            }

            var equipmentItem = item as MyEquipmentItem;
            if (equipmentItem == null)
            {
                Log($"Inventory item {heldItemId.SubtypeName} is not a MyEquipmentItem. Runtime type={item.GetType().FullName}.");
                Retry();
                return;
            }

            var equippedToMainHand = equipment.EquipItem(equipmentItem, MainHandSlotIndex);
            Log($"EquipItem({heldItemId.SubtypeName}, MainHandSlotIndex={MainHandSlotIndex}) => {equippedToMainHand}. Slots after call: {DescribeSlots(equipment)}");
            if (!equippedToMainHand)
            {
                var activateHandler = equipment as IMyItemActivateHandler;
                var canHandle = activateHandler != null && activateHandler.CanHandle(item);
                var activated = canHandle && activateHandler.Activate(Entity, inventory, item, true);
                Log($"Fallback activate path for {heldItemId.SubtypeName}: handler={activateHandler != null}, canHandle={canHandle}, activated={activated}. Slots after fallback: {DescribeSlots(equipment)}");
                if (!activated)
                    Retry();
            }
        }

        private void Retry()
        {
            _attempts++;
            if (_attempts < MaxAttempts)
            {
                Log($"Retrying later; attempt={_attempts}/{MaxAttempts}.");
                AddScheduledCallback(EnsureHeldWeaponVisual, RetryDelayFrames);
            }
            else
            {
                Log($"Giving up after {MaxAttempts} attempts.");
            }
        }

        private string DescribeSlots(MyEntityEquipmentComponent equipment)
        {
            return $"Main={DescribeSlot(equipment, MainHandSlot)}, Off={DescribeSlot(equipment, OffHandSlot)}, Ghost={DescribeSlot(equipment, GhostHandSlot)}";
        }

        private static string DescribeSlot(MyEntityEquipmentComponent equipment, MyStringHash slot)
        {
            var item = equipment.GetItemForSlot(slot);
            return item == null ? "empty" : $"{item.DefinitionId.SubtypeName}@{slot.String}";
        }

        private MyInventoryBase FindInventory(out string source)
        {
            var inventory = Entity.Components.Get<MyInventoryBase>(MyCharacterConstants.MainInventory);
            if (inventory != null)
            {
                source = MyCharacterConstants.MainInventory.String;
                return inventory;
            }

            inventory = Entity.Components.Get<MyInventoryBase>(InternalInventory);
            if (inventory != null)
            {
                source = InternalInventory.String;
                return inventory;
            }

            source = "none";
            return null;
        }

        private void LogMissingComponents(bool hasEquipment, bool hasInventory, string inventorySource)
        {
            var message = $"Missing components; equipment={hasEquipment}, inventory={hasInventory}, inventoryLookup={inventorySource}.";
            if (_loggedComponentDump)
            {
                Log(message);
                return;
            }

            _loggedComponentDump = true;
            var componentSummary = string.Join(", ", Entity.Components.GetComponents<MyEntityComponent>().Select(x => x.GetType().Name));
            Log($"{message} Present components: [{componentSummary}]");
        }

        private void Log(string message)
        {
            var heldSubtype = _definition != null && _definition.HeldItem.HasValue
                ? _definition.HeldItem.Value.SubtypeName
                : "null";
            if (!_logInitialized && MySession.Static?.Log != null)
            {
                _log = new NamedLogger(MySession.Static.Log, nameof(SiNpcHeldWeaponVisualComponent));
                _logInitialized = true;
            }

            if (_logInitialized)
                _log.Warning($"[SiNpcHeldWeaponVisual] entityId={Entity?.EntityId ?? 0} name={Entity?.Name ?? "null"} held={heldSubtype} {message}");
        }
    }
}
