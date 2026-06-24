using System;
using Sandbox.ModAPI;
using VRage.Entities.Gravity;
using VRage.ModAPI;
using VRageMath;

namespace Si.UtilityAI
{
    /// <summary>
    /// Contract exposed to behavior systems which can give an NPC a world-space
    /// destination.  A waypoint is a steering target, not a generated path.
    /// </summary>
    public interface ISiWaypointMover
    {
        bool HasWaypoint { get; }
        Vector3D Waypoint { get; }

        void SetWaypoint(in Vector3D waypoint);
        void ClearWaypoint();
    }

    /// <summary>
    /// Reusable kinematic locomotion for NPCs which walk under natural/artificial
    /// gravity.  Downward physics probes recognize both voxel terrain and grids.
    /// </summary>
    public abstract class SiGroundedNpc : SiNpc, ISiWaypointMover
    {
        private const double MinimumDirectionLengthSquared = 0.0001;

        private Vector3D _horizontalVelocity;
        private Vector3D _verticalVelocity;
        private Vector3D _waypoint;

        protected SiGroundedNpc(long entityId, in MatrixD transform)
            : base(entityId, transform)
        {
        }

        public bool HasWaypoint { get; private set; }
        public Vector3D Waypoint => _waypoint;
        public Vector3D Velocity => _horizontalVelocity + _verticalVelocity;
        public bool IsGrounded { get; private set; }
        public long? GroundEntityId { get; private set; }

        protected virtual double MoveSpeed => 2.5;
        protected virtual double Acceleration => 10;
        protected virtual double BrakingAcceleration => 16;
        protected virtual double ArrivalRadius => 0.25;
        protected virtual double StepHeight => 0.45;
        protected virtual double GroundProbeDistance => 0.2;
        protected virtual double GroundOffset => 0.02;
        protected virtual double ObstacleProbeHeight => 0.8;
        protected virtual double CollisionRadius => 0.25;
        protected virtual double MaximumFallSpeed => 50;

        /// <summary>
        /// Some character models are authored facing local backward.  Such NPCs
        /// can override this while locomotion continues to use logical forward.
        /// </summary>
        protected virtual bool ModelFacesBackward => false;

        public void SetWaypoint(in Vector3D waypoint)
        {
            _waypoint = waypoint;
            HasWaypoint = true;
        }

        public void ClearWaypoint()
        {
            HasWaypoint = false;
        }

        protected sealed override void OnUpdate(long elapsedMilliseconds)
        {
            UpdateBehavior(elapsedMilliseconds);

            var deltaSeconds = Math.Min(elapsedMilliseconds / 1000.0, 0.25);
            if (deltaSeconds <= 0)
                return;

            UpdateLocomotion(deltaSeconds);
        }

        /// <summary>
        /// Called before locomotion so a behavior can choose or replace its
        /// waypoint and have that choice take effect in the same update.
        /// </summary>
        protected virtual void UpdateBehavior(long elapsedMilliseconds)
        {
        }

        protected virtual void OnWaypointReached(in Vector3D waypoint)
        {
        }

        private void UpdateLocomotion(double deltaSeconds)
        {
            var world = Entity.WorldMatrix;
            var position = world.Translation;
            var gravity = (Vector3D)MyGravityProviderSystem.CalculateTotalGravityInPoint(position);
            var up = gravity.LengthSquared() > MinimumDirectionLengthSquared
                ? -Vector3D.Normalize(gravity)
                : NormalizedOrFallback(world.Up, Vector3D.Up);

            _horizontalVelocity = Vector3D.Reject(_horizontalVelocity, up);
            var desiredVelocity = CalculateDesiredVelocity(position, up);
            var acceleration = desiredVelocity.LengthSquared() > MinimumDirectionLengthSquared
                ? Acceleration
                : BrakingAcceleration;
            _horizontalVelocity = MoveTowards(
                _horizontalVelocity,
                desiredVelocity,
                acceleration * deltaSeconds);

            var horizontalDisplacement = _horizontalVelocity * deltaSeconds;
            if (IsObstacleAhead(position, up, horizontalDisplacement))
            {
                horizontalDisplacement = Vector3D.Zero;
                _horizontalVelocity = Vector3D.Zero;
            }

            if (IsGrounded)
                _verticalVelocity = Vector3D.Zero;
            else
                _verticalVelocity += gravity * deltaSeconds;

            var fallSpeed = Vector3D.Dot(_verticalVelocity, -up);
            if (fallSpeed > MaximumFallSpeed)
                _verticalVelocity += up * (fallSpeed - MaximumFallSpeed);

            var horizontalPosition = position + horizontalDisplacement;
            var desiredPosition = horizontalPosition + _verticalVelocity * deltaSeconds;
            if (TryFindGround(horizontalPosition, desiredPosition, up, out var hit))
            {
                desiredPosition = hit.Position + up * GroundOffset;
                _verticalVelocity = Vector3D.Zero;
                IsGrounded = true;
                GroundEntityId = hit.HitEntity?.EntityId;
            }
            else
            {
                IsGrounded = false;
                GroundEntityId = null;
            }

            var facing = CalculateFacing(world, up, desiredVelocity);
            var modelForward = ModelFacesBackward ? -facing : facing;
            Entity.WorldMatrix = MatrixD.CreateWorld(desiredPosition, modelForward, up);
        }

        private Vector3D CalculateDesiredVelocity(in Vector3D position, in Vector3D up)
        {
            if (!HasWaypoint)
                return Vector3D.Zero;

            var toWaypoint = Vector3D.Reject(_waypoint - position, up);
            var distanceSquared = toWaypoint.LengthSquared();
            if (distanceSquared <= ArrivalRadius * ArrivalRadius)
            {
                var reachedWaypoint = _waypoint;
                HasWaypoint = false;
                OnWaypointReached(reachedWaypoint);
                return Vector3D.Zero;
            }

            return toWaypoint / Math.Sqrt(distanceSquared) * MoveSpeed;
        }

        private bool IsObstacleAhead(
            in Vector3D position,
            in Vector3D up,
            in Vector3D horizontalDisplacement)
        {
            var distanceSquared = horizontalDisplacement.LengthSquared();
            if (distanceSquared <= MinimumDirectionLengthSquared)
                return false;

            var direction = horizontalDisplacement / Math.Sqrt(distanceSquared);
            var start = position + up * ObstacleProbeHeight;
            var end = start + horizontalDisplacement + direction * CollisionRadius;
            return MyAPIGateway.Physics.CastRay(start, end, out var hit)
                && hit.HitEntity != Entity;
        }

        private bool TryFindGround(
            in Vector3D horizontalPosition,
            in Vector3D desiredPosition,
            in Vector3D up,
            out IHitInfo hit)
        {
            var start = horizontalPosition + up * StepHeight;
            var end = desiredPosition - up * GroundProbeDistance;
            return MyAPIGateway.Physics.CastRay(start, end, out hit)
                && hit.HitEntity != Entity;
        }

        private Vector3D CalculateFacing(
            in MatrixD world,
            in Vector3D up,
            in Vector3D desiredVelocity)
        {
            var facing = desiredVelocity.LengthSquared() > MinimumDirectionLengthSquared
                ? desiredVelocity
                : ModelFacesBackward ? world.Backward : world.Forward;
            facing = Vector3D.Reject(facing, up);
            return NormalizedOrFallback(facing, Vector3D.CalculatePerpendicularVector(up));
        }

        private static Vector3D MoveTowards(
            in Vector3D current,
            in Vector3D target,
            double maximumDelta)
        {
            var delta = target - current;
            var distanceSquared = delta.LengthSquared();
            if (distanceSquared <= maximumDelta * maximumDelta)
                return target;

            return current + delta / Math.Sqrt(distanceSquared) * maximumDelta;
        }

        private static Vector3D NormalizedOrFallback(
            in Vector3D value,
            in Vector3D fallback)
        {
            var lengthSquared = value.LengthSquared();
            return lengthSquared > MinimumDirectionLengthSquared
                ? value / Math.Sqrt(lengthSquared)
                : fallback;
        }
    }
}
