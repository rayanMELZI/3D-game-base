using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FpsBase
{
    /// <summary>
    /// The game's front-end, drawn over the orbiting arena view:
    ///  - Main menu: PLAY (choose map / mode / snipers-only, host over LOCAL/LAN
    ///    or join by IP with a proper connecting window), PRACTICE, SETTINGS, QUIT.
    ///  - In-match pause menu (Escape): resume / switch team / settings / leave.
    /// Steam play is scaffolded behind the FPSBASE_STEAM define — see README.
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        public const ushort Port = 7777;
        private const float JoinTimeout = 10f;

        private enum MenuScreen { Root, Play, Settings, Classes }

        private MenuScreen screen = MenuScreen.Root;
        private string ipInput = "127.0.0.1";
        private string status = "";
        private int selectedMap;
        private GameMode selectedMode = GameMode.TeamDeathmatch;
        private bool sniperOnly;
        private int radarMode = 2; // 0 off, 1 always, 2 on fire
        private bool connecting;
        private float connectStart;

        // Weapon names for the class builder (display only).
        private static readonly WeaponDefinition[] LoadoutInfo = WeaponDefinition.CreateDefaultLoadout();
        private static readonly int[] PrimaryOptions = { 2, 3, 4, 5, 6, 7, 8 };
        private static readonly int[] SecondaryOptions = { 1, 2, 3, 6, 8 };

        // Gunsmith: which weapons can be customised (knife & RPG stay bare).
        private static readonly int[] GunsmithWeapons = { 1, 2, 3, 4, 5, 7, 8 };
        private int gunsmithWeapon = 4; // rifle

        private bool NetworkRunning =>
            NetworkManager.Singleton != null
            && (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer);

        private void Start()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnect;
                NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            }
            UpdateChecker.EnsureStarted();
        }

        private void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            }
        }

        private void Update()
        {
            // Join attempt timeout: without this, joining a game that doesn't
            // exist would hang forever.
            if (connecting && Time.time - connectStart > JoinTimeout)
            {
                NetworkManager.Singleton.Shutdown();
                connecting = false;
                status = $"No game found at {ipInput.Trim()}:{Port}.";
            }
        }

        private void OnClientConnected(ulong clientId)
        {
            if (NetworkManager.Singleton.LocalClientId == clientId)
            {
                connecting = false;
                status = "";
            }
        }

        private void OnClientDisconnect(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (!nm.IsServer)
            {
                connecting = false;
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

            if (connecting)
            {
                DrawConnectingWindow();
                return;
            }

            if (!NetworkRunning)
                DrawMainMenu();
            else if (Cursor.lockState != CursorLockMode.Locked)
                DrawPauseMenu();
        }

        private void DrawConnectingWindow()
        {
            float w = 380f, h = 190f;
            var panel = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            MenuWidgets.Panel(panel);

            GUILayout.BeginArea(new Rect(panel.x + 30, panel.y + 24, w - 60, h - 48));
            int dots = (int)(Time.time * 2f) % 4;
            GUILayout.Label("CONNECTING" + new string('.', dots), MenuWidgets.Title, GUILayout.Height(48));
            GUILayout.Label($"{ipInput.Trim()}:{Port}", MenuWidgets.Subtitle);
            GUILayout.FlexibleSpace();
            if (MenuWidgets.MenuButton("CANCEL", 36f))
            {
                NetworkManager.Singleton.Shutdown();
                connecting = false;
                status = "Join cancelled.";
            }
            GUILayout.EndArea();
        }

        private void DrawMainMenu()
        {
            float w = 460f;
            float h = screen == MenuScreen.Play ? 640f
                : screen == MenuScreen.Classes ? 660f : 470f;
            var panel = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            MenuWidgets.Panel(panel);

            GUILayout.BeginArea(new Rect(panel.x + 30, panel.y + 22, w - 60, h - 44));

            GUILayout.Label(GameSettings.GameTitle, MenuWidgets.Title, GUILayout.Height(56));
            GUILayout.Label("MyGame online arena shooter · " + GameSettings.Version, MenuWidgets.Subtitle);
            GUILayout.Space(14);

            switch (screen)
            {
                case MenuScreen.Root: DrawRoot(); break;
                case MenuScreen.Play: DrawPlay(); break;
                case MenuScreen.Settings: DrawSettings(showBack: true); break;
                case MenuScreen.Classes: DrawClasses(); break;
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("WASD move · Shift sprint · Ctrl crouch/slide · Space jump · Mouse2 aim · Tab scores",
                MenuWidgets.Small);
            GUILayout.EndArea();
        }

        private void DrawRoot()
        {
            if (UpdateChecker.UpdateAvailable)
                GUILayout.Label($"Update available: {UpdateChecker.LatestVersion} (you have {GameSettings.Version})",
                    MenuWidgets.Label);

            if (MenuWidgets.MenuButton("PLAY"))
                screen = MenuScreen.Play;
            GUILayout.Space(8);
            if (MenuWidgets.MenuButton("CLASSES"))
                screen = MenuScreen.Classes;
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
            GUILayout.Space(10);

            // Match setup.
            GUILayout.BeginHorizontal();
            if (MenuWidgets.MenuButton($"MAP: {MapCatalog.Name(selectedMap)}", 32f))
                selectedMap = (selectedMap + 1) % MapCatalog.Count;
            if (MenuWidgets.MenuButton(sniperOnly ? "SNIPERS ONLY: ON" : "SNIPERS ONLY: OFF", 32f))
                sniperOnly = !sniperOnly;
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
            string radarLabel = radarMode == 0 ? "MINIMAP: OFF"
                : radarMode == 1 ? "MINIMAP: ALWAYS ON" : "MINIMAP: PING ON FIRE";
            if (MenuWidgets.MenuButton(radarLabel, 32f))
                radarMode = (radarMode + 1) % 3;
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            foreach (var mode in new[] { GameMode.Duel, GameMode.TeamDeathmatch, GameMode.FreeForAll, GameMode.GunGame })
            {
                string label = mode == GameMode.Duel ? "1V1"
                    : mode == GameMode.TeamDeathmatch ? "TDM"
                    : mode == GameMode.FreeForAll ? "FFA" : "GUN GAME";
                var prev = GUI.backgroundColor;
                if (selectedMode == mode)
                    GUI.backgroundColor = MenuWidgets.Accent;
                if (MenuWidgets.MenuButton(label, 30f))
                    selectedMode = mode;
                GUI.backgroundColor = prev;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(12);
            GUILayout.Label("LOCAL / LAN", MenuWidgets.Subtitle);
            if (MenuWidgets.MenuButton("HOST GAME"))
                StartHost();
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            ipInput = GUILayout.TextField(ipInput, MenuWidgets.Input, GUILayout.Height(30));
            if (MenuWidgets.MenuButton("JOIN", 30f))
                StartClient();
            GUILayout.EndHorizontal();
            GUILayout.Label($"LAN: enter the host's IP · port {Port} must be allowed in the host firewall",
                MenuWidgets.Small);

            GUILayout.Space(10);
            GUILayout.Label("STEAM", MenuWidgets.Subtitle);
#if FPSBASE_STEAM
            if (MenuWidgets.MenuButton("HOST STEAM GAME (friends can join via overlay)"))
            {
                GameModeManager.PendingRadarMode = radarMode;
                if (!SteamLobbyService.HostLobby(selectedMode, selectedMap, sniperOnly))
                    status = "Steam not ready: check Steam is running, steam_appid.txt exists, and " +
                             "SteamManager + SteamNetworkingSocketsTransport are on the NetworkManager.";
            }
            GUILayout.Label("Friends: right-click you in the Steam friends list → Join Game.", MenuWidgets.Small);
#else
            GUILayout.Label("Not enabled — install Steamworks + the Steam transport and add the\nFPSBASE_STEAM define. Full steps in the README.", MenuWidgets.Small);
#endif

            if (!string.IsNullOrEmpty(status))
            {
                GUILayout.Space(6);
                GUILayout.Label(status, MenuWidgets.Label);
            }

            GUILayout.Space(8);
            if (MenuWidgets.MenuButton("BACK", 32f))
                screen = MenuScreen.Root;
        }

        /// <summary>
        /// CoD-style class builder: 3 classes, each = primary + secondary (the
        /// knife always rides along). The selected class is what you spawn with;
        /// keys 1/2/3 in-game = knife / secondary / primary.
        /// </summary>
        private void DrawClasses()
        {
            var previousBackground = GUI.backgroundColor;
            if (GameSettings.UseClassLoadout)
                GUI.backgroundColor = MenuWidgets.Accent;
            if (MenuWidgets.MenuButton(GameSettings.UseClassLoadout
                    ? "LOADOUT: 2 WEAPONS + KNIFE"
                    : "LOADOUT: FULL ARSENAL", 34f))
                GameSettings.UseClassLoadout = !GameSettings.UseClassLoadout;
            GUI.backgroundColor = previousBackground;
            GUILayout.Label(GameSettings.UseClassLoadout
                    ? "Spawn with the selected class; keys 1/2/3 select knife, secondary, primary."
                    : "Carry all seven weapons; number keys and scroll use the global arsenal order.",
                MenuWidgets.Small);
            GUILayout.Space(6);

            GUILayout.Label("Pick a class to spawn with — click the weapons to change them.", MenuWidgets.Label);
            GUILayout.Space(6);

            for (int i = 0; i < GameSettings.ClassCount; i++)
            {
                GUILayout.BeginHorizontal();

                var prev = GUI.backgroundColor;
                if (GameSettings.SelectedClass == i)
                    GUI.backgroundColor = MenuWidgets.Accent;
                if (MenuWidgets.MenuButton($"CLASS {i + 1}", 34f))
                    GameSettings.SelectedClass = i;
                GUI.backgroundColor = prev;

                if (MenuWidgets.MenuButton(LoadoutInfo[GameSettings.ClassPrimary[i]].displayName, 34f))
                    GameSettings.ClassPrimary[i] = NextOption(PrimaryOptions, GameSettings.ClassPrimary[i]);
                if (MenuWidgets.MenuButton(LoadoutInfo[GameSettings.ClassSecondary[i]].displayName, 34f))
                    GameSettings.ClassSecondary[i] = NextOption(SecondaryOptions, GameSettings.ClassSecondary[i]);

                GUILayout.EndHorizontal();
                GUILayout.Space(4);
            }

            GUILayout.Label("primary · secondary — the knife is always with you (1 = knife, 2 = secondary, 3 = primary)",
                MenuWidgets.Small);

            DrawGunsmith();

            GUILayout.Space(8);
            if (MenuWidgets.MenuButton("BACK", 32f))
            {
                GameSettings.Save();
                screen = MenuScreen.Root;
            }
        }

        /// <summary>
        /// Per-weapon add-ons, chosen once and shared by every class. Pick a
        /// weapon, then toggle its four add-ons (illegal ones show as "—").
        /// </summary>
        private void DrawGunsmith()
        {
            GUILayout.Space(8);
            GUILayout.Label("GUNSMITH — add-ons stick to this weapon in every class:", MenuWidgets.Small);

            GUILayout.BeginHorizontal();
            if (MenuWidgets.MenuButton("◀", 34f)) gunsmithWeapon = Cycle(GunsmithWeapons, gunsmithWeapon, -1);
            if (MenuWidgets.MenuButton(LoadoutInfo[gunsmithWeapon].displayName, 34f))
                gunsmithWeapon = Cycle(GunsmithWeapons, gunsmithWeapon, +1);
            if (MenuWidgets.MenuButton("▶", 34f)) gunsmithWeapon = Cycle(GunsmithWeapons, gunsmithWeapon, +1);
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            var model = LoadoutInfo[gunsmithWeapon].model;
            GUILayout.BeginHorizontal();
            DrawAddonToggle(AttachmentType.Optic, model);
            DrawAddonToggle(AttachmentType.Suppressor, model);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DrawAddonToggle(AttachmentType.Foregrip, model);
            DrawAddonToggle(AttachmentType.ExtendedMag, model);
            GUILayout.EndHorizontal();
            if (MenuWidgets.MenuButton($"COLOR: {WeaponModelBuilder.ColorName(GameSettings.WeaponColors[gunsmithWeapon])}", 30f))
                GameSettings.WeaponColors[gunsmithWeapon] = (GameSettings.WeaponColors[gunsmithWeapon] + 1) % 6;
        }

        private void DrawAddonToggle(AttachmentType type, WeaponModelType model)
        {
            bool allowed = Attachments.IsAllowed(model, type);
            int mask = GameSettings.WeaponAttachments[gunsmithWeapon];
            bool on = Attachments.Has(mask, type);

            var prevBg = GUI.backgroundColor;
            bool prevEnabled = GUI.enabled;
            if (on) GUI.backgroundColor = MenuWidgets.Accent;
            GUI.enabled = allowed;

            string label = allowed
                ? $"{Attachments.Names[(int)type]}: {(on ? "ON" : "OFF")}"
                : $"{Attachments.Names[(int)type]}: —";
            if (MenuWidgets.MenuButton(label, 30f) && allowed)
                GameSettings.WeaponAttachments[gunsmithWeapon] = Attachments.Toggle(mask, type);

            GUI.enabled = prevEnabled;
            GUI.backgroundColor = prevBg;
        }

        private static int NextOption(int[] options, int current)
        {
            int at = System.Array.IndexOf(options, current);
            return options[(at + 1) % options.Length];
        }

        private static int Cycle(int[] options, int current, int dir)
        {
            int at = System.Array.IndexOf(options, current);
            if (at < 0) at = 0;
            return options[(at + dir + options.Length) % options.Length];
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
            float w = 380f, h = screen == MenuScreen.Settings ? 430f : 350f;
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

                // Team switching (team modes only; the server validates balance).
                var gameMode = GameModeManager.Instance;
                if (gameMode != null && gameMode.IsSpawned && gameMode.IsTeamMode)
                {
                    if (MenuWidgets.MenuButton("SWITCH TEAM"))
                    {
                        var nm = NetworkManager.Singleton;
                        var playerObject = nm != null && nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
                        var player = playerObject != null ? playerObject.GetComponent<NetworkPlayer>() : null;
                        if (player != null)
                            player.RequestTeamChange();
                    }
                    GUILayout.Space(8);
                }

                if (gameMode != null && gameMode.IsSpawned)
                {
                    if (MenuWidgets.MenuButton("TOGGLE SPECTATOR"))
                    {
                        var local = NetworkManager.Singleton?.LocalClient?.PlayerObject;
                        local?.GetComponent<NetworkPlayer>()?.RequestSpectatorToggle();
                        MouseLook.LockCursor(true);
                    }
                    GUILayout.Space(8);
                }

                if (MenuWidgets.MenuButton("SETTINGS"))
                    screen = MenuScreen.Settings;
                GUILayout.Space(8);
                if (MenuWidgets.MenuButton("LEAVE MATCH"))
                    Leave();
            }
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------
        // Hosting / joining (LOCAL / LAN via UnityTransport)
        // ------------------------------------------------------------------

        private void StartHost()
        {
            var nm = NetworkManager.Singleton;
            GameModeManager.PendingHostMode = selectedMode;
            GameModeManager.PendingHostMap = selectedMap;
            GameModeManager.PendingSniperOnly = sniperOnly;
            GameModeManager.PendingRadarMode = radarMode;
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
            if (NetworkManager.Singleton.StartClient())
            {
                connecting = true;
                connectStart = Time.time;
            }
            else
            {
                status = "Failed to start client.";
            }
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
