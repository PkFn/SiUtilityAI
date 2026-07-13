using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Medieval.GameSystems;
using Medieval.GUI.Ingame.Map;
using ObjectBuilders.GUI.Map;
using Sandbox.Game.Entities;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using Si.UtilityAI;
using VRage.ObjectBuilders;
using VRage.Input.Devices.Mouse;
using VRage.Utils;
using VRageMath;
using VRageRender;
using VRageRender.Messages;

namespace Medieval.GUI.Ingame.Map.RenderLayers
{
    [MyObjectBuilderDefinition(null)]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiSquadMapLayer : MyObjectBuilder_PlanetMapRenderLayer
    {
        public string DefaultMarkerImage;
        public string PlayerLedMarkerImage;
        public string AlliedMarkerImage;
        public string FriendlyMarkerImage;
        public string EnemyMarkerImage;
        public string IndependentMarkerImage;
        public string WaypointImage;
        public string WaypointLineImage;
    }

    [MyMapRenderLayer(typeof(MyObjectBuilder_SiSquadMapLayer), true)]
    internal sealed class SiSquadMapLayer : MyPlanetMapRenderLayerBase
    {
        private static readonly Vector2 IdleMarkerSize = new Vector2(0.0105f, 0.0105f);
        private static readonly Vector2 HoveredMarkerSize = new Vector2(0.015f, 0.015f);
        private static readonly Vector2 SelectedMarkerSize = new Vector2(0.018f, 0.018f);
        private static readonly Vector2 WaypointMarkerSize = new Vector2(0.012f, 0.012f);
        private static readonly Vector2 HoveredWaypointMarkerSize = new Vector2(0.016f, 0.016f);
        private static readonly char[] PopupLineBreaks = { '\n' };
        private static readonly Vector2 CommandOverlayAnchor = new Vector2(-0.98f, -0.86f);
        private readonly Dictionary<string, string> _markerImages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly MyTooltip _tooltip = new MyTooltip();
        private string _waypointImage;
        private string _waypointLineImage;
        private MyPlanetAreasComponent _planetAreas;
        private SiSquadMapMarker _hoveredMarker;
        private bool _markerTooltipVisible;
        private bool _middleMouseDownLastFrame;
        private bool _ignoreMiddleMouseUntilRelease = true;

        public override void Init(MyPlanetMapControl map, MyMapGridView view, MyObjectBuilder_PlanetMapRenderLayer builder)
        {
            base.Init(map, view, builder);

            var ob = builder as MyObjectBuilder_SiSquadMapLayer;
            _markerImages.Clear();
            _markerImages["default"] = FirstNonEmpty(
                ob?.DefaultMarkerImage,
                ob?.IndependentMarkerImage,
                ob?.FriendlyMarkerImage,
                ob?.AlliedMarkerImage,
                ob?.EnemyMarkerImage);
            _markerImages["player"] = !string.IsNullOrWhiteSpace(ob?.PlayerLedMarkerImage)
                ? ob.PlayerLedMarkerImage
                : FirstNonEmpty(
                    ob?.FriendlyMarkerImage,
                    ob?.AlliedMarkerImage,
                    ob?.DefaultMarkerImage);
            _markerImages["ally"] = !string.IsNullOrWhiteSpace(ob?.AlliedMarkerImage)
                ? ob.AlliedMarkerImage
                : FirstNonEmpty(
                    ob?.FriendlyMarkerImage,
                    ob?.DefaultMarkerImage);
            _markerImages["friendly"] = FirstNonEmpty(
                ob?.FriendlyMarkerImage,
                ob?.AlliedMarkerImage,
                ob?.PlayerLedMarkerImage,
                ob?.DefaultMarkerImage);
            _markerImages["enemy"] = FirstNonEmpty(
                ob?.EnemyMarkerImage,
                ob?.DefaultMarkerImage,
                ob?.IndependentMarkerImage);
            _markerImages["independent"] = FirstNonEmpty(
                ob?.IndependentMarkerImage,
                ob?.DefaultMarkerImage,
                ob?.FriendlyMarkerImage);
            _waypointImage = FirstNonEmpty(
                ob?.WaypointImage,
                "Textures/GUI/Map/SiUtilityAI_SquadWaypoint.png");
            _waypointLineImage = FirstNonEmpty(
                ob?.WaypointLineImage,
                "Textures/GUI/Map/SiUtilityAI_SquadWaypointLine.dds");

            _planetAreas = map?.Planet?.Components.Get<MyPlanetAreasComponent>();
            ResetMiddleMousePollingState(true);
        }

        public override void Draw(float transitionAlpha)
        {
            var session = SiNpcSessionComponent.Instance;
            var snapshot = session?.SquadMapMarkerSnapshot;
            if (session == null
                || _planetAreas == null
                || Map?.CurrentView == null)
            {
                ResetMiddleMousePollingState(true);
                HideMarkerTooltip();
                return;
            }

            PollMiddleMouseCommandActivation(session);

            if (snapshot == null
                || snapshot.Count == 0)
            {
                _hoveredMarker = null;
                HideMarkerTooltip();
                return;
            }

            var layout = BuildMarkerLayout(snapshot);
            var mouseNormalizedPosition = MyGuiManager.MouseCursorPosition;
            var hoveredMarker = FindMarkerAtCursor(layout, mouseNormalizedPosition, out var hoveredMarkerPosition);
            FindWaypointAtCursor(
                layout,
                mouseNormalizedPosition,
                out var hoveredWaypointMarker,
                out var hoveredWaypointPosition);

            DrawWaypointConnections(layout, transitionAlpha);

            for (var i = 0; i < layout.Count; i++)
            {
                var marker = layout[i].Marker;

                if (layout[i].HasLeaderPosition)
                {
                    DrawMarkerIcon(
                        layout[i].MapPosition,
                        marker.StyleId,
                        transitionAlpha,
                        false,
                        session.IsMapCommandLeaderSelected(marker.Leader));
                }

                if (layout[i].HasWaypointPosition)
                    DrawWaypointMarker(layout[i].WaypointMapPosition, transitionAlpha, false);
            }

            if (hoveredWaypointMarker != null)
            {
                _hoveredMarker = null;
                DrawWaypointMarker(hoveredWaypointPosition, transitionAlpha, true);
                ShowWaypointTooltip(hoveredWaypointMarker);
                DrawMapCommandOverlay(session, null);
                return;
            }

            if (hoveredMarker == null)
            {
                _hoveredMarker = null;
                HideMarkerTooltip();
                DrawMapCommandOverlay(session, null);
                return;
            }

            _hoveredMarker = hoveredMarker;
            DrawMarkerIcon(
                hoveredMarkerPosition,
                hoveredMarker.StyleId,
                transitionAlpha,
                true,
                session.IsMapCommandLeaderSelected(hoveredMarker.Leader));
            ShowMarkerTooltip(session, hoveredMarker);
            DrawMapCommandOverlay(session, hoveredMarker);
        }

        public override void OnEnable(bool enable)
        {
            base.OnEnable(enable);
            ResetMiddleMousePollingState(true);
            if (!enable)
                SiNpcSessionComponent.Instance?.ResetLocalMapCommandSelection();
        }

        public override void OnRemoving()
        {
            ResetMiddleMousePollingState(true);
            SiNpcSessionComponent.Instance?.ResetLocalMapCommandSelection();
            base.OnRemoving();
        }

        private void DrawMarkerIcon(
            Vector2 mapPosition,
            string styleId,
            float transitionAlpha,
            bool hovered,
            bool selected)
        {
            if (!_markerImages.TryGetValue(styleId ?? string.Empty, out var texture) || string.IsNullOrWhiteSpace(texture))
                _markerImages.TryGetValue("default", out texture);
            if (string.IsNullOrWhiteSpace(texture))
                return;

            if (selected)
            {
                MyRenderProxy.DrawSprite(
                    texture,
                    mapPosition,
                    SelectedMarkerSize,
                    new Color(1f, 0.85f, 0.25f, 0.42f * transitionAlpha),
                    MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER,
                    0f,
                    Vector2.UnitX,
                    1f,
                    null,
                    0f,
                    true,
                    null,
                    SpriteBatchMode.Default);
            }

            MyRenderProxy.DrawSprite(
                texture,
                mapPosition,
                hovered ? HoveredMarkerSize : IdleMarkerSize,
                hovered
                    ? new Color(1f, 1f, 1f, transitionAlpha)
                    : selected
                        ? new Color(1f, 0.96f, 0.72f, 0.95f * transitionAlpha)
                    : new Color(0.88f, 1f, 0.88f, 0.9f * transitionAlpha),
                MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER,
                0f,
                Vector2.UnitX,
                1f,
                null,
                0f,
                true,
                null,
                SpriteBatchMode.Default);
        }

        private void DrawWaypointConnections(IReadOnlyList<SiMarkerLayout> layout, float transitionAlpha)
        {
            if (string.IsNullOrWhiteSpace(_waypointLineImage))
                return;

            for (var i = 0; i < layout.Count; i++)
            {
                if (!layout[i].HasLeaderPosition || !layout[i].HasWaypointPosition)
                    continue;

                var direction = layout[i].WaypointMapPosition - layout[i].MapPosition;
                var distance = direction.Length();
                if (distance <= 0.0001f)
                    continue;

                direction /= distance;
                var rightVector = new Vector2(direction.Y, -direction.X);
                var color = new Color(0.35f, 0.85f, 1f, 0.9f * transitionAlpha);
                MyRenderProxy.DrawSprite(
                    _waypointLineImage,
                    layout[i].MapPosition + direction * (distance * 0.5f),
                    new Vector2(0.004f, distance),
                    color,
                    MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER,
                    0f,
                    rightVector,
                    1f,
                    null,
                    0f,
                    true,
                    null,
                    SpriteBatchMode.Default);
            }
        }

        private void DrawWaypointMarker(Vector2 mapPosition, float transitionAlpha, bool hovered)
        {
            if (string.IsNullOrWhiteSpace(_waypointImage))
                return;

            var color = hovered
                ? new Color(1f, 1f, 1f, transitionAlpha)
                : new Color(0.35f, 0.85f, 1f, 0.95f * transitionAlpha);
            MyRenderProxy.DrawSprite(
                _waypointImage,
                mapPosition,
                hovered ? HoveredWaypointMarkerSize : WaypointMarkerSize,
                color,
                MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER,
                0f,
                Vector2.UnitX,
                1f,
                null,
                0f,
                true,
                null,
                SpriteBatchMode.Default);
        }

        private void ShowMarkerTooltip(SiNpcSessionComponent session, SiSquadMapMarker marker)
        {
            if (marker == null)
                return;

            BuildTooltip(session, marker);
            Map.SetTooltip(_tooltip);
            Map.ShowToolTip();
            _markerTooltipVisible = true;
        }

        private void ShowWaypointTooltip(SiSquadMapMarker marker)
        {
            if (marker == null)
                return;

            using (_tooltip.OpenBatch(true))
            {
                _tooltip.AddTitle("Squad waypoint");
                if (!string.IsNullOrWhiteSpace(marker.Name))
                    _tooltip.AddLine(marker.Name);
            }

            Map.SetTooltip(_tooltip);
            Map.ShowToolTip();
            _markerTooltipVisible = true;
        }

        private void BuildTooltip(SiNpcSessionComponent session, SiSquadMapMarker marker)
        {
            using (_tooltip.OpenBatch(true))
            {
                if (!string.IsNullOrWhiteSpace(marker.Name))
                    _tooltip.AddTitle(marker.Name);

                if (string.IsNullOrWhiteSpace(marker.Description))
                    return;

                var descriptionLines = marker.Description.Split(PopupLineBreaks, StringSplitOptions.RemoveEmptyEntries);
                for (var i = 0; i < descriptionLines.Length; i++)
                    _tooltip.AddLine(descriptionLines[i]);

                var commandText = session?.MapCommandTooltipText(marker);
                if (!string.IsNullOrWhiteSpace(commandText))
                    _tooltip.AddLine(commandText);
            }
        }

        private void HideMarkerTooltip()
        {
            if (Map == null)
                return;

            Map.SetTooltip((MyTooltip)null);
            if (_markerTooltipVisible)
                Map.HideToolTip();
            _markerTooltipVisible = false;
        }

        private void DrawMapCommandOverlay(SiNpcSessionComponent session, SiSquadMapMarker hoveredMarker)
        {
            var text = session?.MapCommandOverlayText(hoveredMarker);
            if (string.IsNullOrWhiteSpace(text))
                return;

            MyRenderProxy.DebugDrawText2D(
                CommandOverlayAnchor,
                text,
                session.HasSelectedMapCommandLeader() ? Color.Gold : Color.LightGreen,
                0.7f);
        }

        private void PollMiddleMouseCommandActivation(SiNpcSessionComponent session)
        {
            var input = MyAPIGateway.Input;
            if (!CanProcessMapCommands(input, session))
            {
                ResetMiddleMousePollingState(true);
                return;
            }

            var middleDown = input.IsMouseDown(MyMouseButtons.Middle);
            if (_ignoreMiddleMouseUntilRelease)
            {
                _middleMouseDownLastFrame = middleDown;
                if (middleDown)
                    return;

                _ignoreMiddleMouseUntilRelease = false;
                return;
            }

            var middlePressed = input.IsMousePressed(MyMouseButtons.Middle);
            var middleRisingEdge = middleDown && !_middleMouseDownLastFrame;
            _middleMouseDownLastFrame = middleDown;
            if (!middlePressed && !middleRisingEdge)
                return;

            HandleMapCommandActivation(session, Map.HoveredCell);
        }

        private bool TryResolveCommandTarget(Vector2I cell, out Vector3D target)
        {
            target = Vector3D.Zero;
            if (Map?.CurrentView == null || _planetAreas == null)
                return false;

            if (Map.CurrentZoomLevel == MyPlanetMapZoomLevel.Kingdom)
                return TryResolveKingdomTarget(cell, out target);

            long cellId;
            try
            {
                cellId = Map.CurrentView[cell.X, cell.Y];
            }
            catch
            {
                return false;
            }

            try
            {
                target = _planetAreas.CalculateAreaCenter(cellId);
                var planet = Map.Planet;
                if (planet != null)
                    target = planet.GetClosestSurfacePointGlobal(ref target);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryResolveKingdomTarget(Vector2I cell, out Vector3D target)
        {
            target = Vector3D.Zero;
            var planet = Map?.Planet;
            if (_planetAreas == null || planet == null)
                return false;

            var regionCount = _planetAreas.RegionCount;
            if (regionCount <= 0)
                return false;

            try
            {
                Sandbox.Game.Entities.Planet.MyEnvironmentCubemapHelper.TexcoordToWorld(
                    new Vector2D(
                        (2.0 * cell.X - regionCount) / regionCount,
                        (2.0 * cell.Y - regionCount) / regionCount),
                    (int)Map.CurrentFace,
                    planet.AverageRadius,
                    out target);
                target = planet.GetClosestSurfacePointGlobal(ref target);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void HandleMapCommandActivation(SiNpcSessionComponent session, Vector2I? cell)
        {
            if (session == null
                || Map == null
                || !cell.HasValue)
                return;

            var clickedMarker = FindMarkerAtCursor(
                BuildMarkerLayout(session.SquadMapMarkerSnapshot),
                MyGuiManager.MouseCursorPosition,
                out _);
            if (clickedMarker != null && session.CanLocalPlayerCommandSquad(clickedMarker))
            {
                session.ToggleLocalMapCommandSelection(clickedMarker);
                return;
            }

            if (!session.HasSelectedMapCommandLeader())
                return;

            Vector3D target;
            if (!TryResolveCommandTarget(cell.Value, out target))
                return;

            session.TryIssueSelectedMapMoveOrder(target);
        }

        private List<SiMarkerLayout> BuildMarkerLayout(IReadOnlyList<SiSquadMapMarker> snapshot)
        {
            var layout = new List<SiMarkerLayout>();
            if (snapshot == null || Map?.CurrentView == null)
                return layout;

            var screenViewport = GetScreenViewport();
            if (screenViewport.Size.X <= 0 || screenViewport.Size.Y <= 0)
                return layout;

            for (var i = 0; i < snapshot.Count; i++)
            {
                var marker = snapshot[i];
                if (marker == null)
                    continue;

                var hasLeaderPosition = TryGetMapPosition(
                    marker.Position,
                    screenViewport,
                    out var mapPosition);
                var waypointMapPosition = default(Vector2);
                var hasWaypointPosition = marker.HasWaypoint
                    && TryGetMapPosition(
                        marker.Waypoint,
                        screenViewport,
                        out waypointMapPosition);
                if (!hasLeaderPosition && !hasWaypointPosition)
                    continue;

                layout.Add(new SiMarkerLayout(
                    marker,
                    mapPosition,
                    hasLeaderPosition,
                    waypointMapPosition,
                    hasWaypointPosition));
            }

            return layout;
        }

        private SiSquadMapMarker FindMarkerAtCursor(
            IReadOnlyList<SiMarkerLayout> layout,
            Vector2 mouseNormalizedPosition,
            out Vector2 hoveredMarkerPosition)
        {
            hoveredMarkerPosition = default(Vector2);
            if (layout == null
                || Map?.CurrentView == null)
                return null;

            var mousePixelPosition = MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(mouseNormalizedPosition, false);
            SiSquadMapMarker hoveredMarker = null;
            var hoveredDistanceSquared = float.MaxValue;
            var maxDistanceSquared = MarkerHitRadiusSquared();
            for (var i = 0; i < layout.Count; i++)
            {
                var markerEntry = layout[i];
                var marker = markerEntry.Marker;
                if (marker == null || !markerEntry.HasLeaderPosition)
                    continue;

                var markerPixelPosition = MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(markerEntry.MapPosition, false);
                var distanceSquared = Vector2.DistanceSquared(markerPixelPosition, mousePixelPosition);
                if (distanceSquared > maxDistanceSquared
                    || distanceSquared >= hoveredDistanceSquared)
                    continue;

                hoveredMarker = marker;
                hoveredMarkerPosition = markerEntry.MapPosition;
                hoveredDistanceSquared = distanceSquared;
            }

            return hoveredMarker;
        }

        private void FindWaypointAtCursor(
            IReadOnlyList<SiMarkerLayout> layout,
            Vector2 mouseNormalizedPosition,
            out SiSquadMapMarker hoveredMarker,
            out Vector2 hoveredWaypointPosition)
        {
            hoveredMarker = null;
            hoveredWaypointPosition = default(Vector2);
            if (layout == null || Map?.CurrentView == null)
                return;

            var mousePixelPosition = MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(mouseNormalizedPosition, false);
            var hoveredDistanceSquared = float.MaxValue;
            var maxDistanceSquared = WaypointHitRadiusSquared();
            for (var i = 0; i < layout.Count; i++)
            {
                var markerEntry = layout[i];
                if (markerEntry.Marker == null || !markerEntry.HasWaypointPosition)
                    continue;

                var waypointPixelPosition = MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(
                    markerEntry.WaypointMapPosition,
                    false);
                var distanceSquared = Vector2.DistanceSquared(waypointPixelPosition, mousePixelPosition);
                if (distanceSquared > maxDistanceSquared || distanceSquared >= hoveredDistanceSquared)
                    continue;

                hoveredMarker = markerEntry.Marker;
                hoveredWaypointPosition = markerEntry.WaypointMapPosition;
                hoveredDistanceSquared = distanceSquared;
            }
        }

        private bool IsActiveInteractiveLayer()
        {
            return Visible
                   && Map != null
                   && View != null
                   && ReferenceEquals(View, Map.CurrentView);
        }

        private bool CanProcessMapCommands(VRage.Input.IMyInput input, SiNpcSessionComponent session)
        {
            return session != null
                   && input != null
                   && Map != null
                   && Map.IsMouseOver
                   && IsActiveInteractiveLayer();
        }

        private void ResetMiddleMousePollingState(bool requireRelease)
        {
            _middleMouseDownLastFrame = false;
            _ignoreMiddleMouseUntilRelease = requireRelease;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
                return null;

            for (var i = 0; i < values.Length; i++)
                if (!string.IsNullOrWhiteSpace(values[i]))
                    return values[i];

            return null;
        }

        private bool TryGetMapPosition(
            Vector3D position,
            RectangleF screenViewport,
            out Vector2 mapPosition)
        {
            mapPosition = default(Vector2);
            if (Map?.CurrentView == null)
                return false;

            try
            {
                mapPosition = Map.GetMapPosition(position);
            }
            catch
            {
                return false;
            }

            return screenViewport.Contains(mapPosition);
        }

        private static float MarkerHitRadiusSquared()
        {
            var markerPixelSize = MyGuiManager.GetScreenSizeFromNormalizedSize(HoveredMarkerSize, false);
            var radius = Math.Max(markerPixelSize.X, markerPixelSize.Y) * 0.75f + 4f;
            return radius * radius;
        }

        private static float WaypointHitRadiusSquared()
        {
            var markerPixelSize = MyGuiManager.GetScreenSizeFromNormalizedSize(HoveredWaypointMarkerSize, false);
            var radius = Math.Max(markerPixelSize.X, markerPixelSize.Y) * 0.75f + 4f;
            return radius * radius;
        }

        private struct SiMarkerLayout
        {
            public SiMarkerLayout(
                SiSquadMapMarker marker,
                Vector2 mapPosition,
                bool hasLeaderPosition,
                Vector2 waypointMapPosition,
                bool hasWaypointPosition)
            {
                Marker = marker;
                MapPosition = mapPosition;
                HasLeaderPosition = hasLeaderPosition;
                WaypointMapPosition = waypointMapPosition;
                HasWaypointPosition = hasWaypointPosition;
            }

            public SiSquadMapMarker Marker;
            public Vector2 MapPosition;
            public bool HasLeaderPosition;
            public Vector2 WaypointMapPosition;
            public bool HasWaypointPosition;
        }

        private RectangleF GetScreenViewport()
        {
            return new RectangleF(Map.GetPositionAbsoluteTopLeft() + Map.MapOffset, Map.MapSize);
        }
    }
}
