using System;
using System.Collections.Generic;
using Equinox76561198048419394.Core.Controller;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems.Chat;
using Sandbox.ModAPI;
using SiCore.Core.Debug;
using VRage.Components;
using VRage.Components.Interfaces;
using VRage.Game.Components;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Components;
using VRage.Session;
using VRageRender;

namespace Si.UtilityAI
{
    [StaticEventOwner]
    [MySessionComponent(typeof(MyObjectBuilder_SiNpcSessionComponent), AllowAutomaticCreation = true, AlwaysOn = true)]
    [MyDependency(typeof(MyChatSystem), Critical = false)]
    public sealed partial class SiNpcSessionComponent : MySessionComponent, IDraw
    {
        private const double SpawnDistance = 2.5;
        private const double SpawnProbeLength = 1.6;
        private const double SpawnProbeElevation = 2.0;
        private const int SpawnProbeMaxElevations = 2;
        private const long CombatStanceCooldownMilliseconds = 60000;
        private const long CoverScanCacheLifetimeMilliseconds = 1000;
        private const long CoverSearchCacheLifetimeMilliseconds = 750;
        private const double CoverScanCachePositionQuantization = 6.0;
        private const double CoverSearchCachePositionQuantization = 8.0;
        private const double CombatStanceNearbyEnemyDistance = 80;
        private const double AiMapCommandArrivalDistance = 4.0;

        private static SiNpcSessionComponent _instance;
        private static double _speakRange = -1;
        private readonly Dictionary<long, SiSquadCommandState> _squadOrders =
            new Dictionary<long, SiSquadCommandState>();
        private readonly Dictionary<SiSquadLeaderKey, SiSquadCombatState> _squadCombatStates =
            new Dictionary<SiSquadLeaderKey, SiSquadCombatState>();
        private readonly Dictionary<SiSquadLeaderKey, SiAiSquadMoveOrderState> _aiSquadMoveOrders =
            new Dictionary<SiSquadLeaderKey, SiAiSquadMoveOrderState>();
        private readonly Dictionary<long, SiPlayerLeaderState> _playerLeaderStates =
            new Dictionary<long, SiPlayerLeaderState>();
        private readonly Dictionary<long, SiMotionState> _leaderMotionStates =
            new Dictionary<long, SiMotionState>();
        private readonly Dictionary<long, SiMotionState> _npcMotionStates =
            new Dictionary<long, SiMotionState>();
        private readonly Dictionary<long, SiNpcSnapshot> _pendingNpcSnapshots =
            new Dictionary<long, SiNpcSnapshot>();
        private readonly Dictionary<long, SiNpcPositionCacheState> _positionCache =
            new Dictionary<long, SiNpcPositionCacheState>();
        private readonly Dictionary<long, SiTransportNpcState> _transportNpcStates =
            new Dictionary<long, SiTransportNpcState>();
        private readonly SiNearbyEntityScanner _transportVehicleScanner = new SiNearbyEntityScanner();
        private readonly List<SiNearbyEntityScanner.EntityCandidate> _nearbyTransportVehicleCandidates =
            new List<SiNearbyEntityScanner.EntityCandidate>();
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
        private readonly List<long> _stalePlayerLeaderIds = new List<long>();
        private readonly List<long> _pendingTransportSeatRestoreNpcIds = new List<long>();
        private readonly List<long> _resolvedPendingNpcIds = new List<long>();
        private List<MyObjectBuilder_SiNpcSessionComponent.SavedNpc> _savedNpcs;
        private List<MyObjectBuilder_SiNpcSessionComponent.SquadOrder> _savedSquadOrders;
        private List<MyObjectBuilder_SiNpcSessionComponent.AiSquadMoveOrder> _savedAiSquadMoveOrders;

        [Automatic]
        private readonly MyChatSystem _chat = null;

        private bool _showTroopMarkers;
        private bool _showSquadChatter;
        private bool _utilityDecisionMakingEnabled = true;
        private readonly SiGameLog _log = new SiGameLog(nameof(SiNpcSessionComponent), "[SiCover]");
        private long _lastCoverCleanupLogTime = long.MinValue;
        private bool _restoreSavedStatePending;

        public static SiNpcSessionComponent Instance => _instance;
        public SiNpcManager Npcs { get; private set; }
        internal SiSquadBook Squads { get; private set; }
        internal SiStaticDefenderSystem StaticDefenders { get; private set; }
        internal SiSpottingSystem Spotting { get; private set; }
        internal SiMarkerSystemDefinition MarkerSettings { get; private set; }
        internal SiVehicleSystemDefinition VehicleSettings { get; private set; }
        internal bool ShowSquadChatter => _showSquadChatter;
        internal bool UtilityDecisionMakingEnabled => _utilityDecisionMakingEnabled;

        protected override void OnLoad()
        {
            base.OnLoad();
            _instance = this;
            Npcs = new SiNpcManager();
            Squads = new SiSquadBook();
            StaticDefenders = new SiStaticDefenderSystem(this);
            Spotting = new SiSpottingSystem(this);
            MarkerSettings = SiMarkerSystemDefinition.Load();
            VehicleSettings = SiVehicleSystemDefinition.Load();
            Npcs.WaypointSet += OnWaypointSet;
            Npcs.WaypointCleared += OnWaypointCleared;
            Npcs.NpcSpoke += OnNpcSpoke;
            if (!IsAuthoritative)
                MyEntities.OnEntityAdd += OnEntityAddedClient;

        }

        protected override void OnSessionReady()
        {
            base.OnSessionReady();
            if (IsAuthoritative)
                _restoreSavedStatePending = true;
            else if (MyMultiplayerModApi.Static != null)
                MyMultiplayerModApi.Static.RaiseStaticEvent(x => RequestNpcSnapshot);
        }

        protected override void OnUnload()
        {
            ClearAiLeaderPersistence();
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
            _aiSquadMoveOrders.Clear();
            _playerLeaderStates.Clear();
            _leaderMotionStates.Clear();
            _npcMotionStates.Clear();
            _pendingNpcSnapshots.Clear();
            _positionCache.Clear();
            _transportNpcStates.Clear();
            _nearbyTransportVehicleCandidates.Clear();
            _coverReservations.Clear();
            ClearSquadMapMarkers();
            _coverScanCache.Clear();
            _coverSearchCache.Clear();
            _expiredCoverScanCacheKeys.Clear();
            _expiredCoverSearchCacheKeys.Clear();
            _staleCoverReservationIds.Clear();
            _stalePlayerLeaderIds.Clear();
            _pendingTransportSeatRestoreNpcIds.Clear();
            _resolvedPendingNpcIds.Clear();
            StaticDefenders?.Clear();
            StaticDefenders = null;
            Spotting?.Clear();
            Spotting = null;
            MarkerSettings = null;
            VehicleSettings = null;
            _savedNpcs = null;
            _savedSquadOrders = null;
            _savedAiSquadMoveOrders = null;
            _restoreSavedStatePending = false;
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
                RestoreSavedStateIfPending();
                UpdateTrackedMotionStates();
                HandleInactivePlayerLedSquads();
                UpdateSquadOrders();
                UpdateCombatStances();
                UpdateCombatMovementSpeeds();
                CleanupTransportStates();
                CleanupPositionCache();
                CleanupExpiredCoverScanCache();
                CleanupExpiredCoverSearchCache();
                CleanupCoverReservations();
                RestorePendingTransportSeats();
            }
            else
                ApplyPendingNpcSnapshots();

            if (IsAuthoritative)
                StaticDefenders?.Update(elapsedMilliseconds);
            Npcs?.Update(elapsedMilliseconds);
            if (IsAuthoritative)
            {
                ReassignLeaderlessSquads();
                UpdateAiLeaderPersistence();
                Spotting?.Update(elapsedMilliseconds);
            }
        }

        private void RestoreSavedStateIfPending()
        {
            if (!_restoreSavedStatePending)
                return;

            _restoreSavedStatePending = false;
            RestoreSavedState();
        }

        protected override bool IsSerialized =>
            (Npcs != null && Npcs.Npcs.Count > 0)
            || (_savedNpcs != null && _savedNpcs.Count > 0)
            || _squadOrders.Count > 0
            || (_savedSquadOrders != null && _savedSquadOrders.Count > 0)
            || _aiSquadMoveOrders.Count > 0
            || (_savedAiSquadMoveOrders != null && _savedAiSquadMoveOrders.Count > 0);

        protected override VRage.Game.MyObjectBuilder_SessionComponent Serialize()
        {
            var ob = (MyObjectBuilder_SiNpcSessionComponent)base.Serialize();

            var npcs = Npcs != null ? CreateSavedNpcs() : _savedNpcs;
            ob.Npcs = npcs != null && npcs.Count > 0 ? npcs : null;

            var orders = _squadOrders.Count > 0 ? CreateSavedSquadOrders() : _savedSquadOrders;
            ob.SquadOrders = orders != null && orders.Count > 0 ? orders : null;

            var aiMoveOrders = _aiSquadMoveOrders.Count > 0
                ? CreateSavedAiSquadMoveOrders()
                : _savedAiSquadMoveOrders;
            ob.AiSquadMoveOrders = aiMoveOrders != null && aiMoveOrders.Count > 0
                ? aiMoveOrders
                : null;
            return ob;
        }

        protected override void Deserialize(VRage.Game.MyObjectBuilder_SessionComponent objectBuilder)
        {
            base.Deserialize(objectBuilder);
            var ob = (MyObjectBuilder_SiNpcSessionComponent)objectBuilder;
            _savedNpcs = ob.Npcs;
            _savedSquadOrders = ob.SquadOrders;
            _savedAiSquadMoveOrders = ob.AiSquadMoveOrders;
        }

        internal void RequestUtilityCommand(SiUtilityCommandMenuCommand command)
        {
            switch (command)
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
    }
}
