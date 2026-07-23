using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Match HUD for multiplayer: mode-aware scores top-center, kill feed
    /// top-right, hold-Tab scoreboard (K/D, teams, gun game progress),
    /// win banner with the final-kill cam, and death/kill-cam messages.
    /// </summary>
    public class NetworkGameHud : MonoBehaviour
    {
        private GUIStyle scoreStyle;
        private GUIStyle feedStyle;
        private GUIStyle bannerStyle;
        private GUIStyle rowStyle;
        private GUIStyle modeStyle;   // reused (was allocated every frame)
        private GUIStyle subStyle;
        private GUIStyle headerStyle;
        private int lastWinner = -1;

        // Cached player lookups — FindObjectsByType is slow and allocates, so we
        // must not call it every OnGUI (which runs twice per frame). Refresh a
        // few times a second instead.
        private NetworkPlayer[] cachedPlayers = new NetworkPlayer[0];
        private float nextPlayerRefresh;
        private NetworkPlayer cachedLocal;

        private NetworkPlayer[] Players()
        {
            if (Time.unscaledTime >= nextPlayerRefresh || cachedPlayers.Length == 0)
            {
                cachedPlayers = FindObjectsByType<NetworkPlayer>();
                nextPlayerRefresh = Time.unscaledTime + 0.4f;
            }
            return cachedPlayers;
        }

        private void Update()
        {
            var gameMode = GameModeManager.Instance;
            if (gameMode == null || !gameMode.IsSpawned)
                return;
            if (gameMode.LobbyOpen.Value)
                MouseLook.LockCursor(false);

            // Win jingle + final kill cam (BO2 style: everyone watches the last killer).
            int winner = gameMode.WinnerTeam.Value;
            if (winner != lastWinner)
            {
                if (winner >= 0)
                {
                    SfxSynth.Play2D(SfxSynth.WinJingle(), 0.9f);
                    var killer = FindPlayer(gameMode.LastKillerId.Value);
                    if (killer != null)
                        DeathCam.Begin(killer.transform, "FINAL KILL");
                }
                else
                {
                    DeathCam.End(); // match restarted
                }
                lastWinner = winner;
            }
        }

        private void OnGUI()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || (!nm.IsClient && !nm.IsServer))
                return;
            var gameMode = GameModeManager.Instance;
            if (gameMode == null || !gameMode.IsSpawned)
                return;

            EnsureStyles();
            if (gameMode.LobbyOpen.Value)
            {
                DrawLobby(nm, gameMode);
                return;
            }
            DrawScores(gameMode);
            DrawKillFeed(gameMode);
            if (Input.GetKey(KeyCode.Tab))
                DrawScoreboard(gameMode);
            DrawBanners(nm, gameMode);
        }

        private void DrawLobby(NetworkManager nm, GameModeManager gameMode)
        {
            // The class editor overlay (opened from here) draws on top; hide the
            // lobby behind it so they don't fight.
            if (MainMenu.ShowClassesOverlay)
                return;

            // Full-screen dim.
            GUI.color = new Color(0.03f, 0.04f, 0.06f, 0.92f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            bannerStyle.normal.textColor = MenuWidgets.Accent;
            GUI.Label(new Rect(0, 26, Screen.width, 56), "PRE-GAME LOBBY", bannerStyle);
            subStyle.normal.textColor = new Color(1f, 1f, 1f, 0.8f);
            GUI.Label(new Rect(0, 84, Screen.width, 24),
                nm.IsHost ? "You are the host — set up the match, then START."
                          : "Waiting for the host to start… you can prepare your class.", subStyle);

            float top = 130f;
            float colH = Screen.height - top - 40f;
            float centerX = Screen.width * 0.5f;

            // ---- LEFT: player list ----
            var left = new Rect(centerX - 470f, top, 300f, colH);
            Panel(left);
            headerStyle.normal.textColor = MenuWidgets.Accent;
            GUI.Label(new Rect(left.x + 16, left.y + 12, left.width - 32, 24), "PLAYERS", headerStyle);
            var players = Players();
            float ry = left.y + 46f;
            rowStyle.alignment = TextAnchor.MiddleLeft; // CenteredRow() mutates the shared style
            foreach (var p in players)
            {
                if (p == null) continue;
                bool spec = p.Spectating.Value;
                rowStyle.normal.textColor = spec ? new Color(1f, 1f, 1f, 0.5f)
                    : EnvironmentBuilder.TeamColor(p.Team.Value);
                string name = p.PlayerName.Value.IsEmpty ? $"Player {p.OwnerClientId + 1}" : p.PlayerName.Value.ToString();
                string tag = spec ? "  (spectator)" : $"  ·  lvl {p.CareerLevel.Value}";
                GUI.Label(new Rect(left.x + 16, ry, left.width - 32, 24), name + tag, rowStyle);
                ry += 26f;
            }

            // ---- RIGHT: map "card" (name + mode; no real thumbnail exists) ----
            var right = new Rect(centerX + 170f, top, 300f, colH);
            Panel(right);
            headerStyle.normal.textColor = MenuWidgets.Accent;
            GUI.Label(new Rect(right.x + 16, right.y + 12, right.width - 32, 24), "MAP", headerStyle);
            var pic = new Rect(right.x + 16, right.y + 46, right.width - 32, 150f);
            GUI.color = new Color(0.15f, 0.17f, 0.22f, 1f);
            GUI.DrawTexture(pic, Texture2D.whiteTexture);
            GUI.color = MenuWidgets.Accent;
            GUI.DrawTexture(new Rect(pic.x, pic.y, pic.width, 3f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            bannerStyle.fontSize = 26;
            GUI.Label(pic, MapCatalog.Name(gameMode.MapIndex.Value), bannerStyle);
            bannerStyle.fontSize = 46;
            subStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(right.x + 16, pic.yMax + 10, right.width - 32, 60), ModeTitle(gameMode), subStyle);

            // ---- CENTER: vertical settings menu ----
            var menu = new Rect(centerX - 140f, top, 280f, colH);
            float my = menu.y;
            const float bh = 40f, gap = 8f;
            Rect Slot() { var r = new Rect(menu.x, my, menu.width, bh); my += bh + gap; return r; }

            // Everyone can edit their own class here.
            if (GUI.Button(Slot(), "EDIT CLASSES / GUNSMITH"))
                MainMenu.ShowClassesOverlay = true;
            my += 6f;

            if (nm.IsHost)
            {
                var mapRow = Slot();
                if (GUI.Button(new Rect(mapRow.x, mapRow.y, 54, bh), "◀")) gameMode.HostCycleMap(-1);
                GUI.Label(new Rect(mapRow.x + 60, mapRow.y, mapRow.width - 120, bh), "MAP", CenteredRow());
                if (GUI.Button(new Rect(mapRow.xMax - 54, mapRow.y, 54, bh), "▶")) gameMode.HostCycleMap(1);

                if (GUI.Button(Slot(), $"MODE: {ModeShort(gameMode)}")) gameMode.HostCycleMode();
                if (GUI.Button(Slot(), gameMode.SniperOnly.Value ? "SNIPERS ONLY: ON" : "SNIPERS ONLY: OFF"))
                    gameMode.HostToggleSniper();
                if (GUI.Button(Slot(), $"MINIMAP: {RadarLabel(gameMode.RadarMode.Value)}"))
                    gameMode.HostCycleRadar();

                my += 6f;
                var start = Slot(); start.height = 52f;
                var prev = GUI.backgroundColor;
                GUI.backgroundColor = MenuWidgets.Accent;
                if (GUI.Button(start, "START MATCH")) gameMode.HostStartMatch();
                GUI.backgroundColor = prev;
            }

            // Spectator toggle for everyone.
            my += 6f;
            var local = LocalPlayer();
            if (local != null && GUI.Button(Slot(),
                local.Spectating.Value ? "SPECTATING (click to join)" : "JOIN AS SPECTATOR"))
                local.RequestSpectatorToggle();

            GUI.Label(new Rect(menu.x, menu.yMax - 26, menu.width, 22),
                "Players can join at any time, even mid-match.", modeStyle);
        }

        private void Panel(Rect r)
        {
            GUI.color = new Color(0.06f, 0.07f, 0.1f, 0.92f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        private GUIStyle CenteredRow()
        {
            rowStyle.alignment = TextAnchor.MiddleCenter;
            rowStyle.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
            return rowStyle;
        }

        private static string ModeShort(GameModeManager gm)
        {
            switch (gm.CurrentMode)
            {
                case GameMode.Duel: return "1V1";
                case GameMode.TeamDeathmatch: return "TDM";
                case GameMode.FreeForAll: return "FFA";
                case GameMode.ZombieSurvival: return "ZOMBIES";
                default: return "GUN GAME";
            }
        }

        private static string RadarLabel(int mode) =>
            mode == 0 ? "OFF" : mode == 1 ? "ALWAYS" : "ON FIRE";

        private void EnsureStyles()
        {
            if (scoreStyle != null)
                return;
            scoreStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
            };
            feedStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight,
            };
            bannerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 46, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
            };
            rowStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold };
            modeStyle = new GUIStyle(feedStyle) { alignment = TextAnchor.MiddleCenter, fontSize = 13 };
            subStyle = new GUIStyle(feedStyle) { alignment = TextAnchor.MiddleCenter };
            headerStyle = new GUIStyle(rowStyle) { fontSize = 14 };
        }

        // ------------------------------------------------------------------

        private string ModeTitle(GameModeManager gameMode)
        {
            string sniper = gameMode.SniperOnly.Value ? " · SNIPERS ONLY" : "";
            string map = MapCatalog.Name(gameMode.MapIndex.Value);
            switch (gameMode.CurrentMode)
            {
                case GameMode.Duel: return $"1V1 DUEL · first to {gameMode.ScoreLimit} · {map}{sniper}";
                case GameMode.TeamDeathmatch: return $"TEAM DEATHMATCH · first to {gameMode.ScoreLimit} · {map}{sniper}";
                case GameMode.FreeForAll: return $"FREE-FOR-ALL · first to {gameMode.ScoreLimit} · {map}{sniper}";
                case GameMode.ZombieSurvival: return $"ZOMBIE SURVIVAL · {map}";
                default: return $"GUN GAME · {GameModeManager.GunGameOrder.Length} weapons · {map}{sniper}";
            }
        }

        private void DrawScores(GameModeManager gameMode)
        {
            float cx = Screen.width / 2f;

            modeStyle.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
            GUI.Label(new Rect(cx - 260, 8, 520, 18), ModeTitle(gameMode), modeStyle);

            if (gameMode.IsTeamMode)
            {
                DrawScoreBlock(new Rect(cx - 110, 30, 90, 42), EnvironmentBuilder.Team0Color, gameMode.ScoreTeam0.Value.ToString());
                DrawScoreBlock(new Rect(cx + 20, 30, 90, 42), EnvironmentBuilder.Team1Color, gameMode.ScoreTeam1.Value.ToString());
            }
            else
            {
                var local = LocalPlayer();
                if (local != null)
                {
                    string progress = gameMode.CurrentMode == GameMode.GunGame
                        ? $"WEAPON {Mathf.Min(local.GunGameLevel.Value + 1, GameModeManager.GunGameOrder.Length)} / {GameModeManager.GunGameOrder.Length}"
                        : $"KILLS {local.Kills.Value} / {gameMode.ScoreLimit}";
                    DrawScoreBlock(new Rect(cx - 130, 30, 260, 42), EnvironmentBuilder.TeamColor(local.Team.Value), progress);
                }
            }
        }

        private void DrawScoreBlock(Rect rect, Color color, string text)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - 4, rect.width, 4), Texture2D.whiteTexture);
            GUI.color = Color.white;
            scoreStyle.normal.textColor = color;
            GUI.Label(rect, text, scoreStyle);
        }

        private void DrawKillFeed(GameModeManager gameMode)
        {
            float y = 64f;
            float width = 430f;
            float x = Screen.width - width - 16f;
            foreach (var entry in gameMode.KillFeed)
            {
                float age = Time.time - entry.time;
                if (age > 7f)
                    continue;
                float alpha = Mathf.Clamp01(1.3f - age / 7f);

                GUI.color = new Color(0f, 0f, 0f, 0.45f * alpha);
                GUI.DrawTexture(new Rect(x, y, width, 26), Texture2D.whiteTexture);
                GUI.color = Color.white;
                feedStyle.normal.textColor = entry.message.Contains("HEADSHOT")
                    ? new Color(1f, 0.45f, 0.35f, alpha)
                    : new Color(1f, 1f, 1f, alpha);
                GUI.Label(new Rect(x + 8, y + 2, width - 16, 22), entry.message, feedStyle);
                y += 30f;
            }
        }

        // ------------------------------------------------------------------

        private void DrawScoreboard(GameModeManager gameMode)
        {
            var players = Players()
                .OrderByDescending(p => p.Kills.Value)
                .ThenBy(p => p.Deaths.Value)
                .ToArray();

            float w = 560f;
            float h = 92f + players.Length * 30f;
            var panel = new Rect((Screen.width - w) / 2f, Screen.height * 0.18f, w, h);
            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = MenuWidgets.Accent;
            GUI.DrawTexture(new Rect(panel.x, panel.y, panel.width, 3), Texture2D.whiteTexture);
            GUI.color = Color.white;

            headerStyle.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
            GUI.Label(new Rect(panel.x + 20, panel.y + 12, w - 40, 20), ModeTitle(gameMode), headerStyle);
            GUI.Label(new Rect(panel.x + 20, panel.y + 40, 300, 20), "PLAYER", headerStyle);
            GUI.Label(new Rect(panel.x + w - 170, panel.y + 40, 60, 20), "LVL", headerStyle);
            GUI.Label(new Rect(panel.x + w - 110, panel.y + 40, 40, 20), "K", headerStyle);
            GUI.Label(new Rect(panel.x + w - 60, panel.y + 40, 40, 20), "D", headerStyle);

            float y = panel.y + 66f;
            foreach (var player in players)
            {
                var color = EnvironmentBuilder.TeamColor(player.Team.Value);
                GUI.color = color;
                GUI.DrawTexture(new Rect(panel.x + 20, y + 5, 12, 12), Texture2D.whiteTexture);
                GUI.color = Color.white;

                rowStyle.normal.textColor = player.IsOwner ? MenuWidgets.Accent : Color.white;
                string name = player.PlayerName.Value.IsEmpty
                    ? $"Player {player.OwnerClientId + 1}" : player.PlayerName.Value.ToString();
                if (player.IsDead.Value)
                    name += "  (dead)";
                GUI.Label(new Rect(panel.x + 42, y, 300, 24), name, rowStyle);
                GUI.Label(new Rect(panel.x + w - 170, y, 60, 24), player.CareerLevel.Value.ToString(), rowStyle);
                GUI.Label(new Rect(panel.x + w - 110, y, 40, 24), player.Kills.Value.ToString(), rowStyle);
                GUI.Label(new Rect(panel.x + w - 60, y, 40, 24), player.Deaths.Value.ToString(), rowStyle);
                y += 30f;
            }
        }

        // ------------------------------------------------------------------

        private void DrawBanners(NetworkManager nm, GameModeManager gameMode)
        {
            if (!gameMode.IsMatchActive && gameMode.WinnerTeam.Value >= 0)
            {
                var color = EnvironmentBuilder.TeamColor(gameMode.WinnerTeam.Value);
                string winText = gameMode.WinnerName.Value.IsEmpty
                    ? (gameMode.WinnerTeam.Value == 0 ? "BLUE TEAM WINS" : "ORANGE TEAM WINS")
                    : $"{gameMode.WinnerName.Value}  WINS";

                bannerStyle.normal.textColor = color;
                GUI.Label(new Rect(0, Screen.height * 0.16f, Screen.width, 60), winText, bannerStyle);

                subStyle.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
                string camLabel = DeathCam.CurrentLabel;
                GUI.Label(new Rect(0, Screen.height * 0.16f + 62, Screen.width, 24),
                    camLabel != null ? camLabel + "  ·  next match starting soon..." : "Next match starting soon...", subStyle);
                return;
            }

            // Local death: ELIMINATED + kill cam label.
            var localPlayer = LocalPlayer();
            if (localPlayer != null && localPlayer.IsDead.Value)
            {
                bannerStyle.normal.textColor = new Color(1f, 0.3f, 0.25f);
                GUI.Label(new Rect(0, Screen.height * 0.14f, Screen.width, 56), "ELIMINATED", bannerStyle);

                subStyle.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
                string camLabel = DeathCam.CurrentLabel;
                GUI.Label(new Rect(0, Screen.height * 0.14f + 58, Screen.width, 24),
                    camLabel != null ? camLabel : "Respawning...", subStyle);
            }
        }

        // ------------------------------------------------------------------

        private NetworkPlayer LocalPlayer()
        {
            if (cachedLocal != null)
                return cachedLocal;
            var nm = NetworkManager.Singleton;
            var po = nm != null && nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
            cachedLocal = po != null ? po.GetComponent<NetworkPlayer>() : null;
            return cachedLocal;
        }

        private NetworkPlayer FindPlayer(ulong clientId)
        {
            foreach (var player in Players())
                if (player != null && player.OwnerClientId == clientId)
                    return player;
            return null;
        }
    }
}
