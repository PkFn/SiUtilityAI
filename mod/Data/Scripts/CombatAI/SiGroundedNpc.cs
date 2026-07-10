using System;
using System.Xml.Serialization;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.ModAPI;
using SiCore.Core.Debug;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Entities.Gravity;
using VRage.ObjectBuilders;
using VRageMath;
using VRageRender.Animations;

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
        public double ArrivalRadius;
        public bool ModelFacesBackward;
        public bool WantsWalk;
        public bool WantsSprint;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiGroundedNpcControllerComponentDefinition))]
    public class SiGroundedNpcControllerComponentDefinition : MyEntityComponentDefinition
    {
        public double ArrivalRadius { get; private set; }
        public bool ModelFacesBackward { get; private set; }
        public bool WantsWalk { get; private set; }
        public bool WantsSprint { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiGroundedNpcControllerComponentDefinition)builder;
            ArrivalRadius = Math.Max(0, ob.ArrivalRadius);
            ModelFacesBackward = ob.ModelFacesBackward;
            WantsWalk = ob.WantsWalk;
            WantsSprint = ob.WantsSprint;
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

    public interface ISiPostureController
    {
        bool WantsCrouch { get; }

        void SetCrouch(bool wantsCrouch);
    }

    /// <summary>
    /// Reusable waypoint locomotion for NPCs driven by the stock
    /// character movement component.
    /// </summary>
    public abstract class SiGroundedNpc : SiNpc, ISiWaypointMover, ISiPostureController
    {
        private const double MinimumDirectionLengthSquared = 0.0001;

        private Vector3D _waypoint;
        private MyCharacterMovementComponent _movement;
        private bool _movementHandlersRegistered;
        private bool _wantsCrouch;
        private bool _hasLoggedMovementState;
        private bool _lastLoggedRequestedCrouch;
        private bool _lastLoggedWantsCrouch;
        private bool _lastLoggedIsCrouching;
        private bool _lastLoggedTryCrouchAccepted;
        private bool _hasLoggedTryCrouchAccepted;
        private string _lastLoggedMovementState;
        private readonly SiGameLog _log = new SiGameLog(nameof(SiGroundedNpc), "[SiGroundedNpc]");

        protected SiGroundedNpc(long entityId, in MatrixD transform)
            : base(entityId, transform)
        {
        }

        public bool HasWaypoint { get; private set; }
        public Vector3D Waypoint => _waypoint;
        public Vector3D Velocity => Entity?.Physics?.LinearVelocity ?? Vector3.Zero;
        public bool WantsCrouch => _wantsCrouch;

        public void SetWaypoint(in Vector3D waypoint)
        {
            _waypoint = waypoint;
            HasWaypoint = true;
        }

        public void ClearWaypoint()
        {
            HasWaypoint = false;
        }

        public void SetCrouch(bool wantsCrouch)
        {
            if (_wantsCrouch == wantsCrouch)
                return;

            _wantsCrouch = wantsCrouch;
            var tryCrouchAccepted = _movement != null && _movement.TryCrouch(wantsCrouch);
            _log.Info($"entityId={Entity?.EntityId ?? EntityId} name={Entity?.Name ?? "null"} definition={EntityDefinition.SubtypeName} branch=posture-request requestedCrouch={wantsCrouch} tryCrouchAccepted={tryCrouchAccepted}"); // AGENT-DEBUG-LOG
        }

        protected sealed override void OnUpdate(long elapsedMilliseconds)
        {
            UpdateBehavior(elapsedMilliseconds);
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

        protected override void OnActivated()
        {
            base.OnActivated();
            _movement = Entity?.Components.Get<MyCharacterMovementComponent>();
            if (_movement == null)
                throw new InvalidOperationException(
                    $"Grounded NPC '{EntityDefinition}' requires a {nameof(MyCharacterMovementComponent)}.");

            var animation = Entity.Components.Get<MyCharacterAnimationControllerComponent>();
            _log.Info($"entityId={Entity?.EntityId ?? EntityId} name={Entity?.Name ?? "null"} definition={EntityDefinition.SubtypeName} branch=activated movement={(_movement != null)} physics={(Entity?.Physics != null)} animation={(animation != null)} animationController={(animation?.Controller != null)} source={animation?.SourceId}"); // AGENT-DEBUG-LOG

            _movement.MovementIndicatorHandler += MovementIndicatorHandler;
            _movement.RotationIndicatorHandler += RotationIndicatorHandler;
            _movement.OnPostProcessPhysicalMovement += PostProcessPhysicalMovement;
            _movementHandlersRegistered = true;
        }

        protected override void OnKilled()
        {
            base.OnKilled();
            ClearWaypoint();
            _wantsCrouch = false;
            if (_movement != null)
                _movement.BlockMovement = true;
        }

        protected override void OnClosing()
        {
            if (_movementHandlersRegistered && _movement != null)
            {
                _movement.MovementIndicatorHandler -= MovementIndicatorHandler;
                _movement.RotationIndicatorHandler -= RotationIndicatorHandler;
                _movement.OnPostProcessPhysicalMovement -= PostProcessPhysicalMovement;
            }

            base.OnClosing();
            _movement = null;
            _movementHandlersRegistered = false;
            _wantsCrouch = false;
        }

        private bool TryGetMoveDirection(
            out Vector3D direction,
            SiGroundedNpcControllerComponentDefinition definition)
        {
            direction = Vector3D.Zero;
            if (!HasWaypoint || Entity == null)
                return false;

            var world = Entity.WorldMatrix;
            var position = world.Translation;
            var gravity = (Vector3D)MyGravityProviderSystem.CalculateTotalGravityInPoint(position);
            var up = gravity.LengthSquared() > MinimumDirectionLengthSquared
                ? -Vector3D.Normalize(gravity)
                : NormalizedOrFallback(world.Up, Vector3D.Up);
            var toWaypoint = Vector3D.Reject(_waypoint - position, up);
            var distanceSquared = toWaypoint.LengthSquared();
            if (distanceSquared <= definition.ArrivalRadius * definition.ArrivalRadius)
            {
                var reachedWaypoint = _waypoint;
                HasWaypoint = false;
                OnWaypointReached(reachedWaypoint);
                return false;
            }

            direction = toWaypoint / Math.Sqrt(distanceSquared);
            return true;
        }

        private void MovementIndicatorHandler(
            MyCharacterMovementComponent movement,
            ref Vector3 moveIndicator)
        {
            // TryCrouch updates the desired modifier consumed by the movement
            // state calculation immediately after this callback.  Calling it
            // from post-process is too late for the current animation state,
            // especially when the NPC is stationary.
            var tryCrouchAccepted = movement.TryCrouch(_wantsCrouch);
            LogMovementState(movement, "movement-callback", tryCrouchAccepted);

            if (IsDead)
            {
                moveIndicator = Vector3.Zero;
                return;
            }

            var definition = GetControllerDefinition();
            if (!TryGetMoveDirection(out var direction, definition))
            {
                moveIndicator = Vector3.Zero;
                return;
            }

            var localDirection = Vector3D.TransformNormal(
                direction,
                Entity.PositionComp.WorldMatrixNormalizedInv);
            moveIndicator = (Vector3)localDirection;
            if (_wantsCrouch)
                moveIndicator.Y = -1f;
        }

        private void RotationIndicatorHandler(
            MyCharacterMovementComponent _,
            ref Vector2 rotationIndicator,
            ref Vector3? forcedForward)
        {
            var definition = GetControllerDefinition();
            if (!TryGetMoveDirection(out var direction, definition))
                return;

            var gravity = (Vector3D)(-MyGravityProviderSystem.CalculateTotalGravityInPoint(Entity.GetPosition()));
            if (gravity.LengthSquared() <= MinimumDirectionLengthSquared)
                gravity = Vector3D.Up;
            gravity.Normalize();

            if (definition.ModelFacesBackward)
                direction = -direction;

            Vector3D.Cross(ref direction, ref gravity, out var right);
            Vector3D.Cross(ref gravity, ref right, out var forward);
            if (forward.Normalize() < 1e-2f)
                return;

            forcedForward = (Vector3)forward;
        }

        private void PostProcessPhysicalMovement(
            MyCharacterMovementComponent cmp,
            ref MatrixD transform)
        {
            var definition = GetControllerDefinition();
            cmp.WantsWalk = definition.WantsWalk;
            cmp.WantsSprint = definition.WantsSprint;
            cmp.BlockMovement = IsDead;
            LogMovementState(cmp, "post-process", null);
        }

        private void LogMovementState(
            MyCharacterMovementComponent movement,
            string branch,
            bool? tryCrouchAccepted)
        {
            var state = movement.GetMovementState().ToString();
            var wantsCrouch = movement.WantsCrouch;
            var isCrouching = movement.IsCrouching;
            var animation = Entity?.Components.Get<MyCharacterAnimationControllerComponent>();
            var animationCrouch = 0f;
            var hasAnimationCrouch = animation?.Variables?.GetValue(
                MyAnimationVariableStorageHints.StrIdCrouch,
                out animationCrouch) ?? false;
            if (_hasLoggedMovementState
                && _lastLoggedRequestedCrouch == _wantsCrouch
                && _lastLoggedWantsCrouch == wantsCrouch
                && _lastLoggedIsCrouching == isCrouching
                && _lastLoggedMovementState == state
                && (!tryCrouchAccepted.HasValue
                    || (_hasLoggedTryCrouchAccepted
                        && _lastLoggedTryCrouchAccepted == tryCrouchAccepted.Value)))
                return;

            _hasLoggedMovementState = true;
            _lastLoggedRequestedCrouch = _wantsCrouch;
            _lastLoggedWantsCrouch = wantsCrouch;
            _lastLoggedIsCrouching = isCrouching;
            _lastLoggedMovementState = state;
            if (tryCrouchAccepted.HasValue)
            {
                _lastLoggedTryCrouchAccepted = tryCrouchAccepted.Value;
                _hasLoggedTryCrouchAccepted = true;
            }
            _log.Warning($"entityId={Entity?.EntityId ?? EntityId} name={Entity?.Name ?? "null"} definition={EntityDefinition.SubtypeName} branch={branch} requestedCrouch={_wantsCrouch} tryCrouchAccepted={(tryCrouchAccepted.HasValue ? tryCrouchAccepted.Value.ToString() : "n/a")} wantsCrouch={wantsCrouch} isCrouching={isCrouching} state={state} falling={movement.IsFalling} physics={(Entity?.Physics != null)} animation={(animation != null)} animationController={(animation?.Controller != null)} animationCrouch={(hasAnimationCrouch ? animationCrouch.ToString() : "missing")}"); // AGENT-DEBUG-LOG
        }

        private static Vector3D NormalizedOrFallback(in Vector3D value, in Vector3D fallback)
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
