using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Player-facing settings, persisted with PlayerPrefs and applied live.
    /// Read by MouseLook (sensitivity/FOV), the audio system (volume) and the menu.
    /// </summary>
    public static class GameSettings
    {
        /// <summary>Shown on the main menu — rename your game here.</summary>
        public const string GameTitle = "SUNDOWN ARENA";
        /// <summary>0.MINOR.PATCH — bump MINOR for features, PATCH for fixes.
        /// Git tags follow the same number (v0.5.0 style).</summary>
        public const string Version = "0.13.4";

        /// <summary>
        /// URL of a plain-text file containing the latest version string
        /// (e.g. https://raw.githubusercontent.com/you/repo/main/version.txt).
        /// Empty = update checking disabled.
        /// </summary>
        public const string UpdateCheckUrl = "";

        public static string PlayerName;
        public static float MouseSensitivity; // multiplier, 0.2–3
        public static float Fov;              // 50–90
        public static float Volume;           // 0–1
        public static int QualityLevel;
        public static int Experience;
        public static int Level => 1 + Mathf.FloorToInt(Mathf.Sqrt(Experience / 100f));

        // Weapon classes (CoD-style loadouts): 3 slots, each = primary +
        // secondary weapon index into the default loadout; the knife is always
        // carried. Applied on every (re)spawn; Gun Game / snipers-only override.
        public const int ClassCount = 3;
        public static int SelectedClass;
        public static bool UseClassLoadout;
        public static readonly int[] ClassPrimary = new int[ClassCount];
        public static readonly int[] ClassSecondary = new int[ClassCount];

        // Weapon add-ons: one bitmask per GLOBAL weapon index (see AttachmentType).
        // Chosen per weapon, shared by every class, and persisted permanently.
        // Applied by WeaponController and replicated by NetworkWeapon.
        public static readonly int[] WeaponAttachments;
        public static readonly int[] WeaponColors;

        static GameSettings()
        {
            PlayerName = PlayerPrefs.GetString("playerName", "Player" + Random.Range(100, 999));
            MouseSensitivity = PlayerPrefs.GetFloat("sensitivity", 1f);
            Fov = PlayerPrefs.GetFloat("fov", 60f);
            Volume = PlayerPrefs.GetFloat("volume", 0.8f);
            QualityLevel = Mathf.Clamp(
                PlayerPrefs.GetInt("quality", QualitySettings.GetQualityLevel()),
                0, QualitySettings.names.Length - 1);
            Experience = Mathf.Max(0, PlayerPrefs.GetInt("experience", 0));

            // Default classes: assault, close-quarters, long-range.
            int[] defaultPrimary = { 4, 2, 5 };   // rifle, smg, sniper
            int[] defaultSecondary = { 1, 3, 1 }; // pistol, shotgun, pistol
            SelectedClass = Mathf.Clamp(PlayerPrefs.GetInt("selectedClass", 0), 0, ClassCount - 1);
            UseClassLoadout = PlayerPrefs.GetInt("useClassLoadout", 1) != 0;
            for (int i = 0; i < ClassCount; i++)
            {
                ClassPrimary[i] = PlayerPrefs.GetInt($"class{i}Primary", defaultPrimary[i]);
                ClassSecondary[i] = PlayerPrefs.GetInt($"class{i}Secondary", defaultSecondary[i]);
            }

            // Per-weapon add-ons, sized to the arsenal and sanitised against each
            // weapon's allowed set (so an old pref can't leave an illegal add-on on).
            var loadout = WeaponDefinition.CreateDefaultLoadout();
            WeaponAttachments = new int[loadout.Length];
            WeaponColors = new int[loadout.Length];
            for (int i = 0; i < loadout.Length; i++)
            {
                WeaponAttachments[i] = Attachments.Sanitize(PlayerPrefs.GetInt($"weaponAtt{i}", 0), loadout[i].model);
                WeaponColors[i] = Mathf.Clamp(PlayerPrefs.GetInt($"weaponColor{i}", 0), 0, 5);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ApplyOnBoot() => Apply();

        public static void Apply()
        {
            AudioListener.volume = Volume;
            if (QualityLevel >= 0 && QualityLevel < QualitySettings.names.Length
                && QualityLevel != QualitySettings.GetQualityLevel())
                QualitySettings.SetQualityLevel(QualityLevel, true);
        }

        public static void Save()
        {
            PlayerPrefs.SetString("playerName", PlayerName);
            PlayerPrefs.SetFloat("sensitivity", MouseSensitivity);
            PlayerPrefs.SetFloat("fov", Fov);
            PlayerPrefs.SetFloat("volume", Volume);
            PlayerPrefs.SetInt("quality", QualityLevel);
            PlayerPrefs.SetInt("experience", Experience);
            PlayerPrefs.SetInt("selectedClass", SelectedClass);
            PlayerPrefs.SetInt("useClassLoadout", UseClassLoadout ? 1 : 0);
            for (int i = 0; i < ClassCount; i++)
            {
                PlayerPrefs.SetInt($"class{i}Primary", ClassPrimary[i]);
                PlayerPrefs.SetInt($"class{i}Secondary", ClassSecondary[i]);
            }
            for (int i = 0; i < WeaponAttachments.Length; i++)
            {
                PlayerPrefs.SetInt($"weaponAtt{i}", WeaponAttachments[i]);
                PlayerPrefs.SetInt($"weaponColor{i}", WeaponColors[i]);
            }
            PlayerPrefs.Save();
        }

        public static void AwardExperience(int amount)
        {
            Experience = Mathf.Max(0, Experience + amount);
            PlayerPrefs.SetInt("experience", Experience);
            PlayerPrefs.Save();
        }
    }
}
