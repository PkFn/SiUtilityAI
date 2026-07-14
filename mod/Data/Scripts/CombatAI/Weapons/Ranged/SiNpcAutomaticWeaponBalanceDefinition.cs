using System;
using System.Xml.Serialization;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;

namespace Si.UtilityAI
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiNpcAutomaticWeaponBalanceDefinition : MyObjectBuilder_DefinitionBase
    {
        public int BurstCount;
        public int BurstCooldownMilliseconds;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiNpcAutomaticWeaponBalanceDefinition))]
    public class SiNpcAutomaticWeaponBalanceDefinition : MyDefinitionBase
    {
        public int BurstCount { get; private set; }
        public int BurstCooldownMilliseconds { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiNpcAutomaticWeaponBalanceDefinition)builder;
            BurstCount = Math.Max(1, ob.BurstCount);
            BurstCooldownMilliseconds = Math.Max(0, ob.BurstCooldownMilliseconds);
        }
    }
}
