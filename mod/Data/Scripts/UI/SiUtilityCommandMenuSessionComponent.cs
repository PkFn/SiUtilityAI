using Medieval.GUI.ContextMenu;
using ObjectBuilders.Definitions.GUI;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Game;
using VRage.Game.Input;
using VRage.Input;
using VRage.Input.Input;
using VRage.Session;
using VRage.Utils;

namespace Si.UtilityAI
{
    [MySessionComponent(AllowAutomaticCreation = true, AlwaysOn = true)]
    public sealed class SiUtilityCommandMenuSessionComponent : MySessionComponent
    {
        private static readonly MyStringHash CommandMenuControl =
            MyStringHash.GetOrCompute("SiUtilityCommandMenu");

        private static readonly MyDefinitionId CommandMenu =
            new MyDefinitionId(typeof(MyObjectBuilder_ContextMenu), "SiUtilityCommandMenu");

        private MyInputContext _commandMenuContext;

        protected override void OnSessionReady()
        {
            base.OnSessionReady();

            _commandMenuContext = new MyInputContext("Si Utility AI command menu");
            _commandMenuContext.RegisterAction(CommandMenuControl, MyInputStateFlags.Pressed, OpenCommandMenu);
            if (!_commandMenuContext.InStack)
                _commandMenuContext.Push();
        }

        protected override void OnUnload()
        {
            if (_commandMenuContext != null && _commandMenuContext.InStack)
                _commandMenuContext.Pop();
            base.OnUnload();
        }

        private void OpenCommandMenu(ref MyInputContext.ActionEvent action)
        {
            var controlled = MyAPIGateway.Session?.ControlledObject;
            if (controlled == null)
            {
                action.Captured = false;
                return;
            }

            MyContextMenuScreen.OpenMenu(controlled, CommandMenu.SubtypeName, this);
        }

        internal void CommandRootInfo()
        {
            RequestCommand(SiUtilityCommandMenuCommand.Info);
        }

        internal void CommandRootStop()
        {
            RequestCommand(SiUtilityCommandMenuCommand.Stop);
        }

        internal void CommandRootFollow()
        {
            RequestCommand(SiUtilityCommandMenuCommand.Follow);
        }

        internal void CommandRootRearm()
        {
            RequestCommand(SiUtilityCommandMenuCommand.Rearm);
        }

        internal void CommandFormationColumn()
        {
            RequestCommand(SiUtilityCommandMenuCommand.FormationColumn);
        }

        internal void CommandFormationFile()
        {
            RequestCommand(SiUtilityCommandMenuCommand.FormationFile);
        }

        internal void CommandFormationLine()
        {
            RequestCommand(SiUtilityCommandMenuCommand.FormationLine);
        }

        internal void CommandFormationVee()
        {
            RequestCommand(SiUtilityCommandMenuCommand.FormationVee);
        }

        internal void CommandFormationLongBox()
        {
            RequestCommand(SiUtilityCommandMenuCommand.FormationLongBox);
        }

        internal void CommandFormationWideBox()
        {
            RequestCommand(SiUtilityCommandMenuCommand.FormationWideBox);
        }

        internal void CommandFormationSquare()
        {
            RequestCommand(SiUtilityCommandMenuCommand.FormationSquare);
        }

        internal void CommandFormationStaggeredColumn()
        {
            RequestCommand(SiUtilityCommandMenuCommand.FormationStaggeredColumn);
        }

        internal void CommandEngagementEnemiesNeutrals()
        {
            RequestCommand(SiUtilityCommandMenuCommand.EngagementEnemiesNeutrals);
        }

        internal void CommandEngagementEnemies()
        {
            RequestCommand(SiUtilityCommandMenuCommand.EngagementEnemies);
        }

        internal void CommandEngagementHoldFire()
        {
            RequestCommand(SiUtilityCommandMenuCommand.EngagementHoldFire);
        }

        internal void CommandCombatSafe()
        {
            RequestCommand(SiUtilityCommandMenuCommand.CombatSafe);
        }

        internal void CommandCombatCombat()
        {
            RequestCommand(SiUtilityCommandMenuCommand.CombatCombat);
        }

        internal void CommandTransportationGetIn()
        {
            RequestCommand(SiUtilityCommandMenuCommand.TransportationGetIn);
        }

        internal void CommandTransportationDisembark()
        {
            RequestCommand(SiUtilityCommandMenuCommand.TransportationDisembark);
        }

        internal void CommandSettingsToggleUi()
        {
            RequestCommand(SiUtilityCommandMenuCommand.ToggleUi);
        }

        internal void CommandSettingsToggleSquadChatter()
        {
            RequestCommand(SiUtilityCommandMenuCommand.ToggleSquadChatter);
        }

        private static void RequestCommand(SiUtilityCommandMenuCommand command)
        {
            SiNpcSessionComponent.Instance?.RequestUtilityCommand(command);
        }
    }

    internal enum SiUtilityCommandMenuCommand
    {
        Info,
        Stop,
        Follow,
        Rearm,
        FormationColumn,
        FormationFile,
        FormationLine,
        FormationVee,
        FormationLongBox,
        FormationWideBox,
        FormationSquare,
        FormationStaggeredColumn,
        EngagementEnemiesNeutrals,
        EngagementEnemies,
        EngagementHoldFire,
        CombatSafe,
        CombatCombat,
        TransportationGetIn,
        TransportationDisembark,
        ToggleUi,
        ToggleSquadChatter,
    }
}
