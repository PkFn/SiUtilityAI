using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Medieval.GameSystems.Factions;
using Sandbox.Definitions.Chat;
using Sandbox.Game.GameSystems.Chat;
using Sandbox.Game.Players;
using Sandbox.ModAPI;
using VRage;
using VRage.Components;
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
        private readonly Dictionary<long, SiCoverReservation> _coverReservations =
            new Dictionary<long, SiCoverReservation>();
        private readonly List<long> _staleCoverReservationIds = new List<long>();
        private List<MyObjectBuilder_SiNpcSessionComponent.SavedNpc> _savedNpcs;
        private List<MyObjectBuilder_SiNpcSessionComponent.SquadOrder> _savedSquadOrders;

        [Automatic]
        private readonly MyChatSystem _chat = null;
        private bool _showTroopMarkers;
        private bool _showSquadChatter;
        private bool _utilityDecisionMakingEnabled = true;

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

            _chat?.RegisterChatCommand(
                Command,
                HandleCommand,
                "Manage custom Si Utility AI NPCs. /si-npc spawn [archetype] | spawn-enemy | list | clear | utility-ai [toggle|on|off|status]",
                MyChatCommandType.Server);
            _chat?.RegisterChatCommand(
                EnemyCommand,
                HandleEnemyCommand,
                "Spawn a hostile test Si Utility AI trooper. /si-enemy [spawn]",
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
            _coverReservations.Clear();
            _staleCoverReservationIds.Clear();
            Spotting?.Clear();
            Spotting = null;
            _savedNpcs = null;
            _savedSquadOrders = null;
            Squads?.ClearNpcs();
            Squads = null;
            if (_instance == this)
                _instance = null;
            base.OnUnload();
        }

        [Update(100)]
        private void UpdateNpcs(long elapsedMilliseconds)
        {
            if (IsAuthoritative)
            {
                UpdateTrackedMotionStates();
                UpdateSquadOrders();
                UpdateCombatStances();
                CleanupCoverReservations();
            }
            Npcs?.Update(elapsedMilliseconds);
            if (IsAuthoritative)
                Spotting?.Update(elapsedMilliseconds);
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

                if (observation.SpottingSum > highestSpottingSum)
                {
                    highestSpottingSum = observation.SpottingSum;
                    highestSpottingThreshold = observation.SpottingThreshold;
                    isSpotted = observation.IsSpotted;
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

        private void SpeakPlayerCommand(MyPlayer player, SiUtilityCommandMenuCommand command)
        {
            var message = UtilityCommandSpeech(command);
            if (string.IsNullOrWhiteSpace(message) || player == null)
                return;

            var chat = _chat ?? MyChatSystem.Static;
            chat?.BroadcastMessage(SpeakChannel, player.Id.SteamId, message);
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
                $"Order: {OrderName(state.Mode)}, formation {FormationName(state.Formation)}, engagement {EngagementName(state.EngagementStance)}, combat {CombatStanceName(GetCombatStance(PlayerLeaderKey(leaderIdentityId)))}.");
        }

        private void StopSquad(ulong sender, long leaderIdentityId)
        {
            var state = GetSquadOrder(leaderIdentityId);
            state.Mode = SiSquadOrderMode.Stopped;
            var cleared = ClearLeaderWaypoints(leaderIdentityId);
        }

        private void FollowSquad(ulong sender, long leaderIdentityId)
        {
            var state = GetSquadOrder(leaderIdentityId);
            state.Mode = SiSquadOrderMode.Follow;

            string failure;
            var ordered = ApplyFollowOrder(leaderIdentityId, state, true, out failure);
        }

        private void SetFormation(ulong sender, long leaderIdentityId, SiSquadFormation formation)
        {
            var state = GetSquadOrder(leaderIdentityId);
            state.Formation = formation;
            state.Mode = SiSquadOrderMode.Follow;

            string failure;
            var ordered = ApplyFollowOrder(leaderIdentityId, state, true, out failure);
        }

        private void SetEngagementStance(long leaderIdentityId, SiSquadEngagementStance engagementStance)
        {
            var state = GetSquadOrder(leaderIdentityId);
            state.EngagementStance = engagementStance;
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
                ClearSquadWaypoints(leader);

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
                    return SpawnFromCommand(sender, tokens.Length >= 3
                        ? tokens[2]
                        : SiNpcManager.SoldierArchetype);
                case "spawn-enemy":
                case "enemy":
                    return SpawnFromCommand(sender, SiNpcManager.EnemyTrooperArchetype);
                case "list":
                    return Respond(sender, $"Custom NPCs alive: {Npcs.Npcs.Count}.");
                case "clear":
                    var removed = Npcs.Npcs.Count;
                    Npcs.CloseAll();
                    _squadOrders.Clear();
                    _squadCombatStates.Clear();
                    Squads?.ClearNpcs();
                    BroadcastClear();
                    return Respond(sender, $"Removed {removed} custom NPC(s).");
                case "utility-ai":
                case "utilityai":
                case "ai":
                    return HandleUtilityAiCommand(sender, tokens);
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

            return SpawnFromCommand(sender, SiNpcManager.EnemyTrooperArchetype);
        }

        private bool SpawnFromCommand(ulong sender, string archetype)
        {
            if (!Npcs.IsKnownArchetype(archetype))
                return Respond(sender, $"Unknown NPC archetype '{archetype}'. Available: {Npcs.KnownArchetypesText}.");

            var player = MyPlayers.Static.GetPlayer(new MyPlayer.PlayerId(sender, 0));
            var playerPosition = player?.ControlledEntity?.Get<MyPositionComponentBase>();
            if (playerPosition == null)
                return Respond(sender, "You must control a character to spawn an NPC.");

            var transform = CreateSpawnTransform(playerPosition.WorldMatrix);
            var entityId = MyEntityIdentifier.AllocateId();
            if (!Npcs.TrySpawn(archetype, entityId, transform, out var npc))
                return Respond(sender, $"Failed to spawn custom NPC '{archetype}'; its model or entity definition could not be loaded.");

            string failure;
            if (!ConfigureSpawnedNpc(archetype, npc, player, out failure))
            {
                Npcs.Close(entityId);
                return Respond(sender, failure ?? $"Failed to configure custom NPC '{archetype}'.");
            }

            BroadcastSpawn(npc);
            return Respond(sender, $"Spawned {archetype} ({entityId}).");
        }

        private bool ConfigureSpawnedNpc(
            string archetype,
            SiNpc npc,
            MyPlayer player,
            out string failure)
        {
            failure = null;
            if (Npcs != null && Npcs.IsHostileToSpawner(archetype))
                return ConfigureEnemyTrooper(npc, player, out failure);

            if (!ConfigureFriendlyTrooper(npc, player, out failure))
                return false;

            Squads?.AssignNpcToPlayer(npc, player);
            return true;
        }

        private static bool ConfigureFriendlyTrooper(SiNpc npc, MyPlayer player, out string failure)
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

            AssignNpcToEnemyFaction(npc, enemyFaction);
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

        private void AssignNpcToEnemyFaction(SiNpc npc, MyFaction enemyFaction)
        {
            if (npc == null || enemyFaction == null)
                return;

            Squads?.AssignNpcToLeader(
                npc,
                SiSquadLeaderKind.Ai,
                npc.EntityId,
                SiArmyKind.Faction,
                enemyFaction.FactionId,
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
                if (state.Mode != SiSquadOrderMode.Follow)
                    continue;
                var leader = PlayerLeaderKey(entry.Key);
                var combatStance = GetCombatStance(leader);
                if (combatStance == SiSquadCombatStance.Combat)
                    continue;

                string failure;
                ApplyFollowOrder(entry.Key, state, false, out failure);
            }
        }

        private void CleanupCoverReservations()
        {
            if (_coverReservations.Count == 0 || Npcs == null)
                return;

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
            _staleCoverReservationIds.Clear();
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
                    if (TryIssueFollowWaypoint(troops[i], target, refreshDistanceSquared))
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
                if (TryIssueFollowWaypoint(troops[i], target, refreshDistanceSquared))
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

        private bool TryIssueFollowWaypoint(SiNpc npc, in Vector3D target, double refreshDistanceSquared)
        {
            var mover = npc as ISiWaypointMover;
            if (mover != null
                && mover.HasWaypoint
                && refreshDistanceSquared > 0
                && Vector3D.DistanceSquared(mover.Waypoint, target) < refreshDistanceSquared)
                return true;

            return Npcs.TrySetWaypoint(npc.EntityId, target);
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
            $"{Command} spawn [archetype] | spawn-enemy | list | clear | utility-ai [toggle|on|off|status]. Available: {Npcs?.KnownArchetypesText ?? SiNpcManager.SoldierArchetype}";

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
                || string.IsNullOrWhiteSpace(saved.Archetype)
                || !Npcs.IsKnownArchetype(saved.Archetype))
                return;

            SiNpc npc;
            if (!Npcs.TrySpawn(saved.Archetype, saved.EntityId, saved.Transform.GetMatrix(), out npc))
                return;

            RestoreDiplomaticIdentity(saved, npc);
            if (Npcs != null && Npcs.IsHostileToSpawner(saved.Archetype))
            {
                if (!RestoreHostileNpcFaction(saved, npc))
                    RestoreSquadAssignment(saved, npc);
            }
            else
                RestoreSquadAssignment(saved, npc);

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
            SiNpc npc)
        {
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

            MyFaction enemyFaction;
            string failure;
            if (!TryAssignIdentityToEnemyFaction(npc.DiplomaticIdentityId, out enemyFaction, out failure))
                return false;

            AssignNpcToEnemyFaction(npc, enemyFaction);
            return true;
        }

        private static void SetNpcDiplomaticIdentity(SiNpc npc, MyIdentity identity)
        {
            if (npc == null || identity == null)
                return;

            npc.SetDiplomaticIdentity(identity, true);
            try
            {
                if (npc.Entity != null)
                    MyIdentities.Static?.SetControlledEntity(identity, npc.Entity);
            }
            catch
            {
            }

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
                    || !Enum.IsDefined(typeof(SiSquadCombatStance), (int)saved.CombatStance))
                    continue;

                _squadOrders[saved.LeaderIdentityId] = new SiSquadCommandState
                {
                    Mode = (SiSquadOrderMode)saved.Mode,
                    Formation = (SiSquadFormation)saved.Formation,
                    EngagementStance = (SiSquadEngagementStance)saved.EngagementStance,
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
            SiAssignedNpc assignment = null;
            var hasAssignment = _instance?.Squads != null
                                && _instance.Squads.TryGetAssignment(npc.EntityId, out assignment);
            return new MyObjectBuilder_SiNpcSessionComponent.SavedNpc
            {
                EntityId = npc.EntityId,
                Archetype = npc.Archetype,
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
            };
        }

        private static SiNpcSnapshot CreateSnapshot(SiNpc npc)
        {
            var mover = npc as ISiWaypointMover;
            SiAssignedNpc assignment = null;
            var hasAssignment = _instance?.Squads != null
                                && _instance.Squads.TryGetAssignment(npc.EntityId, out assignment);
            return new SiNpcSnapshot
            {
                EntityId = npc.EntityId,
                Archetype = npc.Archetype,
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

        private static bool IsLocalPlayerInSpeakRange(Vector3D position)
        {
            var player = LocalPlayer();
            var playerPosition = player?.ControlledEntity?.Get<MyPositionComponentBase>();
            if (playerPosition == null)
                return false;

            var rangeSquared = SpeakRange * SpeakRange;
            return Vector3D.DistanceSquared(position, playerPosition.WorldMatrix.Translation) <= rangeSquared;
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

        private static void BroadcastSpawn(SiNpc npc)
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
        private static void SpawnNpcClient(SiNpcSnapshot snapshot)
        {
            SiNpc npc = null;
            _instance?.Npcs?.TrySpawn(
                snapshot.Archetype,
                snapshot.EntityId,
                snapshot.Transform,
                out npc);
            if (npc != null && snapshot.HasSquadAssignment)
                _instance?.Squads?.AssignNpcToLeader(
                    npc,
                    (SiSquadLeaderKind)snapshot.SquadLeaderKind,
                    snapshot.SquadLeaderId,
                    (SiArmyKind)snapshot.SquadArmyKind,
                    snapshot.SquadArmyId,
                    snapshot.LeaderName,
                    snapshot.IsSquadLeader);
            if (snapshot.HasWaypoint)
                _instance?.Npcs?.ApplyWaypoint(snapshot.EntityId, snapshot.Waypoint);
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

        [RpcSerializable]
        private struct SiNpcSnapshot
        {
            public long EntityId;
            public string Archetype;
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
            public byte CombatStance;
        }
    }

    internal enum SiSquadOrderMode
    {
        Stopped,
        Follow,
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
    }

    internal sealed class SiSquadCombatState
    {
        public string LeaderName { get; set; }
        public SiSquadCombatStance Stance { get; set; }
        public long LastShotAtTime { get; set; }
        public long LastEnemySpottedTime { get; set; }
        public long LastStanceChangeTime { get; set; }
    }

    internal sealed class SiMotionState
    {
        public bool HasPosition { get; set; }
        public Vector3D Position { get; set; }
        public Vector3D Direction { get; set; }
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
}
