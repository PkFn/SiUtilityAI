using System;
using System.Collections.Generic;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.Players;
using Sandbox.Game.SessionComponents;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRageMath;
using VRage.Session;

namespace Si.UtilityAI
{
    internal struct SiSpottingObservation
    {
        public static readonly SiSpottingObservation None = new SiSpottingObservation(false, 0);

        public SiSpottingObservation(bool isSpotted, float chance)
        {
            IsSpotted = isSpotted;
            Chance = chance;
        }

        public bool IsSpotted { get; }
        public float Chance { get; }
    }

    internal sealed class SiSpottingSystem
    {
        private static readonly Random SpottingRandom = new Random();
        private static readonly object SpottingRandomLock = new object();

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
        }

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

                if (now - state.LastRequestedTime > state.Definition.SpottingTrackingTimeoutMilliseconds)
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

            return new SiSpottingObservation(state.IsSpotted, state.Chance);
        }

        public void ReportShot(long shooterEntityId, MyEntity shooter)
        {
            if (shooterEntityId == 0 || shooter == null)
                return;

            var now = CurrentTimeMilliseconds();
            _recentShotTimes[shooterEntityId] = now;
            ApplyShotEvidence(shooterEntityId, shooter.WorldMatrix.Translation, now);
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
                if (state.Definition.ShotAwarenessMaxDistance <= 0
                    || distance > state.Definition.ShotAwarenessMaxDistance)
                    continue;

                var normalized = 1f - (float)(distance / state.Definition.ShotAwarenessMaxDistance);
                normalized = MathHelper.Clamp(normalized, 0, 1);
                var gain = state.Definition.ShotAwarenessPerShot
                           * (float)Math.Pow(normalized, state.Definition.ShotAwarenessDistanceExponent);
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
                state.Chance = 0;
                state.NextEvaluationTime = now + EvaluationInterval(state);
                return;
            }

            var observerPosition = observer.Entity.WorldMatrix.Translation;
            var targetPosition = target.WorldMatrix.Translation;
            var distance = knownDistance ?? Vector3D.Distance(observerPosition, targetPosition);
            if (state.Definition.HearingGuaranteedRadius > 0
                && distance <= state.Definition.HearingGuaranteedRadius)
            {
                state.Chance = 1;
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
                state.Chance = 0;
                state.IsSpotted = false;
                state.NextEvaluationTime = now + EvaluationInterval(state);
                return;
            }

            var chance = ComputeVisualChance(target, state.Definition, now);
            chance = 1f - (1f - chance) * (1f - state.ShotAwareness);
            chance = MathHelper.Clamp(chance, 0, 1);

            state.Chance = chance;
            state.IsSpotted = chance >= 1f || Roll(chance);
            state.NextEvaluationTime = now + EvaluationInterval(state);
        }

        private float ComputeVisualChance(
            MyEntity target,
            SiShootOpposingNpcBehaviorDefinition definition,
            long now)
        {
            var chance = 1f;

            if (TargetSpeed(target) <= definition.StillnessVelocityThreshold)
                chance *= definition.StillnessChanceMultiplier;

            if (!HasRecentShot(target.EntityId, definition.RecentShotMilliseconds, now))
                chance *= definition.NotFiringChanceMultiplier;

            chance *= BushMultiplier(target.WorldMatrix.Translation, definition);
            chance *= DarknessMultiplier(target.WorldMatrix.Translation, definition);
            return MathHelper.Clamp(chance, 0, 1);
        }

        private float BushMultiplier(in Vector3D position, SiShootOpposingNpcBehaviorDefinition definition)
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

        private float DarknessMultiplier(in Vector3D position, SiShootOpposingNpcBehaviorDefinition definition)
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
                return Math.Max(50, state.Definition.SpottingReevaluationIntervalMilliseconds);
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
            if (state == null || state.Definition == null)
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
                state.ShotAwareness - state.Definition.ShotAwarenessDecayPerSecond * elapsedSeconds);
            state.LastAwarenessUpdateTime = now;
        }

        private static int EvaluationInterval(SpottingState state)
        {
            return Math.Max(50, state?.Definition?.SpottingReevaluationIntervalMilliseconds ?? 250);
        }

        private static bool Roll(float chance)
        {
            if (chance <= 0)
                return false;
            if (chance >= 1)
                return true;

            lock (SpottingRandomLock)
                return SpottingRandom.NextDouble() <= chance;
        }

        private static long CurrentTimeMilliseconds()
        {
            var session = MyAPIGateway.Session;
            return session != null
                ? (long)session.ElapsedPlayTime.TotalMilliseconds
                : 0;
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
            public long ObserverId;
            public long TargetId;
            public SiShootOpposingNpcBehaviorDefinition Definition;
            public float AimHeight;
            public bool IsSpotted;
            public float Chance;
            public float ShotAwareness;
            public long LastRequestedTime;
            public long LastAwarenessUpdateTime;
            public long NextEvaluationTime;
        }
    }
}
