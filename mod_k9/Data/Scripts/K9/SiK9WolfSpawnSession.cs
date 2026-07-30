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
        private const double FollowTeleportDistance = 20.0;
        private const double WolfSprintForwardSpeed = 12.0;
        private const double WolfRunForwardSpeed = 4.3;
        private const double WolfWalkForwardSpeed = 1.8;

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
                MyEntity wolfEntity;
                if (!MyEntities.TryGetEntityById(new EntityId((ulong)pair.Key), out wolfEntity)
                    || wolfEntity == null
                    || wolfEntity.Closed
                    || wolfEntity.MarkedForClose
                    || !wolfEntity.InScene)
                {
                    _staleWolves.Add(pair.Key);
                    continue;
                }

                if (pair.Value.Order == SiK9DogMotionOrder.Stop)
                {
                    ApplyIdle(wolfEntity);
                    continue;
                }

                var owner = MyPlayers.Static?.GetPlayer(new MyPlayer.PlayerId(pair.Value.OwnerSteamId, 0));
                var target = owner?.ControlledEntity;
                if (target == null)
                {
                    ApplyIdle(wolfEntity);
                    continue;
                }

                FollowOwner(wolfEntity, owner, target, elapsedMilliseconds, pair.Value.Order, _followSpeedDefinition);
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
            MyEntity wolfEntity,
            MyPlayer owner,
            MyEntity target,
            long elapsedMilliseconds,
            SiK9DogMotionOrder order,
            SiSquadSystemDefinition followSpeedDefinition)
        {
            var movement = wolfEntity.Components.Get<MyCharacterMovementComponent>();
            var current = wolfEntity.WorldMatrix;
            var targetMatrix = target.WorldMatrix;
            var up = ResolveUp(current.Translation);
            var toTarget = Vector3D.Reject(targetMatrix.Translation - current.Translation, up);
            var distance = toTarget.Length();
            var followDistance = followSpeedDefinition?.FollowDistance ?? 2.5;
            if (distance <= followDistance)
            {
                ApplyIdle(wolfEntity);
                return;
            }

            var direction = distance > 0.001 ? toTarget / distance : Vector3D.Zero;
            var destination = targetMatrix.Translation - direction * followDistance;
            if (distance >= FollowTeleportDistance)
            {
                var teleportMatrix = MatrixD.CreateWorld(destination, direction, up);
                wolfEntity.PositionComp.SetWorldMatrix(teleportMatrix, null, true);
                if (movement != null)
                {
                    movement.Teleport(destination);
                    movement.MoveIndicator = Vector3.Zero;
                    movement.WantsSprint = false;
                    movement.WantsWalk = false;
                }

                if (wolfEntity.Physics != null)
                    wolfEntity.Physics.LinearVelocity = Vector3.Zero;
                return;
            }

            var checkpointSpeed = SiFollowSpeedLogic.GetPlayerCheckpointSpeed(owner);
            var followSpeed = SiFollowSpeedLogic.ResolveFollowerSpeed(
                followSpeedDefinition,
                checkpointSpeed,
                distance);
            var stepSpeed = SpeedFor(followSpeed, order);

            var step = Math.Min(
                stepSpeed * Math.Max(0.01, elapsedMilliseconds / 1000.0),
                Math.Max(0, distance - followDistance));
            var nextPosition = current.Translation + direction * step;
            var nextMatrix = MatrixD.CreateWorld(nextPosition, direction, up);
            wolfEntity.PositionComp.SetWorldMatrix(nextMatrix, null, true);

            if (movement != null)
            {
                movement.MoveIndicator = Vector3.Forward;
                movement.WantsSprint = followSpeed == SiNpcMovementSpeed.Sprint;
                movement.WantsWalk = followSpeed == SiNpcMovementSpeed.Walk;
                movement.MoveAndRotate();
            }

            if (wolfEntity.Physics != null)
                wolfEntity.Physics.LinearVelocity = (Vector3)(direction * (step / Math.Max(0.01, elapsedMilliseconds / 1000.0)));
        }

        private static void ApplyIdle(MyEntity wolfEntity)
        {
            var movement = wolfEntity.Components.Get<MyCharacterMovementComponent>();
            if (movement != null)
            {
                movement.MoveIndicator = Vector3.Zero;
                movement.WantsSprint = false;
                movement.WantsWalk = false;
                movement.MoveAndRotate();
            }

            if (wolfEntity.Physics != null)
                wolfEntity.Physics.LinearVelocity = Vector3.Zero;
        }

        private static Vector3D ResolveUp(in Vector3D position)
        {
            var gravity = MyGravityProviderSystem.CalculateTotalGravityInPoint(position);
            if (gravity.LengthSquared() > 0.0001)
                return -Vector3D.Normalize(gravity);
            return Vector3D.Up;
        }

        private static double SpeedFor(SiNpcMovementSpeed speed, SiK9DogMotionOrder order)
        {
            switch (speed)
            {
                case SiNpcMovementSpeed.Walk:
                    return WolfWalkForwardSpeed;
                case SiNpcMovementSpeed.Sprint:
                    return order == SiK9DogMotionOrder.Follow ? WolfSprintForwardSpeed : WolfRunForwardSpeed;
                default:
                    return WolfRunForwardSpeed;
            }
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

        private struct SiK9WolfState
        {
            public readonly ulong OwnerSteamId;
            public SiK9DogMotionOrder Order;

            public SiK9WolfState(ulong ownerSteamId, SiK9DogMotionOrder order)
            {
                OwnerSteamId = ownerSteamId;
                Order = order;
            }
        }
    }

    internal enum SiK9DogMotionOrder : byte
    {
        Stop = 0,
        Follow = 1,
    }
}
