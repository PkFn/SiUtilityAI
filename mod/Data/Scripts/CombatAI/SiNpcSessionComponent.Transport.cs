using System;
using System.Collections.Generic;
using Equinox76561198048419394.Core.Controller;
using Pax.Animals;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Players;
using SiCore.Core.Grid;
using VRage.Components.Entity.CubeGrid;
using VRage.Definitions.Components.Entity.CubeGrid;
using VRage.Definitions.Grid;
using VRage.Entity.Block;
using VRage.Factory;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.Entity.EntityComponents;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Block;
using VRage.ObjectBuilders.Scene;
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

            if (!TrySpawnAdminHorse(npc, out var horse, out failure))
                return false;

            if (!TryAssignTransportSeat(npc, new[] { horse }, out var assignedState))
            {
                _log.Warning($"entityId={npc.EntityId} entityName={npc.Entity.Name ?? "null"} keyDefinition={AdminHorseBlockSubtype} branch=seat-assignment-failed horseId={horse?.EntityId ?? 0}"); // AGENT-DEBUG-LOG
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

            _log.Info($"entityId={npc.EntityId} entityName={npc.Entity.Name ?? "null"} keyDefinition={AdminHorseBlockSubtype} branch=mounted-prepared horseId={horse.EntityId} seatEntityId={assignedState.SeatEntityId} seatSlot={assignedState.SeatSlotName ?? "null"}"); // AGENT-DEBUG-LOG

            return true;
        }

        private bool TrySpawnAdminHorse(
            SiNpc npc,
            out MyEntity horse,
            out string failure)
        {
            horse = null;
            failure = null;
            var anchor = npc?.Entity;
            var anchorId = anchor?.EntityId ?? 0;
            var anchorName = anchor?.Name ?? "null";
            var transform = anchor?.WorldMatrix ?? MatrixD.Identity;
            try
            {
                _log.Info($"entityId={anchorId} entityName={anchorName} keyDefinition={AdminHorseBlockSubtype} branch=spawn-start"); // AGENT-DEBUG-LOG
                MyDefinitionId horseDefinitionId;
                var hasHorseDefinition = TryGetHorseDefinitionId(anchorId, anchorName, out horseDefinitionId);
                if (hasHorseDefinition)
                {
                    horse = MyEntities.CreateFromComponentContainerDefinitionAndAdd(
                        horseDefinitionId,
                        transform,
                        true);
                    _log.Info($"entityId={anchorId} entityName={anchorName} keyDefinition={AdminHorseBlockSubtype} branch=container-create result={(horse == null ? "null" : "success")} horseId={horse?.EntityId ?? 0} definitionId={horseDefinitionId}"); // AGENT-DEBUG-LOG

                    if (horse == null)
                    {
                        horse = MyEntities.CreateEntityAndAdd(
                            horseDefinitionId,
                            true,
                            transform.Translation,
                            (Vector3)transform.Up,
                            (Vector3)transform.Forward);
                        _log.Info($"entityId={anchorId} entityName={anchorName} keyDefinition={AdminHorseBlockSubtype} branch=entity-container-create result={(horse == null ? "null" : "success")} horseId={horse?.EntityId ?? 0} definitionId={horseDefinitionId}"); // AGENT-DEBUG-LOG
                    }
                }

                if (horse != null)
                    return true;

                horse = TryCreateHorseGridUsingPlacementPipeline(
                    transform,
                    horseDefinitionId,
                    anchorId,
                    anchorName);
                if (horse == null)
                {
                    _log.Warning($"entityId={anchorId} entityName={anchorName} keyDefinition={AdminHorseBlockSubtype} branch=placement-pipeline-failed invoking-loaded-horse-fallback"); // AGENT-DEBUG-LOG
                    horse = TryCloneLoadedHorse(transform, anchorId, anchorName);
                    _log.Info($"entityId={anchorId} entityName={anchorName} keyDefinition={AdminHorseBlockSubtype} branch=loaded-horse-fallback-result result={(horse == null ? "null" : "success")} horseId={horse?.EntityId ?? 0}"); // AGENT-DEBUG-LOG
                }
                if (horse == null)
                {
                    _log.Warning($"entityId={anchorId} entityName={anchorName} keyDefinition={AdminHorseBlockSubtype} branch=spawn-failed reason=dynamic-grid-initialization"); // AGENT-DEBUG-LOG
                    failure = "The PAX horse block could not be initialized into a dynamic grid.";
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                _log.Error($"entityId={anchorId} entityName={anchorName} keyDefinition={AdminHorseBlockSubtype} branch=spawn-exception exception={exception}"); // AGENT-DEBUG-LOG
                horse?.Close();
                horse = null;
                failure = $"Failed to create a PAX horse: {exception.Message}";
                return false;
            }
        }

        private MyEntity TryCreateHorseGridUsingPlacementPipeline(
            in MatrixD transform,
            MyDefinitionId horseDefinitionId,
            long anchorId,
            string anchorName)
        {
            var session = MySession.Static;
            if (session?.Scene == null)
            {
                _log.Warning($"entityId={anchorId} entityName={anchorName} keyDefinition={AdminHorseBlockSubtype} branch=placement-scene-lookup result=missing scene=null"); // AGENT-DEBUG-LOG
                return null;
            }

            MyContainerDefinition gridContainer;
            MyGridDataComponentDefinition gridDataDefinition;
            MyGridFamilyDefinition gridFamily;
            if (!TryGetSmallGridPlacementDefinitions(
                    anchorId,
                    anchorName,
                    out gridContainer,
                    out gridDataDefinition,
                    out gridFamily))
            {
                _log.Warning($"entityId={anchorId} entityName={anchorName} keyDefinition={AdminHorseBlockSubtype} branch=placement-grid-definition result=missing"); // AGENT-DEBUG-LOG
                return null;
            }

            _log.Info($"entityId={anchorId} entityName={anchorName} keyDefinition={AdminHorseBlockSubtype} branch=placement-grid-definition result=success container={gridContainer.Id} gridData={gridDataDefinition.Id} size={gridDataDefinition.Size} family={gridFamily.Id} finalGrid={gridFamily.FinalGridDefinitionId}"); // AGENT-DEBUG-LOG
            var grid = session.Scene.CreateEntity(
                gridFamily.FinalGridDefinitionId,
                MyEntityIdentifier.AllocateId());
            _log.Info($"entityId={anchorId} entityName={anchorName} keyDefinition={AdminHorseBlockSubtype} branch=placement-grid-create result={(grid == null ? "null" : "success")} gridId={grid?.EntityId ?? 0} definition={gridFamily.FinalGridDefinitionId}"); // AGENT-DEBUG-LOG
            if (grid == null)
                return null;

            grid.PositionComp.SetWorldMatrix(transform, null, true);

            MyGridDataComponent gridData;
            if (!grid.Components.TryGet(out gridData) || gridData == null)
            {
                _log.Warning($"entityId={anchorId} entityName={anchorName} keyDefinition={AdminHorseBlockSubtype} branch=placement-grid-components result=missing gridId={grid.EntityId}"); // AGENT-DEBUG-LOG
                grid.Close();
                return null;
            }

            var horseBuilder = new MyObjectBuilder_Block
            {
                Id = (ulong)MyEntityIdentifier.AllocateId(),
                DefinitionId = new SerializableDefinitionId(
                    horseDefinitionId.TypeId,
                    horseDefinitionId.SubtypeName),
                Min = Vector3I.Zero,
                Orientation = new SerializableBlockOrientation(
                    Base6Directions.Direction.Forward,
                    Base6Directions.Direction.Up),
            };
            _log.Info($"entityId={anchorId} entityName={anchorName} keyDefinition={AdminHorseBlockSubtype} branch=placement-add-block-start gridId={grid.EntityId} blockId={horseBuilder.Id} blockCountBefore={gridData.BlockCount}"); // AGENT-DEBUG-LOG

            var horseBlockData = MyBlock.Factory.CreateAndDeserialize(horseBuilder);
            if (horseBlockData == null)
            {
                _log.Warning($"entityId={anchorId} entityName={anchorName} keyDefinition={AdminHorseBlockSubtype} branch=placement-block-factory result=null gridId={grid.EntityId}"); // AGENT-DEBUG-LOG
                grid.Close();
                return null;
            }
            var addResult = gridData.AddBlock(horseBlockData, false);

            MyBlock horseBlock = null;
            foreach (var block in gridData.Blocks)
            {
                if (block == null)
                    continue;
                if (string.Equals(block.DefinitionId.SubtypeName, AdminHorseBlockSubtype, StringComparison.Ordinal))
                    horseBlock = block;
            }

            _log.Info($"entityId={anchorId} entityName={anchorName} keyDefinition={AdminHorseBlockSubtype} branch=placement-add-block-result addResult={addResult} horseBlock={(horseBlock == null ? "missing" : "success")} blockCountAfter={gridData.BlockCount}"); // AGENT-DEBUG-LOG
            if (!addResult || horseBlock == null)
            {
                grid.Close();
                return null;
            }

            session.Scene.ActivateEntity(grid);

            _log.Info($"entityId={anchorId} entityName={anchorName} keyDefinition={AdminHorseBlockSubtype} branch=placement-grid-ready gridId={grid.EntityId} blockCount={gridData.BlockCount}"); // AGENT-DEBUG-LOG
            return grid;
        }

        private bool TryGetSmallGridPlacementDefinitions(
            long anchorId,
            string anchorName,
            out MyContainerDefinition gridContainer,
            out MyGridDataComponentDefinition gridDataDefinition,
            out MyGridFamilyDefinition gridFamily)
        {
            gridContainer = null;
            gridDataDefinition = null;
            gridFamily = null;
            MyContainerDefinition fallbackContainer = null;
            MyGridDataComponentDefinition fallbackGridData = null;
            MyGridFamilyDefinition fallbackFamily = null;

            foreach (var candidate in MyDefinitionManager.GetOfType<MyContainerDefinition>())
            {
                if (candidate?.Components == null)
                    continue;

                foreach (var component in candidate.Components)
                {
                    var candidateGridData = component?.Definition as MyGridDataComponentDefinition;
                    if (candidateGridData == null
                        || !MyDefinitionManager.TryGet(candidateGridData.Id, out MyGridFamilyDefinition candidateFamily)
                        || candidateFamily == null)
                        continue;

                    if (fallbackContainer == null)
                    {
                        fallbackContainer = candidate;
                        fallbackGridData = candidateGridData;
                        fallbackFamily = candidateFamily;
                    }

                    if (candidateGridData.Size <= 0.25f)
                    {
                        gridContainer = candidate;
                        gridDataDefinition = candidateGridData;
                        gridFamily = candidateFamily;
                        _log.Info($"entityId={anchorId} entityName={anchorName} keyDefinition={AdminHorseBlockSubtype} branch=placement-grid-definition-candidate container={candidate.Id} gridData={candidateGridData.Id} size={candidateGridData.Size} family={candidateFamily.Id}"); // AGENT-DEBUG-LOG
                        return true;
                    }
                }
            }

            gridContainer = fallbackContainer;
            gridDataDefinition = fallbackGridData;
            gridFamily = fallbackFamily;
            if (gridContainer != null)
                _log.Warning($"entityId={anchorId} entityName={anchorName} keyDefinition={AdminHorseBlockSubtype} branch=placement-grid-definition-fallback container={gridContainer.Id} gridData={gridDataDefinition.Id} size={gridDataDefinition.Size} family={gridFamily.Id}"); // AGENT-DEBUG-LOG
            return gridContainer != null;
        }

        private bool TryGetHorseDefinitionId(
            long entityId,
            string entityName,
            out MyDefinitionId definitionId)
        {
            definitionId = default(MyDefinitionId);
            var found = false;
            var typeName = "none";
            foreach (var container in MyDefinitionManager.GetOfType<MyContainerDefinition>())
            {
                if (container == null
                    || !string.Equals(container.Id.SubtypeName, AdminHorseBlockSubtype, StringComparison.OrdinalIgnoreCase))
                    continue;

                found = true;
                definitionId = container.Id;
                typeName = container.Id.TypeId.ToString();
                break;
            }

            _log.Info($"entityId={entityId} entityName={entityName} keyDefinition={AdminHorseBlockSubtype} branch=definition-scan found={found} type={typeName}"); // AGENT-DEBUG-LOG
            return found;
        }

        private MyEntity TryCloneLoadedHorse(in MatrixD transform, long anchorId, string anchorName)
        {
            _log.Info($"entityId={anchorId} entityName={anchorName} keyDefinition={AdminHorseBlockSubtype} branch=loaded-horse-scan-start"); // AGENT-DEBUG-LOG
            var scannedGridIds = new HashSet<long>();
            foreach (var entity in MyEntities.GetEntities())
            {
                if (entity == null || entity.Closed || entity.MarkedForClose
                    || !entity.Components.TryGet(out MyGridDataComponent gridData))
                    continue;

                var hasHorseBlock = false;
                foreach (var block in gridData.Blocks)
                {
                    if (block != null
                        && string.Equals(block.DefinitionId.SubtypeName, AdminHorseBlockSubtype, StringComparison.Ordinal))
                    {
                        hasHorseBlock = true;
                        break;
                    }
                }

                if (!hasHorseBlock)
                    continue;

                _log.Info($"entityId={entity.EntityId} entityName={entity.Name ?? "null"} keyDefinition={AdminHorseBlockSubtype} branch=loaded-horse-candidate"); // AGENT-DEBUG-LOG

                var sourceEntity = gridData.Entity ?? entity;
                if (sourceEntity == null || !scannedGridIds.Add(sourceEntity.EntityId))
                    continue;

                _log.Info($"entityId={entity.EntityId} entityName={entity.Name ?? "null"} keyDefinition={AdminHorseBlockSubtype} branch=loaded-horse-grid-resolution gridEntityId={sourceEntity.EntityId} gridEntityName={sourceEntity.Name ?? "null"} entityType={sourceEntity.GetType().FullName} parentId={sourceEntity.Parent?.EntityId ?? 0}"); // AGENT-DEBUG-LOG

                var sourceBuilder = ((VRage.Core.IMyObject)sourceEntity).Serialize();
                var source = sourceBuilder as MyObjectBuilder_CubeGrid;
                _log.Info($"entityId={entity.EntityId} entityName={entity.Name ?? "null"} keyDefinition={AdminHorseBlockSubtype} branch=loaded-horse-source-builder gridEntityId={sourceEntity.EntityId} result={(source == null ? "null" : "success")} builderType={sourceBuilder?.GetType().FullName ?? "null"} blockCount={source?.CubeBlocks?.Count ?? 0}"); // AGENT-DEBUG-LOG
                var clone = source?.Clone() as MyObjectBuilder_CubeGrid;
                if (clone == null || clone.CubeBlocks == null)
                {
                    _log.Warning($"entityId={entity.EntityId} entityName={entity.Name ?? "null"} keyDefinition={AdminHorseBlockSubtype} branch=loaded-horse-clone-failed gridEntityId={sourceEntity.EntityId} reason=builder-clone-null"); // AGENT-DEBUG-LOG
                    continue;
                }

                clone.EntityId = MyEntityIdentifier.AllocateId();
                clone.PersistentFlags |= MyPersistentEntityFlags2.InScene;
                clone.PositionAndOrientation = new MyPositionAndOrientation(transform);
                clone.IsStatic = false;
                clone.CreatePhysics = false;

                foreach (var block in clone.CubeBlocks)
                {
                    if (block == null)
                        continue;

                    block.EntityId = MyEntityIdentifier.AllocateId();
                    block.ComponentContainer = null;
                    block.ConstructionStockpile = null;
                    block.BuildPercent = 100f;
                    block.IntegrityPercent = 100f;
                }

                _log.Info($"entityId={entity.EntityId} entityName={entity.Name ?? "null"} keyDefinition={AdminHorseBlockSubtype} branch=loaded-horse-clone-builder gridId={clone.EntityId} blockCount={clone.CubeBlocks.Count} gridStatic={clone.IsStatic} createPhysics={clone.CreatePhysics}"); // AGENT-DEBUG-LOG

                var horse = MyEntities.CreateFromObjectBuilder(clone);
                _log.Info($"entityId={entity.EntityId} entityName={entity.Name ?? "null"} keyDefinition={AdminHorseBlockSubtype} branch=loaded-horse-clone-create result={(horse == null ? "null" : "success")} horseId={horse?.EntityId ?? 0}"); // AGENT-DEBUG-LOG
                if (horse != null)
                {
                    if (horse.EntityId == 0)
                        horse.EntityId = clone.EntityId;
                    MyEntities.Add(horse, true);
                    _log.Info($"entityId={entity.EntityId} entityName={entity.Name ?? "null"} keyDefinition={AdminHorseBlockSubtype} branch=loaded-horse-clone-add result=success horseId={horse.EntityId}"); // AGENT-DEBUG-LOG
                    return horse;
                }
            }

            _log.Warning($"entityId={anchorId} entityName={anchorName} keyDefinition={AdminHorseBlockSubtype} branch=loaded-horse-scan-result result=no-candidate"); // AGENT-DEBUG-LOG
            return null;
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
