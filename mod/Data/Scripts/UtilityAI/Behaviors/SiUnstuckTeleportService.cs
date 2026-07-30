using System;
using Sandbox.ModAPI;
using VRage.Entities.Gravity;
using VRage.Game.Entity;
using VRage.ModAPI;
using VRageMath;

namespace Si.UtilityAI
{
    public static class SiUnstuckTeleportService
    {
        public sealed class State
        {
            public long StuckMilliseconds;
            public long RetryAfterMilliseconds = -1;

            public void Reset()
            {
                StuckMilliseconds = 0;
                RetryAfterMilliseconds = -1;
            }
        }

        public struct Settings
        {
            public int StuckTimeoutMilliseconds;
            public float MaximumPlanarSpeed;
            public float MinimumRemainingDistance;
            public float MinimumTeleportDistance;
            public float MaximumTeleportDistance;
            public float VerticalProbeHeight;
            public float VerticalProbeDepth;
            public float TeleportClearance;
            public float MinimumGroundUpDot;
            public int RetryCooldownMilliseconds;

            public static Settings CreateDefault()
            {
                return new Settings
                {
                    StuckTimeoutMilliseconds = 2500,
                    MaximumPlanarSpeed = 0.2f,
                    MinimumRemainingDistance = 1.5f,
                    MinimumTeleportDistance = 2.5f,
                    MaximumTeleportDistance = 7f,
                    VerticalProbeHeight = 4f,
                    VerticalProbeDepth = 10f,
                    TeleportClearance = 0.35f,
                    MinimumGroundUpDot = 0.6f,
                    RetryCooldownMilliseconds = 250,
                };
            }
        }

        private static readonly Random AttemptRandom = new Random();
        private static readonly object AttemptRandomLock = new object();

        public static bool TryUnstuckToWaypoint(
            MyEntity entity,
            in Vector3D position,
            in Vector3D velocity,
            in Vector3D waypoint,
            long elapsedSinceLastEvaluation,
            long nowMilliseconds,
            Settings settings,
            State state)
        {
            if (entity == null || state == null)
                return false;

            var remainingDistanceSquared = Vector3D.DistanceSquared(position, waypoint);
            if (remainingDistanceSquared <= settings.MinimumRemainingDistance * settings.MinimumRemainingDistance)
            {
                state.Reset();
                return false;
            }

            if (ResolvePlanarSpeed(velocity, position, entity.WorldMatrix.Up) <= settings.MaximumPlanarSpeed)
                state.StuckMilliseconds = Math.Min(
                    Math.Max(0, state.StuckMilliseconds + elapsedSinceLastEvaluation),
                    settings.StuckTimeoutMilliseconds);
            else
                state.StuckMilliseconds = 0;

            if (state.StuckMilliseconds < settings.StuckTimeoutMilliseconds)
                return false;
            if (state.RetryAfterMilliseconds > nowMilliseconds)
                return false;

            state.RetryAfterMilliseconds = nowMilliseconds + settings.RetryCooldownMilliseconds;
            if (!TryTeleportToEscapePoint(entity, position, settings))
                return false;

            state.Reset();
            return true;
        }

        private static bool TryTeleportToEscapePoint(
            MyEntity entity,
            in Vector3D position,
            Settings settings)
        {
            var up = ResolveUp(position, entity.WorldMatrix.Up);
            var world = entity.WorldMatrix;
            var forward = SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(
                Vector3D.Reject(world.Forward, up),
                Vector3D.CalculatePerpendicularVector(up));
            var right = SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(
                Vector3D.Cross(forward, up),
                world.Right);

            double angle;
            double distance;
            lock (AttemptRandomLock)
            {
                angle = AttemptRandom.NextDouble() * Math.PI * 2d;
                distance = MathHelper.Lerp(
                    settings.MinimumTeleportDistance,
                    settings.MaximumTeleportDistance,
                    (float)AttemptRandom.NextDouble());
            }

            var direction = SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(
                forward * Math.Cos(angle) + right * Math.Sin(angle),
                forward);
            var probeCenter = position + direction * distance;
            var rayStart = probeCenter + up * settings.VerticalProbeHeight;
            var rayEnd = probeCenter - up * settings.VerticalProbeDepth;

            IHitInfo hit;
            if (!MyAPIGateway.Physics.CastRay(rayStart, rayEnd, out hit) || hit == null)
                return false;

            var hitUp = SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback((Vector3D)hit.Normal, up);
            if (Vector3D.Dot(hitUp, up) < settings.MinimumGroundUpDot)
                return false;

            var landingPosition = hit.Position + up * settings.TeleportClearance;
            entity.PositionComp.WorldMatrix = MatrixD.CreateWorld(landingPosition, forward, up);
            if (entity.Physics != null)
            {
                entity.Physics.LinearVelocity = Vector3.Zero;
                entity.Physics.AngularVelocity = Vector3.Zero;
            }

            return true;
        }

        private static double ResolvePlanarSpeed(in Vector3D velocity, in Vector3D position, in Vector3D fallbackUp)
        {
            var up = ResolveUp(position, fallbackUp);
            var planarVelocity = Vector3D.Reject(velocity, up);
            return planarVelocity.Length();
        }

        private static Vector3D ResolveUp(in Vector3D position, in Vector3D fallbackUp)
        {
            var gravity = MyGravityProviderSystem.CalculateTotalGravityInPoint(position);
            if (gravity.LengthSquared() > 0.0001)
                return -Vector3D.Normalize(gravity);

            return SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(fallbackUp, Vector3D.Up);
        }
    }
}
