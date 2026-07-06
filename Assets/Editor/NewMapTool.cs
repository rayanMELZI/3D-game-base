using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FpsBase.EditorTools
{
    /// <summary>
    /// "Tools > FPS Base > New Map" — creates a starter map you edit visually in
    /// the Unity editor (no code). It makes a prefab in Assets/Resources/Maps/
    /// with a ground plane, a few walls and 8 spawn markers, then opens it in
    /// Prefab Mode. Add/move objects, press Ctrl+S, and the map automatically
    /// shows up in the game's map list.
    /// </summary>
    public static class NewMapTool
    {
        [MenuItem("Tools/FPS Base/New Map")]
        public static void CreateMap()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "New Map", "MyMap", "prefab",
                "Name your map. It will be saved in Assets/Resources/Maps/.",
                "Assets/Resources/Maps");
            if (string.IsNullOrEmpty(path))
                return;

            // Must live under a Resources/Maps folder so the game can load it.
            if (!path.Replace("\\", "/").Contains("/Resources/Maps/"))
            {
                EnsureFolder("Assets/Resources");
                EnsureFolder("Assets/Resources/Maps");
                path = "Assets/Resources/Maps/" + System.IO.Path.GetFileName(path);
            }

            var root = BuildStarterMap();
            try
            {
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                AssetDatabase.Refresh();
                Selection.activeObject = prefab;
                AssetDatabase.OpenAsset(prefab); // enter Prefab Mode for editing

                EditorUtility.DisplayDialog("FPS Base — New Map",
                    "Your map opened in Prefab Mode.\n\n" +
                    "• Add objects: GameObject > 3D Object (they need colliders — primitives have them).\n" +
                    "• Move the colored SpawnPoint markers where players should appear\n" +
                    "  (blue = team 0, orange = team 1, green = anyone).\n" +
                    "• Press Ctrl+S to save. The map appears in the game's map list automatically.\n\n" +
                    "Tip: you can drag in imported models/props too — just give them colliders.",
                    "Got it");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject BuildStarterMap()
        {
            var root = new GameObject("Map");

            // Ground.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(root.transform, false);
            ground.transform.localScale = Vector3.one * 5f; // 50x50m

            // A few starter walls so it isn't totally empty.
            CreateWall(root.transform, "Wall North", new Vector3(0, 2, 24), new Vector3(48, 4, 1));
            CreateWall(root.transform, "Wall South", new Vector3(0, 2, -24), new Vector3(48, 4, 1));
            CreateWall(root.transform, "Wall East", new Vector3(24, 2, 0), new Vector3(1, 4, 48));
            CreateWall(root.transform, "Wall West", new Vector3(-24, 2, 0), new Vector3(1, 4, 48));
            CreateWall(root.transform, "Center Block", new Vector3(0, 1, 0), new Vector3(6, 2, 6));

            // Spawn markers: 4 per team, facing the center.
            for (int i = 0; i < 4; i++)
            {
                float x = -9f + i * 6f;
                CreateSpawn(root.transform, 0, new Vector3(x, 0, -20), 0f);
                CreateSpawn(root.transform, 1, new Vector3(x, 0, 20), 180f);
            }
            return root;
        }

        private static void CreateWall(Transform parent, string name, Vector3 pos, Vector3 scale)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.SetParent(parent, false);
            wall.transform.localPosition = pos;
            wall.transform.localScale = scale;
        }

        private static void CreateSpawn(Transform parent, int team, Vector3 pos, float yaw)
        {
            var go = new GameObject($"Spawn_{(team == 0 ? "Blue" : "Orange")}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            go.AddComponent<SpawnPoint>().team = team;
        }

        private static void EnsureFolder(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                string parent = System.IO.Path.GetDirectoryName(folder).Replace("\\", "/");
                string leaf = System.IO.Path.GetFileName(folder);
                AssetDatabase.CreateFolder(parent, leaf);
            }
        }
    }
}
