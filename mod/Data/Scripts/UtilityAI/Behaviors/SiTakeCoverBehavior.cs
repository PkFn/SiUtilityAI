using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using Medieval.WorldEnvironment.Modules;
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
        private const long CoverSearchRetryMilliseconds = 1500;
        private const long InitialCoverSearchSpreadMilliseconds = CoverSearchRetryMilliseconds;
        private const long ExhaustedCoverRetryMilliseconds = 3000;
        private const long SlowCoverSearchLogCooldownMilliseconds = 5000;
        private const double SlowCoverSearchMilliseconds = 5;
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
        private long _lastNoCoverLogTime = long.MinValue;
        private long _lastReservationFailLogTime = long.MinValue;
        private long _lastCoverSearchTime = -1;
        private long _nextCoverSearchAllowedTime = -1;
        private long _lastSlowCoverSearchLogTime = long.MinValue;
        private string _lastCoverRejectReason;

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
                ResetCoverSearchSchedule();
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
            {
                ReleaseCover(session, context);
                ResetCoverSearchSchedule();
            }
        }

        internal bool IsRunningToCover(SiUtilityContext context)
        {
            if (context?.Agent == null || !_hasReservedCover)
                return false;

            return Vector3D.DistanceSquared(context.Position, _reservedStandPosition)
                   > _definition.CoverArrivalDistance * _definition.CoverArrivalDistance;
        }

        private void MaintainCover(SiUtilityContext context, bool forceRefresh)
        {
            var session = SiNpcSessionComponent.Instance;
            if (context?.Agent == null || session == null)
                return;

            if (session.GetCombatStance(context.Agent) != SiSquadCombatStance.Combat)
            {
                ReleaseCover(session, context);
                ResetCoverSearchSchedule();
                return;
            }

            var hasThreat = TryGetThreat(context, out var threatEntity, out var threatPosition);
            var hasUsableCurrentCover = HasUsableCurrentCover(context, session, hasThreat, threatEntity, threatPosition);
            var wantsSwitch = ShouldSearchForCover(forceRefresh, hasUsableCurrentCover);

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
                    else
                        LogWithCooldown(ref _lastReservationFailLogTime, $"[SiCover] reservation failed cover={FormatVector(coverPosition)}");
                }
                else
                {
                    session.TryReserveCover(context.Agent, _reservedCoverPosition, _definition.CoverOccupancyRadius);
                    _reservedStandPosition = standPosition;
                }
            }
            else if (wantsSwitch)
            {
                LogWithCooldown(
                    ref _lastNoCoverLogTime,
                    $"[SiCover] no valid cover found scanned={_coverPositions.Count} lastReject={_lastCoverRejectReason ?? "none"}");
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
            {
                LogWithCooldown(ref _lastReservationFailLogTime, $"[SiCover] lost reservation cover={FormatVector(_reservedCoverPosition)}");
                return false;
            }

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

        private bool ShouldSearchForCover(bool forceRefresh, bool hasUsableCurrentCover)
        {
            if (_nextCoverSearchAllowedTime < 0)
                InitializeCoverSearchSchedule();

            if (forceRefresh)
                return IsCoverSearchDue() && MarkCoverSearchAttempt();

            if (!_hasReservedCover || !hasUsableCurrentCover)
                return IsCoverSearchDue() && MarkCoverSearchAttempt();

            return false;
        }

        private bool IsCoverSearchDue()
        {
            var now = CurrentTimeMilliseconds();
            return now >= _nextCoverSearchAllowedTime;
        }

        private bool MarkCoverSearchAttempt()
        {
            var now = CurrentTimeMilliseconds();
            _lastCoverSearchTime = now;
            _nextCoverSearchAllowedTime = now + CoverSearchRetryMilliseconds;
            return true;
        }

        private void InitializeCoverSearchSchedule()
        {
            var now = CurrentTimeMilliseconds();
            _nextCoverSearchAllowedTime = now + ResolveInitialCoverSearchDelayMilliseconds();
        }

        private long ResolveInitialCoverSearchDelayMilliseconds()
        {
            var entityId = Entity?.EntityId ?? 0;
            if (InitialCoverSearchSpreadMilliseconds <= 0 || entityId == 0)
                return 0;

            return Math.Abs(entityId % (InitialCoverSearchSpreadMilliseconds + 1));
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

            Vector3D searchOrigin;
            if (!session.TryGetLeaderPosition(context.Agent, out searchOrigin))
                searchOrigin = context.Position;

            var cacheState = "none";
            var rawScanElapsedMilliseconds = 0d;
            var buildElapsedMilliseconds = 0d;
            SiCoverSearchCacheEntry cachedEvaluation = null;
            SiCoverScanCacheEntry cachedScan = null;
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
                cacheState = hasThreat ? "scan-hit" : "scan-hit/eval-none";
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
                cacheState = hasThreat ? "scan-miss" : "scan-miss/eval-none";
            }

            if (_coverPositions.Count == 0)
            {
                LogSlowCoverSearch(
                    context,
                    searchOrigin,
                    hasThreat,
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

            if (hasThreat)
            {
                var threatEntityId = threatEntity?.EntityId ?? 0;
                if (session.TryGetCachedCoverSearch(
                        searchOrigin,
                        _definition.SearchRadius,
                        threatPosition,
                        threatEntityId,
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
                    hasThreat,
                    threatEntity,
                    threatPosition,
                    scanStats,
                    out standingPointRejects,
                    out viableCandidates);
                buildElapsedMilliseconds = DebugElapsedMilliseconds(buildStartedAt);
                if (hasThreat)
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
                        threatPosition,
                        threatEntity?.EntityId ?? 0,
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
                    hasThreat,
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
                    hasThreat,
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

            if (viableCandidates > 0 && occupiedRejects >= viableCandidates)
                ApplyExhaustedCoverCooldown();

            LogSlowCoverSearch(
                context,
                searchOrigin,
                hasThreat,
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

        private void ApplyExhaustedCoverCooldown()
        {
            var now = CurrentTimeMilliseconds();
            _nextCoverSearchAllowedTime = Math.Max(_nextCoverSearchAllowedTime, now + ExhaustedCoverRetryMilliseconds);
        }

        private List<SiCoverSearchCandidate> BuildEvaluatedCandidates(
            SiUtilityContext context,
            in Vector3D searchOrigin,
            bool hasThreat,
            MyEntity threatEntity,
            in Vector3D threatPosition,
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
                        out var ignoredCoverScore))
                {
                    standingPointRejects++;
                    SetRejectReason($"ray reject cover={FormatVector(candidate)}");
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
                    if (!TryScoreStandingPoint(context, standPosition, threatEntity, threatPosition, up, out var score))
                        continue;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestStandPosition = standPosition;
                        isTree = offset >= _definition.PreferredTreeOffset;
                    }
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
                               + toThreat * GetCoverTestMuzzleForwardOffset();
            var bodyThreatDistance = Vector3D.Distance(bodyOrigin, aimPoint);
            var bodyFoliageBlockage = FoliageBlockage(bodyOrigin, aimPoint);

            double bodyHitDistance;
            var hasSolidBodyBlocker = TryGetBlockingHitDistance(
                bodyOrigin,
                aimPoint,
                context.Entity,
                threatEntity,
                out bodyHitDistance);
            if (!hasSolidBodyBlocker && bodyFoliageBlockage <= 0)
            {
                SetRejectReason(
                    $"body open stand={FormatVector(standPosition)} foliage={bodyFoliageBlockage:0.00}");
                return false;
            }

            double muzzleHitDistance;
            if (TryGetBlockingHitDistance(muzzleOrigin, aimPoint, context.Entity, threatEntity, out muzzleHitDistance))
            {
                SetRejectReason(
                    $"muzzle blocked stand={FormatVector(standPosition)} hitDistance={muzzleHitDistance:0.00}");
                return false;
            }

            var solidBodyCover = hasSolidBodyBlocker && bodyThreatDistance > 0.001
                ? MathHelper.Clamp((float)(1d - bodyHitDistance / bodyThreatDistance), 0, 1)
                : 0f;
            var normalizedCover = Math.Max(solidBodyCover, bodyFoliageBlockage);
            if (normalizedCover <= 0)
            {
                SetRejectReason($"cover too shallow stand={FormatVector(standPosition)} blockage={normalizedCover:0.00}");
                return false;
            }

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

        private static float FoliageBlockage(in Vector3D start, in Vector3D end)
        {
            return MathHelper.Clamp(
                MyFoliageRaycastEnvironmentModule.Intersect((Vector3)start, (Vector3)end),
                0,
                1);
        }

        private float GetAimTargetHeight() =>
            _shootBehavior?.GetWeaponAimHeightForCover() ?? _definition.BodyCoverAimHeight;

        private float GetMuzzleForwardOffset() =>
            _shootBehavior?.GetWeaponMuzzleForwardOffsetForCover() ?? 0.6f;

        private float GetCoverTestMuzzleForwardOffset() =>
            Math.Min(GetMuzzleForwardOffset(), _definition.BodyCoverForwardOffset);

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

        private void ResetCoverSearchSchedule()
        {
            _lastCoverSearchTime = -1;
            _nextCoverSearchAllowedTime = -1;
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
            _log.Warning($"entityId={Entity?.EntityId ?? 0} name={Entity?.Name ?? "null"} definition={DefinitionId.SubtypeName} debug slow-cover-search outcome={(foundCover ? "found" : "none")} cache={cacheState} totalMs={totalElapsedMilliseconds:0.00} rawScanMs={rawScanElapsedMilliseconds:0.00} buildMs={buildElapsedMilliseconds:0.00} filterMs={filterElapsedMilliseconds:0.00} hasThreat={hasThreat} searchOrigin={FormatVector(searchOrigin)} reserved={_hasReservedCover} scannedSectors={scanStats.TotalSectors} intersectingSectors={scanStats.IntersectingSectors} foliageEntries={scanStats.FoliageEntries} candidates={scanStats.AcceptedCandidates} viable={viableCandidates} occupiedRejects={occupiedRejects} standingRejects={standingPointRejects} lastReject={_lastCoverRejectReason ?? "none"} waypoint={FormatVector(context?.Waypoint ?? Vector3D.Zero)}"); // AGENT-DEBUG-LOG
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
