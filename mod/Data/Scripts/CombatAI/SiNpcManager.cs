using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.ObjectBuilders;
using VRage.Session;
using VRageMath;

namespace Si.UtilityAI
{
    /// <summary>
    /// Owns the lightweight entities used by the custom NPC framework.  NPCs are
    /// intentionally not game bots; their behavior is driven by <see cref="SiNpc.Update"/>.
    /// </summary>
    public sealed class SiNpcManager
    {
        public const string SoldierArchetype = "us-rifle-trooper";
        public const string EnemyTrooperArchetype = "enemy-trooper";
        public const string EnemyFactionTag = "BARB";

        private readonly Dictionary<string, SiNpcArchetypeRecord> _archetypes =
            new Dictionary<string, SiNpcArchetypeRecord>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<long, SiNpc> _npcs = new Dictionary<long, SiNpc>();
        private readonly List<long> _behaviorNpcIds = new List<long>();
        private readonly List<long> _closedNpcIds = new List<long>();
        private int _behaviorNpcIndex;

        public SiNpcManager()
        {
            LoadArchetypes();
        }

        public IReadOnlyDictionary<long, SiNpc> Npcs => _npcs;

        public event Action<long, Vector3D> WaypointSet;
        public event Action<long> WaypointCleared;
        public event Action<long, Vector3D, string> NpcSpoke;

        private void LoadArchetypes()
        {
            foreach (var container in MyDefinitionManager.GetOfType<MyContainerDefinition>())
            {
                if (container == null || container.Id.TypeId != typeof(MyObjectBuilder_EntityBase))
                    continue;

                var archetypeDefinition = FindArchetypeDefinition(container);
                if (archetypeDefinition == null)
                    continue;

                var archetype = string.IsNullOrWhiteSpace(archetypeDefinition.Archetype)
                    ? container.Id.SubtypeName
                    : archetypeDefinition.Archetype;
                if (_archetypes.ContainsKey(archetype))
                    continue;

                _archetypes.Add(
                    archetype,
                    new SiNpcArchetypeRecord(archetype, container.Id, archetypeDefinition));
            }
        }

        private static SiNpcArchetypeComponentDefinition FindArchetypeDefinition(MyContainerDefinition container)
        {
            if (container?.Components == null)
                return null;

            foreach (var component in container.Components)
                if (component.Definition is SiNpcArchetypeComponentDefinition archetypeDefinition)
                    return archetypeDefinition;
            return null;
        }

        public bool IsKnownArchetype(string name) =>
            !string.IsNullOrWhiteSpace(name) && _archetypes.ContainsKey(name);

        internal bool TryGetArchetype(string name, out SiNpcArchetypeRecord definition)
        {
            definition = null;
            return !string.IsNullOrWhiteSpace(name) && _archetypes.TryGetValue(name, out definition);
        }

        internal bool IsHostileToSpawner(string archetype)
        {
            SiNpcArchetypeRecord definition;
            return TryGetArchetype(archetype, out definition)
                   && definition.SpawnDisposition == SiNpcSpawnDisposition.HostileToSpawner;
        }

        public string KnownArchetypesText
        {
            get
            {
                var names = new List<string>();
                foreach (var entry in _archetypes)
                    if (!entry.Value.HiddenFromCommands)
                        names.Add(entry.Key);
                names.Sort(StringComparer.OrdinalIgnoreCase);
                return string.Join(", ", names);
            }
        }

        public bool Close(long entityId)
        {
            SiNpc npc;
            if (!_npcs.TryGetValue(entityId, out npc))
                return false;

            npc.Close();
            _npcs.Remove(entityId);
            _closedNpcIds.Remove(entityId);
            return true;
        }

        /// <summary>
        /// Assigns a world-space steering target to an NPC which supports
        /// waypoint locomotion.  Behavior systems should use this manager API so
        /// the session component can replicate the command to clients.
        /// </summary>
        public bool TrySetWaypoint(long entityId, in Vector3D waypoint)
        {
            if (!ApplyWaypoint(entityId, waypoint))
                return false;

            WaypointSet?.Invoke(entityId, waypoint);
            return true;
        }

        public bool TryClearWaypoint(long entityId)
        {
            if (!ApplyClearWaypoint(entityId))
                return false;

            WaypointCleared?.Invoke(entityId);
            return true;
        }

        public bool TrySpeak(long entityId, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;
            if (!_npcs.TryGetValue(entityId, out var npc))
                return false;

            var entity = npc.Entity;
            if (entity == null || entity.Closed || entity.MarkedForClose)
                return false;

            NpcSpoke?.Invoke(entityId, entity.WorldMatrix.Translation, message.Trim());
            return true;
        }

        internal bool ApplyWaypoint(long entityId, in Vector3D waypoint)
        {
            if (!_npcs.TryGetValue(entityId, out var npc) || !(npc is ISiWaypointMover mover))
                return false;

            mover.SetWaypoint(waypoint);
            return true;
        }

        internal bool ApplyClearWaypoint(long entityId)
        {
            if (!_npcs.TryGetValue(entityId, out var npc) || !(npc is ISiWaypointMover mover))
                return false;

            mover.ClearWaypoint();
            return true;
        }

        public bool TrySpawn(string archetype, long entityId, in MatrixD transform, out SiNpc npc)
        {
            if (_npcs.TryGetValue(entityId, out npc))
                return string.Equals(npc.Archetype, archetype, StringComparison.OrdinalIgnoreCase);
            SiNpcArchetypeRecord definition;
            if (!_archetypes.TryGetValue(archetype, out definition))
                return false;

            npc = new SiDataDrivenNpc(definition, entityId, transform);
            npc.AttachManager(this);
            _npcs.Add(entityId, npc);
            if (!npc.TryActivate())
            {
                _npcs.Remove(entityId);
                npc = null;
                return false;
            }

            return true;
        }

        public void Update(long elapsedMilliseconds)
        {
            _behaviorNpcIds.Clear();
            _closedNpcIds.Clear();
            foreach (var entry in _npcs)
                if (!entry.Value.UpdateFrame(elapsedMilliseconds))
                {
                    entry.Value.Close();
                    _closedNpcIds.Add(entry.Key);
                }
                else
                    _behaviorNpcIds.Add(entry.Key);

            ProcessBehaviorEngineStep();

            foreach (var id in _closedNpcIds)
                _npcs.Remove(id);
        }

        private void ProcessBehaviorEngineStep()
        {
            if (_behaviorNpcIds.Count == 0)
            {
                _behaviorNpcIndex = 0;
                return;
            }

            if (_behaviorNpcIndex >= _behaviorNpcIds.Count)
                _behaviorNpcIndex = 0;

            var entityId = _behaviorNpcIds[_behaviorNpcIndex];
            _behaviorNpcIndex = (_behaviorNpcIndex + 1) % _behaviorNpcIds.Count;
            if (_npcs.TryGetValue(entityId, out var npc))
                npc.ProcessBehaviorUpdate();
        }

        public void CloseAll(bool deleteDiplomaticIdentities = true)
        {
            foreach (var npc in _npcs.Values)
                npc.Close(deleteDiplomaticIdentities);
            _npcs.Clear();
            _behaviorNpcIds.Clear();
            _behaviorNpcIndex = 0;
            _closedNpcIds.Clear();
        }
    }
}
