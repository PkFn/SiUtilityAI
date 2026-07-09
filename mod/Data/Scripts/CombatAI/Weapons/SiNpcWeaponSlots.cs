using System;
using Pax.Cannons;
using VRage.Game;
using VRage.Game.Definitions;

namespace Si.UtilityAI
{
    public enum SiNpcWeaponSlot
    {
        None,
        MainFirearm,
        AtFirearm,
        Handgun,
        Melee,
    }

    public enum SiNpcWeaponKind
    {
        Unknown,
        Ranged,
        Melee,
    }

    public enum SiNpcWeaponTargetDomain
    {
        Any,
        InfantryOnly,
        VehicleOnly,
    }

    internal static class SiNpcWeaponSlotExtensions
    {
        internal static SiNpcWeaponTargetDomain TargetDomain(this SiNpcWeaponSlot slot)
        {
            switch (slot)
            {
                case SiNpcWeaponSlot.AtFirearm:
                    return SiNpcWeaponTargetDomain.VehicleOnly;
                case SiNpcWeaponSlot.MainFirearm:
                case SiNpcWeaponSlot.Handgun:
                case SiNpcWeaponSlot.Melee:
                    return SiNpcWeaponTargetDomain.InfantryOnly;
                default:
                    return SiNpcWeaponTargetDomain.Any;
            }
        }

        internal static bool TryParse(string value, out SiNpcWeaponSlot slot)
        {
            slot = SiNpcWeaponSlot.None;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var trimmed = value.Trim();
            if (string.Equals(trimmed, "AT", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "ATFirearm", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "AntiVehicle", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "AntiVehicleFirearm", StringComparison.OrdinalIgnoreCase))
            {
                slot = SiNpcWeaponSlot.AtFirearm;
                return true;
            }

            if (!Enum.TryParse(trimmed, true, out slot))
                return false;

            return slot != SiNpcWeaponSlot.None;
        }
    }

    public sealed class SiNpcWeaponSlotBindingDefinition
    {
        public SiNpcWeaponSlotBindingDefinition(
            SiNpcWeaponSlot slot,
            MyDefinitionId weaponDefinitionId,
            MyDefinitionId? shootBehaviorDefinitionId)
        {
            Slot = slot;
            WeaponDefinitionId = weaponDefinitionId;
            ShootBehaviorDefinitionId = shootBehaviorDefinitionId;
        }

        public SiNpcWeaponSlot Slot { get; }
        public MyDefinitionId WeaponDefinitionId { get; }
        public MyDefinitionId? ShootBehaviorDefinitionId { get; }
        public SiNpcWeaponTargetDomain TargetDomain => Slot.TargetDomain();

        public SiNpcWeaponKind ResolveWeaponKind()
        {
            if (TryResolveRangedDefinition(out _))
                return SiNpcWeaponKind.Ranged;
            if (TryResolveMeleeDefinition(out _))
                return SiNpcWeaponKind.Melee;
            return SiNpcWeaponKind.Unknown;
        }

        public bool TryResolveRangedDefinition(out SiNpcRangedWeaponComponentDefinition definition) =>
            TryResolveDefinition(WeaponDefinitionId, out definition);

        public bool TryResolveMeleeDefinition(out SiNpcMeleeWeaponComponentDefinition definition) =>
            TryResolveDefinition(WeaponDefinitionId, out definition);

        private static bool TryResolveDefinition<TDefinition>(
            MyDefinitionId definitionId,
            out TDefinition definition)
            where TDefinition : MyDefinitionBase
        {
            if (MyDefinitionManager.TryGet(definitionId, out definition) && definition != null)
                return true;

            var subtype = definitionId.SubtypeName;
            if (string.IsNullOrWhiteSpace(subtype))
                return false;

            foreach (var candidate in MyDefinitionManager.GetOfType<TDefinition>())
                if (string.Equals(candidate?.Id.SubtypeName, subtype, StringComparison.OrdinalIgnoreCase))
                {
                    definition = candidate;
                    return true;
                }

            definition = null;
            return false;
        }
    }
}
