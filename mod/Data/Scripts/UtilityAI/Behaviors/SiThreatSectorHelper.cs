using System;
using VRageMath;

namespace Si.UtilityAI
{
    internal static class SiThreatSectorHelper
    {
        internal static double ClampFrontExclusionAngleDegrees(double degrees)
        {
            return MathHelper.Clamp((float)degrees, 0f, 359f);
        }

        internal static bool TryGetPlanarDirection(
            in Vector3D from,
            in Vector3D to,
            in Vector3D up,
            out Vector3D direction)
        {
            direction = Vector3D.Reject(to - from, up);
            var lengthSquared = direction.LengthSquared();
            if (lengthSquared <= 0.0001)
            {
                direction = Vector3D.Zero;
                return false;
            }

            direction /= Math.Sqrt(lengthSquared);
            return true;
        }

        internal static bool IsInsideFrontExclusionSector(
            in Vector3D origin,
            in Vector3D candidatePosition,
            in Vector3D frontDirection,
            in Vector3D up,
            double sectorAngleDegrees)
        {
            if (frontDirection.LengthSquared() <= 0.0001)
                return false;

            var clampedAngle = ClampFrontExclusionAngleDegrees(sectorAngleDegrees);
            if (clampedAngle <= 0)
                return false;

            Vector3D candidateDirection;
            if (!TryGetPlanarDirection(origin, candidatePosition, up, out candidateDirection))
                return false;

            var cosineThreshold = Math.Cos(MathHelper.ToRadians((float)(clampedAngle * 0.5)));
            return Vector3D.Dot(candidateDirection, frontDirection) >= cosineThreshold;
        }
    }
}
