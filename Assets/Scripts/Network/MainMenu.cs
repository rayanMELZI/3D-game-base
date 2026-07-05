using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FpsBase
{
    /// <summary>
    /// The game's front-end, drawn over the orbiting arena view:
    ///  - Main menu: PLAY (host duel / host TDM / join by IP + player name),
    ///    PRACTICE (offline range), SETTINGS, QUIT.
    ///  - In-match pause menu (Escape): resume / settings / leave match.
    /// Lives in the Multiplayer scene, which is the first scene in builds.
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        public const ushort Port = 7777;

        private enum MenuScreen { Root, Play, Settings }

        private MenuScreen screen = MenuScreen.Root;
        private string ipInput = "127.0.0.1";
        private string status = "";

        private bool NetworkRunning =>
            NetworkManager.Singleton != null
            && (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer);

        private void Start()
        {
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnect;
        }

        private void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
        }

        private void OnClientDisconnect(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (!nm.IsServer) // join refused, host closed, connection lost...
            {
                status = string.IsNullOrEmpty(nm.DisconnectReason)
                    ? "Disconnected from server."
                    : nm.DisconnectReason;
                MouseLook.LockCursor(false);
                if (MultiplayerBootstrap.Instance != null)
                    MultiplayerBootstrap.Instance.SetMenuCamera(true);
                screen = MenuScreen.Root;
            }
        }

        // ------------------------------------------------------------------

        private void OnGUI()
        {
            MenuWidgets.EnsureStyles();

            if (!NetworkRunning)
                DrawMainMenu();
            else if (Cursor.lockState != CursorLockMode.Locked)
                DrawPauseMenu();
        }

        private void DrawMainMenu()
        {
            float w = 440f, h = 460f;
            var panel = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            MenuWidgets.Panel(panel);

            GUILayout.BeginArea(new Rect(panel.x + 30, panel.y + 22, w - 60, h - 44));

            GUILayout.Label(GameSettings.GameTitle, MenuWidgets.Title, GUILayout.Height(56));
            GUILayout.Label("online arena shooter · " + GameSettings.Version, MenuWidgets.Subtitle);
            GUILayout.Space(18);

            switch (screen)
            {
                case MenuScreen.Root: DrawRoot(); break;
                case MenuScreen.Play: DrawPlay(); break;
                case MenuScreen.Settings: DrawSettings(showBack: true); break;
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("WASD move · Shift sprint · Space jump · 1/2/3 weapons · Mouse2 scope · R reload",
                MenuWidgets.Small);
            GUILayout.EndArea();
        }

        private void DrawRoot()
        {
            if (MenuWidgets.MenuButton("PLAY ONLINE"))
                screen = MenuScreen.Play;
            GUILayout.Space(8);
            if (MenuWidgets.MenuButton("PRACTICE RANGE"))
                SceneManager.LoadScene("Main");
            GUILayout.Space(8);
            if (MenuWidgets.MenuButton("SETTINGS"))
                screen = MenuScreen.Settings;
            GUILayout.Space(8);
            if (MenuWidgets.MenuButton("QUIT"))
                Application.Quit();

            if (!string.IsNullOrEmpty(status))
            {
                GUILayout.Space(10);
                GUILayout.Label(status, MenuWidgets.Label);
            }
        }

        private void DrawPlay()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Your name", MenuWidgets.Label, GUILayout.Width(90));
            string newName = GUILayout.TextField(GameSettings.PlayerName, 16, MenuWidgets.Input, GUILayout.Height(28));
            if (newName != GameSettings.PlayerName)
            {
                GameSettings.PlayerName = newName;
                GameSettings.Save();
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(14);

            if (MenuWidgets.MenuButton("HOST — 1V1 DUEL  (first to 10)"))
                StartHost(GameMode.Duel);
            GUILayout.Space(6);
            if (MenuWidgets.MenuButton("HOST — TEAM DEATHMATCH  (first to 30)"))
                StartHost(GameMode.TeamDeathmatch);

            GUILayout.Space(14);
            GUILayout.BeginHorizontal();
            ipInput = GUILayout.TextField(ipInput, MenuWidgets.Input, GUILayout.Height(30));
            if (MenuWidgets.MenuButton("JOIN", 30f))
                StartClient();
            GUILayout.EndHorizontal();
            GUILayout.Label($"LAN: enter the host's IP · port {Port} must be allowed in the host firewall",
                MenuWidgets.Small);

            if (!string.IsNullOrEmpty(status))
            {
                GUILayout.Space(6);
                GUILayout.Label(status, MenuWidgets.Label);
            }

            GUILayout.Space(10);
            if (MenuWidgets.MenuButton("BACK", 32f))
                screen = MenuScreen.Root;
        }

        private void DrawSettings(bool showBack)
        {
            GameSettings.MouseSensitivity = MenuWidgets.Slider("Mouse sensitivity", GameSettings.MouseSensitivity, 0.2f, 3f, "0.00");
            GameSettings.Fov = Mathf.Round(MenuWidgets.Slider("Field of view", GameSettings.Fov, 50f, 90f, "0"));
            GameSettings.Volume = MenuWidgets.Slider("Volume", GameSettings.Volume, 0f, 1f, "0%");
            AudioListener.volume = GameSettings.Volume; // live

            GUILayout.Space(8);
            if (MenuWidgets.MenuButton(UnityEngine.Screen.fullScreen ? "WINDOWED" : "FULLSCREEN", 32f))
                UnityEngine.Screen.fullScreen = !UnityEngine.Screen.fullScreen;
            GUILayout.Space(6);
            if (MenuWidgets.MenuButton($"QUALITY: {QualitySettings.names[GameSettings.QualityLevel].ToUpper()}", 32f))
            {
                GameSettings.QualityLevel = (GameSettings.QualityLevel + 1) % QualitySettings.names.Length;
                GameSettings.Apply();
            }

            GUILayout.Space(10);
            if (showBack && MenuWidgets.MenuButton("BACK", 32f))
            {
                GameSettings.Save();
                screen = MenuScreen.Root;
            }
        }

        private void DrawPauseMenu()
        {
            float w = 380f, h = screen == MenuScreen.Settings ? 420f : 300f;
            var panel = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            MenuWidgets.Panel(panel);

            GUILayout.BeginArea(new Rect(panel.x + 30, panel.y + 22, w - 60, h - 44));
            GUILayout.Label("PAUSED", MenuWidgets.Title, GUILayout.Height(50));
            GUILayout.Space(12);

            if (screen == MenuScreen.Settings)
            {
                DrawSettings(showBack: false);
                GUILayout.Space(8);
                if (MenuWidgets.MenuButton("BACK", 32f))
                {
                    GameSettings.Save();
                    screen = MenuScreen.Root;
                }
            }
            else
            {
                if (MenuWidgets.MenuButton("RESUME"))
                    MouseLook.LockCursor(true);
                GUILayout.Space(8);
                if (MenuWidgets.MenuButton("SETTINGS"))
                    screen = MenuScreen.Settings;
                GUILayout.Space(8);
                if (MenuWidgets.MenuButton("LEAVE MATCH"))
                    Leave();
            }
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------
        // Hosting / joining
        // ------------------------------------------------------------------

        private void StartHost(GameMode mode)
        {
            var nm = NetworkManager.Singleton;
            GameModeManager.PendingHostMode = mode;
            ConfigureTransport("127.0.0.1");

            nm.NetworkConfig.ConnectionApproval = true;
            nm.ConnectionApprovalCallback = ApproveConnection;

            status = nm.StartHost() ? "" : "Failed to start host (is another host running?).";
        }

        private void ApproveConnection(
            NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            int maxPlayers = GameModeManager.MaxPlayersFor(GameModeManager.PendingHostMode);
            bool full = NetworkManager.Singleton.ConnectedClientsList.Count >= maxPlayers;

            response.Approved = !full;
            response.CreatePlayerObject = !full;
            if (full)
                response.Reason = "Server is full.";
        }

        private void StartClient()
        {
            ConfigureTransport(ipInput.Trim());
            status = $"Connecting to {ipInput.Trim()}:{Port} ...";
            if (!NetworkManager.Singleton.StartClient())
                status = "Failed to start client.";
        }

        private void ConfigureTransport(string address)
        {
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetConnectionData(address, Port, "0.0.0.0");
        }

        private void Leave()
        {
            NetworkManager.Singleton.Shutdown();
            MouseLook.LockCursor(false);
            if (MultiplayerBootstrap.Instance != null)
                MultiplayerBootstrap.Instance.SetMenuCamera(true);
            status = "";
            screen = MenuScreen.Root;
        }
    }
}
