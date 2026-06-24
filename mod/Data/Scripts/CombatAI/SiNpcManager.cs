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
        public const string SoldierDummyArchetype = "soldier-dummy";

        private readonly Dictionary<string, Func<long, MatrixD, SiNpc>> _archetypes =
            new Dictionary<string, Func<long, MatrixD, SiNpc>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<long, SiNpc> _npcs = new Dictionary<long, SiNpc>();
        private readonly List<long> _closedNpcIds = new List<long>();

        public SiNpcManager()
        {
            RegisterArchetype(SoldierDummyArchetype, (id, transform) => new SiSoldierDummyNpc(id, transform));
        }

        public IReadOnlyDictionary<long, SiNpc> Npcs => _npcs;

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

        public bool TrySpawn(string archetype, long entityId, in MatrixD transform, out SiNpc npc)
        {
            if (_npcs.TryGetValue(entityId, out npc))
                return string.Equals(npc.Archetype, archetype, StringComparison.OrdinalIgnoreCase);
            if (!_archetypes.TryGetValue(archetype, out var factory))
                return false;

            npc = factory(entityId, transform);
            if (npc == null || !npc.TryActivate())
            {
                npc = null;
                return false;
            }

            _npcs.Add(entityId, npc);
            return true;
        }

        public void Update(long elapsedMilliseconds)
        {
            _closedNpcIds.Clear();
            foreach (var entry in _npcs)
                if (!entry.Value.Update(elapsedMilliseconds))
                    _closedNpcIds.Add(entry.Key);

            foreach (var id in _closedNpcIds)
                _npcs.Remove(id);
        }

        public void CloseAll()
        {
            foreach (var npc in _npcs.Values)
                npc.Close();
            _npcs.Clear();
            _closedNpcIds.Clear();
        }
    }

    /// <summary>
    /// Base for a custom NPC controlled entirely by this mod.  The entity is a
    /// model container; sensing, decisions, movement, combat, and other systems
    /// can be layered onto the update and lifecycle hooks later.
    /// </summary>
    public abstract class SiNpc
    {
        protected SiNpc(long entityId, in MatrixD transform)
        {
            EntityId = entityId;
            Transform = transform;
        }

        public long EntityId { get; }
        public MatrixD Transform { get; protected set; }
        public MyEntity Entity { get; private set; }

        public abstract string Archetype { get; }
        protected abstract MyDefinitionId EntityDefinition { get; }

        internal bool TryActivate()
        {
            if (Entity != null)
                return !Entity.Closed && !Entity.MarkedForClose;
            if (MySession.Static?.Scene == null)
                return false;

            MyEntity entity = null;
            try
            {
                entity = new MyEntity
                {
                    EntityId = EntityId,
                    Name = $"SiNpc_{Archetype}_{EntityId}",
                    Save = false,
                };
                entity.Init(EntityDefinition);
                entity.Save = false;
                entity.WorldMatrix = Transform;

                MySession.Static.Scene.ActivateEntity(entity);
                if (entity.Render != null)
                {
                    entity.Render.Visible = true;
                    entity.Render.DrawOutsideViewDistance = true;
                    entity.Render.SkipIfTooSmall = false;
                }

                Entity = entity;
                OnActivated();
                return true;
            }
            catch (Exception exception)
            {
                MyAPIGateway.Utilities?.ShowNotification(
                    $"Failed to create {Archetype}: {exception.Message}", 5000);
                entity?.Close();
                return false;
            }
        }

        internal bool Update(long elapsedMilliseconds)
        {
            if (Entity == null || Entity.Closed || Entity.MarkedForClose)
                return false;

            OnUpdate(elapsedMilliseconds);
            if (Entity == null || Entity.Closed || Entity.MarkedForClose)
                return false;
            Transform = Entity.WorldMatrix;
            return true;
        }

        public void Close()
        {
            if (Entity == null)
                return;

            OnClosing();
            Entity.Close();
            Entity = null;
        }

        protected virtual void OnActivated()
        {
        }

        protected virtual void OnUpdate(long elapsedMilliseconds)
        {
        }

        protected virtual void OnClosing()
        {
        }
    }

    /// <summary>
    /// First framework proof: a visible soldier which intentionally remains idle.
    /// </summary>
    public sealed class SiSoldierDummyNpc : SiNpc
    {
        private static readonly MyDefinitionId DefinitionId =
            new MyDefinitionId(typeof(MyObjectBuilder_EntityBase), "SiSoldierDummy");

        public SiSoldierDummyNpc(long entityId, in MatrixD transform)
            : base(entityId, transform)
        {
        }

        public override string Archetype => SiNpcManager.SoldierDummyArchetype;
        protected override MyDefinitionId EntityDefinition => DefinitionId;

        // Intentionally idle.  The first real behavior can override OnUpdate here.
    }
}
