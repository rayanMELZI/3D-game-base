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
        public const string Version = "0.5.0";

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

        // Weapon classes (CoD-style loadouts): 3 slots, each = primary +
        // secondary weapon index into the default loadout; the knife is always
        // carried. Applied on every (re)spawn; Gun Game / snipers-only override.
        public const int ClassCount = 3;
        public static int SelectedClass;
        public static readonly int[] ClassPrimary = new int[ClassCount];
        public static readonly int[] ClassSecondary = new int[ClassCount];

        static GameSettings()
        {
            PlayerName = PlayerPrefs.GetString("playerName", "Player" + Random.Range(100, 999));
            MouseSensitivity = PlayerPrefs.GetFloat("sensitivity", 1f);
            Fov = PlayerPrefs.GetFloat("fov", 60f);
            Volume = PlayerPrefs.GetFloat("volume", 0.8f);
            QualityLevel = Mathf.Clamp(
                PlayerPrefs.GetInt("quality", QualitySettings.GetQualityLevel()),
                0, QualitySettings.names.Length - 1);

            // Default classes: assault, close-quarters, long-range.
            int[] defaultPrimary = { 4, 2, 5 };   // rifle, smg, sniper
            int[] defaultSecondary = { 1, 3, 1 }; // pistol, shotgun, pistol
            SelectedClass = Mathf.Clamp(PlayerPrefs.GetInt("selectedClass", 0), 0, ClassCount - 1);
            for (int i = 0; i < ClassCount; i++)
            {
                ClassPrimary[i] = PlayerPrefs.GetInt($"class{i}Primary", defaultPrimary[i]);
                ClassSecondary[i] = PlayerPrefs.GetInt($"class{i}Secondary", defaultSecondary[i]);
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
            PlayerPrefs.SetInt("selectedClass", SelectedClass);
            for (int i = 0; i < ClassCount; i++)
            {
                PlayerPrefs.SetInt($"class{i}Primary", ClassPrimary[i]);
                PlayerPrefs.SetInt($"class{i}Secondary", ClassSecondary[i]);
            }
            PlayerPrefs.Save();
        }
    }
}
