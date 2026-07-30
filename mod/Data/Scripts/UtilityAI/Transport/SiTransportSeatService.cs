using System;
using Equinox76561198048419394.Core.Controller;
using Sandbox.Game.Entities;
using Sandbox.Game.Players;
using SiCore.Core.Grid;
using VRage.Components.Entity.CubeGrid;
using VRage.Game.Entity;
using VRage.Session;
using VRageMath;

namespace Si.UtilityAI
{
    public static class SiTransportSeatService
    {
        public enum SeatMountResult : byte
        {
            Failed,
            Approach,
            Mounted,
        }

        public sealed class SeatApproachState
        {
            public bool HasProgressPosition;
            public Vector3D ProgressPosition;
            public long LastProgressTimeMilliseconds;

            public void Reset()
            {
                HasProgressPosition = false;
                ProgressPosition = Vector3D.Zero;
                LastProgressTimeMilliseconds = 0;
            }
        }

        public sealed class RelativeExitPointState
        {
            public bool HasLocalPosition;
            public Vector3D LocalPosition;

            public void Reset()
            {
                HasLocalPosition = false;
                LocalPosition = Vector3D.Zero;
            }
        }

        public static bool TryGetMountedVehicle(MyPlayer player, out MyEntity vehicle, out string failure)
        {
            vehicle = null;
            failure = null;

            var controlledEntity = player?.ControlledEntity as MyEntity;
            var controller = controlledEntity?.Components.Get<EquiEntityControllerComponent>();
            var seat = controller?.Controlled;
            if (seat == null)
            {
                failure = "You must be sitting in a vehicle seat to issue Mount up.";
                return false;
            }

            if (!TransportSeatQueries.TryGetSeatGrid(seat, out var seatBlockEntity, out var vehicleGrid))
            {
                failure = "Failed to resolve the current vehicle grid.";
                return false;
            }

            vehicle = vehicleGrid.Entity ?? seatBlockEntity;
            return vehicle != null;
        }

        public static bool TryFindNearestFreeSeat(
            MyEntity passenger,
            MyEntity vehicle,
            Func<long, string, bool> isSeatReserved,
            out EquiPlayerAttachmentComponent.Slot seat)
        {
            seat = null;
            if (passenger == null || vehicle == null || !vehicle.Components.TryGet(out MyGridDataComponent gridData))
                return false;

            var bestDistanceSquared = double.MaxValue;
            foreach (var candidate in TransportSeatQueries.EnumerateSlots(gridData))
            {
                if (candidate?.Controllable?.Entity == null)
                    continue;
                if (candidate.AttachedCharacter != null && candidate.AttachedCharacter != passenger)
                    continue;

                var seatEntityId = candidate.Controllable.Entity.EntityId;
                var seatName = candidate.Definition.Name;
                if (isSeatReserved != null && isSeatReserved(seatEntityId, seatName))
                    continue;

                var distanceSquared = Vector3D.DistanceSquared(
                    passenger.WorldMatrix.Translation,
                    candidate.Controllable.Entity.WorldMatrix.Translation);
                if (distanceSquared >= bestDistanceSquared)
                    continue;

                bestDistanceSquared = distanceSquared;
                seat = candidate;
            }

            return seat != null;
        }

        public static bool TryResolveSeat(
            long seatEntityId,
            string seatSlotName,
            out EquiPlayerAttachmentComponent.Slot slot)
        {
            slot = null;
            if (seatEntityId == 0 || string.IsNullOrWhiteSpace(seatSlotName))
                return false;

            var entity = MyEntities.GetEntityByIdOrDefault(seatEntityId);
            if (entity == null || entity.Closed || entity.MarkedForClose)
                return false;

            return (slot = entity.Components.Get<EquiPlayerAttachmentComponent>()?.GetSlotOrDefault(seatSlotName)) != null;
        }

        public static bool IsSameSeat(
            EquiPlayerAttachmentComponent.Slot slot,
            long seatEntityId,
            string seatSlotName)
        {
            return slot != null
                   && seatEntityId == (slot.Controllable?.Entity?.EntityId ?? 0)
                   && string.Equals(seatSlotName, slot.Definition.Name, StringComparison.Ordinal);
        }

        public static bool TryGetTransportVehicleEntity(long vehicleEntityId, out MyEntity vehicle)
        {
            vehicle = MyEntities.GetEntityByIdOrDefault(vehicleEntityId);
            return vehicle != null && !vehicle.Closed && !vehicle.MarkedForClose;
        }

        public static bool TryRecordRelativeExitPoint(
            long vehicleEntityId,
            RelativeExitPointState exitPoint,
            in Vector3D worldPosition)
        {
            if (exitPoint == null)
                return false;
            if (!TryGetTransportVehicleEntity(vehicleEntityId, out var vehicle) || vehicle?.PositionComp == null)
                return false;

            exitPoint.LocalPosition = Vector3D.Transform(worldPosition, vehicle.PositionComp.WorldMatrixInvScaled);
            exitPoint.HasLocalPosition = true;
            return true;
        }

        public static bool TryResolveRelativeExitPoint(
            long vehicleEntityId,
            RelativeExitPointState exitPoint,
            out Vector3D worldPosition)
        {
            worldPosition = Vector3D.Zero;
            if (exitPoint == null || !exitPoint.HasLocalPosition)
                return false;
            if (!TryGetTransportVehicleEntity(vehicleEntityId, out var vehicle) || vehicle?.PositionComp == null)
                return false;

            worldPosition = Vector3D.Transform(exitPoint.LocalPosition, vehicle.PositionComp.WorldMatrix);
            return true;
        }

        public static SeatMountResult TryMountSeatOrApproach(
            MyEntity passenger,
            EquiEntityControllerComponent controller,
            EquiPlayerAttachmentComponent.Slot seat,
            SeatApproachState approachState,
            double instantMountDistance,
            long warpFallbackDelayMilliseconds,
            double progressDistance,
            out Vector3D seatPosition,
            out Vector3D exitPosition)
        {
            seatPosition = Vector3D.Zero;
            exitPosition = Vector3D.Zero;

            if (passenger == null || controller == null || seat == null)
                return SeatMountResult.Failed;

            var seatEntity = seat.Controllable?.Entity;
            if (seatEntity == null || !seatEntity.InScene)
                return SeatMountResult.Failed;

            seatPosition = seatEntity.WorldMatrix.Translation;
            exitPosition = passenger.WorldMatrix.Translation;

            var distanceSquared = Vector3D.DistanceSquared(exitPosition, seatPosition);
            var shouldWarp = false;
            if (distanceSquared > instantMountDistance * instantMountDistance)
                shouldWarp = ShouldWarpToSeat(exitPosition, approachState, warpFallbackDelayMilliseconds, progressDistance);

            if (shouldWarp)
                WarpPassengerToSeat(passenger, seatEntity);
            else if (distanceSquared > instantMountDistance * instantMountDistance)
                return SeatMountResult.Approach;

            controller.RequestControl(seat);
            if (controller.Controlled == null || !IsSameSeat(controller.Controlled, seat.Controllable.Entity.EntityId, seat.Definition.Name))
                return SeatMountResult.Failed;

            approachState?.Reset();
            return SeatMountResult.Mounted;
        }

        private static bool ShouldWarpToSeat(
            in Vector3D passengerPosition,
            SeatApproachState approachState,
            long warpFallbackDelayMilliseconds,
            double progressDistance)
        {
            if (approachState == null)
                return false;

            var now = (long)(MySession.Static?.ElapsedGameTime.TotalMilliseconds ?? 0);
            if (!approachState.HasProgressPosition)
            {
                approachState.HasProgressPosition = true;
                approachState.ProgressPosition = passengerPosition;
                approachState.LastProgressTimeMilliseconds = now;
                return false;
            }

            if (Vector3D.DistanceSquared(approachState.ProgressPosition, passengerPosition) >= progressDistance * progressDistance)
            {
                approachState.ProgressPosition = passengerPosition;
                approachState.LastProgressTimeMilliseconds = now;
                return false;
            }

            return now - approachState.LastProgressTimeMilliseconds >= warpFallbackDelayMilliseconds;
        }

        private static void WarpPassengerToSeat(MyEntity passenger, MyEntity seatEntity)
        {
            if (passenger?.PositionComp == null || seatEntity == null)
                return;

            var seatWorld = seatEntity.WorldMatrix;
            passenger.PositionComp.WorldMatrix = MatrixD.CreateWorld(seatWorld.Translation, seatWorld.Forward, seatWorld.Up);
        }
    }
}
