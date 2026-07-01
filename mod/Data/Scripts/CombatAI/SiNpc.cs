using System;
using Sandbox.Game.Entities.Entity.Stats;
using Sandbox.Game.Entities.Entity.Stats.Extensions;
using Sandbox.Game.Players;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.ObjectBuilders;
using VRage.Session;
using VRage.Utils;
using VRageMath;

namespace Si.UtilityAI
{
    /// <summary>
    /// Base for a custom NPC controlled entirely by this mod.  The entity is a
    /// model container; sensing, decisions, movement, combat, and other systems
    /// can be layered onto the update and lifecycle hooks later.
    /// </summary>
    public abstract class SiNpc
    {
        private static readonly MyStringHash HealthStat = MyStringHash.GetOrCompute("Health");

        private SiNpcManager _manager;
        private SiUtilityBrainComponent _utilityBrain;
        private bool _deleteDiplomaticIdentityOnClose;
        private bool _deathStarted;
        private long _pendingBehaviorElapsedMilliseconds;

        protected SiNpc(long entityId, in MatrixD transform)
        {
            EntityId = entityId;
            Transform = transform;
        }

        public long EntityId { get; }
        public long DiplomaticIdentityId { get; private set; }
        public MatrixD Transform { get; protected set; }
        public MyEntity Entity { get; private set; }
        public bool IsDead
        {
            get
            {
                var stats = ResolveStatComponent();
                return stats?.IsDead() ?? false;
            }
        }

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
                var objectBuilder = MyObjectBuilderSerializer.CreateNewObject(EntityDefinition) as MyObjectBuilder_EntityBase;
                if (objectBuilder == null)
                    throw new InvalidOperationException(
                        $"Definition '{EntityDefinition}' could not create a character object builder.");

                objectBuilder.EntityId = EntityId;
                objectBuilder.PositionAndOrientation = new MyPositionAndOrientation(Transform);
                objectBuilder.PersistentFlags |= MyPersistentEntityFlags2.InScene;

                entity = Sandbox.Game.Entities.MyEntities.CreateFromObjectBuilderAndAdd(objectBuilder);
                if (entity == null)
                    throw new InvalidOperationException(
                        $"Failed to create character '{EntityDefinition.SubtypeName}'.");

                entity.Name = $"SiNpc_{Archetype}_{EntityId}";
                entity.Save = false;
                if (entity.Render != null)
                {
                    entity.Render.Visible = true;
                    entity.Render.DrawOutsideViewDistance = true;
                    entity.Render.SkipIfTooSmall = false;
                }

                Entity = entity;
                _utilityBrain = Entity.Components.Get<SiUtilityBrainComponent>();
                _utilityBrain?.Bind(this);
                OnActivated();
                return true;
            }
            catch (Exception exception)
            {
                MyAPIGateway.Utilities?.ShowNotification(
                    $"Failed to create {Archetype}: {exception.Message}", 5000);
                _utilityBrain?.Unbind();
                _utilityBrain = null;
                _deathStarted = false;
                entity?.Close();
                return false;
            }
        }

        internal bool UpdateFrame(long elapsedMilliseconds)
        {
            if (Entity == null || Entity.Closed || Entity.MarkedForClose)
                return false;

            if (IsDead)
            {
                if (!_deathStarted)
                {
                    _deathStarted = true;
                    _utilityBrain?.Unbind();
                    _utilityBrain = null;
                    OnKilled();
                }
            }
            else
            {
                _pendingBehaviorElapsedMilliseconds += Math.Max(0, elapsedMilliseconds);
                _utilityBrain?.SetDecisionMakingEnabled(
                    SiNpcSessionComponent.Instance?.UtilityDecisionMakingEnabled ?? true);
                OnUpdate(elapsedMilliseconds);
            }

            if (Entity == null || Entity.Closed || Entity.MarkedForClose)
                return false;
            Transform = Entity.WorldMatrix;
            return true;
        }

        internal void ProcessBehaviorUpdate()
        {
            if (Entity == null || Entity.Closed || Entity.MarkedForClose || IsDead)
                return;

            var utilityBrain = _utilityBrain;
            if (utilityBrain == null)
            {
                _pendingBehaviorElapsedMilliseconds = 0;
                return;
            }

            utilityBrain.SetDecisionMakingEnabled(
                SiNpcSessionComponent.Instance?.UtilityDecisionMakingEnabled ?? true);
            utilityBrain.UpdateDecision(_pendingBehaviorElapsedMilliseconds);
            _pendingBehaviorElapsedMilliseconds = 0;
        }

        public bool TryGetHealth(out float current, out float max)
        {
            current = 0;
            max = 0;

            var stats = ResolveStatComponent();
            if (stats == null)
                return false;

            MyEntityStat health;
            if (!stats.TryGetStat(HealthStat, out health) || health == null)
                return false;

            current = health.Current;
            max = health.Max;
            return max > 0;
        }

        public void Close(bool deleteDiplomaticIdentity = true)
        {
            if (Entity == null)
                return;

            _utilityBrain?.Unbind();
            _utilityBrain = null;
            _pendingBehaviorElapsedMilliseconds = 0;
            OnClosing();
            _deathStarted = false;
            if (deleteDiplomaticIdentity)
                DeleteDiplomaticIdentity();
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

        protected virtual void OnKilled()
        {
        }

        private MyEntityStatComponent ResolveStatComponent()
        {
            return Entity?.Components.Get<MyEntityStatComponent>();
        }

        internal void AttachManager(SiNpcManager manager)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        internal bool TrySetWaypoint(in Vector3D waypoint) =>
            _manager?.TrySetWaypoint(EntityId, waypoint) ?? false;

        internal bool TryClearWaypoint() =>
            _manager?.TryClearWaypoint(EntityId) ?? false;

        internal bool TrySpeak(string message) =>
            _manager?.TrySpeak(EntityId, message) ?? false;

        internal void SetDiplomaticIdentity(MyIdentity identity, bool deleteOnClose)
        {
            DiplomaticIdentityId = identity?.Id ?? 0;
            _deleteDiplomaticIdentityOnClose = deleteOnClose && DiplomaticIdentityId != 0;
        }

        private void DeleteDiplomaticIdentity()
        {
            if (!_deleteDiplomaticIdentityOnClose || DiplomaticIdentityId == 0)
                return;

            try
            {
                var identity = MyIdentities.Static?.GetIdentity(DiplomaticIdentityId);
                if (identity != null)
                    MyIdentities.Static.DeleteIdentity(identity);
            }
            catch
            {
            }

            DiplomaticIdentityId = 0;
            _deleteDiplomaticIdentityOnClose = false;
        }
    }
}
