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
        private readonly string _archetype;

        public SiDataDrivenNpc(SiNpcArchetypeRecord definition, long entityId, in MatrixD transform)
            : this(definition, definition?.Archetype, entityId, transform)
        {
        }

        public SiDataDrivenNpc(
            SiNpcArchetypeRecord definition,
            string archetype,
            long entityId,
            in MatrixD transform)
            : base(entityId, transform)
        {
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
            _archetype = string.IsNullOrWhiteSpace(archetype) ? _definition.Archetype : archetype.Trim();
        }

        public override string Archetype => _archetype;
        protected override MyDefinitionId EntityDefinition => _definition.EntityDefinition;

        public string WebbingSubtype { get; private set; }
        public bool IsParatrooperSpawn { get; private set; }
        public bool IsEnemySpawn { get; private set; }
        public bool IsMountedSpawn { get; private set; }

        internal void SetSpawnMetadata(
            string webbingSubtype,
            bool isParatrooper,
            bool isEnemy,
            bool isMounted = false)
        {
            WebbingSubtype = string.IsNullOrWhiteSpace(webbingSubtype) ? null : webbingSubtype.Trim();
            IsParatrooperSpawn = isParatrooper;
            IsEnemySpawn = isEnemy;
            IsMountedSpawn = isMounted;
        }
    }
}
