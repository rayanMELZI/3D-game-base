using Unity.Netcode.Components;

namespace FpsBase
{
    /// <summary>
    /// Server-authoritative transform sync for zombies: the server moves them,
    /// clients interpolate. Yaw only (zombies stay upright) and no scale, to
    /// keep the per-zombie bandwidth small when a wave is large.
    /// </summary>
    public class ZombieNetworkTransform : NetworkTransform
    {
        // Sync flags (yaw only, no scale) are set on the prefab.
        protected override bool OnIsServerAuthoritative() => true;
    }
}
