using System;
using Equinox76561198048419394.Core.Controller;
using Sandbox.Game.Players;
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

            if (!SiTransportSeatService.TryGetTransportVehicleEntity(state.TransportVehicleEntityId, out var ignored))
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

        internal SiTransportSeatService.SeatMountResult TryMountAssignedTransportSeat(
            SiNpc npc,
            EquiEntityControllerComponent controller,
            EquiPlayerAttachmentComponent.Slot slot,
            double instantMountDistance,
            long warpFallbackDelayMilliseconds,
            double progressDistance,
            out Vector3D seatPosition,
            out Vector3D exitPosition)
        {
            seatPosition = Vector3D.Zero;
            exitPosition = Vector3D.Zero;
            if (npc?.Entity == null || controller == null || slot == null)
                return SiTransportSeatService.SeatMountResult.Failed;
            if (!_transportNpcStates.TryGetValue(npc.EntityId, out var state) || state == null)
                return SiTransportSeatService.SeatMountResult.Failed;

            return SiTransportSeatService.TryMountSeatOrApproach(
                npc.Entity,
                controller,
                slot,
                state.SeatApproach,
                instantMountDistance,
                warpFallbackDelayMilliseconds,
                progressDistance,
                out seatPosition,
                out exitPosition);
        }

        internal void ResetTransportSeatApproach(SiNpc npc)
        {
            if (npc == null)
                return;
            if (_transportNpcStates.TryGetValue(npc.EntityId, out var state))
                state?.SeatApproach.Reset();
        }

        internal void RecordTransportExitPosition(SiNpc npc, in Vector3D worldPosition)
        {
            if (npc == null)
                return;
            if (!_transportNpcStates.TryGetValue(npc.EntityId, out var state))
                return;

            SiTransportSeatService.TryRecordRelativeExitPoint(state.VehicleEntityId, state.ExitPoint, worldPosition);
        }

        internal bool TryGetTransportExitWorldPosition(SiNpc npc, out Vector3D worldPosition)
        {
            worldPosition = Vector3D.Zero;
            if (npc == null)
                return false;
            if (!_transportNpcStates.TryGetValue(npc.EntityId, out var state))
                return false;

            return SiTransportSeatService.TryResolveRelativeExitPoint(state.VehicleEntityId, state.ExitPoint, out worldPosition);
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
            if (!SiTransportSeatService.TryGetTransportVehicleEntity(vehicleEntityId, out var vehicle))
                return false;

            if (_transportNpcStates.TryGetValue(npc.EntityId, out var existing)
                && existing.VehicleEntityId == vehicleEntityId
                && TryResolveTransportSeat(existing, out var currentSeat)
                && (currentSeat.AttachedCharacter == null || currentSeat.AttachedCharacter == npc.Entity))
            {
                assignedState = existing;
                return true;
            }

            if (!SiTransportSeatService.TryFindNearestFreeSeat(
                npc.Entity,
                vehicle,
                (seatEntityId, seatName) => IsSeatAssignedToOtherNpc(npc.EntityId, seatEntityId, seatName),
                out var bestSeat))
                return false;

            if (existing == null)
            {
                existing = new SiTransportNpcState();
                _transportNpcStates[npc.EntityId] = existing;
            }

            existing.VehicleEntityId = vehicleEntityId;
            existing.SeatEntityId = bestSeat.Controllable.Entity.EntityId;
            existing.SeatSlotName = bestSeat.Definition.Name;
            existing.SeatApproach.Reset();
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

            return SiTransportSeatService.TryResolveSeat(state.SeatEntityId, state.SeatSlotName, out slot);
        }

        private bool IsSeatAssignedToOtherNpc(long npcEntityId, long seatEntityId, string seatName)
        {
            foreach (var entry in _transportNpcStates)
            {
                if (entry.Key == npcEntityId)
                    continue;

                var state = entry.Value;
                if (state == null)
                    continue;
                if (state.SeatEntityId != seatEntityId)
                    continue;
                if (string.Equals(state.SeatSlotName, seatName, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private void MountSquad(ulong sender, MyPlayer player, long leaderIdentityId)
        {
            string failure;
            MyEntity vehicle;
            if (!SiTransportSeatService.TryGetMountedVehicle(player, out vehicle, out failure))
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
