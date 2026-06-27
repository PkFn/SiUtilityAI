using System;
using System.Collections.Generic;
using Medieval.WorldEnvironment.Modules;
using VRageMath;

namespace Si.UtilityAI
{
    internal struct SiNearbyEnvironmentSample
    {
        public double NearestBushDistance;
        public int NearbyBushCount;

        public bool HasBush => NearbyBushCount > 0;

        public void RegisterBush(double distance)
        {
            if (distance < 0)
                distance = 0;

            if (NearbyBushCount <= 0 || distance < NearestBushDistance)
                NearestBushDistance = distance;
            NearbyBushCount++;
        }
    }

    internal interface ISiEnvironmentProbe
    {
        void Sample(in Vector3D position, double radius, ref SiNearbyEnvironmentSample sample);
    }

    internal sealed class SiNearbyEnvironmentScanner
    {
        private static readonly ISiEnvironmentProbe[] Probes =
        {
            new SiFoliageEnvironmentProbe(),
        };

        public SiNearbyEnvironmentSample Scan(in Vector3D position, double radius)
        {
            var sample = default(SiNearbyEnvironmentSample);
            if (radius <= 0)
                return sample;

            for (var i = 0; i < Probes.Length; i++)
                Probes[i].Sample(position, radius, ref sample);
            return sample;
        }
    }

    internal sealed class SiFoliageEnvironmentProbe : ISiEnvironmentProbe
    {
        public void Sample(in Vector3D position, double radius, ref SiNearbyEnvironmentSample sample)
        {
            if (radius <= 0 || MyFoliageRaycastEnvironmentModule.FoliageSectors == null)
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
                    var distanceSquared = Vector3D.DistanceSquared(position, foliage.Value);
                    if (distanceSquared > radiusSquared)
                        continue;

                    sample.RegisterBush(Math.Sqrt(distanceSquared));
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
