using System;
using VRage.Game.Components;
using VRage.Game.Entity;

namespace Pax.Cannons
{
    /// <summary>
    /// Compile-only surface for the PAX vehicle weapon components.
    /// The real implementations are supplied by the ref_pax_core workshop dependency in game.
    /// </summary>
    public class MyPAX_MachineGun : MyEntityComponent
    {
        public event Action FiredGun;
    }

    public class MyPAX_Cannon : MyEntityComponent
    {
    }
}
