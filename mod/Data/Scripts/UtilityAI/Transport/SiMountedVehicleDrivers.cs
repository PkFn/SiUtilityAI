using System;
using Equinox76561198048419394.Core.Controller;
using Pax.Animals;
using Pax.RemoteRope;
using VRage.Entities.Gravity;
using VRage.Game.Entity;
using VRageMath;

namespace Si.UtilityAI
{
    /// <summary>
    /// A vehicle-specific adapter for a seated NPC.  New vehicle integrations
    /// register another implementation here without changing formation or
    /// transport-seat behavior.
    /// </summary>
    internal interface ISiMountedVehicleDriver
    {
        bool CanDrive(MyEntity vehicle, EquiPlayerAttachmentComponent.Slot seat);

        void Drive(
            MyEntity vehicle,
            EquiPlayerAttachmentComponent.Slot seat,
            in Vector3D formationTarget,
            float leaderThrottle,
            in SiMountedVehicleDriveSettings settings);

        void Stop(MyEntity vehicle, EquiPlayerAttachmentComponent.Slot seat);
    }

    internal readonly struct SiMountedVehicleDriveSettings
    {
        public SiMountedVehicleDriveSettings(
            float turnDeadZone,
            float minimumForwardAlignment)
        {
            TurnDeadZone = turnDeadZone;
            MinimumForwardAlignment = minimumForwardAlignment;
        }

        public float TurnDeadZone { get; }
        public float MinimumForwardAlignment { get; }
    }

    internal static class SiMountedVehicleDrivers
    {
        private static readonly ISiMountedVehicleDriver[] Drivers =
        {
            new SiPaxHorseMountedVehicleDriver(),
        };

        public static bool CanDrive(MyEntity vehicle, EquiPlayerAttachmentComponent.Slot seat)
        {
            for (var i = 0; i < Drivers.Length; i++)
            {
                var driver = Drivers[i];
                if (driver != null && driver.CanDrive(vehicle, seat))
                    return true;
            }

            return false;
        }

        public static bool TryDrive(
            MyEntity vehicle,
            EquiPlayerAttachmentComponent.Slot seat,
            in Vector3D formationTarget,
            float leaderThrottle,
            in SiMountedVehicleDriveSettings settings)
        {
            for (var i = 0; i < Drivers.Length; i++)
            {
                var driver = Drivers[i];
                if (driver == null || !driver.CanDrive(vehicle, seat))
                    continue;

                driver.Drive(vehicle, seat, formationTarget, leaderThrottle, settings);
                return true;
            }

            return false;
        }

        public static void Stop(MyEntity vehicle, EquiPlayerAttachmentComponent.Slot seat)
        {
            for (var i = 0; i < Drivers.Length; i++)
            {
                var driver = Drivers[i];
                if (driver != null && driver.CanDrive(vehicle, seat))
                    driver.Stop(vehicle, seat);
            }
        }
    }

    /// <summary>
    /// PAX horses receive their movement through the public RemoteRope action
    /// surface.  Actions are sent directly on the authoritative server, while
    /// PAX remains responsible for simulating and replicating the animal.
    /// </summary>
    internal sealed class SiPaxHorseMountedVehicleDriver : ISiMountedVehicleDriver
    {
        private const short ForwardAction = 0;
        private const short BackwardAction = 1;
        private const short TurnLeftAction = 2;
        private const short TurnRightAction = 3;
        private const short StopAction = 4;
        private const double MinimumDirectionLengthSquared = 0.0001;

        public bool CanDrive(MyEntity vehicle, EquiPlayerAttachmentComponent.Slot seat)
        {
            var horse = SeatEntity(seat);
            return SiNpcSessionComponent.Instance?.VehicleSettings?.PaxHorseSteeringMultiplier > 0
                   && horse != null
                   && horse.Components.Contains<MyPAX_Horse>()
                   && horse.Components.Contains<MyRemoteRopeControlComponent>();
        }

        public void Drive(
            MyEntity vehicle,
            EquiPlayerAttachmentComponent.Slot seat,
            in Vector3D formationTarget,
            float leaderThrottle,
            in SiMountedVehicleDriveSettings settings)
        {
            var horse = SeatEntity(seat);
            var controls = horse?.Components.Get<MyRemoteRopeControlComponent>();
            var vehicleSettings = SiNpcSessionComponent.Instance?.VehicleSettings;
            var steeringMultiplier = vehicleSettings?.PaxHorseSteeringMultiplier ?? 0;
            var distanceThrottleCoefficient = vehicleSettings?.PaxHorseDistanceThrottleCoefficient ?? 0;
            if (controls == null || steeringMultiplier <= 0 || distanceThrottleCoefficient <= 0)
                return;

            var world = horse.WorldMatrix;
            var position = world.Translation;
            var gravity = MyGravityProviderSystem.CalculateTotalGravityInPoint(position);
            var up = gravity.LengthSquared() > MinimumDirectionLengthSquared
                ? -Vector3D.Normalize(gravity)
                : NormalizedOrFallback(world.Up, Vector3D.Up);
            var toTarget = Vector3D.Reject(formationTarget - position, up);
            var distance = toTarget.Length();
            var direction = distance > MinimumDirectionLengthSquared
                ? toTarget / distance
                : Vector3D.Zero;
            // PAX applies its positive forward command along the inverse of
            // the horse block's WorldMatrix.Forward.  Use that travel axis
            // for navigation, otherwise a target directly ahead makes the
            // driver accelerate away from it.
            var forward = NormalizedOrFallback(Vector3D.Reject(world.Backward, up), Vector3D.Backward);
            var right = NormalizedOrFallback(Vector3D.Cross(forward, up), world.Left);
            var lateral = Vector3D.Dot(direction, right);
            var forwardAlignment = Vector3D.Dot(direction, forward);

            if (distance > MinimumDirectionLengthSquared
                && forwardAlignment < settings.MinimumForwardAlignment)
            {
                Stop(controls);
                SendTurnAction(
                    controls,
                    lateral >= settings.TurnDeadZone ? TurnRightAction : TurnLeftAction,
                    steeringMultiplier);
                return;
            }

            if (Math.Abs(lateral) >= settings.TurnDeadZone)
                SendTurnAction(
                    controls,
                    lateral > 0 ? TurnRightAction : TurnLeftAction,
                    steeringMultiplier);

            var desiredThrottle = Math.Max(0, leaderThrottle) + (float)distance * distanceThrottleCoefficient;
            var currentThrottle = (float)Vector3D.Dot(vehicle?.Physics?.LinearVelocity ?? Vector3D.Zero, forward);
            if (currentThrottle < desiredThrottle)
                controls.LocalAction(ForwardAction, 0, false, true);
            else if (currentThrottle > desiredThrottle)
                controls.LocalAction(BackwardAction, 0, false, true);
        }

        public void Stop(MyEntity vehicle, EquiPlayerAttachmentComponent.Slot seat)
        {
            var controls = SeatEntity(seat)?.Components.Get<MyRemoteRopeControlComponent>();
            if (controls != null)
                Stop(controls);
        }

        private static MyEntity SeatEntity(EquiPlayerAttachmentComponent.Slot seat)
        {
            return seat?.Controllable?.Entity;
        }

        private static void Stop(MyRemoteRopeControlComponent controls)
        {
            controls.LocalAction(StopAction, 0, false, true);
        }

        private static void SendTurnAction(
            MyRemoteRopeControlComponent controls,
            short action,
            float steeringMultiplier)
        {
            var repeatCount = Math.Max(1, (int)Math.Ceiling(steeringMultiplier));
            for (var i = 0; i < repeatCount; i++)
                controls.LocalAction(action, 0, false, true);
        }

        private static Vector3D NormalizedOrFallback(in Vector3D value, in Vector3D fallback)
        {
            return value.LengthSquared() > MinimumDirectionLengthSquared
                ? Vector3D.Normalize(value)
                : fallback;
        }
    }
}
