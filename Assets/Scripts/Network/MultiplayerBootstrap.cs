using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Builds the multiplayer arena when the scene loads (identical on every
    /// client — the arena is fully deterministic) and provides the overview
    /// "menu camera" shown before joining a match.
    /// </summary>
    public class MultiplayerBootstrap : MonoBehaviour
    {
        public static MultiplayerBootstrap Instance { get; private set; }

        public float arenaSize = 60f;

        private GameObject menuCamera;

        private void Awake()
        {
            Instance = this;

            EnvironmentBuilder.SetupLightingAndSky();
            EnvironmentBuilder.BuildArena(arenaSize);

            menuCamera = new GameObject("MenuCamera");
            var cam = menuCamera.AddComponent<Camera>();
            menuCamera.AddComponent<AudioListener>();
            PostFx.Attach(cam);
            menuCamera.transform.position = new Vector3(0, 22f, -36f);
            menuCamera.transform.rotation = Quaternion.Euler(32f, 0f, 0f);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            // Slow cinematic orbit around the arena while the menu is up.
            if (menuCamera != null && menuCamera.activeSelf)
            {
                float angle = Time.time * 0.08f;
                menuCamera.transform.position = new Vector3(Mathf.Sin(angle) * 38f, 20f, Mathf.Cos(angle) * 38f);
                menuCamera.transform.LookAt(new Vector3(0, 1.5f, 0));
            }
        }

        /// <summary>Shown in the menu, hidden while playing (the player camera takes over).</summary>
        public void SetMenuCamera(bool active)
        {
            if (menuCamera != null)
                menuCamera.SetActive(active);
        }
    }
}
