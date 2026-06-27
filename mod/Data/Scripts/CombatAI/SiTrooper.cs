using System;
using System.Xml.Serialization;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRageMath;

namespace Si.UtilityAI
{
    public enum SiNpcSpawnDisposition
    {
        PlayerCommanded,
        HostileToSpawner,
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiNpcArchetypeComponent : MyObjectBuilder_EntityComponent
    {
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiNpcArchetypeComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public string Archetype;
        public SiNpcSpawnDisposition SpawnDisposition;
        public bool HiddenFromCommands;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiNpcArchetypeComponentDefinition))]
    public class SiNpcArchetypeComponentDefinition : MyEntityComponentDefinition
    {
        public string Archetype { get; private set; }
        public SiNpcSpawnDisposition SpawnDisposition { get; private set; }
        public bool HiddenFromCommands { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiNpcArchetypeComponentDefinition)builder;
            Archetype = TrimOrNull(ob.Archetype);
            SpawnDisposition = ob.SpawnDisposition;
            HiddenFromCommands = ob.HiddenFromCommands;
        }

        private static string TrimOrNull(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            return value.Trim();
        }
    }

    [MyComponent(typeof(MyObjectBuilder_SiNpcArchetypeComponent))]
    [MyDefinitionRequired(typeof(SiNpcArchetypeComponentDefinition))]
    public class SiNpcArchetypeComponent : MyEntityComponent
    {
        public SiNpcArchetypeComponentDefinition Definition { get; private set; }

        public override bool IsSerialized => false;

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            Definition = (SiNpcArchetypeComponentDefinition)definition;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiNpcLifecycleComponent : MyObjectBuilder_EntityComponent
    {
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiNpcLifecycleComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public long DeathRemovalMilliseconds;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiNpcLifecycleComponentDefinition))]
    public class SiNpcLifecycleComponentDefinition : MyEntityComponentDefinition
    {
        public long DeathRemovalMilliseconds { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiNpcLifecycleComponentDefinition)builder;
            DeathRemovalMilliseconds = Math.Max(0, ob.DeathRemovalMilliseconds);
        }
    }

    [MyComponent(typeof(MyObjectBuilder_SiNpcLifecycleComponent))]
    [MyDefinitionRequired(typeof(SiNpcLifecycleComponentDefinition))]
    public class SiNpcLifecycleComponent : MyEntityComponent
    {
        public SiNpcLifecycleComponentDefinition Definition { get; private set; }

        public override bool IsSerialized => false;

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            Definition = (SiNpcLifecycleComponentDefinition)definition;
        }
    }

    internal sealed class SiNpcArchetypeRecord
    {
        public SiNpcArchetypeRecord(
            string archetype,
            MyDefinitionId entityDefinition,
            SiNpcArchetypeComponentDefinition componentDefinition)
        {
            if (string.IsNullOrWhiteSpace(archetype))
                throw new ArgumentException("An NPC archetype needs a name.", nameof(archetype));
            if (componentDefinition == null)
                throw new ArgumentNullException(nameof(componentDefinition));

            Archetype = archetype.Trim();
            EntityDefinition = entityDefinition;
            ComponentDefinition = componentDefinition;
        }

        public string Archetype { get; }
        public MyDefinitionId EntityDefinition { get; }
        public SiNpcArchetypeComponentDefinition ComponentDefinition { get; }
        public SiNpcSpawnDisposition SpawnDisposition => ComponentDefinition.SpawnDisposition;
        public bool HiddenFromCommands => ComponentDefinition.HiddenFromCommands;
    }

    /// <summary>
    /// A visible grounded NPC whose entity, brain, movement, and behaviors all
    /// come from the entity container definition selected by data.
    /// </summary>
    internal sealed class SiDataDrivenNpc : SiGroundedNpc
    {
        private readonly SiNpcArchetypeRecord _definition;

        public SiDataDrivenNpc(SiNpcArchetypeRecord definition, long entityId, in MatrixD transform)
            : base(entityId, transform)
        {
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        public override string Archetype => _definition.Archetype;
        protected override MyDefinitionId EntityDefinition => _definition.EntityDefinition;
    }
}
