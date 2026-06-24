using System;
using System.Xml.Serialization;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Entities.Gravity;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace Si.UtilityAI
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiGroundedNpcControllerComponent : MyObjectBuilder_EntityComponent
    {
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiGroundedNpcControllerComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public double MoveSpeed;
        public double Acceleration;
        public double BrakingAcceleration;
        public double ArrivalRadius;
        public double StepHeight;
        public double GroundProbeDistance;
        public double GroundOffset;
        public double ObstacleProbeHeight;
        public double CollisionRadius;
        public double MaximumFallSpeed;
        public bool ModelFacesBackward;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiGroundedNpcControllerComponentDefinition))]
    public class SiGroundedNpcControllerComponentDefinition : MyEntityComponentDefinition
    {
        public double MoveSpeed { get; private set; }
        public double Acceleration { get; private set; }
        public double BrakingAcceleration { get; private set; }
        public double ArrivalRadius { get; private set; }
        public double StepHeight { get; private set; }
        public double GroundProbeDistance { get; private set; }
        public double GroundOffset { get; private set; }
        public double ObstacleProbeHeight { get; private set; }
        public double CollisionRadius { get; private set; }
        public double MaximumFallSpeed { get; private set; }
        public bool ModelFacesBackward { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiGroundedNpcControllerComponentDefinition)builder;
            MoveSpeed = Math.Max(0, ob.MoveSpeed);
            Acceleration = Math.Max(0, ob.Acceleration);
            BrakingAcceleration = Math.Max(0, ob.BrakingAcceleration);
            ArrivalRadius = Math.Max(0, ob.ArrivalRadius);
            StepHeight = Math.Max(0, ob.StepHeight);
            GroundProbeDistance = Math.Max(0, ob.GroundProbeDistance);
            GroundOffset = Math.Max(0, ob.GroundOffset);
            ObstacleProbeHeight = Math.Max(0, ob.ObstacleProbeHeight);
            CollisionRadius = Math.Max(0, ob.CollisionRadius);
            MaximumFallSpeed = Math.Max(0, ob.MaximumFallSpeed);
            ModelFacesBackward = ob.ModelFacesBackward;
        }
    }

    /// <summary>
    /// Data-only controller selected by an entity container.  Locomotion tuning
    /// belongs to its component definition so NPC archetypes do not need C#
    /// subclasses merely to change movement values.
    /// </summary>
    [MyComponent(typeof(MyObjectBuilder_SiGroundedNpcControllerComponent))]
    [MyDefinitionRequired(typeof(SiGroundedNpcControllerComponentDefinition))]
    public class SiGroundedNpcControllerComponent : MyEntityComponent
    {
        public SiGroundedNpcControllerComponentDefinition Definition { get; private set; }

        public override bool IsSerialized => false;

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            Definition = (SiGroundedNpcControllerComponentDefinition)definition;
        }
    }

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
            var definition = GetControllerDefinition();
            var world = Entity.WorldMatrix;
            var position = world.Translation;
            var gravity = (Vector3D)MyGravityProviderSystem.CalculateTotalGravityInPoint(position);
            var up = gravity.LengthSquared() > MinimumDirectionLengthSquared
                ? -Vector3D.Normalize(gravity)
                : NormalizedOrFallback(world.Up, Vector3D.Up);

            _horizontalVelocity = Vector3D.Reject(_horizontalVelocity, up);
            var desiredVelocity = CalculateDesiredVelocity(position, up, definition);
            var acceleration = desiredVelocity.LengthSquared() > MinimumDirectionLengthSquared
                ? definition.Acceleration
                : definition.BrakingAcceleration;
            _horizontalVelocity = MoveTowards(
                _horizontalVelocity,
                desiredVelocity,
                acceleration * deltaSeconds);

            var horizontalDisplacement = _horizontalVelocity * deltaSeconds;
            if (IsObstacleAhead(position, up, horizontalDisplacement, definition))
            {
                horizontalDisplacement = Vector3D.Zero;
                _horizontalVelocity = Vector3D.Zero;
            }

            if (IsGrounded)
                _verticalVelocity = Vector3D.Zero;
            else
                _verticalVelocity += gravity * deltaSeconds;

            var fallSpeed = Vector3D.Dot(_verticalVelocity, -up);
            if (fallSpeed > definition.MaximumFallSpeed)
                _verticalVelocity += up * (fallSpeed - definition.MaximumFallSpeed);

            var horizontalPosition = position + horizontalDisplacement;
            var desiredPosition = horizontalPosition + _verticalVelocity * deltaSeconds;
            if (TryFindGround(horizontalPosition, desiredPosition, up, definition, out var hit))
            {
                desiredPosition = hit.Position + up * definition.GroundOffset;
                _verticalVelocity = Vector3D.Zero;
                IsGrounded = true;
                GroundEntityId = hit.HitEntity?.EntityId;
            }
            else
            {
                IsGrounded = false;
                GroundEntityId = null;
            }

            var facing = CalculateFacing(world, up, desiredVelocity, definition);
            var modelForward = definition.ModelFacesBackward ? -facing : facing;
            Entity.WorldMatrix = MatrixD.CreateWorld(desiredPosition, modelForward, up);
        }

        private Vector3D CalculateDesiredVelocity(
            in Vector3D position,
            in Vector3D up,
            SiGroundedNpcControllerComponentDefinition definition)
        {
            if (!HasWaypoint)
                return Vector3D.Zero;

            var toWaypoint = Vector3D.Reject(_waypoint - position, up);
            var distanceSquared = toWaypoint.LengthSquared();
            if (distanceSquared <= definition.ArrivalRadius * definition.ArrivalRadius)
            {
                var reachedWaypoint = _waypoint;
                HasWaypoint = false;
                OnWaypointReached(reachedWaypoint);
                return Vector3D.Zero;
            }

            return toWaypoint / Math.Sqrt(distanceSquared) * definition.MoveSpeed;
        }

        private bool IsObstacleAhead(
            in Vector3D position,
            in Vector3D up,
            in Vector3D horizontalDisplacement,
            SiGroundedNpcControllerComponentDefinition definition)
        {
            var distanceSquared = horizontalDisplacement.LengthSquared();
            if (distanceSquared <= MinimumDirectionLengthSquared)
                return false;

            var direction = horizontalDisplacement / Math.Sqrt(distanceSquared);
            var start = position + up * definition.ObstacleProbeHeight;
            var end = start + horizontalDisplacement + direction * definition.CollisionRadius;
            return MyAPIGateway.Physics.CastRay(start, end, out var hit)
                && hit.HitEntity != Entity;
        }

        private bool TryFindGround(
            in Vector3D horizontalPosition,
            in Vector3D desiredPosition,
            in Vector3D up,
            SiGroundedNpcControllerComponentDefinition definition,
            out IHitInfo hit)
        {
            var start = horizontalPosition + up * definition.StepHeight;
            var end = desiredPosition - up * definition.GroundProbeDistance;
            return MyAPIGateway.Physics.CastRay(start, end, out hit)
                && hit.HitEntity != Entity;
        }

        private Vector3D CalculateFacing(
            in MatrixD world,
            in Vector3D up,
            in Vector3D desiredVelocity,
            SiGroundedNpcControllerComponentDefinition definition)
        {
            var facing = desiredVelocity.LengthSquared() > MinimumDirectionLengthSquared
                ? desiredVelocity
                : definition.ModelFacesBackward ? world.Backward : world.Forward;
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

        private SiGroundedNpcControllerComponentDefinition GetControllerDefinition()
        {
            var controller = Entity.Components.Get<SiGroundedNpcControllerComponent>();
            if (controller?.Definition == null)
                throw new InvalidOperationException(
                    $"Grounded NPC '{EntityDefinition}' requires a {nameof(SiGroundedNpcControllerComponent)}.");
            return controller.Definition;
        }
    }
}
