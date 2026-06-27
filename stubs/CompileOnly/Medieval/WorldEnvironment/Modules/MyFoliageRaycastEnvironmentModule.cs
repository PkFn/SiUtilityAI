using System.Collections.Generic;
using VRageMath;

namespace Medieval.WorldEnvironment.Modules
{
    public static class MyFoliageRaycastEnvironmentModule
    {
        public static List<FoliageSector> FoliageSectors;

        public class FoliageSector
        {
            public long SectorId;
            public BoundingBoxD BoundingBox;
            public Dictionary<int, Vector3> Foliage;
        }
    }
}
