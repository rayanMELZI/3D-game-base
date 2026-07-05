using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// In-game HUD (IMGUI, zero assets): crosshair + hit marker, sniper scope,
    /// big panel-backed ammo block with weapon slots and a reload bar, big
    /// health bar with a low-HP warning vignette.
    /// The kill feed / scores / scoreboard live in NetworkGameHud.
    /// </summary>
    public class HudOverlay : MonoBehaviour
    {
        public WeaponController weaponController;

        /// <summary>Health readout (offline Health or multiplayer NetworkHealth). Set from code.</summary>
        public IHealthSource HealthSource { get; set; }

        public float crosshairSize = 7f;

        private Texture2D whiteTex;
        private GUIStyle bigNumber;
        private GUIStyle mediumText;
        private GUIStyle smallText;
        private float lastHitTime = -10f;
        private bool lastHitWasHeadshot;
        private bool subscribed;

        private void Awake()
        {
            whiteTex = Texture2D.whiteTexture;
        }

        private void Update()
        {
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
            if (bigNumber == null)
            {
                bigNumber = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 42, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight,
                };
                bigNumber.normal.textColor = Color.white;
                mediumText = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
                mediumText.normal.textColor = new Color(1f, 1f, 1f, 0.92f);
                smallText = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
                smallText.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
            }

            bool zoomed = weaponController != null && weaponController.IsZoomed
                && weaponController.CurrentWeapon.hideWhenZoomed;
            if (zoomed)
                DrawScopeOverlay();
            else
                DrawCrosshair();

            DrawHitMarker();
            DrawAmmoPanel();
            DrawHealthPanel();
        }

        private void Panel(Rect rect)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(rect, whiteTex);
            GUI.color = MenuWidgets.Accent;
            GUI.DrawTexture(new Rect(rect.x, rect.y, 3, rect.height), whiteTex);
            GUI.color = Color.white;
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
            GUI.color = lastHitWasHeadshot
                ? new Color(1f, 0.2f, 0.15f, 1f - age / 0.18f)
                : new Color(1f, 1f, 1f, 0.85f * (1f - age / 0.18f));

            var oldMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, new Vector2(cx, cy));
            GUI.DrawTexture(new Rect(cx - 1, cy - 16, 2, 10), whiteTex);
            GUI.DrawTexture(new Rect(cx - 1, cy + 6, 2, 10), whiteTex);
            GUI.DrawTexture(new Rect(cx - 16, cy - 1, 10, 2), whiteTex);
            GUI.DrawTexture(new Rect(cx + 6, cy - 1, 10, 2), whiteTex);
            GUI.matrix = oldMatrix;
            GUI.color = Color.white;
        }

        private void DrawScopeOverlay()
        {
            float w = Screen.width, h = Screen.height;
            float cx = w / 2f, cy = h / 2f;
            float r = h * 0.42f;

            GUI.color = new Color(0f, 0f, 0f, 0.96f);
            GUI.DrawTexture(new Rect(0, 0, cx - r, h), whiteTex);
            GUI.DrawTexture(new Rect(cx + r, 0, w - (cx + r), h), whiteTex);
            GUI.DrawTexture(new Rect(cx - r, 0, 2 * r, cy - r), whiteTex);
            GUI.DrawTexture(new Rect(cx - r, cy + r, 2 * r, h - (cy + r)), whiteTex);

            GUI.color = new Color(0f, 0f, 0f, 0.9f);
            GUI.DrawTexture(new Rect(cx - r, cy - 0.5f, 2 * r, 1), whiteTex);
            GUI.DrawTexture(new Rect(cx - 0.5f, cy - r, 1, 2 * r), whiteTex);
            GUI.color = Color.white;
        }

        // ------------------------------------------------------------------

        private void DrawAmmoPanel()
        {
            if (weaponController == null || weaponController.CurrentWeapon == null)
                return;
            MenuWidgets.EnsureStyles();

            var weapon = weaponController.CurrentWeapon;
            var panel = new Rect(Screen.width - 330, Screen.height - 128, 310, 84);
            Panel(panel);

            // Weapon name.
            GUI.Label(new Rect(panel.x + 16, panel.y + 8, 200, 24), weapon.displayName, mediumText);

            // Big ammo readout (∞ for the knife).
            string ammoText = weapon.magazineSize <= 0
                ? "∞"
                : $"{weaponController.CurrentAmmo}";
            bigNumber.normal.textColor = weaponController.IsReloading
                ? new Color(1f, 0.7f, 0.3f)
                : (weapon.magazineSize > 0 && weaponController.CurrentAmmo == 0
                    ? new Color(1f, 0.35f, 0.3f) : Color.white);
            GUI.Label(new Rect(panel.x + 100, panel.y + 18, 130, 50), ammoText, bigNumber);
            if (weapon.magazineSize > 0)
                GUI.Label(new Rect(panel.x + 236, panel.y + 40, 70, 26), $"/ {weapon.magazineSize}", mediumText);

            // Reload progress bar.
            if (weaponController.IsReloading)
            {
                var barRect = new Rect(panel.x + 16, panel.y + panel.height - 14, panel.width - 32, 6);
                GUI.color = new Color(1f, 1f, 1f, 0.2f);
                GUI.DrawTexture(barRect, whiteTex);
                GUI.color = new Color(1f, 0.7f, 0.3f);
                GUI.DrawTexture(new Rect(barRect.x, barRect.y, barRect.width * weaponController.ReloadProgress, barRect.height), whiteTex);
                GUI.color = Color.white;
            }

            // Weapon slot numbers.
            for (int i = 0; i < weaponController.weapons.Length; i++)
            {
                bool current = i == weaponController.CurrentIndex;
                smallText.normal.textColor = current ? MenuWidgets.Accent : new Color(1f, 1f, 1f, 0.4f);
                GUI.Label(new Rect(panel.x + 14 + i * 26, panel.y + panel.height + 4, 26, 20), (i + 1).ToString(), smallText);
            }
            smallText.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
        }

        private void DrawHealthPanel()
        {
            if (HealthSource == null)
                return;

            float hp = HealthSource.CurrentHealth;
            float fill = HealthSource.MaxHealth > 0 ? Mathf.Clamp01(hp / HealthSource.MaxHealth) : 0f;

            var panel = new Rect(20, Screen.height - 118, 290, 74);
            Panel(panel);

            bigNumber.normal.textColor = Color.Lerp(new Color(1f, 0.3f, 0.25f), Color.white, fill);
            var numStyle = new GUIStyle(bigNumber) { alignment = TextAnchor.MiddleLeft };
            GUI.Label(new Rect(panel.x + 16, panel.y + 6, 110, 48), Mathf.CeilToInt(hp).ToString(), numStyle);

            var barRect = new Rect(panel.x + 16, panel.y + panel.height - 18, panel.width - 32, 10);
            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            GUI.DrawTexture(barRect, whiteTex);
            GUI.color = Color.Lerp(new Color(0.9f, 0.25f, 0.2f), new Color(0.4f, 0.9f, 0.45f), fill);
            GUI.DrawTexture(new Rect(barRect.x, barRect.y, barRect.width * fill, barRect.height), whiteTex);
            GUI.color = Color.white;

            // Low-health red vignette.
            if (fill < 0.35f)
            {
                float alpha = (0.35f - fill) / 0.35f * 0.35f;
                GUI.color = new Color(0.8f, 0.1f, 0.05f, alpha);
                float t = 26f;
                GUI.DrawTexture(new Rect(0, 0, Screen.width, t), whiteTex);
                GUI.DrawTexture(new Rect(0, Screen.height - t, Screen.width, t), whiteTex);
                GUI.DrawTexture(new Rect(0, 0, t, Screen.height), whiteTex);
                GUI.DrawTexture(new Rect(Screen.width - t, 0, t, Screen.height), whiteTex);
                GUI.color = Color.white;
            }
        }
    }
}
