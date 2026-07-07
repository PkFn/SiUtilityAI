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
using VRage.ObjectBuilders;
using VRageMath;

namespace Si.UtilityAI
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiAdvanceTowardEnemyLeaderBehavior : MyObjectBuilder_EntityComponent
    {
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiAdvanceTowardEnemyLeaderBehaviorDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public float SearchRadius;
        public float WaypointRefreshDistance;
        public float CoverOccupancyRadius;
        public float CoverArrivalDistance;
        public float MinimumCoverOffset;
        public float PreferredTreeOffset;
        public float MaximumCoverOffset;
        public float CoverOffsetStep;
        public float AdvanceSectorAngleDegrees = 150f;
        public float MoveScore = 0.2f;
        public float MinimumTakeCoverScore = 0.05f;
        public float ThreatTakeCoverScore = 0.8f;
        public float DamageTakeCoverScore = 0.95f;
        public float TakeCoverScoreDecayPerSecond = 0.08f;
        public int SuccessfulFireMemoryMilliseconds = 2500;
        public int CoverHoldCooldownMilliseconds = 4500;
        public int RecentDamageMemoryMilliseconds = 5000;
        public int TargetMemoryMilliseconds = 2500;
        public float MinimumAdvanceProgressDistance = 1.25f;
        public float StopAdvanceDistance = 12f;
        public float LeaderCoverBlacklistRadius = 1f;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiAdvanceTowardEnemyLeaderBehaviorDefinition))]
    public class SiAdvanceTowardEnemyLeaderBehaviorDefinition : MyEntityComponentDefinition
    {
        private static readonly MyDefinitionId DefaultDefinitionId =
            new MyDefinitionId(typeof(MyObjectBuilder_SiAdvanceTowardEnemyLeaderBehaviorDefinition), "SiAiLeaderAdvanceToEnemyLeader");

        private static bool _defaultResolved;
        private static SiAdvanceTowardEnemyLeaderBehaviorDefinition _defaultDefinition;

        public float SearchRadius { get; private set; }
        public float WaypointRefreshDistance { get; private set; }
        public float CoverOccupancyRadius { get; private set; }
        public float CoverArrivalDistance { get; private set; }
        public float MinimumCoverOffset { get; private set; }
        public float PreferredTreeOffset { get; private set; }
        public float MaximumCoverOffset { get; private set; }
        public float CoverOffsetStep { get; private set; }
        public float AdvanceSectorAngleDegrees { get; private set; }
        public float MoveScore { get; private set; }
        public float MinimumTakeCoverScore { get; private set; }
        public float ThreatTakeCoverScore { get; private set; }
        public float DamageTakeCoverScore { get; private set; }
        public float TakeCoverScoreDecayPerSecond { get; private set; }
        public int SuccessfulFireMemoryMilliseconds { get; private set; }
        public int CoverHoldCooldownMilliseconds { get; private set; }
        public int RecentDamageMemoryMilliseconds { get; private set; }
        public int TargetMemoryMilliseconds { get; private set; }
        public float MinimumAdvanceProgressDistance { get; private set; }
        public float StopAdvanceDistance { get; private set; }
        public float LeaderCoverBlacklistRadius { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiAdvanceTowardEnemyLeaderBehaviorDefinition)builder;
            SearchRadius = Math.Max(0, ob.SearchRadius);
            WaypointRefreshDistance = Math.Max(0.05f, ob.WaypointRefreshDistance);
            CoverOccupancyRadius = Math.Max(0.1f, ob.CoverOccupancyRadius);
            CoverArrivalDistance = Math.Max(0.1f, ob.CoverArrivalDistance);
            MinimumCoverOffset = Math.Max(0.1f, ob.MinimumCoverOffset);
            PreferredTreeOffset = Math.Max(MinimumCoverOffset, ob.PreferredTreeOffset);
            MaximumCoverOffset = Math.Max(PreferredTreeOffset, ob.MaximumCoverOffset);
            CoverOffsetStep = Math.Max(0.05f, ob.CoverOffsetStep);
            AdvanceSectorAngleDegrees = MathHelper.Clamp(ob.AdvanceSectorAngleDegrees, 1f, 359f);
            MoveScore = Math.Max(0, ob.MoveScore);
            MinimumTakeCoverScore = Math.Max(0, ob.MinimumTakeCoverScore);
            ThreatTakeCoverScore = Math.Max(MinimumTakeCoverScore, ob.ThreatTakeCoverScore);
            DamageTakeCoverScore = Math.Max(ThreatTakeCoverScore, ob.DamageTakeCoverScore);
            TakeCoverScoreDecayPerSecond = Math.Max(0, ob.TakeCoverScoreDecayPerSecond);
            SuccessfulFireMemoryMilliseconds = Math.Max(0, ob.SuccessfulFireMemoryMilliseconds);
            CoverHoldCooldownMilliseconds = Math.Max(0, ob.CoverHoldCooldownMilliseconds);
            RecentDamageMemoryMilliseconds = Math.Max(0, ob.RecentDamageMemoryMilliseconds);
            TargetMemoryMilliseconds = Math.Max(0, ob.TargetMemoryMilliseconds);
            MinimumAdvanceProgressDistance = Math.Max(0, ob.MinimumAdvanceProgressDistance);
            StopAdvanceDistance = Math.Max(0, ob.StopAdvanceDistance);
            LeaderCoverBlacklistRadius = Math.Max(0, ob.LeaderCoverBlacklistRadius);
        }

        internal static SiAdvanceTowardEnemyLeaderBehaviorDefinition LoadDefault()
        {
            if (_defaultResolved)
                return _defaultDefinition;

            _defaultResolved = true;
            if (MyDefinitionManager.TryGet(DefaultDefinitionId, out _defaultDefinition))
                return _defaultDefinition;

            foreach (var candidate in MyDefinitionManager.GetOfType<SiAdvanceTowardEnemyLeaderBehaviorDefinition>())
            {
                _defaultDefinition = candidate;
                break;
            }

            return _defaultDefinition;
        }
    }

    [MyComponent(typeof(MyObjectBuilder_SiAdvanceTowardEnemyLeaderBehavior))]
    [MyDefinitionRequired(typeof(SiAdvanceTowardEnemyLeaderBehaviorDefinition))]
    public class SiAdvanceTowardEnemyLeaderBehaviorComponent : MyEntityComponent, ISiUtilityBehavior, ISiContinuousUtilityBehavior
    {
        private const double MinimumDirectionLengthSquared = 0.0001;

        private static readonly double[] SideOffsetSamples =
        {
            0,
            0.35,
            -0.35,
            0.7,
            -0.7,
            1.0,
            -1.0,
        };

        private readonly List<Vector3D> _coverPositions = new List<Vector3D>();
        private readonly SiNearbyCoverScanner _coverScanner = new SiNearbyCoverScanner();

        private SiAdvanceTowardEnemyLeaderBehaviorDefinition _definition;
        private SiShootOpposingNpcBehaviorComponent _shootBehavior;
        private Vector3D _reservedCoverPosition;
        private Vector3D _reservedStandPosition;
        private bool _hasReservedCover;
        private bool _awaitingCoverArrival;
        private Vector3D _directMoveTarget;
        private bool _hasDirectMoveTarget;
        private long _activeCombatToken = long.MinValue;
        private long _lastCoverArrivalTime = long.MinValue;
        private long _lastDamageReactionTime = long.MinValue;
        private long _lastKnownEnemyLeaderObservationTime = long.MinValue;
        private Vector3D _lastKnownEnemyLeaderPosition;
        private bool _hasLastKnownEnemyLeaderPosition;
        private Vector3D _lastAdvanceDirection;
        private bool _hasLastAdvanceDirection;
        private AdvanceMode _mode;

        public string BehaviorName => DefinitionId.ToString();

        public override bool IsSerialized => false;

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            _definition = (SiAdvanceTowardEnemyLeaderBehaviorDefinition)definition;
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

            if (!session.IsAiSquadLeader(context.Agent)
                || session.GetCombatStance(context.Agent) != SiSquadCombatStance.Combat)
            {
                ResetState(session, context);
                return 0;
            }

            var combatToken = session.GetCombatEntryToken(context.Agent);
            EnsureCombatState(combatToken, session, context);
            UpdateDamageReactionTime(session, context.Agent);

            var now = CurrentTimeMilliseconds();
            var hasEnemyLeaderTarget = TryGetEnemyLeaderTarget(
                context,
                session,
                now,
                out var enemyLeaderPosition,
                out var enemyLeaderEntityId);
            if (!hasEnemyLeaderTarget)
            {
                if (_mode == AdvanceMode.Cover && _hasReservedCover && IsTravellingToCover(context))
                    return Math.Max(_definition.MoveScore, _definition.MinimumTakeCoverScore);

                ResetDirectAdvance(context);
                return 0;
            }

            RememberAdvanceDirection(context, enemyLeaderPosition);
            if (Vector3D.DistanceSquared(context.Position, enemyLeaderPosition)
                <= _definition.StopAdvanceDistance * _definition.StopAdvanceDistance)
            {
                ResetDirectAdvance(context);
                return 0;
            }

            var hasUsableCover = HasUsableCurrentCover(context, session, enemyLeaderPosition);
            var takeCoverScore = ComputeTakeCoverScore(now, session, context.Agent);

            if (!hasUsableCover)
            {
                if (TryAssignForwardCover(
                        context,
                        session,
                        enemyLeaderPosition,
                        enemyLeaderEntityId,
                        out var coverPosition,
                        out var standPosition))
                {
                    if (!session.TryReserveCover(context.Agent, coverPosition, _definition.CoverOccupancyRadius))
                    {
                        ReleaseReservedCover(session, context);
                        AssignDirectAdvance(enemyLeaderPosition);
                        return _definition.MoveScore;
                    }

                    AssignReservedCover(coverPosition, standPosition);
                    return Math.Max(_definition.MoveScore, takeCoverScore);
                }

                ReleaseReservedCover(session, context);
                AssignDirectAdvance(enemyLeaderPosition);
                return _definition.MoveScore;
            }

            if (_awaitingCoverArrival && !IsTravellingToCover(context))
                MarkCoverArrived();

            ResetDirectAdvance(context);
            if (now - _lastCoverArrivalTime < _definition.CoverHoldCooldownMilliseconds)
                return 0;
            if (takeCoverScore > _definition.MoveScore)
                return 0;

            if (TryAssignForwardCover(
                    context,
                    session,
                    enemyLeaderPosition,
                    enemyLeaderEntityId,
                    out var nextCoverPosition,
                    out var nextStandPosition))
            {
                if (!session.TryReserveCover(context.Agent, nextCoverPosition, _definition.CoverOccupancyRadius))
                {
                    ReleaseReservedCover(session, context);
                    AssignDirectAdvance(enemyLeaderPosition);
                    return _definition.MoveScore;
                }

                AssignReservedCover(nextCoverPosition, nextStandPosition);
                return _definition.MoveScore;
            }

            ReleaseReservedCover(session, context);
            AssignDirectAdvance(enemyLeaderPosition);
            return _definition.MoveScore;
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

            if (_mode == AdvanceMode.Cover && _hasReservedCover)
            {
                session.TryReserveCover(context.Agent, _reservedCoverPosition, _definition.CoverOccupancyRadius);
                session.CacheCombatPosition(context.Agent, SiCombatMovementRole.Covered, _reservedStandPosition);
                context.TrySetCrouch(false);
                session.TryFollowCachedCombatPosition(
                    context.Agent,
                    SiCombatMovementRole.Covered,
                    _definition.WaypointRefreshDistance * _definition.WaypointRefreshDistance);

                if (!IsTravellingToCover(context))
                {
                    context.TryClearWaypoint();
                    MarkCoverArrived();
                }

                return;
            }

            session.ClearCachedCombatPosition(context.Agent.EntityId, SiCombatMovementRole.Covered);
            if (_mode != AdvanceMode.Direct || !_hasDirectMoveTarget)
                return;

            context.TrySetCrouch(false);
            if (Vector3D.DistanceSquared(context.Position, _directMoveTarget)
                <= _definition.CoverArrivalDistance * _definition.CoverArrivalDistance)
            {
                context.TryClearWaypoint();
                return;
            }

            if (context.HasWaypoint
                && Vector3D.DistanceSquared(context.Waypoint, _directMoveTarget)
                   <= _definition.WaypointRefreshDistance * _definition.WaypointRefreshDistance)
                return;

            context.TrySetWaypoint(_directMoveTarget);
        }

        private void EnsureCombatState(long combatToken, SiNpcSessionComponent session, SiUtilityContext context)
        {
            if (_activeCombatToken == combatToken)
                return;

            ResetState(session, context);
            _activeCombatToken = combatToken;
        }

        private void ResetState(SiNpcSessionComponent session, SiUtilityContext context)
        {
            ReleaseReservedCover(session, context);
            ResetDirectAdvance(context);
            _awaitingCoverArrival = false;
            _activeCombatToken = long.MinValue;
            _lastCoverArrivalTime = long.MinValue;
            _lastDamageReactionTime = long.MinValue;
            _lastKnownEnemyLeaderObservationTime = long.MinValue;
            _lastKnownEnemyLeaderPosition = Vector3D.Zero;
            _hasLastKnownEnemyLeaderPosition = false;
            _lastAdvanceDirection = Vector3D.Zero;
            _hasLastAdvanceDirection = false;
            _mode = AdvanceMode.None;
            context?.TrySetCrouch(false);
        }

        private void ReleaseReservedCover(SiNpcSessionComponent session, SiUtilityContext context)
        {
            if (_hasReservedCover)
                session.ReleaseCover(context.Agent.EntityId);

            session.ClearCachedCombatPosition(context.Agent.EntityId, SiCombatMovementRole.Covered);
            _reservedCoverPosition = Vector3D.Zero;
            _reservedStandPosition = Vector3D.Zero;
            _hasReservedCover = false;
            _awaitingCoverArrival = false;
            if (_mode == AdvanceMode.Cover)
                _mode = AdvanceMode.None;
        }

        private void AssignReservedCover(in Vector3D coverPosition, in Vector3D standPosition)
        {
            _reservedCoverPosition = coverPosition;
            _reservedStandPosition = standPosition;
            _hasReservedCover = true;
            _awaitingCoverArrival = true;
            _mode = AdvanceMode.Cover;
            _hasDirectMoveTarget = false;
            _directMoveTarget = Vector3D.Zero;
        }

        private void AssignDirectAdvance(in Vector3D targetPosition)
        {
            _directMoveTarget = targetPosition;
            _hasDirectMoveTarget = true;
            _mode = AdvanceMode.Direct;
        }

        private void ResetDirectAdvance(SiUtilityContext context)
        {
            var hadDirectAdvance = _mode == AdvanceMode.Direct || _hasDirectMoveTarget;
            _hasDirectMoveTarget = false;
            _directMoveTarget = Vector3D.Zero;
            if (_mode == AdvanceMode.Direct)
                _mode = _hasReservedCover ? AdvanceMode.Cover : AdvanceMode.None;

            if (hadDirectAdvance)
                context?.TryClearWaypoint();
        }

        private bool HasUsableCurrentCover(
            SiUtilityContext context,
            SiNpcSessionComponent session,
            in Vector3D enemyLeaderPosition)
        {
            if (!_hasReservedCover)
                return false;

            if (!session.TryReserveCover(context.Agent, _reservedCoverPosition, _definition.CoverOccupancyRadius))
                return false;

            if (TryResolveStandingPoint(
                    context,
                    _reservedCoverPosition,
                    enemyLeaderPosition,
                    out var refreshedStandPosition,
                    out var ignoredIsTree))
                _reservedStandPosition = refreshedStandPosition;

            _mode = AdvanceMode.Cover;
            return true;
        }

        private bool IsTravellingToCover(SiUtilityContext context)
        {
            return _hasReservedCover
                   && Vector3D.DistanceSquared(context.Position, _reservedStandPosition)
                      > _definition.CoverArrivalDistance * _definition.CoverArrivalDistance;
        }

        private void MarkCoverArrived()
        {
            _awaitingCoverArrival = false;
            _lastCoverArrivalTime = CurrentTimeMilliseconds();
        }

        private void UpdateDamageReactionTime(SiNpcSessionComponent session, SiNpc agent)
        {
            var lastShotAtTime = session.GetLastSquadShotAtTime(agent);
            if (lastShotAtTime > _lastDamageReactionTime)
                _lastDamageReactionTime = lastShotAtTime;
        }

        private float ComputeTakeCoverScore(
            long now,
            SiNpcSessionComponent session,
            SiNpc agent)
        {
            var score = _definition.MinimumTakeCoverScore;
            var lastSuccessfulFireTime = _shootBehavior?.GetLastSuccessfulFireTime() ?? long.MinValue;
            if (lastSuccessfulFireTime > long.MinValue
                && now - lastSuccessfulFireTime <= _definition.SuccessfulFireMemoryMilliseconds)
            {
                var threatScore = Math.Max(
                    _definition.MinimumTakeCoverScore,
                    _definition.ThreatTakeCoverScore - _definition.TakeCoverScoreDecayPerSecond
                    * Math.Max(0, (now - lastSuccessfulFireTime) / 1000f));
                score = Math.Max(score, threatScore);
            }

            if (_lastDamageReactionTime > long.MinValue
                && now - _lastDamageReactionTime <= _definition.RecentDamageMemoryMilliseconds
                && session.WasSquadRecentlyShotAt(agent, _definition.RecentDamageMemoryMilliseconds))
            {
                var damageScore = Math.Max(
                    _definition.MinimumTakeCoverScore,
                    _definition.DamageTakeCoverScore - _definition.TakeCoverScoreDecayPerSecond
                    * Math.Max(0, (now - _lastDamageReactionTime) / 1000f));
                score = Math.Max(score, damageScore);
            }

            return Math.Max(_definition.MinimumTakeCoverScore, score);
        }

        private bool TryGetEnemyLeaderTarget(
            SiUtilityContext context,
            SiNpcSessionComponent session,
            long now,
            out Vector3D enemyLeaderPosition,
            out long enemyLeaderEntityId)
        {
            enemyLeaderPosition = Vector3D.Zero;
            enemyLeaderEntityId = 0;

            MyEntity threatEntity;
            Vector3D threatPosition;
            double ignoredDistance;
            if (_shootBehavior != null
                && _shootBehavior.TryGetCurrentThreat(context, out threatEntity, out threatPosition, out ignoredDistance))
            {
                enemyLeaderPosition = threatPosition;
                enemyLeaderEntityId = threatEntity?.EntityId ?? 0;
                var squads = session.Squads;
                if (threatEntity != null
                    && session.Npcs?.Npcs != null
                    && session.Npcs.Npcs.TryGetValue(threatEntity.EntityId, out var threatNpc)
                    && squads != null
                    && squads.TryGetAssignment(threatNpc.EntityId, out var assignment)
                    && session.TryGetLeaderPosition(assignment.Leader, out var leaderPosition))
                {
                    enemyLeaderPosition = leaderPosition;
                    enemyLeaderEntityId = assignment.Leader.Id;
                }

                _lastKnownEnemyLeaderPosition = enemyLeaderPosition;
                _lastKnownEnemyLeaderObservationTime = now;
                _hasLastKnownEnemyLeaderPosition = true;
                return true;
            }

            if (!_hasLastKnownEnemyLeaderPosition
                || now - _lastKnownEnemyLeaderObservationTime > _definition.TargetMemoryMilliseconds)
                return false;

            enemyLeaderPosition = _lastKnownEnemyLeaderPosition;
            enemyLeaderEntityId = 0;
            return true;
        }

        private void RememberAdvanceDirection(SiUtilityContext context, in Vector3D enemyLeaderPosition)
        {
            if (context?.Entity == null)
                return;

            Vector3D direction;
            if (!SiThreatSectorHelper.TryGetPlanarDirection(
                    context.Position,
                    enemyLeaderPosition,
                    ResolveUp(context.Position, context.Entity.WorldMatrix.Up),
                    out direction))
                return;

            _lastAdvanceDirection = direction;
            _hasLastAdvanceDirection = true;
        }

        private bool TryAssignForwardCover(
            SiUtilityContext context,
            SiNpcSessionComponent session,
            in Vector3D enemyLeaderPosition,
            long enemyLeaderEntityId,
            out Vector3D coverPosition,
            out Vector3D standPosition)
        {
            coverPosition = Vector3D.Zero;
            standPosition = Vector3D.Zero;

            if (context?.Entity == null)
                return false;

            var searchOrigin = context.Position;
            var up = ResolveUp(searchOrigin, context.Entity.WorldMatrix.Up);
            if (!SiThreatSectorHelper.TryGetPlanarDirection(searchOrigin, enemyLeaderPosition, up, out var advanceDirection))
            {
                if (!_hasLastAdvanceDirection)
                    return false;
                advanceDirection = _lastAdvanceDirection;
            }

            SiCoverScanCacheEntry cachedScan = null;
            SiCoverSearchCacheEntry cachedEvaluation = null;
            var scanStats = default(SiNearbyCoverScanner.ScanStats);
            if (session.TryGetCachedCoverScan(searchOrigin, _definition.SearchRadius, DefinitionId, out cachedScan))
            {
                _coverPositions.Clear();
                _coverPositions.AddRange(cachedScan.CoverPositions);
                scanStats.TotalSectors = cachedScan.ScannedSectors;
                scanStats.IntersectingSectors = cachedScan.IntersectingSectors;
                scanStats.FoliageEntries = cachedScan.FoliageEntries;
                scanStats.AcceptedCandidates = cachedScan.CandidateCount;
            }
            else
            {
                _coverPositions.Clear();
                _coverScanner.Scan(searchOrigin, _definition.SearchRadius, _coverPositions, out scanStats);
                var scanEntry = new SiCoverScanCacheEntry
                {
                    ScannedSectors = scanStats.TotalSectors,
                    IntersectingSectors = scanStats.IntersectingSectors,
                    FoliageEntries = scanStats.FoliageEntries,
                    CandidateCount = scanStats.AcceptedCandidates,
                };
                scanEntry.CoverPositions.AddRange(_coverPositions);
                session.StoreCachedCoverScan(searchOrigin, _definition.SearchRadius, DefinitionId, scanEntry);
            }

            if (_coverPositions.Count == 0)
                return false;

            List<SiCoverSearchCandidate> evaluatedCandidates;
            if (session.TryGetCachedCoverSearch(
                    searchOrigin,
                    _definition.SearchRadius,
                    enemyLeaderPosition,
                    enemyLeaderEntityId,
                    DefinitionId,
                    out cachedEvaluation))
            {
                evaluatedCandidates = cachedEvaluation.Candidates;
            }
            else
            {
                evaluatedCandidates = BuildEvaluatedCandidates(
                    context,
                    searchOrigin,
                    enemyLeaderPosition,
                    advanceDirection);
                var evaluationEntry = new SiCoverSearchCacheEntry
                {
                    ScannedSectors = scanStats.TotalSectors,
                    IntersectingSectors = scanStats.IntersectingSectors,
                    FoliageEntries = scanStats.FoliageEntries,
                    CandidateCount = scanStats.AcceptedCandidates,
                    ViableCount = evaluatedCandidates.Count,
                };
                evaluationEntry.Candidates.AddRange(evaluatedCandidates);
                session.StoreCachedCoverSearch(
                    searchOrigin,
                    _definition.SearchRadius,
                    enemyLeaderPosition,
                    enemyLeaderEntityId,
                    DefinitionId,
                    evaluationEntry);
            }

            var currentEnemyDistance = Vector3D.Distance(searchOrigin, enemyLeaderPosition);
            var bestDistanceSquared = double.MaxValue;
            for (var i = 0; i < evaluatedCandidates.Count; i++)
            {
                var candidate = evaluatedCandidates[i];
                if (_hasReservedCover
                    && Vector3D.DistanceSquared(candidate.CoverPosition, _reservedCoverPosition)
                       <= _definition.CoverOccupancyRadius * _definition.CoverOccupancyRadius)
                    continue;

                if (!session.IsCoverAvailable(context.Agent, candidate.CoverPosition, _definition.CoverOccupancyRadius))
                    continue;

                var candidateEnemyDistance = Vector3D.Distance(candidate.StandPosition, enemyLeaderPosition);
                if (currentEnemyDistance - candidateEnemyDistance < _definition.MinimumAdvanceProgressDistance)
                    continue;

                if (candidate.DistanceSquared >= bestDistanceSquared)
                    continue;

                bestDistanceSquared = candidate.DistanceSquared;
                coverPosition = candidate.CoverPosition;
                standPosition = candidate.StandPosition;
            }

            return bestDistanceSquared < double.MaxValue;
        }

        private List<SiCoverSearchCandidate> BuildEvaluatedCandidates(
            SiUtilityContext context,
            in Vector3D searchOrigin,
            in Vector3D enemyLeaderPosition,
            in Vector3D advanceDirection)
        {
            var results = new List<SiCoverSearchCandidate>(_coverPositions.Count);
            for (var i = 0; i < _coverPositions.Count; i++)
            {
                var candidate = _coverPositions[i];
                if (!IsInsideAdvanceSector(searchOrigin, candidate, advanceDirection, context.Position, context.Entity.WorldMatrix.Up))
                    continue;

                if (!TryResolveStandingPoint(
                        context,
                        candidate,
                        enemyLeaderPosition,
                        out var standPosition,
                        out var isTree))
                    continue;

                results.Add(new SiCoverSearchCandidate(
                    candidate,
                    standPosition,
                    isTree,
                    Vector3D.DistanceSquared(searchOrigin, candidate)));
            }

            return results;
        }

        private bool TryResolveStandingPoint(
            SiUtilityContext context,
            in Vector3D coverPosition,
            in Vector3D enemyLeaderPosition,
            out Vector3D bestStandPosition,
            out bool isTree)
        {
            bestStandPosition = Vector3D.Zero;
            isTree = false;

            var world = context.Entity.WorldMatrix;
            var up = ResolveUp(context.Position, world.Up);
            var awayFromEnemy = Vector3D.Reject(coverPosition - enemyLeaderPosition, up);
            awayFromEnemy = SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(
                awayFromEnemy,
                SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(
                    context.Position - coverPosition,
                    Vector3D.CalculatePerpendicularVector(up)));
            var side = SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(
                Vector3D.Cross(awayFromEnemy, up),
                Vector3D.CalculatePerpendicularVector(awayFromEnemy));

            for (var offset = _definition.MaximumCoverOffset; offset >= _definition.MinimumCoverOffset; offset -= _definition.CoverOffsetStep)
            {
                for (var sampleIndex = 0; sampleIndex < SideOffsetSamples.Length; sampleIndex++)
                {
                    var standPosition = coverPosition
                                        + awayFromEnemy * offset
                                        + side * SideOffsetSamples[sampleIndex];
                    if (!IsFiniteVector(standPosition))
                        continue;

                    bestStandPosition = standPosition;
                    isTree = offset >= _definition.PreferredTreeOffset;
                    return true;
                }
            }

            return false;
        }

        private bool IsInsideAdvanceSector(
            in Vector3D origin,
            in Vector3D candidatePosition,
            in Vector3D advanceDirection,
            in Vector3D referencePosition,
            in Vector3D referenceUp)
        {
            Vector3D candidateDirection;
            if (!SiThreatSectorHelper.TryGetPlanarDirection(
                    origin,
                    candidatePosition,
                    ResolveUp(referencePosition, referenceUp),
                    out candidateDirection))
                return false;

            var cosineThreshold = Math.Cos(MathHelper.ToRadians(_definition.AdvanceSectorAngleDegrees * 0.5f));
            return Vector3D.Dot(candidateDirection, advanceDirection) >= cosineThreshold;
        }

        private static Vector3D ResolveUp(in Vector3D position, in Vector3D fallbackUp)
        {
            var gravity = MyGravityProviderSystem.CalculateTotalGravityInPoint(position);
            if (gravity.LengthSquared() > MinimumDirectionLengthSquared)
                return -Vector3D.Normalize(gravity);
            return SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(fallbackUp, Vector3D.Up);
        }

        private static bool IsFiniteVector(in Vector3D value)
        {
            return !(double.IsNaN(value.X)
                     || double.IsInfinity(value.X)
                     || double.IsNaN(value.Y)
                     || double.IsInfinity(value.Y)
                     || double.IsNaN(value.Z)
                     || double.IsInfinity(value.Z));
        }

        private static long CurrentTimeMilliseconds()
        {
            var session = MyAPIGateway.Session;
            return session != null
                ? (long)session.ElapsedPlayTime.TotalMilliseconds
                : 0;
        }

        private enum AdvanceMode
        {
            None,
            Cover,
            Direct,
        }
    }
}
