using System;
using System.Xml.Serialization;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Entities.Gravity;
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
    public class MyObjectBuilder_SiTakePlainViewBehavior : MyObjectBuilder_EntityComponent
    {
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiTakePlainViewBehaviorDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public float MinimumDistanceFromLeader = 4f;
        public float MaximumDistanceFromLeader = 10f;
        public float ArrivalDistance = 1.1f;
        public float WaypointRefreshDistance = 0.75f;
        public float RepositionLeaderDistance = 18f;
        public float BaseScore = 0.35f;
        public float DistanceScore = 0.65f;
        public float DistanceExponent = 1f;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiTakePlainViewBehaviorDefinition))]
    public class SiTakePlainViewBehaviorDefinition : MyEntityComponentDefinition
    {
        public float MinimumDistanceFromLeader { get; private set; }
        public float MaximumDistanceFromLeader { get; private set; }
        public float ArrivalDistance { get; private set; }
        public float WaypointRefreshDistance { get; private set; }
        public float RepositionLeaderDistance { get; private set; }
        public float BaseScore { get; private set; }
        public float DistanceScore { get; private set; }
        public float DistanceExponent { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiTakePlainViewBehaviorDefinition)builder;
            MinimumDistanceFromLeader = Math.Max(0.5f, ob.MinimumDistanceFromLeader);
            MaximumDistanceFromLeader = Math.Max(MinimumDistanceFromLeader, ob.MaximumDistanceFromLeader);
            ArrivalDistance = Math.Max(0.1f, ob.ArrivalDistance);
            WaypointRefreshDistance = Math.Max(0.05f, ob.WaypointRefreshDistance);
            RepositionLeaderDistance = Math.Max(MaximumDistanceFromLeader, ob.RepositionLeaderDistance);
            BaseScore = Math.Max(0, ob.BaseScore);
            DistanceScore = Math.Max(0, ob.DistanceScore);
            DistanceExponent = Math.Max(0.01f, ob.DistanceExponent);
        }
    }

    [MyComponent(typeof(MyObjectBuilder_SiTakePlainViewBehavior))]
    [MyDefinitionRequired(typeof(SiTakePlainViewBehaviorDefinition))]
    public class SiTakePlainViewBehaviorComponent : MyEntityComponent, ISiUtilityBehavior
    {
        private SiTakePlainViewBehaviorDefinition _definition;
        private Vector3D _plainViewPosition;
        private bool _hasPlainViewPosition;
        private long _activeCombatToken = long.MinValue;
        private int _repositionIndex;

        public string BehaviorName => DefinitionId.ToString();

        public override bool IsSerialized => false;

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            _definition = (SiTakePlainViewBehaviorDefinition)definition;
        }

        float ISiUtilityBehavior.Evaluate(SiUtilityContext context)
        {
            var session = SiNpcSessionComponent.Instance;
            if (context?.Agent == null || session == null)
                return 0;

            if (session.GetCombatStance(context.Agent) != SiSquadCombatStance.Combat)
            {
                ResetState(context);
                context.Agent.ClearCombatMovementRole();
                return 0;
            }

            var combatToken = session.GetCombatEntryToken(context.Agent);
            if (context.Agent.GetCombatMovementRole(combatToken) != SiCombatMovementRole.PlainView)
            {
                ResetState(context);
                return 0;
            }

            if (_activeCombatToken != combatToken)
            {
                _activeCombatToken = combatToken;
                _hasPlainViewPosition = false;
                _repositionIndex = 0;
                context.TrySetCrouch(false);
            }

            var leaderPosition = ResolveLeaderPosition(session, context);
            var leaderDistance = Vector3D.Distance(context.Position, leaderPosition);
            if (!_hasPlainViewPosition)
                AssignPlainViewPosition(context, leaderPosition);
            else if (leaderDistance >= _definition.RepositionLeaderDistance && HasReachedDestination(context))
                AssignPlainViewPosition(context, leaderPosition);

            if (!_hasPlainViewPosition)
                return 0;

            if (!HasReachedDestination(context))
                return 1f;

            if (leaderDistance < _definition.RepositionLeaderDistance)
                return 0;

            var scoreRange = Math.Max(0.1f, _definition.RepositionLeaderDistance - _definition.MaximumDistanceFromLeader);
            var normalizedDistance = MathHelper.Clamp(
                (float)((leaderDistance - _definition.MaximumDistanceFromLeader) / scoreRange),
                0,
                1);
            return _definition.BaseScore
                   + _definition.DistanceScore
                   * (float)Math.Pow(normalizedDistance, _definition.DistanceExponent);
        }

        void ISiUtilityBehavior.Begin(SiUtilityContext context)
        {
            ApplyMovement(context);
        }

        void ISiUtilityBehavior.Tick(SiUtilityContext context, long elapsedMilliseconds)
        {
            ApplyMovement(context);
        }

        void ISiUtilityBehavior.End(SiUtilityContext context)
        {
        }

        private void ApplyMovement(SiUtilityContext context)
        {
            if (context?.Agent == null)
                return;

            if (!_hasPlainViewPosition)
            {
                context.TrySetCrouch(false);
                return;
            }

            if (HasReachedDestination(context))
            {
                context.TryClearWaypoint();
                context.TrySetCrouch(true);
                return;
            }

            context.TrySetCrouch(false);
            if (!context.HasWaypoint
                || Vector3D.DistanceSquared(context.Waypoint, _plainViewPosition)
                   > _definition.WaypointRefreshDistance * _definition.WaypointRefreshDistance)
                context.TrySetWaypoint(_plainViewPosition);
        }

        private void AssignPlainViewPosition(SiUtilityContext context, in Vector3D leaderPosition)
        {
            var up = ResolveUp(context.Position, context.Entity?.WorldMatrix.Up ?? Vector3D.Up);
            var forward = Vector3D.Reject(context.Entity?.WorldMatrix.Forward ?? Vector3D.Forward, up);
            if (forward.LengthSquared() <= 0.0001)
                forward = Vector3D.CalculatePerpendicularVector(up);
            forward.Normalize();
            var right = Vector3D.Normalize(Vector3D.Cross(forward, up));

            _repositionIndex++;
            var hash = unchecked((int)(context.EntityId ^ (_activeCombatToken * 397) ^ (_repositionIndex * 7919)));
            var normalizedAngle = (Math.Abs(hash) % 1024) / 1024d;
            var normalizedRadius = (Math.Abs(hash / 1024) % 1024) / 1024d;
            var angle = normalizedAngle * Math.PI * 2d;
            var radius = MathHelper.Lerp(
                _definition.MinimumDistanceFromLeader,
                _definition.MaximumDistanceFromLeader,
                (float)normalizedRadius);

            _plainViewPosition = leaderPosition
                                 + forward * (Math.Cos(angle) * radius)
                                 + right * (Math.Sin(angle) * radius);
            _hasPlainViewPosition = true;
        }

        private Vector3D ResolveLeaderPosition(SiNpcSessionComponent session, SiUtilityContext context)
        {
            Vector3D leaderPosition;
            return session.TryGetLeaderPosition(context.Agent, out leaderPosition)
                ? leaderPosition
                : context.Position;
        }

        private bool HasReachedDestination(SiUtilityContext context)
        {
            return _hasPlainViewPosition
                   && Vector3D.DistanceSquared(context.Position, _plainViewPosition)
                      <= _definition.ArrivalDistance * _definition.ArrivalDistance;
        }

        private void ResetState(SiUtilityContext context)
        {
            _hasPlainViewPosition = false;
            _plainViewPosition = Vector3D.Zero;
            _activeCombatToken = long.MinValue;
            _repositionIndex = 0;
            context?.TrySetCrouch(false);
        }

        private static Vector3D ResolveUp(in Vector3D position, in Vector3D fallbackUp)
        {
            var gravity = MyGravityProviderSystem.CalculateTotalGravityInPoint(position);
            if (gravity.LengthSquared() > 0.0001)
                return -Vector3D.Normalize(gravity);
            return SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(fallbackUp, Vector3D.Up);
        }
    }
}
