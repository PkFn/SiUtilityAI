using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;

namespace Pax.Equipment
{
    public class MyPAX_CharacterUniformEquipment : MyEntityComponent
    {
        public bool IsEquipped(string material) => false;

        public void EquipMaterial(
            string material,
            string colorMetal,
            string normalGloss,
            string addOrAlpha,
            string originalColorMetal,
            string originalNormalGloss,
            string originalAddOrAlpha,
            bool isAlpha,
            string requiredModel)
        {
        }
    }

    public class MyPAX_UniformEquipmentDefinition : MyEntityComponentDefinition
    {
        public string RequiredCharacter { get; protected set; }
        public string RequiredCharacterName { get; protected set; }
        public string Name { get; protected set; }
        public string Material { get; protected set; }
        public string ColorMetal { get; protected set; }
        public string NormalGloss { get; protected set; }
        public string AddOrAlpha { get; protected set; }
        public string OriginalColorMetal { get; protected set; }
        public string OriginalNormalGloss { get; protected set; }
        public string OriginalAddOrAlpha { get; protected set; }
        public bool IsAlpha { get; protected set; }
    }
}
