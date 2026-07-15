using System.Xml.Serialization;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;

namespace Si.UtilityAI
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiNpcWeaponSetComponent : MyObjectBuilder_EntityComponent
    {
    }

    [MyComponent(typeof(MyObjectBuilder_SiNpcWeaponSetComponent))]
    public class SiNpcWeaponSetComponent : MyEntityComponent
    {
        private SiNpcTrooperWeaponBindingDefinition _runtimeDefinition;
        private SiNpcWeaponSlot _activeSlot;
        private SiNpcRangedWeaponComponent _rangedWeapon;
        private SiNpcMeleeWeaponComponent _meleeWeapon;
        private SiShootOpposingNpcBehaviorComponent _shootBehavior;

        public override bool IsSerialized => false;
        public SiNpcWeaponSlot ActiveSlot => _activeSlot;

        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            CacheComponents();
        }

        internal bool ApplyRuntimeDefinition(SiNpcTrooperWeaponBindingDefinition runtimeDefinition)
        {
            if (runtimeDefinition == null)
                return false;

            CacheComponents();
            _runtimeDefinition = runtimeDefinition;
            _activeSlot = SiNpcWeaponSlot.None;
            return TryActivateMainFirearm();
        }

        internal bool TryActivateMainFirearm()
        {
            if (TryActivateSlot(SiNpcWeaponSlot.MainFirearm))
                return true;

            if (_runtimeDefinition != null && _runtimeDefinition.TryGetDefaultSlot(out var binding))
                return TryActivateSlot(binding.Slot);
            return false;
        }

        internal bool TryActivateAtFirearm() =>
            TryActivateSlot(SiNpcWeaponSlot.AtFirearm);

        internal bool TryActivateHandgun() =>
            TryActivateSlot(SiNpcWeaponSlot.Handgun);

        internal bool TryActivateMelee() =>
            TryActivateSlot(SiNpcWeaponSlot.Melee);

        internal bool TryActivateSlot(SiNpcWeaponSlot slot)
        {
            CacheComponents();
            if (_runtimeDefinition == null || !_runtimeDefinition.TryGetSlot(slot, out var binding))
                return false;

            if (_activeSlot == slot)
                return true;

            if (binding.TryResolveRangedDefinition(out var rangedDefinition))
            {
                _meleeWeapon?.ClearRuntimeDefinition();
                if (_rangedWeapon == null || !_rangedWeapon.ApplyRuntimeDefinition(rangedDefinition))
                    return false;

                if (binding.ShootBehaviorDefinitionId.HasValue && _shootBehavior != null)
                    _shootBehavior.ApplyRuntimeDefinition((MyDefinitionId)binding.ShootBehaviorDefinitionId.Value);

                _activeSlot = slot;
                return true;
            }

            if (binding.TryResolveMeleeDefinition(out var meleeDefinition))
            {
                _rangedWeapon?.ClearRuntimeDefinition();
                if (_meleeWeapon == null || !_meleeWeapon.ApplyRuntimeDefinition(meleeDefinition))
                    return false;

                _activeSlot = slot;
                return true;
            }

            return false;
        }

        internal bool TryGetSlotBinding(
            SiNpcWeaponSlot slot,
            out SiNpcWeaponSlotBindingDefinition binding)
        {
            binding = null;
            return _runtimeDefinition != null && _runtimeDefinition.TryGetSlot(slot, out binding);
        }

        private void CacheComponents()
        {
            _rangedWeapon = Entity?.Components?.Get<SiNpcRangedWeaponComponent>();
            _meleeWeapon = Entity?.Components?.Get<SiNpcMeleeWeaponComponent>();
            _shootBehavior = Entity?.Components?.Get<SiShootOpposingNpcBehaviorComponent>();
        }
    }
}
