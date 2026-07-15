using System.Xml.Serialization;
using Medieval.GUI.ContextMenu;
using Medieval.GUI.ContextMenu.Attributes;
using Sandbox.ModAPI;
using VRage.ObjectBuilders;

namespace Si.UtilityAI
{
    internal static class SiWaypointEditorMenu
    {
        internal static bool OpenFor(SiSquadLeaderKey leader)
        {
            var controlled = MyAPIGateway.Session?.ControlledObject;
            if (controlled == null)
                return false;

            MyContextMenuScreen.OpenMenu(
                controlled,
                "SiWaypointEditorMenu",
                new SiWaypointEditorMenuSession(leader));
            return true;
        }
    }

    internal sealed class SiWaypointEditorMenuSession
    {
        private readonly SiSquadLeaderKey _leader;

        public SiWaypointEditorMenuSession(SiSquadLeaderKey leader)
        {
            _leader = leader;
        }

        public void SelectSpeed(SiWaypointEditorSpeed speed)
        {
            // Speed orders will be added after AI waypoint speed handling exists.
        }

        public void SelectFormation(SiSquadFormation formation)
        {
            SiNpcSessionComponent.Instance?.RequestAiSquadWaypointFormation(_leader, formation);
        }
    }

    internal enum SiWaypointEditorSpeed
    {
        Walk,
        Run,
        Sprint,
    }

    [MyContextMenuContextType(typeof(MyObjectBuilder_SiWaypointEditorMenuContext))]
    public sealed class SiWaypointEditorMenuContext : MyContextMenuContext
    {
        private SiWaypointEditorMenuSession _session;

        public override void Init(object[] contextParams)
        {
            _session = contextParams != null && contextParams.Length > 0
                ? contextParams[0] as SiWaypointEditorMenuSession
                : null;
        }

        public void WaypointSpeedWalk()
        {
            _session?.SelectSpeed(SiWaypointEditorSpeed.Walk);
        }

        public void WaypointSpeedRun()
        {
            _session?.SelectSpeed(SiWaypointEditorSpeed.Run);
        }

        public void WaypointSpeedSprint()
        {
            _session?.SelectSpeed(SiWaypointEditorSpeed.Sprint);
        }

        public void CommandFormationColumn()
        {
            _session?.SelectFormation(SiSquadFormation.Column);
        }

        public void CommandFormationFile()
        {
            _session?.SelectFormation(SiSquadFormation.File);
        }

        public void CommandFormationLine()
        {
            _session?.SelectFormation(SiSquadFormation.Line);
        }

        public void CommandFormationVee()
        {
            _session?.SelectFormation(SiSquadFormation.Vee);
        }

        public void CommandFormationLongBox()
        {
            _session?.SelectFormation(SiSquadFormation.LongBox);
        }

        public void CommandFormationWideBox()
        {
            _session?.SelectFormation(SiSquadFormation.WideBox);
        }

        public void CommandFormationSquare()
        {
            _session?.SelectFormation(SiSquadFormation.Square);
        }

        public void CommandFormationStaggeredColumn()
        {
            _session?.SelectFormation(SiSquadFormation.StaggeredColumn);
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiWaypointEditorMenuContext : MyObjectBuilder_Base
    {
    }
}
