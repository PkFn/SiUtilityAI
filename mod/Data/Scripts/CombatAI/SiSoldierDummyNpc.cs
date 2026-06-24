using VRage.Game;
using VRage.ObjectBuilders;
using VRageMath;

namespace Si.UtilityAI
{
    /// <summary>
    /// First framework proof: a visible soldier using grounded waypoint locomotion.
    /// </summary>
    public sealed class SiSoldierDummyNpc : SiGroundedNpc
    {
        private static readonly MyDefinitionId DefinitionId =
            new MyDefinitionId(typeof(MyObjectBuilder_EntityBase), "SiSoldierDummy");

        public SiSoldierDummyNpc(long entityId, in MatrixD transform)
            : base(entityId, transform)
        {
        }

        public override string Archetype => SiNpcManager.SoldierDummyArchetype;
        protected override MyDefinitionId EntityDefinition => DefinitionId;
        protected override bool ModelFacesBackward => true;
    }
}
