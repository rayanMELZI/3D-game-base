using UnityEngine;
using UnityEngine.SceneManagement;

namespace FpsBase
{
    /// <summary>
    /// Pause menu for the offline practice range (Escape): resume, settings,
    /// back to the main menu (when the game was started from it).
    /// </summary>
    public class OfflineMenu : MonoBehaviour
    {
        private bool showSettings;

        private void OnGUI()
        {
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                showSettings = false;
                return;
            }

            MenuWidgets.EnsureStyles();

            float w = 380f, h = showSettings ? 420f : 300f;
            var panel = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            MenuWidgets.Panel(panel);

            GUILayout.BeginArea(new Rect(panel.x + 30, panel.y + 22, w - 60, h - 44));
            GUILayout.Label("PRACTICE", MenuWidgets.Title, GUILayout.Height(50));
            GUILayout.Space(12);

            if (showSettings)
            {
                GameSettings.MouseSensitivity = MenuWidgets.Slider("Mouse sensitivity", GameSettings.MouseSensitivity, 0.2f, 3f, "0.00");
                GameSettings.Fov = Mathf.Round(MenuWidgets.Slider("Field of view", GameSettings.Fov, 50f, 90f, "0"));
                GameSettings.Volume = MenuWidgets.Slider("Volume", GameSettings.Volume, 0f, 1f, "0%");
                AudioListener.volume = GameSettings.Volume;

                GUILayout.Space(10);
                if (MenuWidgets.MenuButton("BACK", 32f))
                {
                    GameSettings.Save();
                    showSettings = false;
                }
            }
            else
            {
                if (MenuWidgets.MenuButton("RESUME"))
                    MouseLook.LockCursor(true);
                GUILayout.Space(8);
                if (MenuWidgets.MenuButton("SETTINGS"))
                    showSettings = true;
                GUILayout.Space(8);
                // Only offer the main menu if it's actually in the build / loadable.
                if (Application.CanStreamedLevelBeLoaded("Multiplayer")
                    && MenuWidgets.MenuButton("BACK TO MAIN MENU"))
                    SceneManager.LoadScene("Multiplayer");
            }
            GUILayout.EndArea();
        }
    }
}
