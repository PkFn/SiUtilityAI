using System.Xml.Serialization;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.Inventory;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Inventory;
using VRage.ObjectBuilders;

namespace Si.UtilityAI
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiNpcMeleeWeaponComponent : MyObjectBuilder_EntityComponent
    {
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiNpcMeleeWeaponComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public SerializableDefinitionId? HeldItem;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiNpcMeleeWeaponComponentDefinition))]
    public class SiNpcMeleeWeaponComponentDefinition : MyEntityComponentDefinition
    {
        public SerializableDefinitionId? HeldItem { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiNpcMeleeWeaponComponentDefinition)builder;
            HeldItem = ob.HeldItem;
        }
    }

    [MyComponent(typeof(MyObjectBuilder_SiNpcMeleeWeaponComponent))]
    [MyDefinitionRequired(typeof(SiNpcMeleeWeaponComponentDefinition))]
    public class SiNpcMeleeWeaponComponent : MyEntityComponent
    {
        private SiNpcMeleeWeaponComponentDefinition _definition;
        private SiNpcMeleeWeaponComponentDefinition _runtimeDefinition;

        public override bool IsSerialized => false;
        public SiNpcMeleeWeaponComponentDefinition Definition => _runtimeDefinition ?? _definition;
        internal MyDefinitionId? HeldItemId => Definition.HeldItem.HasValue
            ? (MyDefinitionId?)Definition.HeldItem.Value
            : null;

        public bool IsOperational =>
            HeldItemId.HasValue
            && SiNpcEquipmentHelper.HasEquippedSubtype(Entity, HeldItemId.Value.SubtypeName)
            && GetHeldBehavior() != null;

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            _definition = (SiNpcMeleeWeaponComponentDefinition)definition;
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();

            if (MyAPIGateway.Multiplayer != null && !MyAPIGateway.Multiplayer.IsServer)
                return;

            AddScheduledCallback(EnsureHeldWeaponEquipped, 1);
        }

        internal bool ApplyRuntimeDefinition(MyDefinitionId definitionId)
        {
            SiNpcMeleeWeaponComponentDefinition runtimeDefinition;
            if (!MyDefinitionManager.TryGet(definitionId, out runtimeDefinition) || runtimeDefinition == null)
                return false;

            return ApplyRuntimeDefinition(runtimeDefinition);
        }

        internal bool ApplyRuntimeDefinition(SiNpcMeleeWeaponComponentDefinition runtimeDefinition)
        {
            if (runtimeDefinition == null)
                return false;

            _runtimeDefinition = runtimeDefinition;
            if (Entity != null && Entity.InScene && (MyAPIGateway.Multiplayer == null || MyAPIGateway.Multiplayer.IsServer))
                AddScheduledCallback(EnsureHeldWeaponEquipped, 1);
            return true;
        }

        internal void ClearRuntimeDefinition()
        {
            EndPrimaryAction();
            _runtimeDefinition = null;
        }

        internal bool TryEquipHeldWeapon()
        {
            if (!HeldItemId.HasValue || Entity == null)
                return false;

            string failure;
            return SiNpcEquipmentHelper.TryEnsureEquipmentItemEquipped(
                Entity,
                HeldItemId.Value,
                out failure,
                2);
        }

        internal bool TryStartPrimaryAction()
        {
            var behavior = GetHeldBehavior();
            if (behavior == null)
                return false;

            behavior.StartAction(MyHandItemActionEnum.Primary);
            return true;
        }

        internal void EndPrimaryAction()
        {
            GetHeldBehavior()?.EndAction(MyHandItemActionEnum.Primary);
        }

        [Update(false)]
        private void EnsureHeldWeaponEquipped(long _)
        {
            if (Entity == null || Entity.Closed || Entity.MarkedForClose || !HeldItemId.HasValue)
                return;

            string ignored;
            var inventory = SiNpcEquipmentHelper.FindInventory(Entity, out ignored);
            if (inventory == null)
                return;

            var heldItemId = HeldItemId.Value;
            var equipment = Entity.Components.Get<Sandbox.Entities.Components.MyEntityEquipmentComponent>();
            if (equipment != null && equipment.IsEquipped(heldItemId) && inventory.FindItem(heldItemId) != null)
                return;

            TryEquipHeldWeapon();
        }

        private MyHandItemBehaviorBase GetHeldBehavior()
        {
            return Entity?.Components
                .Get<MyCharacterHandItemsComponent>()
                ?.GetBehavior<MyHandItemBehaviorBase>();
        }
    }
}
