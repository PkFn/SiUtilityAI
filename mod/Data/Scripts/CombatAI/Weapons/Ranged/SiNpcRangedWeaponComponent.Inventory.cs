using System;
using Sandbox.Game.Inventory;
using VRage.Game;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Inventory;

namespace Si.UtilityAI
{
    public partial class SiNpcRangedWeaponComponent
    {
        private bool HasInventory()
        {
            string ignored;
            var inventory = SiNpcEquipmentHelper.FindInventory(Entity, out ignored);
            return inventory != null;
        }

        private int GetLoadedRoundsFromEquippedItem()
        {
            if (!HeldItemId.HasValue)
                return 0;

            string ignored;
            var inventory = SiNpcEquipmentHelper.FindInventory(Entity, out ignored);
            if (inventory == null)
                return 0;

            var heldItemId = HeldItemId.Value;
            var durable = inventory.FindItem(heldItemId) as MyDurableItem;
            return durable != null ? Math.Max(0, durable.Durability) : 0;
        }

        private bool HasCompatibleLoadedMagazine()
        {
            string ignored;
            var inventory = SiNpcEquipmentHelper.FindInventory(Entity, out ignored);
            if (inventory == null || Definition.AcceptedMagazines == null)
                return false;

            foreach (var item in inventory.Items)
            {
                if (item == null)
                    continue;

                var durable = item as MyDurableItem;
                if (durable == null || durable.Durability <= 0)
                    continue;

                if (IsCompatibleMagazineSubtype(item.Subtype.String))
                    return true;
            }

            return false;
        }

        private bool HasCompatibleMagazineShell()
        {
            string ignored;
            var inventory = SiNpcEquipmentHelper.FindInventory(Entity, out ignored);
            if (inventory == null || Definition.AcceptedMagazines == null)
                return false;

            foreach (var item in inventory.Items)
            {
                if (item == null)
                    continue;

                if (IsCompatibleMagazineSubtype(item.Subtype.String))
                    return true;
            }

            return false;
        }

        private bool HasCompatibleLooseAmmo()
        {
            string ignored;
            var inventory = SiNpcEquipmentHelper.FindInventory(Entity, out ignored);
            if (inventory == null || Definition.AcceptedCartridges == null)
                return false;

            for (var i = 0; i < Definition.AcceptedCartridges.Length; i++)
            {
                var ammoId = new MyDefinitionId(typeof(MyObjectBuilder_InventoryItem), Definition.AcceptedCartridges[i]);
                if (inventory.GetItemAmount(ammoId) > 0)
                    return true;
            }

            return false;
        }

        private bool IsCompatibleMagazineSubtype(string subtype)
        {
            if (string.IsNullOrWhiteSpace(subtype) || Definition.AcceptedMagazines == null)
                return false;

            for (var i = 0; i < Definition.AcceptedMagazines.Length; i++)
            {
                if (subtype.StartsWith(Definition.AcceptedMagazines[i], StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}
