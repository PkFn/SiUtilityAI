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
using VRage.ModAPI;
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
        public float ThreatFrontExclusionAngleDegrees = 90f;
        public float ArrivalDistance = 1.1f;
        public float WaypointRefreshDistance = 0.75f;
        public float RepositionLeaderDistance = 18f;
        public float TravelScore = 1f;
        public float BaseScore = 0.35f;
        public float DistanceScore = 0.65f;
        public float DistanceExponent = 1f;
        public float MinimumCachedPositionDistance = 2f;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiTakePlainViewBehaviorDefinition))]
    public class SiTakePlainViewBehaviorDefinition : MyEntityComponentDefinition
    {
        public float MinimumDistanceFromLeader { get; private set; }
        public float MaximumDistanceFromLeader { get; private set; }
        public float ThreatFrontExclusionAngleDegrees { get; private set; }
        public float ArrivalDistance { get; private set; }
        public float WaypointRefreshDistance { get; private set; }
        public float RepositionLeaderDistance { get; private set; }
        public float TravelScore { get; private set; }
        public float BaseScore { get; private set; }
        public float DistanceScore { get; private set; }
        public float DistanceExponent { get; private set; }
        public float MinimumCachedPositionDistance { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiTakePlainViewBehaviorDefinition)builder;
            MinimumDistanceFromLeader = Math.Max(0.5f, ob.MinimumDistanceFromLeader);
            MaximumDistanceFromLeader = Math.Max(MinimumDistanceFromLeader, ob.MaximumDistanceFromLeader);
            ThreatFrontExclusionAngleDegrees = (float)SiThreatSectorHelper.ClampFrontExclusionAngleDegrees(ob.ThreatFrontExclusionAngleDegrees);
            ArrivalDistance = Math.Max(0.1f, ob.ArrivalDistance);
            WaypointRefreshDistance = Math.Max(0.05f, ob.WaypointRefreshDistance);
            RepositionLeaderDistance = Math.Max(MaximumDistanceFromLeader, ob.RepositionLeaderDistance);
            TravelScore = Math.Max(0, ob.TravelScore);
            BaseScore = Math.Max(0, ob.BaseScore);
            DistanceScore = Math.Max(0, ob.DistanceScore);
            DistanceExponent = Math.Max(0.01f, ob.DistanceExponent);
            MinimumCachedPositionDistance = Math.Max(0, ob.MinimumCachedPositionDistance);
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
        private SiShootOpposingNpcBehaviorComponent _shootBehavior;
        private Vector3D _lastThreatDirection;
        private bool _hasLastThreatDirection;

        public string BehaviorName => DefinitionId.ToString();

        public override bool IsSerialized => false;

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            _definition = (SiTakePlainViewBehaviorDefinition)definition;
        }

        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            _shootBehavior = Entity?.Components?.Get<SiShootOpposingNpcBehaviorComponent>();
        }

        float ISiUtilityBehavior.Evaluate(SiUtilityContext context)
        {
            var session = SiNpcSessionComponent.Instance;
            if (context?.Agent == null || session == null)
                return 0;
            if (session.IsAiSquadLeader(context.Agent))
            {
                ResetState(context);
                context.Agent.ClearCombatMovementRole();
                return 0;
            }

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
            RememberThreatDirectionIfAvailable(context, leaderPosition);
            var leaderDistance = Vector3D.Distance(context.Position, leaderPosition);
            if (!_hasPlainViewPosition)
                AssignPlainViewPosition(context, leaderPosition);
            else if (leaderDistance >= _definition.RepositionLeaderDistance && HasReachedDestination(context))
                AssignPlainViewPosition(context, leaderPosition);

            if (!_hasPlainViewPosition)
                return 0;

            if (!HasReachedDestination(context))
                return _definition.TravelScore;

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
            var session = SiNpcSessionComponent.Instance;
            if (context?.Agent == null || session == null)
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
            session.CacheCombatPosition(context.Agent, SiCombatMovementRole.PlainView, _plainViewPosition);
            session.TryFollowCachedCombatPosition(
                context.Agent,
                SiCombatMovementRole.PlainView,
                _definition.WaypointRefreshDistance * _definition.WaypointRefreshDistance);
        }

        internal bool IsMovingToPlainView(SiUtilityContext context)
        {
            return _hasPlainViewPosition
                   && context?.Agent != null
                   && !HasReachedDestination(context);
        }

        private void AssignPlainViewPosition(SiUtilityContext context, in Vector3D leaderPosition)
        {
            var session = SiNpcSessionComponent.Instance;
            if (context?.Agent == null || session == null)
                return;

            var up = ResolveUp(context.Position, context.Entity?.WorldMatrix.Up ?? Vector3D.Up);
            var forward = Vector3D.Reject(context.Entity?.WorldMatrix.Forward ?? Vector3D.Forward, up);
            if (forward.LengthSquared() <= 0.0001)
                forward = Vector3D.CalculatePerpendicularVector(up);
            forward.Normalize();
            var right = Vector3D.Normalize(Vector3D.Cross(forward, up));
            var hasThreatDirection = _hasLastThreatDirection;
            var threatDirection = _lastThreatDirection;
            var threatRight = hasThreatDirection
                ? SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(Vector3D.Cross(threatDirection, up), right)
                : Vector3D.Zero;

            _repositionIndex++;
            var hash = unchecked((int)(context.EntityId ^ (_activeCombatToken * 397) ^ (_repositionIndex * 7919)));
            var normalizedAngle = (Math.Abs(hash) % 1024) / 1024d;
            var normalizedRadius = (Math.Abs(hash / 1024) % 1024) / 1024d;
            var radius = MathHelper.Lerp(
                _definition.MinimumDistanceFromLeader,
                _definition.MaximumDistanceFromLeader,
                (float)normalizedRadius);
            Vector3D candidatePosition;

            if (hasThreatDirection && _definition.ThreatFrontExclusionAngleDegrees > 0)
            {
                var angle = ResolveAllowedSectorAngle(normalizedAngle);
                candidatePosition = leaderPosition
                                    + threatDirection * (Math.Cos(angle) * radius)
                                    + threatRight * (Math.Sin(angle) * radius);
            }
            else
            {
                var angle = normalizedAngle * Math.PI * 2d;
                candidatePosition = leaderPosition
                                    + forward * (Math.Cos(angle) * radius)
                                    + right * (Math.Sin(angle) * radius);
            }

            long ignoredBlockingEntityId;
            if (session.HasNearbyCachedCombatPosition(
                    context.Agent,
                    candidatePosition,
                    _definition.MinimumCachedPositionDistance,
                    out ignoredBlockingEntityId))
                return;

            if (!HasClearImmediateMovementProbe(context, candidatePosition, up))
                return;

            _plainViewPosition = candidatePosition;
            _hasPlainViewPosition = true;
            session.CacheCombatPosition(context.Agent, SiCombatMovementRole.PlainView, _plainViewPosition);
        }

        private bool HasClearImmediateMovementProbe(
            SiUtilityContext context,
            in Vector3D candidatePosition,
            in Vector3D up)
        {
            var entity = context?.Entity;
            if (entity == null)
                return false;

            var movement = Vector3D.Reject(candidatePosition - context.Position, up);
            var movementLengthSquared = movement.LengthSquared();
            if (movementLengthSquared <= 0.0001)
                return true;

            var direction = movement / Math.Sqrt(movementLengthSquared);
            var aabb = entity.PositionComp.WorldAABB;
            var colliderLength = Math.Max(
                0.5,
                Math.Max(aabb.Size.X, Math.Max(aabb.Size.Y, aabb.Size.Z)));
            var probeLength = Math.Min(colliderLength, Math.Sqrt(movementLengthSquared));
            var rayStart = aabb.Center;
            var rayEnd = rayStart + direction * probeLength;

            IHitInfo hit;
            if (!MyAPIGateway.Physics.CastRay(rayStart, rayEnd, out hit) || hit == null)
                return true;

            return hit.HitEntity == null || hit.HitEntity == entity;
        }

        private void RememberThreatDirectionIfAvailable(SiUtilityContext context, in Vector3D leaderPosition)
        {
            if (_shootBehavior == null)
                return;

            MyEntity threatEntity;
            double ignoredDistance;
            if (!_shootBehavior.TryGetCurrentThreat(context, out threatEntity, out ignoredDistance) || threatEntity == null)
                return;

            Vector3D threatDirection;
            if (!SiThreatSectorHelper.TryGetPlanarDirection(
                    leaderPosition,
                    threatEntity.WorldMatrix.Translation,
                    ResolveUp(leaderPosition, context.Entity?.WorldMatrix.Up ?? Vector3D.Up),
                    out threatDirection))
                return;

            _lastThreatDirection = threatDirection;
            _hasLastThreatDirection = true;
        }

        private double ResolveAllowedSectorAngle(double normalizedAngle)
        {
            var blockedAngle = MathHelper.ToRadians(_definition.ThreatFrontExclusionAngleDegrees);
            var allowedAngle = Math.Max(0.001d, Math.PI * 2d - blockedAngle);
            var startAngle = blockedAngle * 0.5d;
            return startAngle + normalizedAngle * allowedAngle;
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
            SiNpcSessionComponent.Instance?.ClearCachedCombatPosition(context?.Agent?.EntityId ?? 0, SiCombatMovementRole.PlainView);
            _hasPlainViewPosition = false;
            _plainViewPosition = Vector3D.Zero;
            _activeCombatToken = long.MinValue;
            _repositionIndex = 0;
            _hasLastThreatDirection = false;
            _lastThreatDirection = Vector3D.Zero;
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
