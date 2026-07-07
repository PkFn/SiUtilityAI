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
        private const float MarkerTextScale = 0.55f;
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
                DrawMarkerIcon(mapPosition, marker.StyleId, transitionAlpha);
                DrawMarkerLabel(mapPosition, marker.Name, transitionAlpha);
            }
        }

        private void DrawMarkerIcon(Vector2 mapPosition, string styleId, float transitionAlpha)
        {
            if (!_markerImages.TryGetValue(styleId ?? string.Empty, out var texture) || string.IsNullOrWhiteSpace(texture))
                _markerImages.TryGetValue("default", out texture);
            if (string.IsNullOrWhiteSpace(texture))
                return;

            MyRenderProxy.DrawSprite(
                texture,
                mapPosition,
                Vector2.Zero,
                new Color(1f, 1f, 1f, transitionAlpha),
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

        private static void DrawMarkerLabel(Vector2 mapPosition, string label, float transitionAlpha)
        {
            if (string.IsNullOrWhiteSpace(label))
                return;

            var screenPosition = MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(mapPosition + new Vector2(0.008f, -0.006f));
            MyRenderProxy.DrawString(
                MyGuiConstants.DEFAULT_FONT,
                screenPosition,
                new Color(0.8f, 1f, 0.8f, transitionAlpha),
                label,
                MarkerTextScale,
                float.PositiveInfinity);
        }
    }
}
