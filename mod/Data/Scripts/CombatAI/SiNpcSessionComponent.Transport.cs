using System;
using System.Collections.Generic;
using Equinox76561198048419394.Core.Controller;
using Sandbox.Game.Entities;
using Sandbox.Game.Players;
using SiCore.Core.Grid;
using VRage.Components.Entity.CubeGrid;
using VRage.Game.Entity;
using VRage.Game.Entity.EntityComponents;
using VRageMath;

namespace Si.UtilityAI
{
    public sealed partial class SiNpcSessionComponent
    {
        internal bool TryGetTransportMode(SiNpc npc, out SiSquadTransportMode mode)
        {
            mode = SiSquadTransportMode.None;
            if (npc == null || Squads == null)
                return false;

            SiAssignedNpc assignment;
            if (!Squads.TryGetAssignment(npc.EntityId, out assignment)
                || assignment.Leader.Kind != SiSquadLeaderKind.Player)
                return false;

            SiSquadCommandState state;
            if (!_squadOrders.TryGetValue(assignment.Leader.Id, out state)
                || state.TransportMode == SiSquadTransportMode.None
                || state.TransportVehicleEntityId == 0)
                return false;

            if (MyEntities.GetEntityByIdOrDefault(state.TransportVehicleEntityId) == null)
            {
                state.TransportMode = SiSquadTransportMode.None;
                state.TransportVehicleEntityId = 0;
                return false;
            }

            mode = state.TransportMode;
            return true;
        }

        internal bool TryConsumeTransportActionSlot(
            SiNpc npc,
            SiSquadTransportMode mode,
            long intervalMilliseconds)
        {
            if (npc == null || mode == SiSquadTransportMode.None || Squads == null)
                return false;

            SiAssignedNpc assignment;
            if (!Squads.TryGetAssignment(npc.EntityId, out assignment)
                || assignment.Leader.Kind != SiSquadLeaderKind.Player)
                return false;

            if (!_squadOrders.TryGetValue(assignment.Leader.Id, out var state)
                || state == null
                || state.TransportMode != mode
                || state.TransportVehicleEntityId == 0)
                return false;

            if (state.TransportCadenceMode != mode)
            {
                state.TransportCadenceMode = mode;
                state.NextTransportActionTimeMilliseconds = 0;
            }

            var now = CurrentTimeMilliseconds();
            if (state.NextTransportActionTimeMilliseconds > now)
                return false;

            state.NextTransportActionTimeMilliseconds = now + Math.Max(0L, intervalMilliseconds);
            return true;
        }

        internal bool TryGetAssignedTransportSeat(
            SiNpc npc,
            out EquiPlayerAttachmentComponent.Slot slot)
        {
            slot = null;
            if (npc == null || npc.Entity == null || npc.Entity.Closed || npc.Entity.MarkedForClose || Squads == null)
                return false;

            SiAssignedNpc assignment;
            if (!Squads.TryGetAssignment(npc.EntityId, out assignment)
                || assignment.Leader.Kind != SiSquadLeaderKind.Player)
                return false;

            SiSquadCommandState order;
            if (!_squadOrders.TryGetValue(assignment.Leader.Id, out order)
                || order.TransportMode == SiSquadTransportMode.None
                || order.TransportVehicleEntityId == 0)
                return false;

            if (!TryAssignTransportSeat(npc, order.TransportVehicleEntityId, out var state))
                return false;

            return TryResolveTransportSeat(state, out slot);
        }

        internal bool IsAssignedTransportSeat(SiNpc npc, EquiPlayerAttachmentComponent.Slot slot)
        {
            if (npc == null || slot == null)
                return false;
            if (!_transportNpcStates.TryGetValue(npc.EntityId, out var state))
                return false;

            return state.SeatEntityId == (slot.Controllable?.Entity?.EntityId ?? 0)
                   && string.Equals(state.SeatSlotName, slot.Definition.Name, StringComparison.Ordinal);
        }

        internal void RecordTransportExitPosition(SiNpc npc, in Vector3D worldPosition)
        {
            if (npc == null)
                return;
            if (!_transportNpcStates.TryGetValue(npc.EntityId, out var state))
                return;
            if (!TryGetTransportVehicleEntity(state.VehicleEntityId, out var vehicle))
                return;

            state.ExitLocalPosition = Vector3D.Transform(worldPosition, vehicle.PositionComp.WorldMatrixInvScaled);
            state.HasExitLocalPosition = true;
        }

        internal bool TryGetTransportExitWorldPosition(SiNpc npc, out Vector3D worldPosition)
        {
            worldPosition = Vector3D.Zero;
            if (npc == null)
                return false;
            if (!_transportNpcStates.TryGetValue(npc.EntityId, out var state) || !state.HasExitLocalPosition)
                return false;
            if (!TryGetTransportVehicleEntity(state.VehicleEntityId, out var vehicle))
                return false;

            worldPosition = Vector3D.Transform(state.ExitLocalPosition, vehicle.PositionComp.WorldMatrix);
            return true;
        }

        internal void CompleteTransportOrder(SiNpc npc)
        {
            if (npc == null || Squads == null)
                return;

            SiAssignedNpc assignment;
            if (!Squads.TryGetAssignment(npc.EntityId, out assignment)
                || assignment.Leader.Kind != SiSquadLeaderKind.Player)
            {
                _transportNpcStates.Remove(npc.EntityId);
                return;
            }

            _transportNpcStates.Remove(npc.EntityId);
            if (!HasActiveTransportStateForLeader(assignment.Leader.Id)
                && _squadOrders.TryGetValue(assignment.Leader.Id, out var state)
                && state.TransportMode != SiSquadTransportMode.None)
            {
                state.TransportMode = SiSquadTransportMode.None;
                state.TransportVehicleEntityId = 0;
                ResetTransportCadence(state);
            }
        }

        private void CancelTransportOverride(long leaderIdentityId, SiSquadCommandState state)
        {
            if (state == null)
                return;

            ReleaseLeaderTransportSeats(leaderIdentityId);
            state.TransportMode = SiSquadTransportMode.None;
            state.TransportVehicleEntityId = 0;
            ResetTransportCadence(state);
            RemoveTransportStatesForLeader(leaderIdentityId);
        }

        private bool TryGetMountedVehicle(MyPlayer player, out MyEntity vehicle, out string failure)
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
            return true;
        }

        private bool HasActiveTransportStateForLeader(long leaderIdentityId)
        {
            if (leaderIdentityId == 0 || Squads == null || Npcs == null)
                return false;

            foreach (var npc in Squads.GetLeaderNpcs(Npcs, leaderIdentityId))
                if (npc != null && _transportNpcStates.ContainsKey(npc.EntityId))
                    return true;
            return false;
        }

        private static void ResetTransportCadence(SiSquadCommandState state)
        {
            if (state == null)
                return;

            state.TransportCadenceMode = SiSquadTransportMode.None;
            state.NextTransportActionTimeMilliseconds = 0;
        }

        private void RemoveTransportStatesForLeader(long leaderIdentityId)
        {
            if (leaderIdentityId == 0 || Squads == null || Npcs == null)
                return;

            foreach (var npc in Squads.GetLeaderNpcs(Npcs, leaderIdentityId))
                if (npc != null)
                    _transportNpcStates.Remove(npc.EntityId);
        }

        private void ReleaseLeaderTransportSeats(long leaderIdentityId)
        {
            if (leaderIdentityId == 0 || Squads == null || Npcs == null)
                return;

            foreach (var npc in Squads.GetLeaderNpcs(Npcs, leaderIdentityId))
            {
                var controller = npc?.Entity?.Components.Get<EquiEntityControllerComponent>();
                if (controller?.Controlled != null)
                    controller.ReleaseControl();
            }
        }

        private void TrimTransportStatesForLeader(long leaderIdentityId, long vehicleEntityId)
        {
            if (leaderIdentityId == 0 || vehicleEntityId == 0 || Squads == null || Npcs == null)
                return;

            foreach (var npc in Squads.GetLeaderNpcs(Npcs, leaderIdentityId))
            {
                if (npc == null)
                    continue;
                if (_transportNpcStates.TryGetValue(npc.EntityId, out var state)
                    && state.VehicleEntityId != vehicleEntityId)
                    _transportNpcStates.Remove(npc.EntityId);
            }
        }

        private bool TryAssignTransportSeat(
            SiNpc npc,
            long vehicleEntityId,
            out SiTransportNpcState assignedState)
        {
            assignedState = null;
            if (npc?.Entity == null || vehicleEntityId == 0)
                return false;
            if (!TryGetTransportVehicleEntity(vehicleEntityId, out var vehicle))
                return false;

            if (_transportNpcStates.TryGetValue(npc.EntityId, out var existing)
                && existing.VehicleEntityId == vehicleEntityId
                && TryResolveTransportSeat(existing, out var currentSeat)
                && (currentSeat.AttachedCharacter == null || currentSeat.AttachedCharacter == npc.Entity))
            {
                assignedState = existing;
                return true;
            }

            EquiPlayerAttachmentComponent.Slot bestSeat = null;
            var bestDistanceSquared = double.MaxValue;
            foreach (var seat in EnumerateVehicleSeats(vehicle))
            {
                if (seat?.Controllable?.Entity == null)
                    continue;
                if (seat.AttachedCharacter != null && seat.AttachedCharacter != npc.Entity)
                    continue;
                if (IsSeatAssignedToOtherNpc(npc.EntityId, seat))
                    continue;

                var distanceSquared = Vector3D.DistanceSquared(
                    npc.Entity.WorldMatrix.Translation,
                    seat.Controllable.Entity.WorldMatrix.Translation);
                if (distanceSquared >= bestDistanceSquared)
                    continue;

                bestDistanceSquared = distanceSquared;
                bestSeat = seat;
            }

            if (bestSeat == null)
                return false;

            if (existing == null)
            {
                existing = new SiTransportNpcState();
                _transportNpcStates[npc.EntityId] = existing;
            }

            existing.VehicleEntityId = vehicleEntityId;
            existing.SeatEntityId = bestSeat.Controllable.Entity.EntityId;
            existing.SeatSlotName = bestSeat.Definition.Name;
            assignedState = existing;
            return true;
        }

        private bool TryResolveTransportSeat(
            SiTransportNpcState state,
            out EquiPlayerAttachmentComponent.Slot slot)
        {
            slot = null;
            if (state == null || state.SeatEntityId == 0 || string.IsNullOrWhiteSpace(state.SeatSlotName))
                return false;

            var entity = MyEntities.GetEntityByIdOrDefault(state.SeatEntityId);
            if (entity == null || entity.Closed || entity.MarkedForClose)
                return false;

            return (slot = entity.Components.Get<EquiPlayerAttachmentComponent>()?.GetSlotOrDefault(state.SeatSlotName)) != null;
        }

        private bool TryGetTransportVehicleEntity(long vehicleEntityId, out MyEntity vehicle)
        {
            vehicle = MyEntities.GetEntityByIdOrDefault(vehicleEntityId);
            return vehicle != null && !vehicle.Closed && !vehicle.MarkedForClose;
        }

        private bool IsSeatAssignedToOtherNpc(long npcEntityId, EquiPlayerAttachmentComponent.Slot seat)
        {
            foreach (var entry in _transportNpcStates)
            {
                if (entry.Key == npcEntityId)
                    continue;

                var state = entry.Value;
                if (state == null)
                    continue;
                if (state.SeatEntityId != (seat?.Controllable?.Entity?.EntityId ?? 0))
                    continue;
                if (string.Equals(state.SeatSlotName, seat.Definition.Name, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private IEnumerable<EquiPlayerAttachmentComponent.Slot> EnumerateVehicleSeats(MyEntity vehicle)
        {
            if (vehicle == null || !vehicle.Components.TryGet(out MyGridDataComponent gridData))
                yield break;

            foreach (var slot in TransportSeatQueries.EnumerateSlots(gridData))
                yield return slot;
        }

        private void MountSquad(ulong sender, MyPlayer player, long leaderIdentityId)
        {
            string failure;
            MyEntity vehicle;
            if (!TryGetMountedVehicle(player, out vehicle, out failure))
            {
                Respond(sender, failure ?? "You must sit in a vehicle seat to issue Mount up.");
                return;
            }

            var troops = Squads?.GetLeaderNpcs(Npcs, leaderIdentityId);
            if (troops == null || troops.Count == 0)
            {
                Respond(sender, "Your squad has no utility AI troops.");
                return;
            }

            var state = GetSquadOrder(leaderIdentityId);
            SetRearmOverride(state, false);
            state.TransportMode = SiSquadTransportMode.Mount;
            state.TransportVehicleEntityId = vehicle.EntityId;
            ResetTransportCadence(state);

            ClearLeaderWaypoints(leaderIdentityId);
            TrimTransportStatesForLeader(leaderIdentityId, vehicle.EntityId);

            var assigned = 0;
            for (var i = 0; i < troops.Count; i++)
                if (TryAssignTransportSeat(troops[i], vehicle.EntityId, out var ignored))
                    assigned++;

            if (assigned == 0)
            {
                state.TransportMode = SiSquadTransportMode.None;
                state.TransportVehicleEntityId = 0;
                ResetTransportCadence(state);
                Respond(sender, "No free transport seats were found on the current vehicle.");
                return;
            }
        }

        private void DisembarkSquad(ulong sender, long leaderIdentityId)
        {
            var state = GetSquadOrder(leaderIdentityId);
            if (!HasActiveTransportStateForLeader(leaderIdentityId))
            {
                state.TransportMode = SiSquadTransportMode.None;
                state.TransportVehicleEntityId = 0;
                ResetTransportCadence(state);
                Respond(sender, "No squad members are currently assigned to transport seats.");
                return;
            }

            SetRearmOverride(state, false);
            state.TransportMode = SiSquadTransportMode.Disembark;
            ResetTransportCadence(state);
        }
    }
}
