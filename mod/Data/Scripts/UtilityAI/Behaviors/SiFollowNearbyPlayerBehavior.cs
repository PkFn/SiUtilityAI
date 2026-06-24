using System;
using System.ComponentModel;
using System.Xml.Serialization;
using Sandbox.Game.Players;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRageMath;

namespace Si.UtilityAI
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiFollowNearbyPlayerBehavior : MyObjectBuilder_EntityComponent
    {
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiFollowNearbyPlayerBehaviorDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        [DefaultValue(40f)]
        public float SearchRadius = 40f;

        [DefaultValue(2f)]
        public float StopDistance = 2f;

        [DefaultValue(3f)]
        public float ResumeDistance = 3f;

        [DefaultValue(0.75f)]
        public float WaypointRefreshDistance = 0.75f;

        [DefaultValue(0.2f)]
        public float BaseScore = 0.2f;

        [DefaultValue(0.8f)]
        public float DistanceScore = 0.8f;

        [DefaultValue(1.5f)]
        public float DistanceExponent = 1.5f;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiFollowNearbyPlayerBehaviorDefinition))]
    public class SiFollowNearbyPlayerBehaviorDefinition : MyEntityComponentDefinition
    {
        public float SearchRadius { get; private set; }
        public float StopDistance { get; private set; }
        public float ResumeDistance { get; private set; }
        public float WaypointRefreshDistance { get; private set; }
        public float BaseScore { get; private set; }
        public float DistanceScore { get; private set; }
        public float DistanceExponent { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiFollowNearbyPlayerBehaviorDefinition)builder;

            SearchRadius = Math.Max(0.1f, ob.SearchRadius);
            StopDistance = Math.Max(0, Math.Min(ob.StopDistance, SearchRadius));
            ResumeDistance = Math.Max(StopDistance, Math.Min(ob.ResumeDistance, SearchRadius));
            WaypointRefreshDistance = Math.Max(0.05f, ob.WaypointRefreshDistance);
            BaseScore = Math.Max(0, ob.BaseScore);
            DistanceScore = Math.Max(0, ob.DistanceScore);
            DistanceExponent = Math.Max(0.01f, ob.DistanceExponent);
        }
    }

    /// <summary>
    /// Scores the nearest controlled player and follows it while preserving a
    /// configurable personal-space band.  All tuning lives in its component
    /// definition, so the behavior can be attached to another utility entity.
    /// </summary>
    [MyComponent(typeof(MyObjectBuilder_SiFollowNearbyPlayerBehavior))]
    [MyDefinitionRequired(typeof(SiFollowNearbyPlayerBehaviorDefinition))]
    public class SiFollowNearbyPlayerBehaviorComponent : MyEntityComponent, ISiUtilityBehavior
    {
        private SiFollowNearbyPlayerBehaviorDefinition _definition;
        private MyEntity _target;
        private long _followedEntityId;
        private bool _moving;
        private bool _hasIssuedWaypoint;
        private Vector3D _lastIssuedWaypoint;

        public string BehaviorName => DefinitionId.ToString();

        public override bool IsSerialized => false;

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            _definition = (SiFollowNearbyPlayerBehaviorDefinition)definition;
        }

        float ISiUtilityBehavior.Evaluate(SiUtilityContext context)
        {
            _target = FindNearestPlayer(context.Position, out var distance);
            if (_target == null)
                return 0;

            var scoreRange = Math.Max(0.1, _definition.SearchRadius - _definition.StopDistance);
            var normalizedDistance = MathHelper.Clamp(
                (float)((distance - _definition.StopDistance) / scoreRange),
                0,
                1);
            return _definition.BaseScore
                   + _definition.DistanceScore
                   * (float)Math.Pow(normalizedDistance, _definition.DistanceExponent);
        }

        void ISiUtilityBehavior.Begin(SiUtilityContext context)
        {
            _followedEntityId = 0;
            _moving = false;
            _hasIssuedWaypoint = false;
        }

        void ISiUtilityBehavior.Tick(SiUtilityContext context, long elapsedMilliseconds)
        {
            if (!IsValidTarget(_target))
            {
                StopMoving(context);
                return;
            }

            if (_followedEntityId != _target.EntityId)
            {
                _followedEntityId = _target.EntityId;
                _moving = false;
                _hasIssuedWaypoint = false;
            }

            var targetPosition = _target.WorldMatrix.Translation;
            var distanceSquared = Vector3D.DistanceSquared(context.Position, targetPosition);
            if (distanceSquared <= _definition.StopDistance * _definition.StopDistance)
            {
                StopMoving(context);
                return;
            }

            if (!_moving
                && distanceSquared < _definition.ResumeDistance * _definition.ResumeDistance)
            {
                if (context.HasWaypoint)
                    context.TryClearWaypoint();
                return;
            }

            _moving = true;
            var refreshDistanceSquared = _definition.WaypointRefreshDistance
                                         * _definition.WaypointRefreshDistance;
            if (!_hasIssuedWaypoint
                || !context.HasWaypoint
                || Vector3D.DistanceSquared(_lastIssuedWaypoint, targetPosition) >= refreshDistanceSquared)
            {
                if (context.TrySetWaypoint(targetPosition))
                {
                    _lastIssuedWaypoint = targetPosition;
                    _hasIssuedWaypoint = true;
                }
            }
        }

        void ISiUtilityBehavior.End(SiUtilityContext context)
        {
            StopMoving(context);
            _target = null;
            _followedEntityId = 0;
            _hasIssuedWaypoint = false;
        }

        private MyEntity FindNearestPlayer(in Vector3D origin, out double nearestDistance)
        {
            MyEntity nearest = null;
            var nearestDistanceSquared = (double)(_definition.SearchRadius * _definition.SearchRadius);
            if (MyPlayers.Static == null)
            {
                nearestDistance = 0;
                return null;
            }

            foreach (var entry in MyPlayers.Static.GetAllPlayers())
            {
                var controlled = entry.Value?.ControlledEntity;
                if (!IsValidTarget(controlled) || controlled == Entity)
                    continue;

                var distanceSquared = Vector3D.DistanceSquared(
                    origin,
                    controlled.WorldMatrix.Translation);
                if (distanceSquared > nearestDistanceSquared)
                    continue;

                nearest = controlled;
                nearestDistanceSquared = distanceSquared;
            }

            nearestDistance = Math.Sqrt(nearestDistanceSquared);
            return nearest;
        }

        private static bool IsValidTarget(MyEntity target) =>
            target != null && target.InScene && !target.Closed && !target.MarkedForClose;

        private void StopMoving(SiUtilityContext context)
        {
            _moving = false;
            _hasIssuedWaypoint = false;
            if (context.HasWaypoint)
                context.TryClearWaypoint();
        }
    }
}
