using System;
using Pax.Cannons;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.Inventory;
using VRage.Components;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Inventory;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Inventory;
using VRageMath;

namespace Si.UtilityAI
{
    public partial class SiNpcRangedWeaponComponent
    {
        private void BeginReloadMaintenance()
        {
            if (!UsesDetachableMagazineMaintenance || Entity == null)
                return;

            _reloadMaintenanceState = ReloadMaintenanceState.RemovingEmptyMagazine;
            MyPAX_HandheldGun.RequestTertiary(Entity.EntityId, false);
            _fireCooldown = Math.Max(_fireCooldown, EffectiveReloadIntervalMilliseconds);
            ScheduleReloadMaintenance(EffectiveReloadIntervalMilliseconds);
        }

        private void ScheduleReloadMaintenance(long delay)
        {
            if (_maintenanceQueued)
                return;

            _maintenanceQueued = true;
            AddScheduledCallback(ContinueReloadMaintenance, Math.Max(1L, delay));
        }

        [Update(false)]
        private void ContinueReloadMaintenance(long _)
        {
            _maintenanceQueued = false;
            try
            {
                if (_reloadMaintenanceState == ReloadMaintenanceState.None)
                    return;
                if (Entity == null || Entity.Closed || Entity.MarkedForClose)
                {
                    _reloadMaintenanceState = ReloadMaintenanceState.None;
                    return;
                }

                if (!HasInventory())
                {
                    _reloadMaintenanceState = ReloadMaintenanceState.None;
                    return;
                }

                switch (_reloadMaintenanceState)
                {
                    case ReloadMaintenanceState.RemovingEmptyMagazine:
                        AddCasualReplacementMagazine();
                        if (HasCompatibleLoadedMagazine())
                        {
                            TriggerMagazineLoad();
                            _reloadMaintenanceState = ReloadMaintenanceState.LoadingMagazine;
                            ScheduleReloadMaintenance(EffectiveReloadIntervalMilliseconds);
                            return;
                        }

                        if (HasCompatibleLooseAmmo() && HasCompatibleMagazineShell())
                        {
                            MyPAX_HandheldGun.RequestTertiary(Entity.EntityId, true);
                            _reloadMaintenanceState = ReloadMaintenanceState.FillingMagazines;
                            ScheduleReloadMaintenance(EffectiveReloadIntervalMilliseconds);
                            return;
                        }

                        _reloadMaintenanceState = ReloadMaintenanceState.None;
                        return;

                    case ReloadMaintenanceState.FillingMagazines:
                        if (HasCompatibleLoadedMagazine())
                        {
                            TriggerMagazineLoad();
                            _reloadMaintenanceState = ReloadMaintenanceState.LoadingMagazine;
                            ScheduleReloadMaintenance(EffectiveReloadIntervalMilliseconds);
                            return;
                        }

                        if (HasCompatibleLooseAmmo() && HasCompatibleMagazineShell())
                        {
                            ScheduleReloadMaintenance(EffectiveReloadIntervalMilliseconds);
                            return;
                        }

                        _reloadMaintenanceState = ReloadMaintenanceState.None;
                        return;

                    case ReloadMaintenanceState.LoadingMagazine:
                        RestoreCasualAmmoAfterReload();
                        _reloadMaintenanceState = ReloadMaintenanceState.None;
                        return;
                }
            }
            finally
            {
                UpdateAmmoSpeechState();
            }
        }

        private void TriggerMagazineLoad()
        {
            MyPAX_HandheldGun.ServerGunShootEvent(Entity.EntityId, Quaternion.Identity);
            _fireCooldown = Math.Max(_fireCooldown, EffectiveReloadIntervalMilliseconds);
        }

        private int EffectiveFireIntervalMilliseconds
        {
            get
            {
                var interval = Definition.FireCooldownMilliseconds;
                return interval > 0 ? interval : 1;
            }
        }

        private int EffectiveReloadIntervalMilliseconds =>
            Math.Max(600, Definition.MagazineReloadMilliseconds > 0 ? Definition.MagazineReloadMilliseconds : 600);

        private bool UsesDetachableMagazineMaintenance =>
            Definition != null
            && Definition.ConsumeAmmo
            && Definition.NewMagazineMethod
            && !Definition.InternallyLoaded
            && Definition.AcceptedMagazines != null
            && Definition.AcceptedMagazines.Length > 0;

        private bool NeedsReloadMaintenanceAfterShot =>
            NeedsReloadMaintenanceNow;

        private bool NeedsReloadMaintenanceNow =>
            UsesDetachableMagazineMaintenance
            && GetLoadedRoundsFromEquippedItem() <= 0;

        private bool ShouldRestoreCasualAmmoAfterEmptyShot()
        {
            return _casualAmmoRestoreEnabled
                   && SiNpcSessionComponent.Instance?.CasualModeEnabled == true
                   && Definition != null
                   && Definition.ConsumeAmmo
                   && !UsesDetachableMagazineMaintenance
                   && Definition.InternallyLoaded
                   && GetLoadedRoundsFromEquippedItem() <= 0;
        }

        private void AddCasualReplacementMagazine()
        {
            if (!_casualAmmoRestoreEnabled
                || SiNpcSessionComponent.Instance?.CasualModeEnabled != true
                || !UsesDetachableMagazineMaintenance
                || Definition.AcceptedMagazines == null)
                return;

            string ignored;
            var inventory = SiNpcEquipmentHelper.FindInventory(Entity, out ignored);
            if (inventory == null)
                return;

            var fallbackSubtype = (string)null;
            for (var i = 0; i < Definition.AcceptedMagazines.Length; i++)
            {
                var subtype = Definition.AcceptedMagazines[i];
                if (string.IsNullOrWhiteSpace(subtype))
                    continue;

                if (fallbackSubtype == null)
                    fallbackSubtype = subtype;
                if (subtype.IndexOf("_full_", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var magazineId = new MyDefinitionId(
                    typeof(MyObjectBuilder_MagazineItem),
                    subtype);
                var magazine = MyInventoryItem.Create(magazineId, 1);
                if (magazine == null)
                    continue;

                if (inventory.Add(magazine, MyInventoryBase.NewItemParams.ForcedInsertion))
                    return;
            }

            if (!string.IsNullOrWhiteSpace(fallbackSubtype))
            {
                var magazine = MyInventoryItem.Create(
                    new MyDefinitionId(typeof(MyObjectBuilder_MagazineItem), fallbackSubtype),
                    1);
                if (magazine is MyDurableItem durable)
                    durable.Durability = durable.GetDefinition().MaxDurability;
                if (magazine != null)
                    inventory.Add(magazine, MyInventoryBase.NewItemParams.ForcedInsertion);
            }
        }

        private void RestoreCasualAmmoAfterReload()
        {
            if (!_casualAmmoRestoreEnabled
                || SiNpcSessionComponent.Instance?.CasualModeEnabled != true
                || Definition == null
                || !Definition.ConsumeAmmo)
                return;

            string ignored;
            var inventory = SiNpcEquipmentHelper.FindInventory(Entity, out ignored);
            if (inventory == null || Definition.AcceptedCartridges == null)
                return;

            var amount = Math.Max(1, Definition.MagazineCount);
            for (var i = 0; i < Definition.AcceptedCartridges.Length; i++)
            {
                var subtype = Definition.AcceptedCartridges[i];
                if (!string.IsNullOrWhiteSpace(subtype))
                    inventory.AddItems(
                        new MyDefinitionId(typeof(MyObjectBuilder_InventoryItem), subtype),
                        amount,
                        MyInventoryBase.NewItemParams.ForcedInsertion);
            }
        }

        private void UpdateAmmoSpeechState()
        {
            if (Entity == null)
            {
                _lastAmmoSpeechState = AmmoSpeechState.Unknown;
                return;
            }

            var state = EvaluateAmmoSpeechState();
            if (state == _lastAmmoSpeechState)
                return;

            if (state == AmmoSpeechState.Low)
                TrySpeak("Running low on ammo");
            else if (state == AmmoSpeechState.Empty)
                TrySpeak("Out of ammo");

            _lastAmmoSpeechState = state;
        }

        private AmmoSpeechState EvaluateAmmoSpeechState()
        {
            if (Entity == null)
                return AmmoSpeechState.Unknown;

            var session = SiNpcSessionComponent.Instance;
            if (session?.Npcs == null
                || !session.Npcs.Npcs.TryGetValue(Entity.EntityId, out var npc)
                || !SiNpcAmmoStatusHelper.TryGetAmmoStatus(npc, out var ammoStatus))
                return AmmoSpeechState.Unknown;

            if (ammoStatus.IsEmpty)
                return AmmoSpeechState.Empty;
            if (ammoStatus.IsLow)
                return AmmoSpeechState.Low;
            return AmmoSpeechState.Normal;
        }

        private bool TrySpeak(string message)
        {
            var entityId = Entity?.EntityId ?? 0;
            return entityId != 0
                && SiNpcSessionComponent.Instance?.Npcs?.TrySpeak(entityId, message) == true;
        }

        private enum ReloadMaintenanceState
        {
            None,
            RemovingEmptyMagazine,
            FillingMagazines,
            LoadingMagazine,
        }

        private enum AmmoSpeechState
        {
            Unknown,
            Normal,
            Low,
            Empty,
        }
    }
}
