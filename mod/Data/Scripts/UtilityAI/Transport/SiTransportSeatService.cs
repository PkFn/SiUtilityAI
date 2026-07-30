using System;
using Equinox76561198048419394.Core.Controller;
using Sandbox.Game.Entities;
using Sandbox.Game.Players;
using SiCore.Core.Grid;
using VRage.Components.Entity.CubeGrid;
using VRage.Game.Entity;
using VRageMath;

namespace Si.UtilityAI
{
    public static class SiTransportSeatService
    {
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
    }
}
