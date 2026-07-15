using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.UI;
using Medieval.GUI.ContextMenu;
using Medieval.GUI.ContextMenu.Attributes;
using Medieval.GUI.ContextMenu.DataSources;
using VRage;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace Si.UtilityAI
{
    [MyContextMenuContextType(typeof(MyObjectBuilder_SiUtilityAdminMenuContext))]
    public sealed class SiUtilityAdminMenuContext : MyContextMenuContext
    {
        private static readonly MyStringId Webbings = MyStringId.GetOrCompute("AdminWebbings");
        private static readonly MyStringId SpawnParatrooper = MyStringId.GetOrCompute("AdminSpawnParatrooper");
        private static readonly MyStringId SpawnEnemy = MyStringId.GetOrCompute("AdminSpawnEnemy");
        private static readonly MyStringId Squads = MyStringId.GetOrCompute("AdminSquads");
        private static readonly MyStringId SquadEnemy = MyStringId.GetOrCompute("AdminSquadEnemy");
        private static readonly MyStringId SelectedSquadMembers = MyStringId.GetOrCompute("AdminSelectedSquadMembers");
        private static readonly MyStringId SelectedSquadMembersVersion = MyStringId.GetOrCompute("AdminSelectedSquadMembersVersion");
        private static readonly MyStringId NpcCount = MyStringId.GetOrCompute("AdminNpcCount");
        private static readonly MyStringId SquadRoster = MyStringId.GetOrCompute("AdminSquadRoster");
        private static readonly MyStringId UtilityDecisionMaking = MyStringId.GetOrCompute("AdminUtilityDecisionMaking");
        private static readonly MyStringId GameLog = MyStringId.GetOrCompute("AdminGameLog");

        private string _selectedWebbing;
        private string _selectedSquad;
        private bool _spawnParatrooper;
        private bool _spawnEnemy;
        private bool _squadEnemy;
        private bool _utilityDecisionMakingEnabled;
        private bool _gameLogEnabled;
        private long _selectedSquadMembersVersion;

        public override void Init(object[] contextParams)
        {
            _utilityDecisionMakingEnabled = SiNpcSessionComponent.Instance?.AdminUtilityDecisionMakingEnabled ?? false;
            _gameLogEnabled = SiNpcSessionComponent.Instance?.AdminGameLogEnabled ?? false;

            m_dataSources.Add(Webbings, new DynamicListDataSource<string>(
                SiNpcTrooperCatalog.GetKnownWebbings,
                item => item,
                item => item,
                () => SelectedWebbing,
                value => _selectedWebbing = value));
            m_dataSources.Add(SpawnParatrooper, SimpleDataSources.Simple(
                () => _spawnParatrooper,
                value => _spawnParatrooper = value));
            m_dataSources.Add(SpawnEnemy, SimpleDataSources.Simple(
                () => _spawnEnemy,
                value => _spawnEnemy = value));

            m_dataSources.Add(Squads, new DynamicListDataSource<SiNpcSquadPresetDefinition>(
                SiNpcSquadPresetCatalog.GetKnownPresets,
                preset => preset.Id.SubtypeName,
                preset => preset.Id.SubtypeName,
                () => SelectedSquad,
                SetSelectedSquad));
            m_dataSources.Add(SquadEnemy, SimpleDataSources.Simple(
                () => _squadEnemy,
                value => _squadEnemy = value));
            m_dataSources.Add(SelectedSquadMembers, new DynamicListDataSource<string>(
                SelectedSquadMemberIds,
                item => item,
                item => item,
                () => null,
                value => { }));
            m_dataSources.Add(SelectedSquadMembersVersion, SimpleDataSources.SimpleReadOnly(
                () => _selectedSquadMembersVersion));
            m_dataSources.Add(NpcCount, SimpleDataSources.SimpleReadOnly(
                () => SiNpcSessionComponent.Instance?.AdminNpcCountText() ?? "Custom NPC system is not available."));
            m_dataSources.Add(SquadRoster, new DynamicListDataSource<string>(
                SquadRosterLines,
                item => item,
                item => item,
                () => null,
                value => { }));
            m_dataSources.Add(UtilityDecisionMaking, SimpleDataSources.Simple(
                () => _utilityDecisionMakingEnabled,
                SetUtilityDecisionMaking));
            m_dataSources.Add(GameLog, SimpleDataSources.Simple(
                () => _gameLogEnabled,
                SetGameLog));
        }

        public void AdminSpawn()
        {
            SiNpcSessionComponent.Instance?.RequestAdminSpawn(SelectedWebbing, _spawnParatrooper, _spawnEnemy);
        }

        public void AdminSpawnSquad()
        {
            SiNpcSessionComponent.Instance?.RequestAdminSpawnSquad(SelectedSquad, _squadEnemy);
        }

        public void AdminRearm()
        {
            SiNpcSessionComponent.Instance?.RequestAdminRearm();
        }

        public void AdminClear()
        {
            SiNpcSessionComponent.Instance?.RequestAdminClear();
        }

        private void SetUtilityDecisionMaking(bool enabled)
        {
            _utilityDecisionMakingEnabled = enabled;
            SiNpcSessionComponent.Instance?.RequestAdminSetUtilityDecisionMaking(enabled);
        }

        private void SetGameLog(bool enabled)
        {
            _gameLogEnabled = enabled;
            SiNpcSessionComponent.Instance?.RequestAdminSetGameLog(enabled);
        }

        private void SetSelectedSquad(string squadSubtype)
        {
            if (string.Equals(_selectedSquad, squadSubtype, StringComparison.OrdinalIgnoreCase))
                return;

            _selectedSquad = squadSubtype;
            _selectedSquadMembersVersion++;
        }

        private string SelectedWebbing
        {
            get
            {
                var webbings = SiNpcTrooperCatalog.GetKnownWebbings();
                if (webbings.Count == 0)
                    return null;

                if (!webbings.Contains(_selectedWebbing))
                    _selectedWebbing = webbings[0];
                return _selectedWebbing;
            }
        }

        private string SelectedSquad
        {
            get
            {
                var presets = SiNpcSquadPresetCatalog.GetKnownPresets();
                if (presets.Count == 0)
                    return null;

                for (var i = 0; i < presets.Count; i++)
                    if (string.Equals(presets[i].Id.SubtypeName, _selectedSquad, StringComparison.OrdinalIgnoreCase))
                        return _selectedSquad;

                _selectedSquad = presets[0].Id.SubtypeName;
                return _selectedSquad;
            }
        }

        private List<string> SelectedSquadMemberIds()
        {
            var selected = SelectedSquad;
            var presets = SiNpcSquadPresetCatalog.GetKnownPresets();
            for (var i = 0; i < presets.Count; i++)
                if (string.Equals(presets[i].Id.SubtypeName, selected, StringComparison.OrdinalIgnoreCase))
                    return SquadMemberIds(presets[i]);

            return new List<string>();
        }

        private static List<string> SquadRosterLines()
        {
            var session = SiNpcSessionComponent.Instance;
            var lines = session?.Squads?.CreateRosterLines(session.Npcs);
            if (lines == null || lines.Count == 0)
                return new List<string> { "No squad roster is available." };

            var entries = new List<string>();
            for (var i = 0; i < lines.Count; i++)
            {
                if (i > 0)
                    entries.Add(string.Empty);

                var squadLines = lines[i].Split(new[] { '\n' }, StringSplitOptions.None);
                for (var j = 0; j < squadLines.Length; j++)
                    entries.Add(squadLines[j].TrimEnd('\r'));
            }

            return entries;
        }

        private static List<string> SquadMemberIds(SiNpcSquadPresetDefinition preset)
        {
            var members = new List<string>();
            if (preset?.Members == null)
                return members;

            for (var i = 0; i < preset.Members.Count; i++)
            {
                var member = preset.Members[i];
                if (member == null || string.IsNullOrWhiteSpace(member.WebbingSubtype) || member.Count <= 0)
                    continue;

                for (var count = 0; count < member.Count; count++)
                    members.Add(member.WebbingSubtype);
            }

            return members;
        }

        private sealed class DynamicListDataSource<T> : IMyListboxDataSource
        {
            private readonly Func<List<T>> _getItems;
            private readonly Func<T, string> _key;
            private readonly Func<T, string> _title;
            private readonly Func<string> _getSelected;
            private readonly Action<string> _setSelected;

            public DynamicListDataSource(
                Func<List<T>> getItems,
                Func<T, string> key,
                Func<T, string> title,
                Func<string> getSelected,
                Action<string> setSelected)
            {
                _getItems = getItems;
                _key = key;
                _title = title;
                _getSelected = getSelected;
                _setSelected = setSelected;
            }

            public void Close()
            {
            }

            public void GetItems(List<MyTuple<MyStringId, string>> output)
            {
                output.Clear();
                var items = _getItems();
                for (var i = 0; i < items.Count; i++)
                    output.Add(new MyTuple<MyStringId, string>(
                        MyStringId.NullOrEmpty,
                        _title(items[i])));
            }

            public void GetItemSelection(List<bool> output)
            {
                output.Clear();
                var items = _getItems();
                var selected = _getSelected();
                for (var i = 0; i < items.Count; i++)
                    output.Add(string.Equals(_key(items[i]), selected, StringComparison.OrdinalIgnoreCase));
            }

            public void SetItemSelection(List<bool> input)
            {
                if (input == null)
                    return;

                var items = _getItems();
                var count = Math.Min(input.Count, items.Count);
                for (var i = 0; i < count; i++)
                    if (input[i])
                    {
                        _setSelected(_key(items[i]));
                        return;
                    }
            }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiUtilityAdminMenuContext : MyObjectBuilder_Base
    {
    }
}
