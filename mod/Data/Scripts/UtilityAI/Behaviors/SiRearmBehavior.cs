using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Xml.Serialization;
using Sandbox.Game.Players;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Inventory;
using VRage.ObjectBuilders;
using VRageMath;

namespace Si.UtilityAI
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiRearmBehavior : MyObjectBuilder_EntityComponent
    {
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiRearmBehaviorDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        [DefaultValue(1f)]
        public float Score = 1f;

        [DefaultValue(20f)]
        public float SearchRadius = 20f;

        [DefaultValue(2.25f)]
        public float TransferDistance = 2.25f;

        [DefaultValue(0.75f)]
        public float WaypointRefreshDistance = 0.75f;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiRearmBehaviorDefinition))]
    public class SiRearmBehaviorDefinition : MyEntityComponentDefinition
    {
        public float Score { get; private set; }
        public float SearchRadius { get; private set; }
        public float TransferDistance { get; private set; }
        public float WaypointRefreshDistance { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiRearmBehaviorDefinition)builder;
            Score = Math.Max(0f, Math.Min(1f, ob.Score));
            SearchRadius = Math.Max(0.5f, ob.SearchRadius);
            TransferDistance = Math.Max(0.25f, ob.TransferDistance);
            WaypointRefreshDistance = Math.Max(0.05f, ob.WaypointRefreshDistance);
        }
    }

    [MyComponent(typeof(MyObjectBuilder_SiRearmBehavior))]
    [MyDefinitionRequired(typeof(SiRearmBehaviorDefinition))]
    public class SiRearmBehaviorComponent : MyEntityComponent, ISiUtilityBehavior, ISiContinuousUtilityBehavior
    {
        private readonly SiNearbyEntityScanner _scanner = new SiNearbyEntityScanner();
        private readonly List<SiNearbyEntityScanner.InventoryCandidate> _candidates =
            new List<SiNearbyEntityScanner.InventoryCandidate>();

        private SiRearmBehaviorDefinition _definition;
        private bool _hasIssuedWaypoint;
        private Vector3D _lastIssuedWaypoint;

        public string BehaviorName => DefinitionId.ToString();

        public override bool IsSerialized => false;

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            _definition = (SiRearmBehaviorDefinition)definition;
        }

        float ISiUtilityBehavior.Evaluate(SiUtilityContext context)
        {
            var session = SiNpcSessionComponent.Instance;
            if (context?.Agent == null || session == null || !session.IsRearming(context.Agent))
            {
                ResetState();
                return 0f;
            }

            return _definition.Score;
        }

        void ISiUtilityBehavior.Begin(SiUtilityContext context)
        {
            ApplyRearm(context);
        }

        void ISiUtilityBehavior.Tick(SiUtilityContext context, long elapsedMilliseconds)
        {
            ApplyRearm(context);
        }

        void ISiUtilityBehavior.End(SiUtilityContext context)
        {
            var stillRearming = context?.Agent != null
                                && SiNpcSessionComponent.Instance != null
                                && SiNpcSessionComponent.Instance.IsRearming(context.Agent);
            if (!stillRearming)
                context?.TryClearWaypoint();
            ResetState();
        }

        private void ApplyRearm(SiUtilityContext context)
        {
            var session = SiNpcSessionComponent.Instance;
            if (context?.Agent == null || context.Entity == null || session == null)
                return;

            if (!session.IsRearming(context.Agent))
            {
                context.TryClearWaypoint();
                ResetState();
                return;
            }

            context.Agent.ClearCombatMovementRole();
            session.ReleaseCover(context.Agent.EntityId);
            context.TrySetCrouch(false);

            if (!SiNpcAmmoStatusHelper.TryGetAmmoProfile(context.Agent, out var profile) || profile == null)
            {
                context.TryClearWaypoint();
                ResetState();
                return;
            }

            if (!SiNpcAmmoStatusHelper.TryGetAmmoStatus(context.Agent, out var status) || !status.NeedsRearm)
            {
                context.TryClearWaypoint();
                ResetState();
                return;
            }

            string ignored;
            var destinationInventory = SiNpcEquipmentHelper.FindInventory(context.Entity, out ignored);
            if (destinationInventory == null)
            {
                context.TryClearWaypoint();
                ResetState();
                return;
            }

            if (!TryFindBestAmmoSource(context, profile, out var sourceEntity, out var sourceInventory))
            {
                context.TryClearWaypoint();
                ResetState();
                return;
            }

            var sourcePosition = sourceEntity.WorldMatrix.Translation;
            if (Vector3D.DistanceSquared(context.Position, sourcePosition)
                <= _definition.TransferDistance * _definition.TransferDistance)
            {
                if (!TryTransferAmmo(profile, sourceInventory, destinationInventory, status.MaxUnits - status.CurrentUnits))
                {
                    context.TryClearWaypoint();
                    ResetState();
                    return;
                }

                if (SiNpcAmmoStatusHelper.TryGetAmmoStatus(context.Agent, out var refreshedStatus) && !refreshedStatus.NeedsRearm)
                {
                    context.TryClearWaypoint();
                    ResetState();
                    return;
                }

                if (!SiNpcAmmoStatusHelper.InventoryHasSourceAmmo(profile, sourceInventory))
                {
                    context.TryClearWaypoint();
                    ResetState();
                }

                return;
            }

            var refreshDistanceSquared = _definition.WaypointRefreshDistance * _definition.WaypointRefreshDistance;
            if (!_hasIssuedWaypoint
                || !context.HasWaypoint
                || Vector3D.DistanceSquared(_lastIssuedWaypoint, sourcePosition) > refreshDistanceSquared)
            {
                if (context.TrySetWaypoint(sourcePosition))
                {
                    _lastIssuedWaypoint = sourcePosition;
                    _hasIssuedWaypoint = true;
                }
            }
        }

        private bool TryFindBestAmmoSource(
            SiUtilityContext context,
            SiNpcAmmoProfile profile,
            out MyEntity sourceEntity,
            out MyInventoryBase sourceInventory)
        {
            sourceEntity = null;
            sourceInventory = null;

            _scanner.ScanInventories(
                context.Position,
                _definition.SearchRadius,
                _candidates,
                entity => IsEligibleSource(context.Agent, entity));

            for (var i = 0; i < _candidates.Count; i++)
            {
                var candidate = _candidates[i];
                if (!SiNpcAmmoStatusHelper.InventoryHasSourceAmmo(profile, candidate.Inventory))
                    continue;

                sourceEntity = candidate.Entity;
                sourceInventory = candidate.Inventory;
                return true;
            }

            return false;
        }

        private bool IsEligibleSource(SiNpc agent, MyEntity entity)
        {
            if (entity == null || entity.EntityId == 0 || entity.EntityId == agent.EntityId)
                return false;

            var session = SiNpcSessionComponent.Instance;
            if (session?.Npcs?.Npcs.ContainsKey(entity.EntityId) == true)
                return false;

            if (MyPlayers.Static != null)
                foreach (var playerEntry in MyPlayers.Static.GetAllPlayers())
                    if (playerEntry.Value?.ControlledEntity?.EntityId == entity.EntityId)
                        return false;

            return true;
        }

        private bool TryTransferAmmo(
            SiNpcAmmoProfile profile,
            MyInventoryBase sourceInventory,
            MyInventoryBase destinationInventory,
            int neededUnits)
        {
            if (profile == null || sourceInventory == null || destinationInventory == null || neededUnits <= 0)
                return false;

            for (var i = 0; i < sourceInventory.Items.Count; i++)
            {
                var item = sourceInventory.Items.ItemAt(i);
                if (item == null)
                    continue;

                if (!SiNpcAmmoStatusHelper.TryResolveTransferAmount(profile, item, neededUnits, out var transferAmount))
                    continue;
                if (transferAmount <= 0 || !destinationInventory.CanAddItems(item.DefinitionId, transferAmount))
                    continue;
                if (destinationInventory.TransferItemsFrom(sourceInventory, item, transferAmount))
                    return true;
            }

            return false;
        }

        private void ResetState()
        {
            _hasIssuedWaypoint = false;
            _lastIssuedWaypoint = Vector3D.Zero;
            _candidates.Clear();
        }
    }
}
