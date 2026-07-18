using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.UI;
using Medieval.GUI.ContextMenu;
using Medieval.GUI.ContextMenu.Attributes;
using Medieval.GUI.ContextMenu.DataSources;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Inventory;
using Sandbox.Game.Players;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.Entity;
using VRage.Inventory;
using VRage.ObjectBuilders;
using VRage.Session;
using VRage.Utils;
using VRageMath;

namespace Si.UtilityAI
{
    internal sealed class SiBaseCampMenuSession
    {
        private const string RecruitInventory = "SiBaseCampRecruits";
        private const string WebbingInventory = "SiBaseCampWebbings";

        private readonly MyEntity _baseCamp;
        private readonly float _nearbySquadRadius;
        private SiSquadLeaderKey? _selectedRefundSquad;

        public SiBaseCampMenuSession(MyEntity baseCamp, float nearbySquadRadius)
        {
            _baseCamp = baseCamp;
            _nearbySquadRadius = nearbySquadRadius;
        }

        public MyInventory GetInventory(string subtype)
        {
            return _baseCamp?.Components.Get<MyInventoryBase>(MyStringHash.GetOrCompute(subtype)) as MyInventory;
        }

        public List<string> GetWebbingInventoryLines()
        {
            var lines = new List<string>();
            var inventory = GetInventory(WebbingInventory);
            if (inventory == null || inventory.Items.Count == 0)
            {
                lines.Add("No webbings stored.");
                return lines;
            }

            var knownWebbings = new HashSet<string>(
                SiNpcTrooperCatalog.GetKnownWebbings(),
                StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < inventory.Items.Count; i++)
            {
                var item = inventory.Items.ItemAt(i);
                if (!knownWebbings.Contains(item.DefinitionId.SubtypeName))
                    continue;
                lines.Add(item.DefinitionId.SubtypeName + " x" + item.Amount);
            }

            if (lines.Count == 0)
                lines.Add("No compatible webbings stored.");
            return lines;
        }

        public List<SiSquadView> GetNearbyAlliedSquads()
        {
            var session = SiNpcSessionComponent.Instance;
            var player = MyAPIGateway.Session?.Player as MyPlayer;
            var identityId = player?.Identity?.Id ?? 0;
            var position = _baseCamp?.WorldMatrix.Translation ?? Vector3D.Zero;
            return session?.Squads?.CreateNearbyAlliedSquads(
                session.Npcs,
                identityId,
                position,
                _nearbySquadRadius) ?? new List<SiSquadView>();
        }

        public void SelectRefundSquad(SiSquadLeaderKey leader)
        {
            _selectedRefundSquad = leader;
        }

        public SiSquadView GetSelectedRefundSquad()
        {
            var squads = GetNearbyAlliedSquads();
            if (_selectedRefundSquad.HasValue)
                for (var i = 0; i < squads.Count; i++)
                    if (squads[i].Leader.Equals(_selectedRefundSquad.Value))
                        return squads[i];

            if (squads.Count == 0)
                return null;

            _selectedRefundSquad = squads[0].Leader;
            return squads[0];
        }

        public string SelectedLeaderLabel()
        {
            var squad = GetSelectedRefundSquad();
            if (squad == null)
                return "Squad leader: none";
            if (squad.Leader.Kind == SiSquadLeaderKind.Ai)
                return "Squad leader: AI";
            return "Squad leader: " + squad.LeaderName + " (Player)";
        }

        public List<string> SelectedMemberLines()
        {
            var lines = new List<string>();
            var squad = GetSelectedRefundSquad();
            if (squad == null || squad.Members.Count == 0)
            {
                lines.Add("No squad members available.");
                return lines;
            }

            for (var i = 0; i < squad.Members.Count; i++)
                lines.Add(squad.Members[i].Name);
            return lines;
        }

        public void OpenInventory()
        {
            OpenInventory(RecruitInventory);
        }

        public void SpawnAiSquad()
        {
            SiNpcSessionComponent.Instance?.RequestBaseCampSpawn(
                _baseCamp?.EntityId ?? 0,
                true);
        }

        public void SpawnPlayerSquad()
        {
            SiNpcSessionComponent.Instance?.RequestBaseCampSpawn(
                _baseCamp?.EntityId ?? 0,
                false);
        }

        public void RefundSquad()
        {
            var squad = GetSelectedRefundSquad();
            if (squad == null)
                return;

            SiNpcSessionComponent.Instance?.RequestBaseCampRefund(
                _baseCamp?.EntityId ?? 0,
                squad.Leader.Kind,
                squad.Leader.Id);
        }

        private void OpenInventory(string subtype)
        {
            var inventory = GetInventory(subtype);
            if (inventory == null)
                return;

#if !VRAGE_VERSION_0
            Medieval.GUI.Hud.MyGuiScreenHudMedieval.Static.ShowInventory(inventory);
#else
            Sandbox.Game.Gui.MyGuiScreenHudBase.Static.ShowInventory(inventory);
#endif
        }
    }

    [MyContextMenuContextType(typeof(MyObjectBuilder_SiBaseCampMenuContext))]
    public sealed class SiBaseCampMenuContext : MyContextMenuContext
    {
        private static readonly MyStringId WebbingInventory = MyStringId.GetOrCompute("BaseCampWebbingInventory");
        private static readonly MyStringId NearbySquads = MyStringId.GetOrCompute("BaseCampNearbySquads");
        private static readonly MyStringId SelectedLeader = MyStringId.GetOrCompute("BaseCampSelectedLeader");
        private static readonly MyStringId SelectedMembers = MyStringId.GetOrCompute("BaseCampSelectedMembers");
        private static readonly MyStringId SelectedSquadVersion = MyStringId.GetOrCompute("BaseCampSelectedSquadVersion");

        private SiBaseCampMenuSession _session;
        private long _selectedSquadVersion;

        public override void Init(object[] contextParams)
        {
            _session = contextParams != null && contextParams.Length > 0
                ? contextParams[0] as SiBaseCampMenuSession
                : null;

            m_dataSources.Add(WebbingInventory, new StringListDataSource(
                () => _session?.GetWebbingInventoryLines(),
                null,
                null));
            m_dataSources.Add(NearbySquads, new SquadListDataSource(
                () => _session?.GetNearbyAlliedSquads(),
                () => _session?.GetSelectedRefundSquad()?.Leader,
                SelectRefundSquad));
            m_dataSources.Add(SelectedLeader, SimpleDataSources.SimpleReadOnly(
                () => _session?.SelectedLeaderLabel() ?? "Squad leader: none"));
            m_dataSources.Add(SelectedMembers, new StringListDataSource(
                () => _session?.SelectedMemberLines(),
                null,
                null));
            m_dataSources.Add(SelectedSquadVersion, SimpleDataSources.SimpleReadOnly(
                () => _selectedSquadVersion));
        }

        public void OpenInventory()
        {
            _session?.OpenInventory();
        }

        public void BaseCampSpawnAiSquad()
        {
            _session?.SpawnAiSquad();
        }

        public void BaseCampSpawnPlayerSquad()
        {
            _session?.SpawnPlayerSquad();
        }

        public void BaseCampRefundSquad()
        {
            _session?.RefundSquad();
        }

        private void SelectRefundSquad(SiSquadLeaderKey leader)
        {
            _session?.SelectRefundSquad(leader);
            _selectedSquadVersion++;
        }

        private sealed class StringListDataSource : IMyListboxDataSource
        {
            private readonly Func<List<string>> _getItems;
            private readonly Func<string> _getSelected;
            private readonly Action<string> _setSelected;

            public StringListDataSource(
                Func<List<string>> getItems,
                Func<string> getSelected,
                Action<string> setSelected)
            {
                _getItems = getItems;
                _getSelected = getSelected;
                _setSelected = setSelected;
            }

            public void Close()
            {
            }

            public void GetItems(List<MyTuple<MyStringId, string>> output)
            {
                output.Clear();
                var items = _getItems?.Invoke();
                if (items == null)
                    return;
                for (var i = 0; i < items.Count; i++)
                    output.Add(new MyTuple<MyStringId, string>(MyStringId.NullOrEmpty, items[i]));
            }

            public void GetItemSelection(List<bool> output)
            {
                output.Clear();
                var items = _getItems?.Invoke();
                var selected = _getSelected?.Invoke();
                if (items == null)
                    return;
                for (var i = 0; i < items.Count; i++)
                    output.Add(selected != null && string.Equals(items[i], selected, StringComparison.Ordinal));
            }

            public void SetItemSelection(List<bool> input)
            {
                if (input == null || _setSelected == null)
                    return;
                var items = _getItems?.Invoke();
                if (items == null)
                    return;
                var count = Math.Min(input.Count, items.Count);
                for (var i = 0; i < count; i++)
                    if (input[i])
                    {
                        _setSelected(items[i]);
                        return;
                    }
            }
        }

        private sealed class SquadListDataSource : IMyListboxDataSource
        {
            private readonly Func<List<SiSquadView>> _getItems;
            private readonly Func<SiSquadLeaderKey?> _getSelected;
            private readonly Action<SiSquadLeaderKey> _setSelected;

            public SquadListDataSource(
                Func<List<SiSquadView>> getItems,
                Func<SiSquadLeaderKey?> getSelected,
                Action<SiSquadLeaderKey> setSelected)
            {
                _getItems = getItems;
                _getSelected = getSelected;
                _setSelected = setSelected;
            }

            public void Close()
            {
            }

            public void GetItems(List<MyTuple<MyStringId, string>> output)
            {
                output.Clear();
                var items = _getItems?.Invoke();
                if (items == null)
                    return;
                for (var i = 0; i < items.Count; i++)
                {
                    var squad = items[i];
                    output.Add(new MyTuple<MyStringId, string>(
                        MyStringId.NullOrEmpty,
                        squad.ArmyName + " - " + squad.LeaderName + " (" + squad.Members.Count + ")"));
                }
            }

            public void GetItemSelection(List<bool> output)
            {
                output.Clear();
                var items = _getItems?.Invoke();
                var selected = _getSelected?.Invoke();
                if (items == null)
                    return;
                for (var i = 0; i < items.Count; i++)
                    output.Add(selected.HasValue && items[i].Leader.Equals(selected.Value));
            }

            public void SetItemSelection(List<bool> input)
            {
                if (input == null || _setSelected == null)
                    return;
                var items = _getItems?.Invoke();
                if (items == null)
                    return;
                var count = Math.Min(input.Count, items.Count);
                for (var i = 0; i < count; i++)
                    if (input[i])
                    {
                        _setSelected(items[i].Leader);
                        return;
                    }
            }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiBaseCampMenuContext : MyObjectBuilder_Base
    {
    }
}
