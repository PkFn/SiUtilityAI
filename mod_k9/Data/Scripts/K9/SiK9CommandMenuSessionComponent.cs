using Medieval.GUI.ContextMenu;
using ObjectBuilders.Definitions.GUI;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Input;
using VRage.Input;
using VRage.Input.Input;
using VRage.Session;
using VRage.Utils;

namespace Si.K9
{
    [MySessionComponent(AllowAutomaticCreation = true, AlwaysOn = true)]
    public sealed class SiK9CommandMenuSessionComponent : MySessionComponent
    {
        private static readonly MyStringHash CommandMenuControl =
            MyStringHash.GetOrCompute("SiK9CommandMenu");

        private static readonly MyDefinitionId CommandMenu =
            new MyDefinitionId(typeof(MyObjectBuilder_ContextMenu), "SiK9CommandMenu");

        private MyInputContext _commandMenuContext;

        protected override void OnSessionReady()
        {
            base.OnSessionReady();
            if (MyAPIGateway.Utilities?.IsDedicated ?? false)
                return;

            _commandMenuContext = new MyInputContext("Si K9 command menu");
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

        internal void CommandMotionStop()
        {
            SiK9WolfSpawnSession.Instance?.RequestMotionOrder(SiK9DogMotionOrder.Stop);
            SiK9WolfSpawnSession.Instance?.NotifyLocalOrder(SiK9DogMotionOrder.Stop);
        }

        internal void CommandMotionFollow()
        {
            SiK9WolfSpawnSession.Instance?.RequestMotionOrder(SiK9DogMotionOrder.Follow);
            SiK9WolfSpawnSession.Instance?.NotifyLocalOrder(SiK9DogMotionOrder.Follow);
        }

        internal void CommandTransportationGetIn()
        {
            SiK9WolfSpawnSession.Instance?.RequestTransportOrder(SiK9DogTransportOrder.GetIn);
            SiK9WolfSpawnSession.Instance?.NotifyLocalTransportOrder(SiK9DogTransportOrder.GetIn);
        }

        internal void CommandTransportationGetOut()
        {
            SiK9WolfSpawnSession.Instance?.RequestTransportOrder(SiK9DogTransportOrder.GetOut);
            SiK9WolfSpawnSession.Instance?.NotifyLocalTransportOrder(SiK9DogTransportOrder.GetOut);
        }
    }
}
