using System;
using Equinox76561198048419394.Core.Controller;
using Pax.Animals;
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
    /// PAX horses expose a replicated direct-control surface.  This lets the
    /// AI use a requested speed and signed steering rather than emulating a
    /// player's repeated RemoteRope key actions.
    /// </summary>
    internal sealed class SiPaxHorseMountedVehicleDriver : ISiMountedVehicleDriver
    {
        private const double MinimumDirectionLengthSquared = 0.0001;

        public bool CanDrive(MyEntity vehicle, EquiPlayerAttachmentComponent.Slot seat)
        {
            var horse = SeatEntity(seat);
            return horse != null && horse.Components.Contains<MyPAX_Horse>();
        }

        public void Drive(
            MyEntity vehicle,
            EquiPlayerAttachmentComponent.Slot seat,
            in Vector3D formationTarget,
            float leaderThrottle,
            in SiMountedVehicleDriveSettings settings)
        {
            var horse = SeatEntity(seat);
            var horseController = horse?.Components.Get<MyPAX_Horse>();
            var vehicleSettings = SiNpcSessionComponent.Instance?.VehicleSettings;
            var catchUpThrottle = vehicleSettings?.PaxHorseCatchUpThrottle ?? 0;
            var throttleHysteresisRadius = vehicleSettings?.PaxHorseThrottleHysteresisRadius ?? 0;
            if (horseController == null || vehicleSettings == null)
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

            float steering;
            float desiredThrottle;
            if (distance > MinimumDirectionLengthSquared
                && forwardAlignment < settings.MinimumForwardAlignment)
            {
                // Turn in place until the horse faces the target.  PAX keeps
                // this steering input active between AI ticks.
                steering = lateral >= settings.TurnDeadZone ? -1f : 1f;
                desiredThrottle = 0;
            }
            else
            {
                steering = Math.Abs(lateral) >= settings.TurnDeadZone
                    ? -MathHelper.Clamp((float)lateral, -1f, 1f)
                    : 0;
                desiredThrottle = Math.Max(0, leaderThrottle)
                                  + (distance > throttleHysteresisRadius ? catchUpThrottle : 0);
            }

            horseController.SetThrottleAndSteering(desiredThrottle, steering);
        }

        public void Stop(MyEntity vehicle, EquiPlayerAttachmentComponent.Slot seat)
        {
            var horseController = SeatEntity(seat)?.Components.Get<MyPAX_Horse>();
            horseController?.SetThrottleAndSteering(0, 0);
        }

        private static MyEntity SeatEntity(EquiPlayerAttachmentComponent.Slot seat)
        {
            return seat?.Controllable?.Entity;
        }

        private static Vector3D NormalizedOrFallback(in Vector3D value, in Vector3D fallback)
        {
            return value.LengthSquared() > MinimumDirectionLengthSquared
                ? Vector3D.Normalize(value)
                : fallback;
        }
    }
}
