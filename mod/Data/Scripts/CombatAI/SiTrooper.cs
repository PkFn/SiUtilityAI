using VRage.Game;
using VRage.ObjectBuilders;
using VRageMath;

namespace Si.UtilityAI
{
    /// <summary>
    /// A visible grounded soldier.  Its utility brain and available behaviors
    /// are selected entirely by components attached to its entity definition.
    /// </summary>
    public sealed class SiTrooperNpc : SiGroundedNpc
    {
        private static readonly MyDefinitionId DefinitionId =
            new MyDefinitionId(typeof(MyObjectBuilder_EntityBase), "SiTrooper");

        public SiTrooperNpc(long entityId, in MatrixD transform)
            : base(entityId, transform)
        {
        }

        public override string Archetype => SiNpcManager.SoldierArchetype;
        protected override MyDefinitionId EntityDefinition => DefinitionId;
    }

    /// <summary>
    /// A hostile test soldier using the PAX defenders German rifle model.
    /// Its combat behavior is selected by UtilityAI components on the entity.
    /// </summary>
    public sealed class SiEnemyTrooperNpc : SiGroundedNpc
    {
        private static readonly MyDefinitionId DefinitionId =
            new MyDefinitionId(typeof(MyObjectBuilder_EntityBase), "SiEnemyTrooper");

        public SiEnemyTrooperNpc(long entityId, in MatrixD transform)
            : base(entityId, transform)
        {
        }

        public override string Archetype => SiNpcManager.EnemyTrooperArchetype;
        protected override MyDefinitionId EntityDefinition => DefinitionId;
    }
}
