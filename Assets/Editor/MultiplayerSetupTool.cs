using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FpsBase.EditorTools
{
    /// <summary>
    /// One-click multiplayer setup. Netcode needs a real player prefab asset
    /// (with an editor-computed NetworkObject id) and a scene with a configured
    /// NetworkManager — this tool bakes both:
    ///   Assets/Prefabs/NetworkPlayer.prefab
    ///   Assets/Scenes/Multiplayer.unity
    /// Safe to run again at any time; it simply rebuilds both assets.
    /// </summary>
    public static class MultiplayerSetupTool
    {
        private const string PrefabPath = "Assets/Prefabs/NetworkPlayer.prefab";
        private const string ScenePath = "Assets/Scenes/Multiplayer.unity";

        [MenuItem("Tools/FPS Base/Setup Multiplayer (Scene + Player Prefab)")]
        public static void Setup()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var playerPrefab = BuildPlayerPrefab();
            BuildMultiplayerScene(playerPrefab);
            AddScenesToBuildSettings();
            EnsureAlwaysIncludedShaders();

            EditorUtility.DisplayDialog("FPS Base",
                "Multiplayer setup complete.\n\n" +
                "The Multiplayer scene is now open — press Play, then Host or Join.",
                "OK");
        }

        // ------------------------------------------------------------------

        private static GameObject BuildPlayerPrefab()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
                AssetDatabase.CreateFolder("Assets", "Prefabs");

            var root = new GameObject("NetworkPlayer");
            try
            {
                PlayerFactory.BuildPlayerRig(root);

                root.AddComponent<NetworkObject>();

                var netTransform = root.AddComponent<ClientAuthNetworkTransform>();
                netTransform.SyncScaleX = false;
                netTransform.SyncScaleY = false;
                netTransform.SyncScaleZ = false;

                root.AddComponent<NetworkHealth>();
                root.AddComponent<NetworkPlayer>();
                root.AddComponent<NetworkWeapon>();

                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void BuildMultiplayerScene(GameObject playerPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // NetworkManager + transport, wired to the player prefab.
            var managerGo = new GameObject("NetworkManager");
            var networkManager = managerGo.AddComponent<NetworkManager>();
            var transport = managerGo.AddComponent<UnityTransport>();
            if (networkManager.NetworkConfig == null)
                networkManager.NetworkConfig = new NetworkConfig();
            networkManager.NetworkConfig.NetworkTransport = transport;
            networkManager.NetworkConfig.PlayerPrefab = playerPrefab;
            networkManager.NetworkConfig.ConnectionApproval = true; // enforced player limits
            transport.ConnectionData.Address = "127.0.0.1";
            transport.ConnectionData.Port = MainMenu.Port;
            transport.ConnectionData.ServerListenAddress = "0.0.0.0";

            // Match logic (in-scene network object).
            var gameModeGo = new GameObject("GameModeManager");
            gameModeGo.AddComponent<NetworkObject>();
            gameModeGo.AddComponent<GameModeManager>();

            // Arena builder + menu camera.
            new GameObject("MultiplayerBootstrap").AddComponent<MultiplayerBootstrap>();

            // Main menu, pause menu and match HUD.
            var uiGo = new GameObject("UI");
            uiGo.AddComponent<MainMenu>();
            uiGo.AddComponent<NetworkGameHud>();

            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        /// <summary>
        /// The game creates all materials at runtime with Shader.Find, which only
        /// works in standalone builds if the shaders are force-included.
        /// </summary>
        private static void EnsureAlwaysIncludedShaders()
        {
            string[] shaderNames = { "Standard", "Skybox/Procedural", "Sprites/Default", "Hidden/SundownPost" };

            var graphicsSettings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0];
            var serialized = new SerializedObject(graphicsSettings);
            var list = serialized.FindProperty("m_AlwaysIncludedShaders");

            foreach (var name in shaderNames)
            {
                var shader = Shader.Find(name);
                if (shader == null)
                    continue;

                bool present = false;
                for (int i = 0; i < list.arraySize; i++)
                {
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                    {
                        present = true;
                        break;
                    }
                }
                if (!present)
                {
                    list.InsertArrayElementAtIndex(list.arraySize);
                    list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
                }
            }
            serialized.ApplyModifiedProperties();
        }

        private static void AddScenesToBuildSettings()
        {
            // The menu scene MUST be scene 0 — it's what a build boots into.
            var ordered = new System.Collections.Generic.List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(ScenePath, true),                 // main menu + multiplayer
                new EditorBuildSettingsScene("Assets/Scenes/Main.unity", true), // offline practice range
            };
            // Keep any other scenes the user added, after ours.
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (ordered.All(s => s.path != scene.path))
                    ordered.Add(scene);
            }
            EditorBuildSettings.scenes = ordered.ToArray();
        }
    }
}
