using System;
using Pax.Cannons;
using VRage.Components;
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

        private void UpdateAmmoSpeechState()
        {
            if (Definition == null || !Definition.ConsumeAmmo)
            {
                _lastAmmoSpeechState = AmmoSpeechState.Unknown;
                return;
            }

            if (!HasInventory())
                return;

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
            if (Definition == null || !Definition.ConsumeAmmo)
                return AmmoSpeechState.Unknown;

            var loadedRounds = GetLoadedRoundsFromEquippedItem();
            var hasLooseAmmo = HasCompatibleLooseAmmo();
            var hasLoadedMagazine = HasCompatibleLoadedMagazine();
            var hasMagazineShell = HasCompatibleMagazineShell();
            var hasReserveAmmo = UsesDetachableMagazineMaintenance
                ? hasLoadedMagazine || (hasLooseAmmo && hasMagazineShell)
                : hasLooseAmmo || hasLoadedMagazine;

            if (loadedRounds <= 0)
                return hasReserveAmmo ? AmmoSpeechState.Normal : AmmoSpeechState.Empty;

            return hasReserveAmmo ? AmmoSpeechState.Normal : AmmoSpeechState.Low;
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
