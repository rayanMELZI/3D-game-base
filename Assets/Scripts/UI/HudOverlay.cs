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
        public static HudOverlay Local { get; private set; }
        public WeaponController weaponController;

        /// <summary>Health readout (offline Health or multiplayer NetworkHealth). Set from code.</summary>
        public IHealthSource HealthSource { get; set; }

        public float crosshairSize = 7f;

        private Texture2D whiteTex;
        private GUIStyle bigNumber;
        private GUIStyle leftNumber;
        private GUIStyle mediumText;
        private GUIStyle smallText;
        private float lastHitTime = -10f;
        private bool lastHitWasHeadshot;
        private bool subscribed;
        private float damageIndicatorUntil;
        private float damageAngle;
        private float lastHealth = -1f;
        private float breathingUntil;
        private AudioSource breathingSource;
        private float flashUntil;
        private float flashStrength;

        private void Awake()
        {
            Local = this;
            whiteTex = Texture2D.whiteTexture;
        }

        private void OnDestroy()
        {
            if (Local == this) Local = null;
        }

        public void NotifyDamage(Vector3 sourcePosition)
        {
            Vector3 direction = sourcePosition - transform.position;
            direction.y = 0f;
            damageAngle = Vector3.SignedAngle(transform.forward, direction, Vector3.up);
            damageIndicatorUntil = Time.time + 1.15f;
            breathingUntil = Time.time + 5f;
            EnsureBreathing();
        }

        public void NotifyFlash(float strength)
        {
            flashStrength = Mathf.Max(flashStrength, strength);
            flashUntil = Time.time + 2.8f * strength;
        }

        private void EnsureBreathing()
        {
            if (breathingSource != null) return;
            breathingSource = gameObject.AddComponent<AudioSource>();
            breathingSource.clip = SfxSynth.HeavyBreathing();
            breathingSource.loop = true;
            breathingSource.spatialBlend = 0f;
            breathingSource.volume = 0f;
            breathingSource.Play();
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
            if (HealthSource != null)
            {
                float hp = HealthSource.CurrentHealth;
                if (lastHealth >= 0f && hp < lastHealth)
                {
                    breathingUntil = Time.time + 5f;
                    EnsureBreathing();
                }
                lastHealth = hp;
                if (breathingSource != null)
                {
                    float low = hp < HealthSource.MaxHealth * 0.35f && Time.time < breathingUntil ? 1f : 0f;
                    breathingSource.volume = Mathf.MoveTowards(breathingSource.volume, low * 0.42f, Time.deltaTime * 0.8f);
                }
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
                leftNumber = new GUIStyle(bigNumber) { alignment = TextAnchor.MiddleLeft };
            }

            // While the kill cam / replay owns the camera, the combat HUD would
            // just float over someone else's view — draw nothing (NetworkGameHud
            // still shows the KILLCAM label and kill feed).
            if (DeathCam.CurrentLabel != null)
                return;

            bool zoomed = weaponController != null && weaponController.IsZoomed
                && weaponController.CurrentWeapon.hideWhenZoomed;
            if (zoomed)
                DrawScopeOverlay();
            else
                DrawCrosshair();

            DrawHitMarker();
            DrawDamageDirection();
            DrawAmmoPanel();
            DrawHealthPanel();
            DrawRadar();
            DrawEquipment();
            if (Time.time < flashUntil)
            {
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01((flashUntil - Time.time) * flashStrength));
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), whiteTex);
                GUI.color = Color.white;
            }
        }

        // Equipment strip (bottom-center): shows the throwable keys + cooldowns
        // so throwing feels deliberate, not like random hotkeys.
        private void DrawEquipment()
        {
            if (throwables == null)
            {
                throwables = GetComponent<ThrowableController>();
                if (throwables == null) return;
            }

            const float bw = 74f, bh = 30f, gap = 6f;
            int n = ThrowableController.Count;
            float totalW = n * bw + (n - 1) * gap;
            float x0 = (Screen.width - totalW) / 2f;
            float y = Screen.height - 92f;

            for (int i = 0; i < n; i++)
            {
                var rect = new Rect(x0 + i * (bw + gap), y, bw, bh);
                float cd = throwables.CooldownFraction(i);

                GUI.color = new Color(0f, 0f, 0f, 0.5f);
                GUI.DrawTexture(rect, whiteTex);
                if (cd > 0f) // cooldown fills from the left
                {
                    GUI.color = new Color(0.6f, 0.25f, 0.15f, 0.55f);
                    GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * cd, rect.height), whiteTex);
                }
                GUI.color = MenuWidgets.Accent;
                GUI.DrawTexture(new Rect(rect.x, rect.y, 3f, rect.height), whiteTex);
                GUI.color = Color.white;

                if (smallText != null)
                    GUI.Label(rect, $" {ThrowableController.Keys[i].ToString()[0]}·{ThrowableController.Names[i]}", smallText);
            }
        }

        private ThrowableController throwables;

        private void DrawDamageDirection()
        {
            if (Time.time >= damageIndicatorUntil) return;
            float alpha = Mathf.Clamp01((damageIndicatorUntil - Time.time) / 0.45f);
            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            var old = GUI.matrix;
            GUIUtility.RotateAroundPivot(damageAngle, center);
            GUI.color = new Color(1f, 0.12f, 0.08f, alpha);
            GUI.DrawTexture(new Rect(center.x - 28f, center.y - 170f, 56f, 7f), whiteTex);
            GUI.DrawTexture(new Rect(center.x - 16f, center.y - 163f, 32f, 4f), whiteTex);
            GUI.matrix = old;
            GUI.color = Color.white;
        }

        // ------------------------------------------------------------------
        // Minimap radar (top-left): teammates always, enemies per host setting
        // (always visible, or pinged for a few seconds when they fire).
        // ------------------------------------------------------------------

        private const float RadarSize = 150f;   // pixels
        private const float RadarRange = 42f;   // meters covered edge to edge/2
        private const float FirePingSeconds = 3f;

        private NetworkPlayer[] radarPlayers = System.Array.Empty<NetworkPlayer>();
        private NetworkPlayer radarSelf;
        private float nextRadarScan;

        private void DrawRadar()
        {
            var gameMode = GameModeManager.Instance;
            if (gameMode == null || !gameMode.IsSpawned || gameMode.RadarMode.Value == 0)
                return;
            bool alwaysShow = gameMode.RadarMode.Value == 1;

            // Throttled player scan (FindObjectsByType every frame is wasteful).
            if (Time.time >= nextRadarScan)
            {
                nextRadarScan = Time.time + 0.5f;
                radarPlayers = FindObjectsByType<NetworkPlayer>();
                radarSelf = GetComponent<NetworkPlayer>();
            }
            if (radarSelf == null)
                return;

            var rect = new Rect(16f, 16f, RadarSize, RadarSize);
            Panel(rect);

            // Rotate the world into radar space so "up" is where I'm looking.
            float myYaw = transform.eulerAngles.y;
            Vector3 myPos = transform.position;
            Vector2 center = new Vector2(rect.x + RadarSize / 2f, rect.y + RadarSize / 2f);

            foreach (var player in radarPlayers)
            {
                if (player == null || player == radarSelf || !player.IsSpawned || player.IsDead.Value)
                    continue;

                bool teammate = gameMode.IsTeamMode && player.Team.Value == radarSelf.Team.Value;
                if (!teammate && !alwaysShow)
                {
                    // "On fire" mode: enemies only ping for a few seconds after shooting.
                    var weapon = player.GetComponent<NetworkWeapon>();
                    if (weapon == null || Time.time - weapon.LastShotTime > FirePingSeconds)
                        continue;
                }

                Vector3 delta = player.transform.position - myPos;
                var local = Quaternion.Euler(0f, -myYaw, 0f) * delta;
                var blip = new Vector2(local.x, -local.z) * (RadarSize / 2f / RadarRange);
                blip = Vector2.ClampMagnitude(blip, RadarSize / 2f - 6f);

                GUI.color = teammate
                    ? new Color(0.35f, 0.65f, 1f)
                    : new Color(1f, 0.25f, 0.2f);
                GUI.DrawTexture(new Rect(center.x + blip.x - 4f, center.y + blip.y - 4f, 8f, 8f), whiteTex);
            }

            // Me: white arrowhead in the center (always pointing up = my facing).
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(center.x - 3f, center.y - 5f, 6f, 10f), whiteTex);
            GUI.color = new Color(1f, 1f, 1f, 0.25f);
            GUI.DrawTexture(new Rect(center.x - RadarSize / 2f + 6f, center.y - 0.5f, RadarSize - 12f, 1f), whiteTex);
            GUI.DrawTexture(new Rect(center.x - 0.5f, center.y - RadarSize / 2f + 6f, 1f, RadarSize - 12f), whiteTex);
            GUI.color = Color.white;
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
                GUI.Label(new Rect(panel.x + 236, panel.y + 40, 70, 26), $"/ {weaponController.CurrentMagSize}", mediumText);

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

            leftNumber.normal.textColor = Color.Lerp(new Color(1f, 0.3f, 0.25f), Color.white, fill);
            GUI.Label(new Rect(panel.x + 16, panel.y + 6, 110, 48), Mathf.CeilToInt(hp).ToString(), leftNumber);

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
