// Included so you do NOT need to import the "SteamManager" sample from the
// Steamworks.NET package — this is the same minimal bootstrap: initializes the
// Steam API on load and pumps its callbacks every frame.
// Add this component next to the NetworkManager (Multiplayer scene).
#if FPSBASE_STEAM
using Steamworks;
using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Boots the Steam API. AppID comes from steam_appid.txt in the project
    /// root during development (480 = Valve's test app "Spacewar"); a shipped
    /// build launched through Steam gets it from Steam itself.
    /// Requires the Steam client to be running and logged in.
    /// </summary>
    [DisallowMultipleComponent]
    public class SteamManager : MonoBehaviour
    {
        public static bool Initialized { get; private set; }

        private static SteamManager instance;

        private void Awake()
        {
            if (instance != null)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);

            try
            {
                Initialized = SteamAPI.Init();
                if (!Initialized)
                    Debug.LogWarning("[Steam] SteamAPI.Init() failed — is Steam running and " +
                                     "steam_appid.txt present next to the project/executable?");
            }
            catch (System.DllNotFoundException e)
            {
                Debug.LogError("[Steam] steam_api DLL not found: " + e.Message);
            }
        }

        private void Update()
        {
            if (Initialized)
                SteamAPI.RunCallbacks(); // lobby/join callbacks arrive through this
        }

        private void OnDestroy()
        {
            if (instance != this)
                return;
            instance = null;
            if (Initialized)
            {
                SteamAPI.Shutdown();
                Initialized = false;
            }
        }
    }
}
#endif
