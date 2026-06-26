using System;
using VRage.Game.Entity;
using VRageMath;

namespace Pax.Misc
{
    /// <summary>
    /// Compile-only surface for PAX core virtual projectile colliders.
    /// The real implementation is supplied by the ref_pax_core workshop dependency in game.
    /// </summary>
    public static class VirtualColliders
    {
        public static void AddCollider(
            MyEntity parent,
            MyEntity block,
            Vector3[] vertex,
            Action<int, long> hitCallback)
        {
        }

        public static void RemoveCollider(MyEntity parent, MyEntity block)
        {
        }
    }
}
