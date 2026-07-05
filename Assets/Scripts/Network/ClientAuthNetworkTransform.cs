using Unity.Netcode.Components;

namespace FpsBase
{
    /// <summary>
    /// Owner-authoritative transform sync (the classic "ClientNetworkTransform"):
    /// each player moves their own character locally and replicates it to everyone.
    /// Simple and lag-free for the mover; swap for server-authoritative movement
    /// if you ever need cheat-proof physics.
    /// </summary>
    public class ClientAuthNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative() => false;
    }
}
