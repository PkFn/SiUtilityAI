using System.Collections.Generic;
using Medieval.WorldEnvironment.Modules;
using VRageMath;

namespace Si.UtilityAI
{
    internal sealed class SiNearbyCoverScanner
    {
        public void Scan(in Vector3D position, double radius, List<Vector3D> results)
        {
            results?.Clear();
            if (results == null
                || radius <= 0
                || MyFoliageRaycastEnvironmentModule.FoliageSectors == null)
                return;

            var radiusSquared = radius * radius;
            var sectors = MyFoliageRaycastEnvironmentModule.FoliageSectors;
            for (var i = 0; i < sectors.Count; i++)
            {
                var sector = sectors[i];
                if (sector?.Foliage == null || !IntersectsSphere(sector.BoundingBox, position, radius))
                    continue;

                foreach (var foliage in sector.Foliage)
                {
                    var candidate = (Vector3D)foliage.Value;
                    if (Vector3D.DistanceSquared(position, candidate) > radiusSquared)
                        continue;

                    results.Add(candidate);
                }
            }
        }

        private static bool IntersectsSphere(BoundingBoxD box, in Vector3D center, double radius)
        {
            var dx = AxisDistance(center.X, box.Min.X, box.Max.X);
            var dy = AxisDistance(center.Y, box.Min.Y, box.Max.Y);
            var dz = AxisDistance(center.Z, box.Min.Z, box.Max.Z);
            return dx * dx + dy * dy + dz * dz <= radius * radius;
        }

        private static double AxisDistance(double value, double min, double max)
        {
            if (value < min)
                return min - value;
            if (value > max)
                return value - max;
            return 0;
        }
    }
}
