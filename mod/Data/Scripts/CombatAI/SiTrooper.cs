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
}
