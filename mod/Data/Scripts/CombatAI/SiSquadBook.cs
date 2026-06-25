using System;
using System.Collections.Generic;
using System.Text;
using Medieval.GameSystems.Factions;
using Sandbox.Game.Players;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;

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

            var leader = CreatePlayerLeader(player.Identity.Id);
            _assignedNpcs[npc.EntityId] = new SiAssignedNpc(
                leader,
                PlayerName(player),
                npc.Archetype);
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

            _assignedNpcs[npc.EntityId] = new SiAssignedNpc(
                CreatePlayerLeader(identityId),
                string.IsNullOrWhiteSpace(leaderName) ? "Player " + identityId : leaderName,
                npc.Archetype);
        }

        public bool TryGetAssignment(long npcId, out SiAssignedNpc assignment) =>
            _assignedNpcs.TryGetValue(npcId, out assignment);

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
                        Definition.NpcRank,
                        true));
                }

            var squads = new List<SiSquadView>(byLeader.Values);
            AssignLetters(squads);
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

        private void PurgeClosedNpcs(SiNpcManager npcManager)
        {
            _staleNpcIds.Clear();
            foreach (var assigned in _assignedNpcs)
                if (npcManager == null || !npcManager.Npcs.ContainsKey(assigned.Key))
                    _staleNpcIds.Add(assigned.Key);

            foreach (var id in _staleNpcIds)
                _assignedNpcs.Remove(id);
            _staleNpcIds.Clear();
        }

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

        private static int RankOrder(SiRankDefinition rank) => rank?.Order ?? 0;

        private string FormatSquadLine(SiSquadView squad)
        {
            var builder = new StringBuilder();
            builder.Append(squad.ArmyName);
            builder.Append(" - ");
            builder.Append(squad.Letter != null
                ? squad.Letter.CallSign
                : "#" + (squad.LetterIndex + 1));
            builder.Append(": ");

            for (var i = 0; i < squad.Members.Count; i++)
            {
                if (i > 0)
                    builder.Append("; ");
                builder.Append(FormatMember(squad.Members[i]));
            }

            return builder.ToString();
        }

        private static string FormatMarkerLabel(SiSquadView squad, SiSquadMemberView member)
        {
            var builder = new StringBuilder();
            if (squad.Letter != null)
            {
                builder.Append(squad.Letter.CallSign);
                builder.Append(' ');
            }

            var rank = member.Rank?.ShortName;
            if (!string.IsNullOrWhiteSpace(rank))
            {
                builder.Append(rank);
                builder.Append(' ');
            }

            builder.Append(member.Name);
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

            builder.Append(member.Name);
            if (member.Kind == SiSquadMemberKind.Npc)
                builder.Append(" [AI]");
            else if (!member.Online)
                builder.Append(" [offline]");
            return builder.ToString();
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
            var name = !string.IsNullOrWhiteSpace(assignment.Archetype)
                ? assignment.Archetype
                : npc.Archetype;
            return name + " " + npc.EntityId;
        }
    }

    internal enum SiArmyKind
    {
        Faction,
        Player,
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
        public SiAssignedNpc(SiSquadLeaderKey leader, string leaderName, string archetype)
        {
            Leader = leader;
            LeaderName = leaderName;
            Archetype = archetype;
        }

        public SiSquadLeaderKey Leader { get; }
        public string LeaderName { get; }
        public string Archetype { get; }
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
    }
}
