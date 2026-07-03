using System;
using Medieval.Constants;
using Sandbox.Entities.Components;
using Sandbox.Game.Inventory;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders;
using VRage.Inventory;
using VRage.Utils;

namespace Si.UtilityAI
{
    internal static class SiNpcEquipmentHelper
    {
        private static readonly MyStringHash InternalInventory = MyStringHash.GetOrCompute("Internal");
        private static readonly MyStringHash[] InventoryNames =
        {
            MyCharacterConstants.MainInventory,
            InternalInventory,
        };

        public static bool TryGetEquipmentContext(
            MyEntity entity,
            out MyEntityEquipmentComponent equipment,
            out MyInventoryBase inventory,
            out string inventorySource)
        {
            equipment = entity?.Components.Get<MyEntityEquipmentComponent>();
            inventory = FindInventory(entity, out inventorySource);
            return equipment != null && inventory != null;
        }

        public static MyInventoryBase FindInventory(MyEntity entity, out string source)
        {
            if (entity?.Components == null)
            {
                source = "none";
                return null;
            }

            for (var i = 0; i < InventoryNames.Length; i++)
            {
                var inventoryName = InventoryNames[i];
                var inventory = entity.Components.Get<MyInventoryBase>(inventoryName);
                if (inventory != null)
                {
                    source = inventoryName.String;
                    return inventory;
                }
            }

            source = "none";
            return null;
        }

        public static bool TryEnsureItemInInventory(MyInventoryBase inventory, MyDefinitionId itemId, out MyInventoryItem item)
        {
            item = inventory?.FindItem(itemId);
            if (item != null)
                return true;

            if (inventory == null || !inventory.AddItems(itemId, 1, MyInventoryBase.NewItemParams.ForcedInsertion))
            {
                item = null;
                return false;
            }

            item = inventory.FindItem(itemId);
            return item != null;
        }

        public static bool TryEnsureEquipmentItemEquipped(
            MyEntity entity,
            MyDefinitionId itemId,
            out string failure,
            int? slotIndex = null)
        {
            failure = null;

            MyEntityEquipmentComponent equipment;
            MyInventoryBase inventory;
            string inventorySource;
            if (!TryGetEquipmentContext(entity, out equipment, out inventory, out inventorySource))
            {
                failure = $"Missing components; equipment={equipment != null}, inventory={inventory != null}, inventoryLookup={inventorySource}.";
                return false;
            }

            MyInventoryItem item;
            if (!TryEnsureItemInInventory(inventory, itemId, out item))
            {
                failure = $"Failed to add or find {itemId.SubtypeName} in inventory '{inventorySource}'.";
                return false;
            }

            var equipmentItem = item as MyEquipmentItem;
            if (equipmentItem == null)
            {
                failure = $"Inventory item {itemId.SubtypeName} is not a MyEquipmentItem. Runtime type={item.GetType().FullName}.";
                return false;
            }

            if (slotIndex.HasValue)
            {
                if (equipment.EquipItem(equipmentItem, slotIndex.Value))
                    return true;
            }
            else if (equipment.EquipItem(equipmentItem))
            {
                return true;
            }

            var activateHandler = equipment as IMyItemActivateHandler;
            if (activateHandler != null && activateHandler.CanHandle(item) && activateHandler.Activate(entity, inventory, item, true))
                return true;

            failure = slotIndex.HasValue
                ? $"Failed to equip {itemId.SubtypeName} into slot {slotIndex.Value}."
                : $"Failed to equip {itemId.SubtypeName}.";
            return false;
        }

        public static bool HasEquippedSubtype(MyEntity entity, string equipmentSubtype)
        {
            var equipment = entity?.Components.Get<MyEntityEquipmentComponent>();
            if (equipment == null || string.IsNullOrWhiteSpace(equipmentSubtype))
                return false;

            foreach (var equippedItem in equipment.EquippedItems)
            {
                var item = equippedItem as MyEquipmentItem;
                if (item != null && string.Equals(item.Subtype.String, equipmentSubtype, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
