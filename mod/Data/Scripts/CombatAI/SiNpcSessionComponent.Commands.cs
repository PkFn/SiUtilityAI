using System;
using System.Collections.Generic;
using Equinox76561198048419394.Core.Inventory;
using Equinox76561198048419394.Core.Util;
using Medieval.GameSystems;
using Medieval.GameSystems.Factions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems.Chat;
using Sandbox.Game.Inventory;
using Sandbox.Game.Players;
using Sandbox.ModAPI;
using SiCore.Core.Debug;
using VRage;
using VRage.Collections;
using VRage.Components.Physics;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders;
using VRage.Inventory;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Inventory;
using VRage.Scene;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace Si.UtilityAI
{
    public sealed partial class SiNpcSessionComponent
    {
        private static readonly Vector2 SpottingTextAnchor = new Vector2(-0.98f, -0.92f);
        private const string HouseChannelName = "House";
        private const string SpeakChannelName = "Speak";
        private static readonly MyStringHash HouseChannel = MyStringHash.GetOrCompute(HouseChannelName);
        private static readonly MyStringHash HostileRelationship = MyStringHash.GetOrCompute("War");
        private static readonly MyStringHash SpeakChannel = MyStringHash.GetOrCompute(SpeakChannelName);

        private sealed class SiPendingNpcSpawn
        {
            public SiPendingNpcSpawn(SiNpc npc, SiNpcSpawnRequest request)
            {
                Npc = npc;
                Request = request;
            }

            public SiNpc Npc { get; }
            public SiNpcSpawnRequest Request { get; }
        }

        private struct SiIndependentSquadSpawnContext
        {
            public bool HasLeader;
            public SiSquadLeaderKey Leader;
            public string LeaderName;
        }

        public void Draw()
        {
            var player = LocalPlayer();
            if (player?.Identity == null)
                return;

            DrawPlayerSpottingOverlay(player);

            if (!_showTroopMarkers || Npcs == null || Squads == null)
                return;

            var definition = MarkerSettings;
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
                if (SiNpcAmmoStatusHelper.TryGetAmmoStatus(marker.Npc, out var ammoStatus))
                    label += $"\nAmmo {ammoStatus.MarkerText}";

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
            NotifyShow($"Unit markers {(_showTroopMarkers ? "shown" : "hidden")}.");
        }

        private void ToggleSquadChatter()
        {
            _showSquadChatter = !_showSquadChatter;
            NotifyShow($"Squad chatter {(_showSquadChatter ? "enabled" : "disabled")}.");
        }

        internal bool CanLocalPlayerManageNpcs()
        {
            var player = LocalPlayer();
            return player != null && CanManageNpcs(player.Id.SteamId);
        }

        internal string AdminNpcCountText() =>
            Npcs == null ? "Custom NPC system is not available." : $"Custom NPCs alive: {Npcs.Npcs.Count}.";

        internal bool AdminUtilityDecisionMakingEnabled => _utilityDecisionMakingEnabled;
        internal bool AdminGameLogEnabled => SiGameLog.Enabled;

        internal void RequestAdminSpawn(string webbingSubtype, bool isParatrooper, bool isEnemy)
        {
            if (string.IsNullOrWhiteSpace(webbingSubtype))
                return;

            if (MyMultiplayerModApi.Static != null && !MyMultiplayerModApi.Static.IsServer)
            {
                MyMultiplayerModApi.Static.RaiseStaticEvent(
                    x => RequestAdminSpawnServer,
                    webbingSubtype,
                    isParatrooper,
                    isEnemy);
                return;
            }

            ExecuteAdminSpawn(LocalPlayer(), webbingSubtype, isParatrooper, isEnemy);
        }

        internal void RequestAdminSpawnSquad(string presetSubtype, bool isEnemy)
        {
            if (string.IsNullOrWhiteSpace(presetSubtype))
                return;

            if (MyMultiplayerModApi.Static != null && !MyMultiplayerModApi.Static.IsServer)
            {
                MyMultiplayerModApi.Static.RaiseStaticEvent(
                    x => RequestAdminSpawnSquadServer,
                    presetSubtype,
                    isEnemy);
                return;
            }

            ExecuteAdminSpawnSquad(LocalPlayer(), presetSubtype, isEnemy);
        }

        internal void RequestBaseCampSpawn(long baseCampEntityId, bool aiLed)
        {
            if (baseCampEntityId == 0)
                return;

            if (MyMultiplayerModApi.Static != null && !MyMultiplayerModApi.Static.IsServer)
            {
                MyMultiplayerModApi.Static.RaiseStaticEvent(
                    x => RequestBaseCampSpawnServer,
                    baseCampEntityId,
                    aiLed);
                return;
            }

            ExecuteBaseCampSpawn(LocalPlayer(), baseCampEntityId, aiLed);
        }

        internal void RequestBaseCampRefund(
            long baseCampEntityId,
            SiSquadLeaderKind leaderKind,
            long leaderId)
        {
            if (baseCampEntityId == 0 || leaderId == 0)
                return;

            if (MyMultiplayerModApi.Static != null && !MyMultiplayerModApi.Static.IsServer)
            {
                MyMultiplayerModApi.Static.RaiseStaticEvent(
                    x => RequestBaseCampRefundServer,
                    baseCampEntityId,
                    (byte)leaderKind,
                    leaderId);
                return;
            }

            ExecuteBaseCampRefund(LocalPlayer(), baseCampEntityId, (byte)leaderKind, leaderId);
        }

        internal void RequestAdminRearm()
        {
            if (MyMultiplayerModApi.Static != null && !MyMultiplayerModApi.Static.IsServer)
            {
                MyMultiplayerModApi.Static.RaiseStaticEvent(x => RequestAdminRearmServer);
                return;
            }

            ExecuteAdminRearm(LocalPlayer());
        }

        internal void RequestAdminClear()
        {
            if (MyMultiplayerModApi.Static != null && !MyMultiplayerModApi.Static.IsServer)
            {
                MyMultiplayerModApi.Static.RaiseStaticEvent(x => RequestAdminClearServer);
                return;
            }

            ExecuteAdminClear(LocalPlayer());
        }

        internal void RequestAdminSetUtilityDecisionMaking(bool enabled)
        {
            if (MyMultiplayerModApi.Static != null && !MyMultiplayerModApi.Static.IsServer)
            {
                MyMultiplayerModApi.Static.RaiseStaticEvent(
                    x => RequestAdminSetUtilityDecisionMakingServer,
                    enabled);
                return;
            }

            ExecuteAdminSetUtilityDecisionMaking(LocalPlayer(), enabled);
        }

        internal void RequestAdminSetGameLog(bool enabled)
        {
            if (MyMultiplayerModApi.Static != null && !MyMultiplayerModApi.Static.IsServer)
            {
                MyMultiplayerModApi.Static.RaiseStaticEvent(
                    x => RequestAdminSetGameLogServer,
                    enabled);
                return;
            }

            ExecuteAdminSetGameLog(LocalPlayer(), enabled);
        }

        private void ExecuteAdminSpawn(MyPlayer player, string webbingSubtype, bool isParatrooper, bool isEnemy)
        {
            if (player == null
                || string.IsNullOrWhiteSpace(webbingSubtype)
                || webbingSubtype.Length > 128
                || !CanManageNpcs(player.Id.SteamId))
                return;

            SpawnAdminNpc(player.Id.SteamId, new SiNpcSpawnRequest(webbingSubtype, isParatrooper, isEnemy));
        }

        private void ExecuteAdminSpawnSquad(MyPlayer player, string presetSubtype, bool isEnemy)
        {
            if (player == null
                || string.IsNullOrWhiteSpace(presetSubtype)
                || presetSubtype.Length > 128
                || !CanManageNpcs(player.Id.SteamId))
                return;

            SpawnAdminSquad(player.Id.SteamId, presetSubtype, isEnemy);
        }

        private void ExecuteBaseCampSpawn(MyPlayer player, long baseCampEntityId, bool aiLed)
        {
            if (player?.Identity == null || baseCampEntityId == 0)
                return;

            if (!TryGetBaseCampInventories(
                    baseCampEntityId,
                    out var recruits,
                    out var webbings))
            {
                Respond(player.Id.SteamId, "The base camp inventories are not available.");
                return;
            }

            var webbingReservations = BuildBaseCampWebbingReservations(webbings, recruits);
            var memberCount = 0;
            for (var i = 0; i < webbingReservations.Count; i++)
                memberCount += webbingReservations[i].Amount;

            if (memberCount <= 0)
            {
                Respond(player.Id.SteamId, "Store at least one recruit and compatible webbing in the base camp.");
                return;
            }

            var recruitId = new MyDefinitionId(
                typeof(MyObjectBuilder_InventoryItem),
                "DefenderRecruit");
            if (!recruits.RemoveItems(recruitId, memberCount))
            {
                Respond(player.Id.SteamId, "The recruit inventory changed before spawning could begin.");
                return;
            }

            var removedWebbing = new List<SiBaseCampWebbingReservation>();
            for (var i = 0; i < webbingReservations.Count; i++)
            {
                var reservation = webbingReservations[i];
                if (!webbings.RemoveItems(reservation.DefinitionId, reservation.Amount))
                {
                    RestoreBaseCampInventory(recruits, webbings, recruitId, memberCount, removedWebbing);
                    Respond(player.Id.SteamId, "The webbing inventory changed before spawning could begin.");
                    return;
                }

                removedWebbing.Add(reservation);
            }

            var playerPosition = player.ControlledEntity?.Get<MyPositionComponentBase>();
            if (playerPosition == null)
            {
                RestoreBaseCampInventory(recruits, webbings, recruitId, memberCount, removedWebbing);
                Respond(player.Id.SteamId, "You must control a character to spawn a squad.");
                return;
            }

            var pendingBroadcasts = new List<SiPendingNpcSpawn>();
            var spawnedEntityIds = new List<long>();
            var squadContext = default(SiIndependentSquadSpawnContext);
            var memberIndex = 0;
            string failure;
            for (var reservationIndex = 0; reservationIndex < removedWebbing.Count; reservationIndex++)
            {
                var reservation = removedWebbing[reservationIndex];
                for (var count = 0; count < reservation.Amount; count++)
                {
                    var request = new SiNpcSpawnRequest(
                        reservation.WebbingSubtype,
                        false,
                        false);
                    SiNpc npc;
                    var spawned = aiLed
                        ? TrySpawnIndependentAiSquadMember(
                            player,
                            CreateSpawnTransform(playerPosition.WorldMatrix, memberIndex),
                            request,
                            ref squadContext,
                            out npc,
                            out failure)
                        : TrySpawnConfiguredNpc(
                            player,
                            CreateSpawnTransform(playerPosition.WorldMatrix, memberIndex),
                            request,
                            out npc,
                            out failure);
                    if (!spawned)
                    {
                        CloseSpawnedNpcs(spawnedEntityIds);
                        RestoreBaseCampInventory(recruits, webbings, recruitId, memberCount, removedWebbing);
                        Respond(player.Id.SteamId, failure ?? "Failed to spawn the base camp squad.");
                        return;
                    }

                    spawnedEntityIds.Add(npc.EntityId);
                    pendingBroadcasts.Add(new SiPendingNpcSpawn(npc, request));
                    memberIndex++;
                }
            }

            for (var i = 0; i < pendingBroadcasts.Count; i++)
                BroadcastSpawn(pendingBroadcasts[i].Npc, pendingBroadcasts[i].Request);

            Respond(
                player.Id.SteamId,
                $"Spawned {(aiLed ? "AI-led" : "player-led")} squad with {memberCount} trooper(s).");
        }

        private static bool TryGetBaseCampInventories(
            long baseCampEntityId,
            out MyInventory recruits,
            out MyInventory webbings)
        {
            recruits = null;
            webbings = null;

            var entity = MyEntities.GetEntityByIdOrDefault(baseCampEntityId);
            if (entity?.Components.Get<SiBaseCampComponent>() == null)
                return false;

            recruits = entity.Components.Get<MyInventoryBase>(
                MyStringHash.GetOrCompute("SiBaseCampRecruits")) as MyInventory;
            webbings = entity.Components.Get<MyInventoryBase>(
                MyStringHash.GetOrCompute("SiBaseCampWebbings")) as MyInventory;
            return recruits != null && webbings != null;
        }

        private static List<SiBaseCampWebbingReservation> BuildBaseCampWebbingReservations(
            MyInventory webbings,
            MyInventory recruits)
        {
            var reservations = new List<SiBaseCampWebbingReservation>();
            if (webbings == null || recruits == null)
                return reservations;

            var recruitId = new MyDefinitionId(
                typeof(MyObjectBuilder_InventoryItem),
                "DefenderRecruit");
            var remaining = Math.Max(0, recruits.GetItemAmount(recruitId));
            for (var i = 0; i < webbings.Items.Count && remaining > 0; i++)
            {
                var item = webbings.Items.ItemAt(i);
                if (!SiNpcTrooperCatalog.TryResolveLoadout(
                        item.DefinitionId.SubtypeName,
                        false,
                        out var resolvedWebbingSubtype,
                        out _))
                    continue;

                var amount = Math.Max(0, item.Amount);
                var reservedAmount = Math.Min(amount, remaining);
                if (reservedAmount <= 0)
                    continue;

                reservations.Add(new SiBaseCampWebbingReservation(
                    item.DefinitionId,
                    resolvedWebbingSubtype,
                    reservedAmount));
                remaining -= reservedAmount;
            }

            return reservations;
        }

        private void ExecuteBaseCampRefund(
            MyPlayer player,
            long baseCampEntityId,
            byte leaderKind,
            long leaderId)
        {
            if (player?.Identity == null
                || baseCampEntityId == 0
                || !Enum.IsDefined(typeof(SiSquadLeaderKind), (int)leaderKind)
                || leaderId == 0)
                return;

            var baseCamp = MyEntities.GetEntityByIdOrDefault(baseCampEntityId);
            var baseCampComponent = baseCamp?.Components.Get<SiBaseCampComponent>();
            if (baseCampComponent == null || Squads == null || Npcs == null)
            {
                Respond(player.Id.SteamId, "The base camp or squad system is not available.");
                return;
            }

            var nearbySquads = Squads.CreateNearbyAlliedSquads(
                Npcs,
                player.Identity.Id,
                baseCamp.WorldMatrix.Translation,
                baseCampComponent.NearbySquadRadius);
            SiSquadView selectedSquad = null;
            for (var i = 0; i < nearbySquads.Count; i++)
            {
                if (nearbySquads[i].Leader.Kind != (SiSquadLeaderKind)leaderKind
                    || nearbySquads[i].Leader.Id != leaderId)
                    continue;

                selectedSquad = nearbySquads[i];
                break;
            }

            if (selectedSquad == null)
            {
                Respond(player.Id.SteamId, "The selected allied squad is no longer nearby.");
                return;
            }

            var npcIds = new List<long>();
            for (var i = 0; i < selectedSquad.Members.Count; i++)
            {
                var member = selectedSquad.Members[i];
                if (member.Kind == SiSquadMemberKind.Npc && member.Id != 0)
                    npcIds.Add(member.Id);
            }

            if (npcIds.Count == 0)
            {
                Respond(player.Id.SteamId, "The selected squad has no NPC members to refund.");
                return;
            }

            var refundedCount = 0;
            var refundedNpcIds = new List<long>();
            for (var i = 0; i < npcIds.Count; i++)
            {
                SiNpc npc;
                if (!Npcs.Npcs.TryGetValue(npcIds[i], out npc)
                    || npc?.Entity == null
                    || npc.Entity.Closed
                    || npc.Entity.MarkedForClose)
                    continue;

                if (!DropNpcInventoryToSack(npc))
                    continue;

                if (Npcs.Close(npc.EntityId))
                {
                    refundedCount++;
                    refundedNpcIds.Add(npc.EntityId);
                }
            }

            if (refundedNpcIds.Count > 0)
                BroadcastNpcClose(refundedNpcIds);

            Respond(
                player.Id.SteamId,
                refundedCount == npcIds.Count
                    ? $"Refunded squad with {refundedCount} trooper(s)."
                    : $"Refunded {refundedCount} of {npcIds.Count} trooper(s); unavailable members were left untouched.");
        }

        private static bool DropNpcInventoryToSack(SiNpc npc)
        {
            var entity = npc?.Entity;
            if (entity == null || entity.Closed || entity.MarkedForClose)
                return false;

            var inventory = SiNpcEquipmentHelper.FindInventory(entity, out _) as MyInventory;
            if (inventory == null)
                return false;

            var recruitId = new MyDefinitionId(
                typeof(MyObjectBuilder_InventoryItem),
                "DefenderRecruit");
            if (inventory.GetItemAmount(recruitId) <= 0
                && !inventory.AddItems(
                    recruitId,
                    1,
                    MyInventoryBase.NewItemParams.ForcedInsertion))
                return false;

            var items = new List<MyInventoryItem>();
            foreach (var item in inventory.Items)
                if (item != null)
                    items.Add(item.Clone());

            if (items.Count == 0)
                return false;

            var characterBagId = new MyDefinitionId(
                typeof(MyObjectBuilder_InventoryBagEntity),
                "CharacterBag");
            var bag = InventoryDropper.DropItemsInBag(
                new ListReader<MyInventoryItem>(items),
                entity.WorldMatrix.Translation,
                characterBagId,
                entity.Get<MyPhysicsComponentBase>());
            return bag != null;
        }

        private static void RestoreBaseCampInventory(
            MyInventory recruits,
            MyInventory webbings,
            MyDefinitionId recruitId,
            int recruitAmount,
            List<SiBaseCampWebbingReservation> removedWebbing)
        {
            if (recruits != null && recruitAmount > 0)
                recruits.AddItems(recruitId, recruitAmount, MyInventoryBase.NewItemParams.ForcedInsertion);

            if (webbings == null || removedWebbing == null)
                return;

            for (var i = 0; i < removedWebbing.Count; i++)
            {
                var reservation = removedWebbing[i];
                webbings.AddItems(
                    reservation.DefinitionId,
                    reservation.Amount,
                    MyInventoryBase.NewItemParams.ForcedInsertion);
            }
        }

        private sealed class SiBaseCampWebbingReservation
        {
            public SiBaseCampWebbingReservation(
                MyDefinitionId definitionId,
                string webbingSubtype,
                int amount)
            {
                DefinitionId = definitionId;
                WebbingSubtype = webbingSubtype;
                Amount = amount;
            }

            public MyDefinitionId DefinitionId { get; }
            public string WebbingSubtype { get; }
            public int Amount { get; }
        }

        private void ExecuteAdminRearm(MyPlayer player)
        {
            if (player?.Identity == null || !CanManageNpcs(player.Id.SteamId))
                return;

            RearmSquad(player.Id.SteamId, player.Identity.Id);
            Respond(player.Id.SteamId, "Rearm order issued.");
        }

        private void ExecuteAdminClear(MyPlayer player)
        {
            if (player == null || !CanManageNpcs(player.Id.SteamId) || Npcs == null)
                return;

            var removed = Npcs.Npcs.Count;
            Npcs.CloseAll();
            _squadOrders.Clear();
            _squadCombatStates.Clear();
            _playerLeaderStates.Clear();
            Squads?.ClearNpcs();
            BroadcastClear();
            Respond(player.Id.SteamId, $"Removed {removed} custom NPC(s).");
        }

        private void ExecuteAdminSetUtilityDecisionMaking(MyPlayer player, bool enabled)
        {
            if (player == null || !CanManageNpcs(player.Id.SteamId))
                return;

            _utilityDecisionMakingEnabled = enabled;
            Respond(player.Id.SteamId, UtilityAiDecisionMakingStatusText());
        }

        private void ExecuteAdminSetGameLog(MyPlayer player, bool enabled)
        {
            if (player == null || !CanManageNpcs(player.Id.SteamId))
                return;

            SiGameLog.SetEnabled(enabled);
            Respond(player.Id.SteamId, SiGameLog.StatusText());
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

        private void BroadcastPlayerHouseCommand(MyPlayer player, string message)
        {
            if (string.IsNullOrWhiteSpace(message) || player == null)
                return;

            var chat = _chat ?? MyChatSystem.Static;
            var sender = player.Id.SteamId;
            if (chat == null || sender == 0)
                return;

            chat.BroadcastMessage(HouseChannel, sender, message);
        }

        private void SpeakAiMapMoveOrder(MyPlayer player, SiSquadLeaderKey leader, in Vector3D target)
        {
            if (player == null)
                return;

            var squadCallsign = Squads?.GetSquadCallsign(Npcs, leader) ?? "Squad";
            string grid;
            var message = TryFormatMapCommandGrid(target, out grid)
                ? $"{squadCallsign}, move to grid {grid}."
                : $"{squadCallsign}, move to target.";
            BroadcastPlayerHouseCommand(player, message);
        }

        private static bool TryFormatMapCommandGrid(in Vector3D target, out string grid)
        {
            grid = null;

            var planet = MyGamePruningStructureSandbox.GetClosestPlanet(target);
            var areas = planet?.Components.Get<MyPlanetAreasComponent>();
            if (areas == null)
                return false;

            long areaId;
            try
            {
                var localTarget = Vector3D.Transform(in target, in areas.Entity.PositionComp.WorldMatrixInvScaledRef);
                areaId = areas.GetArea((Vector3)localTarget);
            }
            catch
            {
                return false;
            }

            string kingdom;
            string region;
            string area;
            try
            {
                areas.UnpackAreaIdToNames(areaId, out kingdom, out region, out area);
            }
            catch
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(region) || string.IsNullOrWhiteSpace(area))
                return false;

            grid = string.IsNullOrWhiteSpace(kingdom)
                ? $"{region}, {area}"
                : $"{kingdom}, {region}, {area}";
            return true;
        }

        private static string UtilityCommandSpeech(SiUtilityCommandMenuCommand command)
        {
            switch (command)
            {
                case SiUtilityCommandMenuCommand.Stop:
                    return "Halt";
                case SiUtilityCommandMenuCommand.Follow:
                    return "Follow me";
                case SiUtilityCommandMenuCommand.Rearm:
                    return "Rearm";
                case SiUtilityCommandMenuCommand.FormationColumn:
                    return "Form column";
                case SiUtilityCommandMenuCommand.FormationFile:
                    return "Form file";
                case SiUtilityCommandMenuCommand.FormationLine:
                    return "Form line";
                case SiUtilityCommandMenuCommand.FormationVee:
                    return "Form vee";
                case SiUtilityCommandMenuCommand.FormationLongBox:
                    return "Form long box";
                case SiUtilityCommandMenuCommand.FormationWideBox:
                    return "Form wide box";
                case SiUtilityCommandMenuCommand.FormationSquare:
                    return "Form square";
                case SiUtilityCommandMenuCommand.FormationStaggeredColumn:
                    return "Form staggered column";
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
                case SiUtilityCommandMenuCommand.Rearm:
                    RearmSquad(sender, leaderIdentityId);
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
                case SiUtilityCommandMenuCommand.FormationLongBox:
                    SetFormation(sender, leaderIdentityId, SiSquadFormation.LongBox);
                    return;
                case SiUtilityCommandMenuCommand.FormationWideBox:
                    SetFormation(sender, leaderIdentityId, SiSquadFormation.WideBox);
                    return;
                case SiUtilityCommandMenuCommand.FormationSquare:
                    SetFormation(sender, leaderIdentityId, SiSquadFormation.Square);
                    return;
                case SiUtilityCommandMenuCommand.FormationStaggeredColumn:
                    SetFormation(sender, leaderIdentityId, SiSquadFormation.StaggeredColumn);
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
            var rearmText = state.RearmOverride ? ", rearm active" : string.Empty;
            lines.Add($"Order: {OrderName(state.Mode)}{rearmText}, formation {FormationName(state.Formation)}, engagement {EngagementName(state.EngagementStance)}, combat {CombatStanceName(GetCombatStance(PlayerLeaderKey(leaderIdentityId)))}, transport {TransportModeName(state.TransportMode)}.");
            RespondLines(sender, lines);
        }

        private void StopSquad(ulong sender, long leaderIdentityId)
        {
            var state = GetSquadOrder(leaderIdentityId);
            state.Mode = SiSquadOrderMode.Stopped;
            SetRearmOverride(state, false);
            CancelTransportOverride(leaderIdentityId, state);
            ClearLeaderWaypoints(leaderIdentityId);
        }

        private void FollowSquad(ulong sender, long leaderIdentityId)
        {
            var state = GetSquadOrder(leaderIdentityId);
            state.Mode = SiSquadOrderMode.Follow;
            SetRearmOverride(state, false);
            CancelTransportOverride(leaderIdentityId, state);

            string failure;
            ApplyFollowOrder(leaderIdentityId, state, true, out failure);
        }

        private void SetFormation(ulong sender, long leaderIdentityId, SiSquadFormation formation)
        {
            var state = GetSquadOrder(leaderIdentityId);
            state.Formation = formation;
            state.Mode = SiSquadOrderMode.Follow;
            SetRearmOverride(state, false);
            CancelTransportOverride(leaderIdentityId, state);

            string failure;
            ApplyFollowOrder(leaderIdentityId, state, true, out failure);
        }

        private void RearmSquad(ulong sender, long leaderIdentityId)
        {
            var state = GetSquadOrder(leaderIdentityId);
            SetRearmOverride(state, true);
            CancelTransportOverride(leaderIdentityId, state);
            ClearLeaderWaypoints(leaderIdentityId);
        }

        private void SetEngagementStance(long leaderIdentityId, SiSquadEngagementStance engagementStance)
        {
            var state = GetSquadOrder(leaderIdentityId);
            state.EngagementStance = engagementStance;
        }

        private bool SpawnAdminNpc(ulong sender, SiNpcSpawnRequest request)
        {
            if (!SiNpcTrooperCatalog.TryResolveLoadout(request.WebbingSubtype, request.IsParatrooper, out _, out _))
                return Respond(sender, $"Unknown trooper webbing '{request.WebbingSubtype}'.");

            var player = MyPlayers.Static.GetPlayer(new MyPlayer.PlayerId(sender, 0));
            var playerPosition = player?.ControlledEntity?.Get<MyPositionComponentBase>();
            if (playerPosition == null)
                return Respond(sender, "You must control a character to spawn an NPC.");

            if (!TrySpawnConfiguredNpc(
                    player,
                    CreateSpawnTransform(playerPosition.WorldMatrix),
                    request,
                    out var npc,
                    out var failure))
                return Respond(sender, failure ?? $"Failed to configure custom NPC '{request.DisplayArchetype}'.");

            BroadcastSpawn(npc, request);
            return Respond(sender, $"Spawned {request.DisplayArchetype} ({npc.EntityId}).");
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
            var weaponSet = npc.Entity.Components.Get<SiNpcWeaponSetComponent>();
            if (loadoutComponent == null || uniform == null || weaponSet == null)
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

            if (!weaponSet.ApplyRuntimeDefinition(loadout.WeaponBindings, loadout.PrimaryWeaponItemId))
            {
                failure = $"Weapon slot bindings for '{resolvedWebbingSubtype}' could not be applied.";
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

        private bool TrySpawnConfiguredNpc(
            MyPlayer player,
            in MatrixD transform,
            SiNpcSpawnRequest request,
            out SiNpc npc,
            out string failure)
        {
            npc = null;
            failure = null;

            var entityId = MyEntityIdentifier.AllocateId();
            if (!Npcs.TrySpawnConfigured(
                    SiNpcManager.SoldierArchetype,
                    request.DisplayArchetype,
                    entityId,
                    transform,
                    out npc))
            {
                failure = $"Failed to spawn custom NPC '{request.DisplayArchetype}'; its model or entity definition could not be loaded.";
                return false;
            }

            if (ApplySpawnRequest(npc, request, out failure)
                && ConfigureSpawnedNpc(request, npc, player, out failure))
                return true;

            Npcs.Close(entityId);
            npc = null;
            return false;
        }

        private bool SpawnAdminSquad(ulong sender, string presetSubtype, bool isEnemy)
        {
            if (!SiNpcSquadPresetCatalog.TryResolvePreset(
                    presetSubtype,
                    out var resolvedPresetSubtype,
                    out _,
                    out var members,
                    out var failure))
                return Respond(sender, failure ?? $"Unknown squad preset '{presetSubtype}'.");

            var player = MyPlayers.Static.GetPlayer(new MyPlayer.PlayerId(sender, 0));
            var playerPosition = player?.ControlledEntity?.Get<MyPositionComponentBase>();
            if (playerPosition == null)
                return Respond(sender, "You must control a character to spawn an AI squad.");

            var pendingBroadcasts = new List<SiPendingNpcSpawn>();
            var spawnedEntityIds = new List<long>();
            var squadContext = default(SiIndependentSquadSpawnContext);
            for (var i = 0; i < members.Count; i++)
            {
                var request = new SiNpcSpawnRequest(
                    members[i].WebbingSubtype,
                    members[i].IsParatrooper,
                    isEnemy);
                if (!TrySpawnIndependentAiSquadMember(
                        player,
                        CreateSpawnTransform(playerPosition.WorldMatrix, i),
                        request,
                        ref squadContext,
                        out var npc,
                        out failure))
                {
                    CloseSpawnedNpcs(spawnedEntityIds);
                    return Respond(sender, failure ?? $"Failed to spawn squad preset '{resolvedPresetSubtype}'.");
                }

                spawnedEntityIds.Add(npc.EntityId);
                pendingBroadcasts.Add(new SiPendingNpcSpawn(npc, request));
            }

            for (var i = 0; i < pendingBroadcasts.Count; i++)
                BroadcastSpawn(pendingBroadcasts[i].Npc, pendingBroadcasts[i].Request);

            return Respond(
                sender,
                $"Spawned squad preset '{resolvedPresetSubtype}' with {pendingBroadcasts.Count} {(isEnemy ? "enemy" : "allied")} AI trooper(s).");
        }

        private bool TrySpawnIndependentAiSquadMember(
            MyPlayer player,
            in MatrixD transform,
            SiNpcSpawnRequest request,
            ref SiIndependentSquadSpawnContext squadContext,
            out SiNpc npc,
            out string failure)
        {
            npc = null;
            failure = null;

            var entityId = MyEntityIdentifier.AllocateId();
            if (!Npcs.TrySpawnConfigured(
                    SiNpcManager.SoldierArchetype,
                    request.DisplayArchetype,
                    entityId,
                    transform,
                    out npc))
            {
                failure = $"Failed to spawn custom NPC '{request.DisplayArchetype}'; its model or entity definition could not be loaded.";
                return false;
            }

            if (!ApplySpawnRequest(npc, request, out failure)
                || !ConfigureIndependentAiSquadMember(request, npc, player, ref squadContext, out failure))
            {
                Npcs.Close(entityId);
                npc = null;
                return false;
            }

            return true;
        }

        private bool ConfigureIndependentAiSquadMember(
            SiNpcSpawnRequest request,
            SiNpc npc,
            MyPlayer player,
            ref SiIndependentSquadSpawnContext squadContext,
            out string failure)
        {
            failure = null;

            if (request.IsEnemy)
            {
                if (!TryPrepareEnemyTrooper(npc, player, out var enemyFaction, out failure))
                    return false;

                var enemyArmy = new SiArmyKey(SiArmyKind.Faction, enemyFaction.FactionId);
                return TryAssignIndependentAiSquadMember(
                    npc,
                    request,
                    enemyArmy,
                    EnemyTrooperName(npc),
                    ref squadContext,
                    out failure);
            }

            if (!ConfigureFriendlyTrooper(npc, player, out failure))
                return false;

            return TryAssignIndependentAiSquadMember(
                npc,
                request,
                SiSquadBook.ArmyForPlayerIdentity(player.Identity.Id),
                FriendlyTrooperName(npc),
                ref squadContext,
                out failure);
        }

        private bool TryAssignIndependentAiSquadMember(
            SiNpc npc,
            SiNpcSpawnRequest request,
            SiArmyKey army,
            string defaultLeaderName,
            ref SiIndependentSquadSpawnContext squadContext,
            out string failure)
        {
            failure = null;
            if (npc == null)
            {
                failure = $"The spawned NPC for '{request.DisplayArchetype}' is missing.";
                return false;
            }

            var squads = Squads;
            if (squads == null)
            {
                failure = "The squad system is not available.";
                return false;
            }

            if (!squadContext.HasLeader)
            {
                var leaderName = string.IsNullOrWhiteSpace(defaultLeaderName) ? request.DisplayArchetype : defaultLeaderName;
                squads.AssignNpcAsAiLeader(npc, leaderName, army.Kind, army.Id);
                squadContext = new SiIndependentSquadSpawnContext
                {
                    HasLeader = true,
                    Leader = new SiSquadLeaderKey(SiSquadLeaderKind.Ai, npc.EntityId, army),
                    LeaderName = leaderName,
                };
                return true;
            }

            squads.AssignNpcToLeader(
                npc,
                squadContext.Leader.Kind,
                squadContext.Leader.Id,
                squadContext.Leader.Army.Kind,
                squadContext.Leader.Army.Id,
                squadContext.LeaderName,
                false);
            return true;
        }

        private static void CloseSpawnedNpcs(List<long> entityIds)
        {
            if (_instance?.Npcs == null || entityIds == null)
                return;

            for (var i = 0; i < entityIds.Count; i++)
                _instance.Npcs.Close(entityIds[i]);
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
            if (!TryPrepareEnemyTrooper(npc, player, out var enemyFaction, out failure))
                return false;

            AssignNpcToEnemySquad(npc, enemyFaction);
            return true;
        }

        private bool TryPrepareEnemyTrooper(
            SiNpc npc,
            MyPlayer player,
            out MyFaction enemyFaction,
            out string failure)
        {
            failure = null;
            enemyFaction = null;
            if (npc == null || player?.Identity == null)
            {
                failure = "You must control a character to spawn an enemy NPC.";
                return false;
            }

            if (npc.DiplomaticIdentityId == 0)
            {
                var identity = MyIdentities.Static?.CreateIdentity(EnemyTrooperName(npc));
                if (identity == null)
                {
                    failure = "Failed to create a diplomatic identity for the enemy NPC.";
                    return false;
                }

                SetNpcDiplomaticIdentity(npc, identity);
            }

            if (!TryAssignIdentityToEnemyFaction(npc.DiplomaticIdentityId, out enemyFaction, out failure))
                return false;

            return TryMarkHostileToCaller(player, enemyFaction, out failure);
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
            "Enemy AI";

        private static string FriendlyTrooperName(SiNpc npc) =>
            "AI";

        private string UtilityAiDecisionMakingStatusText() =>
            $"UtilityAI decision making {(_utilityDecisionMakingEnabled ? "enabled" : "disabled")}.";

        private bool Respond(ulong sender, string response)
        {
            _chat?.SendMessageToClient(sender, MyStringHash.GetOrCompute("System"), 0, response);
            return true;
        }

        private bool RespondLines(ulong sender, IReadOnlyList<string> lines)
        {
            if (lines == null || lines.Count == 0)
                return Respond(sender, string.Empty);

            return Respond(sender, string.Join("\n\n", lines));
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
