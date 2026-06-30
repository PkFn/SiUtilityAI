using System;
using System.Collections.Generic;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.Players;
using Sandbox.Game.SessionComponents;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRageMath;
using VRage.ObjectBuilders;
using VRage.Session;

namespace Si.UtilityAI
{
    internal struct SiSpottingObservation
    {
        public static readonly SiSpottingObservation None = new SiSpottingObservation(false, 0, 1);

        public SiSpottingObservation(bool isSpotted, float spottingSum, float spottingThreshold)
        {
            IsSpotted = isSpotted;
            SpottingSum = spottingSum;
            SpottingThreshold = spottingThreshold;
        }

        public bool IsSpotted { get; }
        public float SpottingSum { get; }
        public float SpottingThreshold { get; }
    }

    internal sealed class SiSpottingSystem
    {
        private static readonly MyDefinitionId DefaultDefinitionId =
            new MyDefinitionId(typeof(MyObjectBuilder_SiSpottingSystemDefinition), "SiDefaultSpottingSystem");

        private readonly Dictionary<SpottingKey, SpottingState> _observations =
            new Dictionary<SpottingKey, SpottingState>();
        private readonly Dictionary<long, long> _recentShotTimes =
            new Dictionary<long, long>();
        private readonly Dictionary<long, long> _recentPlayerEvidenceTimes =
            new Dictionary<long, long>();
        private readonly List<SpottingKey> _removals = new List<SpottingKey>();
        private readonly SiNpcSessionComponent _session;
        private readonly SiNearbyEnvironmentScanner _environmentScanner = new SiNearbyEnvironmentScanner();

        private MySectorWeatherComponent _weather;

        public SiSpottingSystem(SiNpcSessionComponent session)
        {
            _session = session;
            Definition = LoadDefinition();
        }

        public SiSpottingSystemDefinition Definition { get; }

        public void Clear()
        {
            _observations.Clear();
            _recentShotTimes.Clear();
            _recentPlayerEvidenceTimes.Clear();
            _removals.Clear();
            _weather = null;
        }

        public void Update(long elapsedMilliseconds)
        {
            TryResolveWeather();
            UpdatePlayerFiringEvidence();

            if (_observations.Count == 0)
                return;

            var now = CurrentTimeMilliseconds();
            _removals.Clear();
            foreach (var pair in _observations)
            {
                var state = pair.Value;
                if (state == null || state.Definition == null)
                {
                    _removals.Add(pair.Key);
                    continue;
                }

                if (now - state.LastRequestedTime > TrackingTimeoutMilliseconds())
                {
                    _removals.Add(pair.Key);
                    continue;
                }

                if (now < state.NextEvaluationTime)
                    continue;

                var observer = ResolveObserver(state.ObserverId);
                var target = ResolveEntity(state.TargetId);
                Evaluate(state, observer, target, now);
            }

            for (var i = 0; i < _removals.Count; i++)
                _observations.Remove(_removals[i]);
        }

        public SiSpottingObservation ObserveTarget(
            SiNpc observer,
            MyEntity target,
            SiShootOpposingNpcBehaviorDefinition definition,
            float aimHeight,
            double distance)
        {
            if (observer == null || target == null || definition == null)
                return SiSpottingObservation.None;

            var key = new SpottingKey(observer.EntityId, target.EntityId);
            if (!_observations.TryGetValue(key, out var state))
            {
                state = new SpottingState
                {
                    System = this,
                    ObserverId = observer.EntityId,
                    TargetId = target.EntityId,
                    LastAwarenessUpdateTime = CurrentTimeMilliseconds(),
                };
                _observations.Add(key, state);
            }

            state.Definition = definition;
            state.AimHeight = aimHeight;
            state.LastRequestedTime = CurrentTimeMilliseconds();

            if (state.NextEvaluationTime <= 0 || CurrentTimeMilliseconds() >= state.NextEvaluationTime)
                Evaluate(state, observer, target, CurrentTimeMilliseconds(), distance);

            var sharedSpottingSum = GetSharedSpottingSum(observer, target, state.SpottingSum);
            var isSpotted = sharedSpottingSum >= state.SpottingThreshold;
            return new SiSpottingObservation(isSpotted, sharedSpottingSum, state.SpottingThreshold);
        }

        public void ReportShot(long shooterEntityId, MyEntity shooter)
        {
            if (shooterEntityId == 0 || shooter == null)
                return;

            var now = CurrentTimeMilliseconds();
            _recentShotTimes[shooterEntityId] = now;
            ApplyShotEvidence(shooterEntityId, shooter.WorldMatrix.Translation, now);
        }

        public bool HasSpottedTargetNearby(long observerEntityId, double distance)
        {
            if (observerEntityId == 0 || distance < 0)
                return false;

            var observer = ResolveObserver(observerEntityId);
            var observerEntity = observer?.Entity;
            if (observerEntity == null)
                return false;

            var distanceSquared = distance * distance;
            foreach (var pair in _observations)
            {
                var state = pair.Value;
                if (state == null || !state.IsSpotted || state.ObserverId != observerEntityId)
                    continue;

                var target = ResolveEntity(state.TargetId);
                if (target == null || target.Closed || target.MarkedForClose || !target.InScene)
                    continue;

                if (Vector3D.DistanceSquared(observerEntity.WorldMatrix.Translation, target.WorldMatrix.Translation) <= distanceSquared)
                    return true;
            }

            return false;
        }

        private void ApplyShotEvidence(long shooterEntityId, Vector3D shooterPosition, long now)
        {
            foreach (var pair in _observations)
            {
                var state = pair.Value;
                if (state == null
                    || state.TargetId != shooterEntityId
                    || state.Definition == null)
                    continue;

                var observer = ResolveObserver(state.ObserverId);
                if (observer?.Entity == null)
                    continue;

                DecayAwareness(state, now);

                var distance = Vector3D.Distance(
                    observer.Entity.WorldMatrix.Translation,
                    shooterPosition);
                if (Definition == null
                    || Definition.ShotAwarenessMaxDistance <= 0
                    || distance > Definition.ShotAwarenessMaxDistance)
                    continue;

                var normalized = 1f - (float)(distance / Definition.ShotAwarenessMaxDistance);
                normalized = MathHelper.Clamp(normalized, 0, 1);
                var gain = Definition.ShotAwarenessPerShot
                           * (float)Math.Pow(normalized, Definition.ShotAwarenessDistanceExponent);
                state.ShotAwareness = MathHelper.Clamp(state.ShotAwareness + gain, 0, 1);
            }
        }

        private void Evaluate(
            SpottingState state,
            SiNpc observer,
            MyEntity target,
            long now,
            double? knownDistance = null)
        {
            DecayAwareness(state, now);

            if (observer?.Entity == null || target == null || !target.InScene || target.Closed || target.MarkedForClose)
            {
                state.IsSpotted = false;
                state.SpottingSum = 0;
                state.SpottingThreshold = 1;
                state.NextEvaluationTime = now + EvaluationInterval(state);
                return;
            }

            var observerPosition = observer.Entity.WorldMatrix.Translation;
            var targetPosition = target.WorldMatrix.Translation;
            var distance = knownDistance ?? Vector3D.Distance(observerPosition, targetPosition);
            if (Definition != null
                && Definition.HearingGuaranteedRadius > 0
                && distance <= Definition.HearingGuaranteedRadius)
            {
                state.SpottingSum = 1;
                state.SpottingThreshold = 0;
                state.IsSpotted = true;
                state.NextEvaluationTime = now + EvaluationInterval(state);
                return;
            }

            if (state.Definition.RequireLineOfSight
                && !SiShootOpposingNpcBehaviorComponent.HasLineOfSight(
                    observer.Entity,
                    target,
                    state.AimHeight))
            {
                state.SpottingSum = 0;
                state.SpottingThreshold = ComputeSpottingThreshold(Definition, distance);
                state.IsSpotted = false;
                state.NextEvaluationTime = now + EvaluationInterval(state);
                return;
            }

            var spottingSum = ComputeVisualChance(target, now);
            spottingSum = 1f - (1f - spottingSum) * (1f - state.ShotAwareness);
            spottingSum = MathHelper.Clamp(spottingSum, 0, 1);

            state.SpottingSum = spottingSum;
            state.SpottingThreshold = ComputeSpottingThreshold(Definition, distance);
            var wasSpotted = state.IsSpotted;
            state.IsSpotted = spottingSum >= state.SpottingThreshold;
            if (!wasSpotted && state.IsSpotted)
                _session?.ReportNpcSpottedTarget(state.ObserverId, state.TargetId);
            state.NextEvaluationTime = now + EvaluationInterval(state);
        }

        private static float ComputeSpottingThreshold(
            SiSpottingSystemDefinition definition,
            double distance)
        {
            if (definition == null)
                return 1f;

            var normalizedDistance = (float)Math.Max(0, distance) / 500f;
            var threshold = definition.Constant
                            * (float)Math.Pow(normalizedDistance, 0.5f);
            return MathHelper.Clamp(threshold, 0, 1);
        }

        private float ComputeVisualChance(
            MyEntity target,
            long now)
        {
            var chance = 1f;
            var definition = Definition;
            if (definition == null)
                return chance;

            if (TargetSpeed(target) <= definition.StillnessVelocityThreshold)
                chance *= definition.StillnessChanceMultiplier;

            if (!HasRecentShot(target.EntityId, definition.RecentShotMilliseconds, now))
                chance *= definition.NotFiringChanceMultiplier;

            chance *= BushMultiplier(target.WorldMatrix.Translation, definition);
            chance *= DarknessMultiplier(target.WorldMatrix.Translation, definition);
            return MathHelper.Clamp(chance, 0, 1);
        }

        private float BushMultiplier(in Vector3D position, SiSpottingSystemDefinition definition)
        {
            if (definition.NearbyBushScanRadius <= 0)
                return 1f;

            var sample = _environmentScanner.Scan(position, definition.NearbyBushScanRadius);
            if (!sample.HasBush)
                return 1f;

            var normalizedDistance = definition.NearbyBushScanRadius > 0
                ? (float)(sample.NearestBushDistance / definition.NearbyBushScanRadius)
                : 1f;
            normalizedDistance = MathHelper.Clamp(normalizedDistance, 0, 1);
            var easedDistance = (float)Math.Pow(normalizedDistance, definition.NearbyBushDistanceExponent);
            return MathHelper.Lerp(definition.NearbyBushMinimumChanceMultiplier, 1f, easedDistance);
        }

        private float DarknessMultiplier(in Vector3D position, SiSpottingSystemDefinition definition)
        {
            TryResolveWeather();
            if (_weather == null)
                return 1f;

            var observation = _weather.CreateSolarObservation(_weather.CurrentTime, position);
            var night = Math.Min(definition.DarknessNightSolarElevation, definition.DarknessDaySolarElevation);
            var day = Math.Max(definition.DarknessNightSolarElevation, definition.DarknessDaySolarElevation);
            var normalized = day - night > 0.0001f
                ? MathHelper.Clamp((float)((observation.SolarElevation - night) / (day - night)), 0, 1)
                : (observation.SolarElevation >= day ? 1f : 0f);
            var multiplier = MathHelper.Lerp(definition.DarknessMinimumChanceMultiplier, 1f, normalized);
            if (observation.IsInside || observation.IsUnderground)
                multiplier *= definition.InteriorChanceMultiplier;
            return MathHelper.Clamp(multiplier, 0, 1);
        }

        private bool HasRecentShot(long entityId, int windowMilliseconds, long now)
        {
            if (windowMilliseconds <= 0)
                return false;
            return _recentShotTimes.TryGetValue(entityId, out var lastShot)
                   && now - lastShot <= windowMilliseconds;
        }

        private double TargetSpeed(MyEntity target)
        {
            if (target == null)
                return 0;

            if (_session?.Npcs != null
                && _session.Npcs.Npcs.TryGetValue(target.EntityId, out var npc))
            {
                if (npc is SiGroundedNpc grounded)
                    return grounded.Velocity.Length();
                return npc.Entity?.Physics?.LinearVelocity.Length() ?? 0;
            }

            return target.Physics?.LinearVelocity.Length() ?? 0;
        }

        private void UpdatePlayerFiringEvidence()
        {
            if (MyPlayers.Static == null)
                return;

            var now = CurrentTimeMilliseconds();
            foreach (var pair in MyPlayers.Static.GetAllPlayers())
            {
                var player = pair.Value;
                var entity = player?.ControlledEntity;
                if (entity == null || !IsLikelyRangedAction(entity))
                    continue;

                var interval = PlayerEvidenceIntervalMilliseconds(entity.EntityId);
                if (_recentPlayerEvidenceTimes.TryGetValue(entity.EntityId, out var lastEvidence)
                    && now - lastEvidence < interval)
                    continue;

                _recentPlayerEvidenceTimes[entity.EntityId] = now;
                _recentShotTimes[entity.EntityId] = now;
                ApplyShotEvidence(entity.EntityId, entity.WorldMatrix.Translation, now);
            }
        }

        private int PlayerEvidenceIntervalMilliseconds(long entityId)
        {
            foreach (var pair in _observations)
            {
                var state = pair.Value;
                if (state == null || state.TargetId != entityId || state.Definition == null)
                    continue;
                return EvaluationInterval(state);
            }

            return 200;
        }

        private static bool IsLikelyRangedAction(MyEntity entity)
        {
            var handItems = entity?.Components?.Get<MyCharacterHandItemsComponent>();
            var behavior = handItems?.GetBehavior<MyHandItemBehaviorBase>();
            if (handItems?.MainHand == null || behavior == null || !behavior.IsActive)
                return false;

            var subtype = handItems.MainHand.Subtype.String ?? string.Empty;
            return subtype.IndexOf("rifle", StringComparison.OrdinalIgnoreCase) >= 0
                   || subtype.IndexOf("smg", StringComparison.OrdinalIgnoreCase) >= 0
                   || subtype.IndexOf("gun", StringComparison.OrdinalIgnoreCase) >= 0
                   || subtype.IndexOf("pistol", StringComparison.OrdinalIgnoreCase) >= 0
                   || subtype.IndexOf("revolver", StringComparison.OrdinalIgnoreCase) >= 0
                   || subtype.IndexOf("garand", StringComparison.OrdinalIgnoreCase) >= 0
                   || subtype.IndexOf("mosin", StringComparison.OrdinalIgnoreCase) >= 0
                   || subtype.IndexOf("kar", StringComparison.OrdinalIgnoreCase) >= 0
                   || subtype.IndexOf("k98", StringComparison.OrdinalIgnoreCase) >= 0
                   || subtype.IndexOf("thompson", StringComparison.OrdinalIgnoreCase) >= 0
                   || subtype.IndexOf("ppsh", StringComparison.OrdinalIgnoreCase) >= 0
                   || subtype.IndexOf("mp40", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void TryResolveWeather()
        {
            if (_weather == null)
                _weather = MySession.Static?.Components.Get<MySectorWeatherComponent>();
        }

        private SiNpc ResolveObserver(long observerId)
        {
            if (_session?.Npcs == null)
                return null;

            _session.Npcs.Npcs.TryGetValue(observerId, out var npc);
            return npc;
        }

        private static MyEntity ResolveEntity(long entityId)
        {
            return MyAPIGateway.Entities?.GetEntityById(entityId) as MyEntity;
        }

        private void DecayAwareness(SpottingState state, long now)
        {
            if (state == null || Definition == null)
                return;

            if (state.LastAwarenessUpdateTime <= 0)
            {
                state.LastAwarenessUpdateTime = now;
                return;
            }

            var elapsedSeconds = (now - state.LastAwarenessUpdateTime) / 1000f;
            if (elapsedSeconds <= 0)
                return;

            state.ShotAwareness = Math.Max(
                0,
                state.ShotAwareness - Definition.ShotAwarenessDecayPerSecond * elapsedSeconds);
            state.LastAwarenessUpdateTime = now;
        }

        private static int EvaluationInterval(SpottingState state)
        {
            return Math.Max(50, state?.System?.Definition?.SpottingReevaluationIntervalMilliseconds ?? 250);
        }

        private float GetSharedSpottingSum(SiNpc observer, MyEntity target, float localSpottingSum)
        {
            if (observer == null || target == null || _session?.Squads == null)
                return MathHelper.Clamp(localSpottingSum, 0, 1);

            SiAssignedNpc assignment;
            if (!_session.Squads.TryGetAssignment(observer.EntityId, out assignment))
                return MathHelper.Clamp(localSpottingSum, 0, 1);

            var bestSpottingSum = localSpottingSum;
            foreach (var pair in _observations)
            {
                var state = pair.Value;
                if (state == null
                    || state.TargetId != target.EntityId
                    || state.ObserverId == observer.EntityId)
                    continue;

                SiAssignedNpc otherAssignment;
                if (!_session.Squads.TryGetAssignment(state.ObserverId, out otherAssignment)
                    || !otherAssignment.Leader.Equals(assignment.Leader))
                    continue;

                bestSpottingSum = Math.Max(bestSpottingSum, state.SpottingSum);
            }

            return MathHelper.Clamp(bestSpottingSum, 0, 1);
        }

        private int TrackingTimeoutMilliseconds()
        {
            return Math.Max(50, Definition?.SpottingTrackingTimeoutMilliseconds ?? 2000);
        }

        private static long CurrentTimeMilliseconds()
        {
            var session = MyAPIGateway.Session;
            return session != null
                ? (long)session.ElapsedPlayTime.TotalMilliseconds
                : 0;
        }

        private static SiSpottingSystemDefinition LoadDefinition()
        {
            SiSpottingSystemDefinition definition;
            if (MyDefinitionManager.TryGet(DefaultDefinitionId, out definition))
                return definition;

            foreach (var candidate in MyDefinitionManager.GetOfType<SiSpottingSystemDefinition>())
                return candidate;
            return null;
        }

        private struct SpottingKey : IEquatable<SpottingKey>
        {
            public SpottingKey(long observerId, long targetId)
            {
                ObserverId = observerId;
                TargetId = targetId;
            }

            public long ObserverId { get; }
            public long TargetId { get; }

            public bool Equals(SpottingKey other) =>
                ObserverId == other.ObserverId && TargetId == other.TargetId;

            public override bool Equals(object obj) =>
                obj is SpottingKey other && Equals(other);

            public override int GetHashCode() =>
                unchecked(((int)ObserverId * 397) ^ (int)TargetId);
        }

        private sealed class SpottingState
        {
            public SiSpottingSystem System;
            public long ObserverId;
            public long TargetId;
            public SiShootOpposingNpcBehaviorDefinition Definition;
            public float AimHeight;
            public bool IsSpotted;
            public float SpottingSum;
            public float SpottingThreshold;
            public float ShotAwareness;
            public long LastRequestedTime;
            public long LastAwarenessUpdateTime;
            public long NextEvaluationTime;
        }
    }
}
