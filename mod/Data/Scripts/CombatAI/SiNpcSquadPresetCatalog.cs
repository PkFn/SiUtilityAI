using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;

namespace Si.UtilityAI
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiNpcSquadPresetDefinition : MyObjectBuilder_DefinitionBase
    {
        [XmlElement]
        public string DisplayName;

        [XmlElement]
        public string Description;

        [XmlArrayItem("Member")]
        public List<Member> Members;

        public class Member
        {
            [XmlAttribute]
            public string WebbingSubtype;

            [XmlAttribute]
            public bool Paratrooper;

            [XmlAttribute]
            public int Count = 1;
        }
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiNpcSquadPresetDefinition))]
    public class SiNpcSquadPresetDefinition : MyDefinitionBase
    {
        private readonly List<SiNpcSquadPresetMemberDefinition> _members =
            new List<SiNpcSquadPresetMemberDefinition>();

        public string DisplayName { get; private set; }
        public string Description { get; private set; }
        public IReadOnlyList<SiNpcSquadPresetMemberDefinition> Members => _members;

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiNpcSquadPresetDefinition)builder;
            DisplayName = string.IsNullOrWhiteSpace(ob.DisplayName) ? null : ob.DisplayName.Trim();
            Description = string.IsNullOrWhiteSpace(ob.Description) ? null : ob.Description.Trim();

            _members.Clear();
            if (ob.Members == null)
                return;

            foreach (var member in ob.Members)
            {
                if (member == null || string.IsNullOrWhiteSpace(member.WebbingSubtype) || member.Count <= 0)
                    continue;

                _members.Add(new SiNpcSquadPresetMemberDefinition(
                    member.WebbingSubtype.Trim(),
                    member.Paratrooper,
                    member.Count));
            }
        }
    }

    public sealed class SiNpcSquadPresetMemberDefinition
    {
        public SiNpcSquadPresetMemberDefinition(string webbingSubtype, bool isParatrooper, int count)
        {
            WebbingSubtype = webbingSubtype;
            IsParatrooper = isParatrooper;
            Count = count;
        }

        public string WebbingSubtype { get; }
        public bool IsParatrooper { get; }
        public int Count { get; }
    }

    internal sealed class SiNpcSquadPresetSpawnEntry
    {
        public SiNpcSquadPresetSpawnEntry(string webbingSubtype, bool isParatrooper)
        {
            WebbingSubtype = webbingSubtype;
            IsParatrooper = isParatrooper;
        }

        public string WebbingSubtype { get; }
        public bool IsParatrooper { get; }
    }

    internal static class SiNpcSquadPresetCatalog
    {
        internal static bool TryResolvePreset(
            string presetSubtype,
            out string resolvedPresetSubtype,
            out SiNpcSquadPresetDefinition preset,
            out List<SiNpcSquadPresetSpawnEntry> members,
            out string failure)
        {
            resolvedPresetSubtype = null;
            preset = null;
            members = null;
            failure = null;

            if (!TryGetPreset(presetSubtype, out preset) || preset == null)
            {
                failure = $"Unknown squad preset '{presetSubtype}'.";
                return false;
            }

            resolvedPresetSubtype = preset.Id.SubtypeName;
            if (preset.Members == null || preset.Members.Count == 0)
            {
                failure = $"Squad preset '{resolvedPresetSubtype}' does not define any members.";
                return false;
            }

            var entries = new List<SiNpcSquadPresetSpawnEntry>();
            for (var i = 0; i < preset.Members.Count; i++)
            {
                var member = preset.Members[i];
                if (member == null || string.IsNullOrWhiteSpace(member.WebbingSubtype) || member.Count <= 0)
                    continue;

                if (!SiNpcTrooperCatalog.TryResolveLoadout(member.WebbingSubtype, member.IsParatrooper, out _, out _))
                {
                    failure = $"Squad preset '{resolvedPresetSubtype}' references unknown webbing '{member.WebbingSubtype}'.";
                    return false;
                }

                for (var count = 0; count < member.Count; count++)
                    entries.Add(new SiNpcSquadPresetSpawnEntry(member.WebbingSubtype, member.IsParatrooper));
            }

            if (entries.Count == 0)
            {
                failure = $"Squad preset '{resolvedPresetSubtype}' does not contain any valid members.";
                return false;
            }

            members = entries;
            return true;
        }

        internal static List<SiNpcSquadPresetDefinition> GetKnownPresets()
        {
            var presets = new List<SiNpcSquadPresetDefinition>();
            foreach (var preset in MyDefinitionManager.GetOfType<SiNpcSquadPresetDefinition>())
            {
                if (preset == null || string.IsNullOrWhiteSpace(preset.Id.SubtypeName))
                    continue;

                if (!TryResolvePreset(preset.Id.SubtypeName, out _, out _, out _, out _))
                    continue;

                presets.Add(preset);
            }

            presets.Sort((left, right) => string.Compare(
                left?.Id.SubtypeName,
                right?.Id.SubtypeName,
                StringComparison.OrdinalIgnoreCase));
            return presets;
        }

        private static bool TryGetPreset(string presetSubtype, out SiNpcSquadPresetDefinition preset)
        {
            preset = null;
            if (string.IsNullOrWhiteSpace(presetSubtype))
                return false;

            var id = new MyDefinitionId(
                typeof(MyObjectBuilder_SiNpcSquadPresetDefinition),
                presetSubtype.Trim());
            if (MyDefinitionManager.TryGet(id, out preset))
                return preset != null;

            foreach (var candidate in MyDefinitionManager.GetOfType<SiNpcSquadPresetDefinition>())
                if (string.Equals(candidate?.Id.SubtypeName, presetSubtype, StringComparison.OrdinalIgnoreCase))
                {
                    preset = candidate;
                    return true;
                }

            return false;
        }
    }
}
