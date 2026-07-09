using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FpsBase.EditorTools
{
    /// <summary>
    /// "Tools > FPS Base > Bake Built-in Map > ..." — turns a code-built map
    /// (Arena / Backrooms / Nuketown 2025) into a regular prefab you can edit
    /// visually, saved into Assets/Resources/Maps/ so it appears in the game's
    /// map list automatically.
    ///
    /// This is the proper version of "copy the map out of play mode": that
    /// loses every code-generated material (the prefab turns pink). This tool
    /// saves each generated material (and generated ground texture) as a real
    /// asset in Assets/MapMaterials/&lt;map name&gt;/ and wires them up, then adds
    /// SpawnPoint markers matching the built-in spawn candidates.
    /// </summary>
    public static class BakeMapTool
    {
        [MenuItem("Tools/FPS Base/Bake Built-in Map/Arena")]
        public static void BakeArena() => Bake("My Arena",
            () => EnvironmentBuilder.BuildArena(60f),
            EnvironmentBuilder.BuiltinSpawnCandidates(false, 0),
            EnvironmentBuilder.BuiltinSpawnCandidates(false, 1));

        [MenuItem("Tools/FPS Base/Bake Built-in Map/Backrooms")]
        public static void BakeBackrooms() => Bake("My Backrooms",
            EnvironmentBuilder.BuildBackrooms,
            EnvironmentBuilder.BuiltinSpawnCandidates(true, 0),
            EnvironmentBuilder.BuiltinSpawnCandidates(true, 1));

        [MenuItem("Tools/FPS Base/Bake Built-in Map/Nuketown 2025")]
        public static void BakeNuketown() => Bake("My Nuketown",
            EnvironmentBuilder.BuildNuketown,
            EnvironmentBuilder.NuketownSpawnCandidates(0),
            EnvironmentBuilder.NuketownSpawnCandidates(1));

        // ------------------------------------------------------------------

        private static void Bake(string defaultName, System.Action build, Vector3[] spawns0, Vector3[] spawns1)
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Bake Map To Prefab", defaultName, "prefab",
                "Name your editable copy. It will be saved in Assets/Resources/Maps/ " +
                "and appear in the game's map list under this name.",
                "Assets/Resources/Maps");
            if (string.IsNullOrEmpty(path))
                return;

            if (!path.Replace("\\", "/").Contains("/Resources/Maps/"))
            {
                EnsureFolder("Assets/Resources");
                EnsureFolder("Assets/Resources/Maps");
                path = "Assets/Resources/Maps/" + System.IO.Path.GetFileName(path);
            }

            string mapName = System.IO.Path.GetFileNameWithoutExtension(path);

            // Build the map exactly like the game does, then adopt the root.
            build();
            var root = GameObject.Find(EnvironmentBuilder.MapRootName);
            if (root == null)
            {
                EditorUtility.DisplayDialog("FPS Base — Bake Map", "Map build failed (no MapRoot).", "OK");
                return;
            }

            try
            {
                root.name = mapName;

                // Code-generated materials/textures can't live inside a prefab —
                // save each unique one as a real asset and re-point the renderers.
                string matFolder = "Assets/MapMaterials/" + mapName;
                EnsureFolder("Assets/MapMaterials");
                EnsureFolder(matFolder);
                SaveGeneratedMaterials(root, matFolder);

                // Spawn markers (blue = team 0, orange = team 1), facing the center.
                foreach (var pos in spawns0) AddSpawn(root.transform, 0, pos);
                foreach (var pos in spawns1) AddSpawn(root.transform, 1, pos);

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                MapCatalog.Refresh();

                Selection.activeObject = prefab;
                AssetDatabase.OpenAsset(prefab); // straight into Prefab Mode

                EditorUtility.DisplayDialog("FPS Base — Bake Map",
                    $"'{mapName}' is now an editable prefab (opened in Prefab Mode).\n\n" +
                    "• Move/delete/add anything — new objects need colliders.\n" +
                    "• The SpawnPoint markers are where players appear; drag them around.\n" +
                    "• Materials live in " + matFolder + " — edit colors there.\n" +
                    "• Ctrl+S saves; the map shows up in the game's map list automatically.",
                    "Got it");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>Save every unique generated material (+ its generated texture) as assets.</summary>
        private static void SaveGeneratedMaterials(GameObject root, string folder)
        {
            var saved = new Dictionary<Material, Material>();
            var usedNames = new HashSet<string>();

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var mat = mats[i];
                    if (mat == null || AssetDatabase.Contains(mat))
                        continue; // already an asset (or empty slot)

                    if (!saved.TryGetValue(mat, out var asset))
                    {
                        string baseName = Sanitize(renderer.gameObject.name);
                        string name = baseName;
                        for (int n = 2; !usedNames.Add(name); n++)
                            name = baseName + "_" + n;

                        // Generated textures (e.g. the tiled ground) must become
                        // assets too, or the material breaks on reload.
                        if (mat.mainTexture != null && !AssetDatabase.Contains(mat.mainTexture))
                            AssetDatabase.CreateAsset(mat.mainTexture, $"{folder}/{name}_Tex.asset");

                        AssetDatabase.CreateAsset(mat, $"{folder}/{name}.mat");
                        saved[mat] = asset = mat;
                    }
                    mats[i] = asset;
                    changed = true;
                }
                if (changed)
                    renderer.sharedMaterials = mats;
            }
        }

        private static void AddSpawn(Transform parent, int team, Vector3 pos)
        {
            var go = new GameObject($"Spawn_{(team == 0 ? "Blue" : "Orange")}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            // Face the map center, matching how respawns orient players.
            Vector3 toCenter = new Vector3(-pos.x, 0f, -pos.z);
            if (toCenter.sqrMagnitude > 0.01f)
                go.transform.localRotation = Quaternion.LookRotation(toCenter);
            go.AddComponent<SpawnPoint>().team = team;
        }

        private static string Sanitize(string name)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
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
