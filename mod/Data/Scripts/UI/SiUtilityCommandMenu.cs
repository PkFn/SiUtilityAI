using System.Xml.Serialization;
using Medieval.GUI.ContextMenu;
using Medieval.GUI.ContextMenu.Attributes;
using VRage.ObjectBuilders;

namespace Si.UtilityAI
{
    [MyContextMenuContextType(typeof(MyObjectBuilder_SiUtilityCommandMenuContext))]
    public sealed class SiUtilityCommandMenuContext : MyContextMenuContext
    {
        private SiUtilityCommandMenuSessionComponent _session;

        public override void Init(object[] contextParams)
        {
            _session = contextParams != null && contextParams.Length > 0
                ? contextParams[0] as SiUtilityCommandMenuSessionComponent
                : null;
        }

        public void CommandRootInfo()
        {
            _session?.CommandRootInfo();
        }

        public void CommandRootStop()
        {
            _session?.CommandRootStop();
        }

        public void CommandRootFollow()
        {
            _session?.CommandRootFollow();
        }

        public void CommandRootRearm()
        {
            _session?.CommandRootRearm();
        }

        public void CommandFormationColumn()
        {
            _session?.CommandFormationColumn();
        }

        public void CommandFormationFile()
        {
            _session?.CommandFormationFile();
        }

        public void CommandFormationLine()
        {
            _session?.CommandFormationLine();
        }

        public void CommandFormationVee()
        {
            _session?.CommandFormationVee();
        }

        public void CommandEngagementEnemiesNeutrals()
        {
            _session?.CommandEngagementEnemiesNeutrals();
        }

        public void CommandEngagementEnemies()
        {
            _session?.CommandEngagementEnemies();
        }

        public void CommandEngagementHoldFire()
        {
            _session?.CommandEngagementHoldFire();
        }

        public void CommandCombatSafe()
        {
            _session?.CommandCombatSafe();
        }

        public void CommandCombatCombat()
        {
            _session?.CommandCombatCombat();
        }

        public void CommandTransportationGetIn()
        {
            _session?.CommandTransportationGetIn();
        }

        public void CommandTransportationDisembark()
        {
            _session?.CommandTransportationDisembark();
        }

        public void CommandSettingsToggleUi()
        {
            _session?.CommandSettingsToggleUi();
        }

        public void CommandSettingsToggleSquadChatter()
        {
            _session?.CommandSettingsToggleSquadChatter();
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiUtilityCommandMenuContext : MyObjectBuilder_Base
    {
    }
}
