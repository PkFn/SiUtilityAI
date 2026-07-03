using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;

namespace Si.UtilityAI
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiNpcTrooperWeaponBindingDefinition : MyObjectBuilder_DefinitionBase
    {
        public SerializableDefinitionId? Weapon;
        public SerializableDefinitionId? ShootBehavior;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiNpcTrooperWeaponBindingDefinition))]
    public class SiNpcTrooperWeaponBindingDefinition : MyDefinitionBase
    {
        public SerializableDefinitionId? Weapon { get; private set; }
        public SerializableDefinitionId? ShootBehavior { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiNpcTrooperWeaponBindingDefinition)builder;
            Weapon = ob.Weapon;
            ShootBehavior = ob.ShootBehavior;
        }
    }

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

    internal static class SiNpcTrooperCatalog
    {
        internal static bool TryGetLoadout(string webbingSubtype, out SiNpcLoadoutComponentDefinition definition)
        {
            definition = null;
            if (string.IsNullOrWhiteSpace(webbingSubtype))
                return false;

            var id = new MyDefinitionId(typeof(MyObjectBuilder_SiNpcLoadoutComponent), webbingSubtype.Trim());
            if (MyDefinitionManager.TryGet(id, out definition))
                return definition != null;

            foreach (var candidate in MyDefinitionManager.GetOfType<SiNpcLoadoutComponentDefinition>())
                if (string.Equals(candidate.Id.SubtypeName, webbingSubtype, StringComparison.OrdinalIgnoreCase))
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
                if (string.Equals(candidate.Id.SubtypeName, webbingSubtype, StringComparison.OrdinalIgnoreCase))
                {
                    definition = candidate;
                    return true;
                }

            return false;
        }

        internal static SerializableDefinitionId? GuessUniform(string webbingSubtype, bool paratrooper)
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

        internal static List<string> GetKnownWebbings()
        {
            var webbings = new List<string>();
            foreach (var binding in MyDefinitionManager.GetOfType<SiNpcTrooperWeaponBindingDefinition>())
            {
                if (binding == null
                    || string.IsNullOrWhiteSpace(binding.Id.SubtypeName)
                    || !TryGetLoadout(binding.Id.SubtypeName, out _))
                    continue;

                webbings.Add(binding.Id.SubtypeName);
            }

            webbings.Sort(StringComparer.OrdinalIgnoreCase);
            return webbings;
        }
    }
}
