using System;
using System.Collections.Generic;
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

    public enum SiNpcMovementSpeed
    {
        Walk,
        Run,
        Sprint,
    }

    public interface ISiMovementSpeedController
    {
        SiNpcMovementSpeed CurrentMovementSpeed { get; }

        void SetSquadMovementSpeed(SiNpcMovementSpeed speed);
        void ClearSquadMovementSpeed();
        void SetCombatMovementSpeed(SiNpcMovementSpeed speed);
        void ClearCombatMovementSpeed();
    }

    /// <summary>
    /// Reusable waypoint locomotion for NPCs driven by the stock
    /// character movement component.
    /// </summary>
    public abstract class SiGroundedNpc : SiNpc, ISiWaypointMover, ISiPostureController, ISiMovementSpeedController
    {
        private const double MinimumDirectionLengthSquared = 0.0001;

        private Vector3D _waypoint;
        private MyCharacterMovementComponent _movement;
        private bool _movementHandlersRegistered;
        private bool _wantsCrouch;
        private SiNpcMovementSpeed? _squadMovementSpeed;
        private SiNpcMovementSpeed? _combatMovementSpeed;
        private readonly HashSet<string> _loggedMovementTransitions = new HashSet<string>();
        private readonly SiGameLog _log = new SiGameLog(nameof(SiGroundedNpc), "[SiGroundedNpc]");

        protected SiGroundedNpc(long entityId, in MatrixD transform)
            : base(entityId, transform)
        {
        }

        public bool HasWaypoint { get; private set; }
        public Vector3D Waypoint => _waypoint;
        public Vector3D Velocity => Entity?.Physics?.LinearVelocity ?? Vector3.Zero;
        public bool WantsCrouch => _wantsCrouch;
        public SiNpcMovementSpeed CurrentMovementSpeed
        {
            get
            {
                if (_movement != null)
                    return MovementSpeedFromMovement(_movement);

                var controller = Entity?.Components.Get<SiGroundedNpcControllerComponent>();
                return controller?.Definition != null
                    ? MovementSpeedFromDefinition(controller.Definition)
                    : SiNpcMovementSpeed.Run;
            }
        }

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
            // The utility brain runs before character movement.  Storing the
            // request here prevents a freshly restored NPC from changing its
            // movement modifier while its animation components are still
            // attaching.  The movement callback applies it in the frame where
            // the game calculates the animation state.
            LogAnimationState("posture-request", _movement, null);
        }

        public void SetSquadMovementSpeed(SiNpcMovementSpeed speed)
        {
            _squadMovementSpeed = speed;
            ApplyCurrentMovementSpeed();
        }

        public void ClearSquadMovementSpeed()
        {
            _squadMovementSpeed = null;
            ApplyCurrentMovementSpeed();
        }

        public void SetCombatMovementSpeed(SiNpcMovementSpeed speed)
        {
            _combatMovementSpeed = speed;
            ApplyCurrentMovementSpeed();
        }

        public void ClearCombatMovementSpeed()
        {
            _combatMovementSpeed = null;
            ApplyCurrentMovementSpeed();
        }

        protected sealed override void OnUpdate(long elapsedMilliseconds)
        {
            if (!TryGetControllerDefinition(out _))
                return;

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
            _loggedMovementTransitions.Clear();
            _squadMovementSpeed = null;
            _combatMovementSpeed = null;
            _movement = Entity?.Components.Get<MyCharacterMovementComponent>();
            if (_movement == null)
                throw new InvalidOperationException(
                    $"Grounded NPC '{EntityDefinition}' requires a {nameof(MyCharacterMovementComponent)}.");

            if (!TryGetControllerDefinition(out _))
                throw new InvalidOperationException(
                    $"Grounded NPC '{EntityDefinition}' requires a {nameof(SiGroundedNpcControllerComponent)}.");

            LogAnimationState("activated", _movement, null);

            _movement.MovementIndicatorHandler += MovementIndicatorHandler;
            _movement.RotationIndicatorHandler += RotationIndicatorHandler;
            _movement.OnPostProcessPhysicalMovement += PostProcessPhysicalMovement;
            _movement.OnMovementStateChanged += OnMovementStateChanged;
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
                _movement.OnMovementStateChanged -= OnMovementStateChanged;
            }

            base.OnClosing();
            _movement = null;
            _movementHandlersRegistered = false;
            _wantsCrouch = false;
            _squadMovementSpeed = null;
            _combatMovementSpeed = null;
            _loggedMovementTransitions.Clear();
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
            if (!TryGetControllerDefinition(out var definition))
            {
                moveIndicator = Vector3.Zero;
                movement.BlockMovement = true;
                return;
            }

            var tryCrouchAccepted = movement.TryCrouch(_wantsCrouch);
            ApplyMovementSpeed(movement, definition);
            if (IsDead)
            {
                moveIndicator = Vector3.Zero;
                if (!tryCrouchAccepted)
                    LogAnimationState("crouch-rejected-dead", movement, tryCrouchAccepted);
                return;
            }

            if (!TryGetMoveDirection(out var direction, definition))
            {
                moveIndicator = Vector3.Zero;
                if (!tryCrouchAccepted)
                    LogAnimationState("crouch-rejected-no-waypoint", movement, tryCrouchAccepted);
                return;
            }

            var localDirection = Vector3D.TransformNormal(
                direction,
                Entity.PositionComp.WorldMatrixNormalizedInv);
            // Formation bots rotate toward the checkpoint instead of using
            // lateral input. This prevents small heading errors from turning
            // into visible strafing.
            moveIndicator = new Vector3(0f, (float)localDirection.Y, (float)localDirection.Z);
            if (_wantsCrouch)
                moveIndicator.Y = -1f;

            if (!tryCrouchAccepted)
                LogAnimationState("crouch-rejected-steering", movement, tryCrouchAccepted);
        }

        private void RotationIndicatorHandler(
            MyCharacterMovementComponent _,
            ref Vector2 rotationIndicator,
            ref Vector3? forcedForward)
        {
            if (!TryGetControllerDefinition(out var definition))
                return;

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
            if (!TryGetControllerDefinition(out var definition))
            {
                cmp.BlockMovement = true;
                return;
            }

            ApplyMovementSpeed(cmp, definition);
            cmp.BlockMovement = IsDead;
        }

        private void OnMovementStateChanged(MyCharacterMovement previous, MyCharacterMovement current)
        {
            var transition = $"{previous}->{current}";
            if (_loggedMovementTransitions.Add(transition))
                LogAnimationState($"movement-state {transition}", _movement, null);
        }

        private void ApplyCurrentMovementSpeed()
        {
            if (_movement == null || !TryGetControllerDefinition(out var definition))
                return;

            ApplyMovementSpeed(_movement, definition);
        }

        private void ApplyMovementSpeed(
            MyCharacterMovementComponent movement,
            SiGroundedNpcControllerComponentDefinition definition)
        {
            var speed = _combatMovementSpeed
                        ?? _squadMovementSpeed
                        ?? MovementSpeedFromDefinition(definition);
            movement.WantsWalk = speed == SiNpcMovementSpeed.Walk;
            movement.WantsSprint = speed == SiNpcMovementSpeed.Sprint;
        }

        private static SiNpcMovementSpeed MovementSpeedFromMovement(MyCharacterMovementComponent movement)
        {
            if (movement.IsSprinting)
                return SiNpcMovementSpeed.Sprint;
            if (movement.IsWalking)
                return SiNpcMovementSpeed.Walk;
            return SiNpcMovementSpeed.Run;
        }

        private static SiNpcMovementSpeed MovementSpeedFromDefinition(
            SiGroundedNpcControllerComponentDefinition definition)
        {
            if (definition.WantsSprint)
                return SiNpcMovementSpeed.Sprint;
            if (definition.WantsWalk)
                return SiNpcMovementSpeed.Walk;
            return SiNpcMovementSpeed.Run;
        }

        private void LogAnimationState(
            string branch,
            MyCharacterMovementComponent movement,
            bool? tryCrouchAccepted)
        {
            if (!SiGameLog.Enabled)
                return;

            var animation = Entity?.Components.Get<MyCharacterAnimationControllerComponent>();
            var state = movement?.GetMovementState().ToString() ?? "missing";
            var snapshot = $"branch={branch} requestedCrouch={_wantsCrouch} tryCrouchAccepted={(tryCrouchAccepted.HasValue ? tryCrouchAccepted.Value.ToString() : "n/a")} "
                + $"movementState={state} wantsCrouch={(movement?.WantsCrouch.ToString() ?? "missing")} "
                + $"isCrouching={(movement?.IsCrouching.ToString() ?? "missing")} isWalking={(movement?.IsWalking.ToString() ?? "missing")} "
                + $"isRunning={(movement?.IsRunning.ToString() ?? "missing")} isSprinting={(movement?.IsSprinting.ToString() ?? "missing")} "
                + $"falling={(movement?.IsFalling.ToString() ?? "missing")} flying={(movement?.IsFlying.ToString() ?? "missing")} jumping={(movement?.IsJumping.ToString() ?? "missing")} "
                + $"wantsWalk={(movement?.WantsWalk.ToString() ?? "missing")} wantsSprint={(movement?.WantsSprint.ToString() ?? "missing")} blockMovement={(movement?.BlockMovement.ToString() ?? "missing")} "
                + $"physics={(Entity?.Physics != null)} animation={(animation != null)} animationController={(animation?.Controller != null)} "
                + $"source={(animation?.SourceId.ToString() ?? "missing")} animationPaused={(animation?.IsPaused.ToString() ?? "missing")} "
                + $"variables={(animation?.Variables != null)}";
        }

        private static Vector3D NormalizedOrFallback(in Vector3D value, in Vector3D fallback)
        {
            var lengthSquared = value.LengthSquared();
            return lengthSquared > MinimumDirectionLengthSquared
                ? value / Math.Sqrt(lengthSquared)
                : fallback;
        }

        private bool TryGetControllerDefinition(
            out SiGroundedNpcControllerComponentDefinition definition)
        {
            definition = null;
            var entity = Entity;
            if (entity == null || entity.Closed || entity.MarkedForClose)
                return false;

            var controller = entity.Components.Get<SiGroundedNpcControllerComponent>();
            definition = controller?.Definition;
            return definition != null;
        }
    }
}
