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
        // Version comes from Unity: Project Settings > Player > Version
        // (Application.version / bundleVersion). Do not hardcode it here.


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
        // Equipment slots: which throwable is on the lethal (G) and tactical (E)
        // key when playing the class loadout. Indices into ThrowableController.
        public static int LethalType;
        public static int TacticalType;

        // Weapon add-ons: one bitmask per class AND global weapon index
        // ([class][weapon], see AttachmentType), persisted permanently. Every
        // class can kit out any weapon, so the same rifle can carry an optic in
        // one class and a suppressor in another. In full-arsenal mode the
        // SELECTED class's add-ons apply to every weapon. WeaponController
        // snapshots them on spawn; NetworkWeapon replicates the held weapon's.
        public static readonly int[][] ClassAttachments = new int[ClassCount][];
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
            LethalType = Mathf.Clamp(PlayerPrefs.GetInt("lethalType", 0), 0, ThrowableController.Count - 1);   // frag
            TacticalType = Mathf.Clamp(PlayerPrefs.GetInt("tacticalType", 1), 0, ThrowableController.Count - 1); // flash
            // Clamped to the arsenal so a pref from another build can't index past it.
            var loadout = WeaponDefinition.CreateDefaultLoadout();
            for (int i = 0; i < ClassCount; i++)
            {
                ClassPrimary[i] = Mathf.Clamp(PlayerPrefs.GetInt($"class{i}Primary", defaultPrimary[i]), 0, loadout.Length - 1);
                ClassSecondary[i] = Mathf.Clamp(PlayerPrefs.GetInt($"class{i}Secondary", defaultSecondary[i]), 0, loadout.Length - 1);
            }

            // Per-class add-ons, sized to the arsenal and sanitised against each
            // weapon's allowed set (so an old pref can't leave an illegal add-on on).
            // Migration: before add-ons were per class they were global per weapon
            // ("weaponAtt{i}"); a class with no saved value inherits that choice.
            for (int c = 0; c < ClassCount; c++)
            {
                ClassAttachments[c] = new int[loadout.Length];
                for (int i = 0; i < loadout.Length; i++)
                {
                    int legacy = PlayerPrefs.GetInt($"weaponAtt{i}", 0);
                    ClassAttachments[c][i] = Attachments.Sanitize(
                        PlayerPrefs.GetInt($"class{c}Att{i}", legacy), loadout[i].model);
                }
            }
            WeaponColors = new int[loadout.Length];
            for (int i = 0; i < loadout.Length; i++)
                WeaponColors[i] = Mathf.Clamp(PlayerPrefs.GetInt($"weaponColor{i}", 0), 0, 5);
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
            PlayerPrefs.SetInt("lethalType", LethalType);
            PlayerPrefs.SetInt("tacticalType", TacticalType);
            for (int i = 0; i < ClassCount; i++)
            {
                PlayerPrefs.SetInt($"class{i}Primary", ClassPrimary[i]);
                PlayerPrefs.SetInt($"class{i}Secondary", ClassSecondary[i]);
            }
            for (int c = 0; c < ClassCount; c++)
                for (int i = 0; i < ClassAttachments[c].Length; i++)
                    PlayerPrefs.SetInt($"class{c}Att{i}", ClassAttachments[c][i]);
            for (int i = 0; i < WeaponColors.Length; i++)
                PlayerPrefs.SetInt($"weaponColor{i}", WeaponColors[i]);
            PlayerPrefs.Save();
        }

        /// <summary>Saved add-on mask of a weapon in a class (0 when out of range).</summary>
        public static int GetAttachments(int classIndex, int weaponIndex)
        {
            if (classIndex < 0 || classIndex >= ClassCount)
                return 0;
            var masks = ClassAttachments[classIndex];
            return weaponIndex >= 0 && weaponIndex < masks.Length ? masks[weaponIndex] : 0;
        }

        public static void SetAttachments(int classIndex, int weaponIndex, int mask)
        {
            if (classIndex < 0 || classIndex >= ClassCount)
                return;
            var masks = ClassAttachments[classIndex];
            if (weaponIndex >= 0 && weaponIndex < masks.Length)
                masks[weaponIndex] = mask;
        }

        public static void AwardExperience(int amount)
        {
            Experience = Mathf.Max(0, Experience + amount);
            PlayerPrefs.SetInt("experience", Experience);
            PlayerPrefs.Save();
        }
    }
}
