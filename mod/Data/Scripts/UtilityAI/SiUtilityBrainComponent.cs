using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Xml.Serialization;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRageMath;

namespace Si.UtilityAI
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiUtilityBrainComponent : MyObjectBuilder_EntityComponent
    {
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiUtilityBrainComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        [DefaultValue(250)]
        public int DecisionIntervalMilliseconds = 250;

        [DefaultValue(0)]
        public int StartupDelayMilliseconds;

        [DefaultValue(0.05f)]
        public float SwitchScoreMargin = 0.05f;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiUtilityBrainComponentDefinition))]
    public class SiUtilityBrainComponentDefinition : MyEntityComponentDefinition
    {
        public int DecisionIntervalMilliseconds { get; private set; }
        public int StartupDelayMilliseconds { get; private set; }
        public float SwitchScoreMargin { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiUtilityBrainComponentDefinition)builder;
            DecisionIntervalMilliseconds = Math.Max(1, ob.DecisionIntervalMilliseconds);
            StartupDelayMilliseconds = Math.Max(0, ob.StartupDelayMilliseconds);
            SwitchScoreMargin = Math.Max(0, ob.SwitchScoreMargin);
        }
    }

    /// <summary>
    /// The narrow contract implemented by data-backed behaviors attached to the
    /// same entity as a utility brain.
    /// </summary>
    internal interface ISiUtilityBehavior
    {
        string BehaviorName { get; }

        float Evaluate(SiUtilityContext context);
        void Begin(SiUtilityContext context);
        void Tick(SiUtilityContext context, long elapsedMilliseconds);
        void End(SiUtilityContext context);
    }

    internal interface ISiContinuousUtilityBehavior
    {
    }

    /// <summary>
    /// Capabilities exposed to utility behaviors.  Behaviors request movement
    /// through the owning NPC manager so commands are replicated to clients.
    /// </summary>
    internal sealed class SiUtilityContext
    {
        private readonly SiNpc _agent;

        public SiUtilityContext(SiNpc agent)
        {
            _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        }

        public MyEntity Entity => _agent.Entity;
        public SiNpc Agent => _agent;
        public long EntityId => _agent.EntityId;
        public string Archetype => _agent.Archetype;
        public Vector3D Position => _agent.Entity.WorldMatrix.Translation;
        public Vector3D Velocity => _agent is SiGroundedNpc grounded
            ? grounded.Velocity
            : Vector3D.Zero;

        public bool HasWaypoint => _agent is ISiWaypointMover mover && mover.HasWaypoint;
        public Vector3D Waypoint => _agent is ISiWaypointMover mover
            ? mover.Waypoint
            : Vector3D.Zero;

        public bool TrySetWaypoint(in Vector3D waypoint) =>
            _agent.TrySetWaypoint(waypoint);

        public bool TryClearWaypoint() => _agent.TryClearWaypoint();

        public bool TrySpeak(string message) => _agent.TrySpeak(message);

        public bool TrySetCrouch(bool wantsCrouch)
        {
            var posture = _agent as ISiPostureController;
            if (posture == null)
                return false;

            posture.SetCrouch(wantsCrouch);
            return true;
        }
    }

    /// <summary>
    /// Selects the highest-scoring attached behavior at a configurable cadence.
    /// Selection uses a score margin to prevent two similarly useful behaviors
    /// from repeatedly stealing control from one another.
    /// </summary>
    [MyComponent(typeof(MyObjectBuilder_SiUtilityBrainComponent))]
    [MyDefinitionRequired(typeof(SiUtilityBrainComponentDefinition))]
    public class SiUtilityBrainComponent : MyEntityComponent
    {
        private readonly List<ISiUtilityBehavior> _behaviors = new List<ISiUtilityBehavior>();

        private SiUtilityBrainComponentDefinition _definition;
        private SiUtilityContext _context;
        private ISiUtilityBehavior _activeBehavior;
        private long _decisionCountdown;
        private long _startupDelayCountdown;
        private bool _decisionMakingEnabled = true;
        public string ActiveBehaviorName => _activeBehavior?.BehaviorName;
        public float ActiveBehaviorScore { get; private set; }

        public override bool IsSerialized => false;

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            _definition = (SiUtilityBrainComponentDefinition)definition;
        }

        internal void Bind(SiNpc agent)
        {
            Unbind();
            _context = new SiUtilityContext(agent);
            _behaviors.Clear();
            foreach (var behavior in Entity.Components.GetComponents<ISiUtilityBehavior>())
                _behaviors.Add(behavior);

            _decisionCountdown = 0;
            _startupDelayCountdown = _definition.StartupDelayMilliseconds;
            if (!IsAuthoritative)
                return;
            if (_startupDelayCountdown > 0)
                return;

            Decide();
            TickActiveBehavior(0);
        }

        internal void TickActiveBehavior(long elapsedMilliseconds)
        {
            if (_context == null || !IsAuthoritative || !_decisionMakingEnabled)
                return;

            if (_activeBehavior is ISiContinuousUtilityBehavior)
                _activeBehavior.Tick(_context, elapsedMilliseconds);
        }

        internal void AdvanceDecision(long elapsedMilliseconds)
        {
            if (_context == null || !IsAuthoritative || !_decisionMakingEnabled)
                return;

            if (_startupDelayCountdown > 0)
            {
                _startupDelayCountdown -= elapsedMilliseconds;
                if (_startupDelayCountdown > 0)
                    return;

                elapsedMilliseconds = 0;
            }

            _decisionCountdown -= elapsedMilliseconds;
            if (_decisionCountdown <= 0)
                Decide();

            if (!(_activeBehavior is ISiContinuousUtilityBehavior))
                _activeBehavior?.Tick(_context, elapsedMilliseconds);
        }

        internal void Unbind()
        {
            EndActiveBehavior();

            _activeBehavior = null;
            ActiveBehaviorScore = 0;
            _context = null;
            _behaviors.Clear();
            _decisionCountdown = 0;
            _startupDelayCountdown = 0;
            _decisionMakingEnabled = true;
        }

        internal void SetDecisionMakingEnabled(bool enabled)
        {
            if (_decisionMakingEnabled == enabled)
                return;

            _decisionMakingEnabled = enabled;
            if (!_decisionMakingEnabled)
            {
                EndActiveBehavior();
                _decisionCountdown = 0;
                ActiveBehaviorScore = 0;
                return;
            }

            if (_context == null || !IsAuthoritative)
                return;

            _startupDelayCountdown = 0;
            _decisionCountdown = 0;
            Decide();
            TickActiveBehavior(0);
        }

        private void Decide()
        {
            _decisionCountdown = _definition.DecisionIntervalMilliseconds;

            ISiUtilityBehavior best = null;
            var bestScore = 0f;
            var activeScore = 0f;
            foreach (var behavior in _behaviors)
            {
                var score = NormalizeScore(behavior.Evaluate(_context));
                if (ReferenceEquals(behavior, _activeBehavior))
                    activeScore = score;

                if (IsBetterCandidate(behavior, score, best, bestScore))
                {
                    best = behavior;
                    bestScore = score;
                }
            }

            if (bestScore <= 0)
            {
                best = null;
                bestScore = 0;
            }
            else if (_activeBehavior != null
                     && !ReferenceEquals(best, _activeBehavior)
                     && activeScore > 0
                     && bestScore < activeScore + _definition.SwitchScoreMargin)
            {
                best = _activeBehavior;
                bestScore = activeScore;
            }

            if (!ReferenceEquals(best, _activeBehavior))
            {
                _activeBehavior?.End(_context);
                _activeBehavior = best;
                _activeBehavior?.Begin(_context);
            }

            ActiveBehaviorScore = bestScore;
        }

        private bool IsBetterCandidate(
            ISiUtilityBehavior candidate,
            float candidateScore,
            ISiUtilityBehavior currentBest,
            float currentBestScore)
        {
            if (candidateScore > currentBestScore)
                return true;
            if (candidateScore < currentBestScore || candidateScore <= 0)
                return false;
            if (ReferenceEquals(candidate, _activeBehavior))
                return true;
            if (ReferenceEquals(currentBest, _activeBehavior))
                return false;

            return currentBest == null
                   || string.CompareOrdinal(candidate.BehaviorName, currentBest.BehaviorName) < 0;
        }

        private static float NormalizeScore(float score)
        {
            if (float.IsNaN(score) || score <= 0)
                return 0;
            return Math.Min(score, 1);
        }

        private void EndActiveBehavior()
        {
            if (_context != null && _activeBehavior != null && IsAuthoritative)
                _activeBehavior.End(_context);
            _activeBehavior = null;
        }

        private static bool IsAuthoritative =>
            MyMultiplayerModApi.Static == null || MyMultiplayerModApi.Static.IsServer;
    }
}
