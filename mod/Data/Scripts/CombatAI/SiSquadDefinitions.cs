using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;

namespace Si.UtilityAI
{
    [MyDefinitionType(typeof(MyObjectBuilder_SiSquadSystemDefinition))]
    public class SiSquadSystemDefinition : MyDefinitionBase
    {
        private readonly List<SiSquadLetterDefinition> _letters = new List<SiSquadLetterDefinition>();
        private readonly List<SiRankDefinition> _ranks = new List<SiRankDefinition>();
        private readonly Dictionary<string, SiRankDefinition> _ranksById =
            new Dictionary<string, SiRankDefinition>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<SiSquadLetterDefinition> Letters => _letters;
        public IReadOnlyList<SiRankDefinition> Ranks => _ranks;
        public string PlayerRankId { get; private set; }
        public string NpcRankId { get; private set; }
        public double FollowDistance { get; private set; }
        public double ColumnSpacing { get; private set; }
        public double FileSpacing { get; private set; }
        public double LineSpacing { get; private set; }
        public double VeeSpacing { get; private set; }
        public double WaypointRefreshDistance { get; private set; }
        public double EnemyJoinRadius { get; private set; }

        public SiRankDefinition PlayerRank => GetRank(PlayerRankId);
        public SiRankDefinition NpcRank => GetRank(NpcRankId);

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiSquadSystemDefinition)builder;

            _letters.Clear();
            if (ob.Letters != null)
                foreach (var letter in ob.Letters)
                {
                    if (letter == null || string.IsNullOrWhiteSpace(letter.Code))
                        continue;
                    _letters.Add(new SiSquadLetterDefinition(letter));
                }

            _ranks.Clear();
            _ranksById.Clear();
            if (ob.Ranks != null)
                foreach (var rank in ob.Ranks)
                {
                    if (rank == null || string.IsNullOrWhiteSpace(rank.Id))
                        continue;

                    var parsed = new SiRankDefinition(rank);
                    _ranks.Add(parsed);
                    _ranksById[parsed.Id] = parsed;
                }

            PlayerRankId = ob.PlayerRank;
            NpcRankId = ob.NpcRank;
            FollowDistance = Math.Max(0, ob.FollowDistance);
            ColumnSpacing = Math.Max(0, ob.ColumnSpacing);
            FileSpacing = Math.Max(0, ob.FileSpacing);
            LineSpacing = Math.Max(0, ob.LineSpacing);
            VeeSpacing = Math.Max(0, ob.VeeSpacing);
            WaypointRefreshDistance = Math.Max(0, ob.WaypointRefreshDistance);
            EnemyJoinRadius = Math.Max(0, ob.EnemyJoinRadius);
        }

        public SiSquadLetterDefinition GetLetter(int index)
        {
            return index >= 0 && index < _letters.Count ? _letters[index] : null;
        }

        public SiRankDefinition GetRank(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;

            SiRankDefinition rank;
            return _ranksById.TryGetValue(id, out rank) ? rank : null;
        }
    }

    public sealed class SiSquadLetterDefinition
    {
        public SiSquadLetterDefinition(MyObjectBuilder_SiSquadSystemDefinition.SquadLetter ob)
        {
            Code = ob.Code;
            Phonetic = ob.Phonetic;
            DisplayName = ob.DisplayName;
        }

        public string Code { get; }
        public string Phonetic { get; }
        public string DisplayName { get; }

        public string CallSign
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(DisplayName))
                    return DisplayName;
                return string.IsNullOrWhiteSpace(Phonetic) ? Code : Code + "-" + Phonetic;
            }
        }
    }

    public sealed class SiRankDefinition
    {
        public SiRankDefinition(MyObjectBuilder_SiSquadSystemDefinition.Rank ob)
        {
            Id = ob.Id;
            DisplayName = ob.DisplayName;
            Abbreviation = ob.Abbreviation;
            Order = ob.Order;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string Abbreviation { get; }
        public int Order { get; }

        public string ShortName =>
            !string.IsNullOrWhiteSpace(Abbreviation)
                ? Abbreviation
                : DisplayName;
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiSquadSystemDefinition : MyObjectBuilder_DefinitionBase
    {
        [XmlElement]
        public string PlayerRank;

        [XmlElement]
        public string NpcRank;

        [XmlElement]
        public double FollowDistance;

        [XmlElement]
        public double ColumnSpacing;

        [XmlElement]
        public double FileSpacing;

        [XmlElement]
        public double LineSpacing;

        [XmlElement]
        public double VeeSpacing;

        [XmlElement]
        public double WaypointRefreshDistance;

        [XmlElement]
        public double EnemyJoinRadius;

        [XmlArrayItem("Letter")]
        public List<SquadLetter> Letters;

        [XmlArrayItem("Rank")]
        public List<Rank> Ranks;

        public class SquadLetter
        {
            [XmlAttribute]
            public string Code;

            [XmlAttribute]
            public string Phonetic;

            [XmlAttribute]
            public string DisplayName;
        }

        public class Rank
        {
            [XmlAttribute]
            public string Id;

            [XmlAttribute]
            public string DisplayName;

            [XmlAttribute]
            public string Abbreviation;

            [XmlAttribute]
            public int Order;
        }
    }
}
