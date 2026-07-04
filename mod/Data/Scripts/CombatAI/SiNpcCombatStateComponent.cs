using System.Xml.Serialization;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Game.Components;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;

namespace Si.UtilityAI
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiNpcCombatStateComponent : MyObjectBuilder_EntityComponent
    {
    }

    public enum SiNpcCombatState
    {
        Idle,
        Firing,
        ThrowPreparing,
        ThrowRecovering,
    }

    [MyComponent(typeof(MyObjectBuilder_SiNpcCombatStateComponent))]
    public class SiNpcCombatStateComponent : MyEntityComponent
    {
        private long _recoverUntilMilliseconds;

        public override bool IsSerialized => false;
        public SiNpcCombatState State { get; private set; }

        public bool AllowsFiring
        {
            get
            {
                UpdateRecoveryState();
                return State == SiNpcCombatState.Idle || State == SiNpcCombatState.Firing;
            }
        }

        public bool CanBeginThrow
        {
            get
            {
                UpdateRecoveryState();
                return State == SiNpcCombatState.Idle || State == SiNpcCombatState.Firing;
            }
        }

        internal void SetFiring(bool firing)
        {
            UpdateRecoveryState();
            if (firing)
            {
                if (State == SiNpcCombatState.Idle || State == SiNpcCombatState.Firing)
                    State = SiNpcCombatState.Firing;
                return;
            }

            if (State == SiNpcCombatState.Firing)
                State = SiNpcCombatState.Idle;
        }

        internal bool TryBeginThrow()
        {
            UpdateRecoveryState();
            if (!CanBeginThrow)
                return false;

            State = SiNpcCombatState.ThrowPreparing;
            return true;
        }

        internal void BeginRecovery(long recoveryMilliseconds)
        {
            State = SiNpcCombatState.ThrowRecovering;
            _recoverUntilMilliseconds = CurrentTimeMilliseconds() + (recoveryMilliseconds > 0 ? recoveryMilliseconds : 0);
        }

        internal void CancelThrow()
        {
            if (State == SiNpcCombatState.ThrowPreparing || State == SiNpcCombatState.ThrowRecovering)
            {
                State = SiNpcCombatState.Idle;
                _recoverUntilMilliseconds = 0;
            }
        }

        private void UpdateRecoveryState()
        {
            if (State != SiNpcCombatState.ThrowRecovering)
                return;

            if (CurrentTimeMilliseconds() >= _recoverUntilMilliseconds)
            {
                State = SiNpcCombatState.Idle;
                _recoverUntilMilliseconds = 0;
            }
        }

        private static long CurrentTimeMilliseconds()
        {
            var session = MyAPIGateway.Session;
            return session != null
                ? (long)session.ElapsedPlayTime.TotalMilliseconds
                : 0;
        }
    }
}
