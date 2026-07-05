using Unity.Netcode;
using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Match HUD for multiplayer: mode + team scores at the top, kill feed on
    /// the left, win banner and respawn message. Reads GameModeManager state.
    /// </summary>
    public class NetworkGameHud : MonoBehaviour
    {
        private GUIStyle scoreStyle;
        private GUIStyle feedStyle;
        private GUIStyle bannerStyle;
        private int lastWinner = -1;

        private void Update()
        {
            // Play the little win jingle once when a team wins.
            var gameMode = GameModeManager.Instance;
            if (gameMode == null || !gameMode.IsSpawned)
                return;
            int winner = gameMode.WinnerTeam.Value;
            if (winner != lastWinner && winner >= 0)
                SfxSynth.Play2D(SfxSynth.WinJingle(), 0.9f);
            lastWinner = winner;
        }

        private void OnGUI()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || (!nm.IsClient && !nm.IsServer))
                return;
            var gameMode = GameModeManager.Instance;
            if (gameMode == null || !gameMode.IsSpawned)
                return;

            if (scoreStyle == null)
            {
                scoreStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                };
                feedStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
                bannerStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 42, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                };
            }

            DrawScores(gameMode);
            DrawKillFeed(gameMode);
            DrawBanners(nm, gameMode);
        }

        private void DrawScores(GameModeManager gameMode)
        {
            float cx = Screen.width / 2f;

            string modeName = gameMode.CurrentMode == GameMode.Duel
                ? $"1V1 DUEL · first to {gameMode.ScoreLimit}"
                : $"TEAM DEATHMATCH · first to {gameMode.ScoreLimit}";
            feedStyle.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
            var modeStyle = new GUIStyle(feedStyle) { alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(cx - 200, 8, 400, 18), modeName, modeStyle);

            scoreStyle.normal.textColor = EnvironmentBuilder.Team0Color;
            GUI.Label(new Rect(cx - 130, 26, 80, 36), gameMode.ScoreTeam0.Value.ToString(), scoreStyle);
            scoreStyle.normal.textColor = new Color(1f, 1f, 1f, 0.7f);
            GUI.Label(new Rect(cx - 40, 26, 80, 36), "—", scoreStyle);
            scoreStyle.normal.textColor = EnvironmentBuilder.Team1Color;
            GUI.Label(new Rect(cx + 50, 26, 80, 36), gameMode.ScoreTeam1.Value.ToString(), scoreStyle);
        }

        private void DrawKillFeed(GameModeManager gameMode)
        {
            float y = 10f;
            foreach (var entry in gameMode.KillFeed)
            {
                float age = Time.time - entry.time;
                if (age > 6f)
                    continue;
                feedStyle.normal.textColor = new Color(1f, 1f, 1f, Mathf.Clamp01(1.2f - age / 6f));
                GUI.Label(new Rect(12, y, 500, 20), entry.message, feedStyle);
                y += 20f;
            }
        }

        private void DrawBanners(NetworkManager nm, GameModeManager gameMode)
        {
            // Win banner + restart notice.
            if (!gameMode.IsMatchActive && gameMode.WinnerTeam.Value >= 0)
            {
                bool blueWon = gameMode.WinnerTeam.Value == 0;
                bannerStyle.normal.textColor = blueWon ? EnvironmentBuilder.Team0Color : EnvironmentBuilder.Team1Color;
                GUI.Label(new Rect(0, Screen.height * 0.3f, Screen.width, 60),
                    blueWon ? "BLUE TEAM WINS" : "ORANGE TEAM WINS", bannerStyle);

                feedStyle.normal.textColor = new Color(1f, 1f, 1f, 0.8f);
                var sub = new GUIStyle(feedStyle) { alignment = TextAnchor.MiddleCenter };
                GUI.Label(new Rect(0, Screen.height * 0.3f + 60, Screen.width, 24), "Next match starting soon...", sub);
                return;
            }

            // "You are dead" message for the local player.
            var localPlayerObject = nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
            if (localPlayerObject != null)
            {
                var player = localPlayerObject.GetComponent<NetworkPlayer>();
                if (player != null && player.IsDead.Value)
                {
                    bannerStyle.normal.textColor = new Color(1f, 0.3f, 0.25f);
                    GUI.Label(new Rect(0, Screen.height * 0.35f, Screen.width, 50), "ELIMINATED", bannerStyle);
                    var sub = new GUIStyle(feedStyle)
                    {
                        alignment = TextAnchor.MiddleCenter,
                    };
                    sub.normal.textColor = new Color(1f, 1f, 1f, 0.8f);
                    GUI.Label(new Rect(0, Screen.height * 0.35f + 50, Screen.width, 24), "Respawning...", sub);
                }
            }
        }
    }
}
