using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Components;

namespace Si.UtilityAI
{
    public sealed partial class SiNpcSessionComponent
    {
        private readonly List<SiSquadMapMarker> _squadMapMarkerSnapshot = new List<SiSquadMapMarker>();

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
        }

        private void ClearSquadMapMarkers()
        {
            _squadMapMarkerSnapshot.Clear();
        }
    }
}
