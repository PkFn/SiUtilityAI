using System;
using System.ComponentModel;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Controller;
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
    public class MyObjectBuilder_SiUseTransportSeatsBehavior : MyObjectBuilder_EntityComponent
    {
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiUseTransportSeatsBehaviorDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        [DefaultValue(2.25f)]
        public float InstantMountDistance = 2.25f;

        [DefaultValue(0.75f)]
        public float WaypointRefreshDistance = 0.75f;

        [DefaultValue(1.25f)]
        public float ExitArrivalDistance = 1.25f;

        [DefaultValue(500L)]
        public long ActionIntervalMilliseconds = 500L;

        public float VehicleStopDistance;
        public float VehicleSlowDistance;
        public float VehicleTurnDeadZone;
        public float VehicleMinimumForwardAlignment;
        public int VehicleCruiseActionRepeatCount;
        public int VehicleCatchUpActionRepeatCount;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiUseTransportSeatsBehaviorDefinition))]
    public class SiUseTransportSeatsBehaviorDefinition : MyEntityComponentDefinition
    {
        public float InstantMountDistance { get; private set; }
        public float WaypointRefreshDistance { get; private set; }
        public float ExitArrivalDistance { get; private set; }
        public long ActionIntervalMilliseconds { get; private set; }
        internal SiMountedVehicleDriveSettings VehicleDriveSettings { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiUseTransportSeatsBehaviorDefinition)builder;
            InstantMountDistance = Math.Max(0.25f, ob.InstantMountDistance);
            WaypointRefreshDistance = Math.Max(0.05f, ob.WaypointRefreshDistance);
            ExitArrivalDistance = Math.Max(0.1f, ob.ExitArrivalDistance);
            ActionIntervalMilliseconds = Math.Max(0L, ob.ActionIntervalMilliseconds);
            VehicleDriveSettings = new SiMountedVehicleDriveSettings(
                Math.Max(0, ob.VehicleStopDistance),
                Math.Max(0, ob.VehicleSlowDistance),
                Math.Max(0, ob.VehicleTurnDeadZone),
                MathHelper.Clamp(ob.VehicleMinimumForwardAlignment, -1, 1),
                Math.Max(0, ob.VehicleCruiseActionRepeatCount),
                Math.Max(0, ob.VehicleCatchUpActionRepeatCount));
        }
    }

    [MyComponent(typeof(MyObjectBuilder_SiUseTransportSeatsBehavior))]
    [MyDefinitionRequired(typeof(SiUseTransportSeatsBehaviorDefinition))]
    public class SiUseTransportSeatsBehaviorComponent : MyEntityComponent, ISiUtilityBehavior, ISiContinuousUtilityBehavior
    {
        private SiUseTransportSeatsBehaviorDefinition _definition;

        public string BehaviorName => DefinitionId.ToString();

        public override bool IsSerialized => false;

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            _definition = (SiUseTransportSeatsBehaviorDefinition)definition;
        }

        float ISiUtilityBehavior.Evaluate(SiUtilityContext context)
        {
            if (context?.Agent == null)
                return 0;

            var session = SiNpcSessionComponent.Instance;
            if (session == null)
                return 0;

            SiSquadTransportMode mode;
            return session.TryGetTransportMode(context.Agent, out mode) && mode != SiSquadTransportMode.None
                ? 1f
                : 0f;
        }

        void ISiUtilityBehavior.Begin(SiUtilityContext context)
        {
            ApplyTransportOrder(context);
        }

        void ISiUtilityBehavior.Tick(SiUtilityContext context, long elapsedMilliseconds)
        {
            ApplyTransportOrder(context);
        }

        void ISiUtilityBehavior.End(SiUtilityContext context)
        {
            StopMountedVehicle(context);

            if (context?.Agent == null)
                return;

            var session = SiNpcSessionComponent.Instance;
            SiSquadTransportMode mode;
            if (session != null
                && session.TryGetTransportMode(context.Agent, out mode)
                && mode != SiSquadTransportMode.None)
                return;

            if (context.HasWaypoint)
                context.TryClearWaypoint();
            context.TrySetCrouch(false);
        }

        private void ApplyTransportOrder(SiUtilityContext context)
        {
            if (context?.Agent == null)
                return;

            var session = SiNpcSessionComponent.Instance;
            if (session == null)
                return;

            SiSquadTransportMode mode;
            if (!session.TryGetTransportMode(context.Agent, out mode) || mode == SiSquadTransportMode.None)
                return;

            context.Agent.ClearCombatMovementRole();
            session.ReleaseCover(context.Agent.EntityId);
            context.TrySetCrouch(false);
            switch (mode)
            {
                case SiSquadTransportMode.Mount:
                    ApplyMountOrder(session, context);
                    break;
                case SiSquadTransportMode.Disembark:
                    ApplyDisembarkOrder(session, context);
                    break;
            }
        }

        private void ApplyMountOrder(SiNpcSessionComponent session, SiUtilityContext context)
        {
            var controller = Entity?.Components?.Get<EquiEntityControllerComponent>();
            if (controller == null)
                return;

            EquiPlayerAttachmentComponent.Slot assignedSeat;
            if (!session.TryGetAssignedTransportSeat(context.Agent, out assignedSeat))
            {
                if (context.HasWaypoint)
                    context.TryClearWaypoint();
                return;
            }

            if (controller.Controlled != null)
            {
                if (session.IsAssignedTransportSeat(context.Agent, controller.Controlled))
                {
                    if (context.HasWaypoint)
                        context.TryClearWaypoint();
                    DriveMountedVehicle(session, context, controller.Controlled);
                    return;
                }

                controller.ReleaseControl();
                return;
            }

            var seatEntity = assignedSeat.Controllable?.Entity;
            if (seatEntity == null || !seatEntity.InScene)
                return;

            var seatPosition = seatEntity.WorldMatrix.Translation;
            if (Vector3D.DistanceSquared(context.Position, seatPosition)
                <= _definition.InstantMountDistance * _definition.InstantMountDistance)
            {
                if (!session.TryConsumeTransportActionSlot(
                    context.Agent,
                    SiSquadTransportMode.Mount,
                    _definition.ActionIntervalMilliseconds))
                    return;

                session.RecordTransportExitPosition(context.Agent, context.Position);
                controller.RequestControl(assignedSeat);
                if (context.HasWaypoint)
                    context.TryClearWaypoint();
                return;
            }

            if (!context.HasWaypoint
                || Vector3D.DistanceSquared(context.Waypoint, seatPosition)
                   > _definition.WaypointRefreshDistance * _definition.WaypointRefreshDistance)
                context.TrySetWaypoint(seatPosition);
        }

        private void DriveMountedVehicle(
            SiNpcSessionComponent session,
            SiUtilityContext context,
            EquiPlayerAttachmentComponent.Slot seat)
        {
            if (!session.TryGetFormationTarget(context.Agent, out var formationTarget))
            {
                SiMountedVehicleDrivers.Stop(null, seat);
                return;
            }

            if (!session.TryGetAssignedTransportVehicle(context.Agent, out var vehicle))
            {
                SiMountedVehicleDrivers.Stop(null, seat);
                return;
            }

            SiMountedVehicleDrivers.TryDrive(
                vehicle,
                seat,
                formationTarget,
                _definition.VehicleDriveSettings);
        }

        private static void StopMountedVehicle(SiUtilityContext context)
        {
            var controller = context?.Entity?.Components?.Get<EquiEntityControllerComponent>();
            var seat = controller?.Controlled;
            if (seat == null)
                return;

            MyEntity vehicle = null;
            SiNpcSessionComponent.Instance?.TryGetAssignedTransportVehicle(context.Agent, out vehicle);
            SiMountedVehicleDrivers.Stop(vehicle, seat);
        }

        private void ApplyDisembarkOrder(SiNpcSessionComponent session, SiUtilityContext context)
        {
            var controller = Entity?.Components?.Get<EquiEntityControllerComponent>();
            Vector3D exitPosition;
            var hasExitPosition = session.TryGetTransportExitWorldPosition(context.Agent, out exitPosition);

            if (controller?.Controlled != null)
            {
                if (!session.TryConsumeTransportActionSlot(
                    context.Agent,
                    SiSquadTransportMode.Disembark,
                    _definition.ActionIntervalMilliseconds))
                    return;

                controller.ReleaseControl();
                if (hasExitPosition)
                    context.TrySetWaypoint(exitPosition);
                return;
            }

            if (!hasExitPosition)
            {
                if (context.HasWaypoint)
                    context.TryClearWaypoint();
                session.CompleteTransportOrder(context.Agent);
                return;
            }

            if (Vector3D.DistanceSquared(context.Position, exitPosition)
                <= _definition.ExitArrivalDistance * _definition.ExitArrivalDistance)
            {
                if (context.HasWaypoint)
                    context.TryClearWaypoint();
                session.CompleteTransportOrder(context.Agent);
                return;
            }

            if (!context.HasWaypoint
                || Vector3D.DistanceSquared(context.Waypoint, exitPosition)
                   > _definition.WaypointRefreshDistance * _definition.WaypointRefreshDistance)
                context.TrySetWaypoint(exitPosition);
        }
    }
}
