using System.Collections.Generic;
using System.Xml.Serialization;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity.EntityComponents;
using VRage.Game.ObjectBuilders;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;

namespace Pax.RangedDefenders
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_PAX_ShootingDefender : MyObjectBuilder_EntityComponent
    {
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_PAX_ShootingDefenderDefinition : VRage.Game.MyObjectBuilder_EntityComponentDefinition
    {
        public float Durability;
        public float PlayerDetectionRange = 200f;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_PAX_ShootingDefenderDefinition))]
    public class MyPAX_ShootingDefenderDefinition : VRage.Game.MyEntityComponentDefinition
    {
        public float Durability { get; private set; }
        public float PlayerDetectionRange { get; private set; }

        protected override void Init(VRage.Game.MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = builder as MyObjectBuilder_PAX_ShootingDefenderDefinition;
            if (ob == null)
                return;

            Durability = ob.Durability;
            PlayerDetectionRange = ob.PlayerDetectionRange;
        }
    }

    [MyComponent(typeof(MyObjectBuilder_PAX_ShootingDefender))]
    public class MyPAX_ShootingDefender : MyEntityComponent
    {
        public static List<MyPAX_ShootingDefender> ShootingDefenders = new List<MyPAX_ShootingDefender>();

        public MyEntityOwnershipComponent Owner;
        public bool TargetNeutral = true;

        public List<long> GetHouseMembers()
        {
            return new List<long>();
        }
    }
}
