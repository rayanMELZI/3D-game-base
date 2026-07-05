using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Minimal HUD drawn with IMGUI (OnGUI) so it needs no Canvas, fonts or assets:
    /// crosshair, hit marker, sniper scope overlay, weapon/ammo info, health bar.
    /// Swap this for a real uGUI/UI Toolkit interface in your actual games.
    /// </summary>
    public class HudOverlay : MonoBehaviour
    {
        public WeaponController weaponController;

        /// <summary>Health readout (offline Health or multiplayer NetworkHealth). Set from code.</summary>
        public IHealthSource HealthSource { get; set; }

        public float crosshairSize = 6f;

        private Texture2D whiteTex;
        private GUIStyle textStyle;
        private GUIStyle smallStyle;
        private float lastHitTime = -10f;
        private bool lastHitWasHeadshot;
        private bool subscribed;

        private void Awake()
        {
            whiteTex = Texture2D.whiteTexture;
        }

        private void Update()
        {
            // The weapon controller is assigned after AddComponent, so subscribe lazily.
            if (!subscribed && weaponController != null)
            {
                weaponController.TargetHit += headshot =>
                {
                    lastHitTime = Time.time;
                    lastHitWasHeadshot = headshot;
                };
                subscribed = true;
            }
        }

        private void OnGUI()
        {
            if (textStyle == null)
            {
                textStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
                textStyle.normal.textColor = Color.white;
                smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
                smallStyle.normal.textColor = new Color(1f, 1f, 1f, 0.65f);
            }

            bool zoomed = weaponController != null && weaponController.IsZoomed;
            if (zoomed)
                DrawScopeOverlay();
            else
                DrawCrosshair();

            DrawHitMarker();
            DrawWeaponInfo();
            DrawHealth();
        }

        // ------------------------------------------------------------------

        private void DrawCrosshair()
        {
            float cx = Screen.width / 2f;
            float cy = Screen.height / 2f;
            float s = crosshairSize;

            GUI.color = new Color(1f, 1f, 1f, 0.9f);
            GUI.DrawTexture(new Rect(cx - 1, cy - s - 8, 2, s), whiteTex);
            GUI.DrawTexture(new Rect(cx - 1, cy + 8, 2, s), whiteTex);
            GUI.DrawTexture(new Rect(cx - s - 8, cy - 1, s, 2), whiteTex);
            GUI.DrawTexture(new Rect(cx + 8, cy - 1, s, 2), whiteTex);
            GUI.color = Color.white;
        }

        private void DrawHitMarker()
        {
            float age = Time.time - lastHitTime;
            if (age > 0.18f)
                return;

            float cx = Screen.width / 2f;
            float cy = Screen.height / 2f;
            // Headshots flash a bigger red X, body hits a soft white one.
            GUI.color = lastHitWasHeadshot
                ? new Color(1f, 0.2f, 0.15f, 1f - age / 0.18f)
                : new Color(1f, 1f, 1f, 0.8f * (1f - age / 0.18f));

            // X shape: rotate the GUI 45° around the center, draw a plus, rotate back.
            var oldMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, new Vector2(cx, cy));
            GUI.DrawTexture(new Rect(cx - 1, cy - 14, 2, 9), whiteTex);
            GUI.DrawTexture(new Rect(cx - 1, cy + 5, 2, 9), whiteTex);
            GUI.DrawTexture(new Rect(cx - 14, cy - 1, 9, 2), whiteTex);
            GUI.DrawTexture(new Rect(cx + 5, cy - 1, 9, 2), whiteTex);
            GUI.matrix = oldMatrix;
            GUI.color = Color.white;
        }

        private void DrawScopeOverlay()
        {
            float w = Screen.width;
            float h = Screen.height;
            float cx = w / 2f;
            float cy = h / 2f;
            float r = h * 0.42f; // scope "circle" radius

            // Darken everything outside the center square area.
            GUI.color = new Color(0f, 0f, 0f, 0.96f);
            GUI.DrawTexture(new Rect(0, 0, cx - r, h), whiteTex);          // left
            GUI.DrawTexture(new Rect(cx + r, 0, w - (cx + r), h), whiteTex); // right
            GUI.DrawTexture(new Rect(cx - r, 0, 2 * r, cy - r), whiteTex);  // top
            GUI.DrawTexture(new Rect(cx - r, cy + r, 2 * r, h - (cy + r)), whiteTex); // bottom

            // Thin scope crosshair.
            GUI.color = new Color(0f, 0f, 0f, 0.9f);
            GUI.DrawTexture(new Rect(cx - r, cy - 0.5f, 2 * r, 1), whiteTex);
            GUI.DrawTexture(new Rect(cx - 0.5f, cy - r, 1, 2 * r), whiteTex);
            GUI.color = Color.white;
        }

        private void DrawWeaponInfo()
        {
            if (weaponController == null || weaponController.CurrentWeapon == null)
                return;

            var weapon = weaponController.CurrentWeapon;
            string ammoText = weaponController.IsReloading
                ? "RELOADING..."
                : $"{weapon.displayName}   {weaponController.CurrentAmmo} / {weapon.magazineSize}";
            GUI.Label(new Rect(Screen.width - 300, Screen.height - 54, 280, 40), ammoText, textStyle);

            // Weapon slot list, current one highlighted.
            for (int i = 0; i < weaponController.weapons.Length; i++)
            {
                bool current = i == weaponController.CurrentIndex;
                smallStyle.normal.textColor = current ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 1f, 1f, 0.45f);
                GUI.Label(new Rect(Screen.width - 300 + i * 90, Screen.height - 26, 90, 20),
                    $"{i + 1} {weaponController.weapons[i].displayName}", smallStyle);
            }
        }

        private void DrawHealth()
        {
            if (HealthSource == null)
                return;

            float barWidth = 200f;
            float fill = HealthSource.MaxHealth > 0 ? Mathf.Clamp01(HealthSource.CurrentHealth / HealthSource.MaxHealth) : 0f;
            var barRect = new Rect(20, Screen.height - 44, barWidth, 24);

            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(barRect, whiteTex);
            GUI.color = Color.Lerp(Color.red, Color.green, fill);
            GUI.DrawTexture(new Rect(barRect.x, barRect.y, barWidth * fill, barRect.height), whiteTex);
            GUI.color = Color.white;

            GUI.Label(new Rect(24, Screen.height - 46, 200, 30), Mathf.CeilToInt(HealthSource.CurrentHealth).ToString(), textStyle);
        }

    }
}
