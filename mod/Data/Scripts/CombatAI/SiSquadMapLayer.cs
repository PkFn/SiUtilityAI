using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Medieval.GameSystems;
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
    }

    [MyMapRenderLayer(typeof(MyObjectBuilder_SiSquadMapLayer), true)]
    internal sealed class SiSquadMapLayer : MyPlanetMapRenderLayerBase
    {
        private static readonly Vector2 IdleMarkerSize = new Vector2(0.0105f, 0.0105f);
        private static readonly Vector2 HoveredMarkerSize = new Vector2(0.015f, 0.015f);
        private static readonly Vector2 SelectedMarkerSize = new Vector2(0.018f, 0.018f);
        private static readonly char[] PopupLineBreaks = { '\n' };
        private static readonly Vector2 CommandOverlayAnchor = new Vector2(-0.98f, -0.86f);
        private readonly Dictionary<string, string> _markerImages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly MyTooltip _tooltip = new MyTooltip();
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
            _markerImages["default"] = ob?.DefaultMarkerImage;
            _markerImages["player"] = !string.IsNullOrWhiteSpace(ob?.PlayerLedMarkerImage)
                ? ob.PlayerLedMarkerImage
                : ob?.DefaultMarkerImage;
            _markerImages["ally"] = !string.IsNullOrWhiteSpace(ob?.AlliedMarkerImage)
                ? ob.AlliedMarkerImage
                : ob?.DefaultMarkerImage;

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
                HideMarkerTooltip();
                return;
            }

            var mouseScreenPosition = MyGuiManager.MouseCursorPosition;
            var hoveredCell = Map.HoveredCell;
            var hoveredMarker = FindMarkerForCell(snapshot, hoveredCell, mouseScreenPosition, out var hoveredMarkerPosition);

            foreach (var marker in snapshot)
            {
                var mapPosition = Map.GetMapPosition(marker.Position);

                DrawMarkerIcon(
                    mapPosition,
                    marker.StyleId,
                    transitionAlpha,
                    false,
                    session.IsMapCommandLeaderSelected(marker.Leader));
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

        private void ShowMarkerTooltip(SiNpcSessionComponent session, SiSquadMapMarker marker)
        {
            if (marker == null)
                return;

            BuildTooltip(session, marker);
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

            var clickedMarker = FindMarkerForCell(
                session.SquadMapMarkerSnapshot,
                cell,
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

        private SiSquadMapMarker FindMarkerForCell(
            IReadOnlyList<SiSquadMapMarker> snapshot,
            Vector2I? hoveredCell,
            Vector2 mouseScreenPosition,
            out Vector2 hoveredMarkerPosition)
        {
            hoveredMarkerPosition = default(Vector2);
            if (snapshot == null
                || hoveredCell == null
                || _planetAreas == null
                || Map?.CurrentView == null)
                return null;

            SiSquadMapMarker hoveredMarker = null;
            var hoveredDistanceSquared = float.MaxValue;
            for (var i = 0; i < snapshot.Count; i++)
            {
                var marker = snapshot[i];
                if (marker == null)
                    continue;

                long areaId;
                try
                {
                    areaId = _planetAreas.GetArea((Vector3)marker.Position);
                }
                catch
                {
                    continue;
                }

                var cellId = Map.CurrentZoomLevel == MyPlanetMapZoomLevel.Kingdom
                    ? _planetAreas.GetRegionFromArea(areaId)
                    : areaId;
                if (!Map.CurrentView.TryGetCellIdPosition(cellId, out var cellPosition)
                    || hoveredCell.Value != cellPosition)
                    continue;

                var mapPosition = Map.GetMapPosition(marker.Position);
                var screenPosition = MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(mapPosition, false);
                var distanceSquared = Vector2.DistanceSquared(screenPosition, mouseScreenPosition);
                if (distanceSquared >= hoveredDistanceSquared)
                    continue;

                hoveredMarker = marker;
                hoveredMarkerPosition = mapPosition;
                hoveredDistanceSquared = distanceSquared;
            }

            return hoveredMarker;
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
    }
}
