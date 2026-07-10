using System;
using System.Collections.Generic;
using Pax.Equipment;
using Sandbox.Game.Inventory;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Inventory;

namespace Si.UtilityAI
{
    internal readonly struct SiNpcAmmoStatus
    {
        public SiNpcAmmoStatus(int currentUnits, int maxUnits)
        {
            CurrentUnits = Math.Max(0, currentUnits);
            MaxUnits = Math.Max(0, maxUnits);
        }

        public int CurrentUnits { get; }
        public int MaxUnits { get; }
        public bool HasTracking => MaxUnits > 0;
        public bool NeedsRearm => HasTracking && CurrentUnits < MaxUnits;
        public bool IsEmpty => HasTracking && CurrentUnits <= 0;
        public float Ratio => !HasTracking || MaxUnits <= 0
            ? 0f
            : (float)CurrentUnits / MaxUnits;
        public float ClampedRatio => Math.Max(0f, Math.Min(1f, Ratio));
        public bool IsLow => HasTracking
                             && !IsEmpty
                             && ClampedRatio < SiNpcAmmoStatusHelper.LowAmmoThreshold;
        public string MarkerText => $"{Math.Round(ClampedRatio * 100f):0}%";
    }

    internal sealed class SiNpcAmmoProfile
    {
        public readonly HashSet<MyDefinitionId> CartridgeIds = new HashSet<MyDefinitionId>();
        public readonly Dictionary<MyDefinitionId, int> HeldWeaponUnits = new Dictionary<MyDefinitionId, int>();
        public readonly Dictionary<string, int> MagazinePrefixUnits = new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly List<string> MagazinePrefixes = new List<string>();
        public int MaxUnits;

        public bool HasTracking => MaxUnits > 0;

        public bool MatchesTrackedInventoryItem(MyInventoryItem item)
        {
            if (item == null)
                return false;

            return HeldWeaponUnits.ContainsKey(item.DefinitionId)
                   || CartridgeIds.Contains(item.DefinitionId)
                   || MatchesMagazineSubtype(item.Subtype.String);
        }

        public bool MatchesSourceInventoryItem(MyInventoryItem item)
        {
            if (item == null)
                return false;

            return CartridgeIds.Contains(item.DefinitionId)
                   || MatchesMagazineSubtype(item.Subtype.String);
        }

        public bool MatchesMagazineSubtype(string subtype)
        {
            if (string.IsNullOrWhiteSpace(subtype))
                return false;

            for (var i = 0; i < MagazinePrefixes.Count; i++)
                if (subtype.StartsWith(MagazinePrefixes[i], StringComparison.Ordinal))
                    return true;

            return false;
        }
    }

    internal static class SiNpcAmmoStatusHelper
    {
        internal const float LowAmmoThreshold = 0.2f;

        private static readonly Dictionary<string, SiNpcAmmoProfile> ProfileCache =
            new Dictionary<string, SiNpcAmmoProfile>(StringComparer.OrdinalIgnoreCase);

        internal static bool TryGetAmmoStatus(SiNpc npc, out SiNpcAmmoStatus status)
        {
            status = default(SiNpcAmmoStatus);
            if (npc?.Entity == null)
                return false;

            if (!TryGetAmmoProfile(npc, out var profile) || profile == null || !profile.HasTracking)
                return false;

            string ignored;
            var inventory = SiNpcEquipmentHelper.FindInventory(npc.Entity, out ignored);
            if (inventory == null)
                return false;

            status = new SiNpcAmmoStatus(CountTrackedAmmoUnits(profile, inventory), profile.MaxUnits);
            return status.HasTracking;
        }

        internal static bool TryGetAmmoProfile(SiNpc npc, out SiNpcAmmoProfile profile)
        {
            profile = null;
            var webbingSubtype = ResolveWebbingSubtype(npc);
            return !string.IsNullOrWhiteSpace(webbingSubtype)
                   && TryGetAmmoProfile(webbingSubtype, out profile);
        }

        internal static bool TryGetAmmoProfile(string webbingSubtype, out SiNpcAmmoProfile profile)
        {
            profile = null;
            if (string.IsNullOrWhiteSpace(webbingSubtype))
                return false;

            var key = webbingSubtype.Trim();
            if (ProfileCache.TryGetValue(key, out profile))
                return profile != null && profile.HasTracking;

            profile = BuildProfile(key);
            ProfileCache[key] = profile;
            return profile != null && profile.HasTracking;
        }

        internal static bool InventoryHasSourceAmmo(SiNpcAmmoProfile profile, MyInventoryBase inventory) =>
            CountSourceAmmoUnits(profile, inventory) > 0;

        internal static int CountSourceAmmoUnits(SiNpcAmmoProfile profile, MyInventoryBase inventory)
        {
            if (profile == null || inventory == null)
                return 0;

            var total = 0;
            for (var i = 0; i < inventory.Items.Count; i++)
            {
                var item = inventory.Items.ItemAt(i);
                if (item == null || !profile.MatchesSourceInventoryItem(item))
                    continue;

                total += ResolveCurrentItemAmmoUnits(profile, item, includeHeldWeapons: false);
            }

            return Math.Max(0, total);
        }

        internal static bool TryResolveTransferAmount(
            SiNpcAmmoProfile profile,
            MyInventoryItem item,
            int neededUnits,
            out int transferAmount)
        {
            transferAmount = 0;
            if (profile == null || item == null || neededUnits <= 0 || !profile.MatchesSourceInventoryItem(item))
                return false;

            if (profile.CartridgeIds.Contains(item.DefinitionId))
            {
                transferAmount = Math.Max(1, Math.Min(neededUnits, item.Amount));
                return transferAmount > 0;
            }

            if (!profile.MatchesMagazineSubtype(item.Subtype.String))
                return false;

            transferAmount = 1;
            return item.Amount > 0;
        }

        private static string ResolveWebbingSubtype(SiNpc npc)
        {
            if (npc is SiDataDrivenNpc dataDrivenNpc && !string.IsNullOrWhiteSpace(dataDrivenNpc.WebbingSubtype))
                return dataDrivenNpc.WebbingSubtype;

            return npc?.Entity?.Components.Get<SiNpcLoadoutComponent>()?.CurrentWebbingSubtype;
        }

        private static SiNpcAmmoProfile BuildProfile(string webbingSubtype)
        {
            if (!SiNpcTrooperCatalog.TryResolveLoadout(webbingSubtype, false, out _, out var loadout)
                || loadout?.WeaponBindings == null)
                return null;

            if (!MyDefinitionManager.TryGet(loadout.WebbingItemId, out MyContainerDefinition webbingContainer)
                || webbingContainer?.Components == null)
                return null;

            var profile = new SiNpcAmmoProfile();
            for (var i = 0; i < loadout.WeaponBindings.Slots.Count; i++)
            {
                var binding = loadout.WeaponBindings.Slots[i];
                if (binding == null || !binding.TryResolveRangedDefinition(out var rangedDefinition) || rangedDefinition == null)
                    continue;

                rangedDefinition.ResolveWeaponBehavior();
                if (!rangedDefinition.ConsumeAmmo)
                    continue;

                if (rangedDefinition.HeldItem.HasValue)
                {
                    var heldItemId = (MyDefinitionId)rangedDefinition.HeldItem.Value;
                    if (!profile.HeldWeaponUnits.TryGetValue(heldItemId, out var existingUnits)
                        || rangedDefinition.MagazineCount > existingUnits)
                        profile.HeldWeaponUnits[heldItemId] = Math.Max(0, rangedDefinition.MagazineCount);
                }

                for (var cartridgeIndex = 0; cartridgeIndex < rangedDefinition.AcceptedCartridges.Length; cartridgeIndex++)
                    profile.CartridgeIds.Add(
                        new MyDefinitionId(
                            typeof(MyObjectBuilder_InventoryItem),
                            rangedDefinition.AcceptedCartridges[cartridgeIndex]));

                for (var magazineIndex = 0; magazineIndex < rangedDefinition.AcceptedMagazines.Length; magazineIndex++)
                {
                    var prefix = rangedDefinition.AcceptedMagazines[magazineIndex];
                    if (string.IsNullOrWhiteSpace(prefix))
                        continue;

                    if (!profile.MagazinePrefixes.Contains(prefix))
                        profile.MagazinePrefixes.Add(prefix);

                    if (!profile.MagazinePrefixUnits.TryGetValue(prefix, out var prefixUnits)
                        || rangedDefinition.MagazineCount > prefixUnits)
                        profile.MagazinePrefixUnits[prefix] = Math.Max(1, rangedDefinition.MagazineCount);
                }
            }

            var storageDefinition = ResolveWebbingStorageDefinition(webbingContainer);
            if (storageDefinition == null || storageDefinition.Items == null || storageDefinition.Items.Count == 0)
                return null;

            foreach (var item in storageDefinition.Items)
            {
                if (item.Value <= 0)
                    continue;

                profile.MaxUnits += ResolveWebbingAmmoUnits(profile, item.Key, item.Value);
            }

            return profile.HasTracking ? profile : null;
        }

        private static MyPAX_ItemStorageEquipmentDefinition ResolveWebbingStorageDefinition(MyContainerDefinition container)
        {
            if (container?.Components == null)
                return null;

            for (var i = 0; i < container.Components.Count; i++)
                if (container.Components[i]?.Definition is MyPAX_ItemStorageEquipmentDefinition storageDefinition)
                    return storageDefinition;

            return null;
        }

        private static int ResolveWebbingAmmoUnits(
            SiNpcAmmoProfile profile,
            in MyDefinitionId definitionId,
            int amount)
        {
            if (amount <= 0)
                return 0;

            if (profile.HeldWeaponUnits.TryGetValue(definitionId, out var heldWeaponUnits) && heldWeaponUnits > 0)
                return heldWeaponUnits * amount;

            if (profile.CartridgeIds.Contains(definitionId))
                return amount;

            return profile.MatchesMagazineSubtype(definitionId.SubtypeName)
                ? ResolveMagazineAmmoUnits(profile, definitionId.SubtypeName) * amount
                : 0;
        }

        private static int CountTrackedAmmoUnits(SiNpcAmmoProfile profile, MyInventoryBase inventory)
        {
            if (profile == null || inventory == null)
                return 0;

            var total = 0;
            for (var i = 0; i < inventory.Items.Count; i++)
            {
                var item = inventory.Items.ItemAt(i);
                if (item == null || !profile.MatchesTrackedInventoryItem(item))
                    continue;

                total += ResolveCurrentItemAmmoUnits(profile, item, includeHeldWeapons: true);
            }

            return Math.Max(0, total);
        }

        private static int ResolveCurrentItemAmmoUnits(
            SiNpcAmmoProfile profile,
            MyInventoryItem item,
            bool includeHeldWeapons)
        {
            if (item == null)
                return 0;

            if (includeHeldWeapons
                && profile.HeldWeaponUnits.ContainsKey(item.DefinitionId)
                && item is MyDurableItem durableHeldItem)
                return Math.Max(0, durableHeldItem.Durability);

            if (profile.CartridgeIds.Contains(item.DefinitionId))
                return Math.Max(0, item.Amount);

            if (!profile.MatchesMagazineSubtype(item.Subtype.String))
                return 0;

            if (item is MyDurableItem durableMagazine)
                return Math.Max(0, durableMagazine.Durability);

            return ResolveMagazineAmmoUnits(profile, item.Subtype.String) * Math.Max(0, item.Amount);
        }

        private static int ResolveMagazineAmmoUnits(SiNpcAmmoProfile profile, string subtype)
        {
            if (profile == null || string.IsNullOrWhiteSpace(subtype))
                return 1;

            foreach (var entry in profile.MagazinePrefixUnits)
                if (subtype.StartsWith(entry.Key, StringComparison.Ordinal))
                    return Math.Max(1, entry.Value);

            return 1;
        }
    }
}
