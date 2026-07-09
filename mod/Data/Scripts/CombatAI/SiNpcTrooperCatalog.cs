using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.ObjectBuilders;
using VRage.ObjectBuilders;

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
        public SiNpcTrooperWeaponBindingDefinition WeaponBindings;
        public bool IsParatrooper;
        public SerializableDefinitionId? Uniform;
    }

    internal static class SiNpcTrooperCatalog
    {
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

            var requestedSubtype = webbingSubtype.Trim();
            if (!TryBuildLoadout(requestedSubtype, out loadout))
                return false;

            if (preferParatrooper
                && loadout.CompatibilityDefinition != null
                && loadout.CompatibilityDefinition.ParatrooperVariant.HasValue
                && TryBuildLoadout(loadout.CompatibilityDefinition.ParatrooperVariant.Value.SubtypeId, out var paratrooperLoadout)
                && paratrooperLoadout != null)
            {
                loadout = paratrooperLoadout;
            }

            resolvedWebbingSubtype = loadout.SubtypeName;
            return true;
        }

        internal static bool TryGetLoadout(string webbingSubtype, out SiNpcLoadoutComponentDefinition definition)
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

        internal static bool TryGetWeaponBinding(string webbingSubtype, out SiNpcTrooperWeaponBindingDefinition definition)
        {
            definition = null;
            if (string.IsNullOrWhiteSpace(webbingSubtype))
                return false;

            var id = new MyDefinitionId(typeof(MyObjectBuilder_SiNpcTrooperWeaponBindingDefinition), webbingSubtype.Trim());
            if (MyDefinitionManager.TryGet(id, out definition))
                return definition != null;

            foreach (var candidate in MyDefinitionManager.GetOfType<SiNpcTrooperWeaponBindingDefinition>())
                if (string.Equals(candidate?.Id.SubtypeName, webbingSubtype, StringComparison.OrdinalIgnoreCase))
                {
                    definition = candidate;
                    return true;
                }

            return false;
        }

        internal static SerializableDefinitionId? ResolveUniform(string webbingSubtype, bool paratrooper)
        {
            if (TryResolveLoadout(webbingSubtype, paratrooper, out _, out var loadout)
                && loadout != null
                && loadout.Uniform.HasValue)
                return loadout.Uniform;

            return GuessUniform(webbingSubtype, paratrooper);
        }

        internal static bool IsParatrooperWebbing(string webbingSubtype)
        {
            return TryBuildLoadout(webbingSubtype, out var loadout)
                   && loadout != null
                   && loadout.IsParatrooper;
        }

        internal static List<string> GetKnownWebbings()
        {
            var webbings = new List<string>();
            foreach (var loadout in MyDefinitionManager.GetOfType<SiNpcLoadoutComponentDefinition>())
            {
                if (loadout == null
                    || string.IsNullOrWhiteSpace(loadout.Id.SubtypeName)
                    || !loadout.Webbing.HasValue)
                    continue;

                if (!TryBuildLoadout(loadout.Id.SubtypeName, out _))
                    continue;

                webbings.Add(loadout.Id.SubtypeName);
            }

            webbings.Sort(StringComparer.OrdinalIgnoreCase);
            return webbings;
        }

        private static bool TryBuildLoadout(string webbingSubtype, out SiTrooperLoadout loadout)
        {
            loadout = null;
            if (!TryGetLoadout(webbingSubtype, out var definition)
                || definition == null
                || !definition.Webbing.HasValue)
                return false;

            var webbingId = (MyDefinitionId)definition.Webbing.Value;
            if (!MyDefinitionManager.TryGet(webbingId, out MyContainerDefinition webbingContainer)
                || webbingContainer == null)
                return false;

            if (!TryGetWeaponBinding(definition.Id.SubtypeName, out var weaponBinding)
                || weaponBinding == null
                || !weaponBinding.TryGetSlot(SiNpcWeaponSlot.MainFirearm, out var mainFirearm)
                || !mainFirearm.TryResolveRangedDefinition(out _))
                return false;

            loadout = new SiTrooperLoadout
            {
                SubtypeName = definition.Id.SubtypeName,
                WebbingItemId = webbingId,
                CompatibilityDefinition = definition,
                WeaponBindings = weaponBinding,
                IsParatrooper = definition.IsParatrooper,
                Uniform = definition.Uniform.HasValue ? definition.Uniform : null,
            };
            return true;
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
    }
}
