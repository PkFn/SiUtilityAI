using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Medieval.Constants;
using Pax.Equipment;
using Sandbox.Entities.Components;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Game.Entity;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.ObjectBuilders;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Inventory;
using VRage.Network;
using VRage.ObjectBuilders.Inventory;
using VRage.ObjectBuilders;

namespace Si.UtilityAI
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiNpcUniformComponent : MyObjectBuilder_EntityComponent
    {
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiNpcUniformComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public SerializableDefinitionId? Uniform;
        public string DefaultHelmet;
        public string DefaultBackpack;
        [XmlArrayItem("Helmet")]
        public List<string> Helmets = new List<string>();
        [XmlArrayItem("Uniform")]
        public List<string> UniformMatch = new List<string>();
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiNpcUniformComponentDefinition))]
    public class SiNpcUniformComponentDefinition : MyEntityComponentDefinition
    {
        public SerializableDefinitionId? Uniform { get; private set; }
        public string DefaultHelmet { get; private set; }
        public string DefaultBackpack { get; private set; }
        public IReadOnlyDictionary<string, string> HelmetsByUniformTexture { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiNpcUniformComponentDefinition)builder;
            Uniform = ob.Uniform;

            var helmetsByUniformTexture = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (ob.Helmets != null
                && ob.UniformMatch != null
                && ob.Helmets.Count == ob.UniformMatch.Count)
            {
                for (var i = 0; i < ob.Helmets.Count; i++)
                {
                    var texture = ob.UniformMatch[i];
                    var helmet = ob.Helmets[i];
                    if (string.IsNullOrWhiteSpace(texture)
                        || string.IsNullOrWhiteSpace(helmet)
                        || helmetsByUniformTexture.ContainsKey(texture))
                        continue;

                    helmetsByUniformTexture.Add(texture, helmet);
                }
            }

            DefaultHelmet = string.IsNullOrWhiteSpace(ob.DefaultHelmet) ? null : ob.DefaultHelmet;
            DefaultBackpack = string.IsNullOrWhiteSpace(ob.DefaultBackpack) ? null : ob.DefaultBackpack;
            HelmetsByUniformTexture = helmetsByUniformTexture;
        }
    }

    [MyComponent(typeof(MyObjectBuilder_SiNpcUniformComponent))]
    [MyDefinitionRequired(typeof(SiNpcUniformComponentDefinition))]
    public class SiNpcUniformComponent : MyEntityComponent
    {
        private const long UniformApplicationDelayMilliseconds = 200;

        private SiNpcUniformComponentDefinition _definition;
        private SiNpcUniformComponentDefinition _runtimeDefinition;

        public override bool IsSerialized => false;
        public string CurrentUniformSubtype => ActiveDefinition?.Id.SubtypeName;

        private SiNpcUniformComponentDefinition ActiveDefinition => _runtimeDefinition ?? _definition;

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            _definition = (SiNpcUniformComponentDefinition)definition;
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();

            if (MyAPIGateway.Multiplayer != null && !MyAPIGateway.Multiplayer.IsServer)
                return;

            AddScheduledCallback(ApplyUniform, UniformApplicationDelayMilliseconds);
        }

        internal bool ApplyRuntimeDefinition(MyDefinitionId definitionId)
        {
            SiNpcUniformComponentDefinition runtimeDefinition;
            if (!MyDefinitionManager.TryGet(definitionId, out runtimeDefinition) || runtimeDefinition == null)
                return false;

            _runtimeDefinition = runtimeDefinition;
            if (Entity != null && Entity.InScene)
                AddScheduledCallback(ApplyUniform, UniformApplicationDelayMilliseconds);
            return true;
        }

        [Update(false)]
        private void ApplyUniform(long delta)
        {
            var definition = ActiveDefinition;
            if (Entity == null || Entity.Closed || Entity.MarkedForClose || definition == null || !definition.Uniform.HasValue)
                return;

            var uniformEquipment = Entity.Components.Get<MyPAX_CharacterUniformEquipment>();
            if (uniformEquipment == null)
                return;

            MyPAX_UniformEquipmentDefinition uniformDefinition;
            if (!MyDefinitionManager.TryGet(definition.Uniform.Value, out uniformDefinition)
                || uniformDefinition == null
                || string.IsNullOrWhiteSpace(uniformDefinition.Material))
                return;

            if (uniformEquipment.IsEquipped(uniformDefinition.Material))
            {
                EquipConfiguredHelmet(uniformDefinition);
                EquipConfiguredBackpack();
                return;
            }

            uniformEquipment.EquipMaterial(
                uniformDefinition.Material,
                uniformDefinition.ColorMetal,
                uniformDefinition.NormalGloss,
                uniformDefinition.AddOrAlpha,
                uniformDefinition.OriginalColorMetal,
                uniformDefinition.OriginalNormalGloss,
                uniformDefinition.OriginalAddOrAlpha,
                uniformDefinition.IsAlpha,
                uniformDefinition.RequiredCharacter);

            EquipConfiguredHelmet(uniformDefinition);
            EquipConfiguredBackpack();
        }

        private void EquipConfiguredHelmet(MyPAX_UniformEquipmentDefinition uniformDefinition)
        {
            var helmetSubtype = ResolveHelmetSubtype(ActiveDefinition, uniformDefinition);
            if (string.IsNullOrWhiteSpace(helmetSubtype))
                return;

            EnsureEquipmentItemEquipped(helmetSubtype);
        }

        private void EquipConfiguredBackpack()
        {
            var definition = ActiveDefinition;
            if (string.IsNullOrWhiteSpace(definition?.DefaultBackpack))
                return;

            EnsureEquipmentItemEquipped(definition.DefaultBackpack);
        }

        private void EnsureEquipmentItemEquipped(string equipmentSubtype)
        {
            if (SiNpcEquipmentHelper.HasEquippedSubtype(Entity, equipmentSubtype))
                return;

            string failure;
            SiNpcEquipmentHelper.TryEnsureEquipmentItemEquipped(
                Entity,
                new MyDefinitionId(typeof(MyObjectBuilder_EquipmentItem), equipmentSubtype),
                out failure);
        }

        private static string ResolveHelmetSubtype(
            SiNpcUniformComponentDefinition definition,
            MyPAX_UniformEquipmentDefinition uniformDefinition)
        {
            if (definition?.HelmetsByUniformTexture != null
                && uniformDefinition != null
                && !string.IsNullOrWhiteSpace(uniformDefinition.ColorMetal))
            {
                foreach (var pair in definition.HelmetsByUniformTexture)
                {
                    if (uniformDefinition.ColorMetal.IndexOf(pair.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                        return pair.Value;
                }
            }

            return definition?.DefaultHelmet;
        }
    }
}
