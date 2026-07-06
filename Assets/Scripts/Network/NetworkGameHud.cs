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
                cachedPlayers = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);
                nextPlayerRefresh = Time.unscaledTime + 0.4f;
            }
            return cachedPlayers;
        }

        private void Update()
        {
            var gameMode = GameModeManager.Instance;
            if (gameMode == null || !gameMode.IsSpawned)
                return;

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
            DrawScores(gameMode);
            DrawKillFeed(gameMode);
            if (Input.GetKey(KeyCode.Tab))
                DrawScoreboard(gameMode);
            DrawBanners(nm, gameMode);
        }

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
            string map = EnvironmentBuilder.MapNames[
                Mathf.Clamp(gameMode.MapIndex.Value, 0, EnvironmentBuilder.MapNames.Length - 1)];
            switch (gameMode.CurrentMode)
            {
                case GameMode.Duel: return $"1V1 DUEL · first to {gameMode.ScoreLimit} · {map}{sniper}";
                case GameMode.TeamDeathmatch: return $"TEAM DEATHMATCH · first to {gameMode.ScoreLimit} · {map}{sniper}";
                case GameMode.FreeForAll: return $"FREE-FOR-ALL · first to {gameMode.ScoreLimit} · {map}{sniper}";
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
            GUI.Label(new Rect(panel.x + w - 170, panel.y + 40, 60, 20),
                gameMode.CurrentMode == GameMode.GunGame ? "LVL" : "", headerStyle);
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
                if (gameMode.CurrentMode == GameMode.GunGame)
                    GUI.Label(new Rect(panel.x + w - 170, y, 60, 24), (player.GunGameLevel.Value + 1).ToString(), rowStyle);
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
