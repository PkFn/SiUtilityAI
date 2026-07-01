using System;
using System.Xml.Serialization;
using Medieval.GameSystems.Factions;
using Sandbox.Game.Players;
using Sandbox.ModAPI;
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
            DistanceScore = Math.Max(0, ob.DistanceScore);
            DistanceExponent = Math.Max(0.01f, ob.DistanceExponent);
            RequireLineOfSight = ob.RequireLineOfSight;
            RotateToTarget = ob.RotateToTarget;
            EngageSpeech = ob.EngageSpeech;
            EngageSpeechCooldownMilliseconds = Math.Max(0, ob.EngageSpeechCooldownMilliseconds);
            SpotTargetName = ob.SpotTargetName;
            SpotSpeechCooldownMilliseconds = Math.Max(0, ob.SpotSpeechCooldownMilliseconds);
            DetectionAccuracyWorseningMultiplier = Math.Max(1, ob.DetectionAccuracyWorseningMultiplier);
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
            DistanceScore = Math.Max(0, ob.DistanceScore);
            DistanceExponent = Math.Max(0.01f, ob.DistanceExponent);
            RequireLineOfSight = ob.RequireLineOfSight;
            RotateToTarget = ob.RotateToTarget;
            EngageSpeech = ob.EngageSpeech;
            EngageSpeechCooldownMilliseconds = Math.Max(0, ob.EngageSpeechCooldownMilliseconds);
            SpotTargetName = ob.SpotTargetName;
            SpotSpeechCooldownMilliseconds = Math.Max(0, ob.SpotSpeechCooldownMilliseconds);
            DetectionAccuracyWorseningMultiplier = Math.Max(1, ob.DetectionAccuracyWorseningMultiplier);
            TargetReevaluationIntervalMilliseconds = Math.Max(1, ob.TargetReevaluationIntervalMilliseconds);
            TargetArchetypes = ob.TargetArchetypes ?? EmptyArchetypes;
        }

        private void InitFromBalance(SiShootOpposingNpcBehaviorBalanceDefinition balance)
        {
            SearchRadius = balance.SearchRadius;
            BaseScore = balance.BaseScore;
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
    public class SiShootOpposingNpcBehaviorComponent : MyEntityComponent, ISiUtilityBehavior
    {
        private static readonly MyStringHash HostileRelationship = MyStringHash.GetOrCompute("War");

        private SiShootOpposingNpcBehaviorDefinition _definition;
        private SiTakeCoverBehaviorComponent _takeCoverBehavior;
        private ShootTarget _target;
        private long _nextTargetEvaluationTime = -1;
        private long _lastEngageSpeechTime = -1;
        private long _lastSpotSpeechTime = -1;
        private long _lastSpottedTargetId;

        public string BehaviorName => DefinitionId.ToString();

        public override bool IsSerialized => false;

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            _definition = (SiShootOpposingNpcBehaviorDefinition)definition;
            _definition.ResolveBalance();
        }

        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            _takeCoverBehavior = Entity?.Components?.Get<SiTakeCoverBehaviorComponent>();
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

            var normalizedDistance = _definition.SearchRadius > 0
                ? MathHelper.Clamp(1f - (float)(distance / _definition.SearchRadius), 0, 1)
                : 1;
            var score = _definition.BaseScore
                        + _definition.DistanceScore
                        * (float)Math.Pow(normalizedDistance, _definition.DistanceExponent);
            return score;
        }

        void ISiUtilityBehavior.Begin(SiUtilityContext context)
        {
            GetWeapon()?.ResetState();
            TrySpeakWithCooldown(
                context,
                _definition.EngageSpeech,
                ref _lastEngageSpeechTime,
                _definition.EngageSpeechCooldownMilliseconds);
        }

        void ISiUtilityBehavior.Tick(SiUtilityContext context, long elapsedMilliseconds)
        {
            var session = SiNpcSessionComponent.Instance;
            var weapon = GetWeapon();
            if (weapon == null
                || !weapon.IsOperational
                || session?.GetEngagementStance(context.Agent) == SiSquadEngagementStance.HoldFire)
                return;

            if (_takeCoverBehavior?.IsRunningToCover(context) ?? false)
                return;

            var target = GetTrackedTarget(context, false, out var distance);
            if (!IsValidTarget(context.Agent, target))
                return;

            var targetEntity = target.Entity;
            if (_definition.RotateToTarget)
                FaceTarget(context.Entity, targetEntity);

            weapon.Advance(elapsedMilliseconds);

            if (_definition.RequireLineOfSight && !HasLineOfSight(context.Entity, targetEntity, weapon.Definition.AimTargetHeight))
                return;

            var observation = TryReportSpotting(context, target, distance);
            if (!observation.IsSpotted)
                return;

            weapon.TryFire(
                context,
                targetEntity,
                target.Velocity,
                observation.SpottingSum,
                _definition.DetectionAccuracyWorseningMultiplier);
        }

        void ISiUtilityBehavior.End(SiUtilityContext context)
        {
            _target = null;
            _nextTargetEvaluationTime = -1;
            _lastSpottedTargetId = 0;
            GetWeapon()?.ResetState();
        }

        private SiNpcRangedWeaponComponent GetWeapon() =>
            Entity?.Components?.Get<SiNpcRangedWeaponComponent>();

        private SiSpottingObservation TryReportSpotting(SiUtilityContext context, ShootTarget target, double distance)
        {
            var observation = ObserveTarget(context, target, distance);
            if (target == null || !observation.IsSpotted)
                return observation;

            if (_lastSpottedTargetId == target.EntityId
                && !IsSpeechDue(_lastSpotSpeechTime, _definition.SpotSpeechCooldownMilliseconds))
                return observation;

            if (TrySpeakWithCooldown(
                    context,
                    CreateSpottingReport(context, target, distance),
                    ref _lastSpotSpeechTime,
                    _definition.SpotSpeechCooldownMilliseconds))
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
                _definition,
                GetWeaponAimHeight(),
                distance);
        }

        private string CreateSpottingReport(SiUtilityContext context, ShootTarget target, double distance)
        {
            var targetName = string.IsNullOrWhiteSpace(_definition.SpotTargetName)
                ? "target"
                : _definition.SpotTargetName.Trim();
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
            var bestDistanceSquared = (double)_definition.SearchRadius * _definition.SearchRadius;
            foreach (var candidate in manager.Npcs.Values)
            {
                var target = new ShootTarget(candidate);
                if (!IsValidTarget(context.Agent, target))
                    continue;
                if (!IsOpposing(context.Agent, candidate, session.Squads, stance))
                    continue;
                if (!CanTargetArchetype(context.Agent.Archetype, candidate.Archetype))
                    continue;

                var distanceSquared = Vector3D.DistanceSquared(
                    context.Position,
                    target.Entity.WorldMatrix.Translation);
                if (distanceSquared > bestDistanceSquared)
                    continue;
                var distance = Math.Sqrt(distanceSquared);
                var observation = ObserveTarget(context, target, distance);
                if (!observation.IsSpotted)
                    continue;

                best = target;
                bestDistanceSquared = distanceSquared;
            }

            if (MyPlayers.Static != null)
            {
                foreach (var entry in MyPlayers.Static.GetAllPlayers())
                {
                    var player = entry.Value;
                    var controlled = player?.ControlledEntity;
                    var target = new ShootTarget(player, controlled);
                    if (!IsValidTarget(context.Agent, target))
                        continue;
                    if (!IsOpposingPlayer(context.Agent, player, session.Squads, stance))
                        continue;

                    var distanceSquared = Vector3D.DistanceSquared(
                        context.Position,
                        target.Entity.WorldMatrix.Translation);
                    if (distanceSquared > bestDistanceSquared)
                        continue;
                    var distance = Math.Sqrt(distanceSquared);
                    var observation = ObserveTarget(context, target, distance);
                    if (!observation.IsSpotted)
                        continue;

                    best = target;
                    bestDistanceSquared = distanceSquared;
                }
            }

            bestDistance = best != null ? Math.Sqrt(bestDistanceSquared) : 0;
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
            var searchRadiusSquared = (double)_definition.SearchRadius * _definition.SearchRadius;
            if (distanceSquared > searchRadiusSquared)
                return false;

            observation = session.Spotting.ObserveTarget(
                observer,
                targetEntity,
                _definition,
                GetWeaponAimHeight(),
                Math.Sqrt(distanceSquared));
            return true;
        }

        internal bool TryGetCurrentThreat(
            SiUtilityContext context,
            out MyEntity targetEntity,
            out double distance)
        {
            targetEntity = null;
            distance = 0;
            if (context?.Agent == null)
                return false;

            var target = GetTrackedTarget(context, true, out distance);
            if (target?.Entity == null)
                return false;

            _target = target;
            targetEntity = target.Entity;
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
            if (!forceRefresh && currentIsValid && !IsTargetEvaluationDue())
            {
                distance = Vector3D.Distance(
                    context.Position,
                    current.Entity.WorldMatrix.Translation);
                return current;
            }

            if (!currentIsValid)
                forceRefresh = true;

            if (!forceRefresh && !IsTargetEvaluationDue())
                return null;

            current = FindBestTarget(context, out distance);
            _target = current;
            MarkTargetEvaluation();
            if (current == null)
                _lastSpottedTargetId = 0;
            return current;
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
            _nextTargetEvaluationTime = CurrentTimeMilliseconds() + _definition.TargetReevaluationIntervalMilliseconds;
        }

        private long ResolveInitialTargetEvaluationDelayMilliseconds()
        {
            var interval = _definition.TargetReevaluationIntervalMilliseconds;
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
            if (_definition.TargetArchetypes.Length == 0)
                return !string.Equals(selfArchetype, candidateArchetype, StringComparison.OrdinalIgnoreCase);

            for (var i = 0; i < _definition.TargetArchetypes.Length; i++)
                if (string.Equals(_definition.TargetArchetypes[i], candidateArchetype, StringComparison.OrdinalIgnoreCase))
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
