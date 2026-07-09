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

        public int CurrentMap { get; private set; }

        private GameObject menuCamera;

        private void Awake()
        {
            Instance = this;

            EnvironmentBuilder.SetupLightingAndSky();
            MapCatalog.Build(0);
            CurrentMap = 0;

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

        /// <summary>Rebuilds the level for the selected map (synced by GameModeManager).</summary>
        public void SetMap(int index)
        {
            if (index == CurrentMap)
                return;
            var old = GameObject.Find(EnvironmentBuilder.MapRootName);
            if (old != null)
            {
                // Deactivate before the deferred Destroy so this frame's spawn
                // raycasts can't hit the outgoing map's colliders.
                old.SetActive(false);
                Destroy(old);
            }
            MapCatalog.Build(index);
            CurrentMap = index;
            // Register the new map's colliders with physics immediately — the
            // server picks (raycasts) spawn points in this same frame.
            Physics.SyncTransforms();
        }
    }
}
