using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Medieval.Constants;
using Pax.Equipment;
using Sandbox.Entities.Components;
using Sandbox.Game.Inventory;
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
            HelmetsByUniformTexture = helmetsByUniformTexture;
        }
    }

    [MyComponent(typeof(MyObjectBuilder_SiNpcUniformComponent))]
    [MyDefinitionRequired(typeof(SiNpcUniformComponentDefinition))]
    public class SiNpcUniformComponent : MyEntityComponent
    {
        private SiNpcUniformComponentDefinition _definition;

        public override bool IsSerialized => false;

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

            AddScheduledCallback(ApplyUniform, 16);
        }

        [Update(false)]
        private void ApplyUniform(long delta)
        {
            if (Entity == null || Entity.Closed || Entity.MarkedForClose || !_definition.Uniform.HasValue)
                return;

            var uniformEquipment = Entity.Components.Get<MyPAX_CharacterUniformEquipment>();
            if (uniformEquipment == null)
                return;

            MyPAX_UniformEquipmentDefinition uniformDefinition;
            if (!MyDefinitionManager.TryGet(_definition.Uniform.Value, out uniformDefinition)
                || uniformDefinition == null
                || string.IsNullOrWhiteSpace(uniformDefinition.Material))
                return;

            if (uniformEquipment.IsEquipped(uniformDefinition.Material))
            {
                EquipConfiguredHelmet(uniformDefinition);
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
        }

        private void EquipConfiguredHelmet(MyPAX_UniformEquipmentDefinition uniformDefinition)
        {
            var helmetSubtype = ResolveHelmetSubtype(uniformDefinition);
            if (string.IsNullOrWhiteSpace(helmetSubtype))
                return;

            var equipment = Entity.Components.Get<MyEntityEquipmentComponent>();
            if (equipment != null)
            {
                foreach (var equippedItem in equipment.EquippedItems)
                {
                    if (equippedItem is MyEquipmentItem item
                        && string.Equals(item.Subtype.String, helmetSubtype, StringComparison.OrdinalIgnoreCase))
                        return;
                }
            }

            var inventory = Entity.Components.Get<MyInventoryBase>(MyCharacterConstants.MainInventory);
            if (inventory == null)
                return;

            MyEquipmentItem helmetItem = null;
            foreach (var item in inventory.Items)
            {
                if (item is MyEquipmentItem equipmentItem
                    && string.Equals(equipmentItem.Subtype.String, helmetSubtype, StringComparison.OrdinalIgnoreCase))
                {
                    helmetItem = equipmentItem;
                    break;
                }
            }

            if (helmetItem == null)
            {
                if (!inventory.AddItems(new MyDefinitionId(typeof(MyObjectBuilder_EquipmentItem), helmetSubtype), 1, MyInventoryBase.NewItemParams.ForcedInsertion))
                    return;

                foreach (var item in inventory.Items)
                {
                    if (item is MyEquipmentItem equipmentItem
                        && string.Equals(equipmentItem.Subtype.String, helmetSubtype, StringComparison.OrdinalIgnoreCase))
                    {
                        helmetItem = equipmentItem;
                        break;
                    }
                }
            }

            if (helmetItem != null && equipment != null)
                equipment.EquipItem(helmetItem);
        }

        private string ResolveHelmetSubtype(MyPAX_UniformEquipmentDefinition uniformDefinition)
        {
            if (_definition.HelmetsByUniformTexture != null
                && uniformDefinition != null
                && !string.IsNullOrWhiteSpace(uniformDefinition.ColorMetal))
            {
                foreach (var pair in _definition.HelmetsByUniformTexture)
                {
                    if (uniformDefinition.ColorMetal.IndexOf(pair.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                        return pair.Value;
                }
            }

            return _definition.DefaultHelmet;
        }
    }
}
