using System;
using System.Collections.Generic;
using System.Text;
using Medieval.GameSystems.Factions;
using Sandbox.Game.Players;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;
using VRageMath;

namespace Si.UtilityAI
{
    internal sealed class SiSquadBook
    {
        private static readonly MyDefinitionId DefaultDefinitionId =
            new MyDefinitionId(typeof(MyObjectBuilder_SiSquadSystemDefinition), "SiDefaultSquadSystem");

        private readonly Dictionary<long, SiAssignedNpc> _assignedNpcs =
            new Dictionary<long, SiAssignedNpc>();
        private readonly List<long> _staleNpcIds = new List<long>();

        public SiSquadBook()
        {
            Definition = LoadDefinition();
        }

        public SiSquadSystemDefinition Definition { get; }

        public void AssignNpcToPlayer(SiNpc npc, MyPlayer player)
        {
            if (npc == null || player?.Identity == null)
                return;

            AssignNpcToLeader(
                npc,
                CreatePlayerLeader(player.Identity.Id),
                PlayerName(player),
                false);
        }

        public void ClearNpcs()
        {
            _assignedNpcs.Clear();
            _staleNpcIds.Clear();
        }

        public void AssignNpcToPlayerIdentity(SiNpc npc, long identityId, string leaderName)
        {
            if (npc == null || identityId == 0)
                return;

            AssignNpcToLeader(
                npc,
                CreatePlayerLeader(identityId),
                string.IsNullOrWhiteSpace(leaderName) ? "Player " + identityId : leaderName,
                false);
        }

        public void AssignNpcAsAiLeader(SiNpc npc, string leaderName, long enemyArmyId)
        {
            if (npc == null)
                return;

            AssignNpcToLeader(
                npc,
                new SiSquadLeaderKey(
                    SiSquadLeaderKind.Ai,
                    npc.EntityId,
                    new SiArmyKey(SiArmyKind.Enemy, enemyArmyId)),
                string.IsNullOrWhiteSpace(leaderName) ? NpcName(npc, null) : leaderName,
                true);
        }

        public void AssignNpcToLeader(
            SiNpc npc,
            SiSquadLeaderKind leaderKind,
            long leaderId,
            SiArmyKind armyKind,
            long armyId,
            string leaderName,
            bool isLeader)
        {
            if (npc == null || leaderId == 0)
                return;

            AssignNpcToLeader(
                npc,
                new SiSquadLeaderKey(
                    leaderKind,
                    leaderId,
                    new SiArmyKey(armyKind, armyId)),
                leaderName,
                isLeader);
        }

        private void AssignNpcToLeader(
            SiNpc npc,
            SiSquadLeaderKey leader,
            string leaderName,
            bool isLeader)
        {
            _assignedNpcs[npc.EntityId] = new SiAssignedNpc(
                leader,
                string.IsNullOrWhiteSpace(leaderName) ? NpcName(npc, null) : leaderName,
                npc.Archetype,
                isLeader);
        }

        public bool TryGetAssignment(long npcId, out SiAssignedNpc assignment) =>
            _assignedNpcs.TryGetValue(npcId, out assignment);

        public bool TryFindNearbyAiSquadAssignment(
            SiNpcManager npcManager,
            in Vector3D position,
            double radius,
            SiArmyKey army,
            out SiAssignedNpc assignment)
        {
            assignment = null;
            if (npcManager == null || radius <= 0)
                return false;

            PurgeClosedNpcs(npcManager);

            var bestDistanceSquared = radius * radius;
            foreach (var entry in _assignedNpcs)
            {
                var candidateAssignment = entry.Value;
                if (candidateAssignment.Leader.Kind != SiSquadLeaderKind.Ai
                    || !candidateAssignment.Leader.Army.Equals(army))
                    continue;

                SiNpc npc;
                if (!npcManager.Npcs.TryGetValue(entry.Key, out npc)
                    || npc?.Entity == null
                    || npc.Entity.Closed
                    || npc.Entity.MarkedForClose)
                    continue;

                var distanceSquared = Vector3D.DistanceSquared(
                    npc.Entity.WorldMatrix.Translation,
                    position);
                if (distanceSquared > bestDistanceSquared)
                    continue;

                bestDistanceSquared = distanceSquared;
                assignment = candidateAssignment;
            }

            return assignment != null;
        }

        public List<SiSquadLeadershipChange> ReassignLeaderlessSquads(
            SiNpcManager npcManager,
            Func<long, bool> isPlayerLeaderActive)
        {
            var changes = new List<SiSquadLeadershipChange>();
            if (npcManager == null || _assignedNpcs.Count == 0)
                return changes;

            PurgeClosedNpcs(npcManager);

            var memberIdsByLeader = new Dictionary<SiSquadLeaderKey, List<long>>();
            foreach (var assigned in _assignedNpcs)
            {
                List<long> members;
                if (!memberIdsByLeader.TryGetValue(assigned.Value.Leader, out members))
                {
                    members = new List<long>();
                    memberIdsByLeader.Add(assigned.Value.Leader, members);
                }

                members.Add(assigned.Key);
            }

            foreach (var squad in memberIdsByLeader)
            {
                if (IsLeaderActive(npcManager, squad.Key, isPlayerLeaderActive))
                    continue;
                if (squad.Key.Kind == SiSquadLeaderKind.Player)
                    continue;

                SiNpc replacementNpc;
                SiAssignedNpc replacementAssignment;
                if (!TryFindReplacementLeader(npcManager, squad.Value, out replacementNpc, out replacementAssignment))
                    continue;

                var replacementLeader = new SiSquadLeaderKey(
                    SiSquadLeaderKind.Ai,
                    replacementNpc.EntityId,
                    squad.Key.Army);
                var replacementLeaderName = NpcName(replacementNpc, replacementAssignment);
                changes.Add(new SiSquadLeadershipChange(
                    squad.Key,
                    replacementLeader,
                    replacementLeaderName));

                foreach (var memberId in squad.Value)
                {
                    SiAssignedNpc memberAssignment;
                    if (!_assignedNpcs.TryGetValue(memberId, out memberAssignment))
                        continue;

                    _assignedNpcs[memberId] = new SiAssignedNpc(
                        replacementLeader,
                        replacementLeaderName,
                        memberAssignment.Archetype,
                        memberId == replacementNpc.EntityId);
                }
            }

            return changes;
        }

        public static SiArmyKey ArmyForPlayerIdentity(long identityId) =>
            ArmyForIdentity(identityId);

        public static bool TryCreateDiplomaticParty(SiArmyKey army, out MyDiplomaticParty party)
        {
            party = default(MyDiplomaticParty);
            if (army.Kind == SiArmyKind.Player)
            {
                if (army.Id == 0)
                    return false;

                party = new MyDiplomaticParty(DiplomaticPartyType.Player, army.Id);
                return true;
            }

            if (army.Kind == SiArmyKind.Faction)
            {
                var faction = FactionById(army.Id);
                if (faction == null)
                    return false;

                party = new MyDiplomaticParty(faction);
                return true;
            }

            if (army.Kind == SiArmyKind.Enemy)
            {
                var faction = FactionByTag(SiNpcManager.EnemyFactionTag);
                if (faction == null)
                    return false;

                party = new MyDiplomaticParty(faction);
                return true;
            }

            return false;
        }

        public List<SiNpc> GetLeaderNpcs(SiNpcManager npcManager, long leaderIdentityId)
        {
            var result = new List<SiNpc>();
            if (npcManager == null || leaderIdentityId == 0)
                return result;

            PurgeClosedNpcs(npcManager);
            foreach (var entry in _assignedNpcs)
            {
                var assignment = entry.Value;
                if (!IsPlayerLeader(assignment.Leader, leaderIdentityId))
                    continue;

                SiNpc npc;
                if (npcManager.Npcs.TryGetValue(entry.Key, out npc))
                    result.Add(npc);
            }

            result.Sort(CompareNpcs);
            return result;
        }

        public bool HasLeaderNpcs(SiNpcManager npcManager, long leaderIdentityId)
        {
            if (npcManager == null || leaderIdentityId == 0)
                return false;

            PurgeClosedNpcs(npcManager);
            foreach (var entry in _assignedNpcs)
                if (IsPlayerLeader(entry.Value.Leader, leaderIdentityId))
                    return true;

            return false;
        }

        public List<string> CreateRosterLinesForLeader(SiNpcManager npcManager, long leaderIdentityId)
        {
            var lines = new List<string>();
            if (Definition == null)
            {
                lines.Add("Squad data definition is missing.");
                return lines;
            }

            PurgeClosedNpcs(npcManager);
            var squads = BuildSquads(npcManager);
            squads.Sort(CompareSquads);

            foreach (var squad in squads)
                if (IsPlayerLeader(squad.Leader, leaderIdentityId))
                {
                    squad.Members.Sort(CompareMembers);
                    lines.Add(FormatSquadLine(squad));
                    return lines;
                }

            lines.Add("No squad is available for your identity.");
            return lines;
        }

        public List<SiSquadNpcMarker> CreateNpcMarkers(SiNpcManager npcManager, long leaderIdentityId)
        {
            var markers = new List<SiSquadNpcMarker>();
            if (Definition == null || npcManager == null || leaderIdentityId == 0)
                return markers;

            PurgeClosedNpcs(npcManager);
            var squads = BuildSquads(npcManager);
            squads.Sort(CompareSquads);

            foreach (var squad in squads)
                if (IsPlayerLeader(squad.Leader, leaderIdentityId))
                {
                    squad.Members.Sort(CompareMembers);
                    foreach (var member in squad.Members)
                    {
                        if (member.Kind != SiSquadMemberKind.Npc)
                            continue;

                        SiNpc npc;
                        if (npcManager.Npcs.TryGetValue(member.Id, out npc))
                            markers.Add(new SiSquadNpcMarker(
                                npc,
                                FormatMarkerLabel(squad, member)));
                    }

                    return markers;
                }

            return markers;
        }

        public List<SiSquadMapMarker> CreateMapMarkers(
            SiNpcManager npcManager,
            long observerIdentityId,
            SiMarkerSystemDefinition markerSettings)
        {
            var markers = new List<SiSquadMapMarker>();
            if (Definition == null || observerIdentityId == 0)
                return markers;

            PurgeClosedNpcs(npcManager);
            var squads = BuildSquads(npcManager);
            squads.Sort(CompareSquads);

            var observerArmy = ArmyForPlayerIdentity(observerIdentityId);
            MyDiplomaticParty observerParty;
            var hasObserverParty = TryCreateDiplomaticParty(observerArmy, out observerParty);
            foreach (var squad in squads)
            {
                if (!HasNpcMembers(squad))
                    continue;
                if (!ShouldShowMarkerToObserver(
                        markerSettings,
                        observerArmy,
                        hasObserverParty,
                        observerParty,
                        squad.Leader.Army))
                    continue;

                Vector3D position;
                if (!TryGetLeaderPosition(npcManager, squad.Leader, out position))
                    continue;

                markers.Add(new SiSquadMapMarker(
                    squad.Leader,
                    position,
                    FormatMapMarkerName(squad),
                    FormatMapMarkerDescription(squad),
                    false,
                    squad.Leader.Kind == SiSquadLeaderKind.Player ? "player" : "ally"));
            }

            return markers;
        }

        public List<string> CreateRosterLines(SiNpcManager npcManager)
        {
            var lines = new List<string>();
            if (Definition == null)
            {
                lines.Add("Squad data definition is missing.");
                return lines;
            }

            PurgeClosedNpcs(npcManager);
            var squads = BuildSquads(npcManager);
            if (squads.Count == 0)
            {
                lines.Add("No squads are currently active.");
                return lines;
            }

            squads.Sort(CompareSquads);

            var memberCount = 0;
            foreach (var squad in squads)
                memberCount += squad.Members.Count;
            lines.Add($"Squads: {squads.Count} active, {memberCount} member(s).");

            foreach (var squad in squads)
            {
                squad.Members.Sort(CompareMembers);
                lines.Add(FormatSquadLine(squad));
            }

            return lines;
        }

        private static SiSquadSystemDefinition LoadDefinition()
        {
            SiSquadSystemDefinition definition;
            if (MyDefinitionManager.TryGet(DefaultDefinitionId, out definition))
                return definition;

            foreach (var candidate in MyDefinitionManager.GetOfType<SiSquadSystemDefinition>())
                return candidate;
            return null;
        }

        private List<SiSquadView> BuildSquads(SiNpcManager npcManager)
        {
            var byLeader = new Dictionary<SiSquadLeaderKey, SiSquadView>();
            var onlinePlayers = OnlinePlayers();
            onlinePlayers.Sort(ComparePlayers);

            foreach (var player in onlinePlayers)
            {
                if (player.Identity == null)
                    continue;

                var leader = CreatePlayerLeader(player.Identity.Id);
                var squad = GetSquad(byLeader, leader, PlayerName(player));
                squad.Members.Add(new SiSquadMemberView(
                    SiSquadMemberKind.Player,
                    player.Identity.Id,
                    PlayerName(player),
                    Definition.PlayerRank,
                    true));
            }

            if (npcManager != null)
                foreach (var npc in npcManager.Npcs.Values)
                {
                    SiAssignedNpc assignment;
                    if (!_assignedNpcs.TryGetValue(npc.EntityId, out assignment))
                        continue;

                    var squad = GetSquad(byLeader, assignment.Leader, assignment.LeaderName);
                    squad.Members.Add(new SiSquadMemberView(
                        SiSquadMemberKind.Npc,
                        npc.EntityId,
                        NpcName(npc, assignment),
                        assignment.IsLeader ? Definition.PlayerRank : Definition.NpcRank,
                        true));
                }

            var squads = new List<SiSquadView>(byLeader.Values);
            AssignLetters(squads);
            AssignMemberCallsigns(squads);
            return squads;
        }

        private SiSquadView GetSquad(
            Dictionary<SiSquadLeaderKey, SiSquadView> squads,
            SiSquadLeaderKey leader,
            string leaderName)
        {
            SiSquadView squad;
            if (squads.TryGetValue(leader, out squad))
                return squad;

            squad = new SiSquadView(
                leader,
                ArmyName(leader.Army, leaderName),
                leaderName);
            squads.Add(leader, squad);
            return squad;
        }

        private void AssignLetters(List<SiSquadView> squads)
        {
            squads.Sort(CompareSquadsForLetterAssignment);

            var hasArmy = false;
            var army = default(SiArmyKey);
            var index = 0;
            foreach (var squad in squads)
            {
                if (!hasArmy || !squad.Leader.Army.Equals(army))
                {
                    hasArmy = true;
                    army = squad.Leader.Army;
                    index = 0;
                }

                squad.SetLetter(index, Definition.GetLetter(index));
                index++;
            }
        }

        private static void AssignMemberCallsigns(List<SiSquadView> squads)
        {
            foreach (var squad in squads)
            {
                squad.Members.Sort(CompareMembers);
                var squadCallsign = SquadCallsign(squad);
                for (var i = 0; i < squad.Members.Count; i++)
                    squad.Members[i].SetCallsign(squadCallsign + " " + (i + 1));
            }
        }

        private void PurgeClosedNpcs(SiNpcManager npcManager)
        {
            _staleNpcIds.Clear();
            foreach (var assigned in _assignedNpcs)
            {
                SiNpc npc;
                if (npcManager == null
                    || !npcManager.Npcs.TryGetValue(assigned.Key, out npc)
                    || !IsNpcAvailable(npc))
                    _staleNpcIds.Add(assigned.Key);
            }

            foreach (var id in _staleNpcIds)
                _assignedNpcs.Remove(id);
            _staleNpcIds.Clear();
        }

        private static bool IsLeaderActive(
            SiNpcManager npcManager,
            SiSquadLeaderKey leader,
            Func<long, bool> isPlayerLeaderActive)
        {
            if (leader.Kind == SiSquadLeaderKind.Player)
                return leader.Id != 0 && (isPlayerLeaderActive?.Invoke(leader.Id) ?? false);

            SiNpc npc;
            return TryGetNpc(npcManager, leader.Id, out npc);
        }

        private bool TryFindReplacementLeader(
            SiNpcManager npcManager,
            List<long> memberIds,
            out SiNpc npc,
            out SiAssignedNpc assignment)
        {
            npc = null;
            assignment = null;
            if (npcManager == null || memberIds == null || memberIds.Count == 0)
                return false;

            memberIds.Sort();
            for (var i = 0; i < memberIds.Count; i++)
            {
                if (!TryGetNpc(npcManager, memberIds[i], out npc))
                    continue;

                if (!_assignedNpcs.TryGetValue(memberIds[i], out assignment))
                    continue;

                return true;
            }

            npc = null;
            assignment = null;
            return false;
        }

        private static bool TryGetNpc(SiNpcManager npcManager, long entityId, out SiNpc npc)
        {
            npc = null;
            return npcManager != null
                   && npcManager.Npcs.TryGetValue(entityId, out npc)
                   && IsNpcAvailable(npc);
        }

        private static bool IsNpcAvailable(SiNpc npc) =>
            npc?.Entity != null
            && !npc.Entity.Closed
            && !npc.Entity.MarkedForClose
            && !npc.IsDead;

        private static List<MyPlayer> OnlinePlayers()
        {
            var result = new List<MyPlayer>();
            if (MyPlayers.Static == null)
                return result;

            foreach (var player in MyPlayers.Static.GetAllPlayers())
                if (player.Value?.Identity != null)
                    result.Add(player.Value);
            return result;
        }

        private static int ComparePlayers(MyPlayer left, MyPlayer right)
        {
            var leftName = PlayerName(left);
            var rightName = PlayerName(right);
            var nameCompare = string.Compare(leftName, rightName, StringComparison.OrdinalIgnoreCase);
            if (nameCompare != 0)
                return nameCompare;

            var leftId = left?.Identity?.Id ?? 0;
            var rightId = right?.Identity?.Id ?? 0;
            return leftId.CompareTo(rightId);
        }

        private static int CompareSquads(SiSquadView left, SiSquadView right)
        {
            var army = string.Compare(left.ArmyName, right.ArmyName, StringComparison.OrdinalIgnoreCase);
            if (army != 0)
                return army;

            var letter = left.LetterIndex.CompareTo(right.LetterIndex);
            if (letter != 0)
                return letter;

            return string.Compare(left.LeaderName, right.LeaderName, StringComparison.OrdinalIgnoreCase);
        }

        private static int CompareSquadsForLetterAssignment(SiSquadView left, SiSquadView right)
        {
            var army = CompareArmy(left.Leader.Army, right.Leader.Army);
            if (army != 0)
                return army;

            var leaderKind = left.Leader.Kind.CompareTo(right.Leader.Kind);
            if (leaderKind != 0)
                return leaderKind;

            var leaderName = string.Compare(left.LeaderName, right.LeaderName, StringComparison.OrdinalIgnoreCase);
            if (leaderName != 0)
                return leaderName;

            return left.Leader.Id.CompareTo(right.Leader.Id);
        }

        private static int CompareArmy(SiArmyKey left, SiArmyKey right)
        {
            var kind = left.Kind.CompareTo(right.Kind);
            return kind != 0 ? kind : left.Id.CompareTo(right.Id);
        }

        private static int CompareMembers(SiSquadMemberView left, SiSquadMemberView right)
        {
            var kind = left.Kind.CompareTo(right.Kind);
            if (kind != 0)
                return kind;

            var rank = RankOrder(right.Rank).CompareTo(RankOrder(left.Rank));
            if (rank != 0)
                return rank;

            var name = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            return name != 0 ? name : left.Id.CompareTo(right.Id);
        }

        private static int CompareNpcs(SiNpc left, SiNpc right) =>
            left.EntityId.CompareTo(right.EntityId);

        private static bool IsPlayerLeader(SiSquadLeaderKey leader, long identityId) =>
            leader.Kind == SiSquadLeaderKind.Player && leader.Id == identityId;

        private static bool HasNpcMembers(SiSquadView squad)
        {
            if (squad?.Members == null)
                return false;

            for (var i = 0; i < squad.Members.Count; i++)
                if (squad.Members[i].Kind == SiSquadMemberKind.Npc)
                    return true;
            return false;
        }

        private static bool TryGetLeaderPosition(
            SiNpcManager npcManager,
            SiSquadLeaderKey leader,
            out Vector3D position)
        {
            position = Vector3D.Zero;
            if (leader.Kind == SiSquadLeaderKind.Player)
            {
                foreach (var player in OnlinePlayers())
                {
                    if (player?.Identity?.Id != leader.Id)
                        continue;

                    var playerPosition = player.ControlledEntity?.Get<MyPositionComponentBase>();
                    if (playerPosition == null)
                        continue;

                    position = playerPosition.WorldMatrix.Translation;
                    return true;
                }

                return false;
            }

            SiNpc npc;
            if (!TryGetNpc(npcManager, leader.Id, out npc))
                return false;

            position = npc.Entity.WorldMatrix.Translation;
            return true;
        }

        private static bool ShouldShareLocationOnMap(
            SiArmyKey observerArmy,
            bool hasObserverParty,
            MyDiplomaticParty observerParty,
            SiArmyKey squadArmy)
        {
            if (observerArmy.Equals(squadArmy))
                return true;

            if (!hasObserverParty)
                return false;

            MyDiplomaticParty squadParty;
            if (!TryCreateDiplomaticParty(squadArmy, out squadParty))
                return false;

            var diplomacy = MyDiplomacyManager.Instance;
            if (diplomacy == null)
                return false;

            try
            {
                var relationship = diplomacy.GetRelationshipBetweenParties(observerParty, squadParty);
                var statusDefinition = relationship.StatusDefinition;
                if (statusDefinition != null)
                    return statusDefinition.ShareLocationOnMap;

                var status = relationship.Status;
                return status == diplomacy.RelationshipSelf || status == diplomacy.RelationshipFaction;
            }
            catch
            {
                return false;
            }
        }

        private static bool ShouldShowMarkerToObserver(
            SiMarkerSystemDefinition markerSettings,
            SiArmyKey observerArmy,
            bool hasObserverParty,
            MyDiplomaticParty observerParty,
            SiArmyKey squadArmy)
        {
            if ((markerSettings?.SquadVisibility ?? SiSquadMapMarkerVisibility.AlliedOnly) == SiSquadMapMarkerVisibility.All)
                return true;

            return ShouldShareLocationOnMap(observerArmy, hasObserverParty, observerParty, squadArmy);
        }

        private static int RankOrder(SiRankDefinition rank) => rank?.Order ?? 0;

        private string FormatSquadLine(SiSquadView squad)
        {
            var builder = new StringBuilder();
            builder.Append(squad.ArmyName);
            builder.Append(" - ");
            builder.Append(squad.Letter != null
                ? squad.Letter.CallSign
                : "#" + (squad.LetterIndex + 1));
            builder.Append(": \n");

            for (var i = 0; i < squad.Members.Count; i++)
            {
                if (i > 0)
                    builder.Append("; \n");
                builder.Append(FormatMember(squad.Members[i]));
            }
            builder.Append("; \n");

            return builder.ToString();
        }

        private static string FormatMarkerLabel(SiSquadView squad, SiSquadMemberView member)
        {
            var builder = new StringBuilder();
            if (squad.Letter != null)
            {
                builder.Append(squad.Letter.CallSign);
                builder.Append('\n');
            }

            var rank = member.Rank?.ShortName;
            if (!string.IsNullOrWhiteSpace(rank))
            {
                builder.Append(rank);
                builder.Append('\n');
            }

            builder.Append(MemberDisplayName(member));
            return builder.ToString();
        }

        private static string FormatMapMarkerName(SiSquadView squad)
        {
            if (squad == null)
                return "Allied squad";

            var callsign = SquadCallsign(squad);
            if (string.IsNullOrWhiteSpace(squad.ArmyName))
                return callsign;
            return squad.ArmyName + " - " + callsign;
        }

        private static string FormatMapMarkerDescription(SiSquadView squad)
        {
            if (squad == null)
                return "Squad leader position";

            var builder = new StringBuilder();
            builder.Append("Leader: ");
            builder.Append(!string.IsNullOrWhiteSpace(squad.LeaderName) ? squad.LeaderName : "Unknown");
            builder.Append('\n');
            builder.Append("Members: ");
            builder.Append(squad.Members?.Count ?? 0);
            builder.Append('\n');
            builder.Append("Position tracks the squad leader.");
            return builder.ToString();
        }

        private static string FormatMember(SiSquadMemberView member)
        {
            var builder = new StringBuilder();
            var rank = member.Rank?.ShortName;
            if (!string.IsNullOrWhiteSpace(rank))
            {
                builder.Append(rank);
                builder.Append(' ');
            }

            builder.Append(MemberDisplayName(member));
            if (member.Kind == SiSquadMemberKind.Npc)
                builder.Append(" [AI]");
            else if (!member.Online)
                builder.Append(" [offline]");
            return builder.ToString();
        }

        public string GetNpcCallsign(SiNpcManager npcManager, SiNpc npc)
        {
            if (npc == null)
                return "Soldier";

            PurgeClosedNpcs(npcManager);
            var squads = BuildSquads(npcManager);
            foreach (var squad in squads)
            {
                squad.Members.Sort(CompareMembers);
                foreach (var member in squad.Members)
                    if (member.Kind == SiSquadMemberKind.Npc && member.Id == npc.EntityId)
                        return !string.IsNullOrWhiteSpace(member.Callsign)
                            ? member.Callsign
                            : "Soldier";
            }

            return "Soldier";
        }

        private static string MemberDisplayName(SiSquadMemberView member)
        {
            if (member.Kind == SiSquadMemberKind.Npc && !string.IsNullOrWhiteSpace(member.Callsign))
                return member.Callsign;
            return member.Name;
        }

        private static string SquadCallsign(SiSquadView squad)
        {
            if (squad?.Letter != null)
                return squad.Letter.CallSign;
            return "Squad " + ((squad?.LetterIndex ?? 0) + 1);
        }

        private static SiSquadLeaderKey CreatePlayerLeader(long identityId)
        {
            return new SiSquadLeaderKey(
                SiSquadLeaderKind.Player,
                identityId,
                ArmyForIdentity(identityId));
        }

        private static SiArmyKey ArmyForIdentity(long identityId)
        {
            var faction = PlayerFaction(identityId);
            return faction != null
                ? new SiArmyKey(SiArmyKind.Faction, faction.FactionId)
                : new SiArmyKey(SiArmyKind.Player, identityId);
        }

        private static string ArmyName(SiArmyKey army, string fallbackLeaderName)
        {
            if (army.Kind == SiArmyKind.Faction)
            {
                var faction = FactionById(army.Id);
                if (faction != null)
                    return FactionName(faction);
                return "House " + army.Id;
            }

            if (army.Kind == SiArmyKind.Enemy)
                return "Enemy force";

            return "Independent " + fallbackLeaderName;
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

        private static MyFaction FactionById(long factionId)
        {
            try
            {
                if (MyFactionManager.Instance?.Factions == null)
                    return null;

                foreach (var faction in MyFactionManager.Instance.Factions.Values)
                    if (faction.FactionId == factionId)
                        return faction;
            }
            catch
            {
            }

            return null;
        }

        private static MyFaction FactionByTag(string tag)
        {
            try
            {
                return string.IsNullOrWhiteSpace(tag)
                    ? null
                    : MyFactionManager.Instance?.GetFactionByTag(tag);
            }
            catch
            {
                return null;
            }
        }

        private static string FactionName(MyFaction faction)
        {
            if (faction == null)
                return null;

            var tag = faction.FactionTag;
            var name = faction.FactionName;
            if (!string.IsNullOrWhiteSpace(tag) && !string.IsNullOrWhiteSpace(name))
                return "[" + tag + "] " + name;
            if (!string.IsNullOrWhiteSpace(name))
                return name;
            if (!string.IsNullOrWhiteSpace(tag))
                return "[" + tag + "]";
            return "House " + faction.FactionId;
        }

        private static string PlayerName(MyPlayer player)
        {
            if (!string.IsNullOrWhiteSpace(player?.Identity?.DisplayName))
                return player.Identity.DisplayName;
            if (player?.Identity != null)
                return "Player " + player.Identity.Id;
            return "Player";
        }

        private static string NpcName(SiNpc npc, SiAssignedNpc assignment)
        {
            var name = !string.IsNullOrWhiteSpace(assignment?.Archetype)
                ? assignment.Archetype
                : npc?.Archetype;
            return name;
        }
    }

    internal enum SiArmyKind
    {
        Faction,
        Player,
        Enemy,
    }

    internal struct SiArmyKey : IEquatable<SiArmyKey>
    {
        public readonly SiArmyKind Kind;
        public readonly long Id;

        public SiArmyKey(SiArmyKind kind, long id)
        {
            Kind = kind;
            Id = id;
        }

        public bool Equals(SiArmyKey other) => Kind == other.Kind && Id == other.Id;

        public override bool Equals(object obj) => obj is SiArmyKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Kind * 397) ^ Id.GetHashCode();
            }
        }
    }

    internal enum SiSquadLeaderKind
    {
        Player,
        Ai,
    }

    internal struct SiSquadLeaderKey : IEquatable<SiSquadLeaderKey>
    {
        public readonly SiSquadLeaderKind Kind;
        public readonly long Id;
        public readonly SiArmyKey Army;

        public SiSquadLeaderKey(SiSquadLeaderKind kind, long id, SiArmyKey army)
        {
            Kind = kind;
            Id = id;
            Army = army;
        }

        public bool Equals(SiSquadLeaderKey other) =>
            Kind == other.Kind && Id == other.Id && Army.Equals(other.Army);

        public override bool Equals(object obj) => obj is SiSquadLeaderKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (int)Kind;
                hashCode = (hashCode * 397) ^ Id.GetHashCode();
                hashCode = (hashCode * 397) ^ Army.GetHashCode();
                return hashCode;
            }
        }
    }

    internal sealed class SiAssignedNpc
    {
        public SiAssignedNpc(SiSquadLeaderKey leader, string leaderName, string archetype, bool isLeader)
        {
            Leader = leader;
            LeaderName = leaderName;
            Archetype = archetype;
            IsLeader = isLeader;
        }

        public SiSquadLeaderKey Leader { get; }
        public string LeaderName { get; }
        public string Archetype { get; }
        public bool IsLeader { get; }
    }

    internal sealed class SiSquadLeadershipChange
    {
        public SiSquadLeadershipChange(
            SiSquadLeaderKey oldLeader,
            SiSquadLeaderKey newLeader,
            string newLeaderName)
        {
            OldLeader = oldLeader;
            NewLeader = newLeader;
            NewLeaderName = newLeaderName;
        }

        public SiSquadLeaderKey OldLeader { get; }
        public SiSquadLeaderKey NewLeader { get; }
        public string NewLeaderName { get; }
    }

    internal sealed class SiSquadNpcMarker
    {
        public SiSquadNpcMarker(SiNpc npc, string label)
        {
            Npc = npc;
            Label = label;
        }

        public SiNpc Npc { get; }
        public string Label { get; }
    }

    internal sealed class SiSquadMapMarker
    {
        public SiSquadMapMarker(
            SiSquadLeaderKey leader,
            in Vector3D position,
            string name,
            string description,
            bool showOnHud,
            string styleId)
        {
            Leader = leader;
            Position = position;
            Name = name;
            Description = description;
            ShowOnHud = showOnHud;
            StyleId = styleId;
        }

        public SiSquadLeaderKey Leader { get; }
        public Vector3D Position { get; }
        public string Name { get; }
        public string Description { get; }
        public bool ShowOnHud { get; }
        public string StyleId { get; }
    }

    internal enum SiSquadMemberKind
    {
        Player,
        Npc,
    }

    internal sealed class SiSquadView
    {
        public SiSquadView(
            SiSquadLeaderKey leader,
            string armyName,
            string leaderName)
        {
            Leader = leader;
            ArmyName = armyName;
            LeaderName = leaderName;
        }

        public SiSquadLeaderKey Leader { get; }
        public int LetterIndex { get; private set; }
        public SiSquadLetterDefinition Letter { get; private set; }
        public string ArmyName { get; }
        public string LeaderName { get; }
        public List<SiSquadMemberView> Members { get; } = new List<SiSquadMemberView>();

        public void SetLetter(int index, SiSquadLetterDefinition letter)
        {
            LetterIndex = index;
            Letter = letter;
        }
    }

    internal sealed class SiSquadMemberView
    {
        public SiSquadMemberView(
            SiSquadMemberKind kind,
            long id,
            string name,
            SiRankDefinition rank,
            bool online)
        {
            Kind = kind;
            Id = id;
            Name = name;
            Rank = rank;
            Online = online;
        }

        public SiSquadMemberKind Kind { get; }
        public long Id { get; }
        public string Name { get; }
        public SiRankDefinition Rank { get; }
        public bool Online { get; }
        public string Callsign { get; private set; }

        public void SetCallsign(string callsign)
        {
            Callsign = callsign;
        }
    }
}
