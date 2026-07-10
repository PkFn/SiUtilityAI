using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using Sandbox.ModAPI;
using SiCore.Core.Debug;
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
        public float TravelScore = 1f;
        public float ThreatFrontExclusionAngleDegrees;
        public float CoverRescanLeaderDistance;
        public float WaypointRefreshDistance;
        public float CoverOccupancyRadius;
        public float CoverArrivalDistance;
        public float MinimumCoverOffset;
        public float PreferredTreeOffset;
        public float MaximumCoverOffset;
        public float CoverOffsetStep;
        public float BodyCoverAimHeight;
        public float BodyCoverForwardOffset;
        public float SwitchDistanceFromLeader;
        public float FullSwitchDistanceFromLeader;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiTakeCoverBehaviorDefinition))]
    public class SiTakeCoverBehaviorDefinition : MyEntityComponentDefinition
    {
        public float SearchRadius { get; private set; }
        public float TravelScore { get; private set; }
        public float ThreatFrontExclusionAngleDegrees { get; private set; }
        public float CoverRescanLeaderDistance { get; private set; }
        public float WaypointRefreshDistance { get; private set; }
        public float CoverOccupancyRadius { get; private set; }
        public float CoverArrivalDistance { get; private set; }
        public float MinimumCoverOffset { get; private set; }
        public float PreferredTreeOffset { get; private set; }
        public float MaximumCoverOffset { get; private set; }
        public float CoverOffsetStep { get; private set; }
        public float BodyCoverAimHeight { get; private set; }
        public float BodyCoverForwardOffset { get; private set; }
        public float SwitchDistanceFromLeader { get; private set; }
        public float FullSwitchDistanceFromLeader { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiTakeCoverBehaviorDefinition)builder;
            SearchRadius = Math.Max(0, ob.SearchRadius);
            TravelScore = Math.Max(0, ob.TravelScore);
            ThreatFrontExclusionAngleDegrees = (float)SiThreatSectorHelper.ClampFrontExclusionAngleDegrees(ob.ThreatFrontExclusionAngleDegrees);
            CoverRescanLeaderDistance = Math.Max(0.1f, ob.CoverRescanLeaderDistance);
            WaypointRefreshDistance = Math.Max(0, ob.WaypointRefreshDistance);
            CoverOccupancyRadius = Math.Max(0.1f, ob.CoverOccupancyRadius);
            CoverArrivalDistance = Math.Max(0.1f, ob.CoverArrivalDistance);
            MinimumCoverOffset = Math.Max(0.1f, ob.MinimumCoverOffset);
            PreferredTreeOffset = Math.Max(MinimumCoverOffset, ob.PreferredTreeOffset);
            MaximumCoverOffset = Math.Max(PreferredTreeOffset, ob.MaximumCoverOffset);
            CoverOffsetStep = Math.Max(0.05f, ob.CoverOffsetStep);
            BodyCoverAimHeight = Math.Max(0, ob.BodyCoverAimHeight);
            BodyCoverForwardOffset = Math.Max(0, ob.BodyCoverForwardOffset);
            SwitchDistanceFromLeader = Math.Max(0, ob.SwitchDistanceFromLeader);
            FullSwitchDistanceFromLeader = Math.Max(SwitchDistanceFromLeader, ob.FullSwitchDistanceFromLeader);
        }
    }

    [MyComponent(typeof(MyObjectBuilder_SiTakeCoverBehavior))]
    [MyDefinitionRequired(typeof(SiTakeCoverBehaviorDefinition))]
    public class SiTakeCoverBehaviorComponent : MyEntityComponent, ISiUtilityBehavior
    {
        private const long LogCooldownMilliseconds = 2000;
        private const long SlowCoverSearchLogCooldownMilliseconds = 5000;
        private const double SlowCoverSearchMilliseconds = 5;
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

        private SiTakeCoverBehaviorDefinition _definition;
        private SiShootOpposingNpcBehaviorComponent _shootBehavior;
        private readonly SiGameLog _log = new SiGameLog(nameof(SiTakeCoverBehaviorComponent), "[SiCover]");
        private Vector3D _reservedCoverPosition;
        private Vector3D _reservedStandPosition;
        private bool _hasReservedCover;
        private bool _hasLastCoverSearchOrigin;
        private Vector3D _lastCoverSearchOrigin;
        private long _lastNoCoverLogTime = long.MinValue;
        private long _lastReservationFailLogTime = long.MinValue;
        private long _lastSlowCoverSearchLogTime = long.MinValue;
        private string _lastCoverRejectReason;
        private long _activeCombatToken = long.MinValue;
        private Vector3D _lastThreatDirection;
        private bool _hasLastThreatDirection;

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
            if (session.IsAiSquadLeader(context.Agent))
            {
                ResetCombatState(session, context);
                return 0;
            }

            if (session.GetCombatStance(context.Agent) != SiSquadCombatStance.Combat)
            {
                ResetCombatState(session, context);
                return 0;
            }

            var combatToken = session.GetCombatEntryToken(context.Agent);
            EnsureCombatState(combatToken, session, context);

            RefreshCoveredRoleIfNeeded(combatToken, session, context);

            var role = EnsureCombatRole(combatToken, session, context);
            if (role != SiCombatMovementRole.Covered)
                return 0;

            var hasThreat = TryGetThreat(context, out var threatEntity, out var threatPosition);
            RememberThreatDirectionIfAvailable(context, session, hasThreat, threatPosition);
            if (!_hasReservedCover)
                return 0;

            if (IsRunningToCover(context))
                return _definition.TravelScore;

            return !HasUsableCurrentCover(context, session, hasThreat, threatPosition)
                ? _definition.TravelScore
                : 0f;
        }

        void ISiUtilityBehavior.Begin(SiUtilityContext context)
        {
            MoveToReservedCover(context);
        }

        void ISiUtilityBehavior.Tick(SiUtilityContext context, long elapsedMilliseconds)
        {
            MoveToReservedCover(context);
        }

        void ISiUtilityBehavior.End(SiUtilityContext context)
        {
            // This behavior stops scoring as soon as the destination is reached.
            // Apply the hold posture here as well, so the final non-continuous
            // behavior tick cannot leave the NPC standing at cover.
            if (_hasReservedCover && !IsRunningToCover(context))
                context.TrySetCrouch(true);
        }

        internal bool IsRunningToCover(SiUtilityContext context)
        {
            if (context?.Agent == null || !_hasReservedCover)
                return false;

            return Vector3D.DistanceSquared(context.Position, _reservedStandPosition)
                   > _definition.CoverArrivalDistance * _definition.CoverArrivalDistance;
        }

        private void MoveToReservedCover(SiUtilityContext context)
        {
            var session = SiNpcSessionComponent.Instance;
            if (context?.Agent == null || session == null)
                return;

            if (session.GetCombatStance(context.Agent) != SiSquadCombatStance.Combat)
            {
                ResetCombatState(session, context);
                return;
            }

            if (!_hasReservedCover)
                return;

            session.TryReserveCover(context.Agent, _reservedCoverPosition, _definition.CoverOccupancyRadius);
            session.CacheCombatPosition(context.Agent, SiCombatMovementRole.Covered, _reservedStandPosition);
            context.TrySetCrouch(!IsRunningToCover(context));
            session.TryFollowCachedCombatPosition(
                context.Agent,
                SiCombatMovementRole.Covered,
                _definition.WaypointRefreshDistance * _definition.WaypointRefreshDistance);
        }

        private void EnsureCombatState(long combatToken, SiNpcSessionComponent session, SiUtilityContext context)
        {
            if (_activeCombatToken == combatToken)
                return;

            ReleaseCover(session, context);
            ResetCoverSearchState();
            _hasLastThreatDirection = false;
            _lastThreatDirection = Vector3D.Zero;
            context.TrySetCrouch(false);
            session.ClearCachedCombatPosition(context.Agent.EntityId, SiCombatMovementRole.Covered);
            _activeCombatToken = combatToken;
            context.Agent.ClearCombatMovementRole();
        }

        private void RefreshCoveredRoleIfNeeded(long combatToken, SiNpcSessionComponent session, SiUtilityContext context)
        {
            if (context.Agent.GetCombatMovementRole(combatToken) != SiCombatMovementRole.Covered)
                return;
            if (!session.IsFollowingPlayer(context.Agent))
                return;

            double leaderDistance;
            if (!session.TryGetLeaderDistance(context.Agent, out leaderDistance)
                || leaderDistance <= _definition.SwitchDistanceFromLeader)
                return;

            Vector3D searchOrigin;
            if (!session.TryGetLeaderPosition(context.Agent, out searchOrigin))
                searchOrigin = context.Position;

            if (_hasLastCoverSearchOrigin
                && Vector3D.DistanceSquared(_lastCoverSearchOrigin, searchOrigin)
                   < _definition.CoverRescanLeaderDistance * _definition.CoverRescanLeaderDistance)
                return;

            ReleaseCover(session, context);
            ResetCoverSearchState();
            context.TrySetCrouch(false);
            if (context.HasWaypoint)
                context.TryClearWaypoint();
            session.ClearCachedCombatPosition(context.Agent.EntityId, SiCombatMovementRole.Covered);
            context.Agent.SetCombatMovementRole(combatToken, SiCombatMovementRole.None);
        }

        private SiCombatMovementRole EnsureCombatRole(long combatToken, SiNpcSessionComponent session, SiUtilityContext context)
        {
            var currentRole = context.Agent.GetCombatMovementRole(combatToken);
            if (currentRole != SiCombatMovementRole.None)
                return currentRole;

            var hasThreat = TryGetThreat(context, out var threatEntity, out var threatPosition);
            var hasLeaderPosition = session.TryGetLeaderPosition(context.Agent, out var searchOrigin);
            if (!hasLeaderPosition)
                searchOrigin = context.Position;
            RememberThreatDirectionIfAvailable(context, session, hasThreat, threatPosition);
            var hasThreatDirection = ResolveThreatDirection(
                context,
                searchOrigin,
                hasLeaderPosition,
                hasThreat,
                threatPosition,
                out var threatDirection,
                out var effectiveThreatPosition);

            MarkCoverSearchAttempt(searchOrigin);
            if (FindBestCover(
                    context,
                    session,
                    hasThreatDirection,
                    effectiveThreatPosition,
                    searchOrigin,
                    hasThreatDirection,
                    threatDirection,
                    out var coverPosition,
                    out var standPosition)
                && session.TryReserveCover(context.Agent, coverPosition, _definition.CoverOccupancyRadius))
            {
                _reservedCoverPosition = coverPosition;
                _reservedStandPosition = standPosition;
                _hasReservedCover = true;
                session.CacheCombatPosition(context.Agent, SiCombatMovementRole.Covered, _reservedStandPosition);
                context.Agent.SetCombatMovementRole(combatToken, SiCombatMovementRole.Covered);
                return SiCombatMovementRole.Covered;
            }

            _hasReservedCover = false;
            session.ClearCachedCombatPosition(context.Agent.EntityId, SiCombatMovementRole.Covered);
            context.Agent.SetCombatMovementRole(combatToken, SiCombatMovementRole.PlainView);
            LogWithCooldown(
                ref _lastNoCoverLogTime,
                $"[SiCover] no valid cover found scanned={_coverPositions.Count} lastReject={_lastCoverRejectReason ?? "none"}");
            return SiCombatMovementRole.PlainView;
        }

        private void ResetCombatState(SiNpcSessionComponent session, SiUtilityContext context)
        {
            ReleaseCover(session, context);
            ResetCoverSearchState();
            _hasLastThreatDirection = false;
            _lastThreatDirection = Vector3D.Zero;
            session.ClearCachedCombatPosition(context.Agent?.EntityId ?? 0, SiCombatMovementRole.Covered);
            context.Agent.ClearCombatMovementRole();
            _activeCombatToken = long.MinValue;
        }

        private bool HasUsableCurrentCover(
            SiUtilityContext context,
            SiNpcSessionComponent session,
            bool hasThreat,
            in Vector3D threatPosition)
        {
            if (!_hasReservedCover)
                return false;

            if (!session.TryReserveCover(context.Agent, _reservedCoverPosition, _definition.CoverOccupancyRadius))
            {
                LogWithCooldown(ref _lastReservationFailLogTime, $"[SiCover] lost reservation cover={FormatVector(_reservedCoverPosition)}");
                return false;
            }

            if (Vector3D.DistanceSquared(context.Position, _reservedStandPosition)
                > _definition.CoverArrivalDistance * _definition.CoverArrivalDistance)
                return true;

            return !hasThreat || TryRefreshReservedStandPosition(context, threatPosition);
        }

        private bool UpdateStandingPointIfNear(in Vector3D standPosition)
        {
            if (Vector3D.DistanceSquared(_reservedStandPosition, standPosition)
                <= _definition.WaypointRefreshDistance * _definition.WaypointRefreshDistance)
                return true;

            _reservedStandPosition = standPosition;
            return true;
        }

        private void MarkCoverSearchAttempt(in Vector3D searchOrigin)
        {
            _lastCoverSearchOrigin = searchOrigin;
            _hasLastCoverSearchOrigin = true;
        }

        private bool FindBestCover(
            SiUtilityContext context,
            SiNpcSessionComponent session,
            bool hasThreatDirection,
            in Vector3D threatReferencePosition,
            in Vector3D searchOrigin,
            bool useThreatSector,
            in Vector3D threatDirection,
            out Vector3D coverPosition,
            out Vector3D standPosition)
        {
            coverPosition = Vector3D.Zero;
            standPosition = Vector3D.Zero;

            var cacheState = "none";
            var rawScanElapsedMilliseconds = 0d;
            var buildElapsedMilliseconds = 0d;
            SiCoverSearchCacheEntry cachedEvaluation = null;
            SiCoverScanCacheEntry cachedScan = null;
            var effectiveThreatReferencePosition = hasThreatDirection
                ? threatReferencePosition
                : GuessThreatPosition(context, searchOrigin);
            var rawScanStartedAt = DebugTimestampTicks();
            var scanStats = default(SiNearbyCoverScanner.ScanStats);
            if (session.TryGetCachedCoverScan(
                    searchOrigin,
                    _definition.SearchRadius,
                    DefinitionId,
                    out cachedScan))
            {
                _coverPositions.Clear();
                _coverPositions.AddRange(cachedScan.CoverPositions);
            }
            else
            {
                _coverPositions.Clear();
                _coverScanner.Scan(searchOrigin, _definition.SearchRadius, _coverPositions, out scanStats);
            }
            rawScanElapsedMilliseconds = DebugElapsedMilliseconds(rawScanStartedAt);

            if (cachedScan != null)
            {
                scanStats.TotalSectors = cachedScan.ScannedSectors;
                scanStats.IntersectingSectors = cachedScan.IntersectingSectors;
                scanStats.FoliageEntries = cachedScan.FoliageEntries;
                scanStats.AcceptedCandidates = cachedScan.CandidateCount;
                cacheState = hasThreatDirection ? "scan-hit" : "scan-hit/eval-none";
            }
            else
            {
                var scanCacheEntry = new SiCoverScanCacheEntry
                {
                    ScannedSectors = scanStats.TotalSectors,
                    IntersectingSectors = scanStats.IntersectingSectors,
                    FoliageEntries = scanStats.FoliageEntries,
                    CandidateCount = scanStats.AcceptedCandidates,
                };
                scanCacheEntry.CoverPositions.AddRange(_coverPositions);
                session.StoreCachedCoverScan(searchOrigin, _definition.SearchRadius, DefinitionId, scanCacheEntry);
                cacheState = hasThreatDirection ? "scan-miss" : "scan-miss/eval-none";
            }

            if (_coverPositions.Count == 0)
            {
                LogSlowCoverSearch(
                    context,
                    searchOrigin,
                    hasThreatDirection,
                    rawScanElapsedMilliseconds,
                    buildElapsedMilliseconds,
                    0,
                    0,
                    0,
                    0,
                    false,
                    scanStats,
                    cacheState);
                return false;
            }

            if (hasThreatDirection)
            {
                if (session.TryGetCachedCoverSearch(
                        searchOrigin,
                        _definition.SearchRadius,
                        effectiveThreatReferencePosition,
                        0,
                        DefinitionId,
                        out cachedEvaluation))
                    cacheState += "/eval-hit";
                else
                    cacheState += "/eval-miss";
            }

            var standingPointRejects = 0;
            var viableCandidates = 0;
            List<SiCoverSearchCandidate> evaluatedCandidates;
            if (cachedEvaluation != null)
            {
                standingPointRejects = cachedEvaluation.StandingRejects;
                viableCandidates = cachedEvaluation.ViableCount;
                evaluatedCandidates = cachedEvaluation.Candidates;
            }
            else
            {
                var buildStartedAt = DebugTimestampTicks();
                evaluatedCandidates = BuildEvaluatedCandidates(
                    context,
                    searchOrigin,
                    effectiveThreatReferencePosition,
                    useThreatSector,
                    threatDirection,
                    scanStats,
                    out standingPointRejects,
                    out viableCandidates);
                buildElapsedMilliseconds = DebugElapsedMilliseconds(buildStartedAt);
                if (hasThreatDirection)
                {
                    var cacheEntry = new SiCoverSearchCacheEntry
                    {
                        ScannedSectors = scanStats.TotalSectors,
                        IntersectingSectors = scanStats.IntersectingSectors,
                        FoliageEntries = scanStats.FoliageEntries,
                        CandidateCount = scanStats.AcceptedCandidates,
                        StandingRejects = standingPointRejects,
                        ViableCount = viableCandidates,
                    };
                    cacheEntry.Candidates.AddRange(evaluatedCandidates);
                    session.StoreCachedCoverSearch(
                        searchOrigin,
                        _definition.SearchRadius,
                        effectiveThreatReferencePosition,
                        0,
                        DefinitionId,
                        cacheEntry);
                }
            }

            var filterStartedAt = DebugTimestampTicks();
            var bestTreeDistanceSquared = double.MaxValue;
            var bestBushDistanceSquared = double.MaxValue;
            var bestTreeCover = Vector3D.Zero;
            var bestBushCover = Vector3D.Zero;
            var bestTreeStand = Vector3D.Zero;
            var bestBushStand = Vector3D.Zero;
            var occupiedRejects = 0;

            for (var i = 0; i < evaluatedCandidates.Count; i++)
            {
                var candidate = evaluatedCandidates[i];
                if (!session.IsCoverAvailable(context.Agent, candidate.CoverPosition, _definition.CoverOccupancyRadius))
                {
                    occupiedRejects++;
                    SetRejectReason($"occupied cover={FormatVector(candidate.CoverPosition)}");
                    continue;
                }

                if (candidate.IsTree)
                {
                    if (candidate.DistanceSquared < bestTreeDistanceSquared)
                    {
                        bestTreeDistanceSquared = candidate.DistanceSquared;
                        bestTreeCover = candidate.CoverPosition;
                        bestTreeStand = candidate.StandPosition;
                    }
                }
                else if (candidate.DistanceSquared < bestBushDistanceSquared)
                {
                    bestBushDistanceSquared = candidate.DistanceSquared;
                    bestBushCover = candidate.CoverPosition;
                    bestBushStand = candidate.StandPosition;
                }
            }

            var filterElapsedMilliseconds = DebugElapsedMilliseconds(filterStartedAt);
            if (bestTreeDistanceSquared < double.MaxValue)
            {
                coverPosition = bestTreeCover;
                standPosition = bestTreeStand;
                LogSlowCoverSearch(
                    context,
                    searchOrigin,
                    hasThreatDirection,
                    rawScanElapsedMilliseconds,
                    buildElapsedMilliseconds,
                    filterElapsedMilliseconds,
                    occupiedRejects,
                    standingPointRejects,
                    viableCandidates,
                    true,
                    scanStats,
                    cacheState);
                return true;
            }

            if (bestBushDistanceSquared < double.MaxValue)
            {
                coverPosition = bestBushCover;
                standPosition = bestBushStand;
                LogSlowCoverSearch(
                    context,
                    searchOrigin,
                    hasThreatDirection,
                    rawScanElapsedMilliseconds,
                    buildElapsedMilliseconds,
                    filterElapsedMilliseconds,
                    occupiedRejects,
                    standingPointRejects,
                    viableCandidates,
                    true,
                    scanStats,
                    cacheState);
                return true;
            }

            LogSlowCoverSearch(
                context,
                searchOrigin,
                hasThreatDirection,
                rawScanElapsedMilliseconds,
                buildElapsedMilliseconds,
                filterElapsedMilliseconds,
                occupiedRejects,
                standingPointRejects,
                viableCandidates,
                false,
                scanStats,
                cacheState);
            return false;
        }

        private List<SiCoverSearchCandidate> BuildEvaluatedCandidates(
            SiUtilityContext context,
            in Vector3D searchOrigin,
            in Vector3D threatReferencePosition,
            bool useThreatSector,
            in Vector3D threatDirection,
            SiNearbyCoverScanner.ScanStats scanStats,
            out int standingPointRejects,
            out int viableCandidates)
        {
            var results = new List<SiCoverSearchCandidate>(scanStats.AcceptedCandidates);
            standingPointRejects = 0;
            viableCandidates = 0;

            for (var i = 0; i < _coverPositions.Count; i++)
            {
                var candidate = _coverPositions[i];
                if (useThreatSector
                    && IsInThreatFrontSector(searchOrigin, candidate, threatDirection, context.Position, context.Entity.WorldMatrix.Up))
                {
                    SetRejectReason($"front-sector cover={FormatVector(candidate)}");
                    continue;
                }

                if (!TryResolveStandingPoint(
                        context,
                        candidate,
                        threatReferencePosition,
                        out var candidateStand,
                        out var isTree))
                {
                    standingPointRejects++;
                    SetRejectReason($"standpoint reject cover={FormatVector(candidate)}");
                    continue;
                }

                viableCandidates++;
                results.Add(new SiCoverSearchCandidate(
                    candidate,
                    candidateStand,
                    isTree,
                    Vector3D.DistanceSquared(searchOrigin, candidate)));
            }

            return results;
        }

        private bool TryResolveStandingPoint(
            SiUtilityContext context,
            in Vector3D coverPosition,
            in Vector3D threatReferencePosition,
            out Vector3D bestStandPosition,
            out bool isTree)
        {
            bestStandPosition = Vector3D.Zero;
            isTree = false;

            var world = context.Entity.WorldMatrix;
            var up = ResolveUp(context.Position, world.Up);
            var awayFromThreat = Vector3D.Reject(coverPosition - threatReferencePosition, up);
            awayFromThreat = SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(
                awayFromThreat,
                SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(
                    context.Position - coverPosition,
                    Vector3D.CalculatePerpendicularVector(up)));
            var side = SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(
                Vector3D.Cross(awayFromThreat, up),
                Vector3D.CalculatePerpendicularVector(awayFromThreat));

            for (var offset = _definition.MaximumCoverOffset; offset >= _definition.MinimumCoverOffset; offset -= _definition.CoverOffsetStep)
            {
                for (var sampleIndex = 0; sampleIndex < SideOffsetSamples.Length; sampleIndex++)
                {
                    var standPosition = coverPosition
                                        + awayFromThreat * offset
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

        private Vector3D GuessThreatPosition(SiUtilityContext context, in Vector3D coverPosition)
        {
            var forward = SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(
                Vector3D.Reject(context.Entity.WorldMatrix.Forward, ResolveUp(context.Position, context.Entity.WorldMatrix.Up)),
                Vector3D.Forward);
            return coverPosition + forward * Math.Max(15f, _definition.SearchRadius);
        }

        private void RememberThreatDirectionIfAvailable(
            SiUtilityContext context,
            SiNpcSessionComponent session,
            bool hasThreat,
            in Vector3D threatPosition)
        {
            if (!hasThreat || context?.Agent == null || session == null)
                return;

            Vector3D leaderPosition;
            if (!session.TryGetLeaderPosition(context.Agent, out leaderPosition))
                leaderPosition = context.Position;

            Vector3D threatDirection;
            if (!SiThreatSectorHelper.TryGetPlanarDirection(
                    leaderPosition,
                    threatPosition,
                    ResolveUp(leaderPosition, context.Entity.WorldMatrix.Up),
                    out threatDirection))
                return;

            _lastThreatDirection = threatDirection;
            _hasLastThreatDirection = true;
        }

        private bool ResolveThreatDirection(
            SiUtilityContext context,
            in Vector3D searchOrigin,
            bool hasLeaderPosition,
            bool hasThreat,
            in Vector3D threatPosition,
            out Vector3D threatDirection,
            out Vector3D effectiveThreatPosition)
        {
            threatDirection = Vector3D.Zero;
            effectiveThreatPosition = threatPosition;
            var up = ResolveUp(searchOrigin, context.Entity.WorldMatrix.Up);
            if (hasThreat
                && SiThreatSectorHelper.TryGetPlanarDirection(searchOrigin, threatPosition, up, out threatDirection))
            {
                effectiveThreatPosition = threatPosition;
                return true;
            }

            if (hasLeaderPosition && _hasLastThreatDirection)
            {
                threatDirection = _lastThreatDirection;
                effectiveThreatPosition = searchOrigin + threatDirection * Math.Max(15f, _definition.SearchRadius);
                return true;
            }

            return false;
        }

        private bool IsInThreatFrontSector(
            in Vector3D origin,
            in Vector3D candidatePosition,
            in Vector3D threatDirection,
            in Vector3D referencePosition,
            in Vector3D referenceUp)
        {
            return SiThreatSectorHelper.IsInsideFrontExclusionSector(
                origin,
                candidatePosition,
                threatDirection,
                ResolveUp(referencePosition, referenceUp),
                _definition.ThreatFrontExclusionAngleDegrees);
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

        private bool TryRefreshReservedStandPosition(SiUtilityContext context, in Vector3D threatPosition)
        {
            Vector3D refreshedStandPosition;
            bool ignoredIsTree;
            if (!TryResolveStandingPoint(context, _reservedCoverPosition, threatPosition, out refreshedStandPosition, out ignoredIsTree))
                return true;

            return UpdateStandingPointIfNear(refreshedStandPosition);
        }

        private void ReleaseCover(SiNpcSessionComponent session, SiUtilityContext context)
        {
            if (_hasReservedCover)
                session.ReleaseCover(context.Agent.EntityId);
            session.ClearCachedCombatPosition(context.Agent?.EntityId ?? 0, SiCombatMovementRole.Covered);
            _hasReservedCover = false;
            _reservedCoverPosition = Vector3D.Zero;
            _reservedStandPosition = Vector3D.Zero;
        }

        private void ResetCoverSearchState()
        {
            _hasLastCoverSearchOrigin = false;
            _lastCoverSearchOrigin = Vector3D.Zero;
        }

        private void SetRejectReason(string reason)
        {
            _lastCoverRejectReason = reason;
        }

        private void LogWithCooldown(ref long lastLogTime, string message)
        {
            var now = CurrentTimeMilliseconds();
            if (lastLogTime >= 0 && now - lastLogTime < LogCooldownMilliseconds)
                return;

            lastLogTime = now;
            Log(message);
        }

        private void Log(string message)
        {
            _log.Warning($"entityId={Entity?.EntityId ?? 0} name={Entity?.Name ?? "null"} definition={DefinitionId.SubtypeName} {message}");
        }

        private void LogSlowCoverSearch(
            SiUtilityContext context,
            in Vector3D searchOrigin,
            bool hasThreat,
            double rawScanElapsedMilliseconds,
            double buildElapsedMilliseconds,
            double filterElapsedMilliseconds,
            int occupiedRejects,
            int standingPointRejects,
            int viableCandidates,
            bool foundCover,
            SiNearbyCoverScanner.ScanStats scanStats,
            string cacheState)
        {
            var totalElapsedMilliseconds = rawScanElapsedMilliseconds + buildElapsedMilliseconds + filterElapsedMilliseconds;
            var now = CurrentTimeMilliseconds();
            if (totalElapsedMilliseconds < SlowCoverSearchMilliseconds
                && (scanStats.AcceptedCandidates <= 24 || standingPointRejects <= 24))
                return;
            if (_lastSlowCoverSearchLogTime >= 0
                && now - _lastSlowCoverSearchLogTime < SlowCoverSearchLogCooldownMilliseconds)
                return;

            _lastSlowCoverSearchLogTime = now;
        }

        private static long DebugTimestampTicks()
        {
            return DateTime.UtcNow.Ticks;
        }

        private static double DebugElapsedMilliseconds(long startTicks)
        {
            return (DateTime.UtcNow.Ticks - startTicks) / (double)TimeSpan.TicksPerMillisecond;
        }

        private static long CurrentTimeMilliseconds()
        {
            var session = MyAPIGateway.Session;
            return session != null
                ? (long)session.ElapsedPlayTime.TotalMilliseconds
                : 0;
        }

        private static string FormatVector(in Vector3D value) =>
            $"{value.X:0.0},{value.Y:0.0},{value.Z:0.0}";
    }
}
