// Steam play scaffold. Everything is inside #if FPSBASE_STEAM so the project
// compiles without the Steam packages. To enable (full steps in the README):
//   1. Install Steamworks.NET (UPM git URL) and the community Steam transport
//      for Netcode (Unity-Technologies/multiplayer-community-contributions).
//   2. Add FPSBASE_STEAM to Project Settings > Player > Scripting Define Symbols.
//   3. Add a SteamManager + SteamNetworkingSocketsTransport next to the
//      NetworkManager in the Multiplayer scene, keep AppID 480 (Spacewar)
//      for testing, your own AppID for release.
#if FPSBASE_STEAM
using Steamworks;
using Unity.Netcode;
using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Steam lobbies on top of Netcode: hosting creates a friends-only lobby,
    /// friends join through the Steam overlay (Join Game) — no IPs, no port
    /// forwarding, and Steam's relay handles NAT traversal.
    /// </summary>
    public static class SteamLobbyService
    {
        private const string HostIdKey = "host_steam_id";

        private static Callback<LobbyCreated_t> lobbyCreated;
        private static Callback<GameLobbyJoinRequested_t> joinRequested;
        private static Callback<LobbyEnter_t> lobbyEntered;
        private static CSteamID currentLobby;
        private static bool callbacksReady;

        public static void EnsureCallbacks()
        {
            if (callbacksReady || !SteamManager.Initialized)
                return;
            callbacksReady = true;
            lobbyCreated = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
            joinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnJoinRequested);
            lobbyEntered = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
        }

        public static void HostLobby(GameMode mode, int map, bool sniperOnly)
        {
            if (!SteamManager.Initialized)
            {
                Debug.LogWarning("Steam is not running.");
                return;
            }
            EnsureCallbacks();
            GameModeManager.PendingHostMode = mode;
            GameModeManager.PendingHostMap = map;
            GameModeManager.PendingSniperOnly = sniperOnly;
            SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly,
                GameModeManager.MaxPlayersFor(mode));
        }

        private static void OnLobbyCreated(LobbyCreated_t data)
        {
            if (data.m_eResult != EResult.k_EResultOK)
                return;
            currentLobby = new CSteamID(data.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(currentLobby, HostIdKey, SteamUser.GetSteamID().ToString());
            NetworkManager.Singleton.StartHost();
        }

        // A friend clicked "Join Game" in the Steam overlay.
        private static void OnJoinRequested(GameLobbyJoinRequested_t data)
        {
            SteamMatchmaking.JoinLobby(data.m_steamIDLobby);
        }

        private static void OnLobbyEntered(LobbyEnter_t data)
        {
            currentLobby = new CSteamID(data.m_ulSteamIDLobby);
            string hostId = SteamMatchmaking.GetLobbyData(currentLobby, HostIdKey);
            if (string.IsNullOrEmpty(hostId) || hostId == SteamUser.GetSteamID().ToString())
                return; // we are the host

            // Point the Steam transport at the host and connect.
            // (Component from the Netcode community contributions repo.)
            var transport = NetworkManager.Singleton
                .GetComponent<Netcode.Transports.SteamNetworkingSocketsTransport>();
            transport.ConnectToSteamID = ulong.Parse(hostId);
            NetworkManager.Singleton.NetworkConfig.NetworkTransport = transport;
            NetworkManager.Singleton.StartClient();
        }

        public static void LeaveLobby()
        {
            if (currentLobby.IsValid())
                SteamMatchmaking.LeaveLobby(currentLobby);
            currentLobby = default;
        }
    }
}
#endif
