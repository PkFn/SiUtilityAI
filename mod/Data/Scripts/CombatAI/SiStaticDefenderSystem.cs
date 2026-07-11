using System;
using System.Collections.Generic;
using Medieval.Entities.Components.Blocks;
using Medieval.Entities.Components.Grid;
using Pax.RangedDefenders;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.Entity.EntityComponents;
using VRage.ObjectBuilders;

namespace Si.UtilityAI
{
    internal sealed class SiStaticDefenderSystem
    {
        private const string ShootEventId = "StartShoot";

        private readonly Dictionary<string, DefenderMetadata> _metadataByBlockSubtype =
            new Dictionary<string, DefenderMetadata>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<long, SiStaticDefenderTarget> _targets =
            new Dictionary<long, SiStaticDefenderTarget>();
        private readonly HashSet<long> _seenTargetIds = new HashSet<long>();
        private readonly List<long> _removals = new List<long>();
        private readonly List<MyPAX_ShootingDefender> _scratch = new List<MyPAX_ShootingDefender>();
        private readonly SiNpcSessionComponent _session;

        internal SiStaticDefenderSystem(SiNpcSessionComponent session)
        {
            _session = session;
            LoadMetadata();
        }

        internal IEnumerable<SiStaticDefenderTarget> ActiveTargets => _targets.Values;

        internal void Clear()
        {
            foreach (var pair in _targets)
                Unsubscribe(pair.Value);

            _targets.Clear();
            _seenTargetIds.Clear();
            _removals.Clear();
            _scratch.Clear();
        }

        internal void Update(long elapsedMilliseconds)
        {
            _seenTargetIds.Clear();
            _scratch.Clear();

            if (MyPAX_ShootingDefender.ShootingDefenders != null)
                _scratch.AddRange(MyPAX_ShootingDefender.ShootingDefenders);

            for (var i = 0; i < _scratch.Count; i++)
            {
                var component = _scratch[i];
                var entity = component?.Entity;
                if (!IsValidEntity(entity))
                    continue;

                var entityId = entity.EntityId;
                if (entityId == 0 || !_seenTargetIds.Add(entityId))
                    continue;

                if (!_targets.TryGetValue(entityId, out var target))
                {
                    target = new SiStaticDefenderTarget();
                    _targets.Add(entityId, target);
                }

                RefreshTarget(target, component);
            }

            _removals.Clear();
            foreach (var pair in _targets)
            {
                var target = pair.Value;
                if (target == null
                    || !_seenTargetIds.Contains(pair.Key)
                    || !IsValidEntity(target.Entity))
                    _removals.Add(pair.Key);
            }

            for (var i = 0; i < _removals.Count; i++)
            {
                var entityId = _removals[i];
                if (_targets.TryGetValue(entityId, out var target))
                    Unsubscribe(target);
                _targets.Remove(entityId);
            }
        }

        internal bool TryGetTarget(long entityId, out SiStaticDefenderTarget target)
        {
            return _targets.TryGetValue(entityId, out target);
        }

        private void LoadMetadata()
        {
            var metadataByScriptSubtype = new Dictionary<string, DefenderMetadata>(StringComparer.OrdinalIgnoreCase);
            foreach (var definition in MyDefinitionManager.GetOfType<MyPAX_ShootingDefenderDefinition>())
            {
                if (definition == null)
                    continue;

                metadataByScriptSubtype[definition.Id.SubtypeName] = new DefenderMetadata
                {
                    ScriptSubtype = definition.Id.SubtypeName,
                    KnockoutIntegrityThreshold = Math.Max(0, definition.Durability),
                    DetectionRange = Math.Max(0, definition.PlayerDetectionRange),
                };
            }

            foreach (var container in MyDefinitionManager.GetOfType<MyContainerDefinition>())
            {
                if (container?.Components == null)
                    continue;

                for (var i = 0; i < container.Components.Count; i++)
                {
                    var component = container.Components[i];
                    if (!IsShootingDefenderComponent(component))
                        continue;

                    var blockSubtype = container.Id.SubtypeName;
                    var scriptSubtype = component.DefinitionId.SubtypeName ?? string.Empty;
                    if (!metadataByScriptSubtype.TryGetValue(scriptSubtype, out var metadata))
                    {
                        metadata = new DefenderMetadata
                        {
                            ScriptSubtype = scriptSubtype,
                        };
                    }

                    _metadataByBlockSubtype[blockSubtype] = metadata;
                    break;
                }
            }
        }

        private static bool IsShootingDefenderComponent(MyContainerDefinition.Component component)
        {
            if (component == null)
                return false;

            var typeName = component.Type.ToString() ?? string.Empty;
            if (string.Equals(typeName, nameof(MyObjectBuilder_PAX_ShootingDefender), StringComparison.Ordinal))
                return true;

            var definitionTypeName = component.DefinitionId.TypeId.ToString() ?? string.Empty;
            return string.Equals(definitionTypeName, nameof(MyObjectBuilder_PAX_ShootingDefender), StringComparison.Ordinal);
        }

        private void RefreshTarget(SiStaticDefenderTarget target, MyPAX_ShootingDefender component)
        {
            if (target == null || component == null)
                return;

            target.Component = component;
            target.Entity = component.Entity;
            target.EntityId = component.Entity?.EntityId ?? 0;
            target.TargetNeutral = component.TargetNeutral;
            target.OwnerIdentityId = ResolveOwnerIdentityId(component);
            target.Metadata = ResolveMetadata(target.Entity);
            target.IsKnockedOut = ResolveKnockedOut(target, out var actualIntegrity, out var maxIntegrity);
            target.ActualIntegrity = actualIntegrity;
            target.MaxIntegrity = maxIntegrity;
            Subscribe(target);
        }

        private DefenderMetadata ResolveMetadata(MyEntity entity)
        {
            var blockSubtype = entity?.Components?.Get<MyBuildableBlockComponent>()?.DefinitionId.SubtypeName;
            if (string.IsNullOrWhiteSpace(blockSubtype))
                return null;

            _metadataByBlockSubtype.TryGetValue(blockSubtype, out var metadata);
            return metadata;
        }

        private static long ResolveOwnerIdentityId(MyPAX_ShootingDefender component)
        {
            if (component?.Owner != null && component.Owner.OwnerId != 0)
                return component.Owner.OwnerId;

            var ownership = component?.Entity?.Components?.Get<MyEntityOwnershipComponent>();
            return ownership?.OwnerId ?? 0;
        }

        private static bool ResolveKnockedOut(
            SiStaticDefenderTarget target,
            out uint actualIntegrity,
            out uint maxIntegrity)
        {
            actualIntegrity = 0;
            maxIntegrity = 0;

            var entity = target?.Entity;
            var block = entity?.Components?.Get<MyBuildableBlockComponent>();
            var gridBuilding = block?.ParentGridBuildingComponent ?? entity?.Parent?.Components?.Get<MyGridBuildingComponent>();
            if (block == null || gridBuilding == null)
                return false;

            var blockState = gridBuilding.GetBlockState(block.BlockId);
            if (blockState == null)
                return false;

            actualIntegrity = blockState.ActualIntegrity;
            maxIntegrity = blockState.MaxIntegrity;

            var knockoutThreshold = target.Metadata?.KnockoutIntegrityThreshold ?? 0f;
            if (knockoutThreshold > 0)
                return actualIntegrity < (uint)Math.Ceiling(knockoutThreshold);

            return !block.IsFunctional && actualIntegrity == 0;
        }

        private void Subscribe(SiStaticDefenderTarget target)
        {
            if (target == null || target.ShootListener != null)
                return;

            var eventBus = target.Entity?.Components?.Get<MyComponentEventBus>();
            if (eventBus == null)
                return;

            Action<string> listener = eventId => OnDefenderShot(target);
            if (!eventBus.TryAddListener(ShootEventId, listener))
                return;

            target.EventBus = eventBus;
            target.ShootListener = listener;
        }

        private static void Unsubscribe(SiStaticDefenderTarget target)
        {
            if (target?.EventBus == null || target.ShootListener == null)
                return;

            target.EventBus.RemoveListener(ShootEventId, target.ShootListener);
            target.EventBus = null;
            target.ShootListener = null;
        }

        private void OnDefenderShot(SiStaticDefenderTarget target)
        {
            var entity = target?.Entity;
            if (entity == null || target.IsKnockedOut)
                return;

            _session?.Spotting?.ReportShot(entity.EntityId, entity);
        }

        private static bool IsValidEntity(MyEntity entity)
        {
            return entity != null
                   && entity.InScene
                   && !entity.Closed
                   && !entity.MarkedForClose;
        }

        internal sealed class DefenderMetadata
        {
            public string ScriptSubtype;
            public float KnockoutIntegrityThreshold;
            public float DetectionRange;
        }

        internal sealed class SiStaticDefenderTarget
        {
            internal MyPAX_ShootingDefender Component;
            internal DefenderMetadata Metadata;
            internal MyComponentEventBus EventBus;
            internal Action<string> ShootListener;

            public MyEntity Entity { get; internal set; }
            public long EntityId { get; internal set; }
            public long OwnerIdentityId { get; internal set; }
            public bool TargetNeutral { get; internal set; }
            public bool IsKnockedOut { get; internal set; }
            public uint ActualIntegrity { get; internal set; }
            public uint MaxIntegrity { get; internal set; }
            public float DetectionRange => Metadata?.DetectionRange ?? 0f;
        }
    }
}
