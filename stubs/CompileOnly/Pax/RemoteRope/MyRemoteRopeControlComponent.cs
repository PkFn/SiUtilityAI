using VRage.Game.Components;

namespace Pax.RemoteRope
{
    /// <summary>
    /// Compile-only surface for the PAX RemoteRope control component.
    /// The real implementation is supplied by the ref_pax_core workshop dependency in game.
    /// </summary>
    public class MyRemoteRopeControlComponent : MyEntityComponent
    {
        public long AttachedPlayerId;

        public void LocalAction(
            short action,
            float analogValue = 0,
            bool analog = false,
            bool noEvents = false)
        {
        }
    }
}
