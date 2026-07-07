using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Controller;
using Medieval.GameSystems.Factions;
using Sandbox.Definitions.Chat;
using Sandbox.Entities.Components;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Entity.Stats;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.GameSystems.Chat;
using Sandbox.Game.Players;
using Sandbox.ModAPI;
using VRage;
using VRage.Components.Interfaces;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.Entity.EntityComponents;
using VRage.Network;
using VRage.Session;
using VRage.Utils;
using VRageMath;

namespace Si.UtilityAI
{
    public sealed partial class SiNpcSessionComponent
    {
        private List<MyObjectBuilder_SiNpcSessionComponent.SavedNpc> CreateSavedNpcs()
        {
            var saved = new List<MyObjectBuilder_SiNpcSessionComponent.SavedNpc>();
            if (Npcs == null)
                return saved;

            foreach (var npc in Npcs.Npcs.Values)
            {
                if (npc.IsDead)
                    continue;
                saved.Add(CreateSavedNpc(npc));
            }
            return saved;
        }

        private List<MyObjectBuilder_SiNpcSessionComponent.SquadOrder> CreateSavedSquadOrders()
        {
            var saved = new List<MyObjectBuilder_SiNpcSessionComponent.SquadOrder>();
            foreach (var entry in _squadOrders)
                saved.Add(new MyObjectBuilder_SiNpcSessionComponent.SquadOrder
                {
                    LeaderIdentityId = entry.Key,
                    Mode = (byte)entry.Value.Mode,
                    Formation = (byte)entry.Value.Formation,
                    EngagementStance = (byte)entry.Value.EngagementStance,
                    TransportMode = (byte)entry.Value.TransportMode,
                    TransportVehicleEntityId = entry.Value.TransportVehicleEntityId,
                    CombatStance = (byte)GetCombatStance(PlayerLeaderKey(entry.Key)),
                });
            return saved;
        }

        private void RestoreSavedState()
        {
            RestoreSavedNpcs();
            RestoreSavedSquadOrders();
        }

        private void RestoreSavedNpcs()
        {
            var savedNpcs = _savedNpcs;
            _savedNpcs = null;
            if (savedNpcs == null || Npcs == null)
                return;

            foreach (var saved in savedNpcs)
                RestoreSavedNpc(saved);
        }

        private void RestoreSavedNpc(MyObjectBuilder_SiNpcSessionComponent.SavedNpc saved)
        {
            if (saved == null
                || saved.EntityId == 0
                || string.IsNullOrWhiteSpace(saved.WebbingSubtype))
                return;

            var request = new SiNpcSpawnRequest(
                saved.WebbingSubtype,
                saved.IsParatrooper,
                saved.IsEnemy);
            SiNpc npc;
            if (!Npcs.TrySpawnConfigured(
                    SiNpcManager.SoldierArchetype,
                    request.DisplayArchetype,
                    saved.EntityId,
                    saved.Transform.GetMatrix(),
                    out npc))
                return;

            string failure;
            if (!ApplySpawnRequest(npc, request, out failure))
            {
                Npcs.Close(saved.EntityId);
                return;
            }

            RestoreDiplomaticIdentity(saved, npc);
            if (saved.IsEnemy)
            {
                MyFaction enemyFaction;
                if (!RestoreHostileNpcFaction(saved, npc, out enemyFaction))
                    RestoreSquadAssignment(saved, npc);
                else if (saved.HasSquadAssignment)
                    RestoreSquadAssignment(saved, npc);
                else
                    AssignNpcToEnemySquad(npc, enemyFaction);
            }
            else
                RestoreSquadAssignment(saved, npc);

            RestoreTransportState(saved, npc);
            if (saved.HasWaypoint)
                Npcs.ApplyWaypoint(saved.EntityId, saved.Waypoint);
        }

        private void RestoreDiplomaticIdentity(
            MyObjectBuilder_SiNpcSessionComponent.SavedNpc saved,
            SiNpc npc)
        {
            if (saved.DiplomaticIdentityId == 0 || npc == null)
                return;

            var identity = MyIdentities.Static?.GetIdentity(saved.DiplomaticIdentityId);
            if (identity == null)
                identity = MyIdentities.Static?.CreateIdentity(
                    !string.IsNullOrWhiteSpace(saved.LeaderName)
                        ? saved.LeaderName
                        : EnemyTrooperName(npc));
            if (identity == null)
                return;

            SetNpcDiplomaticIdentity(npc, identity);
        }

        private bool RestoreHostileNpcFaction(
            MyObjectBuilder_SiNpcSessionComponent.SavedNpc saved,
            SiNpc npc,
            out MyFaction enemyFaction)
        {
            enemyFaction = null;
            if (npc == null)
                return false;

            if (npc.DiplomaticIdentityId == 0)
            {
                var identity = MyIdentities.Static?.CreateIdentity(
                    !string.IsNullOrWhiteSpace(saved.LeaderName)
                        ? saved.LeaderName
                        : EnemyTrooperName(npc));
                if (identity == null)
                    return false;

                SetNpcDiplomaticIdentity(npc, identity);
            }

            string failure;
            if (!TryAssignIdentityToEnemyFaction(npc.DiplomaticIdentityId, out enemyFaction, out failure))
                return false;
            return true;
        }

        private void SetNpcDiplomaticIdentity(SiNpc npc, MyIdentity identity)
        {
            if (npc == null || identity == null)
                return;

            npc.SetDiplomaticIdentity(identity, true);

            var ownership = npc.Entity?.Components.Get<MyEntityOwnershipComponent>();
            if (ownership != null)
                ownership.OwnerId = identity.Id;
        }

        private void RestoreSquadAssignment(
            MyObjectBuilder_SiNpcSessionComponent.SavedNpc saved,
            SiNpc npc)
        {
            if (!saved.HasSquadAssignment
                || !Enum.IsDefined(typeof(SiSquadLeaderKind), (int)saved.SquadLeaderKind)
                || !Enum.IsDefined(typeof(SiArmyKind), (int)saved.SquadArmyKind))
                return;

            Squads?.AssignNpcToLeader(
                npc,
                (SiSquadLeaderKind)saved.SquadLeaderKind,
                saved.SquadLeaderId,
                (SiArmyKind)saved.SquadArmyKind,
                saved.SquadArmyId,
                saved.LeaderName,
                saved.IsSquadLeader);
        }

        private void RestoreSavedSquadOrders()
        {
            var savedOrders = _savedSquadOrders;
            _savedSquadOrders = null;
            if (savedOrders == null)
                return;

            _squadOrders.Clear();
            _squadCombatStates.Clear();
            foreach (var saved in savedOrders)
            {
                if (saved == null
                    || saved.LeaderIdentityId == 0
                    || !Enum.IsDefined(typeof(SiSquadOrderMode), (int)saved.Mode)
                    || !Enum.IsDefined(typeof(SiSquadFormation), (int)saved.Formation)
                    || !Enum.IsDefined(typeof(SiSquadEngagementStance), (int)saved.EngagementStance)
                    || !Enum.IsDefined(typeof(SiSquadTransportMode), (int)saved.TransportMode)
                    || !Enum.IsDefined(typeof(SiSquadCombatStance), (int)saved.CombatStance))
                    continue;

                _squadOrders[saved.LeaderIdentityId] = new SiSquadCommandState
                {
                    Mode = (SiSquadOrderMode)saved.Mode,
                    Formation = (SiSquadFormation)saved.Formation,
                    EngagementStance = (SiSquadEngagementStance)saved.EngagementStance,
                    TransportMode = SiSquadTransportMode.None,
                    TransportVehicleEntityId = 0,
                };
                _squadCombatStates[PlayerLeaderKey(saved.LeaderIdentityId)] = new SiSquadCombatState
                {
                    LeaderName = "Player " + saved.LeaderIdentityId,
                    Stance = (SiSquadCombatStance)saved.CombatStance,
                    LastShotAtTime = long.MinValue,
                    LastEnemySpottedTime = long.MinValue,
                    LastStanceChangeTime = long.MinValue,
                };
            }
        }

        private static MyObjectBuilder_SiNpcSessionComponent.SavedNpc CreateSavedNpc(SiNpc npc)
        {
            var mover = npc as ISiWaypointMover;
            var dataDrivenNpc = npc as SiDataDrivenNpc;
            SiAssignedNpc assignment = null;
            var hasAssignment = _instance?.Squads != null
                                && _instance.Squads.TryGetAssignment(npc.EntityId, out assignment);
            var transportState = _instance?.CreateSavedTransportState(npc);
            return new MyObjectBuilder_SiNpcSessionComponent.SavedNpc
            {
                EntityId = npc.EntityId,
                Archetype = npc.Archetype,
                WebbingSubtype = dataDrivenNpc?.WebbingSubtype,
                IsParatrooper = dataDrivenNpc?.IsParatrooperSpawn ?? false,
                IsEnemy = dataDrivenNpc?.IsEnemySpawn ?? false,
                Transform = new MyPositionAndOrientation(npc.Transform),
                HasWaypoint = mover?.HasWaypoint ?? false,
                Waypoint = (SerializableVector3D)(mover?.Waypoint ?? Vector3D.Zero),
                HasSquadAssignment = hasAssignment,
                SquadLeaderKind = hasAssignment ? (byte)assignment.Leader.Kind : (byte)0,
                SquadLeaderId = hasAssignment ? assignment.Leader.Id : 0,
                SquadArmyKind = hasAssignment ? (byte)assignment.Leader.Army.Kind : (byte)0,
                SquadArmyId = hasAssignment ? assignment.Leader.Army.Id : 0,
                IsSquadLeader = hasAssignment && assignment.IsLeader,
                LeaderName = hasAssignment ? assignment.LeaderName : null,
                DiplomaticIdentityId = npc.DiplomaticIdentityId,
                HasTransportState = transportState != null,
                TransportVehicleEntityId = transportState?.VehicleEntityId ?? 0,
                SeatEntityId = transportState?.SeatEntityId ?? 0,
                SeatSlotName = transportState?.SeatSlotName,
                HasTransportExitLocalPosition = transportState?.HasExitLocalPosition ?? false,
                TransportExitLocalPosition = (SerializableVector3D)(transportState?.ExitLocalPosition ?? Vector3D.Zero),
                WasInTransportSeat = _instance?.IsNpcMountedInAssignedTransportSeat(npc, transportState) ?? false,
            };
        }

        private void RestoreTransportState(MyObjectBuilder_SiNpcSessionComponent.SavedNpc saved, SiNpc npc)
        {
            if (saved == null || npc?.Entity == null || !saved.HasTransportState)
                return;

            if (saved.TransportVehicleEntityId == 0
                || saved.SeatEntityId == 0
                || string.IsNullOrWhiteSpace(saved.SeatSlotName))
                return;

            _transportNpcStates[npc.EntityId] = new SiTransportNpcState
            {
                VehicleEntityId = saved.TransportVehicleEntityId,
                SeatEntityId = saved.SeatEntityId,
                SeatSlotName = saved.SeatSlotName,
                HasExitLocalPosition = saved.HasTransportExitLocalPosition,
                ExitLocalPosition = saved.TransportExitLocalPosition,
            };

            if (saved.WasInTransportSeat && !_pendingTransportSeatRestoreNpcIds.Contains(npc.EntityId))
                _pendingTransportSeatRestoreNpcIds.Add(npc.EntityId);
        }

        private SiTransportNpcState CreateSavedTransportState(SiNpc npc)
        {
            if (npc == null)
                return null;
            return _transportNpcStates.TryGetValue(npc.EntityId, out var state)
                ? state
                : null;
        }

        private bool IsNpcMountedInAssignedTransportSeat(SiNpc npc, SiTransportNpcState transportState)
        {
            if (npc?.Entity == null || transportState == null)
                return false;

            var controller = npc.Entity.Components.Get<EquiEntityControllerComponent>();
            var controlledSeat = controller?.Controlled;
            return controlledSeat != null
                   && transportState.SeatEntityId == (controlledSeat.Controllable?.Entity?.EntityId ?? 0)
                   && string.Equals(transportState.SeatSlotName, controlledSeat.Definition.Name, StringComparison.Ordinal);
        }

        private void RestorePendingTransportSeats()
        {
            if (_pendingTransportSeatRestoreNpcIds.Count == 0 || Npcs == null)
                return;

            for (var i = _pendingTransportSeatRestoreNpcIds.Count - 1; i >= 0; i--)
            {
                var npcEntityId = _pendingTransportSeatRestoreNpcIds[i];
                if (!Npcs.Npcs.TryGetValue(npcEntityId, out var npc) || npc?.Entity == null)
                {
                    _pendingTransportSeatRestoreNpcIds.RemoveAt(i);
                    continue;
                }

                if (TryRestoreTransportSeat(npc))
                    _pendingTransportSeatRestoreNpcIds.RemoveAt(i);
            }
        }

        private bool TryRestoreTransportSeat(SiNpc npc)
        {
            if (npc?.Entity == null)
                return false;

            var controller = npc.Entity.Components.Get<EquiEntityControllerComponent>();
            if (controller == null)
                return false;

            if (controller.Controlled != null)
                return IsAssignedTransportSeat(npc, controller.Controlled);

            if (!_transportNpcStates.TryGetValue(npc.EntityId, out var state))
                return false;
            if (!TryResolveTransportSeat(state, out var slot))
                return false;
            if (slot.AttachedCharacter != null && slot.AttachedCharacter != npc.Entity)
                return false;

            controller.RequestControl(slot);
            return controller.Controlled != null && IsAssignedTransportSeat(npc, controller.Controlled);
        }

        private static SiNpcSnapshot CreateSnapshot(SiNpc npc)
        {
            var mover = npc as ISiWaypointMover;
            var dataDrivenNpc = npc as SiDataDrivenNpc;
            SiAssignedNpc assignment = null;
            var hasAssignment = _instance?.Squads != null
                                && _instance.Squads.TryGetAssignment(npc.EntityId, out assignment);
            return new SiNpcSnapshot
            {
                EntityId = npc.EntityId,
                Archetype = npc.Archetype,
                WebbingSubtype = dataDrivenNpc?.WebbingSubtype,
                IsParatrooper = dataDrivenNpc?.IsParatrooperSpawn ?? false,
                IsEnemy = dataDrivenNpc?.IsEnemySpawn ?? false,
                Transform = npc.Transform,
                HasWaypoint = mover?.HasWaypoint ?? false,
                Waypoint = mover?.Waypoint ?? Vector3D.Zero,
                HasSquadAssignment = hasAssignment,
                SquadLeaderKind = hasAssignment ? (byte)assignment.Leader.Kind : (byte)0,
                SquadLeaderId = hasAssignment ? assignment.Leader.Id : 0,
                SquadArmyKind = hasAssignment ? (byte)assignment.Leader.Army.Kind : (byte)0,
                SquadArmyId = hasAssignment ? assignment.Leader.Army.Id : 0,
                IsSquadLeader = hasAssignment && assignment.IsLeader,
                LeaderName = hasAssignment ? assignment.LeaderName : null,
            };
        }

        private static void OnNpcSpoke(long entityId, Vector3D position, string message)
        {
            var instance = _instance;
            if (instance == null || string.IsNullOrWhiteSpace(message))
                return;
            if (!instance.ShowSquadChatter)
                return;
            if (!CanAnyPlayerHearSpeech(position))
                return;

            var speaker = instance.NpcCallsign(entityId);
            SpeakNpcLocal(position, speaker, message);
            if (MyMultiplayerModApi.Static != null && MyMultiplayerModApi.Static.IsServer)
                MyMultiplayerModApi.Static.RaiseStaticEvent(
                    x => SpeakNpcClient,
                    position,
                    speaker,
                    message);
        }

        private string NpcCallsign(long entityId)
        {
            SiNpc npc;
            if (Npcs != null && Npcs.Npcs.TryGetValue(entityId, out npc))
                return Squads?.GetNpcCallsign(Npcs, npc) ?? "Soldier";
            return "Soldier";
        }

        private static void SpeakNpcLocal(Vector3D position, string speaker, string message)
        {
            if (string.IsNullOrWhiteSpace(message) || !IsLocalPlayerInSpeakRange(position))
                return;

            var chat = _instance?._chat ?? MyChatSystem.Static;
            chat?.HandleLocalMessage(SpeakChannel, FormatSpeech(speaker, message));
        }

        private static void SpeakPlayerLocal(Vector3D position, string speaker, string message)
        {
            if (string.IsNullOrWhiteSpace(message) || !IsLocalPlayerInSpeakRange(position))
                return;

            var chat = _instance?._chat ?? MyChatSystem.Static;
            chat?.HandleLocalMessage(SpeakChannel, FormatSpeech(speaker, message));
        }

        private static bool IsLocalPlayerInSpeakRange(Vector3D position)
        {
            var player = LocalPlayer();
            Vector3D playerPosition;
            if (!TryGetPlayerSpeakPosition(player, out playerPosition))
                return false;

            return IsPositionInSpeakRange(position, playerPosition);
        }

        private static bool CanAnyPlayerHearSpeech(Vector3D position)
        {
            if (MyPlayers.Static == null)
                return IsLocalPlayerInSpeakRange(position);

            foreach (var playerEntry in MyPlayers.Static.GetAllPlayers())
            {
                Vector3D playerPosition;
                if (!TryGetPlayerSpeakPosition(playerEntry.Value, out playerPosition))
                    continue;
                if (IsPositionInSpeakRange(position, playerPosition))
                    return true;
            }

            return false;
        }

        private static bool TryGetPlayerSpeakPosition(MyPlayer player, out Vector3D position)
        {
            position = Vector3D.Zero;
            var playerPosition = player?.ControlledEntity?.Get<MyPositionComponentBase>();
            if (playerPosition == null)
                return false;

            position = playerPosition.WorldMatrix.Translation;
            return true;
        }

        private static bool IsPositionInSpeakRange(Vector3D speakerPosition, Vector3D listenerPosition)
        {
            var rangeSquared = SpeakRange * SpeakRange;
            return Vector3D.DistanceSquared(speakerPosition, listenerPosition) <= rangeSquared;
        }

        private static double SpeakRange
        {
            get
            {
                if (_speakRange < 0)
                    _speakRange = LoadSpeakRange();
                return _speakRange;
            }
        }

        private static double LoadSpeakRange()
        {
            foreach (var channel in MyDefinitionManager.GetOfType<MyChatChannelDefinition>())
            {
                if (!string.Equals(channel.Id.SubtypeName, SpeakChannelName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (channel.Senders == null)
                    return 0;

                foreach (var senderId in channel.Senders)
                {
                    MyChatSenderDefinition sender;
                    if (MyDefinitionManager.TryGet(senderId, out sender) && sender.Range.HasValue)
                        return sender.Range.Value;
                }
            }

            return 0;
        }

        private static string FormatSpeech(string speaker, string message)
        {
            return (string.IsNullOrWhiteSpace(speaker) ? "Soldier" : speaker)
                   + ": "
                   + message.Trim();
        }

        private static void BroadcastSpawn(SiNpc npc, SiNpcSpawnRequest request)
        {
            if (MyMultiplayerModApi.Static == null)
                return;

            MyMultiplayerModApi.Static.RaiseStaticEvent(
                x => SpawnNpcClient,
                CreateSnapshot(npc));
        }

        private static void BroadcastClear()
        {
            if (MyMultiplayerModApi.Static != null)
                MyMultiplayerModApi.Static.RaiseStaticEvent(x => ClearNpcsClient);
        }

        internal static void ReportNpcDamageBridgeHit(long entityId, MyDamageInformation damageInformation)
        {
            if (entityId == 0
                || damageInformation.Amount <= 0
                || MyMultiplayerModApi.Static == null
                || MyMultiplayerModApi.Static.IsServer)
                return;

            MyMultiplayerModApi.Static.RaiseStaticEvent(
                x => ApplyNpcDamageBridgeServer,
                entityId,
                damageInformation.Amount,
                damageInformation.Type.String);
        }

        private static void OnWaypointSet(long entityId, Vector3D waypoint)
        {
            if (MyMultiplayerModApi.Static != null && MyMultiplayerModApi.Static.IsServer)
                MyMultiplayerModApi.Static.RaiseStaticEvent(
                    x => SetNpcWaypointClient,
                    entityId,
                    waypoint);
        }

        private static void OnWaypointCleared(long entityId)
        {
            if (MyMultiplayerModApi.Static != null && MyMultiplayerModApi.Static.IsServer)
                MyMultiplayerModApi.Static.RaiseStaticEvent(
                    x => ClearNpcWaypointClient,
                    entityId);
        }

        [Event, Reliable, Broadcast]
        private static void SpeakNpcClient(Vector3D position, string speaker, string message)
        {
            if (MyMultiplayerModApi.Static != null && MyMultiplayerModApi.Static.IsServer)
                return;

            SpeakNpcLocal(position, speaker, message);
        }

        [Event, Reliable, Broadcast]
        private static void SpeakPlayerClient(Vector3D position, string speaker, string message)
        {
            if (MyMultiplayerModApi.Static != null && MyMultiplayerModApi.Static.IsServer)
                return;

            SpeakPlayerLocal(position, speaker, message);
        }

        [Event, Reliable, Broadcast]
        private static void SpawnNpcClient(SiNpcSnapshot snapshot)
        {
            _instance?.ApplyReplicatedNpcSnapshot(snapshot);
        }

        [Event, Reliable, Broadcast]
        private static void ClearNpcsClient()
        {
            _instance?.Npcs?.CloseAll();
            _instance?.Squads?.ClearNpcs();
            _instance?._squadOrders.Clear();
            _instance?._squadCombatStates.Clear();
        }

        [Event, Reliable, Broadcast]
        private static void SetNpcWaypointClient(long entityId, Vector3D waypoint)
        {
            _instance?.Npcs?.ApplyWaypoint(entityId, waypoint);
        }

        [Event, Reliable, Broadcast]
        private static void ClearNpcWaypointClient(long entityId)
        {
            _instance?.Npcs?.ApplyClearWaypoint(entityId);
        }

        [Event, Reliable, Server]
        private static void RequestNpcSnapshot()
        {
            if (_instance?.Npcs == null || MyMultiplayerModApi.Static == null)
                return;

            var endpoint = MyEventContext.Current.Sender;
            foreach (var npc in _instance.Npcs.Npcs.Values)
                MyMultiplayerModApi.Static.RaiseStaticEvent(
                    x => SpawnNpcSnapshotClient,
                    CreateSnapshot(npc),
                    endpoint);
        }

        [Event, Reliable, Client]
        private static void SpawnNpcSnapshotClient(SiNpcSnapshot snapshot)
        {
            SpawnNpcClient(snapshot);
        }

        private void ApplyReplicatedNpcSnapshot(SiNpcSnapshot snapshot)
        {
            if (snapshot.EntityId == 0 || Npcs == null)
                return;

            if (TryApplyReplicatedNpcSnapshot(snapshot))
            {
                _pendingNpcSnapshots.Remove(snapshot.EntityId);
                return;
            }

            _pendingNpcSnapshots[snapshot.EntityId] = snapshot;
        }

        private bool TryApplyReplicatedNpcSnapshot(SiNpcSnapshot snapshot)
        {
            if (Npcs == null)
                return false;

            if (!Npcs.TryAttachConfigured(
                    SiNpcManager.SoldierArchetype,
                    snapshot.Archetype,
                    snapshot.EntityId,
                    out var npc)
                || npc?.Entity == null)
                return false;

            var request = new SiNpcSpawnRequest(
                snapshot.WebbingSubtype,
                snapshot.IsParatrooper,
                snapshot.IsEnemy);
            string failure;
            if (!ApplySpawnRequest(npc, request, out failure))
                return false;

            if (snapshot.HasSquadAssignment)
                Squads?.AssignNpcToLeader(
                    npc,
                    (SiSquadLeaderKind)snapshot.SquadLeaderKind,
                    snapshot.SquadLeaderId,
                    (SiArmyKind)snapshot.SquadArmyKind,
                    snapshot.SquadArmyId,
                    snapshot.LeaderName,
                    snapshot.IsSquadLeader);
            if (snapshot.HasWaypoint)
                Npcs.ApplyWaypoint(snapshot.EntityId, snapshot.Waypoint);
            return true;
        }

        private void ApplyPendingNpcSnapshots()
        {
            if (_pendingNpcSnapshots.Count == 0)
                return;

            _resolvedPendingNpcIds.Clear();
            foreach (var entry in _pendingNpcSnapshots)
                if (TryApplyReplicatedNpcSnapshot(entry.Value))
                    _resolvedPendingNpcIds.Add(entry.Key);

            for (var i = 0; i < _resolvedPendingNpcIds.Count; i++)
                _pendingNpcSnapshots.Remove(_resolvedPendingNpcIds[i]);
        }

        private void OnEntityAddedClient(MyEntity entity)
        {
            if (entity == null || IsAuthoritative)
                return;
            if (!_pendingNpcSnapshots.TryGetValue(entity.EntityId, out var snapshot))
                return;

            if (TryApplyReplicatedNpcSnapshot(snapshot))
                _pendingNpcSnapshots.Remove(entity.EntityId);
        }

        [Event, Reliable, Server]
        private static void ApplyNpcDamageBridgeServer(long entityId, float amount, string damageType)
        {
            if (entityId == 0 || amount <= 0)
            {
                MyEventContext.ValidationFailed();
                return;
            }

            if (_instance?.Npcs == null)
                return;

            SiNpc npc;
            if (!_instance.Npcs.Npcs.TryGetValue(entityId, out npc))
                return;

            var bridge = npc.Entity?.Components.Get<SiNpcCharacterDamageBridgeComponent>();
            if (bridge == null)
                return;

            bridge.ApplyReplicatedDamage(
                new MyDamageInformation(
                    amount,
                    string.IsNullOrWhiteSpace(damageType)
                        ? MyStringHash.NullOrEmpty
                        : MyStringHash.GetOrCompute(damageType)));
        }

        [Event, Reliable, Server]
        private static void RequestUtilityCommandServer(byte command)
        {
            if (!Enum.IsDefined(typeof(SiUtilityCommandMenuCommand), (int)command))
            {
                MyEventContext.ValidationFailed();
                return;
            }

            var player = MyPlayers.Static.GetPlayer(new MyPlayer.PlayerId(MyEventContext.Current.Sender.Value, 0));
            if (player?.Identity == null)
            {
                MyEventContext.ValidationFailed();
                return;
            }

            _instance?.ExecuteUtilityCommand(player, (SiUtilityCommandMenuCommand)command);
        }

        [Event, Reliable, Server]
        private static void RequestAiSquadMoveOrderServer(
            byte leaderKind,
            long leaderId,
            byte armyKind,
            long armyId,
            Vector3D target)
        {
            if (!Enum.IsDefined(typeof(SiSquadLeaderKind), (int)leaderKind)
                || !Enum.IsDefined(typeof(SiArmyKind), (int)armyKind)
                || (SiSquadLeaderKind)leaderKind != SiSquadLeaderKind.Ai
                || leaderId == 0)
            {
                MyEventContext.ValidationFailed();
                return;
            }

            var player = MyPlayers.Static.GetPlayer(new MyPlayer.PlayerId(MyEventContext.Current.Sender.Value, 0));
            if (player?.Identity == null)
            {
                MyEventContext.ValidationFailed();
                return;
            }

            _instance?.ApplyAiSquadMoveOrder(
                player,
                new SiSquadLeaderKey(
                    (SiSquadLeaderKind)leaderKind,
                    leaderId,
                    new SiArmyKey((SiArmyKind)armyKind, armyId)),
                target);
        }
    }
}
