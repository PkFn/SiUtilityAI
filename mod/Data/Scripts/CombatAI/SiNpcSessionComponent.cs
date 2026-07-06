using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Controller;
using Medieval.GameSystems.Factions;
using Sandbox.Definitions.Chat;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems.Chat;
using Sandbox.Game.Players;
using Sandbox.ModAPI;
using SiCore.Core.Debug;
using SiCore.Core.Grid;
using VRage;
using VRage.Components;
using VRage.Components.Entity.CubeGrid;
using VRage.Components.Interfaces;
using VRage.Game;
using VRage.Entities.Gravity;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Scene;
using VRage.Game.Entity.EntityComponents;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Components;
using VRage.Session;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace Si.UtilityAI
{
    [StaticEventOwner]
    [MySessionComponent(typeof(MyObjectBuilder_SiNpcSessionComponent), AllowAutomaticCreation = true, AlwaysOn = true)]
    [MyDependency(typeof(MyChatSystem), Critical = false)]
    public sealed class SiNpcSessionComponent : MySessionComponent, IDraw
    {
        private const string Command = "/si-npc";
        private const string EnemyCommand = "/si-enemy";
        private const string SquadCommand = "/si-squad";
        private const double SpawnDistance = 2.5;
        private const long CombatStanceCooldownMilliseconds = 60000;
        private const long CoverScanCacheLifetimeMilliseconds = 1000;
        private const long CoverSearchCacheLifetimeMilliseconds = 750;
        private const double CoverScanCachePositionQuantization = 6.0;
        private const double CoverSearchCachePositionQuantization = 8.0;
        private const double CombatStanceNearbyEnemyDistance = 80;
        private static readonly Vector2 SpottingTextAnchor = new Vector2(-0.98f, -0.92f);
        private const string SpeakChannelName = "Speak";
        private static readonly MyStringHash HostileRelationship = MyStringHash.GetOrCompute("War");
        private static readonly MyStringHash SpeakChannel = MyStringHash.GetOrCompute(SpeakChannelName);

        private static SiNpcSessionComponent _instance;
        private static double _speakRange = -1;
        private readonly Dictionary<long, SiSquadCommandState> _squadOrders =
            new Dictionary<long, SiSquadCommandState>();
        private readonly Dictionary<SiSquadLeaderKey, SiSquadCombatState> _squadCombatStates =
            new Dictionary<SiSquadLeaderKey, SiSquadCombatState>();
        private readonly Dictionary<long, SiMotionState> _leaderMotionStates =
            new Dictionary<long, SiMotionState>();
        private readonly Dictionary<long, SiMotionState> _npcMotionStates =
            new Dictionary<long, SiMotionState>();
        private readonly Dictionary<long, long> _pendingControlledEntityBindings =
            new Dictionary<long, long>();
        private readonly Dictionary<long, SiNpcSnapshot> _pendingNpcSnapshots =
            new Dictionary<long, SiNpcSnapshot>();
        private readonly Dictionary<long, SiNpcPositionCacheState> _positionCache =
            new Dictionary<long, SiNpcPositionCacheState>();
        private readonly Dictionary<long, SiTransportNpcState> _transportNpcStates =
            new Dictionary<long, SiTransportNpcState>();
        private readonly Dictionary<long, SiCoverReservation> _coverReservations =
            new Dictionary<long, SiCoverReservation>();
        private readonly Dictionary<SiCoverScanCacheKey, SiCoverScanCacheEntry> _coverScanCache =
            new Dictionary<SiCoverScanCacheKey, SiCoverScanCacheEntry>();
        private readonly Dictionary<SiCoverSearchCacheKey, SiCoverSearchCacheEntry> _coverSearchCache =
            new Dictionary<SiCoverSearchCacheKey, SiCoverSearchCacheEntry>();
        private readonly List<SiCoverScanCacheKey> _expiredCoverScanCacheKeys =
            new List<SiCoverScanCacheKey>();
        private readonly List<SiCoverSearchCacheKey> _expiredCoverSearchCacheKeys =
            new List<SiCoverSearchCacheKey>();
        private readonly List<long> _staleCoverReservationIds = new List<long>();
        private readonly List<long> _resolvedPendingControlledEntityNpcIds = new List<long>();
        private readonly List<long> _pendingTransportSeatRestoreNpcIds = new List<long>();
        private readonly List<long> _resolvedPendingNpcIds = new List<long>();
        private List<MyObjectBuilder_SiNpcSessionComponent.SavedNpc> _savedNpcs;
        private List<MyObjectBuilder_SiNpcSessionComponent.SquadOrder> _savedSquadOrders;

        [Automatic]
        private readonly MyChatSystem _chat = null;
        private bool _showTroopMarkers;
        private bool _showSquadChatter;
        private bool _utilityDecisionMakingEnabled = true;
        private readonly SiGameLog _log = new SiGameLog(nameof(SiNpcSessionComponent), "[SiCover]");
        private long _lastCoverCleanupLogTime = long.MinValue;

        public static SiNpcSessionComponent Instance => _instance;
        public SiNpcManager Npcs { get; private set; }
        internal SiSquadBook Squads { get; private set; }
        internal SiSpottingSystem Spotting { get; private set; }
        internal bool ShowSquadChatter => _showSquadChatter;
        internal bool UtilityDecisionMakingEnabled => _utilityDecisionMakingEnabled;

        protected override void OnLoad()
        {
            base.OnLoad();
            _instance = this;
            Npcs = new SiNpcManager();
            Squads = new SiSquadBook();
            Spotting = new SiSpottingSystem(this);
            Npcs.WaypointSet += OnWaypointSet;
            Npcs.WaypointCleared += OnWaypointCleared;
            Npcs.NpcSpoke += OnNpcSpoke;
            if (!IsAuthoritative)
                MyEntities.OnEntityAdd += OnEntityAddedClient;

            _chat?.RegisterChatCommand(
                Command,
                HandleCommand,
                "Manage custom Si Utility AI NPCs. /si-npc spawn <webbing> [paratrooper] [enemy] | spawn-enemy [webbing] | list | clear | utility-ai [toggle|on|off|status] | gamelog [toggle|on|off|status]",
                MyChatCommandType.Server);
            _chat?.RegisterChatCommand(
                EnemyCommand,
                HandleEnemyCommand,
                "Spawn a hostile test Si Utility AI trooper. /si-enemy [spawn] [webbing]",
                MyChatCommandType.Server);
            _chat?.RegisterChatCommand(
                SquadCommand,
                HandleSquadCommand,
                "Show Si Utility AI squad rosters. /si-squad list | members",
                MyChatCommandType.Server);
        }

        protected override void OnSessionReady()
        {
            base.OnSessionReady();
            if (IsAuthoritative)
                RestoreSavedState();
            else if (MyMultiplayerModApi.Static != null)
                MyMultiplayerModApi.Static.RaiseStaticEvent(x => RequestNpcSnapshot);
        }

        protected override void OnUnload()
        {
            if (Npcs != null)
            {
                Npcs.WaypointSet -= OnWaypointSet;
                Npcs.WaypointCleared -= OnWaypointCleared;
                Npcs.NpcSpoke -= OnNpcSpoke;
            }
            Npcs?.CloseAll(false);
            Npcs = null;
            _squadOrders.Clear();
            _squadCombatStates.Clear();
            _leaderMotionStates.Clear();
            _npcMotionStates.Clear();
            _pendingControlledEntityBindings.Clear();
            _pendingNpcSnapshots.Clear();
            _positionCache.Clear();
            _transportNpcStates.Clear();
            _coverReservations.Clear();
            _coverScanCache.Clear();
            _coverSearchCache.Clear();
            _expiredCoverScanCacheKeys.Clear();
            _expiredCoverSearchCacheKeys.Clear();
            _staleCoverReservationIds.Clear();
            _resolvedPendingControlledEntityNpcIds.Clear();
            _pendingTransportSeatRestoreNpcIds.Clear();
            _resolvedPendingNpcIds.Clear();
            Spotting?.Clear();
            Spotting = null;
            _savedNpcs = null;
            _savedSquadOrders = null;
            Squads?.ClearNpcs();
            Squads = null;
            if (!IsAuthoritative)
                MyEntities.OnEntityAdd -= OnEntityAddedClient;
            if (_instance == this)
                _instance = null;
            base.OnUnload();
        }

        [Update(100)]
        private void UpdateNpcs(long elapsedMilliseconds)
        {
            if (IsAuthoritative)
            {
                ApplyPendingControlledEntityBindings();
                UpdateTrackedMotionStates();
                UpdateSquadOrders();
                UpdateCombatStances();
                CleanupTransportStates();
                CleanupPositionCache();
                CleanupExpiredCoverScanCache();
                CleanupExpiredCoverSearchCache();
                CleanupCoverReservations();
                RestorePendingTransportSeats();
            }
            else
                ApplyPendingNpcSnapshots();
            Npcs?.Update(elapsedMilliseconds);
            if (IsAuthoritative)
            {
                ReassignLeaderlessSquads();
                Spotting?.Update(elapsedMilliseconds);
            }
        }

        protected override bool IsSerialized =>
            (Npcs != null && Npcs.Npcs.Count > 0)
            || (_savedNpcs != null && _savedNpcs.Count > 0)
            || _squadOrders.Count > 0
            || (_savedSquadOrders != null && _savedSquadOrders.Count > 0);

        protected override MyObjectBuilder_SessionComponent Serialize()
        {
            var ob = (MyObjectBuilder_SiNpcSessionComponent)base.Serialize();

            var npcs = Npcs != null ? CreateSavedNpcs() : _savedNpcs;
            ob.Npcs = npcs != null && npcs.Count > 0 ? npcs : null;

            var orders = _squadOrders.Count > 0 ? CreateSavedSquadOrders() : _savedSquadOrders;
            ob.SquadOrders = orders != null && orders.Count > 0 ? orders : null;
            return ob;
        }

        protected override void Deserialize(MyObjectBuilder_SessionComponent objectBuilder)
        {
            base.Deserialize(objectBuilder);
            var ob = (MyObjectBuilder_SiNpcSessionComponent)objectBuilder;
            _savedNpcs = ob.Npcs;
            _savedSquadOrders = ob.SquadOrders;
        }

        internal void RequestUtilityCommand(SiUtilityCommandMenuCommand command)
        {
            switch(command)
            {
                case SiUtilityCommandMenuCommand.ToggleUi:
                    ToggleTroopMarkers();
                    return;
                case SiUtilityCommandMenuCommand.ToggleSquadChatter:
                    ToggleSquadChatter();
                    return;
                default:
                    break;
            }

            if (MyMultiplayerModApi.Static != null && !MyMultiplayerModApi.Static.IsServer)
            {
                MyMultiplayerModApi.Static.RaiseStaticEvent(
                    x => RequestUtilityCommandServer,
                    (byte)command);
                return;
            }

            ExecuteUtilityCommand(LocalPlayer(), command);
        }

        internal SiSquadEngagementStance GetEngagementStance(SiNpc npc)
        {
            if (npc == null || Squads == null)
                return SiSquadEngagementStance.Enemies;

            SiAssignedNpc assignment;
            if (!Squads.TryGetAssignment(npc.EntityId, out assignment)
                || assignment.Leader.Kind != SiSquadLeaderKind.Player)
                return SiSquadEngagementStance.Enemies;

            SiSquadCommandState state;
            return _squadOrders.TryGetValue(assignment.Leader.Id, out state)
                ? state.EngagementStance
                : SiSquadEngagementStance.Enemies;
        }

        internal SiSquadCombatStance GetCombatStance(SiNpc npc)
        {
            if (npc == null || Squads == null)
                return SiSquadCombatStance.Safe;

            SiAssignedNpc assignment;
            if (!Squads.TryGetAssignment(npc.EntityId, out assignment))
                return SiSquadCombatStance.Safe;

            return GetOrCreateCombatState(assignment.Leader).Stance;
        }

        private SiSquadCombatStance GetCombatStance(SiSquadLeaderKey leader) =>
            GetOrCreateCombatState(leader).Stance;

        internal bool IsFollowingPlayer(SiNpc npc)
        {
            if (npc == null || Squads == null)
                return false;

            SiAssignedNpc assignment;
            if (!Squads.TryGetAssignment(npc.EntityId, out assignment)
                || assignment.Leader.Kind != SiSquadLeaderKind.Player)
                return false;

            SiSquadCommandState state;
            return _squadOrders.TryGetValue(assignment.Leader.Id, out state)
                   && state.Mode == SiSquadOrderMode.Follow;
        }

        internal bool TryGetTransportMode(SiNpc npc, out SiSquadTransportMode mode)
        {
            mode = SiSquadTransportMode.None;
            if (npc == null || Squads == null)
                return false;

            SiAssignedNpc assignment;
            if (!Squads.TryGetAssignment(npc.EntityId, out assignment)
                || assignment.Leader.Kind != SiSquadLeaderKind.Player)
                return false;

            SiSquadCommandState state;
            if (!_squadOrders.TryGetValue(assignment.Leader.Id, out state)
                || state.TransportMode == SiSquadTransportMode.None
                || state.TransportVehicleEntityId == 0)
                return false;

            if (MyEntities.GetEntityByIdOrDefault(state.TransportVehicleEntityId) == null)
            {
                state.TransportMode = SiSquadTransportMode.None;
                state.TransportVehicleEntityId = 0;
                return false;
            }

            mode = state.TransportMode;
            return true;
        }

        internal bool TryConsumeTransportActionSlot(
            SiNpc npc,
            SiSquadTransportMode mode,
            long intervalMilliseconds)
        {
            if (npc == null || mode == SiSquadTransportMode.None || Squads == null)
                return false;

            SiAssignedNpc assignment;
            if (!Squads.TryGetAssignment(npc.EntityId, out assignment)
                || assignment.Leader.Kind != SiSquadLeaderKind.Player)
                return false;

            if (!_squadOrders.TryGetValue(assignment.Leader.Id, out var state)
                || state == null
                || state.TransportMode != mode
                || state.TransportVehicleEntityId == 0)
                return false;

            if (state.TransportCadenceMode != mode)
            {
                state.TransportCadenceMode = mode;
                state.NextTransportActionTimeMilliseconds = 0;
            }

            var now = CurrentTimeMilliseconds();
            if (state.NextTransportActionTimeMilliseconds > now)
                return false;

            state.NextTransportActionTimeMilliseconds = now + Math.Max(0L, intervalMilliseconds);
            return true;
        }

        internal bool TryGetAssignedTransportSeat(
            SiNpc npc,
            out EquiPlayerAttachmentComponent.Slot slot)
        {
            slot = null;
            if (npc == null || npc.Entity == null || npc.Entity.Closed || npc.Entity.MarkedForClose || Squads == null)
                return false;

            SiAssignedNpc assignment;
            if (!Squads.TryGetAssignment(npc.EntityId, out assignment)
                || assignment.Leader.Kind != SiSquadLeaderKind.Player)
                return false;

            SiSquadCommandState order;
            if (!_squadOrders.TryGetValue(assignment.Leader.Id, out order)
                || order.TransportMode == SiSquadTransportMode.None
                || order.TransportVehicleEntityId == 0)
                return false;

            if (!TryAssignTransportSeat(npc, order.TransportVehicleEntityId, out var state))
                return false;

            return TryResolveTransportSeat(state, out slot);
        }

        internal bool IsAssignedTransportSeat(SiNpc npc, EquiPlayerAttachmentComponent.Slot slot)
        {
            if (npc == null || slot == null)
                return false;
            if (!_transportNpcStates.TryGetValue(npc.EntityId, out var state))
                return false;

            return state.SeatEntityId == (slot.Controllable?.Entity?.EntityId ?? 0)
                   && string.Equals(state.SeatSlotName, slot.Definition.Name, StringComparison.Ordinal);
        }

        internal void RecordTransportExitPosition(SiNpc npc, in Vector3D worldPosition)
        {
            if (npc == null)
                return;
            if (!_transportNpcStates.TryGetValue(npc.EntityId, out var state))
                return;
            if (!TryGetTransportVehicleEntity(state.VehicleEntityId, out var vehicle))
                return;

            state.ExitLocalPosition = Vector3D.Transform(worldPosition, vehicle.PositionComp.WorldMatrixInvScaled);
            state.HasExitLocalPosition = true;
        }

        internal bool TryGetTransportExitWorldPosition(SiNpc npc, out Vector3D worldPosition)
        {
            worldPosition = Vector3D.Zero;
            if (npc == null)
                return false;
            if (!_transportNpcStates.TryGetValue(npc.EntityId, out var state) || !state.HasExitLocalPosition)
                return false;
            if (!TryGetTransportVehicleEntity(state.VehicleEntityId, out var vehicle))
                return false;

            worldPosition = Vector3D.Transform(state.ExitLocalPosition, vehicle.PositionComp.WorldMatrix);
            return true;
        }

        internal void CompleteTransportOrder(SiNpc npc)
        {
            if (npc == null || Squads == null)
                return;

            SiAssignedNpc assignment;
            if (!Squads.TryGetAssignment(npc.EntityId, out assignment)
                || assignment.Leader.Kind != SiSquadLeaderKind.Player)
            {
                _transportNpcStates.Remove(npc.EntityId);
                return;
            }

            _transportNpcStates.Remove(npc.EntityId);
            if (!HasActiveTransportStateForLeader(assignment.Leader.Id)
                && _squadOrders.TryGetValue(assignment.Leader.Id, out var state)
                && state.TransportMode != SiSquadTransportMode.None)
            {
                state.TransportMode = SiSquadTransportMode.None;
                state.TransportVehicleEntityId = 0;
                ResetTransportCadence(state);
            }
        }

        internal bool TryGetLeaderPosition(SiNpc npc, out Vector3D position)
        {
            position = Vector3D.Zero;
            if (npc == null || Squads == null)
                return false;

            SiAssignedNpc assignment;
            if (!Squads.TryGetAssignment(npc.EntityId, out assignment))
                return false;

            if (assignment.Leader.Kind == SiSquadLeaderKind.Player)
            {
                MatrixD leaderTransform;
                if (!TryGetLeaderTransform(assignment.Leader.Id, out leaderTransform))
                    return false;

                position = leaderTransform.Translation;
                return true;
            }

            SiNpc leaderNpc;
            if (Npcs == null
                || !Npcs.Npcs.TryGetValue(assignment.Leader.Id, out leaderNpc)
                || leaderNpc?.Entity == null)
                return false;

            position = leaderNpc.Entity.WorldMatrix.Translation;
            return true;
        }

        internal bool TryGetLeaderDistance(SiNpc npc, out double distance)
        {
            distance = 0;
            Vector3D leaderPosition;
            if (npc?.Entity == null || !TryGetLeaderPosition(npc, out leaderPosition))
                return false;

            distance = Vector3D.Distance(npc.Entity.WorldMatrix.Translation, leaderPosition);
            return true;
        }

        internal long GetCombatEntryToken(SiNpc npc)
        {
            if (npc == null || Squads == null)
                return 0;

            SiAssignedNpc assignment;
            return Squads.TryGetAssignment(npc.EntityId, out assignment)
                ? GetOrCreateCombatState(assignment.Leader).CombatEntryToken
                : 0;
        }

        internal bool IsCoverAvailable(SiNpc requester, in Vector3D coverPosition, double radius)
        {
            if (requester == null)
                return false;

            foreach (var reservation in _coverReservations)
            {
                if (reservation.Key == requester.EntityId)
                    continue;

                var claimed = reservation.Value;
                var threshold = Math.Max(radius, claimed.Radius);
                if (Vector3D.DistanceSquared(claimed.Position, coverPosition) <= threshold * threshold)
                    return false;
            }

            return true;
        }

        internal bool TryReserveCover(SiNpc requester, in Vector3D coverPosition, double radius)
        {
            if (requester == null)
                return false;
            if (!IsCoverAvailable(requester, coverPosition, radius))
                return false;

            _coverReservations[requester.EntityId] = new SiCoverReservation(coverPosition, radius);
            return true;
        }

        internal void ReleaseCover(long entityId)
        {
            if (entityId != 0)
                _coverReservations.Remove(entityId);
        }

        internal void CacheFormationPosition(SiNpc npc, in Vector3D position)
        {
            CachePosition(npc?.EntityId ?? 0, SiNpcCachedPositionKind.Formation, position);
        }

        internal void CacheCombatPosition(SiNpc npc, SiCombatMovementRole role, in Vector3D position)
        {
            CachePosition(npc?.EntityId ?? 0, ToCachedPositionKind(role), position);
        }

        internal void ClearCachedCombatPosition(long entityId, SiCombatMovementRole role)
        {
            ClearCachedPosition(entityId, ToCachedPositionKind(role));
        }

        internal bool HasNearbyCachedCombatPosition(
            SiNpc requester,
            in Vector3D position,
            double minimumDistance,
            out long blockingEntityId)
        {
            blockingEntityId = 0;
            if (minimumDistance <= 0 || _positionCache.Count == 0)
                return false;

            var requesterId = requester?.EntityId ?? 0;
            var minimumDistanceSquared = minimumDistance * minimumDistance;
            foreach (var entry in _positionCache)
            {
                if (entry.Key == requesterId || entry.Value == null)
                    continue;

                if (entry.Value.HasCover
                    && Vector3D.DistanceSquared(entry.Value.CoverPosition, position) < minimumDistanceSquared)
                {
                    blockingEntityId = entry.Key;
                    return true;
                }

                if (entry.Value.HasPlainView
                    && Vector3D.DistanceSquared(entry.Value.PlainViewPosition, position) < minimumDistanceSquared)
                {
                    blockingEntityId = entry.Key;
                    return true;
                }
            }

            return false;
        }

        internal bool TryFollowCachedFormationPosition(SiNpc npc, double refreshDistanceSquared)
        {
            return TryFollowCachedPosition(npc, SiNpcCachedPositionKind.Formation, refreshDistanceSquared);
        }

        internal bool TryFollowCachedCombatPosition(
            SiNpc npc,
            SiCombatMovementRole role,
            double refreshDistanceSquared)
        {
            return TryFollowCachedPosition(npc, ToCachedPositionKind(role), refreshDistanceSquared);
        }

        internal bool HasSquadmateInThrowDanger(
            SiNpc requester,
            long requesterEntityId,
            in Vector3D throwOrigin,
            in Vector3D targetPosition,
            double blastRadius,
            double trajectoryRadius,
            out long blockingEntityId,
            out Vector3D blockingPosition)
        {
            blockingEntityId = 0;
            blockingPosition = Vector3D.Zero;
            if (requester == null || Squads == null || Npcs == null)
                return false;

            SiAssignedNpc requesterAssignment;
            if (!Squads.TryGetAssignment(requester.EntityId, out requesterAssignment))
                return false;

            foreach (var entry in Npcs.Npcs)
            {
                var squadmate = entry.Value;
                if (squadmate == null || entry.Key == requesterEntityId)
                    continue;

                SiAssignedNpc assignment;
                if (!Squads.TryGetAssignment(entry.Key, out assignment)
                    || !assignment.Leader.Equals(requesterAssignment.Leader))
                    continue;

                if (TryGetUnsafeThrowPosition(
                        entry.Key,
                        squadmate,
                        throwOrigin,
                        targetPosition,
                        blastRadius,
                        trajectoryRadius,
                        out blockingPosition))
                {
                    blockingEntityId = entry.Key;
                    return true;
                }
            }

            return false;
        }

        internal bool TryGetCachedCoverSearch(
            in Vector3D searchOrigin,
            double searchRadius,
            in Vector3D threatPosition,
            long threatEntityId,
            MyDefinitionId behaviorDefinitionId,
            out SiCoverSearchCacheEntry entry)
        {
            entry = null;
            var key = new SiCoverSearchCacheKey(
                searchOrigin,
                searchRadius,
                threatPosition,
                threatEntityId,
                behaviorDefinitionId,
                CoverSearchCachePositionQuantization);
            SiCoverSearchCacheEntry cached;
            if (!_coverSearchCache.TryGetValue(key, out cached))
                return false;

            var now = CurrentTimeMilliseconds();
            if (cached == null || cached.ExpiresAtMilliseconds < now)
            {
                _coverSearchCache.Remove(key);
                return false;
            }

            entry = cached;
            return true;
        }

        internal void StoreCachedCoverSearch(
            in Vector3D searchOrigin,
            double searchRadius,
            in Vector3D threatPosition,
            long threatEntityId,
            MyDefinitionId behaviorDefinitionId,
            SiCoverSearchCacheEntry entry)
        {
            if (entry == null)
                return;

            entry.ExpiresAtMilliseconds = CurrentTimeMilliseconds() + CoverSearchCacheLifetimeMilliseconds;
            var key = new SiCoverSearchCacheKey(
                searchOrigin,
                searchRadius,
                threatPosition,
                threatEntityId,
                behaviorDefinitionId,
                CoverSearchCachePositionQuantization);
            _coverSearchCache[key] = entry;
        }

        internal bool TryGetCachedCoverScan(
            in Vector3D searchOrigin,
            double searchRadius,
            MyDefinitionId behaviorDefinitionId,
            out SiCoverScanCacheEntry entry)
        {
            entry = null;
            var key = new SiCoverScanCacheKey(
                searchOrigin,
                searchRadius,
                behaviorDefinitionId,
                CoverScanCachePositionQuantization);
            SiCoverScanCacheEntry cached;
            if (!_coverScanCache.TryGetValue(key, out cached))
                return false;

            var now = CurrentTimeMilliseconds();
            if (cached == null || cached.ExpiresAtMilliseconds < now)
            {
                _coverScanCache.Remove(key);
                return false;
            }

            entry = cached;
            return true;
        }

        internal void StoreCachedCoverScan(
            in Vector3D searchOrigin,
            double searchRadius,
            MyDefinitionId behaviorDefinitionId,
            SiCoverScanCacheEntry entry)
        {
            if (entry == null)
                return;

            entry.ExpiresAtMilliseconds = CurrentTimeMilliseconds() + CoverScanCacheLifetimeMilliseconds;
            var key = new SiCoverScanCacheKey(
                searchOrigin,
                searchRadius,
                behaviorDefinitionId,
                CoverScanCachePositionQuantization);
            _coverScanCache[key] = entry;
        }

        public void Draw()
        {
            var player = LocalPlayer();
            if (player?.Identity == null)
                return;

            DrawPlayerSpottingOverlay(player);

            if (!_showTroopMarkers || Npcs == null || Squads == null)
                return;

            var definition = Squads.Definition;
            if (definition == null || definition.MarkerTextScale <= 0)
                return;

            var camera = MyAPIGateway.Session?.Camera;
            if (camera == null)
                return;

            var cameraPosition = camera.WorldMatrix.Translation;
            var cameraForward = camera.WorldMatrix.Forward;
            var maxDistanceSquared = definition.MarkerMaxDistance * definition.MarkerMaxDistance;
            foreach (var marker in Squads.CreateNpcMarkers(Npcs, player.Identity.Id))
            {
                var entity = marker.Npc.Entity;
                if (entity == null || entity.Closed || entity.MarkedForClose)
                    continue;

                var position = entity.WorldMatrix.Translation;
                var toMarker = position - cameraPosition;
                if (definition.MarkerMaxDistance > 0
                    && toMarker.LengthSquared() > maxDistanceSquared)
                    continue;
                if (toMarker.LengthSquared() > 0.0001
                    && Vector3D.Dot(cameraForward, toMarker) <= 0)
                    continue;

                var label = marker.Label;
                float healthCurrent;
                float healthMax;
                if (marker.Npc.TryGetHealth(out healthCurrent, out healthMax))
                    label += $"\nHealth {healthCurrent:0}/{healthMax:0}";

                MyRenderProxy.DebugDrawText3D(
                    position + entity.WorldMatrix.Up * definition.MarkerHeight,
                    label,
                    Color.LightGreen,
                    definition.MarkerTextScale,
                    align: MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER);
            }
        }

        private void DrawPlayerSpottingOverlay(MyPlayer player)
        {
            var controlledEntity = player?.ControlledEntity as MyEntity;
            if (controlledEntity == null || Npcs == null)
                return;

            var highestSpottingSum = 0f;
            var highestSpottingThreshold = 1f;
            var isSpotted = false;
            foreach (var npc in Npcs.Npcs.Values)
            {
                var entity = npc?.Entity;
                if (entity == null || entity.Closed || entity.MarkedForClose)
                    continue;

                var behavior = entity.Components.Get<SiShootOpposingNpcBehaviorComponent>();
                if (behavior == null)
                    continue;

                SiSpottingObservation observation;
                if (!behavior.TryObservePlayer(npc, player, controlledEntity, this, out observation))
                    continue;

                var observationSum = observation.VehicleSpotted
                    ? observation.VehicleSpottingSum
                    : observation.SpottingSum;
                var observationThreshold = observation.VehicleSpotted
                    ? observation.VehicleSpottingThreshold
                    : observation.SpottingThreshold;
                var observationSpotted = observation.VehicleSpotted || observation.IsSpotted;
                if (observationSum > highestSpottingSum)
                {
                    highestSpottingSum = observationSum;
                    highestSpottingThreshold = observationThreshold;
                    isSpotted = observationSpotted;
                }
            }

            var clampedSum = MathHelper.Clamp(highestSpottingSum, 0, 1);
            var clampedThreshold = MathHelper.Clamp(highestSpottingThreshold, 0, 1);
            var color = isSpotted
                ? Color.OrangeRed
                : Color.Lerp(Color.LightGreen, Color.OrangeRed, Math.Max(clampedSum, clampedThreshold));
            MyRenderProxy.DebugDrawText2D(
                SpottingTextAnchor,
                $"Enemy spotting: {(isSpotted ? "spotted" : "hidden")} | sum {clampedSum:0.00} / thr {clampedThreshold:0.00}",
                color,
                0.8f);
        }

        private void NotifyShow(string text)
        {
            MyAPIGateway.Utilities?.ShowNotification(
                text,
                1500);
        }
        private void ToggleTroopMarkers()
        {
            _showTroopMarkers = !_showTroopMarkers;
            NotifyShow($"Troop markers {(_showTroopMarkers ? "shown" : "hidden")}.");
        }

        private void ToggleSquadChatter()
        {
            _showSquadChatter = !_showSquadChatter;
            NotifyShow($"Squad chatter {(_showSquadChatter ? "enabled" : "disabled")}.");
        }

        private bool HandleUtilityAiCommand(ulong sender, string[] tokens)
        {
            var action = tokens.Length >= 3 ? tokens[2].ToLowerInvariant() : "toggle";
            switch (action)
            {
                case "toggle":
                    _utilityDecisionMakingEnabled = !_utilityDecisionMakingEnabled;
                    return Respond(sender, UtilityAiDecisionMakingStatusText());
                case "on":
                    _utilityDecisionMakingEnabled = true;
                    return Respond(sender, UtilityAiDecisionMakingStatusText());
                case "off":
                    _utilityDecisionMakingEnabled = false;
                    return Respond(sender, UtilityAiDecisionMakingStatusText());
                case "status":
                    return Respond(sender, UtilityAiDecisionMakingStatusText());
                default:
                    return Respond(sender, $"{Command} utility-ai [toggle|on|off|status]");
            }
        }

        private bool HandleGameLogCommand(ulong sender, string[] tokens)
        {
            var action = tokens.Length >= 3 ? tokens[2].ToLowerInvariant() : "toggle";
            switch (action)
            {
                case "toggle":
                    SiGameLog.SetEnabled(!SiGameLog.Enabled);
                    return Respond(sender, SiGameLog.StatusText());
                case "on":
                    SiGameLog.SetEnabled(true);
                    return Respond(sender, SiGameLog.StatusText());
                case "off":
                    SiGameLog.SetEnabled(false);
                    return Respond(sender, SiGameLog.StatusText());
                case "status":
                    return Respond(sender, SiGameLog.StatusText());
                default:
                    return Respond(sender, $"{Command} gamelog [toggle|on|off|status]");
            }
        }

        private void SpeakPlayerCommand(MyPlayer player, SiUtilityCommandMenuCommand command)
        {
            var message = UtilityCommandSpeech(command);
            if (string.IsNullOrWhiteSpace(message) || player == null)
                return;

            Vector3D position;
            if (!TryGetPlayerSpeakPosition(player, out position))
                return;

            var speaker = PlayerName(player);
            SpeakPlayerLocal(position, speaker, message);
            if (MyMultiplayerModApi.Static != null
                && MyMultiplayerModApi.Static.IsServer
                && CanAnyPlayerHearSpeech(position))
            {
                MyMultiplayerModApi.Static.RaiseStaticEvent(
                    x => SpeakPlayerClient,
                    position,
                    speaker,
                    message);
            }
        }

        private static string UtilityCommandSpeech(SiUtilityCommandMenuCommand command)
        {
            switch (command)
            {
                case SiUtilityCommandMenuCommand.Stop:
                    return "Halt";
                case SiUtilityCommandMenuCommand.Follow:
                    return "Follow me";
                case SiUtilityCommandMenuCommand.FormationColumn:
                    return "Form column";
                case SiUtilityCommandMenuCommand.FormationFile:
                    return "Form file";
                case SiUtilityCommandMenuCommand.FormationLine:
                    return "Form line";
                case SiUtilityCommandMenuCommand.FormationVee:
                    return "Form vee";
                case SiUtilityCommandMenuCommand.EngagementEnemiesNeutrals:
                    return "Weapons free";
                case SiUtilityCommandMenuCommand.EngagementEnemies:
                    return "Engage enemies";
                case SiUtilityCommandMenuCommand.EngagementHoldFire:
                    return "Hold fire";
                case SiUtilityCommandMenuCommand.CombatSafe:
                    return "Safe movement";
                case SiUtilityCommandMenuCommand.CombatCombat:
                    return "Combat movement";
                case SiUtilityCommandMenuCommand.TransportationGetIn:
                    return "Mount up";
                case SiUtilityCommandMenuCommand.TransportationDisembark:
                    return "Disembark";
                default:
                    return null;
            }
        }

        private void ExecuteUtilityCommand(MyPlayer player, SiUtilityCommandMenuCommand command)
        {
            if (player?.Identity == null)
                return;

            var sender = player.Id.SteamId;
            var leaderIdentityId = player.Identity.Id;
            SpeakPlayerCommand(player, command);
            switch (command)
            {
                case SiUtilityCommandMenuCommand.Info:
                    RespondCurrentSquadInfo(sender, leaderIdentityId);
                    return;
                case SiUtilityCommandMenuCommand.Stop:
                    StopSquad(sender, leaderIdentityId);
                    return;
                case SiUtilityCommandMenuCommand.Follow:
                    FollowSquad(sender, leaderIdentityId);
                    return;
                case SiUtilityCommandMenuCommand.FormationColumn:
                    SetFormation(sender, leaderIdentityId, SiSquadFormation.Column);
                    return;
                case SiUtilityCommandMenuCommand.FormationFile:
                    SetFormation(sender, leaderIdentityId, SiSquadFormation.File);
                    return;
                case SiUtilityCommandMenuCommand.FormationLine:
                    SetFormation(sender, leaderIdentityId, SiSquadFormation.Line);
                    return;
                case SiUtilityCommandMenuCommand.FormationVee:
                    SetFormation(sender, leaderIdentityId, SiSquadFormation.Vee);
                    return;
                case SiUtilityCommandMenuCommand.EngagementEnemiesNeutrals:
                    SetEngagementStance(leaderIdentityId, SiSquadEngagementStance.EnemiesNeutrals);
                    return;
                case SiUtilityCommandMenuCommand.EngagementEnemies:
                    SetEngagementStance(leaderIdentityId, SiSquadEngagementStance.Enemies);
                    return;
                case SiUtilityCommandMenuCommand.EngagementHoldFire:
                    SetEngagementStance(leaderIdentityId, SiSquadEngagementStance.HoldFire);
                    return;
                case SiUtilityCommandMenuCommand.CombatSafe:
                    SetCombatStance(
                        PlayerLeaderKey(leaderIdentityId),
                        player.Identity.DisplayName,
                        SiSquadCombatStance.Safe,
                        SiSquadCombatTransitionReason.PlayerOrder,
                        true);
                    return;
                case SiUtilityCommandMenuCommand.CombatCombat:
                    SetCombatStance(
                        PlayerLeaderKey(leaderIdentityId),
                        player.Identity.DisplayName,
                        SiSquadCombatStance.Combat,
                        SiSquadCombatTransitionReason.PlayerOrder,
                        true);
                    return;
                case SiUtilityCommandMenuCommand.TransportationGetIn:
                    MountSquad(sender, player, leaderIdentityId);
                    return;
                case SiUtilityCommandMenuCommand.TransportationDisembark:
                    DisembarkSquad(sender, leaderIdentityId);
                    return;
                case SiUtilityCommandMenuCommand.ToggleUi:
                case SiUtilityCommandMenuCommand.ToggleSquadChatter:
                    return;
                default:
                    Respond(sender, "Unknown Si Utility AI command.");
                    return;
            }
        }

        private void RespondCurrentSquadInfo(ulong sender, long leaderIdentityId)
        {
            var lines = Squads?.CreateRosterLinesForLeader(Npcs, leaderIdentityId);
            if (lines == null || lines.Count == 0)
            {
                Respond(sender, "No squad roster is available.");
                return;
            }

            foreach (var line in lines)
                Respond(sender, line);

            var state = GetSquadOrder(leaderIdentityId);
            Respond(
                sender,
                $"Order: {OrderName(state.Mode)}, formation {FormationName(state.Formation)}, engagement {EngagementName(state.EngagementStance)}, combat {CombatStanceName(GetCombatStance(PlayerLeaderKey(leaderIdentityId)))}, transport {TransportModeName(state.TransportMode)}.");
        }

        private void StopSquad(ulong sender, long leaderIdentityId)
        {
            var state = GetSquadOrder(leaderIdentityId);
            state.Mode = SiSquadOrderMode.Stopped;
            CancelTransportOverride(leaderIdentityId, state);
            var cleared = ClearLeaderWaypoints(leaderIdentityId);
        }

        private void FollowSquad(ulong sender, long leaderIdentityId)
        {
            var state = GetSquadOrder(leaderIdentityId);
            state.Mode = SiSquadOrderMode.Follow;
            CancelTransportOverride(leaderIdentityId, state);

            string failure;
            var ordered = ApplyFollowOrder(leaderIdentityId, state, true, out failure);
        }

        private void SetFormation(ulong sender, long leaderIdentityId, SiSquadFormation formation)
        {
            var state = GetSquadOrder(leaderIdentityId);
            state.Formation = formation;
            state.Mode = SiSquadOrderMode.Follow;
            CancelTransportOverride(leaderIdentityId, state);

            string failure;
            var ordered = ApplyFollowOrder(leaderIdentityId, state, true, out failure);
        }

        private void SetEngagementStance(long leaderIdentityId, SiSquadEngagementStance engagementStance)
        {
            var state = GetSquadOrder(leaderIdentityId);
            state.EngagementStance = engagementStance;
        }

        private void CancelTransportOverride(long leaderIdentityId, SiSquadCommandState state)
        {
            if (state == null)
                return;

            ReleaseLeaderTransportSeats(leaderIdentityId);
            state.TransportMode = SiSquadTransportMode.None;
            state.TransportVehicleEntityId = 0;
            ResetTransportCadence(state);
            RemoveTransportStatesForLeader(leaderIdentityId);
        }

        private bool TryGetMountedVehicle(MyPlayer player, out MyEntity vehicle, out string failure)
        {
            vehicle = null;
            failure = null;

            var controlledEntity = player?.ControlledEntity as MyEntity;
            var controller = controlledEntity?.Components.Get<EquiEntityControllerComponent>();
            var seat = controller?.Controlled;
            if (seat == null)
            {
                failure = "You must be sitting in a vehicle seat to issue Mount up.";
                return false;
            }

            if (!SiTransportSeatHelpers.TryGetSeatBlockGrid(seat, out var seatBlockEntity, out var vehicleGrid))
            {
                failure = "Failed to resolve the current vehicle grid.";
                return false;
            }

            vehicle = vehicleGrid.Entity ?? seatBlockEntity;
            return true;
        }

        private bool HasActiveTransportStateForLeader(long leaderIdentityId)
        {
            if (leaderIdentityId == 0 || Squads == null || Npcs == null)
                return false;

            foreach (var npc in Squads.GetLeaderNpcs(Npcs, leaderIdentityId))
                if (npc != null && _transportNpcStates.ContainsKey(npc.EntityId))
                    return true;
            return false;
        }

        private static void ResetTransportCadence(SiSquadCommandState state)
        {
            if (state == null)
                return;

            state.TransportCadenceMode = SiSquadTransportMode.None;
            state.NextTransportActionTimeMilliseconds = 0;
        }

        private void RemoveTransportStatesForLeader(long leaderIdentityId)
        {
            if (leaderIdentityId == 0 || Squads == null || Npcs == null)
                return;

            foreach (var npc in Squads.GetLeaderNpcs(Npcs, leaderIdentityId))
                if (npc != null)
                    _transportNpcStates.Remove(npc.EntityId);
        }

        private void ReleaseLeaderTransportSeats(long leaderIdentityId)
        {
            if (leaderIdentityId == 0 || Squads == null || Npcs == null)
                return;

            foreach (var npc in Squads.GetLeaderNpcs(Npcs, leaderIdentityId))
            {
                var controller = npc?.Entity?.Components.Get<EquiEntityControllerComponent>();
                if (controller?.Controlled != null)
                    controller.ReleaseControl();
            }
        }

        private void TrimTransportStatesForLeader(long leaderIdentityId, long vehicleEntityId)
        {
            if (leaderIdentityId == 0 || vehicleEntityId == 0 || Squads == null || Npcs == null)
                return;

            foreach (var npc in Squads.GetLeaderNpcs(Npcs, leaderIdentityId))
            {
                if (npc == null)
                    continue;
                if (_transportNpcStates.TryGetValue(npc.EntityId, out var state)
                    && state.VehicleEntityId != vehicleEntityId)
                    _transportNpcStates.Remove(npc.EntityId);
            }
        }

        private bool TryAssignTransportSeat(
            SiNpc npc,
            long vehicleEntityId,
            out SiTransportNpcState assignedState)
        {
            assignedState = null;
            if (npc?.Entity == null || vehicleEntityId == 0)
                return false;
            if (!TryGetTransportVehicleEntity(vehicleEntityId, out var vehicle))
                return false;

            if (_transportNpcStates.TryGetValue(npc.EntityId, out var existing)
                && existing.VehicleEntityId == vehicleEntityId
                && TryResolveTransportSeat(existing, out var currentSeat)
                && (currentSeat.AttachedCharacter == null || currentSeat.AttachedCharacter == npc.Entity))
            {
                assignedState = existing;
                return true;
            }

            EquiPlayerAttachmentComponent.Slot bestSeat = null;
            var bestDistanceSquared = double.MaxValue;
            foreach (var seat in EnumerateVehicleSeats(vehicle))
            {
                if (seat?.Controllable?.Entity == null)
                    continue;
                if (seat.AttachedCharacter != null && seat.AttachedCharacter != npc.Entity)
                    continue;
                if (IsSeatAssignedToOtherNpc(npc.EntityId, seat))
                    continue;

                var distanceSquared = Vector3D.DistanceSquared(
                    npc.Entity.WorldMatrix.Translation,
                    seat.Controllable.Entity.WorldMatrix.Translation);
                if (distanceSquared >= bestDistanceSquared)
                    continue;

                bestDistanceSquared = distanceSquared;
                bestSeat = seat;
            }

            if (bestSeat == null)
                return false;

            if (existing == null)
            {
                existing = new SiTransportNpcState();
                _transportNpcStates[npc.EntityId] = existing;
            }

            existing.VehicleEntityId = vehicleEntityId;
            existing.SeatEntityId = bestSeat.Controllable.Entity.EntityId;
            existing.SeatSlotName = bestSeat.Definition.Name;
            assignedState = existing;
            return true;
        }

        private bool TryResolveTransportSeat(
            SiTransportNpcState state,
            out EquiPlayerAttachmentComponent.Slot slot)
        {
            slot = null;
            if (state == null || state.SeatEntityId == 0 || string.IsNullOrWhiteSpace(state.SeatSlotName))
                return false;

            var entity = MyEntities.GetEntityByIdOrDefault(state.SeatEntityId);
            if (entity == null || entity.Closed || entity.MarkedForClose)
                return false;

            return (slot = entity.Components.Get<EquiPlayerAttachmentComponent>()?.GetSlotOrDefault(state.SeatSlotName)) != null;
        }

        private bool TryGetTransportVehicleEntity(long vehicleEntityId, out MyEntity vehicle)
        {
            vehicle = MyEntities.GetEntityByIdOrDefault(vehicleEntityId);
            return vehicle != null && !vehicle.Closed && !vehicle.MarkedForClose;
        }

        private bool IsSeatAssignedToOtherNpc(long npcEntityId, EquiPlayerAttachmentComponent.Slot seat)
        {
            foreach (var entry in _transportNpcStates)
            {
                if (entry.Key == npcEntityId)
                    continue;

                var state = entry.Value;
                if (state == null)
                    continue;
                if (state.SeatEntityId != (seat?.Controllable?.Entity?.EntityId ?? 0))
                    continue;
                if (string.Equals(state.SeatSlotName, seat.Definition.Name, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private IEnumerable<EquiPlayerAttachmentComponent.Slot> EnumerateVehicleSeats(MyEntity vehicle)
        {
            if (vehicle == null || !vehicle.Components.TryGet(out MyGridDataComponent gridData))
                yield break;

            foreach (var slot in SiTransportSeatHelpers.EnumerateSeatSlotsOnGrid(gridData))
                yield return slot;
        }

        private void MountSquad(ulong sender, MyPlayer player, long leaderIdentityId)
        {
            string failure;
            MyEntity vehicle;
            if (!TryGetMountedVehicle(player, out vehicle, out failure))
            {
                Respond(sender, failure ?? "You must sit in a vehicle seat to issue Mount up.");
                return;
            }

            var troops = Squads?.GetLeaderNpcs(Npcs, leaderIdentityId);
            if (troops == null || troops.Count == 0)
            {
                Respond(sender, "Your squad has no utility AI troops.");
                return;
            }

            var state = GetSquadOrder(leaderIdentityId);
            state.TransportMode = SiSquadTransportMode.Mount;
            state.TransportVehicleEntityId = vehicle.EntityId;
            ResetTransportCadence(state);

            ClearLeaderWaypoints(leaderIdentityId);
            TrimTransportStatesForLeader(leaderIdentityId, vehicle.EntityId);

            var assigned = 0;
            for (var i = 0; i < troops.Count; i++)
                if (TryAssignTransportSeat(troops[i], vehicle.EntityId, out var ignored))
                    assigned++;

            if (assigned == 0)
            {
                state.TransportMode = SiSquadTransportMode.None;
                state.TransportVehicleEntityId = 0;
                ResetTransportCadence(state);
                Respond(sender, "No free transport seats were found on the current vehicle.");
                return;
            }
        }

        private void DisembarkSquad(ulong sender, long leaderIdentityId)
        {
            var state = GetSquadOrder(leaderIdentityId);
            if (!HasActiveTransportStateForLeader(leaderIdentityId))
            {
                state.TransportMode = SiSquadTransportMode.None;
                state.TransportVehicleEntityId = 0;
                ResetTransportCadence(state);
                Respond(sender, "No squad members are currently assigned to transport seats.");
                return;
            }

            state.TransportMode = SiSquadTransportMode.Disembark;
            ResetTransportCadence(state);
        }

        private void UpdateCombatStances()
        {
            if (_squadCombatStates.Count == 0)
                return;

            var now = CurrentTimeMilliseconds();
            foreach (var entry in _squadCombatStates)
            {
                var leader = entry.Key;
                var state = entry.Value;
                if (state == null || state.Stance != SiSquadCombatStance.Combat)
                    continue;

                var lastThreatTime = Math.Max(state.LastShotAtTime, state.LastEnemySpottedTime);
                var lastRelevantTime = Math.Max(lastThreatTime, state.LastStanceChangeTime);
                if (now - lastRelevantTime < CombatStanceCooldownMilliseconds)
                    continue;
                if (HasSpottedEnemyNearby(leader))
                    continue;

                SetCombatStance(
                    leader,
                    state.LeaderName,
                    SiSquadCombatStance.Safe,
                    SiSquadCombatTransitionReason.AreaClear,
                    false);
            }
        }

        private bool HasSpottedEnemyNearby(SiSquadLeaderKey leader)
        {
            if (Npcs == null || Squads == null || Spotting == null)
                return false;

            foreach (var npc in Npcs.Npcs.Values)
            {
                if (npc?.Entity == null)
                    continue;

                SiAssignedNpc assignment;
                if (!Squads.TryGetAssignment(npc.EntityId, out assignment) || !assignment.Leader.Equals(leader))
                    continue;
                if (Spotting.HasSpottedTargetNearby(npc.EntityId, CombatStanceNearbyEnemyDistance))
                    return true;
            }

            return false;
        }

        private void SetCombatStance(
            SiSquadLeaderKey leader,
            string leaderName,
            SiSquadCombatStance stance,
            SiSquadCombatTransitionReason reason,
            bool speakAsPlayerOrder)
        {
            var state = GetOrCreateCombatState(leader);
            if (!string.IsNullOrWhiteSpace(leaderName))
                state.LeaderName = leaderName;

            var previous = state.Stance;
            state.Stance = stance;
            state.LastStanceChangeTime = CurrentTimeMilliseconds();
            if (stance == SiSquadCombatStance.Combat && reason == SiSquadCombatTransitionReason.PlayerOrder)
                state.LastEnemySpottedTime = state.LastStanceChangeTime;

            if (previous == stance)
                return;

            if (stance == SiSquadCombatStance.Combat)
            {
                state.CombatEntryToken++;
                ClearSquadWaypoints(leader);
            }

            if (speakAsPlayerOrder)
                SpeakSquadBehaviorChange(leader, PlayerOrderCombatStanceReport(stance), true);
            else
                SpeakSquadBehaviorChange(leader, CombatStanceChangeReport(stance, reason), false);
        }

        private SiSquadCombatState GetOrCreateCombatState(SiSquadLeaderKey leader)
        {
            SiSquadCombatState state;
            if (_squadCombatStates.TryGetValue(leader, out state))
                return state;

            state = new SiSquadCombatState
            {
                LeaderName = leader.Kind == SiSquadLeaderKind.Player ? "Player " + leader.Id : "Squad",
                Stance = SiSquadCombatStance.Safe,
                LastShotAtTime = long.MinValue,
                LastEnemySpottedTime = long.MinValue,
                LastStanceChangeTime = long.MinValue,
                CombatEntryToken = 0,
            };
            _squadCombatStates.Add(leader, state);
            return state;
        }

        private void SpeakSquadBehaviorChange(
            SiSquadLeaderKey leader,
            string message,
            bool force)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            var speaker = FindSquadSpeaker(leader);
            if (speaker != null)
            {
                if (force || ShowSquadChatter)
                    speaker.TrySpeak(message);
            }
        }

        private SiNpc FindSquadSpeaker(SiSquadLeaderKey leader)
        {
            if (Npcs == null || Squads == null)
                return null;

            SiNpc first = null;
            foreach (var npc in Npcs.Npcs.Values)
            {
                SiAssignedNpc assignment;
                if (npc == null
                    || !Squads.TryGetAssignment(npc.EntityId, out assignment)
                    || !assignment.Leader.Equals(leader))
                    continue;
                if (assignment.IsLeader)
                    return npc;
                if (first == null)
                    first = npc;
            }

            return first;
        }

        internal void ReportNpcSpottedTarget(long observerEntityId, long targetEntityId)
        {
            if (!IsAuthoritative || observerEntityId == 0 || targetEntityId == 0 || Npcs == null || Squads == null)
                return;

            SiAssignedNpc assignment;
            if (!Npcs.Npcs.ContainsKey(observerEntityId)
                || !Squads.TryGetAssignment(observerEntityId, out assignment))
                return;

            var state = GetOrCreateCombatState(assignment.Leader);
            state.LastEnemySpottedTime = CurrentTimeMilliseconds();
            SetCombatStance(
                assignment.Leader,
                assignment.LeaderName,
                SiSquadCombatStance.Combat,
                SiSquadCombatTransitionReason.EnemySpotted,
                false);
        }

        internal void ReportNpcFiredShot(long entityId)
        {
            if (!IsAuthoritative || entityId == 0 || Squads == null)
                return;

            SiAssignedNpc assignment;
            if (!Squads.TryGetAssignment(entityId, out assignment))
                return;

            var state = GetOrCreateCombatState(assignment.Leader);
            state.LastEnemySpottedTime = CurrentTimeMilliseconds();
            SetCombatStance(
                assignment.Leader,
                assignment.LeaderName,
                SiSquadCombatStance.Combat,
                SiSquadCombatTransitionReason.OpeningFire,
                false);
        }

        internal void ReportNpcShotAt(long entityId)
        {
            if (!IsAuthoritative || entityId == 0 || Squads == null)
                return;

            SiAssignedNpc assignment;
            if (!Squads.TryGetAssignment(entityId, out assignment))
                return;

            var state = GetOrCreateCombatState(assignment.Leader);
            state.LastShotAtTime = CurrentTimeMilliseconds();
            SetCombatStance(
                assignment.Leader,
                assignment.LeaderName,
                SiSquadCombatStance.Combat,
                SiSquadCombatTransitionReason.TakingFire,
                false);
        }

        private bool HandleCommand(ulong sender, string message, MyChatCommandType handledAsType)
        {
            if (!CanManageNpcs(sender))
                return Respond(sender, "Enable Medieval Master to manage custom NPCs in survival.");

            var tokens = message.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2)
                return Respond(sender, HelpText());

            switch (tokens[1].ToLowerInvariant())
            {
                case "spawn":
                    return SpawnFromCommand(sender, tokens);
                case "spawn-enemy":
                case "enemy":
                    return SpawnFromEnemyShortcut(sender, tokens);
                case "list":
                    return Respond(sender, $"Custom NPCs alive: {Npcs.Npcs.Count}.");
                case "clear":
                    var removed = Npcs.Npcs.Count;
                    Npcs.CloseAll();
                    _pendingControlledEntityBindings.Clear();
                    _squadOrders.Clear();
                    _squadCombatStates.Clear();
                    Squads?.ClearNpcs();
                    BroadcastClear();
                    return Respond(sender, $"Removed {removed} custom NPC(s).");
                case "utility-ai":
                case "utilityai":
                case "ai":
                    return HandleUtilityAiCommand(sender, tokens);
                case "gamelog":
                case "log":
                    return HandleGameLogCommand(sender, tokens);
                default:
                    return Respond(sender, HelpText());
            }
        }

        private bool HandleEnemyCommand(ulong sender, string message, MyChatCommandType handledAsType)
        {
            if (!CanManageNpcs(sender))
                return Respond(sender, "Enable Medieval Master to manage custom NPCs in survival.");

            var tokens = message.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 1 && !string.Equals(tokens[1], "spawn", StringComparison.OrdinalIgnoreCase))
                return Respond(sender, $"{EnemyCommand} [spawn]");

            return SpawnFromEnemyShortcut(sender, tokens);
        }

        private bool SpawnFromEnemyShortcut(ulong sender, string[] tokens)
        {
            var webbing = tokens.Length >= 3 ? tokens[2] : GetDefaultEnemyWebbing();
            if (string.IsNullOrWhiteSpace(webbing))
                return Respond(sender, $"No trooper webbings are currently available. Available: {KnownWebbingsText()}.");

            return SpawnFromCommand(
                sender,
                new SiNpcSpawnRequest(webbing, false, true));
        }

        private static string GetDefaultEnemyWebbing()
        {
            var webbings = SiNpcTrooperCatalog.GetKnownWebbings();
            return webbings.Count > 0 ? webbings[0] : null;
        }

        private bool SpawnFromCommand(ulong sender, string[] tokens)
        {
            if (!TryParseSpawnRequest(tokens, false, out var request, out var failure))
                return Respond(sender, failure ?? HelpText());

            return SpawnFromCommand(sender, request);
        }

        private bool SpawnFromCommand(ulong sender, SiNpcSpawnRequest request)
        {
            if (!SiNpcTrooperCatalog.TryResolveLoadout(request.WebbingSubtype, request.IsParatrooper, out _, out _))
                return Respond(sender, $"Unknown trooper webbing '{request.WebbingSubtype}'. Available: {KnownWebbingsText()}.");

            var player = MyPlayers.Static.GetPlayer(new MyPlayer.PlayerId(sender, 0));
            var playerPosition = player?.ControlledEntity?.Get<MyPositionComponentBase>();
            if (playerPosition == null)
                return Respond(sender, "You must control a character to spawn an NPC.");

            var transform = CreateSpawnTransform(playerPosition.WorldMatrix);
            var entityId = MyEntityIdentifier.AllocateId();
            if (!Npcs.TrySpawnConfigured(
                    SiNpcManager.SoldierArchetype,
                    request.DisplayArchetype,
                    entityId,
                    transform,
                    out var npc))
                return Respond(sender, $"Failed to spawn custom NPC '{request.DisplayArchetype}'; its model or entity definition could not be loaded.");

            string failure;
            if (!ApplySpawnRequest(npc, request, out failure)
                || !ConfigureSpawnedNpc(request, npc, player, out failure))
            {
                Npcs.Close(entityId);
                return Respond(sender, failure ?? $"Failed to configure custom NPC '{request.DisplayArchetype}'.");
            }

            BroadcastSpawn(npc, request);
            return Respond(sender, $"Spawned {request.DisplayArchetype} ({entityId}).");
        }

        private bool ConfigureSpawnedNpc(
            SiNpcSpawnRequest request,
            SiNpc npc,
            MyPlayer player,
            out string failure)
        {
            failure = null;
            if (request.IsEnemy)
                return ConfigureEnemyTrooper(npc, player, out failure);

            if (!ConfigureFriendlyTrooper(npc, player, out failure))
                return false;

            Squads?.AssignNpcToPlayer(npc, player);
            return true;
        }

        private bool ApplySpawnRequest(SiNpc npc, SiNpcSpawnRequest request, out string failure)
        {
            failure = null;
            if (npc?.Entity == null)
            {
                failure = "The spawned NPC entity is not available.";
                return false;
            }

            var loadoutComponent = npc.Entity.Components.Get<SiNpcLoadoutComponent>();
            var uniform = npc.Entity.Components.Get<SiNpcUniformComponent>();
            var weapon = npc.Entity.Components.Get<SiNpcRangedWeaponComponent>();
            var shoot = npc.Entity.Components.Get<SiShootOpposingNpcBehaviorComponent>();
            if (loadoutComponent == null || uniform == null || weapon == null || shoot == null)
            {
                failure = "The generic trooper container is missing a required runtime component.";
                return false;
            }

            if (!SiNpcTrooperCatalog.TryResolveLoadout(request.WebbingSubtype, request.IsParatrooper, out var resolvedWebbingSubtype, out var loadout)
                || loadout == null)
            {
                failure = $"No runtime loadout definition was found for '{request.WebbingSubtype}'.";
                return false;
            }

            if (!loadoutComponent.ApplyRuntimeWebbing(loadout.WebbingItemId))
            {
                failure = $"The runtime loadout '{resolvedWebbingSubtype}' could not be applied.";
                return false;
            }

            if (!weapon.ApplyRuntimeDefinition(loadout.WeaponDefinitionId))
            {
                failure = $"Weapon definition for '{resolvedWebbingSubtype}' could not be applied.";
                return false;
            }

            if (!shoot.ApplyRuntimeDefinition(loadout.ShootBehaviorDefinitionId))
            {
                failure = $"Shoot behavior definition for '{resolvedWebbingSubtype}' could not be applied.";
                return false;
            }

            var uniformId = loadout.Uniform ?? SiNpcTrooperCatalog.ResolveUniform(resolvedWebbingSubtype, request.IsParatrooper);
            if (uniformId.HasValue)
                uniform.ApplyRuntimeDefinition((MyDefinitionId)uniformId.Value);

            var dataDrivenNpc = npc as SiDataDrivenNpc;
            dataDrivenNpc?.SetSpawnMetadata(
                resolvedWebbingSubtype,
                loadout.IsParatrooper || request.IsParatrooper,
                request.IsEnemy);
            return true;
        }

        private bool TryParseSpawnRequest(
            string[] tokens,
            bool forceEnemy,
            out SiNpcSpawnRequest request,
            out string failure)
        {
            request = default(SiNpcSpawnRequest);
            failure = null;
            if (tokens == null || tokens.Length < 3 || string.IsNullOrWhiteSpace(tokens[2]))
            {
                failure = $"Usage: {Command} spawn <webbing> [paratrooper] [enemy]. Available: {KnownWebbingsText()}";
                return false;
            }

            var webbingSubtype = tokens[2].Trim();
            var isParatrooper = false;
            var isEnemy = forceEnemy;
            for (var i = 3; i < tokens.Length; i++)
            {
                var token = tokens[i];
                if (string.Equals(token, "paratrooper", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(token, "para", StringComparison.OrdinalIgnoreCase))
                {
                    isParatrooper = true;
                    continue;
                }

                if (string.Equals(token, "enemy", StringComparison.OrdinalIgnoreCase))
                {
                    isEnemy = true;
                    continue;
                }

                if (string.Equals(token, "friendly", StringComparison.OrdinalIgnoreCase))
                {
                    isEnemy = false;
                    continue;
                }

                failure = $"Unknown spawn flag '{token}'. Supported flags: paratrooper, enemy, friendly.";
                return false;
            }

            request = new SiNpcSpawnRequest(webbingSubtype, isParatrooper, isEnemy);
            return true;
        }

        private bool ConfigureFriendlyTrooper(SiNpc npc, MyPlayer player, out string failure)
        {
            failure = null;
            if (npc == null || player?.Identity == null)
            {
                failure = "You must control a character to spawn a friendly NPC.";
                return false;
            }

            if (npc.DiplomaticIdentityId != 0)
                return true;

            var identity = MyIdentities.Static?.CreateIdentity(FriendlyTrooperName(npc));
            if (identity == null)
            {
                failure = "Failed to create a diplomatic identity for the friendly NPC.";
                return false;
            }

            SetNpcDiplomaticIdentity(npc, identity);
            return true;
        }

        private bool ConfigureEnemyTrooper(SiNpc npc, MyPlayer player, out string failure)
        {
            failure = null;
            if (npc == null || player?.Identity == null)
            {
                failure = "You must control a character to spawn an enemy NPC.";
                return false;
            }

            var identity = MyIdentities.Static?.CreateIdentity(EnemyTrooperName(npc));
            if (identity == null)
            {
                failure = "Failed to create a diplomatic identity for the enemy NPC.";
                return false;
            }

            SetNpcDiplomaticIdentity(npc, identity);

            MyFaction enemyFaction;
            if (!TryAssignIdentityToEnemyFaction(identity.Id, out enemyFaction, out failure))
                return false;

            if (!TryMarkHostileToCaller(player, enemyFaction, out failure))
                return false;

            AssignNpcToEnemySquad(npc, enemyFaction);
            return true;
        }

        private static bool TryAssignIdentityToEnemyFaction(
            long identityId,
            out MyFaction enemyFaction,
            out string failure)
        {
            failure = null;
            enemyFaction = EnemyFaction();
            if (enemyFaction == null)
            {
                failure = "Si Utility AI enemy faction '"
                          + SiNpcManager.EnemyFactionTag
                          + "' is missing; check mod/Data/Factions.sbc.";
                return false;
            }

            if (!enemyFaction.IsMember(identityId))
            {
                var result = enemyFaction.ApplyForFaction(identityId, true);
                if (!enemyFaction.IsMember(identityId))
                {
                    failure = "Failed to assign the enemy NPC to faction '"
                              + SiNpcManager.EnemyFactionTag
                              + "': "
                              + result;
                    return false;
                }
            }

            return true;
        }

        private void AssignNpcToEnemySquad(SiNpc npc, MyFaction enemyFaction)
        {
            if (npc == null || enemyFaction == null)
                return;

            var squads = Squads;
            if (squads == null)
                return;

            var army = new SiArmyKey(SiArmyKind.Faction, enemyFaction.FactionId);
            SiAssignedNpc nearbyAssignment;
            if (Npcs != null
                && npc.Entity != null
                && squads.TryFindNearbyAiSquadAssignment(
                    Npcs,
                    npc.Entity.WorldMatrix.Translation,
                    squads.Definition?.EnemyJoinRadius ?? 0,
                    army,
                    out nearbyAssignment))
            {
                squads.AssignNpcToLeader(
                    npc,
                    nearbyAssignment.Leader.Kind,
                    nearbyAssignment.Leader.Id,
                    nearbyAssignment.Leader.Army.Kind,
                    nearbyAssignment.Leader.Army.Id,
                    nearbyAssignment.LeaderName,
                    false);
                return;
            }

            squads.AssignNpcToLeader(
                npc,
                SiSquadLeaderKind.Ai,
                npc.EntityId,
                army.Kind,
                army.Id,
                EnemyTrooperName(npc),
                true);
        }

        private static MyFaction EnemyFaction()
        {
            try
            {
                return MyFactionManager.Instance?.GetFactionByTag(SiNpcManager.EnemyFactionTag);
            }
            catch
            {
                return null;
            }
        }

        private static bool TryMarkHostileToCaller(MyPlayer player, MyFaction enemyFaction, out string failure)
        {
            failure = null;
            var diplomacy = MyDiplomacyManager.Instance;
            if (diplomacy == null)
            {
                failure = "Diplomacy manager is not available; enemy relation could not be set.";
                return false;
            }
            if (player?.Identity == null || enemyFaction == null)
            {
                failure = "Enemy faction relation could not be set.";
                return false;
            }

            try
            {
                var enemyParty = new MyDiplomaticParty(enemyFaction);
                SetHostileRelationship(
                    diplomacy,
                    new MyDiplomaticParty(DiplomaticPartyType.Player, player.Identity.Id),
                    enemyParty);

                var faction = PlayerFaction(player.Identity.Id);
                if (faction != null)
                    SetHostileRelationship(diplomacy, new MyDiplomaticParty(faction), enemyParty);
                return true;
            }
            catch (Exception exception)
            {
                failure = "Failed to mark the enemy NPC hostile: " + exception.Message;
                return false;
            }
        }

        private static void SetHostileRelationship(
            MyDiplomacyManager diplomacy,
            MyDiplomaticParty firstParty,
            MyDiplomaticParty secondParty)
        {
            diplomacy.SetRelationshipBetweenParties(firstParty, secondParty, HostileRelationship);
            diplomacy.SetRelationshipBetweenParties(secondParty, firstParty, HostileRelationship);
        }

        private static MyFaction PlayerFaction(long identityId)
        {
            try
            {
                return MyFactionManager.GetPlayerFaction(identityId);
            }
            catch
            {
                return null;
            }
        }

        private static string EnemyTrooperName(SiNpc npc) =>
            "Enemy trooper " + npc.EntityId;

        private static string FriendlyTrooperName(SiNpc npc) =>
            "Trooper " + npc.EntityId;

        private bool HandleSquadCommand(ulong sender, string message, MyChatCommandType handledAsType)
        {
            var tokens = message.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2)
                return Respond(sender, SquadHelpText());

            switch (tokens[1].ToLowerInvariant())
            {
                case "list":
                case "members":
                    return RespondSquadRoster(sender);
                default:
                    return Respond(sender, SquadHelpText());
            }
        }

        private void UpdateSquadOrders()
        {
            foreach (var entry in _squadOrders)
            {
                var state = entry.Value;
                if (state.Mode != SiSquadOrderMode.Follow || state.TransportMode != SiSquadTransportMode.None)
                    continue;
                var leader = PlayerLeaderKey(entry.Key);
                var combatStance = GetCombatStance(leader);
                if (combatStance == SiSquadCombatStance.Combat)
                    continue;

                string failure;
                ApplyFollowOrder(entry.Key, state, false, out failure);
            }
        }

        private void CleanupTransportStates()
        {
            if (_transportNpcStates.Count == 0 || Npcs == null)
                return;

            _staleCoverReservationIds.Clear();
            foreach (var entry in _transportNpcStates)
            {
                if (!Npcs.Npcs.TryGetValue(entry.Key, out var npc)
                    || npc?.Entity == null
                    || npc.Entity.Closed
                    || npc.Entity.MarkedForClose
                    || npc.IsDead)
                {
                    _staleCoverReservationIds.Add(entry.Key);
                    continue;
                }

                var vehicleId = entry.Value?.VehicleEntityId ?? 0;
                if (vehicleId == 0 || MyEntities.GetEntityByIdOrDefault(vehicleId) == null)
                    _staleCoverReservationIds.Add(entry.Key);
            }

            for (var i = 0; i < _staleCoverReservationIds.Count; i++)
                _transportNpcStates.Remove(_staleCoverReservationIds[i]);
            _staleCoverReservationIds.Clear();
        }

        private void CleanupPositionCache()
        {
            if (_positionCache.Count == 0 || Npcs == null)
                return;

            _staleCoverReservationIds.Clear();
            foreach (var entry in _positionCache)
            {
                if (!Npcs.Npcs.TryGetValue(entry.Key, out var npc)
                    || npc?.Entity == null
                    || npc.Entity.Closed
                    || npc.Entity.MarkedForClose
                    || npc.IsDead
                    || entry.Value == null
                    || entry.Value.IsEmpty)
                {
                    _staleCoverReservationIds.Add(entry.Key);
                }
            }

            for (var i = 0; i < _staleCoverReservationIds.Count; i++)
                _positionCache.Remove(_staleCoverReservationIds[i]);
            _staleCoverReservationIds.Clear();
        }

        private void CleanupCoverReservations()
        {
            if (_coverReservations.Count == 0 || Npcs == null)
                return;

            var cleanupStartedAt = DebugTimestampTicks();
            _staleCoverReservationIds.Clear();
            foreach (var reservation in _coverReservations)
            {
                SiNpc npc;
                if (!Npcs.Npcs.TryGetValue(reservation.Key, out npc)
                    || npc?.Entity == null
                    || npc.Entity.Closed
                    || npc.Entity.MarkedForClose
                    || npc.IsDead
                    || GetCombatStance(npc) != SiSquadCombatStance.Combat)
                {
                    _staleCoverReservationIds.Add(reservation.Key);
                    continue;
                }

            }

            for (var i = 0; i < _staleCoverReservationIds.Count; i++)
                _coverReservations.Remove(_staleCoverReservationIds[i]);
            LogSlowCoverCleanup(DebugElapsedMilliseconds(cleanupStartedAt), _staleCoverReservationIds.Count);
            _staleCoverReservationIds.Clear();
        }

        private void ReassignLeaderlessSquads()
        {
            if (Squads == null || Npcs == null)
                return;

            var changes = Squads.ReassignLeaderlessSquads(Npcs, IsPlayerLeaderActive);
            if (changes == null || changes.Count == 0)
                return;

            for (var i = 0; i < changes.Count; i++)
            {
                var change = changes[i];
                ClearSquadWaypoints(change.NewLeader);
                MigrateSquadLeadershipState(change);
            }
        }

        private void MigrateSquadLeadershipState(SiSquadLeadershipChange change)
        {
            if (change.OldLeader.Kind == SiSquadLeaderKind.Player)
            {
                _squadOrders.Remove(change.OldLeader.Id);
                _leaderMotionStates.Remove(change.OldLeader.Id);
            }

            SiSquadCombatState combatState;
            if (!_squadCombatStates.TryGetValue(change.OldLeader, out combatState))
                return;

            _squadCombatStates.Remove(change.OldLeader);
            combatState.LeaderName = change.NewLeaderName;
            _squadCombatStates[change.NewLeader] = combatState;
        }

        private void LogSlowCoverCleanup(double elapsedMilliseconds, int removedReservations)
        {
            var now = CurrentTimeMilliseconds();
            if (elapsedMilliseconds < 2 && _coverReservations.Count < 16 && removedReservations == 0)
                return;
            if (_lastCoverCleanupLogTime >= 0 && now - _lastCoverCleanupLogTime < 5000)
                return;

            _lastCoverCleanupLogTime = now;
        }

        private static long DebugTimestampTicks()
        {
            return DateTime.UtcNow.Ticks;
        }

        private static double DebugElapsedMilliseconds(long startTicks)
        {
            return (DateTime.UtcNow.Ticks - startTicks) / (double)TimeSpan.TicksPerMillisecond;
        }

        private void CleanupExpiredCoverSearchCache()
        {
            if (_coverSearchCache.Count == 0)
                return;

            var now = CurrentTimeMilliseconds();
            _expiredCoverSearchCacheKeys.Clear();
            foreach (var entry in _coverSearchCache)
                if (entry.Value == null || entry.Value.ExpiresAtMilliseconds < now)
                    _expiredCoverSearchCacheKeys.Add(entry.Key);

            for (var i = 0; i < _expiredCoverSearchCacheKeys.Count; i++)
                _coverSearchCache.Remove(_expiredCoverSearchCacheKeys[i]);
            _expiredCoverSearchCacheKeys.Clear();
        }

        private void CleanupExpiredCoverScanCache()
        {
            if (_coverScanCache.Count == 0)
                return;

            var now = CurrentTimeMilliseconds();
            _expiredCoverScanCacheKeys.Clear();
            foreach (var entry in _coverScanCache)
                if (entry.Value == null || entry.Value.ExpiresAtMilliseconds < now)
                    _expiredCoverScanCacheKeys.Add(entry.Key);

            for (var i = 0; i < _expiredCoverScanCacheKeys.Count; i++)
                _coverScanCache.Remove(_expiredCoverScanCacheKeys[i]);
            _expiredCoverScanCacheKeys.Clear();
        }

        private int ApplyFollowOrder(
            long leaderIdentityId,
            SiSquadCommandState state,
            bool reportFailures,
            out string failure)
        {
            failure = null;
            if (Squads?.Definition == null)
            {
                failure = reportFailures ? "Squad data definition is missing." : null;
                return 0;
            }

            MatrixD leaderTransform;
            if (!TryGetLeaderTransform(leaderIdentityId, out leaderTransform))
            {
                failure = reportFailures ? "You must control a character to command a squad." : null;
                return 0;
            }

            var troops = Squads.GetLeaderNpcs(Npcs, leaderIdentityId);
            if (troops.Count == 0)
            {
                failure = reportFailures ? "Your squad has no utility AI troops." : null;
                return 0;
            }

            var leaderMotion = UpdateLeaderMotionState(leaderIdentityId, leaderTransform);
            Vector3D origin;
            Vector3D forward;
            Vector3D right;
            CreateLeaderFrame(leaderTransform, leaderMotion.Direction, out origin, out forward, out right);

            var definition = Squads.Definition;
            var refreshDistanceSquared = definition.WaypointRefreshDistance * definition.WaypointRefreshDistance;
            var issued = 0;
            if (state.Formation == SiSquadFormation.File || state.Formation == SiSquadFormation.Column)
            {
                issued = ApplyChainedFollowOrder(
                    troops,
                    state.Formation,
                    origin,
                    forward,
                    definition,
                    refreshDistanceSquared);
            }
            else
            {
                for (var i = 0; i < troops.Count; i++)
                {
                    var target = origin + FormationOffset(
                        state.Formation,
                        i,
                        troops.Count,
                        forward,
                        right,
                        definition);
                    if (TryCacheAndIssueFollowWaypoint(troops[i], target, refreshDistanceSquared))
                        issued++;
                }
            }

            return issued;
        }

        private int ApplyChainedFollowOrder(
            List<SiNpc> troops,
            SiSquadFormation formation,
            in Vector3D leaderPosition,
            in Vector3D leaderForward,
            SiSquadSystemDefinition definition,
            double refreshDistanceSquared)
        {
            var issued = 0;
            var anchorPosition = leaderPosition;
            var anchorForward = leaderForward;
            var followerGap = formation == SiSquadFormation.File
                ? definition.FileSpacing
                : definition.ColumnSpacing;
            if (followerGap <= 0)
                followerGap = definition.FollowDistance;

            for (var i = 0; i < troops.Count; i++)
            {
                var gap = i == 0 ? definition.FollowDistance : followerGap;
                var target = anchorPosition - anchorForward * gap;
                if (TryCacheAndIssueFollowWaypoint(troops[i], target, refreshDistanceSquared))
                    issued++;

                var anchor = TryGetNpcFollowAnchor(troops[i], anchorForward);
                anchorPosition = anchor.Position;
                anchorForward = anchor.Forward;
            }

            return issued;
        }

        private int ClearLeaderWaypoints(long leaderIdentityId)
        {
            var cleared = 0;
            foreach (var npc in Squads.GetLeaderNpcs(Npcs, leaderIdentityId))
            {
                var mover = npc as ISiWaypointMover;
                if (mover != null && !mover.HasWaypoint)
                {
                    cleared++;
                    continue;
                }

                if (Npcs.TryClearWaypoint(npc.EntityId))
                    cleared++;
            }

            return cleared;
        }

        private int ClearSquadWaypoints(SiSquadLeaderKey leader)
        {
            if (Npcs == null || Squads == null)
                return 0;

            var cleared = 0;
            foreach (var npc in Npcs.Npcs.Values)
            {
                if (npc == null)
                    continue;

                SiAssignedNpc assignment;
                if (!Squads.TryGetAssignment(npc.EntityId, out assignment)
                    || !assignment.Leader.Equals(leader))
                    continue;

                var mover = npc as ISiWaypointMover;
                if (mover != null && !mover.HasWaypoint)
                {
                    cleared++;
                    continue;
                }

                if (Npcs.TryClearWaypoint(npc.EntityId))
                    cleared++;
            }

            return cleared;
        }

        private SiSquadCommandState GetSquadOrder(long leaderIdentityId)
        {
            SiSquadCommandState state;
            if (!_squadOrders.TryGetValue(leaderIdentityId, out state))
                _squadOrders.Add(leaderIdentityId, state = new SiSquadCommandState());
            return state;
        }

        private bool TryCacheAndIssueFollowWaypoint(SiNpc npc, in Vector3D target, double refreshDistanceSquared)
        {
            if (npc == null)
                return false;

            CacheFormationPosition(npc, target);
            return TryFollowCachedFormationPosition(npc, refreshDistanceSquared);
        }

        private void CachePosition(long entityId, SiNpcCachedPositionKind kind, in Vector3D position)
        {
            if (entityId == 0)
                return;

            if (!_positionCache.TryGetValue(entityId, out var state) || state == null)
                _positionCache[entityId] = state = new SiNpcPositionCacheState();

            state.Set(kind, position);
        }

        private void ClearCachedPosition(long entityId, SiNpcCachedPositionKind kind)
        {
            if (entityId == 0)
                return;
            if (!_positionCache.TryGetValue(entityId, out var state) || state == null)
                return;

            state.Clear(kind);
            if (state.IsEmpty)
                _positionCache.Remove(entityId);
        }

        private bool TryFollowCachedPosition(
            SiNpc npc,
            SiNpcCachedPositionKind kind,
            double refreshDistanceSquared)
        {
            if (npc == null || Npcs == null)
                return false;
            if (!TryGetCachedPosition(npc.EntityId, kind, out var target))
                return false;

            var mover = npc as ISiWaypointMover;
            if (mover != null
                && mover.HasWaypoint
                && refreshDistanceSquared > 0
                && Vector3D.DistanceSquared(mover.Waypoint, target) < refreshDistanceSquared)
                return true;

            return Npcs.TrySetWaypoint(npc.EntityId, target);
        }

        private bool TryGetCachedPosition(long entityId, SiNpcCachedPositionKind kind, out Vector3D position)
        {
            position = Vector3D.Zero;
            if (entityId == 0)
                return false;
            if (!_positionCache.TryGetValue(entityId, out var state) || state == null)
                return false;

            return state.TryGet(kind, out position);
        }

        private bool TryGetUnsafeThrowPosition(
            long entityId,
            SiNpc squadmate,
            in Vector3D throwOrigin,
            in Vector3D targetPosition,
            double blastRadius,
            double trajectoryRadius,
            out Vector3D blockingPosition)
        {
            blockingPosition = Vector3D.Zero;

            if (_positionCache.TryGetValue(entityId, out var cacheState) && cacheState != null)
            {
                if (cacheState.TryGet(SiNpcCachedPositionKind.Cover, out var cachedCover)
                    && IsThrowDangerousForPosition(throwOrigin, targetPosition, cachedCover, blastRadius, trajectoryRadius))
                {
                    blockingPosition = cachedCover;
                    return true;
                }

                if (cacheState.TryGet(SiNpcCachedPositionKind.PlainView, out var cachedPlainView)
                    && IsThrowDangerousForPosition(throwOrigin, targetPosition, cachedPlainView, blastRadius, trajectoryRadius))
                {
                    blockingPosition = cachedPlainView;
                    return true;
                }
            }

            var entity = squadmate?.Entity;
            if (entity == null || entity.Closed || entity.MarkedForClose)
                return false;

            var currentPosition = entity.WorldMatrix.Translation;
            if (!IsThrowDangerousForPosition(throwOrigin, targetPosition, currentPosition, blastRadius, trajectoryRadius))
                return false;

            blockingPosition = currentPosition;
            return true;
        }

        private static bool IsThrowDangerousForPosition(
            in Vector3D throwOrigin,
            in Vector3D targetPosition,
            in Vector3D friendlyPosition,
            double blastRadius,
            double trajectoryRadius)
        {
            if (blastRadius > 0
                && Vector3D.DistanceSquared(friendlyPosition, targetPosition) <= blastRadius * blastRadius)
                return true;

            if (trajectoryRadius <= 0)
                return false;

            return DistanceSquaredToSegment(friendlyPosition, throwOrigin, targetPosition)
                   <= trajectoryRadius * trajectoryRadius;
        }

        private static double DistanceSquaredToSegment(
            in Vector3D point,
            in Vector3D segmentStart,
            in Vector3D segmentEnd)
        {
            var segment = segmentEnd - segmentStart;
            var segmentLengthSquared = segment.LengthSquared();
            if (segmentLengthSquared <= 0.0001)
                return Vector3D.DistanceSquared(point, segmentStart);

            var t = Vector3D.Dot(point - segmentStart, segment) / segmentLengthSquared;
            t = Math.Max(0, Math.Min(1, t));
            var closestPoint = segmentStart + segment * t;
            return Vector3D.DistanceSquared(point, closestPoint);
        }

        private static SiNpcCachedPositionKind ToCachedPositionKind(SiCombatMovementRole role)
        {
            switch (role)
            {
                case SiCombatMovementRole.Covered:
                    return SiNpcCachedPositionKind.Cover;
                case SiCombatMovementRole.PlainView:
                    return SiNpcCachedPositionKind.PlainView;
                case SiCombatMovementRole.None:
                default:
                    return SiNpcCachedPositionKind.None;
            }
        }

        private static Vector3D FormationOffset(
            SiSquadFormation formation,
            int index,
            int count,
            in Vector3D forward,
            in Vector3D right,
            SiSquadSystemDefinition definition)
        {
            switch (formation)
            {
                case SiSquadFormation.File:
                    return -forward * definition.FollowDistance;
                case SiSquadFormation.Line:
                    var wingRank = index / 2;
                    var wingSide = index % 2 == 0 ? -1 : 1;
                    return -forward * (definition.FollowDistance * 0.35 + wingRank * definition.LineSpacing * 0.5)
                           + right * (wingSide * (definition.LineSpacing + wingRank * definition.LineSpacing));
                case SiSquadFormation.Vee:
                    var row = (index + 2) / 2;
                    var side = index % 2 == 0 ? -1 : 1;
                    return -forward * (definition.FollowDistance + row * definition.VeeSpacing)
                           + right * (side * row * definition.VeeSpacing);
                case SiSquadFormation.Column:
                default:
                    return -forward * definition.FollowDistance;
            }
        }

        private static bool TryGetLeaderTransform(long leaderIdentityId, out MatrixD transform)
        {
            transform = MatrixD.Identity;
            if (MyPlayers.Static == null)
                return false;

            foreach (var entry in MyPlayers.Static.GetAllPlayers())
            {
                var player = entry.Value;
                if (player?.Identity == null || player.Identity.Id != leaderIdentityId)
                    continue;

                var position = player.ControlledEntity?.Get<MyPositionComponentBase>();
                if (position == null)
                    return false;

                transform = position.WorldMatrix;
                return true;
            }

            return false;
        }

        private bool IsPlayerLeaderActive(long leaderIdentityId)
        {
            MatrixD leaderTransform;
            return leaderIdentityId != 0 && TryGetLeaderTransform(leaderIdentityId, out leaderTransform);
        }

        private static void CreateLeaderFrame(
            in MatrixD leaderTransform,
            in Vector3D movementDirection,
            out Vector3D origin,
            out Vector3D forward,
            out Vector3D right)
        {
            origin = leaderTransform.Translation;
            var gravity = MyGravityProviderSystem.CalculateTotalGravityInPoint(origin);
            var up = gravity.LengthSquared() > 0.0001
                ? -Vector3D.Normalize(gravity)
                : NormalizedOrFallback(leaderTransform.Up, Vector3D.Up);

            forward = movementDirection.LengthSquared() > 0.0001
                ? Vector3D.Reject(movementDirection, up)
                : Vector3D.Reject(leaderTransform.Forward, up);
            forward = NormalizedOrFallback(forward, Vector3D.CalculatePerpendicularVector(up));
            right = NormalizedOrFallback(Vector3D.Cross(forward, up), leaderTransform.Right);
        }

        private void UpdateTrackedMotionStates()
        {
            foreach (var entry in _squadOrders)
            {
                if (entry.Value.Mode != SiSquadOrderMode.Follow)
                    continue;

                MatrixD leaderTransform;
                if (TryGetLeaderTransform(entry.Key, out leaderTransform))
                    UpdateLeaderMotionState(entry.Key, leaderTransform);
            }

            if (Npcs == null)
                return;

            foreach (var npc in Npcs.Npcs.Values)
            {
                var entity = npc?.Entity;
                if (entity == null || entity.Closed || entity.MarkedForClose)
                    continue;

                UpdateMotionState(
                    _npcMotionStates,
                    npc.EntityId,
                    entity.WorldMatrix.Translation,
                    entity.WorldMatrix.Forward);
            }
        }

        private SiMotionState UpdateLeaderMotionState(long leaderIdentityId, in MatrixD leaderTransform)
        {
            return UpdateMotionState(
                _leaderMotionStates,
                leaderIdentityId,
                leaderTransform.Translation,
                leaderTransform.Forward);
        }

        private SiMotionState UpdateMotionState(
            Dictionary<long, SiMotionState> states,
            long entityId,
            in Vector3D position,
            in Vector3D fallbackForward)
        {
            SiMotionState state;
            if (!states.TryGetValue(entityId, out state))
                states.Add(entityId, state = new SiMotionState());

            var up = SurfaceUp(position);
            if (state.HasPosition)
            {
                var delta = Vector3D.Reject(position - state.Position, up);
                if (delta.LengthSquared() > 0.0025)
                    state.Direction = Vector3D.Normalize(delta);
            }

            if (state.Direction.LengthSquared() <= 0.0001)
            {
                var projectedFallback = Vector3D.Reject(fallbackForward, up);
                if (projectedFallback.LengthSquared() > 0.0001)
                    state.Direction = Vector3D.Normalize(projectedFallback);
            }

            state.Position = position;
            state.HasPosition = true;
            return state;
        }

        private static Vector3D SurfaceUp(in Vector3D position)
        {
            var gravity = MyGravityProviderSystem.CalculateTotalGravityInPoint(position);
            if (gravity.LengthSquared() > 0.0001)
                return -Vector3D.Normalize(gravity);

            return Vector3D.Up;
        }

        private SiFollowAnchor TryGetNpcFollowAnchor(SiNpc npc, in Vector3D fallbackForward)
        {
            var entity = npc?.Entity;
            if (entity == null || entity.Closed || entity.MarkedForClose)
                return new SiFollowAnchor(Vector3D.Zero, fallbackForward);

            var forward = fallbackForward;
            SiMotionState state;
            if (_npcMotionStates.TryGetValue(npc.EntityId, out state)
                && state.Direction.LengthSquared() > 0.0001)
                forward = state.Direction;

            return new SiFollowAnchor(entity.WorldMatrix.Translation, forward);
        }

        private static Vector3D NormalizedOrFallback(in Vector3D value, in Vector3D fallback)
        {
            var lengthSquared = value.LengthSquared();
            return lengthSquared > 0.0001
                ? value / Math.Sqrt(lengthSquared)
                : fallback;
        }

        private static string OrderName(SiSquadOrderMode mode) =>
            mode == SiSquadOrderMode.Follow ? "Follow" : "Stopped";

        private static string TransportModeName(SiSquadTransportMode mode)
        {
            switch (mode)
            {
                case SiSquadTransportMode.Mount:
                    return "Mount";
                case SiSquadTransportMode.Disembark:
                    return "Disembark";
                default:
                    return "None";
            }
        }

        private static string FormationName(SiSquadFormation formation)
        {
            switch (formation)
            {
                case SiSquadFormation.File:
                    return "File";
                case SiSquadFormation.Line:
                    return "Line";
                case SiSquadFormation.Vee:
                    return "Vee";
                case SiSquadFormation.Column:
                default:
                    return "Column";
            }
        }

        private static string EngagementName(SiSquadEngagementStance stance)
        {
            switch (stance)
            {
                case SiSquadEngagementStance.EnemiesNeutrals:
                    return "Enemies and neutrals";
                case SiSquadEngagementStance.HoldFire:
                    return "Hold fire";
                case SiSquadEngagementStance.Enemies:
                default:
                    return "Enemies only";
            }
        }

        private static string CombatStanceName(SiSquadCombatStance stance)
        {
            switch (stance)
            {
                case SiSquadCombatStance.Combat:
                    return "Combat";
                case SiSquadCombatStance.Safe:
                default:
                    return "Safe";
            }
        }

        private static string CombatStanceChangeReport(
            SiSquadCombatStance stance,
            SiSquadCombatTransitionReason reason)
        {
            switch (stance)
            {
                case SiSquadCombatStance.Combat:
                    switch (reason)
                    {
                        case SiSquadCombatTransitionReason.OpeningFire:
                            return "Engaging, combat stance.";
                        case SiSquadCombatTransitionReason.TakingFire:
                            return "Taking fire, combat stance.";
                        case SiSquadCombatTransitionReason.EnemySpotted:
                            return "Contact, combat stance.";
                        case SiSquadCombatTransitionReason.PlayerOrder:
                            return "Combat stance.";
                        default:
                            return "Combat stance.";
                    }
                case SiSquadCombatStance.Safe:
                default:
                    return reason == SiSquadCombatTransitionReason.AreaClear
                        ? "Area clear, safe stance."
                        : "Safe stance.";
            }
        }

        private static string PlayerOrderCombatStanceReport(SiSquadCombatStance stance) =>
            stance == SiSquadCombatStance.Combat
                ? "Switch to combat movement."
                : "Switch to safe movement.";

        private static SiSquadLeaderKey PlayerLeaderKey(long identityId) =>
            new SiSquadLeaderKey(
                SiSquadLeaderKind.Player,
                identityId,
                SiSquadBook.ArmyForPlayerIdentity(identityId));

        private static long CurrentTimeMilliseconds()
        {
            var session = MyAPIGateway.Session;
            return session != null
                ? (long)session.ElapsedPlayTime.TotalMilliseconds
                : 0;
        }

        private static MyPlayer LocalPlayer() =>
            MyAPIGateway.Session?.Player as MyPlayer;

        private static bool CanManageNpcs(ulong sender) =>
            MyAPIGateway.Session.CreativeMode || MyAPIGateway.Session.IsAdminModeEnabled(sender);

        private static bool IsAuthoritative =>
            MyMultiplayerModApi.Static == null || MyMultiplayerModApi.Static.IsServer;

        private static MatrixD CreateSpawnTransform(in MatrixD playerTransform)
        {
            var gravity = MyGravityProviderSystem.CalculateTotalGravityInPoint(playerTransform.Translation);
            var up = gravity.LengthSquared() > 0.0001f
                ? -Vector3D.Normalize(gravity)
                : playerTransform.Up;

            var playerForward = Vector3D.Reject(playerTransform.Forward, up);
            if (playerForward.LengthSquared() < 0.0001)
                playerForward = Vector3D.CalculatePerpendicularVector(up);
            playerForward.Normalize();

            var position = playerTransform.Translation + playerForward * SpawnDistance;
            return MatrixD.CreateWorld(position, -playerForward, up);
        }

        private string HelpText() =>
            $"{Command} spawn <webbing> [paratrooper] [enemy] | spawn-enemy [webbing] | list | clear | utility-ai [toggle|on|off|status] | gamelog [toggle|on|off|status].\n Available webbings:\n {KnownWebbingsText()}";

        private static string KnownWebbingsText()
        {
            var webbings = SiNpcTrooperCatalog.GetKnownWebbings();
            return webbings.Count > 0
                ? string.Join("\n", webbings)
                : "none";
        }

        private string UtilityAiDecisionMakingStatusText() =>
            $"UtilityAI decision making {(_utilityDecisionMakingEnabled ? "enabled" : "disabled")}.";

        private static string SquadHelpText() =>
            $"{SquadCommand} list | members";

        private bool Respond(ulong sender, string response)
        {
            _chat?.SendMessageToClient(sender, MyStringHash.GetOrCompute("System"), 0, response);
            return true;
        }

        private bool RespondSquadRoster(ulong sender)
        {
            var lines = Squads?.CreateRosterLines(Npcs);
            if (lines == null || lines.Count == 0)
                return Respond(sender, "No squad roster is available.");

            foreach (var line in lines)
                Respond(sender, line);
            return true;
        }

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
            QueueControlledEntityBinding(npc, identity);

            var ownership = npc.Entity?.Components.Get<MyEntityOwnershipComponent>();
            if (ownership != null)
                ownership.OwnerId = identity.Id;
        }

        private void QueueControlledEntityBinding(SiNpc npc, MyIdentity identity)
        {
            if (npc == null || identity == null)
                return;

            _pendingControlledEntityBindings[npc.EntityId] = identity.Id;
        }

        private void ApplyPendingControlledEntityBindings()
        {
            if (_pendingControlledEntityBindings.Count == 0 || Npcs == null)
                return;

            _resolvedPendingControlledEntityNpcIds.Clear();
            foreach (var entry in _pendingControlledEntityBindings)
            {
                if (!Npcs.Npcs.TryGetValue(entry.Key, out var npc)
                    || npc?.Entity == null
                    || npc.Entity.Closed
                    || npc.Entity.MarkedForClose)
                {
                    _resolvedPendingControlledEntityNpcIds.Add(entry.Key);
                    continue;
                }

                if (TryBindControlledEntity(entry.Value, npc.Entity))
                    _resolvedPendingControlledEntityNpcIds.Add(entry.Key);
            }

            for (var i = 0; i < _resolvedPendingControlledEntityNpcIds.Count; i++)
                _pendingControlledEntityBindings.Remove(_resolvedPendingControlledEntityNpcIds[i]);
        }

        private static bool TryBindControlledEntity(long identityId, MyEntity entity)
        {
            if (identityId == 0 || entity == null || entity.Closed || entity.MarkedForClose)
                return true;

            var identities = MyIdentities.Static;
            if (identities == null)
                return false;

            var identity = identities.GetIdentity(identityId);
            if (identity == null)
                return false;

            try
            {
                return identities.SetControlledEntity(identity, entity);
            }
            catch
            {
                return false;
            }
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

        private static string PlayerName(MyPlayer player)
        {
            if (!string.IsNullOrWhiteSpace(player?.Identity?.DisplayName))
                return player.Identity.DisplayName;
            if (player?.Identity != null)
                return "Player " + player.Identity.Id;
            return "Player";
        }

        private readonly struct SiNpcSpawnRequest
        {
            public SiNpcSpawnRequest(string webbingSubtype, bool isParatrooper, bool isEnemy)
            {
                WebbingSubtype = string.IsNullOrWhiteSpace(webbingSubtype)
                    ? null
                    : webbingSubtype.Trim();
                IsParatrooper = isParatrooper;
                IsEnemy = isEnemy;
            }

            public string WebbingSubtype { get; }
            public bool IsParatrooper { get; }
            public bool IsEnemy { get; }
            public string DisplayArchetype =>
                string.IsNullOrWhiteSpace(WebbingSubtype)
                    ? "trooper"
                    : WebbingSubtype
                      + (IsParatrooper ? "-paratrooper" : string.Empty)
                      + (IsEnemy ? "-enemy" : string.Empty);
        }

        [RpcSerializable]
        private struct SiNpcSnapshot
        {
            public long EntityId;
            public string Archetype;
            public string WebbingSubtype;
            public bool IsParatrooper;
            public bool IsEnemy;
            public MatrixD Transform;
            public bool HasWaypoint;
            public Vector3D Waypoint;
            public bool HasSquadAssignment;
            public byte SquadLeaderKind;
            public long SquadLeaderId;
            public byte SquadArmyKind;
            public long SquadArmyId;
            public bool IsSquadLeader;
            public string LeaderName;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiNpcSessionComponent : MyObjectBuilder_SessionComponent
    {
        [XmlElement("Npc")]
        public List<SavedNpc> Npcs;

        [XmlElement("SquadOrder")]
        public List<SquadOrder> SquadOrders;

        public class SavedNpc
        {
            [XmlAttribute]
            public long EntityId;

            [XmlAttribute]
            public string Archetype;

            [XmlAttribute]
            public string WebbingSubtype;

            [XmlAttribute]
            public bool IsParatrooper;

            [XmlAttribute]
            public bool IsEnemy;

            public MyPositionAndOrientation Transform;

            public bool HasWaypoint;

            public SerializableVector3D Waypoint;

            public bool HasSquadAssignment;

            [XmlAttribute]
            public byte SquadLeaderKind;

            [XmlAttribute]
            public long SquadLeaderId;

            [XmlAttribute]
            public byte SquadArmyKind;

            [XmlAttribute]
            public long SquadArmyId;

            [XmlAttribute]
            public bool IsSquadLeader;

            [XmlAttribute]
            public string LeaderName;

            [XmlAttribute]
            public long DiplomaticIdentityId;

            [XmlAttribute]
            public bool HasTransportState;

            [XmlAttribute]
            public long TransportVehicleEntityId;

            [XmlAttribute]
            public long SeatEntityId;

            [XmlAttribute]
            public string SeatSlotName;

            public bool HasTransportExitLocalPosition;

            public SerializableVector3D TransportExitLocalPosition;

            [XmlAttribute]
            public bool WasInTransportSeat;
        }

        public class SquadOrder
        {
            [XmlAttribute]
            public long LeaderIdentityId;

            [XmlAttribute]
            public byte Mode;

            [XmlAttribute]
            public byte Formation;

            [XmlAttribute]
            public byte EngagementStance;

            [XmlAttribute]
            public byte TransportMode;

            [XmlAttribute]
            public long TransportVehicleEntityId;

            [XmlAttribute]
            public byte CombatStance;
        }
    }

    internal enum SiSquadOrderMode
    {
        Stopped,
        Follow,
    }

    internal enum SiSquadTransportMode
    {
        None,
        Mount,
        Disembark,
    }

    internal enum SiSquadFormation
    {
        Column,
        File,
        Line,
        Vee,
    }

    internal enum SiSquadEngagementStance
    {
        Enemies,
        EnemiesNeutrals,
        HoldFire,
    }

    internal enum SiSquadCombatStance
    {
        Safe,
        Combat,
    }

    internal enum SiSquadCombatTransitionReason
    {
        PlayerOrder,
        OpeningFire,
        EnemySpotted,
        TakingFire,
        AreaClear,
    }

    internal sealed class SiSquadCommandState
    {
        public SiSquadOrderMode Mode { get; set; }
        public SiSquadFormation Formation { get; set; }
        public SiSquadEngagementStance EngagementStance { get; set; }
        public SiSquadTransportMode TransportMode { get; set; }
        public long TransportVehicleEntityId { get; set; }
        public SiSquadTransportMode TransportCadenceMode { get; set; }
        public long NextTransportActionTimeMilliseconds { get; set; }
    }

    internal sealed class SiSquadCombatState
    {
        public string LeaderName { get; set; }
        public SiSquadCombatStance Stance { get; set; }
        public long LastShotAtTime { get; set; }
        public long LastEnemySpottedTime { get; set; }
        public long LastStanceChangeTime { get; set; }
        public long CombatEntryToken { get; set; }
    }

    internal sealed class SiMotionState
    {
        public bool HasPosition { get; set; }
        public Vector3D Position { get; set; }
        public Vector3D Direction { get; set; }
    }

    internal sealed class SiTransportNpcState
    {
        public long VehicleEntityId { get; set; }
        public long SeatEntityId { get; set; }
        public string SeatSlotName { get; set; }
        public bool HasExitLocalPosition { get; set; }
        public Vector3D ExitLocalPosition { get; set; }
    }

    internal struct SiCoverReservation
    {
        public SiCoverReservation(in Vector3D position, double radius)
        {
            Position = position;
            Radius = radius;
        }

        public Vector3D Position { get; }
        public double Radius { get; }
    }

    internal sealed class SiCoverSearchCacheEntry
    {
        public long ExpiresAtMilliseconds;
        public readonly List<SiCoverSearchCandidate> Candidates = new List<SiCoverSearchCandidate>();
        public int ScannedSectors;
        public int IntersectingSectors;
        public int FoliageEntries;
        public int CandidateCount;
        public int StandingRejects;
        public int ViableCount;
    }

    internal sealed class SiCoverScanCacheEntry
    {
        public long ExpiresAtMilliseconds;
        public readonly List<Vector3D> CoverPositions = new List<Vector3D>();
        public int ScannedSectors;
        public int IntersectingSectors;
        public int FoliageEntries;
        public int CandidateCount;
    }

    internal struct SiCoverSearchCandidate
    {
        public SiCoverSearchCandidate(
            in Vector3D coverPosition,
            in Vector3D standPosition,
            bool isTree,
            double distanceSquared)
        {
            CoverPosition = coverPosition;
            StandPosition = standPosition;
            IsTree = isTree;
            DistanceSquared = distanceSquared;
        }

        public Vector3D CoverPosition { get; }
        public Vector3D StandPosition { get; }
        public bool IsTree { get; }
        public double DistanceSquared { get; }
    }

    internal struct SiCoverSearchCacheKey : IEquatable<SiCoverSearchCacheKey>
    {
        private const double ThreatDirectionQuantization = 0.35;
        private readonly int _originX;
        private readonly int _originY;
        private readonly int _originZ;
        private readonly int _radius;
        private readonly int _directionX;
        private readonly int _directionY;
        private readonly int _directionZ;
        private readonly MyDefinitionId _behaviorDefinitionId;

        public SiCoverSearchCacheKey(
            in Vector3D searchOrigin,
            double searchRadius,
            in Vector3D threatPosition,
            long threatEntityId,
            MyDefinitionId behaviorDefinitionId,
            double quantization)
        {
            _originX = Quantize(searchOrigin.X, quantization);
            _originY = Quantize(searchOrigin.Y, quantization);
            _originZ = Quantize(searchOrigin.Z, quantization);
            _radius = Quantize(searchRadius, 0.25);
            var direction = ResolveThreatDirection(searchOrigin, threatPosition, threatEntityId);
            _directionX = Quantize(direction.X, ThreatDirectionQuantization);
            _directionY = Quantize(direction.Y, ThreatDirectionQuantization);
            _directionZ = Quantize(direction.Z, ThreatDirectionQuantization);
            _behaviorDefinitionId = behaviorDefinitionId;
        }

        public bool Equals(SiCoverSearchCacheKey other)
        {
            return _originX == other._originX
                   && _originY == other._originY
                   && _originZ == other._originZ
                   && _radius == other._radius
                   && _directionX == other._directionX
                   && _directionY == other._directionY
                   && _directionZ == other._directionZ
                   && _behaviorDefinitionId.Equals(other._behaviorDefinitionId);
        }

        public override bool Equals(object obj)
        {
            return obj is SiCoverSearchCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + _originX;
                hash = hash * 31 + _originY;
                hash = hash * 31 + _originZ;
                hash = hash * 31 + _radius;
                hash = hash * 31 + _directionX;
                hash = hash * 31 + _directionY;
                hash = hash * 31 + _directionZ;
                hash = hash * 31 + _behaviorDefinitionId.GetHashCode();
                return hash;
            }
        }

        private static Vector3D ResolveThreatDirection(
            in Vector3D searchOrigin,
            in Vector3D threatPosition,
            long threatEntityId)
        {
            var delta = threatPosition - searchOrigin;
            if (delta.LengthSquared() > 0.0001)
                return Vector3D.Normalize(delta);

            if (threatEntityId != 0)
                return Vector3D.Forward;

            return Vector3D.Zero;
        }

        private static int Quantize(double value, double step)
        {
            if (step <= 0)
                return 0;

            return (int)Math.Round(value / step);
        }
    }

    internal struct SiCoverScanCacheKey : IEquatable<SiCoverScanCacheKey>
    {
        private readonly int _originX;
        private readonly int _originY;
        private readonly int _originZ;
        private readonly int _radius;
        private readonly MyDefinitionId _behaviorDefinitionId;

        public SiCoverScanCacheKey(
            in Vector3D searchOrigin,
            double searchRadius,
            MyDefinitionId behaviorDefinitionId,
            double quantization)
        {
            _originX = Quantize(searchOrigin.X, quantization);
            _originY = Quantize(searchOrigin.Y, quantization);
            _originZ = Quantize(searchOrigin.Z, quantization);
            _radius = Quantize(searchRadius, 0.25);
            _behaviorDefinitionId = behaviorDefinitionId;
        }

        public bool Equals(SiCoverScanCacheKey other)
        {
            return _originX == other._originX
                   && _originY == other._originY
                   && _originZ == other._originZ
                   && _radius == other._radius
                   && _behaviorDefinitionId.Equals(other._behaviorDefinitionId);
        }

        public override bool Equals(object obj)
        {
            return obj is SiCoverScanCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + _originX;
                hash = hash * 31 + _originY;
                hash = hash * 31 + _originZ;
                hash = hash * 31 + _radius;
                hash = hash * 31 + _behaviorDefinitionId.GetHashCode();
                return hash;
            }
        }

        private static int Quantize(double value, double step)
        {
            if (step <= 0)
                return 0;

            return (int)Math.Round(value / step);
        }
    }

    internal struct SiFollowAnchor
    {
        public SiFollowAnchor(in Vector3D position, in Vector3D forward)
        {
            Position = position;
            Forward = forward;
        }

        public Vector3D Position { get; }
        public Vector3D Forward { get; }
    }

    internal enum SiNpcCachedPositionKind
    {
        None,
        Formation,
        Cover,
        PlainView,
    }

    internal sealed class SiNpcPositionCacheState
    {
        public bool HasFormation { get; private set; }
        public bool HasCover { get; private set; }
        public bool HasPlainView { get; private set; }
        public Vector3D FormationPosition { get; private set; }
        public Vector3D CoverPosition { get; private set; }
        public Vector3D PlainViewPosition { get; private set; }

        public bool IsEmpty => !HasFormation && !HasCover && !HasPlainView;

        public void Set(SiNpcCachedPositionKind kind, in Vector3D position)
        {
            switch (kind)
            {
                case SiNpcCachedPositionKind.Formation:
                    FormationPosition = position;
                    HasFormation = true;
                    return;
                case SiNpcCachedPositionKind.Cover:
                    CoverPosition = position;
                    HasCover = true;
                    return;
                case SiNpcCachedPositionKind.PlainView:
                    PlainViewPosition = position;
                    HasPlainView = true;
                    return;
                case SiNpcCachedPositionKind.None:
                default:
                    return;
            }
        }

        public void Clear(SiNpcCachedPositionKind kind)
        {
            switch (kind)
            {
                case SiNpcCachedPositionKind.Formation:
                    HasFormation = false;
                    FormationPosition = Vector3D.Zero;
                    return;
                case SiNpcCachedPositionKind.Cover:
                    HasCover = false;
                    CoverPosition = Vector3D.Zero;
                    return;
                case SiNpcCachedPositionKind.PlainView:
                    HasPlainView = false;
                    PlainViewPosition = Vector3D.Zero;
                    return;
                case SiNpcCachedPositionKind.None:
                default:
                    return;
            }
        }

        public bool TryGet(SiNpcCachedPositionKind kind, out Vector3D position)
        {
            position = Vector3D.Zero;
            switch (kind)
            {
                case SiNpcCachedPositionKind.Formation:
                    position = FormationPosition;
                    return HasFormation;
                case SiNpcCachedPositionKind.Cover:
                    position = CoverPosition;
                    return HasCover;
                case SiNpcCachedPositionKind.PlainView:
                    position = PlainViewPosition;
                    return HasPlainView;
                case SiNpcCachedPositionKind.None:
                default:
                    return false;
            }
        }
    }
}
