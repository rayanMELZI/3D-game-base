using Unity.Netcode.Components;

namespace FpsBase
{
    /// <summary>
    /// Server-authoritative transform sync for bots: the server moves them,
    /// every client sees an interpolated copy.
    /// </summary>
    public class BotNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative() => true;
    }
}
