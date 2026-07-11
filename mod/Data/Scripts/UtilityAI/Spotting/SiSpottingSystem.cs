using System;
using System.Collections.Generic;
using Equinox76561198048419394.Core.Controller;
using Pax.Cannons;
using Pax.RemoteRope;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.Entities;
using Sandbox.Game.Players;
using Sandbox.Game.SessionComponents;
using Sandbox.ModAPI;
using SiCore.Core.Grid;
using VRage.Components.Block;
using VRage.Components;
using VRage.Components.Entity.CubeGrid;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.ModAPI;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Session;
using VRageMath;

namespace Si.UtilityAI
{
    internal enum SiSpottedTargetKind
    {
        Infantry,
        Passenger,
        Vehicle,
        StaticDefender,
    }

    internal struct SiSpottingObservation
    {
        public static readonly SiSpottingObservation None = new SiSpottingObservation(
            false,
            0,
            1,
            SiSpottedTargetKind.Infantry,
            false,
            false,
            0,
            Vector3D.Zero,
            0,
            1);

        public SiSpottingObservation(bool isSpotted, float spottingSum, float spottingThreshold)
            : this(
                isSpotted,
                spottingSum,
                spottingThreshold,
                SiSpottedTargetKind.Infantry,
                isSpotted,
                false,
                0,
                Vector3D.Zero,
                0,
                1)
        {
        }

        public SiSpottingObservation(
            bool isSpotted,
            float spottingSum,
            float spottingThreshold,
            SiSpottedTargetKind targetKind,
            bool canShootTarget,
            bool vehicleSpotted,
            long vehicleEntityId,
            Vector3D vehicleTargetPosition,
            float vehicleSpottingSum,
            float vehicleSpottingThreshold)
        {
            IsSpotted = isSpotted;
            SpottingSum = spottingSum;
            SpottingThreshold = spottingThreshold;
            TargetKind = targetKind;
            CanShootTarget = canShootTarget;
            VehicleSpotted = vehicleSpotted;
            VehicleEntityId = vehicleEntityId;
            VehicleTargetPosition = vehicleTargetPosition;
            VehicleSpottingSum = vehicleSpottingSum;
            VehicleSpottingThreshold = vehicleSpottingThreshold;
        }

        public bool IsSpotted { get; }
        public float SpottingSum { get; }
        public float SpottingThreshold { get; }
        public SiSpottedTargetKind TargetKind { get; }
        public bool CanShootTarget { get; }
        public bool VehicleSpotted { get; }
        public long VehicleEntityId { get; }
        public Vector3D VehicleTargetPosition { get; }
        public float VehicleSpottingSum { get; }
        public float VehicleSpottingThreshold { get; }
    }

    internal sealed class SiSpottingSystem
    {
        private static readonly MyDefinitionId DefaultDefinitionId =
            new MyDefinitionId(typeof(MyObjectBuilder_SiSpottingSystemDefinition), "SiDefaultSpottingSystem");

        private readonly Dictionary<SpottingKey, SpottingState> _observations =
            new Dictionary<SpottingKey, SpottingState>();
        private readonly Dictionary<TargetBankKey, TargetBankEntry> _targetBank =
            new Dictionary<TargetBankKey, TargetBankEntry>();
        private readonly Dictionary<long, long> _recentShotTimes =
            new Dictionary<long, long>();
        private readonly Dictionary<long, long> _recentPlayerEvidenceTimes =
            new Dictionary<long, long>();
        private readonly Dictionary<long, PaxVehicleWeaponSubscription> _paxVehicleWeapons =
            new Dictionary<long, PaxVehicleWeaponSubscription>();
        private readonly Queue<long> _pendingObservers = new Queue<long>();
        private readonly HashSet<long> _queuedObservers = new HashSet<long>();
        private readonly List<SpottingKey> _removals = new List<SpottingKey>();
        private readonly List<TargetBankKey> _targetBankRemovals = new List<TargetBankKey>();
        private readonly SiNpcSessionComponent _session;
        private readonly SiNearbyEnvironmentScanner _environmentScanner = new SiNearbyEnvironmentScanner();

        private MySectorWeatherComponent _weather;
        private long _nextPaxVehicleWeaponScanTime;

        public SiSpottingSystem(SiNpcSessionComponent session)
        {
            _session = session;
            MyEntities.OnEntityAdd += OnEntityAdded;
            MyEntities.OnEntityRemove += OnEntityRemoved;
            Definition = LoadDefinition();
        }

        public SiSpottingSystemDefinition Definition { get; }

        public void Clear()
        {
            _observations.Clear();
            _targetBank.Clear();
            _recentShotTimes.Clear();
            _recentPlayerEvidenceTimes.Clear();
            foreach (var subscription in _paxVehicleWeapons.Values)
                subscription.Dispose();
            _paxVehicleWeapons.Clear();
            _pendingObservers.Clear();
            _queuedObservers.Clear();
            _removals.Clear();
            _targetBankRemovals.Clear();
            _weather = null;
            MyEntities.OnEntityAdd -= OnEntityAdded;
            MyEntities.OnEntityRemove -= OnEntityRemoved;
        }

        public void Update(long elapsedMilliseconds)
        {
            TryResolveWeather();
            UpdatePaxVehicleWeaponSubscriptions();
            UpdatePlayerFiringEvidence();

            if (_observations.Count == 0 && _targetBank.Count == 0)
                return;

            var now = CurrentTimeMilliseconds();
            CleanupExpiredObservations(now);
            CleanupTargetBank(now);
            ProcessQueuedObserver(now);
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

            var now = CurrentTimeMilliseconds();
            var resolved = ResolveTargetBank(target, now);
            var primary = resolved.Primary;
            if (primary == null)
                return SiSpottingObservation.None;

            var primaryState = GetOrCreateState(observer.EntityId, primary, now);
            primaryState.Definition = definition;
            primaryState.AimHeight = aimHeight;
            primaryState.LastRequestedTime = now;
            primaryState.LastKnownDistance = distance;
            primaryState.LastKnownTargetPosition = primary.Position;

            SpottingState vehicleState = null;
            if (resolved.Vehicle != null && !resolved.Vehicle.Key.Equals(primary.Key))
            {
                var vehicleDistance = Vector3D.Distance(
                    observer.Entity?.WorldMatrix.Translation ?? Vector3D.Zero,
                    resolved.Vehicle.Position);
                vehicleState = GetOrCreateState(observer.EntityId, resolved.Vehicle, now);
                vehicleState.Definition = definition;
                vehicleState.AimHeight = aimHeight;
                vehicleState.LastRequestedTime = now;
                vehicleState.LastKnownDistance = vehicleDistance;
                vehicleState.LastKnownTargetPosition = resolved.Vehicle.Position;
            }

            EnqueueObserver(observer.EntityId);

            var sharedSpottingSum = GetSharedSpottingSum(observer, primary.Key, primaryState.SpottingSum);
            var isSpotted = sharedSpottingSum >= primaryState.SpottingThreshold;
            var canShootTarget = observer.Entity != null
                                 && HasLineOfSightToTarget(observer.Entity, primary, aimHeight);

            var vehicleSpotted = false;
            var vehicleSpottingSum = 0f;
            var vehicleSpottingThreshold = 1f;
            var vehicleEntityId = 0L;
            var vehicleTargetPosition = Vector3D.Zero;
            if (resolved.Vehicle != null)
            {
                vehicleEntityId = resolved.Vehicle.EntityId;
                vehicleTargetPosition = resolved.Vehicle.Position;
                if (vehicleState != null)
                {
                    vehicleSpottingSum = GetSharedSpottingSum(observer, resolved.Vehicle.Key, vehicleState.SpottingSum);
                    vehicleSpottingThreshold = vehicleState.SpottingThreshold;
                    vehicleSpotted = vehicleSpottingSum >= vehicleSpottingThreshold;
                }
                else if (primary.Kind == SiSpottedTargetKind.Vehicle)
                {
                    vehicleSpottingSum = sharedSpottingSum;
                    vehicleSpottingThreshold = primaryState.SpottingThreshold;
                    vehicleSpotted = isSpotted;
                }
            }

            return new SiSpottingObservation(
                isSpotted,
                sharedSpottingSum,
                primaryState.SpottingThreshold,
                primary.Kind,
                canShootTarget,
                vehicleSpotted,
                vehicleEntityId,
                vehicleTargetPosition,
                vehicleSpottingSum,
                vehicleSpottingThreshold);
        }

        public void ReportShot(long shooterEntityId, MyEntity shooter)
        {
            if (shooterEntityId == 0 || shooter == null)
                return;

            var now = CurrentTimeMilliseconds();
            RecordShotEvidence(shooterEntityId, shooter.WorldMatrix.Translation, now);

            var resolved = ResolveTargetBank(shooter, now);
            var vehicle = resolved.Vehicle;
            if (vehicle == null)
                TryResolveVehicleForWeapon(shooter, now, out vehicle);
            if (vehicle != null && vehicle.EntityId != shooterEntityId)
                RecordShotEvidence(vehicle.EntityId, vehicle.Position, now);
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
                if (state == null
                    || !state.IsSpotted
                    || state.ObserverId != observerEntityId
                    || state.TargetKind == SiSpottedTargetKind.Vehicle)
                    continue;

                if (Vector3D.DistanceSquared(observerEntity.WorldMatrix.Translation, state.LastKnownTargetPosition) <= distanceSquared)
                    return true;
            }

            return false;
        }

        private void RecordShotEvidence(long targetEntityId, Vector3D targetPosition, long now)
        {
            _recentShotTimes[targetEntityId] = now;
            ApplyShotEvidence(targetEntityId, targetPosition, now);
        }

        private void ApplyShotEvidence(long targetEntityId, Vector3D targetPosition, long now)
        {
            foreach (var pair in _observations)
            {
                var state = pair.Value;
                if (state == null
                    || state.TargetEntityId != targetEntityId
                    || state.Definition == null)
                    continue;

                var observer = ResolveObserver(state.ObserverId);
                if (observer?.Entity == null)
                    continue;

                DecayAwareness(state, now);

                var distance = Vector3D.Distance(
                    observer.Entity.WorldMatrix.Translation,
                    targetPosition);
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

        private void CleanupExpiredObservations(long now)
        {
            _removals.Clear();
            foreach (var pair in _observations)
            {
                var state = pair.Value;
                if (state == null
                    || state.Definition == null
                    || now - state.LastRequestedTime > TrackingTimeoutMilliseconds())
                    _removals.Add(pair.Key);
            }

            for (var i = 0; i < _removals.Count; i++)
                _observations.Remove(_removals[i]);
        }

        private void CleanupTargetBank(long now)
        {
            _targetBankRemovals.Clear();
            foreach (var pair in _targetBank)
            {
                var entry = pair.Value;
                var entity = entry?.Entity;
                if (entry == null
                    || now - entry.LastReferencedTime > TrackingTimeoutMilliseconds()
                    || entity == null
                    || entity.Closed
                    || entity.MarkedForClose
                    || !entity.InScene)
                    _targetBankRemovals.Add(pair.Key);
            }

            for (var i = 0; i < _targetBankRemovals.Count; i++)
                _targetBank.Remove(_targetBankRemovals[i]);
        }

        private void ProcessQueuedObserver(long now)
        {
            var attempts = _pendingObservers.Count;
            for (var i = 0; i < attempts; i++)
            {
                if (_pendingObservers.Count == 0)
                    return;

                var observerId = _pendingObservers.Dequeue();
                _queuedObservers.Remove(observerId);
                if (!TryProcessObserver(observerId, now))
                {
                    RequeueObserverIfActive(observerId, now);
                    continue;
                }

                RequeueObserverIfActive(observerId, now);
                return;
            }
        }

        private bool TryProcessObserver(long observerId, long now)
        {
            var processed = false;
            foreach (var pair in _observations)
            {
                var state = pair.Value;
                if (state == null
                    || state.ObserverId != observerId
                    || now < state.NextEvaluationTime)
                    continue;

                var observer = ResolveObserver(state.ObserverId);
                var target = ResolveTargetEntry(state.TargetKey, now);
                Evaluate(state, observer, target, now, state.LastKnownDistance);
                processed = true;
            }

            return processed;
        }

        private void RequeueObserverIfActive(long observerId, long now)
        {
            foreach (var pair in _observations)
            {
                var state = pair.Value;
                if (state == null || state.ObserverId != observerId)
                    continue;

                if (now >= state.NextEvaluationTime)
                {
                    EnqueueObserver(observerId);
                    return;
                }

                if (now - state.LastRequestedTime <= TrackingTimeoutMilliseconds())
                {
                    EnqueueObserver(observerId);
                    return;
                }
            }
        }

        private void EnqueueObserver(long observerId)
        {
            if (observerId == 0 || !_queuedObservers.Add(observerId))
                return;

            _pendingObservers.Enqueue(observerId);
        }

        private void Evaluate(
            SpottingState state,
            SiNpc observer,
            TargetBankEntry target,
            long now,
            double? knownDistance = null)
        {
            DecayAwareness(state, now);

            if (observer?.Entity == null || target == null || !IsValidObservedEntity(target.Entity))
            {
                state.IsSpotted = false;
                state.SpottingSum = 0;
                state.SpottingThreshold = 1;
                state.LastCanShootTarget = false;
                state.NextEvaluationTime = now + EvaluationInterval(state);
                return;
            }

            var observerPosition = observer.Entity.WorldMatrix.Translation;
            var targetPosition = target.Position;
            var distance = knownDistance ?? Vector3D.Distance(observerPosition, targetPosition);
            var canShootTarget = HasLineOfSightToTarget(observer.Entity, target, state.AimHeight);

            state.LastKnownTargetPosition = targetPosition;
            state.LastCanShootTarget = canShootTarget;

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

            // A mounted character is expected to be occluded by the vehicle that
            // contains it. Keep the passenger awareness state alive in that case;
            // canShootTarget remains false so callers can select the vehicle as
            // the actual fire target.
            if (state.Definition.RequireLineOfSight
                && !canShootTarget
                && state.TargetKind != SiSpottedTargetKind.Passenger)
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
                _session?.ReportNpcSpottedTarget(state.ObserverId, state.TargetEntityId);
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

        private float ComputeVisualChance(TargetBankEntry target, long now)
        {
            var chance = 1f;
            var definition = Definition;
            if (definition == null || target == null)
                return chance;

            if (TargetSpeed(target) <= definition.StillnessVelocityThreshold)
                chance *= definition.StillnessChanceMultiplier;

            if (!HasRecentShot(target.EntityId, definition.RecentShotMilliseconds, now))
                chance *= definition.NotFiringChanceMultiplier;

            chance *= BushMultiplier(target.Position, definition);
            chance *= DarknessMultiplier(target.Position, definition);
            if (target.Kind == SiSpottedTargetKind.Vehicle)
                chance *= VehicleSpottingMultiplier(target, definition);
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

        private static float VehicleSpottingMultiplier(TargetBankEntry target, SiSpottingSystemDefinition definition)
        {
            if (target == null || definition == null)
                return 1f;

            var gain = definition.VehicleSpottingBaseGain;
            var speed = Math.Max(0, target.Velocity.Length());
            if (definition.VehicleSpottingMaxSpeed > definition.VehicleSpottingMovingSpeedThreshold
                && speed > definition.VehicleSpottingMovingSpeedThreshold)
            {
                var normalized = (float)((speed - definition.VehicleSpottingMovingSpeedThreshold)
                                         / (definition.VehicleSpottingMaxSpeed - definition.VehicleSpottingMovingSpeedThreshold));
                normalized = MathHelper.Clamp(normalized, 0, 1);
                gain += normalized * definition.VehicleSpottingMovingGain;
            }

            return Math.Max(1f, 1f + gain);
        }

        private bool HasRecentShot(long entityId, int windowMilliseconds, long now)
        {
            if (windowMilliseconds <= 0)
                return false;
            return _recentShotTimes.TryGetValue(entityId, out var lastShot)
                   && now - lastShot <= windowMilliseconds;
        }

        private static double TargetSpeed(TargetBankEntry target)
        {
            return target?.Velocity.Length() ?? 0;
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
                RecordShotEvidence(entity.EntityId, entity.WorldMatrix.Translation, now);

                var resolved = ResolveTargetBank(entity as MyEntity, now);
                var vehicle = resolved.Vehicle;
                if (vehicle != null && vehicle.EntityId != entity.EntityId)
                    RecordShotEvidence(vehicle.EntityId, vehicle.Position, now);
            }
        }

        private void UpdatePaxVehicleWeaponSubscriptions()
        {
            var now = CurrentTimeMilliseconds();
            if (now < _nextPaxVehicleWeaponScanTime)
                return;

            _nextPaxVehicleWeaponScanTime = now + 1000;
            foreach (var entity in MyEntities.GetEntities())
                TrySubscribePaxVehicleWeapon(entity);
        }

        private void OnEntityAdded(MyEntity entity)
        {
            TrySubscribePaxVehicleWeapon(entity);
        }

        private void OnEntityRemoved(MyEntity entity)
        {
            if (entity == null)
                return;

            if (_paxVehicleWeapons.TryGetValue(entity.EntityId, out var subscription))
            {
                subscription.Dispose();
                _paxVehicleWeapons.Remove(entity.EntityId);
            }
        }

        private void TrySubscribePaxVehicleWeapon(MyEntity entity)
        {
            if (_session == null
                || (MyMultiplayerModApi.Static != null && !MyMultiplayerModApi.Static.IsServer))
                return;
            if (entity == null || !entity.InScene || entity.Closed || entity.MarkedForClose)
                return;
            if (_paxVehicleWeapons.ContainsKey(entity.EntityId))
                return;

            // PAX vehicle weapons expose their control through RemoteRope. Use that
            // component as the compatibility boundary so unrelated grid weapons are
            // not interpreted as PAX vehicle weapons.
            if (!entity.Components.Contains<MyRemoteRopeControlComponent>())
                return;

            var remoteRope = entity.Components.Get<MyRemoteRopeControlComponent>();
            var machineGun = entity.Components.Get<MyPAX_MachineGun>();
            var cannon = entity.Components.Get<MyPAX_Cannon>();
            if (machineGun == null && cannon == null)
                return;

            var subscription = new PaxVehicleWeaponSubscription(this, entity, remoteRope, machineGun, cannon);
            if (!subscription.TrySubscribe())
            {
                subscription.Dispose();
                return;
            }

            _paxVehicleWeapons.Add(entity.EntityId, subscription);
        }

        private void OnPaxVehicleWeaponShot(
            MyEntity weapon,
            MyRemoteRopeControlComponent remoteRope)
        {
            if (weapon == null || weapon.Closed || weapon.MarkedForClose)
                return;

            var now = CurrentTimeMilliseconds();
            RecordShotEvidence(weapon.EntityId, weapon.WorldMatrix.Translation, now);

            var weaponVehicle = ResolveVehicleForShot(weapon, now);
            if (weaponVehicle != null)
                RecordShotEvidence(weaponVehicle.EntityId, weaponVehicle.Position, now);

            // RemoteRope identifies the player operating camera-guided weapons.
            // Keep the player evidence separate so an unseated operator is also
            // revealed, while avoiding a second gain on the same vehicle state.
            var attachedPlayer = ResolveEntity(remoteRope?.AttachedPlayerId ?? 0);
            if (attachedPlayer == null || attachedPlayer.EntityId == weapon.EntityId)
                return;

            RecordShotEvidence(attachedPlayer.EntityId, attachedPlayer.WorldMatrix.Translation, now);
            var playerVehicle = ResolveVehicleForShot(attachedPlayer, now);
            if (playerVehicle != null
                && (weaponVehicle == null || playerVehicle.EntityId != weaponVehicle.EntityId))
                RecordShotEvidence(playerVehicle.EntityId, playerVehicle.Position, now);
        }

        private TargetBankEntry ResolveVehicleForShot(MyEntity shooter, long now)
        {
            var resolved = ResolveTargetBank(shooter, now);
            var vehicle = resolved.Vehicle;
            if (vehicle == null)
                TryResolveVehicleForWeapon(shooter, now, out vehicle);
            return vehicle;
        }

        private int PlayerEvidenceIntervalMilliseconds(long entityId)
        {
            foreach (var pair in _observations)
            {
                var state = pair.Value;
                if (state == null || state.TargetEntityId != entityId || state.Definition == null)
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

        private float GetSharedSpottingSum(SiNpc observer, TargetBankKey targetKey, float localSpottingSum)
        {
            if (observer == null || _session?.Squads == null)
                return MathHelper.Clamp(localSpottingSum, 0, 1);

            SiAssignedNpc assignment;
            if (!_session.Squads.TryGetAssignment(observer.EntityId, out assignment))
                return MathHelper.Clamp(localSpottingSum, 0, 1);

            var bestSpottingSum = localSpottingSum;
            foreach (var pair in _observations)
            {
                var state = pair.Value;
                if (state == null
                    || !state.TargetKey.Equals(targetKey)
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

        private SpottingState GetOrCreateState(long observerId, TargetBankEntry target, long now)
        {
            var key = new SpottingKey(observerId, target.Key);
            if (_observations.TryGetValue(key, out var state))
                return state;

            state = new SpottingState
            {
                System = this,
                ObserverId = observerId,
                TargetKey = target.Key,
                TargetEntityId = target.EntityId,
                TargetKind = target.Kind,
                LastKnownTargetPosition = target.Position,
                LastAwarenessUpdateTime = now,
            };
            _observations.Add(key, state);
            return state;
        }

        private TargetBankResolution ResolveTargetBank(MyEntity target, long now)
        {
            if (target == null)
                return TargetBankResolution.None;

            if (_session?.StaticDefenders != null
                && _session.StaticDefenders.TryGetTarget(target.EntityId, out var staticDefender)
                && staticDefender != null
                && !staticDefender.IsKnockedOut)
            {
                return new TargetBankResolution(UpsertTargetEntry(
                    new TargetBankKey(target.EntityId, SiSpottedTargetKind.StaticDefender),
                    target,
                    SiSpottedTargetKind.StaticDefender,
                    target.WorldMatrix.Translation,
                    Vector3.Zero,
                    now));
            }

            if (TryResolveVehicleForMountedCharacter(target, now, out var passenger, out var vehicle))
                return new TargetBankResolution(passenger, vehicle);

            if (TryResolveVehicleFromGrid(target, now, out vehicle))
                return new TargetBankResolution(vehicle, vehicle);

            return new TargetBankResolution(UpsertTargetEntry(
                new TargetBankKey(target.EntityId, SiSpottedTargetKind.Infantry),
                target,
                SiSpottedTargetKind.Infantry,
                target.WorldMatrix.Translation,
                target.Physics?.LinearVelocity ?? Vector3.Zero,
                now));
        }

        private TargetBankEntry ResolveTargetEntry(TargetBankKey key, long now)
        {
            var entity = ResolveEntity(key.EntityId);
            if (entity == null)
                return null;

            var resolved = ResolveTargetBank(entity, now);
            if (resolved.Primary != null && resolved.Primary.Key.Equals(key))
                return resolved.Primary;
            if (resolved.Vehicle != null && resolved.Vehicle.Key.Equals(key))
                return resolved.Vehicle;
            return null;
        }

        private bool TryResolveVehicleForMountedCharacter(
            MyEntity target,
            long now,
            out TargetBankEntry passenger,
            out TargetBankEntry vehicle)
        {
            passenger = null;
            vehicle = null;

            var controller = target.Components.Get<EquiEntityControllerComponent>();
            var slot = controller?.Controlled;
            if (slot == null)
                return false;

            MyEntity seatBlockEntity;
            MyGridDataComponent gridData;
            if (!SiTransportSeatHelpers.TryGetSeatBlockGrid(slot, out seatBlockEntity, out gridData))
                return false;

            if (!TryBuildVehicleTarget(gridData, now, out vehicle))
                return false;

            passenger = UpsertTargetEntry(
                new TargetBankKey(target.EntityId, SiSpottedTargetKind.Passenger),
                target,
                SiSpottedTargetKind.Passenger,
                target.WorldMatrix.Translation,
                Vector3.Zero,
                now);
            return true;
        }

        private bool TryResolveVehicleFromGrid(MyEntity target, long now, out TargetBankEntry vehicle)
        {
            vehicle = null;
            var gridData = FindGridData(target);
            if (gridData == null)
                return false;

            return TryBuildVehicleTarget(gridData, now, out vehicle);
        }

        private bool TryResolveVehicleForWeapon(MyEntity weapon, long now, out TargetBankEntry vehicle)
        {
            vehicle = null;
            var entity = weapon;
            while (entity != null)
            {
                var gridData = FindGridData(entity);
                if (gridData != null)
                    return TryBuildVehicleTarget(gridData, now, false, out vehicle);

                entity = entity.Parent;
            }

            return false;
        }

        private bool TryBuildVehicleTarget(MyGridDataComponent gridData, long now, out TargetBankEntry vehicle)
        {
            return TryBuildVehicleTarget(gridData, now, true, out vehicle);
        }

        private bool TryBuildVehicleTarget(
            MyGridDataComponent gridData,
            long now,
            bool requireOccupied,
            out TargetBankEntry vehicle)
        {
            vehicle = null;
            if (gridData == null)
                return false;

            var occupied = false;
            var heaviestGrid = (MyEntity)null;
            var heaviestMass = 0f;
            var heaviestVelocity = Vector3.Zero;
            var heaviestPosition = Vector3D.Zero;

            EnumerateVehicleGrids(gridData, ref occupied, ref heaviestGrid, ref heaviestMass, ref heaviestVelocity, ref heaviestPosition);
            if ((requireOccupied && !occupied) || heaviestGrid == null)
                return false;

            vehicle = UpsertTargetEntry(
                new TargetBankKey(heaviestGrid.EntityId, SiSpottedTargetKind.Vehicle),
                heaviestGrid,
                SiSpottedTargetKind.Vehicle,
                heaviestPosition,
                heaviestVelocity,
                now);
            return true;
        }

        private static void EnumerateVehicleGrids(
            MyGridDataComponent originGrid,
            ref bool occupied,
            ref MyEntity heaviestGrid,
            ref float heaviestMass,
            ref Vector3 heaviestVelocity,
            ref Vector3D heaviestPosition)
        {
            var hierarchy = originGrid.Container?.Get<MyGridHierarchyComponent>();
            if (hierarchy == null)
            {
                ProcessVehicleGrid(originGrid, ref occupied, ref heaviestGrid, ref heaviestMass, ref heaviestVelocity, ref heaviestPosition);
                return;
            }

            var top = hierarchy.GetTopMostParent() ?? hierarchy;
            ProcessHierarchyGrid(top.Entity, ref occupied, ref heaviestGrid, ref heaviestMass, ref heaviestVelocity, ref heaviestPosition);
            foreach (var child in top.GetAllChildren())
                ProcessHierarchyGrid(child?.Entity, ref occupied, ref heaviestGrid, ref heaviestMass, ref heaviestVelocity, ref heaviestPosition);
        }

        private static void ProcessHierarchyGrid(
            MyEntity entity,
            ref bool occupied,
            ref MyEntity heaviestGrid,
            ref float heaviestMass,
            ref Vector3 heaviestVelocity,
            ref Vector3D heaviestPosition)
        {
            if (entity == null || !entity.Components.TryGet(out MyGridDataComponent gridData))
                return;

            ProcessVehicleGrid(gridData, ref occupied, ref heaviestGrid, ref heaviestMass, ref heaviestVelocity, ref heaviestPosition);
        }

        private static void ProcessVehicleGrid(
            MyGridDataComponent gridData,
            ref bool occupied,
            ref MyEntity heaviestGrid,
            ref float heaviestMass,
            ref Vector3 heaviestVelocity,
            ref Vector3D heaviestPosition)
        {
            var entity = gridData?.Entity;
            var physics = entity?.Physics;
            if (entity == null || physics == null || physics.IsStatic)
                return;

            if (!occupied)
            {
                foreach (var seat in SiTransportSeatHelpers.EnumerateSeatSlotsOnGrid(gridData))
                    if (seat?.AttachedCharacter != null)
                    {
                        occupied = true;
                        break;
                    }
            }

            if (physics.Mass < heaviestMass)
                return;

            heaviestGrid = entity;
            heaviestMass = physics.Mass;
            heaviestVelocity = physics.LinearVelocity;
            heaviestPosition = ComputePhysicsCenterWorld(entity, physics);
        }

        private TargetBankEntry UpsertTargetEntry(
            TargetBankKey key,
            MyEntity entity,
            SiSpottedTargetKind kind,
            Vector3D position,
            Vector3 velocity,
            long now)
        {
            if (!_targetBank.TryGetValue(key, out var entry))
            {
                entry = new TargetBankEntry
                {
                    Key = key,
                    EntityId = key.EntityId,
                    Kind = kind,
                };
                _targetBank.Add(key, entry);
            }

            entry.Entity = entity;
            entry.Position = position;
            entry.Velocity = velocity;
            entry.LastReferencedTime = now;
            return entry;
        }

        private static bool HasLineOfSightToTarget(MyEntity observer, TargetBankEntry target, float aimHeight)
        {
            if (observer == null || target?.Entity == null)
                return false;

            var shooterUp = NormalizedOrFallback(observer.WorldMatrix.Up, Vector3D.Up);
            var start = observer.WorldMatrix.Translation + shooterUp * aimHeight;
            var end = target.Position;
            if (target.Kind != SiSpottedTargetKind.Vehicle)
            {
                var targetUp = NormalizedOrFallback(target.Entity.WorldMatrix.Up, shooterUp);
                end = target.Entity.WorldMatrix.Translation + targetUp * aimHeight;
            }

            IHitInfo hit;
            if (!MyAPIGateway.Physics.CastRay(start, end, out hit))
                return true;

            if (target.Kind == SiSpottedTargetKind.Vehicle)
            {
                if (hit == null)
                    return true;

                return IsVehicleGridEntity(hit.HitEntity, target.Entity);
            }

            return hit == null
                   || hit.HitEntity == null
                   || hit.HitEntity == target.Entity
                   || hit.HitEntity == observer;
        }

        internal static bool IsVehicleGridEntity(MyEntity hitEntity, MyEntity vehicleEntity)
        {
            if (hitEntity == null || vehicleEntity == null)
                return false;
            if (hitEntity == vehicleEntity)
                return true;

            var vehicleGrid = FindGridData(vehicleEntity);
            var hitGrid = FindGridData(hitEntity);
            if (vehicleGrid == null || hitGrid == null)
                return false;
            if (vehicleGrid == hitGrid)
                return true;

            var hierarchy = vehicleGrid.Container?.Get<MyGridHierarchyComponent>();
            if (hierarchy == null)
                return false;

            var top = hierarchy.GetTopMostParent() ?? hierarchy;
            if (top.Entity == hitGrid.Entity)
                return true;

            foreach (var child in top.GetAllChildren())
                if (child?.Entity == hitGrid.Entity)
                    return true;

            return false;
        }

        private static MyGridDataComponent FindGridData(MyEntity entity)
        {
            var current = entity;
            while (current != null)
            {
                if (current.Components.TryGet(out MyGridDataComponent gridData))
                    return gridData;

                var block = current.Get<MyBlockComponent>();
                if (block?.GridData != null)
                    return block.GridData;

                current = current.Parent;
            }

            return null;
        }

        private static Vector3D ComputePhysicsCenterWorld(MyEntity entity, VRage.Components.Physics.MyPhysicsComponentBase physics)
        {
            if (entity == null)
                return Vector3D.Zero;
            if (physics == null)
                return entity.WorldMatrix.Translation;
            return Vector3D.Transform((Vector3D)physics.Center, entity.WorldMatrix);
        }

        private static bool IsValidObservedEntity(MyEntity entity)
        {
            return entity != null
                   && entity.InScene
                   && !entity.Closed
                   && !entity.MarkedForClose;
        }

        private static Vector3D NormalizedOrFallback(in Vector3D value, in Vector3D fallback)
        {
            var lengthSquared = value.LengthSquared();
            return lengthSquared > 0.0001
                ? value / Math.Sqrt(lengthSquared)
                : fallback;
        }

        private sealed class PaxVehicleWeaponSubscription
        {
            private const string QuarterShotEvent = "shootquarter";
            private const string HalfShotEvent = "shoothalf";
            private const string FullShotEvent = "shootfull";

            private readonly SiSpottingSystem _owner;
            private readonly MyEntity _weapon;
            private readonly MyRemoteRopeControlComponent _remoteRope;
            private readonly MyPAX_MachineGun _machineGun;
            private readonly MyPAX_Cannon _cannon;
            private readonly Action _machineGunShot;
            private readonly Action<string> _cannonShot;
            private MyComponentEventBus _eventBus;
            private bool _machineGunSubscribed;
            private bool _quarterShotSubscribed;
            private bool _halfShotSubscribed;
            private bool _fullShotSubscribed;

            public PaxVehicleWeaponSubscription(
                SiSpottingSystem owner,
                MyEntity weapon,
                MyRemoteRopeControlComponent remoteRope,
                MyPAX_MachineGun machineGun,
                MyPAX_Cannon cannon)
            {
                _owner = owner;
                _weapon = weapon;
                _remoteRope = remoteRope;
                _machineGun = machineGun;
                _cannon = cannon;
                _machineGunShot = OnMachineGunShot;
                _cannonShot = OnCannonShot;
            }

            public bool TrySubscribe()
            {
                if (_machineGun != null)
                {
                    _machineGun.FiredGun += _machineGunShot;
                    _machineGunSubscribed = true;
                }

                if (_cannon != null)
                {
                    _eventBus = _weapon.Components.Get<MyComponentEventBus>();
                    if (_eventBus != null)
                    {
                        _quarterShotSubscribed = _eventBus.TryAddListener(QuarterShotEvent, _cannonShot);
                        _halfShotSubscribed = _eventBus.TryAddListener(HalfShotEvent, _cannonShot);
                        _fullShotSubscribed = _eventBus.TryAddListener(FullShotEvent, _cannonShot);
                    }
                }

                return _machineGunSubscribed
                       || _quarterShotSubscribed
                       || _halfShotSubscribed
                       || _fullShotSubscribed;
            }

            public void Dispose()
            {
                if (_machineGunSubscribed && _machineGun != null)
                    _machineGun.FiredGun -= _machineGunShot;

                if (_eventBus != null)
                {
                    if (_quarterShotSubscribed)
                        _eventBus.RemoveListener(QuarterShotEvent, _cannonShot);
                    if (_halfShotSubscribed)
                        _eventBus.RemoveListener(HalfShotEvent, _cannonShot);
                    if (_fullShotSubscribed)
                        _eventBus.RemoveListener(FullShotEvent, _cannonShot);
                }

                _machineGunSubscribed = false;
                _quarterShotSubscribed = false;
                _halfShotSubscribed = false;
                _fullShotSubscribed = false;
                _eventBus = null;
            }

            private void OnMachineGunShot()
            {
                _owner.OnPaxVehicleWeaponShot(_weapon, _remoteRope);
            }

            private void OnCannonShot(string _)
            {
                _owner.OnPaxVehicleWeaponShot(_weapon, _remoteRope);
            }
        }

        private struct TargetBankResolution
        {
            public static readonly TargetBankResolution None = new TargetBankResolution(null, null);

            public TargetBankResolution(TargetBankEntry primary)
                : this(primary, null)
            {
            }

            public TargetBankResolution(TargetBankEntry primary, TargetBankEntry vehicle)
            {
                Primary = primary;
                Vehicle = vehicle;
            }

            public TargetBankEntry Primary { get; }
            public TargetBankEntry Vehicle { get; }
        }

        private struct TargetBankKey : IEquatable<TargetBankKey>
        {
            public TargetBankKey(long entityId, SiSpottedTargetKind kind)
            {
                EntityId = entityId;
                Kind = kind;
            }

            public long EntityId { get; }
            public SiSpottedTargetKind Kind { get; }

            public bool Equals(TargetBankKey other) =>
                EntityId == other.EntityId && Kind == other.Kind;

            public override bool Equals(object obj) =>
                obj is TargetBankKey other && Equals(other);

            public override int GetHashCode() =>
                unchecked((((int)EntityId * 397) ^ (int)(EntityId >> 32)) * 397 ^ (int)Kind);
        }

        private sealed class TargetBankEntry
        {
            public TargetBankKey Key;
            public long EntityId;
            public SiSpottedTargetKind Kind;
            public MyEntity Entity;
            public Vector3D Position;
            public Vector3 Velocity;
            public long LastReferencedTime;
        }

        private struct SpottingKey : IEquatable<SpottingKey>
        {
            public SpottingKey(long observerId, TargetBankKey targetKey)
            {
                ObserverId = observerId;
                TargetKey = targetKey;
            }

            public long ObserverId { get; }
            public TargetBankKey TargetKey { get; }

            public bool Equals(SpottingKey other) =>
                ObserverId == other.ObserverId && TargetKey.Equals(other.TargetKey);

            public override bool Equals(object obj) =>
                obj is SpottingKey other && Equals(other);

            public override int GetHashCode() =>
                unchecked((((int)ObserverId * 397) ^ (int)(ObserverId >> 32)) * 397 ^ TargetKey.GetHashCode());
        }

        private sealed class SpottingState
        {
            public SiSpottingSystem System;
            public long ObserverId;
            public TargetBankKey TargetKey;
            public long TargetEntityId;
            public SiSpottedTargetKind TargetKind;
            public SiShootOpposingNpcBehaviorDefinition Definition;
            public float AimHeight;
            public bool IsSpotted;
            public bool LastCanShootTarget;
            public float SpottingSum;
            public float SpottingThreshold;
            public float ShotAwareness;
            public long LastRequestedTime;
            public long LastAwarenessUpdateTime;
            public long NextEvaluationTime;
            public double LastKnownDistance;
            public Vector3D LastKnownTargetPosition;
        }
    }
}
