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

        internal void CommandSettingsToggleUi()
        {
            RequestCommand(SiUtilityCommandMenuCommand.ToggleUi);
        }

        private static void RequestCommand(SiUtilityCommandMenuCommand command)
        {
            // Intentionally deferred: the menu and script hooks are in place,
            // but command behavior belongs to the next implementation pass.
        }
    }

    internal enum SiUtilityCommandMenuCommand
    {
        Info,
        Stop,
        Follow,
        FormationColumn,
        FormationFile,
        FormationLine,
        FormationVee,
        ToggleUi,
    }
}
