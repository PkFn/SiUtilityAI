using System.Collections.Generic;
using Medieval.GameSystems;
using Medieval.GUI.Ingame.Map;
using Medieval.GUI.Ingame.Map.RenderLayers;
using ObjectBuilders.GUI.Map;
using Sandbox.Game.Entities;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using VRage.Components;
using VRageMath;
using VRageRender;

namespace Si.UtilityAI
{
    public sealed partial class SiNpcSessionComponent
    {
        private readonly List<SiSquadMapMarker> _squadMapMarkerSnapshot = new List<SiSquadMapMarker>();
        private MyMapScreen _boundSquadMapScreen;
        private SiSquadMapLayer _boundKingdomSquadMapLayer;
        private SiSquadMapLayer _boundRegionSquadMapLayer;

        [Update(1_000)]
        private void UpdateSquadMapMarkers(long elapsedMilliseconds)
        {
            if (MyAPIGateway.Utilities?.IsDedicated ?? false)
                return;

            RefreshSquadMapLayerBinding();
            _squadMapMarkerSnapshot.Clear();

            var player = LocalPlayer();
            if (!_showTroopMarkers
                || player?.Identity == null
                || Npcs == null
                || Squads == null)
                return;

            _squadMapMarkerSnapshot.AddRange(Squads.CreateAlliedSquadMapMarkers(Npcs, player.Identity.Id));
        }

        [FixedUpdate]
        private void RefreshSquadMapLayerBinding()
        {
            if (MyAPIGateway.Utilities?.IsDedicated ?? false)
                return;

            var mapScreen = Container?.Get<MyMapSessionComponent>()?.MapScreen;
            if (mapScreen == null || mapScreen == _boundSquadMapScreen)
                return;

            ClearBoundSquadMapLayers();
            BindSquadMapLayers(mapScreen);
        }

        private void BindSquadMapLayers(MyMapScreen mapScreen)
        {
            var mapControl = mapScreen?.MapControl;
            if (mapControl == null)
                return;

            _boundSquadMapScreen = mapScreen;

            _boundKingdomSquadMapLayer = new SiSquadMapLayer(this);
            _boundKingdomSquadMapLayer.Init(mapControl, mapControl.KingdomView, new MyObjectBuilder_PlanetMapRenderLayer());
            mapControl.KingdomView.AddLayer(_boundKingdomSquadMapLayer);

            _boundRegionSquadMapLayer = new SiSquadMapLayer(this);
            _boundRegionSquadMapLayer.Init(mapControl, mapControl.RegionView, new MyObjectBuilder_PlanetMapRenderLayer());
            mapControl.RegionView.AddLayer(_boundRegionSquadMapLayer);
        }

        private void ClearBoundSquadMapLayers()
        {
            var mapControl = _boundSquadMapScreen?.MapControl;
            if (mapControl != null)
            {
                if (_boundKingdomSquadMapLayer != null)
                    mapControl.KingdomView.RemoveLayer(_boundKingdomSquadMapLayer);
                if (_boundRegionSquadMapLayer != null)
                    mapControl.RegionView.RemoveLayer(_boundRegionSquadMapLayer);
            }

            _boundKingdomSquadMapLayer = null;
            _boundRegionSquadMapLayer = null;
            _boundSquadMapScreen = null;
        }

        private void ClearSquadMapMarkers()
        {
            _squadMapMarkerSnapshot.Clear();
            ClearBoundSquadMapLayers();
        }

        private sealed class SiSquadMapLayer : MyPlanetMapRenderLayerBase
        {
            private const float KingdomTextScale = 0.45f;
            private const float RegionTextScale = 0.6f;
            private const float MarkerRadius = 0.0035f;
            private const double MinPlanetAltitudeTolerance = 1000;
            private const double MaxPlanetAltitudeTolerance = 10000;
            private readonly SiNpcSessionComponent _session;

            public SiSquadMapLayer(SiNpcSessionComponent session)
            {
                _session = session;
            }

            public override void Draw(float transitionAlpha)
            {
                if (_session == null || !_session._showTroopMarkers || _session._squadMapMarkerSnapshot.Count == 0)
                    return;

                var map = Map;
                var planet = map?.Planet;
                if (planet == null)
                    return;

                var environmentViewport = GetEnvironmentMapViewport(map, out var visibleFace);
                var screenViewport = GetScreenViewport(map);
                var envToScreenScale = screenViewport.Size / environmentViewport.Size;
                var envToScreenTranslate = screenViewport.Position - envToScreenScale * environmentViewport.Position;
                var worldToPlanet = planet.PositionComp.WorldMatrixInvScaled;
                var textScale = View.Zoom == MyPlanetMapZoomLevel.Region ? RegionTextScale : KingdomTextScale;

                foreach (var marker in _session._squadMapMarkerSnapshot)
                {
                    Vector2 screenAnchor;
                    if (!TryProjectMarkerToScreen(
                            marker,
                            planet,
                            in worldToPlanet,
                            visibleFace,
                            in environmentViewport,
                            in envToScreenScale,
                            in envToScreenTranslate,
                            out screenAnchor))
                        continue;

                    var color = ResolveMarkerColor(marker.StyleId);
                    var glyph = ResolveMarkerGlyph(marker.StyleId);
                    var drawPos = MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(screenAnchor);
                    MyRenderProxy.DrawString(
                        MyGuiConstants.DEFAULT_FONT,
                        drawPos + new Vector2(0, -6),
                        color,
                        glyph,
                        textScale,
                        float.PositiveInfinity);
                    MyRenderProxy.DrawString(
                        MyGuiConstants.DEFAULT_FONT,
                        drawPos + new Vector2(8, -6),
                        color,
                        marker.Name,
                        textScale,
                        float.PositiveInfinity);
                }
            }

            private static bool TryProjectMarkerToScreen(
                SiSquadMapMarker marker,
                MyPlanet planet,
                in MatrixD worldToPlanet,
                int visibleFace,
                in RectangleF environmentViewport,
                in Vector2 envToScreenScale,
                in Vector2 envToScreenTranslate,
                out Vector2 screenAnchor)
            {
                screenAnchor = default;
                var worldPosition = marker.Position;
                var localPosition = Vector3D.Transform(worldPosition, worldToPlanet);
                var radius = localPosition.Length();
                if (radius < planet.MinimumRadius - MinPlanetAltitudeTolerance
                    || radius > planet.MaximumRadius + MaxPlanetAltitudeTolerance)
                    return false;

                Sandbox.Game.Entities.Planet.MyEnvironmentCubemapHelper.ProjectToCube(ref localPosition, out var face, out var texCoords);
                if (face != visibleFace)
                    return false;

                var markerUv = new Vector2((float)texCoords.X, (float)texCoords.Y);
                if (!Contains(environmentViewport, markerUv))
                    return false;

                screenAnchor = markerUv * envToScreenScale + envToScreenTranslate;
                return true;
            }

            private static RectangleF GetEnvironmentMapViewport(MyPlanetMapControl control, out int face)
            {
                var view = control.CurrentView;
                var counts = view.Size;
                int minCellX;
                int minCellY;
                int maxCellX;
                int maxCellY;
                int ignoredFace;
                MyPlanetAreasComponent.UnpackAreaId(view[0, 0], out face, out minCellX, out minCellY);
                MyPlanetAreasComponent.UnpackAreaId(view[counts.X - 1, counts.Y - 1], out ignoredFace, out maxCellX, out maxCellY);

                var scalingCount = control.CurrentZoomLevel == MyPlanetMapZoomLevel.Kingdom
                    ? control.Planet.Get<MyPlanetAreasComponent>().RegionCount
                    : control.Planet.Get<MyPlanetAreasComponent>().AreaCount;
                var minTexCoord = (2 * new Vector2(minCellX, minCellY) - scalingCount) / scalingCount;
                var maxTexCoord = (2 * new Vector2(maxCellX + 1, maxCellY + 1) - scalingCount) / scalingCount;
                return new RectangleF(minTexCoord, maxTexCoord - minTexCoord);
            }

            private static RectangleF GetScreenViewport(MyPlanetMapControl control)
            {
                return new RectangleF(control.GetPositionAbsoluteTopLeft() + control.MapOffset, control.MapSize);
            }

            private static bool Contains(in RectangleF rectangle, in Vector2 point)
            {
                var min = rectangle.Position;
                var max = rectangle.Position + rectangle.Size;
                return point.X >= min.X - MarkerRadius
                       && point.X <= max.X + MarkerRadius
                       && point.Y >= min.Y - MarkerRadius
                       && point.Y <= max.Y + MarkerRadius;
            }

            private static Color ResolveMarkerColor(string styleId)
            {
                switch ((styleId ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "player":
                        return Color.LightBlue;
                    case "ally":
                        return Color.LightGreen;
                    default:
                        return Color.LightGreen;
                }
            }

            private static string ResolveMarkerGlyph(string styleId)
            {
                switch ((styleId ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "player":
                        return "P";
                    case "ally":
                        return "A";
                    default:
                        return "+";
                }
            }
        }
    }
}
