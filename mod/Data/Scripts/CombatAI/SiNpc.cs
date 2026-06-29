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
        private MyEntityStatComponent _stats;
        private bool _deleteDiplomaticIdentityOnClose;
        private bool _deathStarted;

        protected SiNpc(long entityId, in MatrixD transform)
        {
            EntityId = entityId;
            Transform = transform;
        }

        public long EntityId { get; }
        public long DiplomaticIdentityId { get; private set; }
        public MatrixD Transform { get; protected set; }
        public MyEntity Entity { get; private set; }
        public bool IsDead => _stats?.IsDead() ?? false;

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
                var objectBuilder = new MyObjectBuilder_EntityBase
                {
                    EntityId = EntityId,
                    EntityDefinitionId = EntityDefinition,
                    PositionAndOrientation = new MyPositionAndOrientation(Transform),
                };

                entity = MySession.Static.Scene.LoadEntity(objectBuilder, activate: true);
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
                _stats = Entity.Components.Get<MyEntityStatComponent>();
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
                _stats = null;
                _deathStarted = false;
                entity?.Close();
                return false;
            }
        }

        internal bool Update(long elapsedMilliseconds)
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
                _utilityBrain?.SetDecisionMakingEnabled(
                    SiNpcSessionComponent.Instance?.UtilityDecisionMakingEnabled ?? true);
                _utilityBrain?.UpdateDecision(elapsedMilliseconds);
                OnUpdate(elapsedMilliseconds);
            }

            if (Entity == null || Entity.Closed || Entity.MarkedForClose)
                return false;
            Transform = Entity.WorldMatrix;
            return true;
        }

        public bool TryGetHealth(out float current, out float max)
        {
            current = 0;
            max = 0;

            var stats = _stats;
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
            OnClosing();
            _stats = null;
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
