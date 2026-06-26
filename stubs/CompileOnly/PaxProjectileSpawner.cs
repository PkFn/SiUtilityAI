using System;
using VRageMath;

namespace Pax.Cannons
{
    /// <summary>
    /// Compile-only surface for the PAX core projectile spawner.
    /// The real implementation is supplied by the ref_pax_core workshop dependency in game.
    /// </summary>
    public class PAX_Projectile_Spawner
    {
        public static Vector2 ServerCreateSyncedProjectile(
            string defString,
            MatrixD mat,
            float velocity,
            float accuracy,
            Vector3 gridVelocity,
            float maxDistance,
            float characterDamageMultiplier = 1f,
            long ownerId = -1)
        {
            throw new NotSupportedException("Compile-only PAX API stub was executed. Load the real PAX core mod in game.");
        }
    }

    public class MyPAX_CustomProjectile
    {
        public long GetOwnerId()
        {
            throw new NotSupportedException("Compile-only PAX API stub was executed. Load the real PAX core mod in game.");
        }
    }
}
