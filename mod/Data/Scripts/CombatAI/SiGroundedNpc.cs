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
        private bool _hasLoggedMovementState;
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
            _wantsCrouch = wantsCrouch;
            var tryCrouchAccepted = _movement != null && _movement.TryCrouch(wantsCrouch);
            LogAnimationState("posture-request", _movement, _movement?.MoveIndicator ?? Vector3.Zero, tryCrouchAccepted);
        }

        public void SetSquadMovementSpeed(SiNpcMovementSpeed speed)
        {
            _squadMovementSpeed = speed;
            if (_movement != null)
                ApplyMovementSpeed(_movement, GetControllerDefinition());
        }

        public void ClearSquadMovementSpeed()
        {
            _squadMovementSpeed = null;
            if (_movement != null)
                ApplyMovementSpeed(_movement, GetControllerDefinition());
        }

        public void SetCombatMovementSpeed(SiNpcMovementSpeed speed)
        {
            _combatMovementSpeed = speed;
            if (_movement != null)
                ApplyMovementSpeed(_movement, GetControllerDefinition());
        }

        public void ClearCombatMovementSpeed()
        {
            _combatMovementSpeed = null;
            if (_movement != null)
                ApplyMovementSpeed(_movement, GetControllerDefinition());
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
            _hasLoggedMovementState = false;
            _lastLoggedMovementState = null;
            _squadMovementSpeed = null;
            _combatMovementSpeed = null;
            _movement = Entity?.Components.Get<MyCharacterMovementComponent>();
            if (_movement == null)
                throw new InvalidOperationException(
                    $"Grounded NPC '{EntityDefinition}' requires a {nameof(MyCharacterMovementComponent)}.");

            LogAnimationState("activated", _movement, Vector3.Zero, null);

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
            _squadMovementSpeed = null;
            _combatMovementSpeed = null;
            _hasLoggedMovementState = false;
            _lastLoggedMovementState = null;
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
            ApplyMovementSpeed(movement, GetControllerDefinition());
            if (IsDead)
            {
                moveIndicator = Vector3.Zero;
                LogAnimationState("movement-callback-dead", movement, moveIndicator, tryCrouchAccepted);
                return;
            }

            var definition = GetControllerDefinition();
            if (!TryGetMoveDirection(out var direction, definition))
            {
                moveIndicator = Vector3.Zero;
                LogAnimationState("movement-callback-no-waypoint", movement, moveIndicator, tryCrouchAccepted);
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

            LogAnimationState("movement-callback-steering", movement, moveIndicator, tryCrouchAccepted);
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
            ApplyMovementSpeed(cmp, definition);
            cmp.BlockMovement = IsDead;
            LogAnimationState("post-process", cmp, cmp.MoveIndicator, null);
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
            Vector3 moveIndicator,
            bool? tryCrouchAccepted)
        {
            if (!SiGameLog.Enabled)
                return;

            var animation = Entity?.Components.Get<MyCharacterAnimationControllerComponent>();
            var variables = animation?.Variables;
            var state = movement?.GetMovementState().ToString() ?? "missing";
            var snapshot = $"branch={branch} requestedCrouch={_wantsCrouch} tryCrouchAccepted={(tryCrouchAccepted.HasValue ? tryCrouchAccepted.Value.ToString() : "n/a")} "
                + $"moveIndicator={FormatVector(moveIndicator)} movementState={state} wantsCrouch={(movement?.WantsCrouch.ToString() ?? "missing")} "
                + $"isCrouching={(movement?.IsCrouching.ToString() ?? "missing")} isWalking={(movement?.IsWalking.ToString() ?? "missing")} "
                + $"isRunning={(movement?.IsRunning.ToString() ?? "missing")} isSprinting={(movement?.IsSprinting.ToString() ?? "missing")} "
                + $"falling={(movement?.IsFalling.ToString() ?? "missing")} flying={(movement?.IsFlying.ToString() ?? "missing")} jumping={(movement?.IsJumping.ToString() ?? "missing")} "
                + $"wantsWalk={(movement?.WantsWalk.ToString() ?? "missing")} wantsSprint={(movement?.WantsSprint.ToString() ?? "missing")} blockMovement={(movement?.BlockMovement.ToString() ?? "missing")} "
                + $"physics={(Entity?.Physics != null)} animation={(animation != null)} animationController={(animation?.Controller != null)} "
                + $"source={(animation?.SourceId.ToString() ?? "missing")} animationPaused={(animation?.IsPaused.ToString() ?? "missing")} "
                + $"variables={FormatAnimationVariables(variables)} layers={FormatAnimationLayers(animation?.Controller)}";

            if (_hasLoggedMovementState && _lastLoggedMovementState == snapshot)
                return;

            _hasLoggedMovementState = true;
            _lastLoggedMovementState = snapshot;
            _log.Warning($"entityId={Entity?.EntityId ?? EntityId} name={Entity?.Name ?? "null"} definition={EntityDefinition.SubtypeName} {snapshot}"); // AGENT-DEBUG-LOG
        }

        private static string FormatAnimationVariables(MyAnimationVariableStorage variables)
        {
            if (variables == null)
                return "missing";

            var speed = 0f;
            var speedX = 0f;
            var speedY = 0f;
            var speedZ = 0f;
            var walking = 0f;
            var sprinting = 0f;
            var crouch = 0f;
            var falling = 0f;
            var flying = 0f;
            var jumping = 0f;
            var hasSpeed = variables.GetValue(MyAnimationVariableStorageHints.StrIdSpeed, out speed);
            var hasSpeedX = variables.GetValue(MyAnimationVariableStorageHints.StrIdSpeedX, out speedX);
            var hasSpeedY = variables.GetValue(MyAnimationVariableStorageHints.StrIdSpeedY, out speedY);
            var hasSpeedZ = variables.GetValue(MyAnimationVariableStorageHints.StrIdSpeedZ, out speedZ);
            var hasWalking = variables.GetValue(MyAnimationVariableStorageHints.StrIdWalking, out walking);
            var hasSprinting = variables.GetValue(MyAnimationVariableStorageHints.StrIdSprinting, out sprinting);
            var hasCrouch = variables.GetValue(MyAnimationVariableStorageHints.StrIdCrouch, out crouch);
            var hasFalling = variables.GetValue(MyAnimationVariableStorageHints.StrIdFalling, out falling);
            var hasFlying = variables.GetValue(MyAnimationVariableStorageHints.StrIdFlying, out flying);
            var hasJumping = variables.GetValue(MyAnimationVariableStorageHints.StrIdJumping, out jumping);
            return $"speed={FormatVariable(hasSpeed, speed)} speedX={FormatVariable(hasSpeedX, speedX)} speedY={FormatVariable(hasSpeedY, speedY)} speedZ={FormatVariable(hasSpeedZ, speedZ)} "
                + $"walking={FormatVariable(hasWalking, walking)} sprinting={FormatVariable(hasSprinting, sprinting)} crouch={FormatVariable(hasCrouch, crouch)} "
                + $"falling={FormatVariable(hasFalling, falling)} flying={FormatVariable(hasFlying, flying)} jumping={FormatVariable(hasJumping, jumping)}";
        }

        private static string FormatAnimationLayers(MyAnimationController controller)
        {
            if (controller == null)
                return "missing";

            var result = string.Empty;
            foreach (var layer in controller.Layers)
            {
                if (result.Length > 0)
                    result += ";";

                result += $"{layer.Name}={layer}";
            }

            return result.Length > 0 ? result : "none";
        }

        private static string FormatVariable(bool present, float value)
        {
            return present ? value.ToString("0.00") : "missing";
        }

        private static string FormatVector(Vector3 value)
        {
            return $"({value.X:0.00},{value.Y:0.00},{value.Z:0.00})";
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
