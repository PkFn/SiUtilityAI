using System;
using System.Xml.Serialization;
using Medieval.GameSystems.Factions;
using Sandbox.Game.Players;
using Sandbox.ModAPI;
using SiCore.Core.Debug;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace Si.UtilityAI
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiShootOpposingNpcBehavior : MyObjectBuilder_EntityComponent
    {
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiShootOpposingNpcBehaviorDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public SerializableDefinitionId? Balance;

        public float SearchRadius;
        public float BaseScore;
        public float MaxScore;
        public float DistanceScore;
        public float DistanceExponent;

        public bool RequireLineOfSight;
        public bool RotateToTarget;
        public string EngageSpeech;
        public int EngageSpeechCooldownMilliseconds;
        public string SpotTargetName;
        public int SpotSpeechCooldownMilliseconds;
        public float DetectionAccuracyWorseningMultiplier;
        public int TargetReevaluationIntervalMilliseconds;

        [XmlArrayItem("Archetype")]
        public string[] TargetArchetypes;
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiShootOpposingNpcBehaviorBalanceDefinition : MyObjectBuilder_DefinitionBase
    {
        public float SearchRadius;
        public float BaseScore;
        public float MaxScore;
        public float DistanceScore;
        public float DistanceExponent;

        public bool RequireLineOfSight;
        public bool RotateToTarget;
        public string EngageSpeech;
        public int EngageSpeechCooldownMilliseconds;
        public string SpotTargetName;
        public int SpotSpeechCooldownMilliseconds;
        public float DetectionAccuracyWorseningMultiplier;
        public int TargetReevaluationIntervalMilliseconds;

        [XmlArrayItem("Archetype")]
        public string[] TargetArchetypes;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiShootOpposingNpcBehaviorBalanceDefinition))]
    public class SiShootOpposingNpcBehaviorBalanceDefinition : MyDefinitionBase
    {
        private static readonly string[] EmptyArchetypes = new string[0];

        public float SearchRadius { get; private set; }
        public float BaseScore { get; private set; }
        public float MaxScore { get; private set; }
        public float DistanceScore { get; private set; }
        public float DistanceExponent { get; private set; }
        public bool RequireLineOfSight { get; private set; }
        public bool RotateToTarget { get; private set; }
        public string EngageSpeech { get; private set; }
        public int EngageSpeechCooldownMilliseconds { get; private set; }
        public string SpotTargetName { get; private set; }
        public int SpotSpeechCooldownMilliseconds { get; private set; }
        public float DetectionAccuracyWorseningMultiplier { get; private set; }
        public int TargetReevaluationIntervalMilliseconds { get; private set; }
        public string[] TargetArchetypes { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiShootOpposingNpcBehaviorBalanceDefinition)builder;

            SearchRadius = Math.Max(0, ob.SearchRadius);
            BaseScore = Math.Max(0, ob.BaseScore);
            MaxScore = Math.Max(0, ob.MaxScore);
            DistanceScore = Math.Max(0, ob.DistanceScore);
            DistanceExponent = Math.Max(0.01f, ob.DistanceExponent);
            RequireLineOfSight = ob.RequireLineOfSight;
            RotateToTarget = ob.RotateToTarget;
            EngageSpeech = ob.EngageSpeech;
            EngageSpeechCooldownMilliseconds = Math.Max(0, ob.EngageSpeechCooldownMilliseconds);
            SpotTargetName = ob.SpotTargetName;
            SpotSpeechCooldownMilliseconds = Math.Max(0, ob.SpotSpeechCooldownMilliseconds);
            DetectionAccuracyWorseningMultiplier = Math.Max(0, ob.DetectionAccuracyWorseningMultiplier);
            TargetReevaluationIntervalMilliseconds = Math.Max(1, ob.TargetReevaluationIntervalMilliseconds);
            TargetArchetypes = ob.TargetArchetypes ?? EmptyArchetypes;
        }
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiShootOpposingNpcBehaviorDefinition))]
    public class SiShootOpposingNpcBehaviorDefinition : MyEntityComponentDefinition
    {
        private static readonly string[] EmptyArchetypes = new string[0];
        private SerializableDefinitionId? _balanceId;
        private bool _balanceResolved;

        public float SearchRadius { get; private set; }
        public float BaseScore { get; private set; }
        public float MaxScore { get; private set; }
        public float DistanceScore { get; private set; }
        public float DistanceExponent { get; private set; }
        public bool RequireLineOfSight { get; private set; }
        public bool RotateToTarget { get; private set; }
        public string EngageSpeech { get; private set; }
        public int EngageSpeechCooldownMilliseconds { get; private set; }
        public string SpotTargetName { get; private set; }
        public int SpotSpeechCooldownMilliseconds { get; private set; }
        public float DetectionAccuracyWorseningMultiplier { get; private set; }
        public int TargetReevaluationIntervalMilliseconds { get; private set; }
        public string[] TargetArchetypes { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiShootOpposingNpcBehaviorDefinition)builder;

            _balanceId = ob.Balance;
            _balanceResolved = false;
            InitFromBuilder(ob);
            ResolveBalance();
        }

        internal void ResolveBalance()
        {
            if (_balanceResolved || !_balanceId.HasValue)
                return;

            var balance = LoadBalance(_balanceId);
            if (balance == null)
                return;

            InitFromBalance(balance);
            _balanceResolved = true;
        }

        private void InitFromBuilder(MyObjectBuilder_SiShootOpposingNpcBehaviorDefinition ob)
        {
            SearchRadius = Math.Max(0, ob.SearchRadius);
            BaseScore = Math.Max(0, ob.BaseScore);
            MaxScore = Math.Max(0, ob.MaxScore);
            DistanceScore = Math.Max(0, ob.DistanceScore);
            DistanceExponent = Math.Max(0.01f, ob.DistanceExponent);
            RequireLineOfSight = ob.RequireLineOfSight;
            RotateToTarget = ob.RotateToTarget;
            EngageSpeech = ob.EngageSpeech;
            EngageSpeechCooldownMilliseconds = Math.Max(0, ob.EngageSpeechCooldownMilliseconds);
            SpotTargetName = ob.SpotTargetName;
            SpotSpeechCooldownMilliseconds = Math.Max(0, ob.SpotSpeechCooldownMilliseconds);
            DetectionAccuracyWorseningMultiplier = Math.Max(0, ob.DetectionAccuracyWorseningMultiplier);
            TargetReevaluationIntervalMilliseconds = Math.Max(1, ob.TargetReevaluationIntervalMilliseconds);
            TargetArchetypes = ob.TargetArchetypes ?? EmptyArchetypes;
        }

        private void InitFromBalance(SiShootOpposingNpcBehaviorBalanceDefinition balance)
        {
            SearchRadius = balance.SearchRadius;
            BaseScore = balance.BaseScore;
            MaxScore = balance.MaxScore;
            DistanceScore = balance.DistanceScore;
            DistanceExponent = balance.DistanceExponent;
            RequireLineOfSight = balance.RequireLineOfSight;
            RotateToTarget = balance.RotateToTarget;
            EngageSpeech = balance.EngageSpeech;
            EngageSpeechCooldownMilliseconds = balance.EngageSpeechCooldownMilliseconds;
            SpotTargetName = balance.SpotTargetName;
            SpotSpeechCooldownMilliseconds = balance.SpotSpeechCooldownMilliseconds;
            DetectionAccuracyWorseningMultiplier = balance.DetectionAccuracyWorseningMultiplier;
            TargetReevaluationIntervalMilliseconds = balance.TargetReevaluationIntervalMilliseconds;
            TargetArchetypes = balance.TargetArchetypes ?? EmptyArchetypes;
        }

        private static SiShootOpposingNpcBehaviorBalanceDefinition LoadBalance(SerializableDefinitionId? balanceId)
        {
            if (!balanceId.HasValue)
                return null;

            SiShootOpposingNpcBehaviorBalanceDefinition balance;
            if (MyDefinitionManager.TryGet(balanceId.Value, out balance))
                return balance;

            var subtype = balanceId.Value.SubtypeId;
            if (string.IsNullOrWhiteSpace(subtype))
                return null;

            foreach (var candidate in MyDefinitionManager.GetOfType<SiShootOpposingNpcBehaviorBalanceDefinition>())
                if (string.Equals(candidate.Id.SubtypeName, subtype, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            return null;
        }
    }

    /// <summary>
    /// Scores opposing NPCs and players, reports spotted targets, and grants fire
    /// permission to the attached ranged-weapon component.
    /// </summary>
    [MyComponent(typeof(MyObjectBuilder_SiShootOpposingNpcBehavior))]
    [MyDefinitionRequired(typeof(SiShootOpposingNpcBehaviorDefinition))]
    public class SiShootOpposingNpcBehaviorComponent : MyEntityComponent, ISiUtilityBehavior, ISiContinuousUtilityBehavior
    {
        private static readonly MyStringHash HostileRelationship = MyStringHash.GetOrCompute("War");
        private const long TargetLogCooldownMilliseconds = 1500;
        private const long SearchLogCooldownMilliseconds = 2000;
        private const long FireBlockLogCooldownMilliseconds = 1000;

        private SiShootOpposingNpcBehaviorDefinition _definition;
        private SiTakeCoverBehaviorComponent _takeCoverBehavior;
        private ShootTarget _target;
        private long _nextTargetEvaluationTime = -1;
        private long _lastEngageSpeechTime = -1;
        private long _lastSpotSpeechTime = -1;
        private long _lastSpottedTargetId;
        private long _lastTargetLogTime = -1;
        private long _lastSearchLogTime = -1;
        private long _lastFireBlockLogTime = -1;
        private readonly SiGameLog _log = new SiGameLog(nameof(SiShootOpposingNpcBehaviorComponent), "[SiShoot]");
        private SiShootOpposingNpcBehaviorDefinition _runtimeDefinition;
        private SiNpcCombatStateComponent _combatState;

        public string BehaviorName => DefinitionId.ToString();
        private SiShootOpposingNpcBehaviorDefinition Definition => _runtimeDefinition ?? _definition;

        public override bool IsSerialized => false;

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            _definition = (SiShootOpposingNpcBehaviorDefinition)definition;
            _definition.ResolveBalance();
        }

        internal bool ApplyRuntimeDefinition(MyDefinitionId definitionId)
        {
            SiShootOpposingNpcBehaviorDefinition runtimeDefinition;
            if (!MyDefinitionManager.TryGet(definitionId, out runtimeDefinition) || runtimeDefinition == null)
                return false;

            return ApplyRuntimeDefinition(runtimeDefinition);
        }

        internal bool ApplyRuntimeDefinition(SiShootOpposingNpcBehaviorDefinition runtimeDefinition)
        {
            if (runtimeDefinition == null)
                return false;

            runtimeDefinition.ResolveBalance();
            _runtimeDefinition = runtimeDefinition;
            _target = null;
            _nextTargetEvaluationTime = -1;
            return true;
        }

        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            _takeCoverBehavior = Entity?.Components?.Get<SiTakeCoverBehaviorComponent>();
            _combatState = Entity?.Components?.Get<SiNpcCombatStateComponent>();
        }

        float ISiUtilityBehavior.Evaluate(SiUtilityContext context)
        {
            var weapon = GetWeapon();
            if (weapon == null || !weapon.IsOperational)
            {
                _target = null;
                _nextTargetEvaluationTime = -1;
                return 0;
            }

            var target = GetTrackedTarget(context, false, out var distance);
            if (target == null)
            {
                _lastSpottedTargetId = 0;
                return 0;
            }

            TryReportSpotting(context, target, distance);

            var normalizedDistance = Definition.SearchRadius > 0
                ? MathHelper.Clamp(1f - (float)(distance / Definition.SearchRadius), 0, 1)
                : 1;
            var score = Definition.BaseScore
                        + Definition.DistanceScore
                        * (float)Math.Pow(normalizedDistance, Definition.DistanceExponent);

            score = Math.Min(score, Definition.MaxScore);

            return score;
        }

        void ISiUtilityBehavior.Begin(SiUtilityContext context)
        {
            GetWeapon()?.ResetState();
            TrySpeakWithCooldown(
                context,
                Definition.EngageSpeech,
                ref _lastEngageSpeechTime,
                Definition.EngageSpeechCooldownMilliseconds);
        }

        void ISiUtilityBehavior.Tick(SiUtilityContext context, long elapsedMilliseconds)
        {
            var session = SiNpcSessionComponent.Instance;
            var weapon = GetWeapon();
            var stance = session?.GetEngagementStance(context.Agent) ?? SiSquadEngagementStance.HoldFire;
            var runningToCover = _takeCoverBehavior?.IsRunningToCover(context) ?? false;
            if (weapon == null || !weapon.IsOperational)
            {
                _combatState?.SetFiring(false);
                weapon?.ClearFireIntent();
                LogFireBlockedWithCooldown(ref _lastFireBlockLogTime, FireBlockLogCooldownMilliseconds, context, null, "weapon-unavailable", 0, SiSpottingObservation.None, weapon);
                return;
            }

            if (stance == SiSquadEngagementStance.HoldFire)
            {
                _combatState?.SetFiring(false);
                weapon.ClearFireIntent();
                LogFireBlockedWithCooldown(ref _lastFireBlockLogTime, FireBlockLogCooldownMilliseconds, context, _target, "hold-fire", 0, SiSpottingObservation.None, weapon);
                return;
            }

            var target = GetTrackedTarget(context, false, out var distance);
            if (!IsValidTarget(context.Agent, target))
            {
                _combatState?.SetFiring(false);
                weapon.ClearFireIntent();
                LogFireBlockedWithCooldown(ref _lastFireBlockLogTime, FireBlockLogCooldownMilliseconds, context, target, "no-valid-target", distance, SiSpottingObservation.None, weapon);
                return;
            }

            var targetEntity = target.Entity;
            if (Definition.RotateToTarget)
                FaceTarget(context.Entity, targetEntity);

            weapon.Advance(elapsedMilliseconds);

            if (Definition.RequireLineOfSight && !HasLineOfSight(context.Entity, targetEntity, weapon.Definition.AimTargetHeight))
            {
                _combatState?.SetFiring(false);
                weapon.ClearFireIntent();
                LogFireBlockedWithCooldown(ref _lastFireBlockLogTime, FireBlockLogCooldownMilliseconds, context, target, "line-of-sight-blocked", distance, SiSpottingObservation.None, weapon);
                return;
            }

            var observation = TryReportSpotting(context, target, distance);
            if (!observation.IsSpotted || !observation.CanShootTarget)
            {
                _combatState?.SetFiring(false);
                weapon.ClearFireIntent();
                LogFireBlockedWithCooldown(ref _lastFireBlockLogTime, FireBlockLogCooldownMilliseconds, context, target, runningToCover ? "running-to-cover" : (observation.IsSpotted ? "target-not-shootable" : "spotting-not-confirmed"), distance, observation, weapon);
                return;
            }

            if (_combatState != null && !_combatState.AllowsFiring)
            {
                _combatState.SetFiring(false);
                weapon.ClearFireIntent();
                LogFireBlockedWithCooldown(ref _lastFireBlockLogTime, FireBlockLogCooldownMilliseconds, context, target, "combat-state-blocked", distance, observation, weapon);
                return;
            }

            _combatState?.SetFiring(true);

            var aimSwayDegrees = ComputeDetectionAimSwayDegrees(observation.SpottingSum);
            weapon.TryFire(
                context,
                targetEntity,
                target.Velocity,
                observation.SpottingSum,
                Definition.DetectionAccuracyWorseningMultiplier,
                aimSwayDegrees);
        }

        void ISiUtilityBehavior.End(SiUtilityContext context)
        {
            _target = null;
            _nextTargetEvaluationTime = -1;
            _lastSpottedTargetId = 0;
            _combatState?.SetFiring(false);
            GetWeapon()?.ClearFireIntent();
            GetWeapon()?.ResetState();
        }

        private SiNpcRangedWeaponComponent GetWeapon() =>
            Entity?.Components?.Get<SiNpcRangedWeaponComponent>();

        private float ComputeDetectionAimSwayDegrees(float detectionScore)
        {
            var clampedDetection = MathHelper.Clamp(detectionScore, 0, 1);
            return (1f - clampedDetection) * Definition.DetectionAccuracyWorseningMultiplier;
        }

        private SiSpottingObservation TryReportSpotting(SiUtilityContext context, ShootTarget target, double distance)
        {
            var observation = ObserveTarget(context, target, distance);
            if (target == null || !IsObservationVisible(observation))
                return observation;

            if (_lastSpottedTargetId == target.EntityId
                && !IsSpeechDue(_lastSpotSpeechTime, Definition.SpotSpeechCooldownMilliseconds))
                return observation;

            if (TrySpeakWithCooldown(
                    context,
                    CreateSpottingReport(context, target, distance),
                    ref _lastSpotSpeechTime,
                    Definition.SpotSpeechCooldownMilliseconds))
                _lastSpottedTargetId = target.EntityId;

            return observation;
        }

        private SiSpottingObservation ObserveTarget(
            SiUtilityContext context,
            ShootTarget target,
            double distance)
        {
            var spotting = SiNpcSessionComponent.Instance?.Spotting;
            if (context?.Agent == null || target?.Entity == null || spotting == null)
                return SiSpottingObservation.None;

            return spotting.ObserveTarget(
                context.Agent,
                target.Entity,
                Definition,
                GetWeaponAimHeight(),
                distance);
        }

        private string CreateSpottingReport(SiUtilityContext context, ShootTarget target, double distance)
        {
            var targetName = string.IsNullOrWhiteSpace(Definition.SpotTargetName)
                ? "target"
                : Definition.SpotTargetName.Trim();
            return targetName
                   + ", "
                   + RoundedDistanceMeters(distance)
                   + " meters, "
                   + RelativeBearing(context, target)
                   + ".";
        }

        private static int RoundedDistanceMeters(double distance)
        {
            var rounded = (int)(Math.Round(Math.Max(0, distance) / 10.0) * 10);
            return Math.Max(10, rounded);
        }

        private static string RelativeBearing(SiUtilityContext context, ShootTarget target)
        {
            var self = context?.Entity;
            var targetEntity = target?.Entity;
            if (self == null || targetEntity == null)
                return "front";

            var world = self.WorldMatrix;
            var up = NormalizedOrFallback(world.Up, Vector3D.Up);
            var toTarget = Vector3D.Reject(targetEntity.WorldMatrix.Translation - world.Translation, up);
            var distanceSquared = toTarget.LengthSquared();
            if (distanceSquared <= 0.0001)
                return "front";

            var direction = toTarget / Math.Sqrt(distanceSquared);
            var forward = NormalizedOrFallback(
                Vector3D.Reject(world.Forward, up),
                Vector3D.CalculatePerpendicularVector(up));
            var right = NormalizedOrFallback(Vector3D.Cross(forward, up), world.Right);
            var angle = Math.Atan2(
                Vector3D.Dot(direction, right),
                Vector3D.Dot(direction, forward)) * 180.0 / Math.PI;
            if (angle < 0)
                angle += 360;

            if (angle < 22.5 || angle >= 337.5)
                return "front";
            if (angle < 67.5)
                return "front-right";
            if (angle < 112.5)
                return "right";
            if (angle < 157.5)
                return "rear-right";
            if (angle < 202.5)
                return "rear";
            if (angle < 247.5)
                return "rear-left";
            if (angle < 292.5)
                return "left";
            return "front-left";
        }

        private static bool TrySpeakWithCooldown(
            SiUtilityContext context,
            string message,
            ref long lastSpeechTime,
            int cooldownMilliseconds)
        {
            var session = SiNpcSessionComponent.Instance;
            if (context == null
                || session == null
                || !session.ShowSquadChatter
                || string.IsNullOrWhiteSpace(message)
                || !IsSpeechDue(lastSpeechTime, cooldownMilliseconds))
                return false;

            if (!context.TrySpeak(message.Trim()))
                return false;

            lastSpeechTime = CurrentTimeMilliseconds();
            return true;
        }

        private static bool IsSpeechDue(long lastSpeechTime, int cooldownMilliseconds)
        {
            if (lastSpeechTime < 0 || cooldownMilliseconds <= 0)
                return true;

            return CurrentTimeMilliseconds() - lastSpeechTime >= cooldownMilliseconds;
        }

        private static long CurrentTimeMilliseconds()
        {
            var session = MyAPIGateway.Session;
            return session != null
                ? (long)session.ElapsedPlayTime.TotalMilliseconds
                : 0;
        }

        private ShootTarget FindBestTarget(
            SiUtilityContext context,
            out double bestDistance)
        {
            bestDistance = 0;
            var session = SiNpcSessionComponent.Instance;
            var manager = session?.Npcs;
            if (manager == null)
                return null;

            var stance = session.GetEngagementStance(context.Agent);
            if (stance == SiSquadEngagementStance.HoldFire)
                return null;

            ShootTarget best = null;
            var bestDistanceSquared = (double)Definition.SearchRadius * Definition.SearchRadius;
            var npcTotal = 0;
            var npcValid = 0;
            var npcOpposing = 0;
            var npcArchetype = 0;
            var npcInRange = 0;
            var npcSpotted = 0;
            foreach (var candidate in manager.Npcs.Values)
            {
                npcTotal++;
                var target = new ShootTarget(candidate);
                if (!IsValidTarget(context.Agent, target))
                    continue;
                npcValid++;
                if (!IsOpposing(context.Agent, candidate, session.Squads, stance))
                    continue;
                npcOpposing++;
                if (!CanTargetArchetype(context.Agent.Archetype, candidate.Archetype))
                    continue;
                npcArchetype++;

                var distanceSquared = Vector3D.DistanceSquared(
                    context.Position,
                    target.Entity.WorldMatrix.Translation);
                if (distanceSquared > bestDistanceSquared)
                    continue;
                npcInRange++;
                var distance = Math.Sqrt(distanceSquared);
                var observation = ObserveTarget(context, target, distance);
                if (!IsObservationVisible(observation))
                    continue;
                npcSpotted++;

                best = target;
                bestDistanceSquared = distanceSquared;
            }

            var playerTotal = 0;
            var playerValid = 0;
            var playerOpposing = 0;
            var playerInRange = 0;
            var playerSpotted = 0;
            if (MyPlayers.Static != null)
            {
                foreach (var entry in MyPlayers.Static.GetAllPlayers())
                {
                    playerTotal++;
                    var player = entry.Value;
                    var controlled = player?.ControlledEntity;
                    var target = new ShootTarget(player, controlled);
                    if (!IsValidTarget(context.Agent, target))
                        continue;
                    playerValid++;
                    if (!IsOpposingPlayer(context.Agent, player, session.Squads, stance))
                        continue;
                    playerOpposing++;

                    var distanceSquared = Vector3D.DistanceSquared(
                        context.Position,
                        target.Entity.WorldMatrix.Translation);
                    if (distanceSquared > bestDistanceSquared)
                        continue;
                    playerInRange++;
                    var distance = Math.Sqrt(distanceSquared);
                    var observation = ObserveTarget(context, target, distance);
                    if (!IsObservationVisible(observation))
                        continue;
                    playerSpotted++;

                    best = target;
                    bestDistanceSquared = distanceSquared;
                }
            }

            bestDistance = best != null ? Math.Sqrt(bestDistanceSquared) : 0;
            if (best == null)
                LogSearchWithCooldown(context, "no-target", npcTotal, npcValid, npcOpposing, npcArchetype, npcInRange, npcSpotted, playerTotal, playerValid, playerOpposing, playerInRange, playerSpotted);
            else
                LogSearchWithCooldown(context, "selected-target", npcTotal, npcValid, npcOpposing, npcArchetype, npcInRange, npcSpotted, playerTotal, playerValid, playerOpposing, playerInRange, playerSpotted, best, bestDistance);
            return best;
        }

        private float GetWeaponAimHeight() =>
            GetWeapon()?.Definition?.AimTargetHeight ?? 0.9f;

        internal bool TryObservePlayer(
            SiNpc observer,
            MyPlayer player,
            MyEntity targetEntity,
            SiNpcSessionComponent session,
            out SiSpottingObservation observation)
        {
            observation = SiSpottingObservation.None;
            if (observer == null
                || player?.Identity == null
                || targetEntity == null
                || session?.Spotting == null)
                return false;

            var weapon = GetWeapon();
            if (weapon == null || !weapon.IsOperational)
                return false;

            var target = new ShootTarget(player, targetEntity);
            if (!IsValidTarget(observer, target))
                return false;

            var stance = session.GetEngagementStance(observer);
            if (!IsOpposingPlayer(observer, player, session.Squads, stance))
                return false;

            var distanceSquared = Vector3D.DistanceSquared(
                observer.Entity.WorldMatrix.Translation,
                targetEntity.WorldMatrix.Translation);
            var searchRadiusSquared = (double)Definition.SearchRadius * Definition.SearchRadius;
            if (distanceSquared > searchRadiusSquared)
                return false;

            observation = session.Spotting.ObserveTarget(
                observer,
                targetEntity,
                Definition,
                GetWeaponAimHeight(),
                Math.Sqrt(distanceSquared));
            return true;
        }

        internal bool TryGetCurrentThreat(
            SiUtilityContext context,
            out MyEntity targetEntity,
            out double distance)
        {
            Vector3D ignoredPosition;
            return TryGetCurrentThreat(context, out targetEntity, out ignoredPosition, out distance);
        }

        internal bool TryGetCurrentThreat(
            SiUtilityContext context,
            out MyEntity targetEntity,
            out Vector3D targetPosition,
            out double distance)
        {
            targetEntity = null;
            targetPosition = Vector3D.Zero;
            distance = 0;
            if (context?.Agent == null)
                return false;

            var target = GetTrackedTarget(context, true, out distance);
            if (target?.Entity == null)
                return false;

            _target = target;
            var observation = ObserveTarget(context, target, distance);
            targetEntity = ResolveThreatEntity(target, observation);
            if (targetEntity == null)
                return false;

            if (observation.VehicleSpotted
                && observation.VehicleTargetPosition != Vector3D.Zero
                && targetEntity.EntityId == observation.VehicleEntityId)
                targetPosition = observation.VehicleTargetPosition;
            else
                targetPosition = targetEntity.WorldMatrix.Translation;

            distance = Vector3D.Distance(context.Position, targetPosition);
            return true;
        }

        private ShootTarget GetTrackedTarget(
            SiUtilityContext context,
            bool forceRefresh,
            out double distance)
        {
            distance = 0;
            if (context?.Agent == null)
                return null;

            if (_nextTargetEvaluationTime < 0)
                InitializeTargetEvaluationSchedule();

            var current = _target;
            var currentIsValid = IsValidTarget(context.Agent, current);
            SiSpottingObservation currentObservation = SiSpottingObservation.None;
            if (currentIsValid)
            {
                distance = Vector3D.Distance(
                    context.Position,
                    current.Entity.WorldMatrix.Translation);
                currentObservation = ObserveTarget(context, current, distance);
                if (!IsObservationVisible(currentObservation))
                {
                    LogTargetStateWithCooldown(context, current, distance, currentObservation, "current-target-unspotted");
                    currentIsValid = false;
                    forceRefresh = true;
                }
                else if (!forceRefresh
                         && !IsTargetEvaluationDue()
                         && HasCloserSpottedTarget(context, distance))
                {
                    LogTargetStateWithCooldown(context, current, distance, currentObservation, "closer-spotted-target-available");
                    forceRefresh = true;
                }
            }

            if (!forceRefresh && currentIsValid && !IsTargetEvaluationDue())
                return current;

            if (!currentIsValid)
            {
                LogTargetStateWithCooldown(context, current, distance, currentObservation, "refresh-invalid-target");
                forceRefresh = true;
            }

            if (!forceRefresh && !IsTargetEvaluationDue())
            {
                LogTargetStateWithCooldown(context, current, distance, currentObservation, "evaluation-not-due-no-refresh");
                return null;
            }

            var previous = _target;
            current = FindBestTarget(context, out distance);
            _target = current;
            MarkTargetEvaluation();
            if (current == null)
                LogTargetStateWithCooldown(context, previous, distance, currentObservation, "target-refresh-found-none");
            else if (previous == null || previous.EntityId != current.EntityId)
                LogTargetStateWithCooldown(context, current, distance, ObserveTarget(context, current, distance), "target-changed");
            if (current == null)
                _lastSpottedTargetId = 0;
            return current;
        }

        private bool HasCloserSpottedTarget(SiUtilityContext context, double currentDistance)
        {
            var spotting = SiNpcSessionComponent.Instance?.Spotting;
            if (context?.Agent == null || spotting == null || currentDistance <= 0.5)
                return false;

            return spotting.HasSpottedTargetNearby(context.Agent.EntityId, Math.Max(0, currentDistance - 0.5));
        }

        private static bool IsObservationVisible(SiSpottingObservation observation) =>
            observation.IsSpotted || observation.VehicleSpotted;

        private static MyEntity ResolveThreatEntity(ShootTarget target, SiSpottingObservation observation)
        {
            if (target?.Entity == null)
                return null;

            if (observation.VehicleSpotted
                && !observation.CanShootTarget
                && observation.VehicleEntityId != 0)
                return MyAPIGateway.Entities?.GetEntityById(observation.VehicleEntityId) as MyEntity;

            return target.Entity;
        }

        private bool IsTargetEvaluationDue() =>
            CurrentTimeMilliseconds() >= _nextTargetEvaluationTime;

        private void InitializeTargetEvaluationSchedule()
        {
            var now = CurrentTimeMilliseconds();
            _nextTargetEvaluationTime = now + ResolveInitialTargetEvaluationDelayMilliseconds();
        }

        private void MarkTargetEvaluation()
        {
            _nextTargetEvaluationTime = CurrentTimeMilliseconds() + Definition.TargetReevaluationIntervalMilliseconds;
        }

        private long ResolveInitialTargetEvaluationDelayMilliseconds()
        {
            var interval = Definition.TargetReevaluationIntervalMilliseconds;
            var entityId = Entity?.EntityId ?? 0;
            if (interval <= 0 || entityId == 0)
                return 0;

            return Math.Abs(entityId % (interval + 1));
        }

        internal float GetWeaponAimHeightForCover() =>
            GetWeaponAimHeight();

        internal float GetWeaponMuzzleForwardOffsetForCover() =>
            GetWeapon()?.Definition?.MuzzleForwardOffset ?? 0;

        internal float GetWeaponMuzzleUpOffsetForCover() =>
            GetWeapon()?.Definition?.MuzzleUpOffset ?? GetWeaponAimHeight();

        private bool IsOpposing(SiNpc self, SiNpc candidate, SiSquadBook squads, SiSquadEngagementStance stance)
        {
            if (stance == SiSquadEngagementStance.HoldFire)
                return false;

            SiAssignedNpc selfAssignment = null;
            SiAssignedNpc candidateAssignment = null;
            var hasSelfAssignment = squads != null && squads.TryGetAssignment(self.EntityId, out selfAssignment);
            var hasCandidateAssignment = squads != null && squads.TryGetAssignment(candidate.EntityId, out candidateAssignment);
            if (hasSelfAssignment && hasCandidateAssignment)
            {
                if (selfAssignment.Leader.Army.Equals(candidateAssignment.Leader.Army))
                    return false;
                if (stance == SiSquadEngagementStance.EnemiesNeutrals)
                    return true;

                return HasHostileRelationship(
                    self,
                    selfAssignment,
                    candidate,
                    candidateAssignment);
            }

            if (stance == SiSquadEngagementStance.Enemies)
                return HasHostileRelationship(
                    self,
                    hasSelfAssignment ? selfAssignment : null,
                    candidate,
                    hasCandidateAssignment ? candidateAssignment : null);

            return !string.Equals(self.Archetype, candidate.Archetype, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsOpposingPlayer(
            SiNpc self,
            MyPlayer player,
            SiSquadBook squads,
            SiSquadEngagementStance stance)
        {
            if (stance == SiSquadEngagementStance.HoldFire
                || self == null
                || player?.Identity == null)
                return false;

            SiAssignedNpc selfAssignment = null;
            var hasSelfAssignment = squads != null && squads.TryGetAssignment(self.EntityId, out selfAssignment);
            if (!hasSelfAssignment && self.DiplomaticIdentityId == 0)
                return false;

            var playerArmy = SiSquadBook.ArmyForPlayerIdentity(player.Identity.Id);
            if (hasSelfAssignment && selfAssignment.Leader.Army.Equals(playerArmy))
                return false;

            return stance == SiSquadEngagementStance.EnemiesNeutrals
                   || HasHostileRelationship(
                       self,
                       hasSelfAssignment ? selfAssignment : null,
                       player);
        }

        private static bool HasHostileRelationship(
            SiNpc self,
            SiAssignedNpc selfAssignment,
            SiNpc candidate,
            SiAssignedNpc candidateAssignment)
        {
            MyDiplomaticParty selfParty;
            MyDiplomaticParty candidateParty;
            return TryCreateNpcDiplomaticParty(self, selfAssignment, out selfParty)
                   && TryCreateNpcDiplomaticParty(candidate, candidateAssignment, out candidateParty)
                   && HasHostileRelationship(selfParty, candidateParty);
        }

        private static bool HasHostileRelationship(
            SiNpc self,
            SiAssignedNpc selfAssignment,
            MyPlayer player)
        {
            MyDiplomaticParty selfParty;
            MyDiplomaticParty playerParty;
            return TryCreateNpcDiplomaticParty(self, selfAssignment, out selfParty)
                   && TryCreatePlayerDiplomaticParty(player, out playerParty)
                   && HasHostileRelationship(selfParty, playerParty);
        }

        private static bool TryCreateNpcDiplomaticParty(
            SiNpc npc,
            SiAssignedNpc assignment,
            out MyDiplomaticParty party)
        {
            party = default(MyDiplomaticParty);
            if (assignment != null
                && SiSquadBook.TryCreateDiplomaticParty(assignment.Leader.Army, out party))
                return true;

            if (npc != null && npc.DiplomaticIdentityId != 0)
            {
                var faction = PlayerFaction(npc.DiplomaticIdentityId);
                party = faction != null
                    ? new MyDiplomaticParty(faction)
                    : new MyDiplomaticParty(DiplomaticPartyType.Player, npc.DiplomaticIdentityId);
                return true;
            }

            return false;
        }

        private static bool TryCreatePlayerDiplomaticParty(MyPlayer player, out MyDiplomaticParty party)
        {
            party = default(MyDiplomaticParty);
            if (player?.Identity == null)
                return false;

            return SiSquadBook.TryCreateDiplomaticParty(
                SiSquadBook.ArmyForPlayerIdentity(player.Identity.Id),
                out party);
        }

        private static MyFaction PlayerFaction(long identityId)
        {
            try
            {
                return MyFactionManager.GetPlayerFaction(identityId);
            }
            catch
            {
                return null;
            }
        }

        private static bool HasHostileRelationship(
            MyDiplomaticParty selfParty,
            MyDiplomaticParty candidateParty)
        {
            var diplomacy = MyDiplomacyManager.Instance;
            if (diplomacy == null)
                return false;

            return IsHostileRelationship(diplomacy, selfParty, candidateParty)
                   || IsHostileRelationship(diplomacy, candidateParty, selfParty);
        }

        private static bool IsHostileRelationship(
            MyDiplomacyManager diplomacy,
            MyDiplomaticParty selfParty,
            MyDiplomaticParty candidateParty)
        {
            if (diplomacy == null)
                return false;

            try
            {
                return diplomacy.GetRelationshipBetweenParties(selfParty, candidateParty).Status == HostileRelationship;
            }
            catch
            {
                return false;
            }
        }

        internal bool CanTargetArchetype(string selfArchetype, string candidateArchetype)
        {
            if (Definition.TargetArchetypes.Length == 0)
                return !string.Equals(selfArchetype, candidateArchetype, StringComparison.OrdinalIgnoreCase);

            for (var i = 0; i < Definition.TargetArchetypes.Length; i++)
                if (string.Equals(Definition.TargetArchetypes[i], candidateArchetype, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static bool IsValidTarget(SiNpc self, ShootTarget target)
        {
            if (self == null || target?.Entity == null)
                return false;
            if (target.Npc?.IsDead ?? false)
                return false;

            var entity = target.Entity;
            return entity != self.Entity
                   && entity.EntityId != self.EntityId
                   && entity.InScene
                   && !entity.Closed
                   && !entity.MarkedForClose;
        }

        private static Vector3D TargetVelocity(ShootTarget target)
        {
            if (target == null)
                return Vector3D.Zero;
            if (target.Npc != null)
                return TargetVelocity(target.Npc);
            return target.Entity?.Physics != null
                ? target.Entity.Physics.LinearVelocity
                : Vector3D.Zero;
        }

        private static Vector3D TargetVelocity(SiNpc target)
        {
            if (target is SiGroundedNpc grounded)
                return grounded.Velocity;
            return target?.Entity?.Physics != null
                ? target.Entity.Physics.LinearVelocity
                : Vector3D.Zero;
        }

        private void FaceTarget(MyEntity shooter, MyEntity target)
        {
            if (shooter == null || target == null)
                return;

            var world = shooter.WorldMatrix;
            var up = NormalizedOrFallback(world.Up, Vector3D.Up);
            var toTarget = Vector3D.Reject(target.WorldMatrix.Translation - world.Translation, up);
            if (toTarget.LengthSquared() <= 0.0001)
                return;

            var forward = toTarget / Math.Sqrt(toTarget.LengthSquared());
            shooter.WorldMatrix = MatrixD.CreateWorld(world.Translation, forward, up);
        }

        internal static bool HasLineOfSight(MyEntity shooter, MyEntity target, float aimHeight)
        {
            if (shooter == null || target == null)
                return false;

            var shooterUp = NormalizedOrFallback(shooter.WorldMatrix.Up, Vector3D.Up);
            var targetUp = NormalizedOrFallback(target.WorldMatrix.Up, shooterUp);
            var start = shooter.WorldMatrix.Translation + shooterUp * aimHeight;
            var end = target.WorldMatrix.Translation + targetUp * aimHeight;

            IHitInfo hit;
            if (!MyAPIGateway.Physics.CastRay(start, end, out hit))
                return true;

            return hit == null
                   || hit.HitEntity == null
                   || hit.HitEntity == target
                   || hit.HitEntity == shooter;
        }

        internal static Vector3D NormalizedOrFallback(in Vector3D value, in Vector3D fallback)
        {
            var lengthSquared = value.LengthSquared();
            return lengthSquared > 0.0001
                ? value / Math.Sqrt(lengthSquared)
                : fallback;
        }

        private void LogTargetStateWithCooldown(
            SiUtilityContext context,
            ShootTarget target,
            double distance,
            SiSpottingObservation observation,
            string outcome)
        {
            var now = CurrentTimeMilliseconds();
            if (_lastTargetLogTime >= 0 && now - _lastTargetLogTime < TargetLogCooldownMilliseconds)
                return;

            _lastTargetLogTime = now;
            _log.Warning($"entityId={Entity?.EntityId ?? 0} name={Entity?.Name ?? "null"} definition={DefinitionId.SubtypeName} debug target-state outcome={outcome} stance={SiNpcSessionComponent.Instance?.GetEngagementStance(context?.Agent) ?? SiSquadEngagementStance.HoldFire} currentTargetId={target?.EntityId ?? 0} currentTargetName={target?.Entity?.Name ?? "null"} currentTargetNpc={target?.Npc?.Archetype ?? "player-or-null"} distance={distance:0.00} spotted={observation.IsSpotted} spottingSum={observation.SpottingSum:0.000} spottingThreshold={observation.SpottingThreshold:0.000} forceDue={IsTargetEvaluationDue()} nextEval={_nextTargetEvaluationTime} now={now}"); // AGENT-DEBUG-LOG
        }

        private void LogSearchWithCooldown(
            SiUtilityContext context,
            string outcome,
            int npcTotal,
            int npcValid,
            int npcOpposing,
            int npcArchetype,
            int npcInRange,
            int npcSpotted,
            int playerTotal,
            int playerValid,
            int playerOpposing,
            int playerInRange,
            int playerSpotted,
            ShootTarget best = null,
            double bestDistance = 0)
        {
            var now = CurrentTimeMilliseconds();
            if (_lastSearchLogTime >= 0 && now - _lastSearchLogTime < SearchLogCooldownMilliseconds)
                return;

            _lastSearchLogTime = now;
            _log.Warning($"entityId={Entity?.EntityId ?? 0} name={Entity?.Name ?? "null"} definition={DefinitionId.SubtypeName} debug target-search outcome={outcome} stance={SiNpcSessionComponent.Instance?.GetEngagementStance(context?.Agent) ?? SiSquadEngagementStance.HoldFire} npcTotal={npcTotal} npcValid={npcValid} npcOpposing={npcOpposing} npcArchetype={npcArchetype} npcInRange={npcInRange} npcSpotted={npcSpotted} playerTotal={playerTotal} playerValid={playerValid} playerOpposing={playerOpposing} playerInRange={playerInRange} playerSpotted={playerSpotted} selectedTargetId={best?.EntityId ?? 0} selectedTargetName={best?.Entity?.Name ?? "null"} selectedTargetNpc={best?.Npc?.Archetype ?? "player-or-null"} selectedDistance={bestDistance:0.00}"); // AGENT-DEBUG-LOG
        }

        private void LogFireBlockedWithCooldown(
            ref long lastLogTime,
            long cooldownMilliseconds,
            SiUtilityContext context,
            ShootTarget target,
            string outcome,
            double distance,
            SiSpottingObservation observation,
            SiNpcRangedWeaponComponent weapon)
        {
            var now = CurrentTimeMilliseconds();
            if (lastLogTime >= 0 && now - lastLogTime < cooldownMilliseconds)
                return;

            lastLogTime = now;
            _log.Warning($"entityId={Entity?.EntityId ?? 0} name={Entity?.Name ?? "null"} definition={DefinitionId.SubtypeName} debug fire-blocked outcome={outcome} stance={SiNpcSessionComponent.Instance?.GetEngagementStance(context?.Agent) ?? SiSquadEngagementStance.HoldFire} targetId={target?.EntityId ?? 0} targetName={target?.Entity?.Name ?? "null"} targetNpc={target?.Npc?.Archetype ?? "player-or-null"} distance={distance:0.00} spotted={observation.IsSpotted} spottingSum={observation.SpottingSum:0.000} spottingThreshold={observation.SpottingThreshold:0.000} nextEval={_nextTargetEvaluationTime} now={now} weaponReady={weapon != null && weapon.IsOperational} takingCover={_takeCoverBehavior?.IsRunningToCover(context) ?? false}"); // AGENT-DEBUG-LOG
        }

        private sealed class ShootTarget
        {
            public ShootTarget(SiNpc npc)
            {
                Npc = npc;
                Entity = npc?.Entity;
                EntityId = npc?.EntityId ?? 0;
            }

            public ShootTarget(MyPlayer player, MyEntity entity)
            {
                Player = player;
                Entity = entity;
                EntityId = entity?.EntityId ?? 0;
            }

            public SiNpc Npc { get; }
            public MyPlayer Player { get; }
            public MyEntity Entity { get; }
            public long EntityId { get; }
            public Vector3D Velocity => TargetVelocity(this);
        }
    }
}
