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
        private static readonly Random AttemptRandom = new Random();
        private static readonly object AttemptRandomLock = new object();

        private SiSelfUnstuckTeleportBehaviorDefinition _definition;
        private SiTakeCoverBehaviorComponent _takeCoverBehavior;
        private SiTakePlainViewBehaviorComponent _takePlainViewBehavior;
        private long _lastEvaluationTime = -1;
        private long _stuckMilliseconds;
        private long _retryAfterMilliseconds = -1;

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

            if (ResolvePlanarSpeed(context) <= _definition.MaximumPlanarSpeed)
                _stuckMilliseconds = Math.Min(
                    Math.Max(0, _stuckMilliseconds + elapsedSinceLastEvaluation),
                    _definition.StuckTimeoutMilliseconds);
            else
                _stuckMilliseconds = 0;

            if (_stuckMilliseconds < _definition.StuckTimeoutMilliseconds)
                return 0;
            if (_retryAfterMilliseconds > now)
                return 0;

            return _definition.ActivationScore;
        }

        void ISiUtilityBehavior.Begin(SiUtilityContext context)
        {
            var now = CurrentTimeMilliseconds();
            _retryAfterMilliseconds = now + _definition.RetryCooldownMilliseconds;
            if (TryTeleportToEscapePoint(context))
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

        private bool TryTeleportToEscapePoint(SiUtilityContext context)
        {
            if (context?.Entity == null)
                return false;

            var entity = context.Entity;
            var up = ResolveUp(context.Position, entity.WorldMatrix.Up);
            var world = entity.WorldMatrix;
            var forward = SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(
                Vector3D.Reject(world.Forward, up),
                Vector3D.CalculatePerpendicularVector(up));
            var right = SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(
                Vector3D.Cross(forward, up),
                world.Right);

            double angle;
            double distance;
            lock (AttemptRandomLock)
            {
                angle = AttemptRandom.NextDouble() * Math.PI * 2d;
                distance = MathHelper.Lerp(
                    _definition.MinimumTeleportDistance,
                    _definition.MaximumTeleportDistance,
                    (float)AttemptRandom.NextDouble());
            }

            var direction = SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(
                forward * Math.Cos(angle) + right * Math.Sin(angle),
                forward);
            var probeCenter = context.Position + direction * distance;
            var rayStart = probeCenter + up * _definition.VerticalProbeHeight;
            var rayEnd = probeCenter - up * _definition.VerticalProbeDepth;

            IHitInfo hit;
            if (!MyAPIGateway.Physics.CastRay(rayStart, rayEnd, out hit) || hit == null)
                return false;

            var hitUp = SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback((Vector3D)hit.Normal, up);
            if (Vector3D.Dot(hitUp, up) < _definition.MinimumGroundUpDot)
                return false;

            var landingPosition = hit.Position + up * _definition.TeleportClearance;
            entity.PositionComp.WorldMatrix = MatrixD.CreateWorld(landingPosition, forward, up);
            if (entity.Physics != null)
            {
                entity.Physics.LinearVelocity = Vector3.Zero;
                entity.Physics.AngularVelocity = Vector3.Zero;
            }

            return true;
        }

        private double ResolvePlanarSpeed(SiUtilityContext context)
        {
            var up = ResolveUp(context.Position, context.Entity.WorldMatrix.Up);
            var planarVelocity = Vector3D.Reject(context.Velocity, up);
            return planarVelocity.Length();
        }

        private void ResetTracking()
        {
            _lastEvaluationTime = -1;
            _stuckMilliseconds = 0;
            _retryAfterMilliseconds = -1;
        }

        private void ResetTracking(long now)
        {
            _lastEvaluationTime = now;
            _stuckMilliseconds = 0;
            _retryAfterMilliseconds = -1;
        }

        private static Vector3D ResolveUp(in Vector3D position, in Vector3D fallbackUp)
        {
            var gravity = MyGravityProviderSystem.CalculateTotalGravityInPoint(position);
            if (gravity.LengthSquared() > 0.0001)
                return -Vector3D.Normalize(gravity);

            return SiShootOpposingNpcBehaviorComponent.NormalizedOrFallback(fallbackUp, Vector3D.Up);
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
