using System;
using System.Xml.Serialization;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Components.Interfaces;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRage.Utils;
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
    public class MyObjectBuilder_SiNpcDamageComponent : MyObjectBuilder_EntityComponent
    {
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiNpcDamageComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public float MaximumHealth;
        public float DamageMultiplier;
        public float ProjectileDamageMultiplier;
        public long DeathRemovalMilliseconds;
        public double DeathInitialHorizontalSpeed;
        public double DeathInitialDownwardSpeed;
        public double DeathGravityMultiplier;
        public double DeathHorizontalVelocityMultiplierPerSecond;
        public double DeathMaximumFallSpeed;
        public double DeathPitchSpeedDegreesPerSecond;
        public double DeathRollSpeedDegreesPerSecond;
        public double DeathRestAngleDegrees;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiNpcDamageComponentDefinition))]
    public class SiNpcDamageComponentDefinition : MyEntityComponentDefinition
    {
        public float MaximumHealth { get; private set; }
        public float DamageMultiplier { get; private set; }
        public float ProjectileDamageMultiplier { get; private set; }
        public long DeathRemovalMilliseconds { get; private set; }
        public double DeathInitialHorizontalSpeed { get; private set; }
        public double DeathInitialDownwardSpeed { get; private set; }
        public double DeathGravityMultiplier { get; private set; }
        public double DeathHorizontalVelocityMultiplierPerSecond { get; private set; }
        public double DeathMaximumFallSpeed { get; private set; }
        public double DeathPitchSpeedDegreesPerSecond { get; private set; }
        public double DeathRollSpeedDegreesPerSecond { get; private set; }
        public double DeathRestAngleDegrees { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiNpcDamageComponentDefinition)builder;
            MaximumHealth = Math.Max(0, ob.MaximumHealth);
            DamageMultiplier = Math.Max(0, ob.DamageMultiplier);
            ProjectileDamageMultiplier = Math.Max(0, ob.ProjectileDamageMultiplier);
            DeathRemovalMilliseconds = Math.Max(0, ob.DeathRemovalMilliseconds);
            DeathInitialHorizontalSpeed = Math.Max(0, ob.DeathInitialHorizontalSpeed);
            DeathInitialDownwardSpeed = Math.Max(0, ob.DeathInitialDownwardSpeed);
            DeathGravityMultiplier = Math.Max(0, ob.DeathGravityMultiplier);
            DeathHorizontalVelocityMultiplierPerSecond = Math.Max(
                0,
                ob.DeathHorizontalVelocityMultiplierPerSecond);
            DeathMaximumFallSpeed = Math.Max(0, ob.DeathMaximumFallSpeed);
            DeathPitchSpeedDegreesPerSecond = Math.Max(0, ob.DeathPitchSpeedDegreesPerSecond);
            DeathRollSpeedDegreesPerSecond = Math.Max(0, ob.DeathRollSpeedDegreesPerSecond);
            DeathRestAngleDegrees = Math.Max(0, ob.DeathRestAngleDegrees);
        }
    }

    [MyComponent(typeof(MyObjectBuilder_SiNpcDamageComponent))]
    [MyDefinitionRequired(typeof(SiNpcDamageComponentDefinition))]
    public class SiNpcDamageComponent : MyEntityComponent, IMyDamageReceiver
    {
        public SiNpcDamageComponentDefinition Definition { get; private set; }
        public float Health { get; private set; }
        public long DeadElapsedMilliseconds { get; private set; }

        public override bool IsSerialized => false;
        public bool IsDead => Definition != null && Health <= 0;
        public bool IsRemovalDue => IsDead && DeadElapsedMilliseconds >= Definition.DeathRemovalMilliseconds;

        public event DamageTakenDelegate DamageTaken;

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            Definition = (SiNpcDamageComponentDefinition)definition;
            Health = Definition.MaximumHealth;
            DeadElapsedMilliseconds = 0;
        }

        public bool DoDamage(MyDamageInformation damageInformation)
        {
            if (Definition == null)
                return false;

            DamageTaken?.Invoke(damageInformation);
            if (!IsAuthoritative)
                return true;

            if (IsDead)
                return true;

            var damage = EffectiveDamage(damageInformation);
            if (damage <= 0)
                return true;

            Health = Math.Max(0, Health - damage);
            if (Health <= 0)
                DeadElapsedMilliseconds = 0;
            return true;
        }

        public void AdvanceDeath(long elapsedMilliseconds)
        {
            if (!IsDead || elapsedMilliseconds <= 0)
                return;

            DeadElapsedMilliseconds += elapsedMilliseconds;
        }

        private float EffectiveDamage(MyDamageInformation damageInformation)
        {
            var amount = Math.Max(0, damageInformation.Amount);
            var multiplier = Definition.DamageMultiplier;
            if (IsProjectileDamage(damageInformation.Type))
                multiplier *= Definition.ProjectileDamageMultiplier;
            return amount * multiplier;
        }

        private static bool IsProjectileDamage(MyStringHash damageType) =>
            damageType == MyDamageType.Bolt
            || damageType == MyDamageType.Bullet
            || damageType == MyDamageType.Destruction
            || damageType == MyDamageType.Weapon;

        private static bool IsAuthoritative =>
            MyMultiplayerModApi.Static == null || MyMultiplayerModApi.Static.IsServer;
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
