using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Controller;
using Sandbox.Game;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems.Chat;
using Sandbox.Game.Players;
using Sandbox.ModAPI;
using SiCore.Core.Debug;
using Si.UtilityAI;
using VRage;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Components;
using VRage.Entities.Gravity;
using VRage.Scene;
using VRage.Session;
using VRage.Utils;
using VRageMath;

namespace Si.K9
{
    [StaticEventOwner]
    [MySessionComponent(typeof(MyObjectBuilder_SiK9WolfSpawnSession), AllowAutomaticCreation = true, AlwaysOn = true)]
    [MyDependency(typeof(MyChatSystem), Critical = false)]
    public sealed class SiK9WolfSpawnSession : MySessionComponent
    {
        private const double MinimumDirectionLengthSquared = 0.0001;
        private const double WaypointArrivalRadius = 0.75;
        private const double FollowTeleportDistance = 20.0;
        private const double InstantMountDistance = 2.25;
        private const double SeatWaypointRefreshDistance = 0.75;
        private const double ExitArrivalDistance = 1.25;
        private const long SeatWarpFallbackDelayMilliseconds = 2000L;
        private static readonly SiUnstuckTeleportService.Settings UnstuckSettings = SiUnstuckTeleportService.Settings.CreateDefault();

        private static readonly MyDefinitionId WolfDefinition =
            new MyDefinitionId(typeof(MyObjectBuilder_EntityBase), "SiK9Wolf");
        private static SiK9WolfSpawnSession _instance;

        private readonly Dictionary<long, SiK9WolfState> _wolves = new Dictionary<long, SiK9WolfState>();
        private readonly List<long> _staleWolves = new List<long>();
        private List<MyObjectBuilder_SiK9WolfSpawnSession.SavedWolf> _savedWolves;

        [Automatic]
        private readonly MyChatSystem _chat = null;
        private readonly SiGameLog _log = new SiGameLog(nameof(SiK9WolfSpawnSession), "[SiK9Seat]");

        public static SiK9WolfSpawnSession Instance => _instance;
        private SiSquadSystemDefinition _followSpeedDefinition;
        private bool _restoreSavedStatePending;

        protected override void OnLoad()
        {
            base.OnLoad();
            _instance = this;
            _followSpeedDefinition = SiSquadSystemDefinition.LoadDefault();
            _chat?.RegisterChatCommand(
                "/si-k9",
                HandleCommand,
                "Spawns Si K9 test entities.",
                MyChatCommandType.Server);
        }

        protected override void OnSessionReady()
        {
            base.OnSessionReady();
            if (MyMultiplayerModApi.Static == null || MyMultiplayerModApi.Static.IsServer)
                _restoreSavedStatePending = true;
        }

        protected override void OnUnload()
        {
            _wolves.Clear();
            _staleWolves.Clear();
            _savedWolves = null;
            _followSpeedDefinition = null;
            _restoreSavedStatePending = false;
            if (ReferenceEquals(_instance, this))
                _instance = null;
            base.OnUnload();
        }

        protected override bool IsSerialized =>
            _wolves.Count > 0 || (_savedWolves != null && _savedWolves.Count > 0);

        protected override MyObjectBuilder_SessionComponent Serialize()
        {
            var builder = (MyObjectBuilder_SiK9WolfSpawnSession)base.Serialize();
            var saved = _wolves.Count > 0 ? CreateSavedWolves() : _savedWolves;
            builder.Wolves = saved != null && saved.Count > 0 ? saved : null;
            return builder;
        }

        protected override void Deserialize(MyObjectBuilder_SessionComponent objectBuilder)
        {
            base.Deserialize(objectBuilder);
            var builder = (MyObjectBuilder_SiK9WolfSpawnSession)objectBuilder;
            _savedWolves = builder.Wolves;
        }

        private bool HandleCommand(ulong sender, string message, MyChatCommandType handledAsType)
        {
            if (handledAsType != MyChatCommandType.Server)
                return false;

            var tokens = (message ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var spawnWolf =
                (tokens.Length == 1 && string.Equals(tokens[0], "wolf", StringComparison.OrdinalIgnoreCase))
                || (tokens.Length >= 2
                    && string.Equals(tokens[0], "/si-k9", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(tokens[1], "wolf", StringComparison.OrdinalIgnoreCase));
            if (!spawnWolf)
                return Respond(sender, "Usage: /si-k9 wolf");

            var player = MyPlayers.Static.GetPlayer(new MyPlayer.PlayerId(sender, 0));
            var playerPosition = player?.ControlledEntity?.Get<MyPositionComponentBase>();
            if (playerPosition == null)
                return Respond(sender, "You must control a character to spawn a K9 wolf.");

            try
            {
                var matrix = playerPosition.WorldMatrix;
                var spawnPosition = matrix.Translation + matrix.Forward * 3 + matrix.Up * 0.2;
                var spawnMatrix = MatrixD.CreateWorld(spawnPosition, matrix.Forward, matrix.Up);
                var wolf = new MyObjectBuilder_EntityBase
                {
                    EntityId = MyEntityIdentifier.AllocateId(),
                    EntityDefinitionId = WolfDefinition,
                    PersistentFlags = MyPersistentEntityFlags2.InScene,
                    PositionAndOrientation = new MyPositionAndOrientation(spawnMatrix),
                };

                var entity = MyEntities.CreateFromObjectBuilder(wolf);
                if (entity == null)
                    return Respond(sender, "Failed to create the K9 wolf entity.");

                MyEntities.Add(entity, true);
                RegisterWolf(entity.EntityId, player.Id.SteamId);
                return Respond(sender, $"Spawned K9 wolf ({entity.EntityId}).");
            }
            catch (Exception exception)
            {
                return Respond(sender, $"Failed to spawn K9 wolf: {exception.Message}");
            }
        }

        internal void RequestMotionOrder(SiK9DogMotionOrder order)
        {
            if (MyMultiplayerModApi.Static != null && !MyMultiplayerModApi.Static.IsServer)
            {
                MyMultiplayerModApi.Static.RaiseStaticEvent(
                    x => ApplyMotionOrderServer,
                    (byte)order);
                return;
            }

            ApplyMotionOrder(MyAPIGateway.Session?.Player as MyPlayer, order);
        }

        internal void NotifyLocalOrder(SiK9DogMotionOrder order)
        {
            var text = order == SiK9DogMotionOrder.Follow ? "Dogs ordered to follow." : "Dogs ordered to stop.";
            MyAPIGateway.Utilities?.ShowNotification(text, 1500);
        }

        internal void RequestTransportOrder(SiK9DogTransportOrder order)
        {
            if (MyMultiplayerModApi.Static != null && !MyMultiplayerModApi.Static.IsServer)
            {
                MyMultiplayerModApi.Static.RaiseStaticEvent(
                    x => ApplyTransportOrderServer,
                    (byte)order);
                return;
            }

            ApplyTransportOrder(MyAPIGateway.Session?.Player as MyPlayer, order);
        }

        internal void NotifyLocalTransportOrder(SiK9DogTransportOrder order)
        {
            var text = order == SiK9DogTransportOrder.GetIn ? "Dogs ordered to get in." : "Dogs ordered to get out.";
            MyAPIGateway.Utilities?.ShowNotification(text, 1500);
        }

        [Update(100)]
        private void UpdateDogFollow(long elapsedMilliseconds)
        {
            if (MyMultiplayerModApi.Static != null && !MyMultiplayerModApi.Static.IsServer)
                return;

            RestoreSavedStateIfPending();
            _staleWolves.Clear();
            foreach (var pair in _wolves)
            {
                var state = pair.Value;
                if (!TryResolveWolfEntity(pair.Key, state, out var wolfEntity))
                {
                    UnregisterMovementHandlers(state);
                    _staleWolves.Add(pair.Key);
                    continue;
                }

                EnsureMovementHandlers(state, wolfEntity);

                if (state.TransportOrder != SiK9DogTransportOrder.None)
                {
                    ApplyDogTransportOrder(state, wolfEntity);
                    continue;
                }

                if (state.Order == SiK9DogMotionOrder.Stop)
                {
                    ClearMotionTarget(state);
                    continue;
                }

                var owner = MyPlayers.Static?.GetPlayer(new MyPlayer.PlayerId(state.OwnerSteamId, 0));
                var target = owner?.ControlledEntity;
                if (target == null)
                {
                    ClearMotionTarget(state);
                    continue;
                }

                FollowOwner(state, wolfEntity, owner, target, elapsedMilliseconds, state.Order, _followSpeedDefinition);
            }

            for (var i = 0; i < _staleWolves.Count; i++)
                _wolves.Remove(_staleWolves[i]);
        }

        private void RegisterWolf(long entityId, ulong ownerSteamId)
        {
            _wolves[entityId] = new SiK9WolfState(ownerSteamId, SiK9DogMotionOrder.Stop);
        }

        private void RestoreSavedStateIfPending()
        {
            if (!_restoreSavedStatePending)
                return;

            _restoreSavedStatePending = false;
            RestoreSavedWolves();
        }

        private List<MyObjectBuilder_SiK9WolfSpawnSession.SavedWolf> CreateSavedWolves()
        {
            var saved = new List<MyObjectBuilder_SiK9WolfSpawnSession.SavedWolf>(_wolves.Count);
            foreach (var pair in _wolves)
            {
                var state = pair.Value;
                if (state == null)
                    continue;

                saved.Add(new MyObjectBuilder_SiK9WolfSpawnSession.SavedWolf
                {
                    EntityId = pair.Key,
                    OwnerSteamId = state.OwnerSteamId,
                    Order = (byte)state.Order,
                    TransportOrder = (byte)state.TransportOrder,
                    VehicleEntityId = state.VehicleEntityId,
                    SeatEntityId = state.SeatEntityId,
                    SeatSlotName = state.SeatSlotName,
                    HasTransportExitLocalPosition = state.ExitPoint != null && state.ExitPoint.HasLocalPosition,
                    TransportExitLocalPosition = state.ExitPoint != null
                        ? (SerializableVector3D)state.ExitPoint.LocalPosition
                        : default(SerializableVector3D),
                });
            }

            return saved;
        }

        private void RestoreSavedWolves()
        {
            var savedWolves = _savedWolves;
            _savedWolves = null;
            if (savedWolves == null)
                return;

            foreach (var saved in savedWolves)
            {
                if (saved == null
                    || saved.EntityId == 0
                    || saved.OwnerSteamId == 0
                    || !Enum.IsDefined(typeof(SiK9DogMotionOrder), (int)saved.Order)
                    || !Enum.IsDefined(typeof(SiK9DogTransportOrder), (int)saved.TransportOrder))
                    continue;

                var state = new SiK9WolfState(saved.OwnerSteamId, (SiK9DogMotionOrder)saved.Order)
                {
                    TransportOrder = (SiK9DogTransportOrder)saved.TransportOrder,
                    VehicleEntityId = saved.VehicleEntityId,
                    SeatEntityId = saved.SeatEntityId,
                    SeatSlotName = saved.SeatSlotName,
                };
                if (saved.HasTransportExitLocalPosition)
                {
                    state.ExitPoint.HasLocalPosition = true;
                    state.ExitPoint.LocalPosition = saved.TransportExitLocalPosition;
                }

                _wolves[saved.EntityId] = state;
            }
        }

        private void ApplyMotionOrder(MyPlayer player, SiK9DogMotionOrder order)
        {
            var ownerSteamId = player?.Id.SteamId ?? 0UL;
            if (ownerSteamId == 0)
                return;

            foreach (var entityId in new List<long>(_wolves.Keys))
            {
                SiK9WolfState state;
                if (!_wolves.TryGetValue(entityId, out state) || state.OwnerSteamId != ownerSteamId)
                    continue;

                state.Order = order;
                _wolves[entityId] = state;
            }
        }

        private void ApplyTransportOrder(MyPlayer player, SiK9DogTransportOrder order)
        {
            var ownerSteamId = player?.Id.SteamId ?? 0UL;
            if (ownerSteamId == 0)
                return;

            if (order == SiK9DogTransportOrder.GetIn)
            {
                string failure;
                MyEntity vehicle;
                if (!SiTransportSeatService.TryGetMountedVehicle(player, out vehicle, out failure))
                    return;

                foreach (var entityId in new List<long>(_wolves.Keys))
                {
                    SiK9WolfState state;
                    if (!_wolves.TryGetValue(entityId, out state) || state.OwnerSteamId != ownerSteamId)
                        continue;
                    if (!TryResolveWolfEntity(entityId, state, out var wolfEntity))
                        continue;

                    var controller = wolfEntity.Components.Get<EquiEntityControllerComponent>();
                    if (controller == null)
                        continue;

                    if (controller.Controlled != null && !IsAssignedTransportSeat(state, controller.Controlled))
                        controller.ReleaseControl();

                    if (!TryAssignDogSeat(state, wolfEntity, vehicle))
                        continue;

                    state.TransportOrder = SiK9DogTransportOrder.GetIn;
                    ClearMotionTarget(state);
                }

                return;
            }

            if (order == SiK9DogTransportOrder.GetOut)
            {
                foreach (var entityId in new List<long>(_wolves.Keys))
                {
                    SiK9WolfState state;
                    if (!_wolves.TryGetValue(entityId, out state) || state.OwnerSteamId != ownerSteamId)
                        continue;

                    var controller = state.Entity?.Components.Get<EquiEntityControllerComponent>();
                    if (controller?.Controlled == null && state.SeatEntityId == 0)
                        continue;

                    state.TransportOrder = SiK9DogTransportOrder.GetOut;
                }
            }
        }

        private static void FollowOwner(
            SiK9WolfState state,
            MyEntity wolfEntity,
            MyPlayer owner,
            MyEntity target,
            long elapsedMilliseconds,
            SiK9DogMotionOrder order,
            SiSquadSystemDefinition followSpeedDefinition)
        {
            var movement = wolfEntity.Components.Get<MyCharacterMovementComponent>();
            if (movement == null)
            {
                ClearMotionTarget(state);
                return;
            }

            var current = wolfEntity.WorldMatrix;
            var targetMatrix = target.WorldMatrix;
            var up = ResolveUp(current.Translation);
            var toTarget = Vector3D.Reject(targetMatrix.Translation - current.Translation, up);
            var distance = toTarget.Length();
            var followDistance = followSpeedDefinition?.FollowDistance ?? 2.5;
            var checkpointSpeed = SiFollowSpeedLogic.GetPlayerCheckpointSpeed(owner);
            var hysteresisDistance = followDistance + SiFollowSpeedLogic.DynamicWaypointSpeedHysteresis;
            if (checkpointSpeed == SiNpcMovementSpeed.Walk && distance <= hysteresisDistance)
            {
                state.MovementSpeed = SiNpcMovementSpeed.Walk;
                state.Waypoint = targetMatrix.Translation;
                state.HasWaypoint = true;
                return;
            }

            if (distance <= followDistance)
            {
                ClearMotionTarget(state);
                return;
            }

            var direction = distance > 0.001 ? toTarget / distance : Vector3D.Zero;
            var destination = targetMatrix.Translation - direction * followDistance;

            var followSpeed = SiFollowSpeedLogic.ResolveFollowerSpeed(
                followSpeedDefinition,
                checkpointSpeed,
                distance);
            state.MovementSpeed = distance >= FollowTeleportDistance && order == SiK9DogMotionOrder.Follow
                ? SiNpcMovementSpeed.Sprint
                : followSpeed;
            state.Waypoint = destination;
            state.HasWaypoint = true;
        }

        private static void ClearMotionTarget(SiK9WolfState state)
        {
            state.HasWaypoint = false;
            state.MovementSpeed = SiNpcMovementSpeed.Run;
        }

        private void ApplyDogTransportOrder(SiK9WolfState state, MyEntity wolfEntity)
        {
            if (state == null || wolfEntity == null)
                return;

            var controller = wolfEntity.Components.Get<EquiEntityControllerComponent>();
            if (controller == null)
            {
                ClearTransportState(state);
                return;
            }

            switch (state.TransportOrder)
            {
                case SiK9DogTransportOrder.GetIn:
                    ApplyDogGetInOrder(state, wolfEntity, controller);
                    break;
                case SiK9DogTransportOrder.GetOut:
                    ApplyDogGetOutOrder(state, wolfEntity, controller);
                    break;
            }
        }

        private void ApplyDogGetInOrder(
            SiK9WolfState state,
            MyEntity wolfEntity,
            EquiEntityControllerComponent controller)
        {
            EquiPlayerAttachmentComponent.Slot seat;
            if (!TryGetAssignedTransportSeat(state, wolfEntity, out seat))
            {
                ClearTransportState(state);
                return;
            }

            if (controller.Controlled != null)
            {
                if (IsAssignedTransportSeat(state, controller.Controlled))
                {
                    state.SeatApproach.Reset();
                    ApplySeatedIdleState(wolfEntity, state);
                    ClearMotionTarget(state);
                }
                else
                    controller.ReleaseControl();
                return;
            }

            var mountResult = SiTransportSeatService.TryMountSeatOrApproach(
                wolfEntity,
                controller,
                seat,
                state.SeatApproach,
                InstantMountDistance,
                SeatWarpFallbackDelayMilliseconds,
                SeatWaypointRefreshDistance,
                out var seatPosition,
                out var exitPosition);
            switch (mountResult)
            {
                case SiTransportSeatService.SeatMountResult.Mounted:
                    SiTransportSeatService.TryRecordRelativeExitPoint(state.VehicleEntityId, state.ExitPoint, exitPosition);
                    ClearMotionTarget(state);
                    return;
                case SiTransportSeatService.SeatMountResult.Approach:
                    RefreshTransportWaypoint(state, seatPosition);
                    return;
                default:
                    return;
            }
        }

        private void ApplyDogGetOutOrder(
            SiK9WolfState state,
            MyEntity wolfEntity,
            EquiEntityControllerComponent controller)
        {
            Vector3D exitPosition;
            var hasExitPosition = SiTransportSeatService.TryResolveRelativeExitPoint(
                state.VehicleEntityId,
                state.ExitPoint,
                out exitPosition);
            if (!hasExitPosition)
                exitPosition = wolfEntity.WorldMatrix.Translation;

            if (controller.Controlled != null)
            {
                ApplySeatedIdleState(wolfEntity, state);
                controller.ReleaseControl();
                RefreshTransportWaypoint(state, exitPosition);
                return;
            }

            if (!hasExitPosition
                || Vector3D.DistanceSquared(wolfEntity.WorldMatrix.Translation, exitPosition)
                   <= ExitArrivalDistance * ExitArrivalDistance)
            {
                ClearTransportState(state);
                return;
            }

            RefreshTransportWaypoint(state, exitPosition);
        }

        private bool TryGetAssignedTransportSeat(
            SiK9WolfState state,
            MyEntity wolfEntity,
            out EquiPlayerAttachmentComponent.Slot seat)
        {
            seat = null;
            if (state == null || wolfEntity == null || state.VehicleEntityId == 0)
                return false;

            if (SiTransportSeatService.TryResolveSeat(state.SeatEntityId, state.SeatSlotName, out seat)
                && (seat.AttachedCharacter == null || seat.AttachedCharacter == wolfEntity))
                return true;

            if (!SiTransportSeatService.TryGetTransportVehicleEntity(state.VehicleEntityId, out var vehicle))
                return false;

            return TryAssignDogSeat(state, wolfEntity, vehicle)
                   && SiTransportSeatService.TryResolveSeat(state.SeatEntityId, state.SeatSlotName, out seat);
        }

        private bool TryAssignDogSeat(SiK9WolfState state, MyEntity wolfEntity, MyEntity vehicle)
        {
            if (state == null || wolfEntity == null || vehicle == null)
                return false;

            if (!SiTransportSeatService.TryFindNearestFreeSeat(
                wolfEntity,
                vehicle,
                (seatEntityId, seatName) => IsSeatReservedByOtherDog(wolfEntity.EntityId, seatEntityId, seatName),
                out var seat))
                return false;

            state.VehicleEntityId = vehicle.EntityId;
            state.SeatEntityId = seat.Controllable.Entity.EntityId;
            state.SeatSlotName = seat.Definition.Name;
            state.SeatApproach.Reset();
            return true;
        }

        private bool IsSeatReservedByOtherDog(long wolfEntityId, long seatEntityId, string seatName)
        {
            foreach (var pair in _wolves)
            {
                if (pair.Key == wolfEntityId)
                    continue;

                var state = pair.Value;
                if (state == null)
                    continue;
                if (state.SeatEntityId == seatEntityId
                    && string.Equals(state.SeatSlotName, seatName, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static bool IsAssignedTransportSeat(
            SiK9WolfState state,
            EquiPlayerAttachmentComponent.Slot seat)
        {
            return state != null
                   && SiTransportSeatService.IsSameSeat(seat, state.SeatEntityId, state.SeatSlotName);
        }

        private static void RefreshTransportWaypoint(SiK9WolfState state, in Vector3D position)
        {
            if (!state.HasWaypoint
                || Vector3D.DistanceSquared(state.Waypoint, position)
                   > SeatWaypointRefreshDistance * SeatWaypointRefreshDistance)
            {
                state.Waypoint = position;
                state.HasWaypoint = true;
                state.MovementSpeed = SiNpcMovementSpeed.Run;
            }
        }

        private static void ClearTransportState(SiK9WolfState state)
        {
            state.TransportOrder = SiK9DogTransportOrder.None;
            state.VehicleEntityId = 0;
            state.SeatEntityId = 0;
            state.SeatSlotName = null;
            state.ExitPoint.Reset();
            state.SeatApproach.Reset();
            SetSeatedAnimationState(state.Entity, false);
            ClearMotionTarget(state);
        }

        private void ApplySeatedIdleState(MyEntity wolfEntity, SiK9WolfState state)
        {
            if (state == null)
                return;

            state.HasWaypoint = false;
            state.MovementSpeed = SiNpcMovementSpeed.Run;

            var movement = wolfEntity?.Components.Get<MyCharacterMovementComponent>();
            if (movement != null)
            {
                movement.WantsWalk = false;
                movement.WantsSprint = false;
                movement.BlockMovement = true;
            }

            SetSeatedAnimationState(wolfEntity, true);

            var animation = wolfEntity?.Components.Get<MyAnimationControllerComponent>();
            if (animation?.Variables == null)
                return;

            animation.Variables.SetValue(MyStringId.GetOrCompute("speed"), 0f);
            animation.Variables.SetValue(MyStringId.GetOrCompute("Speed"), 0f);
            LogSeatedAnimationState(wolfEntity, state, movement, animation);
        }

        private static void SetSeatedAnimationState(MyEntity wolfEntity, bool seated)
        {
            var animation = wolfEntity?.Components.Get<MyAnimationControllerComponent>();
            if (animation?.Variables == null)
                return;

            var value = seated ? 1f : 0f;
            animation.Variables.SetValue(MyStringId.GetOrCompute("seated"), value);
            animation.Variables.SetValue(MyStringId.GetOrCompute("Seated"), value);
        }

        private void LogSeatedAnimationState(
            MyEntity wolfEntity,
            SiK9WolfState state,
            MyCharacterMovementComponent movement,
            MyAnimationControllerComponent animation)
        {
            if (wolfEntity == null || state == null)
                return;

            var now = (long)(MySession.Static?.ElapsedGameTime.TotalMilliseconds ?? 0);
            if (now - state.LastSeatedLogTimeMilliseconds < 1000)
                return;

            state.LastSeatedLogTimeMilliseconds = now;
            _log.Info($"entityId={wolfEntity.EntityId} name={wolfEntity.DebugName ?? wolfEntity.DisplayName ?? wolfEntity.ToString()} seat={state.SeatEntityId}:{state.SeatSlotName ?? "none"} branch=seated-idle movementState={(movement?.GetMovementState().ToString() ?? "missing")} isRunning={(movement?.IsRunning.ToString() ?? "missing")} isWalking={(movement?.IsWalking.ToString() ?? "missing")} isSprinting={(movement?.IsSprinting.ToString() ?? "missing")} wantsWalk={(movement?.WantsWalk.ToString() ?? "missing")} wantsSprint={(movement?.WantsSprint.ToString() ?? "missing")} blockMovement={(movement?.BlockMovement.ToString() ?? "missing")} animPaused={(animation?.IsPaused.ToString() ?? "missing")} source={(animation?.SourceId.ToString() ?? "missing")} hasVars={(animation?.Variables != null)}"); // AGENT-DEBUG-LOG
        }

        private static Vector3D ResolveUp(in Vector3D position)
        {
            var gravity = MyGravityProviderSystem.CalculateTotalGravityInPoint(position);
            if (gravity.LengthSquared() > 0.0001)
                return -Vector3D.Normalize(gravity);
            return Vector3D.Up;
        }

        private static bool TryResolveWolfEntity(long entityId, SiK9WolfState state, out MyEntity entity)
        {
            if (state.Entity != null
                && !state.Entity.Closed
                && !state.Entity.MarkedForClose
                && state.Entity.InScene
                && state.Entity.EntityId == entityId)
            {
                entity = state.Entity;
                return true;
            }

            entity = null;
            if (!MyEntities.TryGetEntityById(new EntityId((ulong)entityId), out entity)
                || entity == null
                || entity.Closed
                || entity.MarkedForClose
                || !entity.InScene)
                return false;

            state.Entity = entity;
            return true;
        }

        private void EnsureMovementHandlers(SiK9WolfState state, MyEntity wolfEntity)
        {
            if (state.HandlersRegistered)
                return;

            var movement = wolfEntity.Components.Get<MyCharacterMovementComponent>();
            if (movement == null)
                return;

            state.Entity = wolfEntity;
            state.Movement = movement;
            movement.MovementIndicatorHandler += MovementIndicatorHandler;
            movement.RotationIndicatorHandler += RotationIndicatorHandler;
            movement.OnPostProcessPhysicalMovement += PostProcessPhysicalMovement;
            state.HandlersRegistered = true;
        }

        private void UnregisterMovementHandlers(SiK9WolfState state)
        {
            if (!state.HandlersRegistered || state.Movement == null)
                return;

            state.Movement.MovementIndicatorHandler -= MovementIndicatorHandler;
            state.Movement.RotationIndicatorHandler -= RotationIndicatorHandler;
            state.Movement.OnPostProcessPhysicalMovement -= PostProcessPhysicalMovement;
            state.HandlersRegistered = false;
            state.Movement = null;
            state.Entity = null;
        }

        private void MovementIndicatorHandler(
            MyCharacterMovementComponent movement,
            ref Vector3 moveIndicator)
        {
            var state = FindState(movement?.Entity?.EntityId ?? 0);
            if (state == null)
            {
                moveIndicator = Vector3.Zero;
                return;
            }

            var controller = movement?.Entity?.Components.Get<EquiEntityControllerComponent>();
            if (controller?.Controlled != null)
            {
                ApplySeatedIdleState(state.Entity, state);
                moveIndicator = Vector3.Zero;
                return;
            }

            ApplyMovementSpeed(movement, state.MovementSpeed);
            if (!TryGetMoveDirection(state, out var direction))
            {
                moveIndicator = Vector3.Zero;
                return;
            }

            var localDirection = Vector3D.TransformNormal(
                direction,
                state.Entity.PositionComp.WorldMatrixNormalizedInv);
            moveIndicator = new Vector3(0f, (float)localDirection.Y, (float)localDirection.Z);
        }

        private void RotationIndicatorHandler(
            MyCharacterMovementComponent movement,
            ref Vector2 rotationIndicator,
            ref Vector3? forcedForward)
        {
            var state = FindState(movement?.Entity?.EntityId ?? 0);
            if (movement?.Entity?.Components.Get<EquiEntityControllerComponent>()?.Controlled != null)
            {
                rotationIndicator = Vector2.Zero;
                forcedForward = null;
                return;
            }

            if (state == null || !TryGetMoveDirection(state, out var direction))
                return;

            var gravity = (Vector3D)(-MyGravityProviderSystem.CalculateTotalGravityInPoint(state.Entity.GetPosition()));
            if (gravity.LengthSquared() <= MinimumDirectionLengthSquared)
                gravity = Vector3D.Up;
            gravity.Normalize();

            Vector3D.Cross(ref direction, ref gravity, out var right);
            Vector3D.Cross(ref gravity, ref right, out var forward);
            if (forward.Normalize() < 1e-2f)
                return;

            forcedForward = (Vector3)forward;
        }

        private void PostProcessPhysicalMovement(
            MyCharacterMovementComponent movement,
            ref MatrixD transform)
        {
            var state = FindState(movement?.Entity?.EntityId ?? 0);
            if (state == null)
            {
                movement.BlockMovement = false;
                return;
            }

            if (movement?.Entity?.Components.Get<EquiEntityControllerComponent>()?.Controlled != null)
            {
                ApplySeatedIdleState(state.Entity, state);
                return;
            }

            TryUnstuckMovingDog(state, movement?.Entity);
            ApplyMovementSpeed(movement, state.MovementSpeed);
            movement.BlockMovement = false;
        }

        private static void TryUnstuckMovingDog(SiK9WolfState state, MyEntity entity)
        {
            if (state == null || entity == null || !state.HasWaypoint)
            {
                if (state != null)
                {
                    state.Unstuck.Reset();
                    state.LastUnstuckEvaluationMilliseconds = -1;
                }
                return;
            }

            var physics = entity.Physics;
            var velocity = physics != null ? (Vector3D)physics.LinearVelocity : Vector3D.Zero;
            var now = (long)(MyAPIGateway.Session?.ElapsedPlayTime.TotalMilliseconds ?? 0);
            var elapsedSinceLastEvaluation = state.LastUnstuckEvaluationMilliseconds < 0
                ? 0
                : Math.Max(0, now - state.LastUnstuckEvaluationMilliseconds);
            state.LastUnstuckEvaluationMilliseconds = now;
            SiUnstuckTeleportService.TryUnstuckToWaypoint(
                entity,
                entity.WorldMatrix.Translation,
                velocity,
                state.Waypoint,
                elapsedSinceLastEvaluation,
                now,
                UnstuckSettings,
                state.Unstuck);
        }

        private static void ApplyMovementSpeed(
            MyCharacterMovementComponent movement,
            SiNpcMovementSpeed speed)
        {
            if (movement == null)
                return;

            movement.WantsWalk = speed == SiNpcMovementSpeed.Walk;
            movement.WantsSprint = speed == SiNpcMovementSpeed.Sprint;
        }

        private static bool TryGetMoveDirection(SiK9WolfState state, out Vector3D direction)
        {
            direction = Vector3D.Zero;
            if (state == null || !state.HasWaypoint || state.Entity == null)
                return false;

            var world = state.Entity.WorldMatrix;
            var position = world.Translation;
            var gravity = (Vector3D)MyGravityProviderSystem.CalculateTotalGravityInPoint(position);
            var up = gravity.LengthSquared() > MinimumDirectionLengthSquared
                ? -Vector3D.Normalize(gravity)
                : NormalizedOrFallback(world.Up, Vector3D.Up);
            var toWaypoint = Vector3D.Reject(state.Waypoint - position, up);
            var distanceSquared = toWaypoint.LengthSquared();
            if (distanceSquared <= WaypointArrivalRadius * WaypointArrivalRadius)
            {
                state.HasWaypoint = false;
                return false;
            }

            direction = toWaypoint / Math.Sqrt(distanceSquared);
            return true;
        }

        private static Vector3D NormalizedOrFallback(in Vector3D value, in Vector3D fallback)
        {
            var lengthSquared = value.LengthSquared();
            return lengthSquared > MinimumDirectionLengthSquared
                ? value / Math.Sqrt(lengthSquared)
                : fallback;
        }

        private SiK9WolfState FindState(long entityId)
        {
            if (entityId == 0)
                return null;

            SiK9WolfState state;
            return _wolves.TryGetValue(entityId, out state) ? state : null;
        }

        [Event, Reliable, Server]
        private static void ApplyMotionOrderServer(byte order)
        {
            if (!Enum.IsDefined(typeof(SiK9DogMotionOrder), (int)order))
            {
                MyEventContext.ValidationFailed();
                return;
            }

            var player = MyPlayers.Static.GetPlayer(new MyPlayer.PlayerId(MyEventContext.Current.Sender.Value, 0));
            _instance?.ApplyMotionOrder(player, (SiK9DogMotionOrder)order);
        }

        [Event, Reliable, Server]
        private static void ApplyTransportOrderServer(byte order)
        {
            if (!Enum.IsDefined(typeof(SiK9DogTransportOrder), (int)order))
            {
                MyEventContext.ValidationFailed();
                return;
            }

            var player = MyPlayers.Static.GetPlayer(new MyPlayer.PlayerId(MyEventContext.Current.Sender.Value, 0));
            _instance?.ApplyTransportOrder(player, (SiK9DogTransportOrder)order);
        }

        private bool Respond(ulong sender, string text)
        {
            if (!string.IsNullOrEmpty(text))
                _chat?.SendMessageToClient(sender, MyStringHash.GetOrCompute("System"), 0, text);
            return true;
        }

        private sealed class SiK9WolfState
        {
            public readonly ulong OwnerSteamId;
            public SiK9DogMotionOrder Order;
            public MyEntity Entity;
            public MyCharacterMovementComponent Movement;
            public bool HandlersRegistered;
            public bool HasWaypoint;
            public Vector3D Waypoint;
            public SiNpcMovementSpeed MovementSpeed;
            public SiK9DogTransportOrder TransportOrder;
            public long VehicleEntityId;
            public long SeatEntityId;
            public string SeatSlotName;
            public readonly SiTransportSeatService.RelativeExitPointState ExitPoint;
            public long LastSeatedLogTimeMilliseconds;
            public long LastUnstuckEvaluationMilliseconds;
            public readonly SiTransportSeatService.SeatApproachState SeatApproach;
            public readonly SiUnstuckTeleportService.State Unstuck;

            public SiK9WolfState(ulong ownerSteamId, SiK9DogMotionOrder order)
            {
                OwnerSteamId = ownerSteamId;
                Order = order;
                Entity = null;
                Movement = null;
                HandlersRegistered = false;
                HasWaypoint = false;
                Waypoint = Vector3D.Zero;
                MovementSpeed = SiNpcMovementSpeed.Run;
                TransportOrder = SiK9DogTransportOrder.None;
                VehicleEntityId = 0;
                SeatEntityId = 0;
                SeatSlotName = null;
                ExitPoint = new SiTransportSeatService.RelativeExitPointState();
                LastSeatedLogTimeMilliseconds = long.MinValue;
                LastUnstuckEvaluationMilliseconds = -1;
                SeatApproach = new SiTransportSeatService.SeatApproachState();
                Unstuck = new SiUnstuckTeleportService.State();
            }
        }
    }

    internal enum SiK9DogMotionOrder : byte
    {
        Stop = 0,
        Follow = 1,
    }

    internal enum SiK9DogTransportOrder : byte
    {
        None = 0,
        GetIn = 1,
        GetOut = 2,
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiK9WolfSpawnSession : MyObjectBuilder_SessionComponent
    {
        [XmlElement("Wolf")]
        public List<SavedWolf> Wolves;

        public class SavedWolf
        {
            [XmlAttribute]
            public long EntityId;

            [XmlAttribute]
            public ulong OwnerSteamId;

            [XmlAttribute]
            public byte Order;

            [XmlAttribute]
            public byte TransportOrder;

            [XmlAttribute]
            public long VehicleEntityId;

            [XmlAttribute]
            public long SeatEntityId;

            [XmlAttribute]
            public string SeatSlotName;

            [XmlAttribute]
            public bool HasTransportExitLocalPosition;

            public SerializableVector3D TransportExitLocalPosition;
        }
    }
}
