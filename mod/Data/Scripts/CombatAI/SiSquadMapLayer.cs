using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Medieval.GameSystems;
using ObjectBuilders.GUI.Map;
using Sandbox.Game.Entities;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using Si.UtilityAI;
using VRage.ObjectBuilders;
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
        private static readonly char[] PopupLineBreaks = { '\n' };
        private readonly Dictionary<string, string> _markerImages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly MyTooltip _tooltip = new MyTooltip();
        private MyPlanetAreasComponent _planetAreas;
        private bool _markerTooltipVisible;

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
        }

        public override void Draw(float transitionAlpha)
        {
            var session = SiNpcSessionComponent.Instance;
            var snapshot = session?.SquadMapMarkerSnapshot;
            if (session == null
                || snapshot == null
                || snapshot.Count == 0
                || _planetAreas == null
                || Map?.CurrentView == null)
            {
                HideMarkerTooltip();
                return;
            }

            var mouseScreenPosition = MyGuiManager.MouseCursorPosition;
            var hoveredCell = Map.HoveredCell;
            SiSquadMapMarker hoveredMarker = null;
            Vector2 hoveredMarkerPosition = default(Vector2);
            var hoveredDistanceSquared = float.MaxValue;

            foreach (var marker in snapshot)
            {
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
                Vector2I cellPosition;
                if (!Map.CurrentView.TryGetCellIdPosition(cellId, out cellPosition))
                    continue;

                var mapPosition = Map.GetMapPosition(marker.Position);
                if (hoveredCell.HasValue && hoveredCell.Value == cellPosition)
                {
                    var screenPosition = MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(mapPosition, false);
                    var distanceSquared = Vector2.DistanceSquared(screenPosition, mouseScreenPosition);
                    if (distanceSquared < hoveredDistanceSquared)
                    {
                        hoveredMarker = marker;
                        hoveredMarkerPosition = mapPosition;
                        hoveredDistanceSquared = distanceSquared;
                    }
                }

                DrawMarkerIcon(mapPosition, marker.StyleId, transitionAlpha, false);
            }

            if (hoveredMarker == null)
            {
                HideMarkerTooltip();
                return;
            }

            DrawMarkerIcon(hoveredMarkerPosition, hoveredMarker.StyleId, transitionAlpha, true);
            ShowMarkerTooltip(hoveredMarker);
        }

        private void DrawMarkerIcon(Vector2 mapPosition, string styleId, float transitionAlpha, bool hovered)
        {
            if (!_markerImages.TryGetValue(styleId ?? string.Empty, out var texture) || string.IsNullOrWhiteSpace(texture))
                _markerImages.TryGetValue("default", out texture);
            if (string.IsNullOrWhiteSpace(texture))
                return;

            MyRenderProxy.DrawSprite(
                texture,
                mapPosition,
                hovered ? HoveredMarkerSize : IdleMarkerSize,
                hovered
                    ? new Color(1f, 1f, 1f, transitionAlpha)
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

        private void ShowMarkerTooltip(SiSquadMapMarker marker)
        {
            if (marker == null)
                return;

            BuildTooltip(marker);
            Map.SetTooltip(_tooltip);
            Map.ShowToolTip();
            _markerTooltipVisible = true;
        }

        private void BuildTooltip(SiSquadMapMarker marker)
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
    }
}
