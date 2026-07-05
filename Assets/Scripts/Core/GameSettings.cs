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
        public const string Version = "base v0.3";

        public static string PlayerName;
        public static float MouseSensitivity; // multiplier, 0.2–3
        public static float Fov;              // 50–90
        public static float Volume;           // 0–1
        public static int QualityLevel;

        static GameSettings()
        {
            PlayerName = PlayerPrefs.GetString("playerName", "Player" + Random.Range(100, 999));
            MouseSensitivity = PlayerPrefs.GetFloat("sensitivity", 1f);
            Fov = PlayerPrefs.GetFloat("fov", 60f);
            Volume = PlayerPrefs.GetFloat("volume", 0.8f);
            QualityLevel = Mathf.Clamp(
                PlayerPrefs.GetInt("quality", QualitySettings.GetQualityLevel()),
                0, QualitySettings.names.Length - 1);
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
            PlayerPrefs.Save();
        }
    }
}
