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
        private static readonly MyDefinitionId DefaultDefinitionId =
            new MyDefinitionId(typeof(MyObjectBuilder_SiSquadSystemDefinition), "SiDefaultSquadSystem");

        private readonly List<SiSquadLetterDefinition> _letters = new List<SiSquadLetterDefinition>();
        private readonly List<SiRankDefinition> _ranks = new List<SiRankDefinition>();
        private readonly Dictionary<string, SiRankDefinition> _ranksById =
            new Dictionary<string, SiRankDefinition>(StringComparer.OrdinalIgnoreCase);
        private readonly List<SiSquadFallBehindSpeedOverride> _fallBehindSpeedOverrides =
            new List<SiSquadFallBehindSpeedOverride>();

        public IReadOnlyList<SiSquadLetterDefinition> Letters => _letters;
        public IReadOnlyList<SiRankDefinition> Ranks => _ranks;
        public IReadOnlyList<SiSquadFallBehindSpeedOverride> FallBehindSpeedOverrides => _fallBehindSpeedOverrides;
        public string PlayerRankId { get; private set; }
        public string NpcRankId { get; private set; }
        public double FollowDistance { get; private set; }
        public double ColumnSpacing { get; private set; }
        public double FileSpacing { get; private set; }
        public double LineSpacing { get; private set; }
        public double VeeSpacing { get; private set; }
        public double FormationBoxSpacing { get; private set; }
        public double LongBoxAspectRatio { get; private set; }
        public double WideBoxAspectRatio { get; private set; }
        public double SquareBoxAspectRatio { get; private set; }
        public double StaggeredColumnOffset { get; private set; }
        public double WaypointRefreshDistance { get; private set; }
        public double EnemyJoinRadius { get; private set; }

        public SiRankDefinition PlayerRank => GetRank(PlayerRankId);
        public SiRankDefinition NpcRank => GetRank(NpcRankId);

        internal static SiSquadSystemDefinition LoadDefault()
        {
            SiSquadSystemDefinition definition;
            if (MyDefinitionManager.TryGet(DefaultDefinitionId, out definition))
                return definition;

            foreach (var candidate in MyDefinitionManager.GetOfType<SiSquadSystemDefinition>())
                if (candidate != null)
                    return candidate;
            return null;
        }

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
            FormationBoxSpacing = Math.Max(0, ob.FormationBoxSpacing);
            LongBoxAspectRatio = Math.Max(0, ob.LongBoxAspectRatio);
            WideBoxAspectRatio = Math.Max(0, ob.WideBoxAspectRatio);
            SquareBoxAspectRatio = Math.Max(0, ob.SquareBoxAspectRatio);
            StaggeredColumnOffset = Math.Max(0, ob.StaggeredColumnOffset);
            WaypointRefreshDistance = Math.Max(0, ob.WaypointRefreshDistance);
            EnemyJoinRadius = Math.Max(0, ob.EnemyJoinRadius);

            _fallBehindSpeedOverrides.Clear();
            if (ob.FallBehindSpeedOverrides != null)
                foreach (var speedOverride in ob.FallBehindSpeedOverrides)
                {
                    if (speedOverride == null)
                        continue;

                    _fallBehindSpeedOverrides.Add(new SiSquadFallBehindSpeedOverride(
                        speedOverride.CheckpointSpeed,
                        Math.Max(0, speedOverride.DistanceLessThan),
                        speedOverride.ResultSpeed));
                }

            _fallBehindSpeedOverrides.Sort(CompareFallBehindSpeedOverrides);
        }

        public SiNpcMovementSpeed ResolveFormationSpeed(
            SiNpcMovementSpeed checkpointSpeed,
            double checkpointDistance)
        {
            checkpointDistance = Math.Max(0, checkpointDistance);
            for (var i = 0; i < _fallBehindSpeedOverrides.Count; i++)
            {
                var speedOverride = _fallBehindSpeedOverrides[i];
                if (speedOverride.CheckpointSpeed != checkpointSpeed)
                    continue;
                if (speedOverride.DistanceLessThan > 0
                    && checkpointDistance >= speedOverride.DistanceLessThan)
                    continue;

                return speedOverride.ResultSpeed;
            }

            return checkpointSpeed;
        }

        private static int CompareFallBehindSpeedOverrides(
            SiSquadFallBehindSpeedOverride left,
            SiSquadFallBehindSpeedOverride right)
        {
            var leftDistance = left.DistanceLessThan > 0
                ? left.DistanceLessThan
                : double.MaxValue;
            var rightDistance = right.DistanceLessThan > 0
                ? right.DistanceLessThan
                : double.MaxValue;
            return leftDistance.CompareTo(rightDistance);
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
                if (!string.IsNullOrWhiteSpace(Phonetic))
                    return Phonetic;
                if (!string.IsNullOrWhiteSpace(DisplayName))
                    return DisplayName;
                return Code;
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
        public double FormationBoxSpacing;

        [XmlElement]
        public double LongBoxAspectRatio;

        [XmlElement]
        public double WideBoxAspectRatio;

        [XmlElement]
        public double SquareBoxAspectRatio;

        [XmlElement]
        public double StaggeredColumnOffset;

        [XmlElement]
        public double WaypointRefreshDistance;

        [XmlElement]
        public double EnemyJoinRadius;

        [XmlArrayItem("Override")]
        public List<FallBehindSpeedOverride> FallBehindSpeedOverrides;

        public class FallBehindSpeedOverride
        {
            [XmlAttribute]
            public SiNpcMovementSpeed CheckpointSpeed;

            // Zero means the final, unbounded row in the table.
            [XmlAttribute]
            public double DistanceLessThan;

            [XmlAttribute]
            public SiNpcMovementSpeed ResultSpeed;
        }

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

    public sealed class SiSquadFallBehindSpeedOverride
    {
        public SiSquadFallBehindSpeedOverride(
            SiNpcMovementSpeed checkpointSpeed,
            double distanceLessThan,
            SiNpcMovementSpeed resultSpeed)
        {
            CheckpointSpeed = checkpointSpeed;
            DistanceLessThan = distanceLessThan;
            ResultSpeed = resultSpeed;
        }

        public SiNpcMovementSpeed CheckpointSpeed { get; }
        public double DistanceLessThan { get; }
        public SiNpcMovementSpeed ResultSpeed { get; }
    }
}
