using System.Collections.Generic;
using System.Xml.Serialization;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;

namespace Si.UtilityAI
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiNpcTrooperWeaponBindingDefinition : MyObjectBuilder_DefinitionBase
    {
        public SerializableDefinitionId? Weapon;
        public SerializableDefinitionId? ShootBehavior;

        [XmlArrayItem("Slot")]
        public List<Slot> Slots;

        public class Slot
        {
            [XmlAttribute]
            public string Name;

            public SerializableDefinitionId? Weapon;
            public SerializableDefinitionId? ShootBehavior;
        }
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiNpcTrooperWeaponBindingDefinition))]
    public class SiNpcTrooperWeaponBindingDefinition : MyDefinitionBase
    {
        private readonly List<SiNpcWeaponSlotBindingDefinition> _slots =
            new List<SiNpcWeaponSlotBindingDefinition>();
        private readonly Dictionary<SiNpcWeaponSlot, SiNpcWeaponSlotBindingDefinition> _slotsByKey =
            new Dictionary<SiNpcWeaponSlot, SiNpcWeaponSlotBindingDefinition>();

        public IReadOnlyList<SiNpcWeaponSlotBindingDefinition> Slots => _slots;
        public bool HasAnySlots => _slots.Count > 0;

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiNpcTrooperWeaponBindingDefinition)builder;

            _slots.Clear();
            _slotsByKey.Clear();

            if (ob.Slots != null)
                foreach (var slot in ob.Slots)
                    AddSlotBinding(slot?.Name, slot?.Weapon, slot?.ShootBehavior);

            if (ob.Weapon.HasValue)
                AddSlotBinding(SiNpcWeaponSlot.MainFirearm, ob.Weapon, ob.ShootBehavior);
        }

        public bool TryGetSlot(SiNpcWeaponSlot slot, out SiNpcWeaponSlotBindingDefinition binding)
        {
            binding = null;
            return slot != SiNpcWeaponSlot.None && _slotsByKey.TryGetValue(slot, out binding);
        }

        public bool TryGetDefaultSlot(out SiNpcWeaponSlotBindingDefinition binding)
        {
            if (TryGetSlot(SiNpcWeaponSlot.MainFirearm, out binding))
                return true;

            if (_slots.Count > 0)
            {
                binding = _slots[0];
                return true;
            }

            binding = null;
            return false;
        }

        private void AddSlotBinding(
            string slotName,
            SerializableDefinitionId? weaponDefinitionId,
            SerializableDefinitionId? shootBehaviorDefinitionId)
        {
            if (!SiNpcWeaponSlotExtensions.TryParse(slotName, out var slot))
                return;

            AddSlotBinding(slot, weaponDefinitionId, shootBehaviorDefinitionId);
        }

        private void AddSlotBinding(
            SiNpcWeaponSlot slot,
            SerializableDefinitionId? weaponDefinitionId,
            SerializableDefinitionId? shootBehaviorDefinitionId)
        {
            if (slot == SiNpcWeaponSlot.None
                || !weaponDefinitionId.HasValue
                || _slotsByKey.ContainsKey(slot))
                return;

            var binding = new SiNpcWeaponSlotBindingDefinition(
                slot,
                (MyDefinitionId)weaponDefinitionId.Value,
                shootBehaviorDefinitionId.HasValue
                    ? (MyDefinitionId?)shootBehaviorDefinitionId.Value
                    : null);
            _slots.Add(binding);
            _slotsByKey.Add(slot, binding);
        }
    }
}
