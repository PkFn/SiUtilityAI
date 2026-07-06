using System;
using Medieval.GameSystems.Factions;
using Sandbox.Definitions.Chat;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems.Chat;
using Sandbox.Game.Players;
using Sandbox.ModAPI;
using SiCore.Core.Debug;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Scene;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace Si.UtilityAI
{
    public sealed partial class SiNpcSessionComponent
    {
        private static readonly Vector2 SpottingTextAnchor = new Vector2(-0.98f, -0.92f);
        private const string SpeakChannelName = "Speak";
        private static readonly MyStringHash HostileRelationship = MyStringHash.GetOrCompute("War");
        private static readonly MyStringHash SpeakChannel = MyStringHash.GetOrCompute(SpeakChannelName);

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
            ClearLeaderWaypoints(leaderIdentityId);
        }

        private void FollowSquad(ulong sender, long leaderIdentityId)
        {
            var state = GetSquadOrder(leaderIdentityId);
            state.Mode = SiSquadOrderMode.Follow;
            CancelTransportOverride(leaderIdentityId, state);

            string failure;
            ApplyFollowOrder(leaderIdentityId, state, true, out failure);
        }

        private void SetFormation(ulong sender, long leaderIdentityId, SiSquadFormation formation)
        {
            var state = GetSquadOrder(leaderIdentityId);
            state.Formation = formation;
            state.Mode = SiSquadOrderMode.Follow;
            CancelTransportOverride(leaderIdentityId, state);

            string failure;
            ApplyFollowOrder(leaderIdentityId, state, true, out failure);
        }

        private void SetEngagementStance(long leaderIdentityId, SiSquadEngagementStance engagementStance)
        {
            var state = GetSquadOrder(leaderIdentityId);
            state.EngagementStance = engagementStance;
        }

        private bool HandleCommand(ulong sender, string message, MyChatCommandType handledAsType)
        {
            if (!CanManageNpcs(sender))
                return Respond(sender, "Enable Medieval Master to manage custom NPCs in survival.");

            var tokens = message.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
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
                    _squadOrders.Clear();
                    _squadCombatStates.Clear();
                    _playerLeaderStates.Clear();
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

            var tokens = message.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
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
            var tokens = message.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
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

        private static string PlayerName(MyPlayer player)
        {
            if (!string.IsNullOrWhiteSpace(player?.Identity?.DisplayName))
                return player.Identity.DisplayName;
            if (player?.Identity != null)
                return "Player " + player.Identity.Id;
            return "Player";
        }
    }
}
