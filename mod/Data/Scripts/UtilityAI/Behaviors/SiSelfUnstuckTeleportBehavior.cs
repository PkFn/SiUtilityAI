using System;
using System.ComponentModel;
using System.Xml.Serialization;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Entities.Gravity;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace Si.UtilityAI
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiSelfUnstuckTeleportBehavior : MyObjectBuilder_EntityComponent
    {
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiSelfUnstuckTeleportBehaviorDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        [DefaultValue(1f)]
        public float ActivationScore = 1f;

        [DefaultValue(2500)]
        public int StuckTimeoutMilliseconds = 2500;

        [DefaultValue(0.2f)]
        public float MaximumPlanarSpeed = 0.2f;

        [DefaultValue(1.5f)]
        public float MinimumRemainingDistance = 1.5f;

        [DefaultValue(2.5f)]
        public float MinimumTeleportDistance = 2.5f;

        [DefaultValue(7f)]
        public float MaximumTeleportDistance = 7f;

        [DefaultValue(4f)]
        public float VerticalProbeHeight = 4f;

        [DefaultValue(10f)]
        public float VerticalProbeDepth = 10f;

        [DefaultValue(0.35f)]
        public float TeleportClearance = 0.35f;

        [DefaultValue(0.6f)]
        public float MinimumGroundUpDot = 0.6f;

        [DefaultValue(250)]
        public int RetryCooldownMilliseconds = 250;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiSelfUnstuckTeleportBehaviorDefinition))]
    public class SiSelfUnstuckTeleportBehaviorDefinition : MyEntityComponentDefinition
    {
        public float ActivationScore { get; private set; }
        public int StuckTimeoutMilliseconds { get; private set; }
        public float MaximumPlanarSpeed { get; private set; }
        public float MinimumRemainingDistance { get; private set; }
        public float MinimumTeleportDistance { get; private set; }
        public float MaximumTeleportDistance { get; private set; }
        public float VerticalProbeHeight { get; private set; }
        public float VerticalProbeDepth { get; private set; }
        public float TeleportClearance { get; private set; }
        public float MinimumGroundUpDot { get; private set; }
        public int RetryCooldownMilliseconds { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiSelfUnstuckTeleportBehaviorDefinition)builder;
            ActivationScore = Math.Max(0, ob.ActivationScore);
            StuckTimeoutMilliseconds = Math.Max(1, ob.StuckTimeoutMilliseconds);
            MaximumPlanarSpeed = Math.Max(0, ob.MaximumPlanarSpeed);
            MinimumRemainingDistance = Math.Max(0.1f, ob.MinimumRemainingDistance);
            MinimumTeleportDistance = Math.Max(0.1f, ob.MinimumTeleportDistance);
            MaximumTeleportDistance = Math.Max(MinimumTeleportDistance, ob.MaximumTeleportDistance);
            VerticalProbeHeight = Math.Max(0.1f, ob.VerticalProbeHeight);
            VerticalProbeDepth = Math.Max(VerticalProbeHeight, ob.VerticalProbeDepth);
            TeleportClearance = Math.Max(0.05f, ob.TeleportClearance);
            MinimumGroundUpDot = MathHelper.Clamp(ob.MinimumGroundUpDot, 0, 1);
            RetryCooldownMilliseconds = Math.Max(0, ob.RetryCooldownMilliseconds);
        }
    }

    [MyComponent(typeof(MyObjectBuilder_SiSelfUnstuckTeleportBehavior))]
    [MyDefinitionRequired(typeof(SiSelfUnstuckTeleportBehaviorDefinition))]
    public class SiSelfUnstuckTeleportBehaviorComponent : MyEntityComponent, ISiUtilityBehavior
    {
        private SiSelfUnstuckTeleportBehaviorDefinition _definition;
        private SiTakeCoverBehaviorComponent _takeCoverBehavior;
        private SiTakePlainViewBehaviorComponent _takePlainViewBehavior;
        private long _lastEvaluationTime = -1;
        private readonly SiUnstuckTeleportService.State _unstuckState = new SiUnstuckTeleportService.State();

        public string BehaviorName => DefinitionId.ToString();

        public override bool IsSerialized => false;

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            _definition = (SiSelfUnstuckTeleportBehaviorDefinition)definition;
        }

        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            _takeCoverBehavior = Entity?.Components?.Get<SiTakeCoverBehaviorComponent>();
            _takePlainViewBehavior = Entity?.Components?.Get<SiTakePlainViewBehaviorComponent>();
        }

        float ISiUtilityBehavior.Evaluate(SiUtilityContext context)
        {
            var session = SiNpcSessionComponent.Instance;
            if (context?.Agent == null || context.Entity == null || session == null)
            {
                ResetTracking();
                return 0;
            }

            var now = CurrentTimeMilliseconds();
            if (!HasOrderedMove(context, session))
            {
                ResetTracking(now);
                return 0;
            }

            var remainingDistanceSquared = Vector3D.DistanceSquared(context.Position, context.Waypoint);
            if (remainingDistanceSquared <= _definition.MinimumRemainingDistance * _definition.MinimumRemainingDistance)
            {
                ResetTracking(now);
                return 0;
            }

            var elapsedSinceLastEvaluation = _lastEvaluationTime < 0
                ? 0
                : Math.Max(0, now - _lastEvaluationTime);
            _lastEvaluationTime = now;

            return IsStuckAndEligible(context, elapsedSinceLastEvaluation, now)
                ? _definition.ActivationScore
                : 0;
        }

        void ISiUtilityBehavior.Begin(SiUtilityContext context)
        {
            var now = CurrentTimeMilliseconds();
            if (TryTeleportToEscapePoint(context, now))
                ResetTracking(now);
        }

        void ISiUtilityBehavior.Tick(SiUtilityContext context, long elapsedMilliseconds)
        {
        }

        void ISiUtilityBehavior.End(SiUtilityContext context)
        {
        }

        private bool HasOrderedMove(SiUtilityContext context, SiNpcSessionComponent session)
        {
            if (context == null || !context.HasWaypoint)
                return false;
            if (session.TryGetTransportMode(context.Agent, out var transportMode)
                && transportMode != SiSquadTransportMode.None)
                return false;

            return session.IsFollowingPlayer(context.Agent)
                   || (_takeCoverBehavior?.IsRunningToCover(context) ?? false)
                   || (_takePlainViewBehavior?.IsMovingToPlainView(context) ?? false);
        }

        private bool IsStuckAndEligible(SiUtilityContext context, long elapsedSinceLastEvaluation, long now)
        {
            if (context?.Entity == null)
                return false;

            return SiUnstuckTeleportService.TryUnstuckToWaypoint(
                context.Entity,
                context.Position,
                context.Velocity,
                context.Waypoint,
                elapsedSinceLastEvaluation,
                now,
                CreateSettings(),
                _unstuckState);
        }

        private bool TryTeleportToEscapePoint(SiUtilityContext context, long now)
        {
            if (context?.Entity == null)
                return false;

            _unstuckState.StuckMilliseconds = _definition.StuckTimeoutMilliseconds;
            _unstuckState.RetryAfterMilliseconds = Math.Min(_unstuckState.RetryAfterMilliseconds, now);
            return SiUnstuckTeleportService.TryUnstuckToWaypoint(
                context.Entity,
                context.Position,
                context.Velocity,
                context.Waypoint,
                0,
                now,
                CreateSettings(),
                _unstuckState);
        }

        private void ResetTracking()
        {
            _lastEvaluationTime = -1;
            _unstuckState.Reset();
        }

        private void ResetTracking(long now)
        {
            _lastEvaluationTime = now;
            _unstuckState.Reset();
        }

        private SiUnstuckTeleportService.Settings CreateSettings()
        {
            return new SiUnstuckTeleportService.Settings
            {
                StuckTimeoutMilliseconds = _definition.StuckTimeoutMilliseconds,
                MaximumPlanarSpeed = _definition.MaximumPlanarSpeed,
                MinimumRemainingDistance = _definition.MinimumRemainingDistance,
                MinimumTeleportDistance = _definition.MinimumTeleportDistance,
                MaximumTeleportDistance = _definition.MaximumTeleportDistance,
                VerticalProbeHeight = _definition.VerticalProbeHeight,
                VerticalProbeDepth = _definition.VerticalProbeDepth,
                TeleportClearance = _definition.TeleportClearance,
                MinimumGroundUpDot = _definition.MinimumGroundUpDot,
                RetryCooldownMilliseconds = _definition.RetryCooldownMilliseconds,
            };
        }

        private static long CurrentTimeMilliseconds()
        {
            var session = MyAPIGateway.Session;
            return session != null
                ? (long)session.ElapsedPlayTime.TotalMilliseconds
                : 0;
        }
    }
}
