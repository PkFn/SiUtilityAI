using System.Collections.Generic;
using Medieval.GameSystems.Factions;
using Sandbox.ModAPI;
using VRage.Components;
using VRageMath;

namespace Si.UtilityAI
{
    public sealed partial class SiNpcSessionComponent
    {
        private readonly List<SiSquadMapMarker> _squadMapMarkerSnapshot = new List<SiSquadMapMarker>();
        private bool _hasSelectedMapCommandLeader;
        private SiSquadLeaderKey _selectedMapCommandLeader;

        internal IReadOnlyList<SiSquadMapMarker> SquadMapMarkerSnapshot => _squadMapMarkerSnapshot;

        [Update(1_000)]
        private void UpdateSquadMapMarkers(long elapsedMilliseconds)
        {
            _squadMapMarkerSnapshot.Clear();

            if (MyAPIGateway.Utilities?.IsDedicated ?? false)
                return;

            var player = LocalPlayer();
            if (!_showTroopMarkers
                || player?.Identity == null
                || Npcs == null
                || Squads == null)
                return;

            _squadMapMarkerSnapshot.AddRange(Squads.CreateMapMarkers(Npcs, player.Identity.Id, MarkerSettings));
            if (_hasSelectedMapCommandLeader && !HasVisibleSelectedMapCommandLeader())
                ClearLocalMapCommandSelection(false);
        }

        private void ClearSquadMapMarkers()
        {
            _squadMapMarkerSnapshot.Clear();
            ClearLocalMapCommandSelection(false);
        }

        internal bool HasSelectedMapCommandLeader() =>
            _hasSelectedMapCommandLeader;

        internal bool IsMapCommandLeaderSelected(SiSquadLeaderKey leader) =>
            _hasSelectedMapCommandLeader && _selectedMapCommandLeader.Equals(leader);

        internal bool CanLocalPlayerCommandSquad(SiSquadMapMarker marker)
        {
            if (marker == null || marker.Leader.Kind != SiSquadLeaderKind.Ai)
                return false;

            var identityId = LocalPlayer()?.Identity?.Id ?? 0;
            return CanIdentityCommandArmy(identityId, marker.Leader.Army);
        }

        internal void ResetLocalMapCommandSelection()
        {
            ClearLocalMapCommandSelection(false);
        }

        internal bool ToggleLocalMapCommandSelection(SiSquadMapMarker marker)
        {
            if (!CanLocalPlayerCommandSquad(marker))
                return false;

            if (IsMapCommandLeaderSelected(marker.Leader))
            {
                ClearLocalMapCommandSelection(true);
                return false;
            }

            _selectedMapCommandLeader = marker.Leader;
            _hasSelectedMapCommandLeader = true;
            NotifyShow($"Selected {MapCommandLeaderName(marker.Leader) ?? "allied squad"}.");
            return true;
        }

        internal bool TryIssueSelectedMapMoveOrder(in Vector3D target)
        {
            if (!_hasSelectedMapCommandLeader)
                return false;

            var leader = _selectedMapCommandLeader;
            var identityId = LocalPlayer()?.Identity?.Id ?? 0;
            if (!CanIdentityCommandArmy(identityId, leader.Army))
            {
                ClearLocalMapCommandSelection(false);
                return false;
            }

            RequestAiSquadMoveOrder(leader, target);
            NotifyShow($"Move order sent to {MapCommandLeaderName(leader) ?? "selected squad"}.");
            return true;
        }

        internal string MapCommandOverlayText(SiSquadMapMarker hoveredMarker)
        {
            if (_hasSelectedMapCommandLeader)
            {
                var selectedName = MapCommandLeaderName(_selectedMapCommandLeader) ?? "Selected squad";
                if (hoveredMarker != null
                    && CanLocalPlayerCommandSquad(hoveredMarker)
                    && IsMapCommandLeaderSelected(hoveredMarker.Leader))
                    return $"{selectedName} selected | MBM marker to unselect | MBM map to move";

                if (hoveredMarker != null && CanLocalPlayerCommandSquad(hoveredMarker))
                    return $"{selectedName} selected | MBM hovered marker to switch squad | MBM map to move";

                return $"{selectedName} selected | MBM map to move squad";
            }

            if (hoveredMarker != null && CanLocalPlayerCommandSquad(hoveredMarker))
                return $"MBM to select {hoveredMarker.Name ?? "allied squad"}";

            return "MBM an allied AI squad marker to select it";
        }

        internal string MapCommandTooltipText(SiSquadMapMarker marker)
        {
            if (!CanLocalPlayerCommandSquad(marker))
                return null;

            return IsMapCommandLeaderSelected(marker.Leader)
                ? "Middle mouse: unselect squad"
                : "Middle mouse: select squad";
        }

        private void ClearLocalMapCommandSelection(bool notify)
        {
            if (!_hasSelectedMapCommandLeader)
                return;

            _hasSelectedMapCommandLeader = false;
            _selectedMapCommandLeader = default(SiSquadLeaderKey);
            if (notify)
                NotifyShow("Squad deselected.");
        }

        private bool HasVisibleSelectedMapCommandLeader()
        {
            for (var i = 0; i < _squadMapMarkerSnapshot.Count; i++)
                if (_squadMapMarkerSnapshot[i].Leader.Equals(_selectedMapCommandLeader))
                    return true;

            return false;
        }

        private string MapCommandLeaderName(SiSquadLeaderKey leader)
        {
            for (var i = 0; i < _squadMapMarkerSnapshot.Count; i++)
            {
                var marker = _squadMapMarkerSnapshot[i];
                if (marker.Leader.Equals(leader))
                    return marker.Name;
            }

            return null;
        }

        private static bool CanIdentityCommandArmy(long identityId, SiArmyKey army)
        {
            if (identityId == 0)
                return false;

            var observerArmy = SiSquadBook.ArmyForPlayerIdentity(identityId);
            if (observerArmy.Equals(army))
                return true;

            MyDiplomaticParty observerParty;
            MyDiplomaticParty squadParty;
            if (!SiSquadBook.TryCreateDiplomaticParty(observerArmy, out observerParty)
                || !SiSquadBook.TryCreateDiplomaticParty(army, out squadParty))
                return false;

            var diplomacy = MyDiplomacyManager.Instance;
            if (diplomacy == null)
                return false;

            try
            {
                var relationship = diplomacy.GetRelationshipBetweenParties(observerParty, squadParty);
                var statusDefinition = relationship.StatusDefinition;
                if (statusDefinition != null)
                    return statusDefinition.ShareLocationOnMap;

                var status = relationship.Status;
                return status == diplomacy.RelationshipSelf || status == diplomacy.RelationshipFaction;
            }
            catch
            {
                return false;
            }
        }
    }
}
