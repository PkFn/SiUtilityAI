using System;
using System.Collections.Generic;
using Sandbox.Game;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems.Chat;
using Sandbox.Game.Players;
using Sandbox.ModAPI;
using Si.UtilityAI;
using VRage;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Entities.Gravity;
using VRage.Scene;
using VRage.Session;
using VRage.Utils;
using VRageMath;

namespace Si.K9
{
    [StaticEventOwner]
    [MySessionComponent(AllowAutomaticCreation = true, AlwaysOn = true)]
    [MyDependency(typeof(MyChatSystem), Critical = false)]
    public sealed class SiK9WolfSpawnSession : MySessionComponent
    {
        private const double MinimumDirectionLengthSquared = 0.0001;
        private const double WaypointArrivalRadius = 0.75;
        private const double FollowTeleportDistance = 20.0;

        private static readonly MyDefinitionId WolfDefinition =
            new MyDefinitionId(typeof(MyObjectBuilder_EntityBase), "SiK9Wolf");
        private static SiK9WolfSpawnSession _instance;

        private readonly Dictionary<long, SiK9WolfState> _wolves = new Dictionary<long, SiK9WolfState>();
        private readonly List<long> _staleWolves = new List<long>();

        [Automatic]
        private readonly MyChatSystem _chat = null;

        public static SiK9WolfSpawnSession Instance => _instance;
        private SiSquadSystemDefinition _followSpeedDefinition;

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

        protected override void OnUnload()
        {
            _wolves.Clear();
            _staleWolves.Clear();
            _followSpeedDefinition = null;
            if (ReferenceEquals(_instance, this))
                _instance = null;
            base.OnUnload();
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

        [Update(100)]
        private void UpdateDogFollow(long elapsedMilliseconds)
        {
            if (MyMultiplayerModApi.Static != null && !MyMultiplayerModApi.Static.IsServer)
                return;

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

            ApplyMovementSpeed(movement, state.MovementSpeed);
            movement.BlockMovement = false;
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
            }
        }
    }

    internal enum SiK9DogMotionOrder : byte
    {
        Stop = 0,
        Follow = 1,
    }
}
