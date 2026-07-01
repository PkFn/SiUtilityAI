using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using Medieval.Constants;
using Sandbox.Entities.Components;
using Sandbox.Game.Inventory;
using Sandbox.ModAPI;
using SiCore.Core.Debug;
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
        private static readonly MyStringHash[] InventoryNames =
        {
            MyCharacterConstants.MainInventory,
            InternalInventory,
        };

        private const int MainHandSlotIndex = 2;
        private const int RetryDelayFrames = 16;
        private const int MaxAttempts = 20;

        private SiNpcHeldWeaponVisualComponentDefinition _definition;
        private int _attempts;
        private readonly SiGameLog _log = new SiGameLog(nameof(SiNpcHeldWeaponVisualComponent), "[SiNpcHeldWeaponVisual]");
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
            AddScheduledCallback(EnsureHeldWeaponVisual, RetryDelayFrames);
        }

        [Update(false)]
        private void EnsureHeldWeaponVisual(long delta)
        {
            if (Entity == null || Entity.Closed || Entity.MarkedForClose || !_definition.HeldItem.HasValue)
                return;

            if (!TryGetEquipmentContext(out var equipment, out var inventory, out var inventorySource))
            {
                Retry();
                return;
            }

            if (!TryResolveHeldItem(out var heldItemId))
                return;

            if (equipment.IsEquipped(heldItemId))
                return;

            if (!TryEnsureItemInInventory(inventory, heldItemId, out var item))
            {
                Retry();
                return;
            }

            var equipmentItem = item as MyEquipmentItem;
            if (equipmentItem == null)
            {
                Log($"Inventory item {heldItemId.SubtypeName} is not a MyEquipmentItem. Runtime type={item.GetType().FullName}.");
                Retry();
                return;
            }

            if (equipment.EquipItem(equipmentItem, MainHandSlotIndex))
                return;

            var activateHandler = equipment as IMyItemActivateHandler;
            var canHandle = activateHandler != null && activateHandler.CanHandle(item);
            var activated = canHandle && activateHandler.Activate(Entity, inventory, item, true);
            if (activated)
                return;

            Log($"Failed to equip {heldItemId.SubtypeName}. handler={activateHandler != null}, canHandle={canHandle}. Slots: {DescribeSlots(equipment)}");
            Retry();
        }

        private bool TryGetEquipmentContext(out MyEntityEquipmentComponent equipment, out MyInventoryBase inventory, out string inventorySource)
        {
            equipment = Entity.Components.Get<MyEntityEquipmentComponent>();
            inventory = FindInventory(out inventorySource);
            if (equipment != null && inventory != null)
                return true;

            LogMissingComponents(equipment != null, inventory != null, inventorySource);
            return false;
        }

        private bool TryResolveHeldItem(out MyDefinitionId heldItemId)
        {
            heldItemId = (MyDefinitionId)_definition.HeldItem.Value;
            if (MyDefinitionManager.TryGet(heldItemId, out MyInventoryItemDefinition itemDefinition) && itemDefinition != null)
                return true;

            if (EquiDefinitions.TryGetItemDefinition(heldItemId.SubtypeName, out itemDefinition) && itemDefinition != null)
            {
                heldItemId = itemDefinition.Id;
                return true;
            }

            Log($"Failed to resolve held item definition {heldItemId.TypeId}/{heldItemId.SubtypeName}.");
            return false;
        }

        private bool TryEnsureItemInInventory(MyInventoryBase inventory, MyDefinitionId heldItemId, out MyInventoryItem item)
        {
            item = inventory.FindItem(heldItemId);
            if (item != null)
                return true;

            if (!inventory.AddItems(heldItemId, 1, MyInventoryBase.NewItemParams.ForcedInsertion))
            {
                Log($"Failed to add {heldItemId.SubtypeName} to inventory.");
                return false;
            }

            item = inventory.FindItem(heldItemId);
            if (item != null)
                return true;

            Log($"Inventory still does not contain {heldItemId.SubtypeName} after add attempt.");
            return false;
        }

        private void Retry()
        {
            _attempts++;
            if (_attempts < MaxAttempts)
            {
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
            foreach (var inventoryName in InventoryNames)
            {
                var inventory = Entity.Components.Get<MyInventoryBase>(inventoryName);
                if (inventory != null)
                {
                    source = inventoryName.String;
                    return inventory;
                }
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
            _log.Warning($"entityId={Entity?.EntityId ?? 0} name={Entity?.Name ?? "null"} held={heldSubtype} {message}");
        }
    }
}
