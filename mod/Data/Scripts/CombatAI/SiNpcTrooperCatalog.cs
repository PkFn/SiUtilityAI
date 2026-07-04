using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Pax.Cannons;
using Pax.Equipment;
using Sandbox.Definitions.Equipment;
using Sandbox.Definitions.Inventory;
using Sandbox.Game.EntityComponents;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.ObjectBuilders;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Inventory;

namespace Si.UtilityAI
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiNpcUniformGuessDefinition : MyObjectBuilder_DefinitionBase
    {
        public string MatchToken;
        public SerializableDefinitionId? RegularUniform;
        public SerializableDefinitionId? ParatrooperUniform;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiNpcUniformGuessDefinition))]
    public class SiNpcUniformGuessDefinition : MyDefinitionBase
    {
        public string MatchToken { get; private set; }
        public SerializableDefinitionId? RegularUniform { get; private set; }
        public SerializableDefinitionId? ParatrooperUniform { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiNpcUniformGuessDefinition)builder;
            MatchToken = string.IsNullOrWhiteSpace(ob.MatchToken) ? null : ob.MatchToken.Trim();
            RegularUniform = ob.RegularUniform;
            ParatrooperUniform = ob.ParatrooperUniform;
        }
    }

    internal sealed class SiTrooperLoadout
    {
        public string SubtypeName;
        public MyDefinitionId WebbingItemId;
        public SiNpcLoadoutComponentDefinition CompatibilityDefinition;
        public MyPAX_ItemStorageEquipmentDefinition StorageDefinition;
        public MyDefinitionId PrimaryWeaponItemId;
        public MyDefinitionId WeaponBehaviorId;
        public bool IsParatrooper;
        public SerializableDefinitionId? Uniform;
    }

    internal static class SiNpcTrooperCatalog
    {
        private static readonly MyDefinitionId RifleWeaponBalanceId =
            new MyDefinitionId(typeof(MyObjectBuilder_SiNpcRangedWeaponBalanceDefinition), "SiRifleTrooperSharedWeaponBalance");
        private static readonly MyDefinitionId SmgWeaponBalanceId =
            new MyDefinitionId(typeof(MyObjectBuilder_SiNpcRangedWeaponBalanceDefinition), "SiSmgTrooperSharedWeaponBalance");
        private static readonly MyDefinitionId RifleShootBalanceId =
            new MyDefinitionId(typeof(MyObjectBuilder_SiShootOpposingNpcBehaviorBalanceDefinition), "SiRifleTrooperSharedBalance");
        private static readonly MyDefinitionId SmgShootBalanceId =
            new MyDefinitionId(typeof(MyObjectBuilder_SiShootOpposingNpcBehaviorBalanceDefinition), "SiSmgTrooperSharedBalance");

        internal static bool TryResolveLoadout(
            string webbingSubtype,
            bool preferParatrooper,
            out string resolvedWebbingSubtype,
            out SiTrooperLoadout loadout)
        {
            resolvedWebbingSubtype = null;
            loadout = null;

            if (string.IsNullOrWhiteSpace(webbingSubtype))
                return false;

            if (!TryGetDiscoveredLoadout(webbingSubtype.Trim(), out loadout))
                return false;

            if (preferParatrooper
                && loadout.CompatibilityDefinition != null
                && loadout.CompatibilityDefinition.ParatrooperVariant.HasValue
                && TryGetDiscoveredLoadout(loadout.CompatibilityDefinition.ParatrooperVariant.Value.SubtypeId, out var variant)
                && variant != null)
            {
                loadout = variant;
            }

            resolvedWebbingSubtype = loadout.SubtypeName;
            return true;
        }

        internal static bool TryCreateWeaponDefinition(
            SiTrooperLoadout loadout,
            out SiNpcRangedWeaponComponentDefinition runtimeDefinition)
        {
            runtimeDefinition = null;
            if (loadout == null)
                return false;

            MyPAX_HandheldGunDefinition behaviorDefinition;
            if (!TryGetPaxGunBehaviorDefinition(loadout.PrimaryWeaponItemId, out behaviorDefinition) || behaviorDefinition == null)
                return false;

            var balanceId = SelectWeaponBalanceId(behaviorDefinition);
            var builder = new MyObjectBuilder_SiNpcRangedWeaponComponentDefinition
            {
                Id = new MyDefinitionId(typeof(MyObjectBuilder_SiNpcRangedWeaponComponent), "Dynamic_" + loadout.SubtypeName),
                Balance = balanceId,
                HeldItem = loadout.PrimaryWeaponItemId,
                WeaponBehavior = behaviorDefinition.Id,
            };

            runtimeDefinition = RuntimeSiNpcRangedWeaponComponentDefinition.Create(builder);
            return runtimeDefinition != null;
        }

        internal static bool TryCreateShootBehaviorDefinition(
            SiTrooperLoadout loadout,
            out SiShootOpposingNpcBehaviorDefinition runtimeDefinition)
        {
            runtimeDefinition = null;
            if (loadout == null)
                return false;

            MyPAX_HandheldGunDefinition behaviorDefinition;
            if (!TryGetPaxGunBehaviorDefinition(loadout.PrimaryWeaponItemId, out behaviorDefinition) || behaviorDefinition == null)
                return false;

            var balanceId = SelectShootBehaviorBalanceId(behaviorDefinition);
            var builder = new MyObjectBuilder_SiShootOpposingNpcBehaviorDefinition
            {
                Id = new MyDefinitionId(typeof(MyObjectBuilder_SiShootOpposingNpcBehavior), "Dynamic_" + loadout.SubtypeName),
                Balance = balanceId,
            };

            runtimeDefinition = RuntimeSiShootOpposingNpcBehaviorDefinition.Create(builder);
            return runtimeDefinition != null;
        }

        internal static SerializableDefinitionId? ResolveUniform(string webbingSubtype, bool preferParatrooper)
        {
            if (TryResolveLoadout(webbingSubtype, preferParatrooper, out _, out var loadout) && loadout != null)
            {
                if (loadout.Uniform.HasValue)
                    return loadout.Uniform;

                return GuessUniform(loadout.SubtypeName, loadout.IsParatrooper);
            }

            return GuessUniform(webbingSubtype, preferParatrooper);
        }

        internal static bool IsParatrooperWebbing(string webbingSubtype)
        {
            return TryGetDiscoveredLoadout(webbingSubtype, out var loadout)
                   && loadout != null
                   && loadout.IsParatrooper;
        }

        internal static List<string> GetKnownWebbings()
        {
            var webbings = new List<string>();
            foreach (var loadout in EnumerateTrooperLoadouts())
            {
                if (loadout == null || string.IsNullOrWhiteSpace(loadout.SubtypeName))
                    continue;

                webbings.Add(loadout.SubtypeName);
            }

            webbings.Sort(StringComparer.OrdinalIgnoreCase);
            return webbings;
        }

        private static bool TryGetDiscoveredLoadout(string webbingSubtype, out SiTrooperLoadout loadout)
        {
            loadout = null;
            if (string.IsNullOrWhiteSpace(webbingSubtype))
                return false;

            foreach (var candidate in EnumerateTrooperLoadouts())
            {
                if (candidate == null)
                    continue;

                if (!string.Equals(candidate.SubtypeName, webbingSubtype, StringComparison.OrdinalIgnoreCase))
                    continue;

                loadout = candidate;
                return true;
            }

            return false;
        }

        private static IEnumerable<SiTrooperLoadout> EnumerateTrooperLoadouts()
        {
            foreach (var storageDefinition in MyDefinitionManager.GetOfType<MyPAX_ItemStorageEquipmentDefinition>())
            {
                if (storageDefinition == null || string.IsNullOrWhiteSpace(storageDefinition.Id.SubtypeName))
                    continue;

                if (!TryResolvePrimaryWeapon(storageDefinition, out var primaryWeaponItemId, out var behaviorDefinition)
                    || behaviorDefinition == null)
                    continue;

                SiNpcLoadoutComponentDefinition compatibilityDefinition;
                TryGetCompatibilityLoadout(storageDefinition.Id.SubtypeName, out compatibilityDefinition);

                yield return new SiTrooperLoadout
                {
                    SubtypeName = storageDefinition.Id.SubtypeName,
                    WebbingItemId = new MyDefinitionId(typeof(MyObjectBuilder_EquipmentItem), storageDefinition.Id.SubtypeName),
                    CompatibilityDefinition = compatibilityDefinition,
                    StorageDefinition = storageDefinition,
                    PrimaryWeaponItemId = primaryWeaponItemId,
                    WeaponBehaviorId = behaviorDefinition.Id,
                    IsParatrooper = HasParachute(storageDefinition, compatibilityDefinition),
                    Uniform = compatibilityDefinition != null && compatibilityDefinition.Uniform.HasValue
                        ? compatibilityDefinition.Uniform
                        : null,
                };
            }
        }

        private static bool TryResolvePrimaryWeapon(
            MyPAX_ItemStorageEquipmentDefinition storageDefinition,
            out MyDefinitionId primaryWeaponItemId,
            out MyPAX_HandheldGunDefinition behaviorDefinition)
        {
            primaryWeaponItemId = default(MyDefinitionId);
            behaviorDefinition = null;
            if (storageDefinition?.Items == null)
                return false;

            foreach (var item in storageDefinition.Items)
            {
                if (!TryGetPaxGunBehaviorDefinition(item.Key, out behaviorDefinition) || behaviorDefinition == null)
                    continue;

                primaryWeaponItemId = item.Key;
                return true;
            }

            return false;
        }

        private static bool TryGetPaxGunBehaviorDefinition(
            MyDefinitionId itemId,
            out MyPAX_HandheldGunDefinition behaviorDefinition)
        {
            behaviorDefinition = null;
            if (!string.Equals(itemId.TypeId.ToString(), "MyObjectBuilder_HandItemWithVariable", StringComparison.Ordinal))
                return false;

            var handItemDefinition = MyDefinitionManager.Get<MyHandItemDefinition>(itemId);
            if (handItemDefinition?.Behaviors == null)
                return false;

            foreach (var behavior in handItemDefinition.Behaviors)
            {
                if (behavior == null || behavior.Id.TypeId != typeof(MyObjectBuilder_PAX_HandheldGunDefinition))
                    continue;

                if (MyDefinitionManager.TryGet(behavior.Id, out behaviorDefinition) && behaviorDefinition != null)
                    return true;

                foreach (var candidate in MyDefinitionManager.GetOfType<MyPAX_HandheldGunDefinition>())
                    if (candidate != null
                        && string.Equals(candidate.Id.SubtypeName, behavior.Id.SubtypeName, StringComparison.OrdinalIgnoreCase))
                    {
                        behaviorDefinition = candidate;
                        return true;
                    }
            }

            return false;
        }

        private static bool TryGetCompatibilityLoadout(
            string webbingSubtype,
            out SiNpcLoadoutComponentDefinition definition)
        {
            definition = null;
            if (string.IsNullOrWhiteSpace(webbingSubtype))
                return false;

            var id = new MyDefinitionId(typeof(MyObjectBuilder_SiNpcLoadoutComponent), webbingSubtype.Trim());
            if (MyDefinitionManager.TryGet(id, out definition))
                return definition != null;

            foreach (var candidate in MyDefinitionManager.GetOfType<SiNpcLoadoutComponentDefinition>())
                if (string.Equals(candidate?.Id.SubtypeName, webbingSubtype, StringComparison.OrdinalIgnoreCase))
                {
                    definition = candidate;
                    return true;
                }

            return false;
        }

        private static bool HasParachute(
            MyPAX_ItemStorageEquipmentDefinition storageDefinition,
            SiNpcLoadoutComponentDefinition compatibilityDefinition)
        {
            if (compatibilityDefinition != null && compatibilityDefinition.Parachute.HasValue)
                return true;
            if (storageDefinition?.Items == null)
                return false;

            foreach (var item in storageDefinition.Items)
            {
                if (item.Key.TypeId != typeof(MyObjectBuilder_EquipmentItem))
                    continue;

                if (item.Key.SubtypeName.IndexOf("ParachuteBackpack", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static SerializableDefinitionId? GuessUniform(string webbingSubtype, bool paratrooper)
        {
            if (string.IsNullOrWhiteSpace(webbingSubtype))
                return null;

            SiNpcUniformGuessDefinition best = null;
            foreach (var candidate in MyDefinitionManager.GetOfType<SiNpcUniformGuessDefinition>())
            {
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.MatchToken))
                    continue;

                if (webbingSubtype.IndexOf(candidate.MatchToken, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (best == null || candidate.MatchToken.Length > best.MatchToken.Length)
                    best = candidate;
            }

            if (best == null)
                return null;

            return paratrooper
                ? (best.ParatrooperUniform ?? best.RegularUniform)
                : best.RegularUniform;
        }

        private static SerializableDefinitionId SelectWeaponBalanceId(MyPAX_HandheldGunDefinition behaviorDefinition)
        {
            if (behaviorDefinition == null)
                return RifleWeaponBalanceId;

            if (IsCompactAutomaticWeapon(behaviorDefinition))
                return SmgWeaponBalanceId;

            return RifleWeaponBalanceId;
        }

        private static SerializableDefinitionId SelectShootBehaviorBalanceId(MyPAX_HandheldGunDefinition behaviorDefinition)
        {
            if (behaviorDefinition == null)
                return RifleShootBalanceId;

            if (IsCompactAutomaticWeapon(behaviorDefinition))
                return SmgShootBalanceId;

            return RifleShootBalanceId;
        }

        private static bool IsCompactAutomaticWeapon(MyPAX_HandheldGunDefinition behaviorDefinition)
        {
            if (behaviorDefinition == null)
                return false;

            if (behaviorDefinition.TimeBetweenShots > 0 && behaviorDefinition.TimeBetweenShots <= 250)
                return true;

            var acceptedCartridges = behaviorDefinition.AcceptedCartridges;
            if (acceptedCartridges != null)
                for (var i = 0; i < acceptedCartridges.Length; i++)
                    if (!string.IsNullOrWhiteSpace(acceptedCartridges[i])
                        && acceptedCartridges[i].IndexOf("9mm", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;

            return false;
        }

        private sealed class RuntimeSiNpcRangedWeaponComponentDefinition : SiNpcRangedWeaponComponentDefinition
        {
            internal static SiNpcRangedWeaponComponentDefinition Create(MyObjectBuilder_SiNpcRangedWeaponComponentDefinition builder)
            {
                var definition = new RuntimeSiNpcRangedWeaponComponentDefinition();
                definition.Init(builder);
                return definition;
            }
        }

        private sealed class RuntimeSiShootOpposingNpcBehaviorDefinition : SiShootOpposingNpcBehaviorDefinition
        {
            internal static SiShootOpposingNpcBehaviorDefinition Create(MyObjectBuilder_SiShootOpposingNpcBehaviorDefinition builder)
            {
                var definition = new RuntimeSiShootOpposingNpcBehaviorDefinition();
                definition.Init(builder);
                return definition;
            }
        }
    }
}
