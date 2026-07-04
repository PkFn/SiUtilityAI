using System;
using System.Xml.Serialization;
using Sandbox.Definitions.Equipment;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.Inventory;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;
using VRageMath;

namespace Pax.Cannons
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_PAX_ThrowableItemDefinition : MyObjectBuilder_DefinitionBase
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_PAX_ThrowableItemDefinition))]
    public class MyPAX_ThrowableItemDefinition : MyDefinitionBase
    {
        public string ThrowItemId { get; set; }
        public float ThrowPower { get; set; }
        public float FuseTime { get; set; }
        public bool DisableSprinting { get; set; }
        public float PreparationTime { get; set; }
    }

    public class MyPAX_ThrowableItem : MyHandItemBehaviorBase
    {
        public override float TargetingDistance => -1;

        public override bool SetSecondary(MyHandItem secondaryItem, MyHandItemBehaviorDefinition secondaryDefinition)
        {
            return false;
        }

        public override bool SetTarget()
        {
            return true;
        }

        public override StartActionResponse StartAction(MyHandItemActionEnum action)
        {
            return StartActionResponse.Handled;
        }

        public override void EndAction(MyHandItemActionEnum action)
        {
        }

        public static long Throw(Vector3D pos, Vector3 forward, Vector3 up, Vector4 velocityFuse, string throwItemId, long entityId, long holderId)
        {
            throw new NotSupportedException("Compile-only PAX API stub was executed. Load the real PAX core mod in game.");
        }
    }
}
