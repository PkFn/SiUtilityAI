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
        private const float MarkerTitleScale = 0.55f;
        private const float MarkerDescriptionScale = 0.45f;
        private const float HoverHitRadius = 0.0125f;
        private static readonly Vector2 IdleMarkerSize = new Vector2(0.0105f, 0.0105f);
        private static readonly Vector2 HoveredMarkerSize = new Vector2(0.015f, 0.015f);
        private static readonly Vector2 HoverPopupOffset = new Vector2(18f, 14f);
        private static readonly Vector2 HoverTextShadowOffset = new Vector2(1.5f, 1.5f);
        private static readonly char[] PopupLineBreaks = { '\n' };
        private readonly Dictionary<string, string> _markerImages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private MyPlanetAreasComponent _planetAreas;

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
                return;

            var mousePosition = MyGuiManager.GetNormalizedCoordinateFromScreenCoordinate(MyGuiManager.MouseCursorPosition);
            var hoverRadiusSquared = HoverHitRadius * HoverHitRadius;
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
                var distanceSquared = Vector2.DistanceSquared(mapPosition, mousePosition);
                if (distanceSquared <= hoverRadiusSquared && distanceSquared < hoveredDistanceSquared)
                {
                    hoveredMarker = marker;
                    hoveredMarkerPosition = mapPosition;
                    hoveredDistanceSquared = distanceSquared;
                }

                DrawMarkerIcon(mapPosition, marker.StyleId, transitionAlpha, false);
            }

            if (hoveredMarker == null)
                return;

            DrawMarkerIcon(hoveredMarkerPosition, hoveredMarker.StyleId, transitionAlpha, true);
            DrawMarkerPopup(hoveredMarker, transitionAlpha);
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

        private static void DrawMarkerPopup(SiSquadMapMarker marker, float transitionAlpha)
        {
            if (marker == null)
                return;

            var screenPosition = MyGuiManager.MouseCursorPosition + HoverPopupOffset;
            var title = marker.Name;
            if (!string.IsNullOrWhiteSpace(title))
                DrawPopupLine(screenPosition, title, new Color(0.9f, 1f, 0.9f, transitionAlpha), MarkerTitleScale);

            if (string.IsNullOrWhiteSpace(marker.Description))
                return;

            var descriptionLines = marker.Description.Split(PopupLineBreaks, StringSplitOptions.RemoveEmptyEntries);
            var linePosition = screenPosition + new Vector2(0f, 18f);
            for (var i = 0; i < descriptionLines.Length; i++)
            {
                DrawPopupLine(linePosition, descriptionLines[i], new Color(0.8f, 0.95f, 0.8f, transitionAlpha), MarkerDescriptionScale);
                linePosition += new Vector2(0f, 16f);
            }
        }

        private static void DrawPopupLine(Vector2 screenPosition, string text, Color color, float scale)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            MyRenderProxy.DrawString(
                MyGuiConstants.DEFAULT_FONT,
                screenPosition + HoverTextShadowOffset,
                new Color(0f, 0f, 0f, color.A / 255f * 0.8f),
                text,
                scale,
                float.PositiveInfinity,
                null,
                SpriteBatchMode.Default);
            MyRenderProxy.DrawString(
                MyGuiConstants.DEFAULT_FONT,
                screenPosition,
                color,
                text,
                scale,
                float.PositiveInfinity,
                null,
                SpriteBatchMode.Default);
        }
    }
}
