using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Minimal HUD drawn with IMGUI (OnGUI) so it needs no Canvas, fonts or assets:
    /// crosshair, ammo counter, health bar, and a pause hint when the cursor is unlocked.
    /// Swap this for a real uGUI/UI Toolkit interface in your actual games.
    /// </summary>
    public class HudOverlay : MonoBehaviour
    {
        public Gun gun;
        public Health playerHealth;

        public float crosshairSize = 6f;

        private Texture2D whiteTex;
        private GUIStyle textStyle;

        private void Awake()
        {
            whiteTex = Texture2D.whiteTexture;
        }

        private void OnGUI()
        {
            if (textStyle == null)
            {
                textStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 20,
                    fontStyle = FontStyle.Bold,
                };
                textStyle.normal.textColor = Color.white;
            }

            DrawCrosshair();
            DrawAmmo();
            DrawHealth();
            DrawCursorHint();
        }

        private void DrawCrosshair()
        {
            float cx = Screen.width / 2f;
            float cy = Screen.height / 2f;
            float s = crosshairSize;

            GUI.color = new Color(1f, 1f, 1f, 0.9f);
            // Four small lines around the center.
            GUI.DrawTexture(new Rect(cx - 1, cy - s - 8, 2, s), whiteTex);      // top
            GUI.DrawTexture(new Rect(cx - 1, cy + 8, 2, s), whiteTex);          // bottom
            GUI.DrawTexture(new Rect(cx - s - 8, cy - 1, s, 2), whiteTex);      // left
            GUI.DrawTexture(new Rect(cx + 8, cy - 1, s, 2), whiteTex);          // right
            GUI.color = Color.white;
        }

        private void DrawAmmo()
        {
            if (gun == null)
                return;

            string text = gun.IsReloading
                ? "RELOADING..."
                : $"{gun.CurrentAmmo} / {gun.magazineSize}";

            GUI.Label(new Rect(Screen.width - 220, Screen.height - 50, 200, 40), text, textStyle);
        }

        private void DrawHealth()
        {
            if (playerHealth == null)
                return;

            float barWidth = 200f;
            float fill = Mathf.Clamp01(playerHealth.Current / playerHealth.maxHealth);
            var barRect = new Rect(20, Screen.height - 44, barWidth, 24);

            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(barRect, whiteTex);
            GUI.color = Color.Lerp(Color.red, Color.green, fill);
            GUI.DrawTexture(new Rect(barRect.x, barRect.y, barWidth * fill, barRect.height), whiteTex);
            GUI.color = Color.white;

            GUI.Label(new Rect(24, Screen.height - 46, 200, 30), Mathf.CeilToInt(playerHealth.Current).ToString(), textStyle);
        }

        private void DrawCursorHint()
        {
            if (Cursor.lockState == CursorLockMode.Locked)
                return;

            var rect = new Rect(0, Screen.height / 2f + 40, Screen.width, 40);
            var centered = new GUIStyle(textStyle) { alignment = TextAnchor.MiddleCenter };
            GUI.Label(rect, "PAUSED — click to play  (WASD move · Shift sprint · Space jump · Mouse1 shoot · R reload)", centered);
        }
    }
}
