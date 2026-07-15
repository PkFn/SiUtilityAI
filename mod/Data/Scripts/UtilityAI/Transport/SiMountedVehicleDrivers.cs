using System;
using System.Collections.Generic;
using Equinox76561198048419394.Core.Controller;
using Pax.Animals;
using Pax.RemoteRope;
using SiCore.Core.Debug;
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
            in SiMountedVehicleDriveSettings settings);

        void Stop(MyEntity vehicle, EquiPlayerAttachmentComponent.Slot seat);
    }

    internal readonly struct SiMountedVehicleDriveSettings
    {
        public SiMountedVehicleDriveSettings(
            float stopDistance,
            float slowDistance,
            float turnDeadZone,
            float minimumForwardAlignment,
            int cruiseActionRepeatCount,
            int catchUpActionRepeatCount)
        {
            StopDistance = stopDistance;
            SlowDistance = slowDistance;
            TurnDeadZone = turnDeadZone;
            MinimumForwardAlignment = minimumForwardAlignment;
            CruiseActionRepeatCount = cruiseActionRepeatCount;
            CatchUpActionRepeatCount = catchUpActionRepeatCount;
        }

        public float StopDistance { get; }
        public float SlowDistance { get; }
        public float TurnDeadZone { get; }
        public float MinimumForwardAlignment { get; }
        public int CruiseActionRepeatCount { get; }
        public int CatchUpActionRepeatCount { get; }
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
            in SiMountedVehicleDriveSettings settings)
        {
            for (var i = 0; i < Drivers.Length; i++)
            {
                var driver = Drivers[i];
                if (driver == null || !driver.CanDrive(vehicle, seat))
                    continue;

                driver.Drive(vehicle, seat, formationTarget, settings);
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
        private const short TurnLeftAction = 2;
        private const short TurnRightAction = 3;
        private const short StopAction = 4;
        private const double MinimumDirectionLengthSquared = 0.0001;
        private const long DriveProbeCooldownMilliseconds = 1000;
        private readonly SiGameLog _log = new SiGameLog(nameof(SiPaxHorseMountedVehicleDriver), "[SiHorseDrive]");
        private readonly Dictionary<long, long> _driveProbeTimes = new Dictionary<long, long>();

        public bool CanDrive(MyEntity vehicle, EquiPlayerAttachmentComponent.Slot seat)
        {
            var horse = SeatEntity(seat);
            return horse != null
                   && horse.Components.Contains<MyPAX_Horse>()
                   && horse.Components.Contains<MyRemoteRopeControlComponent>();
        }

        public void Drive(
            MyEntity vehicle,
            EquiPlayerAttachmentComponent.Slot seat,
            in Vector3D formationTarget,
            in SiMountedVehicleDriveSettings settings)
        {
            var horse = SeatEntity(seat);
            var controls = horse?.Components.Get<MyRemoteRopeControlComponent>();
            if (controls == null)
                return;

            var world = horse.WorldMatrix;
            var position = world.Translation;
            var gravity = MyGravityProviderSystem.CalculateTotalGravityInPoint(position);
            var up = gravity.LengthSquared() > MinimumDirectionLengthSquared
                ? -Vector3D.Normalize(gravity)
                : NormalizedOrFallback(world.Up, Vector3D.Up);
            var toTarget = Vector3D.Reject(formationTarget - position, up);
            var distance = toTarget.Length();
            if (distance <= settings.StopDistance)
            {
                LogDriveProbe(horse, vehicle, formationTarget, distance, 0, 0, "stop-within-formation-distance");
                Stop(controls);
                return;
            }

            var direction = toTarget / distance;
            // PAX applies its positive forward command along the inverse of
            // the horse block's WorldMatrix.Forward.  Use that travel axis
            // for navigation, otherwise a target directly ahead makes the
            // driver accelerate away from it.
            var forward = NormalizedOrFallback(Vector3D.Reject(world.Backward, up), Vector3D.Backward);
            var right = NormalizedOrFallback(Vector3D.Cross(forward, up), world.Right);
            var lateral = Vector3D.Dot(direction, right);
            var forwardAlignment = Vector3D.Dot(direction, forward);
            LogDriveProbe(horse, vehicle, formationTarget, distance, forwardAlignment, lateral, forwardAlignment < settings.MinimumForwardAlignment ? "turn-in-place" : "drive-toward-formation-target");

            if (Math.Abs(lateral) >= settings.TurnDeadZone)
                controls.LocalAction(lateral > 0 ? TurnRightAction : TurnLeftAction, 0, false, true);

            if (forwardAlignment < settings.MinimumForwardAlignment)
            {
                Stop(controls);
                return;
            }

            var repeatCount = distance >= settings.SlowDistance
                ? settings.CatchUpActionRepeatCount
                : settings.CruiseActionRepeatCount;
            for (var i = 0; i < repeatCount; i++)
                controls.LocalAction(ForwardAction, 0, false, true);
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

        private void LogDriveProbe(
            MyEntity horse,
            MyEntity vehicle,
            in Vector3D formationTarget,
            double distance,
            double forwardAlignment,
            double lateral,
            string branch)
        {
            if (!SiGameLog.Enabled || horse == null)
                return;

            var now = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
            if (_driveProbeTimes.TryGetValue(horse.EntityId, out var lastTime)
                && now - lastTime < DriveProbeCooldownMilliseconds)
                return;

            _driveProbeTimes[horse.EntityId] = now;
            _log.Warning($"horseId={horse.EntityId} horseName={horse.Name ?? "null"} horseDefinition={horse.Definition?.Id.SubtypeName ?? "null"} vehicleId={vehicle?.EntityId ?? 0} horsePosition={horse.WorldMatrix.Translation} horseForward={horse.WorldMatrix.Forward} formationTarget={formationTarget} distance={distance:0.##} forwardAlignment={forwardAlignment:0.###} lateral={lateral:0.###} branch={branch}"); // AGENT-DEBUG-LOG
        }

        private static Vector3D NormalizedOrFallback(in Vector3D value, in Vector3D fallback)
        {
            return value.LengthSquared() > MinimumDirectionLengthSquared
                ? Vector3D.Normalize(value)
                : fallback;
        }
    }
}
