using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Entities.Gravity;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace Si.UtilityAI
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiTakeCoverBehavior : MyObjectBuilder_EntityComponent
    {
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiTakeCoverBehaviorDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public float SearchRadius;
        public float WaypointRefreshDistance;
        public float CoverOccupancyRadius;
        public float CoverArrivalDistance;
        public float MinimumCoverOffset;
        public float PreferredTreeOffset;
        public float MaximumCoverOffset;
        public float CoverOffsetStep;
        public float BodyCoverAimHeight;
        public float BodyCoverForwardOffset;
        public float BodyCoverMinimumBlockage;
        public float MuzzleMaximumBlockage;
        public float SwitchDistanceFromLeader;
        public float FullSwitchDistanceFromLeader;
        public float LeaderDistanceWeight;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiTakeCoverBehaviorDefinition))]
    public class SiTakeCoverBehaviorDefinition : MyEntityComponentDefinition
    {
        public float SearchRadius { get; private set; }
        public float WaypointRefreshDistance { get; private set; }
        public float CoverOccupancyRadius { get; private set; }
        public float CoverArrivalDistance { get; private set; }
        public float MinimumCoverOffset { get; private set; }
        public float PreferredTreeOffset { get; private set; }
        public float MaximumCoverOffset { get; private set; }
        public float CoverOffsetStep { get; private set; }
        public float BodyCoverAimHeight { get; private set; }
        public float BodyCoverForwardOffset { get; private set; }
        public float BodyCoverMinimumBlockage { get; private set; }
        public float MuzzleMaximumBlockage { get; private set; }
        public float SwitchDistanceFromLeader { get; private set; }
        public float FullSwitchDistanceFromLeader { get; private set; }
        public float LeaderDistanceWeight { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiTakeCoverBehaviorDefinition)builder;
            SearchRadius = Math.Max(0, ob.SearchRadius);
            WaypointRefreshDistance = Math.Max(0, ob.WaypointRefreshDistance);
            CoverOccupancyRadius = Math.Max(0.1f, ob.CoverOccupancyRadius);
            CoverArrivalDistance = Math.Max(0.1f, ob.CoverArrivalDistance);
            MinimumCoverOffset = Math.Max(0.1f, ob.MinimumCoverOffset);
            PreferredTreeOffset = Math.Max(MinimumCoverOffset, ob.PreferredTreeOffset);
            MaximumCoverOffset = Math.Max(PreferredTreeOffset, ob.MaximumCoverOffset);
            CoverOffsetStep = Math.Max(0.05f, ob.CoverOffsetStep);
            BodyCoverAimHeight = Math.Max(0, ob.BodyCoverAimHeight);
            BodyCoverForwardOffset = Math.Max(0, ob.BodyCoverForwardOffset);
            BodyCoverMinimumBlockage = MathHelper.Clamp(ob.BodyCoverMinimumBlockage, 0, 1);
            MuzzleMaximumBlockage = MathHelper.Clamp(ob.MuzzleMaximumBlockage, 0, 1);
            SwitchDistanceFromLeader = Math.Max(0, ob.SwitchDistanceFromLeader);
            FullSwitchDistanceFromLeader = Math.Max(SwitchDistanceFromLeader, ob.FullSwitchDistanceFromLeader);
            LeaderDistanceWeight = Math.Max(0, ob.LeaderDistanceWeight);
        }
    }

    [MyComponent(typeof(MyObjectBuilder_SiTakeCoverBehavior))]
    [MyDefinitionRequired(typeof(SiTakeCoverBehaviorDefinition))]
    public class SiTakeCoverBehaviorComponent : MyEntityComponent, ISiUtilityBehavior
    {
        private readonly List<Vector3D> _coverPositions = new List<Vector3D>();
        private readonly SiNearbyCoverScanner _coverScanner = new SiNearbyCoverScanner();

        private SiTakeCoverBehaviorDefinition _definition;
        private SiShootOpposingNpcBehaviorComponent _shootBehavior;
        private Vector3D _reservedCoverPosition;
        private Vector3D _reservedStandPosition;
        private bool _hasReservedCover;

        public string BehaviorName => DefinitionId.ToString();

        public override bool IsSerialized => false;

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            _definition = (SiTakeCoverBehaviorDefinition)definition;
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

            if (session.GetCombatStance(context.Agent) != SiSquadCombatStance.Combat)
            {
                ReleaseCover(session, context);
                return 0;
            }

            var hasThreat = TryGetThreat(context, out var threatEntity, out var threatPosition);
            if (!HasUsableCurrentCover(context, session, hasThreat, threatEntity, threatPosition))
                return 1f;

            if (!session.IsFollowingPlayer(context.Agent))
                return 0;

            double leaderDistance;
            if (!session.TryGetLeaderDistance(context.Agent, out leaderDistance))
                return 0;
            if (leaderDistance <= _definition.SwitchDistanceFromLeader)
                return 0;

            var span = _definition.FullSwitchDistanceFromLeader - _definition.SwitchDistanceFromLeader;
            if (span <= 0.001f)
                return 1f;

            var normalized = MathHelper.Clamp(
                (float)((leaderDistance - _definition.SwitchDistanceFromLeader) / span),
                0,
                1);
            return normalized;
        }

        void ISiUtilityBehavior.Begin(SiUtilityContext context)
        {
            MaintainCover(context, true);
        }

        void ISiUtilityBehavior.Tick(SiUtilityContext context, long elapsedMilliseconds)
        {
            MaintainCover(context, false);
        }

        void ISiUtilityBehavior.End(SiUtilityContext context)
        {
            var session = SiNpcSessionComponent.Instance;
            if (context?.Agent == null || session == null)
                return;

            if (session.GetCombatStance(context.Agent) != SiSquadCombatStance.Combat)
                ReleaseCover(session, context);
        }

        private void MaintainCover(SiUtilityContext context, bool forceRefresh)
        {
            var session = SiNpcSessionComponent.Instance;
            if (context?.Agent == null || session == null)
                return;

            if (session.GetCombatStance(context.Agent) != SiSquadCombatStance.Combat)
            {
                ReleaseCover(session, context);
                return;
            }

            var hasThreat = TryGetThreat(context, out var threatEntity, out var threatPosition);
            var wantsSwitch = forceRefresh
                              || !HasUsableCurrentCover(context, session, hasThreat, threatEntity, threatPosition)
                              || WantsLeaderCatchup(context, session);

            if (wantsSwitch
                && FindBestCover(
                    context,
                    session,
                    hasThreat,
                    threatEntity,
                    threatPosition,
                    out var coverPosition,
                    out var standPosition))
            {
                if (!_hasReservedCover
                    || Vector3D.DistanceSquared(_reservedCoverPosition, coverPosition) > 0.01)
                {
                    ReleaseCover(session, context);
                    if (session.TryReserveCover(context.Agent, coverPosition, _definition.CoverOccupancyRadius))
                    {
                        _reservedCoverPosition = coverPosition;
                        _reservedStandPosition = standPosition;
                        _hasReservedCover = true;
                    }
                }
                else
                {
                    session.TryReserveCover(context.Agent, _reservedCoverPosition, _definition.CoverOccupancyRadius);
                    _reservedStandPosition = standPosition;
                }
            }

            if (!_hasReservedCover)
                return;

            session.TryReserveCover(context.Agent, _reservedCoverPosition, _definition.CoverOccupancyRadius);
            if (!context.HasWaypoint
                || Vector3D.DistanceSquared(context.Waypoint, _reservedStandPosition)
                   > _definition.WaypointRefreshDistance * _definition.WaypointRefreshDistance)
                context.TrySetWaypoint(_reservedStandPosition);
        }

        private bool HasUsableCurrentCover(
            SiUtilityContext context,
            SiNpcSessionComponent session,
            bool hasThreat,
            MyEntity threatEntity,
            in Vector3D threatPosition)
        {
            if (!_hasReservedCover)
                return false;

            if (!session.TryReserveCover(context.Agent, _reservedCoverPosition, _definition.CoverOccupancyRadius))
                return false;

            if (Vector3D.DistanceSquared(context.Position, _reservedStandPosition)
                > _definition.CoverArrivalDistance * _definition.CoverArrivalDistance)
                return true;

            if (!hasThreat)
                return true;

            return EvaluateCoverStandingPoint(
                context,
                _reservedCoverPosition,
                threatEntity,
                threatPosition,
                out var refreshedStandPosition,
                out var ignoredIsTree,
                out var score)
                && score > 0
                && UpdateStandingPointIfNear(refreshedStandPosition);
        }

        private bool UpdateStandingPointIfNear(in Vector3D standPosition)
        {
            if (Vector3D.DistanceSquared(_reservedStandPosition, standPosition)
                <= _definition.WaypointRefreshDistance * _definition.WaypointRefreshDistance)
                return true;

            _reservedStandPosition = standPosition;
            return true;
        }

        private bool WantsLeaderCatchup(SiUtilityContext context, SiNpcSessionComponent session)
        {
            if (!_hasReservedCover || !session.IsFollowingPlayer(context.Agent))
                return false;

            double leaderDistance;
            return session.TryGetLeaderDistance(context.Agent, out leaderDistance)
                   && leaderDistance > _definition.SwitchDistanceFromLeader;
        }

        private bool FindBestCover(
            SiUtilityContext context,
            SiNpcSessionComponent session,
            bool hasThreat,
            MyEntity threatEntity,
            in Vector3D threatPosition,
            out Vector3D coverPosition,
            out Vector3D standPosition)
        {
            coverPosition = Vector3D.Zero;
            standPosition = Vector3D.Zero;

            _coverPositions.Clear();
            _coverScanner.Scan(context.Position, _definition.SearchRadius, _coverPositions);
            if (_coverPositions.Count == 0)
                return false;

            var bestTreeScore = double.MinValue;
            var bestBushScore = double.MinValue;
            var bestTreeCover = Vector3D.Zero;
            var bestBushCover = Vector3D.Zero;
            var bestTreeStand = Vector3D.Zero;
            var bestBushStand = Vector3D.Zero;

            for (var i = 0; i < _coverPositions.Count; i++)
            {
                var candidate = _coverPositions[i];
                if (!session.IsCoverAvailable(context.Agent, candidate, _definition.CoverOccupancyRadius))
                    continue;

                var effectiveThreat = hasThreat
                    ? threatPosition
                    : GuessThreatPosition(context, candidate);
                if (!EvaluateCoverStandingPoint(
                        context,
                        candidate,
                        threatEntity,
                        effectiveThreat,
                        out var candidateStand,
                        out var isTree,
                        out var coverScore))
                    continue;

                var score = ScoreCoverCandidate(context, session, candidate, candidateStand, coverScore);
                if (isTree)
                {
                    if (score > bestTreeScore)
                    {
                        bestTreeScore = score;
                        bestTreeCover = candidate;
                        bestTreeStand = candidateStand;
                    }
                }
                else if (score > bestBushScore)
                {
                    bestBushScore = score;
                    bestBushCover = candidate;
                    bestBushStand = candidateStand;
                }
            }

            if (bestTreeScore > double.MinValue)
            {
                coverPosition = bestTreeCover;
                standPosition = bestTreeStand;
                return true;
            }

            if (bestBushScore > double.MinValue)
            {
                coverPosition = bestBushCover;
                standPosition = bestBushStand;
                return true;
            }

            return false;
        }

        private double ScoreCoverCandidate(
            SiUtilityContext context,
            SiNpcSessionComponent session,
            in Vector3D coverPosition,
            in Vector3D standPosition,
            double coverScore)
        {
            var score = coverScore;
            score -= Vector3D.DistanceSquared(context.Position, standPosition) * 0.02;

            if (session.IsFollowingPlayer(context.Agent))
            {
                Vector3D leaderPosition;
                if (session.TryGetLeaderPosition(context.Agent, out leaderPosition))
                    score -= Vector3D.DistanceSquared(leaderPosition, standPosition) * _definition.LeaderDistanceWeight * 0.01;
            }

            if (_hasReservedCover
                && Vector3D.DistanceSquared(_reservedCoverPosition, coverPosition) <= 0.01)
                score += 0.15;

            return score;
        }

        private bool EvaluateCoverStandingPoint(
            SiUtilityContext context,
            in Vector3D coverPosition,
            MyEntity threatEntity,
            in Vector3D threatPosition,
            out Vector3D bestStandPosition,
            out bool isTree,
            out double bestScore)
        {
            bestStandPosition = Vector3D.Zero;
            isTree = false;
            bestScore = double.MinValue;

            var world = context.Entity.WorldMatrix;
            var up = ResolveUp(context.Position, world.Up);
            var awayFromThreat = Vector3D.Reject(coverPosition - threatPosition, up);
            awayFromThreat = SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(
                awayFromThreat,
                SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(
                    context.Position - coverPosition,
                    Vector3D.CalculatePerpendicularVector(up)));

            for (var offset = _definition.MaximumCoverOffset; offset >= _definition.MinimumCoverOffset; offset -= _definition.CoverOffsetStep)
            {
                var standPosition = coverPosition + awayFromThreat * offset;
                if (!TryScoreStandingPoint(context, standPosition, threatEntity, threatPosition, up, out var score))
                    continue;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestStandPosition = standPosition;
                    isTree = offset >= _definition.PreferredTreeOffset;
                }
            }

            return bestScore > double.MinValue;
        }

        private bool TryScoreStandingPoint(
            SiUtilityContext context,
            in Vector3D standPosition,
            MyEntity threatEntity,
            in Vector3D threatPosition,
            in Vector3D up,
            out double score)
        {
            score = 0;
            var toThreat = SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(
                threatPosition - standPosition,
                context.Entity.WorldMatrix.Forward);
            var aimPoint = threatPosition + up * GetAimTargetHeight();
            var bodyOrigin = standPosition
                             + up * _definition.BodyCoverAimHeight
                             + toThreat * _definition.BodyCoverForwardOffset;
            var muzzleOrigin = standPosition
                               + up * GetMuzzleUpOffset()
                               + toThreat * GetMuzzleForwardOffset();

            double bodyHitDistance;
            if (!TryGetBlockingHitDistance(bodyOrigin, aimPoint, context.Entity, threatEntity, out bodyHitDistance))
                return false;

            double muzzleHitDistance;
            if (TryGetBlockingHitDistance(muzzleOrigin, aimPoint, context.Entity, threatEntity, out muzzleHitDistance))
                return false;

            var threatDistance = Vector3D.Distance(bodyOrigin, aimPoint);
            var normalizedCover = threatDistance > 0.001
                ? MathHelper.Clamp((float)(1d - bodyHitDistance / threatDistance), 0, 1)
                : 0f;
            if (normalizedCover < _definition.BodyCoverMinimumBlockage)
                return false;

            score = normalizedCover;
            return true;
        }

        private Vector3D GuessThreatPosition(SiUtilityContext context, in Vector3D coverPosition)
        {
            var forward = SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(
                Vector3D.Reject(context.Entity.WorldMatrix.Forward, ResolveUp(context.Position, context.Entity.WorldMatrix.Up)),
                Vector3D.Forward);
            return coverPosition + forward * Math.Max(15f, _definition.SearchRadius);
        }

        private bool TryGetThreat(SiUtilityContext context, out MyEntity threatEntity, out Vector3D threatPosition)
        {
            threatEntity = null;
            threatPosition = Vector3D.Zero;
            double distance;
            if (_shootBehavior == null
                || !_shootBehavior.TryGetCurrentThreat(context, out threatEntity, out distance)
                || threatEntity == null)
                return false;

            threatPosition = threatEntity.WorldMatrix.Translation;
            return true;
        }

        private static bool TryGetBlockingHitDistance(
            in Vector3D start,
            in Vector3D end,
            MyEntity self,
            MyEntity target,
            out double distance)
        {
            distance = 0;

            IHitInfo hit;
            if (!MyAPIGateway.Physics.CastRay(start, end, out hit)
                || hit == null
                || hit.HitEntity == null
                || hit.HitEntity == self
                || hit.HitEntity == target)
                return false;

            distance = Vector3D.Distance(start, hit.Position);
            return true;
        }

        private float GetAimTargetHeight() =>
            _shootBehavior?.GetWeaponAimHeightForCover() ?? _definition.BodyCoverAimHeight;

        private float GetMuzzleForwardOffset() =>
            _shootBehavior?.GetWeaponMuzzleForwardOffsetForCover() ?? 0.6f;

        private float GetMuzzleUpOffset() =>
            _shootBehavior?.GetWeaponMuzzleUpOffsetForCover() ?? GetAimTargetHeight();

        private static Vector3D ResolveUp(in Vector3D position, in Vector3D fallbackUp)
        {
            var gravity = MyGravityProviderSystem.CalculateTotalGravityInPoint(position);
            if (gravity.LengthSquared() > 0.0001)
                return -Vector3D.Normalize(gravity);
            return SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(fallbackUp, Vector3D.Up);
        }

        private void ReleaseCover(SiNpcSessionComponent session, SiUtilityContext context)
        {
            if (_hasReservedCover)
                session.ReleaseCover(context.Agent.EntityId);
            _hasReservedCover = false;
            _reservedCoverPosition = Vector3D.Zero;
            _reservedStandPosition = Vector3D.Zero;
        }
    }
}
