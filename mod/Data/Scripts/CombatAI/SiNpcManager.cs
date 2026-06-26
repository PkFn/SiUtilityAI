using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game;
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
        public const string SoldierArchetype = "trooper";
        public const string EnemyTrooperArchetype = "enemy-trooper";

        private readonly Dictionary<string, Func<long, MatrixD, SiNpc>> _archetypes =
            new Dictionary<string, Func<long, MatrixD, SiNpc>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<long, SiNpc> _npcs = new Dictionary<long, SiNpc>();
        private readonly List<long> _closedNpcIds = new List<long>();

        public SiNpcManager()
        {
            RegisterArchetype(SoldierArchetype, (id, transform) => new SiTrooperNpc(id, transform));
            RegisterArchetype(EnemyTrooperArchetype, (id, transform) => new SiEnemyTrooperNpc(id, transform));
        }

        public IReadOnlyDictionary<long, SiNpc> Npcs => _npcs;

        public event Action<long, Vector3D> WaypointSet;
        public event Action<long> WaypointCleared;
        public event Action<long, Vector3D, string> NpcSpoke;

        /// <summary>
        /// Adds an NPC kind to the manager.  Future behaviors only need a new
        /// <see cref="SiNpc"/> implementation and one registration call.
        /// </summary>
        public void RegisterArchetype(string name, Func<long, MatrixD, SiNpc> factory)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("An NPC archetype needs a name.", nameof(name));
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));
            if (_archetypes.ContainsKey(name))
                throw new ArgumentException($"NPC archetype '{name}' is already registered.", nameof(name));

            _archetypes.Add(name, factory);
        }

        public bool IsKnownArchetype(string name) =>
            !string.IsNullOrWhiteSpace(name) && _archetypes.ContainsKey(name);

        public string KnownArchetypesText
        {
            get
            {
                var names = new List<string>(_archetypes.Keys);
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
            if (!_archetypes.TryGetValue(archetype, out var factory))
                return false;

            npc = factory(entityId, transform);
            if (npc == null)
            {
                npc = null;
                return false;
            }

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
            _closedNpcIds.Clear();
            foreach (var entry in _npcs)
                if (!entry.Value.Update(elapsedMilliseconds))
                {
                    entry.Value.Close();
                    _closedNpcIds.Add(entry.Key);
                }

            foreach (var id in _closedNpcIds)
                _npcs.Remove(id);
        }

        public void CloseAll(bool deleteDiplomaticIdentities = true)
        {
            foreach (var npc in _npcs.Values)
                npc.Close(deleteDiplomaticIdentities);
            _npcs.Clear();
            _closedNpcIds.Clear();
        }
    }
}
