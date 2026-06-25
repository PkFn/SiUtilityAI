using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Medieval.GameSystems.Factions;
using Sandbox.Game.GameSystems.Chat;
using Sandbox.Game.Players;
using Sandbox.ModAPI;
using VRage;
using VRage.Components;
using VRage.Game;
using VRage.Entities.Gravity;
using VRage.Game.Components;
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
        private static readonly MyStringHash HostileRelationship = MyStringHash.GetOrCompute("War");

        private static SiNpcSessionComponent _instance;
        private readonly Dictionary<long, SiSquadCommandState> _squadOrders =
            new Dictionary<long, SiSquadCommandState>();
        private List<MyObjectBuilder_SiNpcSessionComponent.SavedNpc> _savedNpcs;
        private List<MyObjectBuilder_SiNpcSessionComponent.SquadOrder> _savedSquadOrders;

        [Automatic]
        private readonly MyChatSystem _chat = null;
        private bool _showTroopMarkers;

        public static SiNpcSessionComponent Instance => _instance;
        public SiNpcManager Npcs { get; private set; }
        internal SiSquadBook Squads { get; private set; }

        protected override void OnLoad()
        {
            base.OnLoad();
            _instance = this;
            Npcs = new SiNpcManager();
            Squads = new SiSquadBook();
            Npcs.WaypointSet += OnWaypointSet;
            Npcs.WaypointCleared += OnWaypointCleared;

            _chat?.RegisterChatCommand(
                Command,
                HandleCommand,
                "Manage custom Si Utility AI NPCs. /si-npc spawn [trooper|enemy-trooper] | spawn-enemy | list | clear",
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
            }
            Npcs?.CloseAll(false);
            Npcs = null;
            _squadOrders.Clear();
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
                UpdateSquadOrders();
            Npcs?.Update(elapsedMilliseconds);
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
            if (command == SiUtilityCommandMenuCommand.ToggleUi)
            {
                ToggleTroopMarkers();
                return;
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

        public void Draw()
        {
            if (!_showTroopMarkers || Npcs == null || Squads == null)
                return;

            var player = LocalPlayer();
            if (player?.Identity == null)
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

                MyRenderProxy.DebugDrawText3D(
                    position + entity.WorldMatrix.Up * definition.MarkerHeight,
                    marker.Label,
                    Color.LightGreen,
                    definition.MarkerTextScale,
                    align: MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER);
            }
        }

        private void ToggleTroopMarkers()
        {
            _showTroopMarkers = !_showTroopMarkers;
            MyAPIGateway.Utilities?.ShowNotification(
                _showTroopMarkers
                    ? "Si Utility AI troop markers shown."
                    : "Si Utility AI troop markers hidden.",
                1500);
        }

        private void ExecuteUtilityCommand(MyPlayer player, SiUtilityCommandMenuCommand command)
        {
            if (player?.Identity == null)
                return;

            var sender = player.Id.SteamId;
            var leaderIdentityId = player.Identity.Id;
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
                case SiUtilityCommandMenuCommand.ToggleUi:
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
            Respond(sender, $"Order: {OrderName(state.Mode)}, formation {FormationName(state.Formation)}.");
        }

        private void StopSquad(ulong sender, long leaderIdentityId)
        {
            var state = GetSquadOrder(leaderIdentityId);
            state.Mode = SiSquadOrderMode.Stopped;
            var cleared = ClearLeaderWaypoints(leaderIdentityId);
            Respond(sender, $"Stopped {cleared} utility AI troop(s).");
        }

        private void FollowSquad(ulong sender, long leaderIdentityId)
        {
            var state = GetSquadOrder(leaderIdentityId);
            state.Mode = SiSquadOrderMode.Follow;

            string failure;
            var ordered = ApplyFollowOrder(leaderIdentityId, state, true, out failure);
            Respond(sender, failure ?? $"Following in {FormationName(state.Formation)} formation with {ordered} troop(s).");
        }

        private void SetFormation(ulong sender, long leaderIdentityId, SiSquadFormation formation)
        {
            var state = GetSquadOrder(leaderIdentityId);
            state.Formation = formation;
            state.Mode = SiSquadOrderMode.Follow;

            string failure;
            var ordered = ApplyFollowOrder(leaderIdentityId, state, true, out failure);
            Respond(sender, failure ?? $"Formation set to {FormationName(formation)} for {ordered} troop(s).");
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
                    Squads?.ClearNpcs();
                    BroadcastClear();
                    return Respond(sender, $"Removed {removed} custom NPC(s).");
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
            if (string.Equals(archetype, SiNpcManager.EnemyTrooperArchetype, StringComparison.OrdinalIgnoreCase))
                return ConfigureEnemyTrooper(npc, player, out failure);

            Squads?.AssignNpcToPlayer(npc, player);
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

            npc.SetDiplomaticIdentity(identity, true);
            var ownership = npc.Entity?.Components.Get<MyEntityOwnershipComponent>();
            if (ownership != null)
                ownership.OwnerId = identity.Id;

            if (!TryMarkHostileToCaller(player, identity.Id, out failure))
                return false;

            Squads?.AssignNpcAsAiLeader(npc, EnemyTrooperName(npc), EnemyArmyIdFor(player));
            return true;
        }

        private static bool TryMarkHostileToCaller(MyPlayer player, long enemyIdentityId, out string failure)
        {
            failure = null;
            var diplomacy = MyDiplomacyManager.Instance;
            if (diplomacy == null)
            {
                failure = "Diplomacy manager is not available; enemy relation could not be set.";
                return false;
            }

            try
            {
                var enemyParty = new MyDiplomaticParty(DiplomaticPartyType.Player, enemyIdentityId);
                diplomacy.SetRelationshipBetweenParties(
                    new MyDiplomaticParty(DiplomaticPartyType.Player, player.Identity.Id),
                    enemyParty,
                    HostileRelationship);

                var faction = PlayerFaction(player.Identity.Id);
                if (faction != null)
                    diplomacy.SetRelationshipBetweenParties(
                        new MyDiplomaticParty(faction),
                        enemyParty,
                        HostileRelationship);
                return true;
            }
            catch (Exception exception)
            {
                failure = "Failed to mark the enemy NPC hostile: " + exception.Message;
                return false;
            }
        }

        private static long EnemyArmyIdFor(MyPlayer player)
        {
            var faction = player?.Identity != null ? PlayerFaction(player.Identity.Id) : null;
            return faction?.FactionId ?? player?.Identity?.Id ?? 0;
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

                string failure;
                ApplyFollowOrder(entry.Key, state, false, out failure);
            }
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

            Vector3D origin;
            Vector3D forward;
            Vector3D right;
            CreateLeaderFrame(leaderTransform, out origin, out forward, out right);

            var definition = Squads.Definition;
            var refreshDistanceSquared = definition.WaypointRefreshDistance * definition.WaypointRefreshDistance;
            var issued = 0;
            for (var i = 0; i < troops.Count; i++)
            {
                var target = origin + FormationOffset(
                    state.Formation,
                    i,
                    troops.Count,
                    forward,
                    right,
                    definition);
                var mover = troops[i] as ISiWaypointMover;
                if (mover != null
                    && mover.HasWaypoint
                    && refreshDistanceSquared > 0
                    && Vector3D.DistanceSquared(mover.Waypoint, target) < refreshDistanceSquared)
                {
                    issued++;
                    continue;
                }

                if (Npcs.TrySetWaypoint(troops[i].EntityId, target))
                    issued++;
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

        private SiSquadCommandState GetSquadOrder(long leaderIdentityId)
        {
            SiSquadCommandState state;
            if (!_squadOrders.TryGetValue(leaderIdentityId, out state))
                _squadOrders.Add(leaderIdentityId, state = new SiSquadCommandState());
            return state;
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
                    return -forward * (definition.FollowDistance + index * definition.FileSpacing);
                case SiSquadFormation.Line:
                    return -forward * definition.FollowDistance
                           + right * ((index - (count - 1) * 0.5) * definition.LineSpacing);
                case SiSquadFormation.Vee:
                    var row = (index + 2) / 2;
                    var side = index % 2 == 0 ? -1 : 1;
                    return -forward * (definition.FollowDistance + row * definition.VeeSpacing)
                           + right * (side * row * definition.VeeSpacing);
                case SiSquadFormation.Column:
                default:
                    return -forward * (definition.FollowDistance + index * definition.ColumnSpacing);
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
            out Vector3D origin,
            out Vector3D forward,
            out Vector3D right)
        {
            origin = leaderTransform.Translation;
            var gravity = MyGravityProviderSystem.CalculateTotalGravityInPoint(origin);
            var up = gravity.LengthSquared() > 0.0001
                ? -Vector3D.Normalize(gravity)
                : NormalizedOrFallback(leaderTransform.Up, Vector3D.Up);

            forward = Vector3D.Reject(leaderTransform.Forward, up);
            forward = NormalizedOrFallback(forward, Vector3D.CalculatePerpendicularVector(up));
            right = NormalizedOrFallback(Vector3D.Cross(forward, up), leaderTransform.Right);
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

        private static string HelpText() =>
            $"{Command} spawn [{SiNpcManager.SoldierArchetype}|{SiNpcManager.EnemyTrooperArchetype}] | spawn-enemy | list | clear";

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
                saved.Add(CreateSavedNpc(npc));
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
            foreach (var saved in savedOrders)
            {
                if (saved == null
                    || saved.LeaderIdentityId == 0
                    || !Enum.IsDefined(typeof(SiSquadOrderMode), (int)saved.Mode)
                    || !Enum.IsDefined(typeof(SiSquadFormation), (int)saved.Formation))
                    continue;

                _squadOrders[saved.LeaderIdentityId] = new SiSquadCommandState
                {
                    Mode = (SiSquadOrderMode)saved.Mode,
                    Formation = (SiSquadFormation)saved.Formation,
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

    internal sealed class SiSquadCommandState
    {
        public SiSquadOrderMode Mode { get; set; }
        public SiSquadFormation Formation { get; set; }
    }
}
