using System;
using Sandbox.ModAPI;
using VRage.Session;
using VRage.Game;
using VRage.Game.Entity;
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
        private SiNpcManager _manager;
        private SiUtilityBrainComponent _utilityBrain;

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
                entity?.Close();
                return false;
            }
        }

        internal bool Update(long elapsedMilliseconds)
        {
            if (Entity == null || Entity.Closed || Entity.MarkedForClose)
                return false;

            _utilityBrain?.UpdateDecision(elapsedMilliseconds);
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

            _utilityBrain?.Unbind();
            _utilityBrain = null;
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

        internal void AttachManager(SiNpcManager manager)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        internal bool TrySetWaypoint(in Vector3D waypoint) =>
            _manager?.TrySetWaypoint(EntityId, waypoint) ?? false;

        internal bool TryClearWaypoint() =>
            _manager?.TryClearWaypoint(EntityId) ?? false;
    }
}
