using System;
using System.Collections.Generic;
using Equinox76561198048419394.Core.Controller;
using Pax.Animals;
using Sandbox.Game.Entities;
using Sandbox.Game.Players;
using SiCore.Core.Grid;
using VRage.Components.Entity.CubeGrid;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.Entity.EntityComponents;
using VRage.ObjectBuilders;
using VRage.Scene;
using VRage.Session;
using VRage.Utils;
using VRageMath;
using VRage;

namespace Si.UtilityAI
{
    public sealed partial class SiNpcSessionComponent
    {
        private const string AdminHorseBlockSubtype = "PAX_Horse_Default";

        private bool TryPrepareMountedNpc(SiNpc npc, out string failure)
        {
            failure = null;
            if (npc?.Entity == null || Squads == null)
            {
                failure = "The mounted NPC could not be assigned to a squad.";
                return false;
            }

            if (!TrySpawnAdminHorse(npc.Entity.WorldMatrix, out var horse, out failure))
                return false;

            if (!TryAssignTransportSeat(npc, new[] { horse }, out var assignedState))
            {
                horse.Close();
                failure = "The spawned horse did not expose a compatible rider seat.";
                return false;
            }

            assignedState.OwnedByNpc = true;
            if (Squads.TryGetAssignment(npc.EntityId, out var assignment)
                && assignment.Leader.Kind == SiSquadLeaderKind.Player)
            {
                var order = GetSquadOrder(assignment.Leader.Id);
                order.Mode = SiSquadOrderMode.Follow;
                order.TransportMode = SiSquadTransportMode.Mount;
                order.TransportVehicleEntityId = horse.EntityId;
                ResetTransportCadence(order);
            }

            var controller = npc.Entity.Components.Get<EquiEntityControllerComponent>();
            var seat = ResolveAssignedSeat(assignedState);
            if (controller != null && seat != null)
                controller.RequestControl(seat);

            return true;
        }

        private bool TrySpawnAdminHorse(
            in MatrixD transform,
            out MyEntity horse,
            out string failure)
        {
            horse = null;
            failure = null;
            try
            {
                var gridBuilder = new MyObjectBuilder_CubeGrid
                {
                    EntityId = MyEntityIdentifier.AllocateId(),
                    GridSizeEnum = MyCubeSize.Small,
                    PersistentFlags = MyPersistentEntityFlags2.InScene,
                    PositionAndOrientation = new MyPositionAndOrientation(transform),
                    IsStatic = false,
                    LinearVelocity = Vector3.Zero,
                    AngularVelocity = Vector3.Zero,
                    CreatePhysics = true,
                    XMirroxPlane = null,
                    YMirroxPlane = null,
                    ZMirroxPlane = null,
                };
                gridBuilder.CubeBlocks.Add(new MyObjectBuilder_CubeBlock
                {
                    SubtypeName = AdminHorseBlockSubtype,
                    BuildPercent = 100f,
                    IntegrityPercent = 100f,
                    Min = Vector3I.Zero,
                    BlockOrientation = new MyBlockOrientation(
                        Base6Directions.Direction.Forward,
                        Base6Directions.Direction.Up),
                });

                horse = MyEntities.CreateFromObjectBuilderAndAdd(gridBuilder);
                if (horse == null)
                {
                    failure = "The PAX horse block could not be initialized into a dynamic grid.";
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                horse?.Close();
                horse = null;
                failure = $"Failed to create a PAX horse: {exception.Message}";
                return false;
            }
        }

        private static EquiPlayerAttachmentComponent.Slot ResolveAssignedSeat(SiTransportNpcState state)
        {
            if (state == null || state.SeatEntityId == 0)
                return null;

            var entity = MyEntities.GetEntityByIdOrDefault(state.SeatEntityId);
            return entity?.Components.Get<EquiPlayerAttachmentComponent>()?.GetSlotOrDefault(state.SeatSlotName);
        }

        private void CloseNpcWithOwnedTransport(long npcEntityId)
        {
            CloseNpcTransport(npcEntityId);
            Npcs?.Close(npcEntityId);
        }

        private void CloseNpcTransport(long npcEntityId)
        {
            if (!_transportNpcStates.TryGetValue(npcEntityId, out var state))
                return;

            EquiEntityControllerComponent controller = null;
            if (Npcs != null && Npcs.Npcs.ContainsKey(npcEntityId))
            {
                var npc = Npcs.Npcs[npcEntityId];
                controller = npc?.Entity?.Components.Get<EquiEntityControllerComponent>();
            }
            if (controller?.Controlled != null)
                controller.ReleaseControl();

            _transportNpcStates.Remove(npcEntityId);
            if (state.OwnedByNpc)
                MyEntities.GetEntityByIdOrDefault(state.VehicleEntityId)?.Close();
        }

        private void CloseAllNpcTransports()
        {
            _staleCoverReservationIds.Clear();
            foreach (var entry in _transportNpcStates)
                _staleCoverReservationIds.Add(entry.Key);

            for (var i = 0; i < _staleCoverReservationIds.Count; i++)
                CloseNpcTransport(_staleCoverReservationIds[i]);
            _staleCoverReservationIds.Clear();
        }

        internal bool TryGetTransportMode(SiNpc npc, out SiSquadTransportMode mode)
        {
            mode = SiSquadTransportMode.None;
            if (npc == null || Squads == null)
                return false;

            SiAssignedNpc assignment;
            if (!Squads.TryGetAssignment(npc.EntityId, out assignment)
                || !_transportNpcStates.TryGetValue(npc.EntityId, out var transportState))
                return false;

            SiSquadCommandState state = null;
            if (assignment.Leader.Kind == SiSquadLeaderKind.Player
                && (!_squadOrders.TryGetValue(assignment.Leader.Id, out state)
                    || state.TransportMode == SiSquadTransportMode.None
                    || state.TransportVehicleEntityId == 0))
                return false;

            var vehicleEntityId = assignment.Leader.Kind == SiSquadLeaderKind.Player
                ? state.TransportVehicleEntityId
                : transportState.VehicleEntityId;
            if (MyEntities.GetEntityByIdOrDefault(vehicleEntityId) == null)
            {
                if (state != null)
                {
                    state.TransportMode = SiSquadTransportMode.None;
                    state.TransportVehicleEntityId = 0;
                }
                return false;
            }

            mode = assignment.Leader.Kind == SiSquadLeaderKind.Ai
                ? SiSquadTransportMode.Mount
                : state.TransportMode;
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
                || (assignment.Leader.Kind != SiSquadLeaderKind.Player
                    && assignment.Leader.Kind != SiSquadLeaderKind.Ai))
                return false;

            if (assignment.Leader.Kind == SiSquadLeaderKind.Ai)
                return mode == SiSquadTransportMode.Mount
                       && _transportNpcStates.ContainsKey(npc.EntityId);

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
                || (assignment.Leader.Kind != SiSquadLeaderKind.Player
                    && assignment.Leader.Kind != SiSquadLeaderKind.Ai))
                return false;

            if (assignment.Leader.Kind == SiSquadLeaderKind.Player
                && (!_squadOrders.TryGetValue(assignment.Leader.Id, out var order)
                    || order.TransportMode == SiSquadTransportMode.None
                    || order.TransportVehicleEntityId == 0))
                return false;

            if (!_transportNpcStates.ContainsKey(npc.EntityId))
                return false;

            if (!_transportNpcStates.TryGetValue(npc.EntityId, out var state)
                || !TryResolveTransportSeat(state, out slot)
                || !IsSeatAvailableForNpc(npc, slot))
                return false;

            return true;
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

        internal bool TryGetAssignedTransportVehicle(SiNpc npc, out MyEntity vehicle)
        {
            vehicle = null;
            if (npc == null || !_transportNpcStates.TryGetValue(npc.EntityId, out var state))
                return false;

            return TryGetTransportVehicleEntity(state.VehicleEntityId, out vehicle);
        }

        internal bool TryGetTransportLeaderControls(
            SiNpc npc,
            out float throttle,
            out Vector3D heading)
        {
            throttle = 0;
            heading = Vector3D.Zero;
            if (npc == null || Squads == null)
                return false;

            SiAssignedNpc assignment;
            if (!Squads.TryGetAssignment(npc.EntityId, out assignment)
                || (assignment.Leader.Kind != SiSquadLeaderKind.Player
                    && assignment.Leader.Kind != SiSquadLeaderKind.Ai))
                return false;

            if (assignment.Leader.Kind == SiSquadLeaderKind.Player
                && (!_squadOrders.TryGetValue(assignment.Leader.Id, out var order)
                    || order.TransportMode != SiSquadTransportMode.Mount
                    || MyPlayers.Static == null))
                return false;

            if (assignment.Leader.Kind == SiSquadLeaderKind.Ai)
            {
                if (!Npcs.Npcs.TryGetValue(assignment.Leader.Id, out var leaderNpc))
                    return false;

                return TryGetHorseControls(leaderNpc?.Entity, out throttle, out heading);
            }

            foreach (var entry in MyPlayers.Static.GetAllPlayers())
            {
                var player = entry.Value;
                if (player?.Identity == null || player.Identity.Id != assignment.Leader.Id)
                    continue;

                var controlledEntity = player.ControlledEntity as MyEntity;
                return TryGetHorseControls(controlledEntity, out throttle, out heading);
            }

            return false;
        }

        private static bool TryGetHorseControls(
            MyEntity rider,
            out float throttle,
            out Vector3D heading)
        {
            throttle = 0;
            heading = Vector3D.Zero;
            var seat = rider?.Components.Get<EquiEntityControllerComponent>()?.Controlled;
            var horseEntity = seat?.Controllable?.Entity;
            var horse = horseEntity?.Components.Get<MyPAX_Horse>();
            if (horse == null || horseEntity == null)
                return false;

            throttle = horse.Throttle;
            // PAX applies positive horse throttle along WorldMatrix.Backward.
            heading = horseEntity.WorldMatrix.Backward;
            return true;
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

            // Order changes must not dismount an NPC that is still mounted.
            // Keep the seat assignment alive and remove only its cached
            // formation target; the transport behavior will stop the vehicle
            // on its next tick.  Seat release is reserved for the explicit
            // Disembark order below.
            if (state.TransportMode == SiSquadTransportMode.Mount)
            {
                if (Squads != null && Npcs != null)
                    foreach (var npc in Squads.GetLeaderNpcs(Npcs, leaderIdentityId))
                        if (npc != null)
                            ClearCachedFormationPosition(npc.EntityId);
                return;
            }

            ReleaseLeaderTransportSeats(leaderIdentityId);
            state.TransportMode = SiSquadTransportMode.None;
            state.TransportVehicleEntityId = 0;
            ResetTransportCadence(state);
            RemoveTransportStatesForLeader(leaderIdentityId);
        }

        private bool TryGetMountAnchorVehicle(
            MyPlayer player,
            double searchRadius,
            out MyEntity vehicle,
            out string failure)
        {
            vehicle = null;
            failure = null;

            var controlledEntity = player?.ControlledEntity as MyEntity;
            var controller = controlledEntity?.Components.Get<EquiEntityControllerComponent>();
            var seat = controller?.Controlled;
            if (seat != null)
            {
                if (!SiTransportSeatHelpers.TryGetSeatBlockGrid(seat, out var seatBlockEntity, out var vehicleGrid))
                {
                    failure = "Failed to resolve the current vehicle grid.";
                    return false;
                }

                vehicle = vehicleGrid.Entity ?? seatBlockEntity;
                return true;
            }

            if (controlledEntity == null || searchRadius <= 0)
            {
                failure = "No compatible transport vehicle was found nearby.";
                return false;
            }

            _transportVehicleScanner.ScanEntities(
                controlledEntity.WorldMatrix.Translation,
                searchRadius,
                _nearbyTransportVehicleCandidates);
            var seenVehicleIds = new HashSet<long>();
            for (var i = 0; i < _nearbyTransportVehicleCandidates.Count; i++)
            {
                var entity = _nearbyTransportVehicleCandidates[i].Entity;
                if (entity == null || entity.Closed || entity.MarkedForClose)
                    continue;

                MyGridDataComponent gridData;
                if (!entity.Components.TryGet(out gridData))
                    continue;

                var candidate = gridData.Entity ?? entity;
                if (candidate == null
                    || candidate.Closed
                    || candidate.MarkedForClose
                    || !seenVehicleIds.Add(candidate.EntityId))
                    continue;

                foreach (var candidateSeat in EnumerateVehicleSeats(candidate))
                    if (candidateSeat?.Controllable?.Entity != null
                        && SiMountedVehicleDrivers.CanDrive(candidate, candidateSeat))
                    {
                        vehicle = candidate;
                        return true;
                    }
            }

            failure = "No compatible transport vehicle was found nearby.";
            return false;
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
                    CloseNpcTransport(npc.EntityId);
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

        private IEnumerable<MyEntity> EnumerateNearbyMountVehicles(MyEntity anchorVehicle, double searchRadius)
        {
            if (anchorVehicle == null || anchorVehicle.Closed || anchorVehicle.MarkedForClose)
                yield break;

            var seenVehicleIds = new HashSet<long>();
            if (seenVehicleIds.Add(anchorVehicle.EntityId))
                yield return anchorVehicle;

            if (searchRadius <= 0)
                yield break;

            _transportVehicleScanner.ScanEntities(
                anchorVehicle.WorldMatrix.Translation,
                searchRadius,
                _nearbyTransportVehicleCandidates);
            for (var i = 0; i < _nearbyTransportVehicleCandidates.Count; i++)
            {
                var entity = _nearbyTransportVehicleCandidates[i].Entity;
                if (entity == null || entity.Closed || entity.MarkedForClose)
                    continue;

                MyGridDataComponent gridData;
                if (!entity.Components.TryGet(out gridData))
                    continue;

                var vehicle = gridData.Entity ?? entity;
                if (vehicle == null
                    || vehicle.Closed
                    || vehicle.MarkedForClose
                    || !seenVehicleIds.Add(vehicle.EntityId))
                    continue;

                yield return vehicle;
            }
        }

        private bool TryAssignTransportSeat(
            SiNpc npc,
            IEnumerable<MyEntity> vehicles,
            out SiTransportNpcState assignedState)
        {
            assignedState = null;
            if (npc?.Entity == null || vehicles == null)
                return false;

            if (_transportNpcStates.TryGetValue(npc.EntityId, out var existing)
                && TryResolveTransportSeat(existing, out var currentSeat)
                && IsSeatAvailableForNpc(npc, currentSeat))
            {
                assignedState = existing;
                return true;
            }

            EquiPlayerAttachmentComponent.Slot bestSeat = null;
            MyEntity bestVehicle = null;
            var bestDistanceSquared = double.MaxValue;
            foreach (var vehicle in vehicles)
            {
                foreach (var seat in EnumerateVehicleSeats(vehicle))
                {
                    if (seat?.Controllable?.Entity == null
                        || !SiMountedVehicleDrivers.CanDrive(vehicle, seat)
                        || !IsSeatAvailableForNpc(npc, seat))
                        continue;

                    var distanceSquared = Vector3D.DistanceSquared(
                        npc.Entity.WorldMatrix.Translation,
                        seat.Controllable.Entity.WorldMatrix.Translation);
                    if (distanceSquared >= bestDistanceSquared)
                        continue;

                    bestDistanceSquared = distanceSquared;
                    bestSeat = seat;
                    bestVehicle = vehicle;
                }
            }

            if (bestSeat == null)
                return false;

            if (existing == null)
            {
                existing = new SiTransportNpcState();
                _transportNpcStates[npc.EntityId] = existing;
            }

            existing.VehicleEntityId = bestVehicle.EntityId;
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

        private bool IsSeatAvailableForNpc(SiNpc npc, EquiPlayerAttachmentComponent.Slot seat)
        {
            if (npc?.Entity == null || seat?.Controllable?.Entity == null)
                return false;

            if (seat.AttachedCharacter != null && seat.AttachedCharacter != npc.Entity)
                return false;
            if (IsSeatAssignedToOtherNpc(npc.EntityId, seat))
                return false;

            // Equi's attachment slot is authoritative once synchronized, but
            // player controllers can precede that state for a replication tick.
            // Check the controller relationship as well so a player-ridden
            // horse can never be assigned to an NPC during that window.
            if (MyPlayers.Static != null)
            {
                foreach (var entry in MyPlayers.Static.GetAllPlayers())
                {
                    var playerEntity = entry.Value?.ControlledEntity as MyEntity;
                    var controlledSeat = playerEntity?.Components
                        .Get<EquiEntityControllerComponent>()?.Controlled;
                    if (SameSeat(controlledSeat, seat) && playerEntity != npc.Entity)
                        return false;
                }
            }

            return true;
        }

        private static bool SameSeat(
            EquiPlayerAttachmentComponent.Slot left,
            EquiPlayerAttachmentComponent.Slot right)
        {
            return left != null
                   && right != null
                   && left.Controllable?.Entity?.EntityId == right.Controllable?.Entity?.EntityId
                   && string.Equals(left.Definition.Name, right.Definition.Name, StringComparison.Ordinal);
        }

        private IEnumerable<EquiPlayerAttachmentComponent.Slot> EnumerateVehicleSeats(MyEntity vehicle)
        {
            if (vehicle == null || !vehicle.Components.TryGet(out MyGridDataComponent gridData))
                yield break;

            foreach (var slot in SiTransportSeatHelpers.EnumerateSeatSlotsOnGrid(gridData))
                yield return slot;
        }

        private void MountSquad(ulong sender, MyPlayer player, long leaderIdentityId)
        {
            var troops = Squads?.GetLeaderNpcs(Npcs, leaderIdentityId);
            if (troops == null || troops.Count == 0)
            {
                Respond(sender, "Your squad has no utility AI troops.");
                return;
            }

            string failure;
            MyEntity vehicle;
            if (!TryGetMountAnchorVehicle(
                    player,
                    Squads.Definition.TransportVehicleSearchRadius,
                    out vehicle,
                    out failure))
            {
                Respond(sender, failure ?? "No compatible transport vehicle was found nearby.");
                return;
            }

            var state = GetSquadOrder(leaderIdentityId);
            state.Mode = SiSquadOrderMode.Follow;
            SetRearmOverride(state, false);
            state.TransportMode = SiSquadTransportMode.Mount;
            state.TransportVehicleEntityId = vehicle.EntityId;
            ResetTransportCadence(state);

            ClearLeaderWaypoints(leaderIdentityId);
            ReleaseLeaderTransportSeats(leaderIdentityId);
            RemoveTransportStatesForLeader(leaderIdentityId);

            var vehicles = EnumerateNearbyMountVehicles(
                vehicle,
                Squads.Definition.TransportVehicleSearchRadius);

            var assigned = 0;
            for (var i = 0; i < troops.Count; i++)
                if (TryAssignTransportSeat(troops[i], vehicles, out var ignored))
                    assigned++;

            if (assigned == 0)
            {
                state.TransportMode = SiSquadTransportMode.None;
                state.TransportVehicleEntityId = 0;
                ResetTransportCadence(state);
                Respond(sender, "No free compatible transport seats were found near your vehicle.");
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
