using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace FpsBase.EditorTools
{
    /// <summary>
    /// Renders preview sheets without playing the game: every weapon bare and
    /// with all its add-ons (side + three-quarter views), every weapon as the
    /// first-person viewmodel on the Arena, and a few shots of each map.
    ///
    /// Used by .github/workflows/render-preview.yml (batch mode, no window) so
    /// visual changes can be checked from the PNGs it publishes, and from the
    /// menu Tools > FPS Base > Render Preview Sheets (output: PreviewRenders/).
    /// Also writes the renderer bounds of every weapon part and add-on model
    /// (bounds.txt) to help fit the add-ons.
    /// </summary>
    public static class PreviewRenderer
    {
        private const int CellW = 480;
        private const int CellH = 300;
        private const int WeaponsPerSheet = 5;

        private static readonly StringBuilder log = new StringBuilder();

        [MenuItem("Tools/FPS Base/Render Preview Sheets")]
        public static void RenderFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            string dir = RenderAll(Path.Combine(ProjectRoot, "PreviewRenders"));
            EditorUtility.RevealInFinder(dir);
        }

        /// <summary>Batch-mode entry point (-executeMethod).</summary>
        public static void RenderFromCI()
        {
            // game-ci's builder expects its build folder to exist afterwards.
            string buildPath = ArgValue("-customBuildPath");
            if (!string.IsNullOrEmpty(buildPath))
                Directory.CreateDirectory(Path.GetDirectoryName(buildPath) ?? buildPath);
            RenderAll(Path.Combine(ProjectRoot, "PreviewRenders"));
        }

        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        private static string ArgValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name)
                    return args[i + 1];
            return null;
        }

        private static string RenderAll(string outDir)
        {
            Directory.CreateDirectory(outDir);
            log.Clear();
            Application.logMessageReceived += Capture;
            try
            {
                log.AppendLine($"Graphics device: {SystemInfo.graphicsDeviceType} ({SystemInfo.graphicsDeviceName})");
                Step("weapons", () => RenderWeapons(outDir));
                Step("viewmodels", () => RenderViewmodels(outDir));
                Step("maps", () => RenderMaps(outDir));
            }
            finally
            {
                Application.logMessageReceived -= Capture;
                File.WriteAllText(Path.Combine(outDir, "log.txt"), log.ToString());
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            return outDir;
        }

        private static void Step(string name, Action action)
        {
            try { action(); }
            catch (Exception e) { log.AppendLine($"[{name}] FAILED: {e}"); }
        }

        private static void Capture(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Log)
                return;
            log.AppendLine($"{type}: {message}");
            if (type == LogType.Exception)
                log.AppendLine(stackTrace);
        }

        // ------------------------------------------------------------------
        // Weapons (studio)
        // ------------------------------------------------------------------

        private static void RenderWeapons(string outDir)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            StudioLighting();

            var weapons = WeaponDefinition.CreateDefaultLoadout();
            var bounds = new StringBuilder();
            var cam = MakeCamera(new Color(0.42f, 0.44f, 0.47f));
            cam.fieldOfView = 18f;

            int sheets = (weapons.Length + WeaponsPerSheet - 1) / WeaponsPerSheet;
            for (int s = 0; s < sheets; s++)
            {
                int rows = Mathf.Min(WeaponsPerSheet, weapons.Length - s * WeaponsPerSheet);
                var sheet = NewSheet(3, rows);
                for (int r = 0; r < rows; r++)
                {
                    int index = s * WeaponsPerSheet + r;
                    var def = weapons[index];
                    int full = Attachments.AllowedMask(def.model);

                    var bare = WeaponModelBuilder.Build(def, null, Vector3.zero, true, 0, 0);
                    DumpBounds(bounds, $"#{index} {def.displayName} ({Describe(def)})", bare.root, bare.muzzle);
                    var area = RendererBounds(bare.root);
                    FrameSide(cam, area);
                    DrawCell(cam, sheet, 0, rows - 1 - r);
                    UnityEngine.Object.DestroyImmediate(bare.root);

                    var kitted = WeaponModelBuilder.Build(def, null, Vector3.zero, true, full, 0);
                    DumpBounds(bounds, $"#{index} {def.displayName} +addons", kitted.root, kitted.muzzle);
                    area = RendererBounds(kitted.root);
                    FrameSide(cam, area);
                    DrawCell(cam, sheet, 1, rows - 1 - r);
                    FrameThreeQuarter(cam, area);
                    DrawCell(cam, sheet, 2, rows - 1 - r);
                    UnityEngine.Object.DestroyImmediate(kitted.root);
                }
                Save(sheet, Path.Combine(outDir, $"weapons_{s + 1:00}.png"));
            }

            foreach (var name in new[] { "Optic", "Suppressor", "Foregrip", "ExtendedMag" })
            {
                var prefab = Resources.Load<GameObject>("Weapons/Attachments/" + name);
                if (prefab == null) { bounds.AppendLine($"== add-on {name}: missing"); continue; }
                var go = UnityEngine.Object.Instantiate(prefab);
                DumpBounds(bounds, $"add-on {name} (scale 1)", go, null);
                UnityEngine.Object.DestroyImmediate(go);
            }

            File.WriteAllText(Path.Combine(outDir, "bounds.txt"), bounds.ToString());
            UnityEngine.Object.DestroyImmediate(cam.gameObject);
        }

        private static string Describe(WeaponDefinition def) =>
            string.IsNullOrEmpty(def.modelName) ? def.model.ToString() : def.modelName;

        private static void StudioLighting()
        {
            RenderSettings.fog = false;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.62f, 0.66f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.45f, 0.48f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.25f, 0.27f);

            var key = new GameObject("Key").AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.2f;
            key.color = new Color(1f, 0.95f, 0.88f);
            key.shadows = LightShadows.Soft;
            key.transform.rotation = Quaternion.Euler(40f, -120f, 0f);
            var fill = new GameObject("Fill").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.45f;
            fill.color = new Color(0.8f, 0.86f, 1f);
            fill.transform.rotation = Quaternion.Euler(20f, 60f, 0f);
        }

        private static void FrameSide(Camera cam, Bounds b)
        {
            float radius = Mathf.Max(b.extents.z, b.extents.y * 1.6f, 0.05f);
            float dist = radius / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.15f;
            // From the weapon's right side: the barrel (+Z) points right on screen.
            cam.transform.position = b.center + Vector3.right * dist;
            cam.transform.LookAt(b.center);
        }

        private static void FrameThreeQuarter(Camera cam, Bounds b)
        {
            float radius = Mathf.Max(b.extents.magnitude, 0.05f);
            float dist = radius / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.95f;
            var dir = new Vector3(-0.75f, 0.5f, 0.55f).normalized; // left side, from above and ahead
            cam.transform.position = b.center + dir * dist;
            cam.transform.LookAt(b.center);
        }

        private static void DumpBounds(StringBuilder sb, string title, GameObject root, Transform muzzle)
        {
            sb.AppendLine("== " + title);
            if (muzzle != null)
                sb.AppendLine($"   muzzle {Fmt(muzzle.position)}");
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                sb.AppendLine($"   {r.name,-34} min {Fmt(r.bounds.min)} max {Fmt(r.bounds.max)}");
        }

        private static string Fmt(Vector3 v) => $"({v.x:0.000}, {v.y:0.000}, {v.z:0.000})";

        // ------------------------------------------------------------------
        // First-person viewmodels on the Arena
        // ------------------------------------------------------------------

        private static void RenderViewmodels(string outDir)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EnvironmentBuilder.SetupLightingAndSky();
            MapCatalog.Build(0);
            DynamicGI.UpdateEnvironment();

            var cam = MakeCamera(Color.black);
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 70f;
            cam.nearClipPlane = 0.05f;
            PostFx.Attach(cam);
            cam.transform.position = new Vector3(-6f, 1.62f, -22f);
            cam.transform.rotation = Quaternion.Euler(4f, 20f, 0f);

            var weapons = WeaponDefinition.CreateDefaultLoadout();
            const int cols = 4, rows = 4;
            int perSheet = cols * rows;
            for (int s = 0; s * perSheet < weapons.Length; s++)
            {
                var sheet = NewSheet(cols, rows);
                for (int k = 0; k < perSheet && s * perSheet + k < weapons.Length; k++)
                {
                    var def = weapons[s * perSheet + k];
                    var model = WeaponModelBuilder.Build(def, cam.transform, def.viewOffset, false,
                        Attachments.AllowedMask(def.model), 0);
                    DrawCell(cam, sheet, k % cols, rows - 1 - k / cols);
                    UnityEngine.Object.DestroyImmediate(model.root);
                }
                Save(sheet, Path.Combine(outDir, $"viewmodels_{s + 1:00}.png"));
            }
            UnityEngine.Object.DestroyImmediate(cam.gameObject);
        }

        // ------------------------------------------------------------------
        // Maps
        // ------------------------------------------------------------------

        private static void RenderMaps(string outDir)
        {
            MapCatalog.Refresh();
            int count = MapCatalog.Count;
            const int cols = 3;
            var sheet = NewSheet(cols, count);
            for (int i = 0; i < count; i++)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EnvironmentBuilder.SetupLightingAndSky();
                try { MapCatalog.Build(i); }
                catch (Exception e) { log.AppendLine($"[map {MapCatalog.Name(i)}] build threw: {e.Message}"); }
                DynamicGI.UpdateEnvironment();

                var cam = MakeCamera(Color.black);
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.fieldOfView = 70f;
                cam.farClipPlane = 1000f;
                PostFx.Attach(cam);

                var area = MapBounds();
                var spawns = MapCatalog.SpawnCandidates(i, -1);
                Vector3 eye = spawns.Length > 0 ? spawns[0] + Vector3.up * 1.62f : area.center + Vector3.up * 1.62f;
                Vector3 target = new Vector3(area.center.x, eye.y, area.center.z);

                // 1: overview from high up, 2: eye level from a spawn toward the
                // middle, 3: eye level from another spawn.
                cam.transform.position = area.center + new Vector3(0f, area.extents.magnitude * 0.55f, -area.extents.magnitude * 0.75f);
                cam.transform.LookAt(area.center);
                DrawCell(cam, sheet, 0, count - 1 - i);

                cam.transform.position = eye;
                cam.transform.LookAt(target);
                DrawCell(cam, sheet, 1, count - 1 - i);

                Vector3 eye2 = spawns.Length > 1 ? spawns[spawns.Length - 1] + Vector3.up * 1.62f : eye + Vector3.forward * 4f;
                cam.transform.position = eye2;
                cam.transform.LookAt(new Vector3(target.x, eye2.y, target.z));
                DrawCell(cam, sheet, 2, count - 1 - i);

                log.AppendLine($"map row {i + 1}: {MapCatalog.Name(i)}");
            }
            Save(sheet, Path.Combine(outDir, "maps.png"));
        }

        private static Bounds MapBounds()
        {
            var root = GameObject.Find(EnvironmentBuilder.MapRootName);
            if (root == null)
                return new Bounds(Vector3.zero, Vector3.one * 40f);
            var b = RendererBounds(root);
            // Skies / huge backdrops would push the camera miles away.
            b.extents = Vector3.Min(b.extents, new Vector3(60f, 30f, 60f));
            return b;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static Camera MakeCamera(Color background)
        {
            var cam = new GameObject("PreviewCamera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 500f;
            cam.allowHDR = true;
            cam.allowMSAA = true;
            return cam;
        }

        private static Bounds RendererBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(root.transform.position, Vector3.one * 0.3f);
            var b = renderers[0].bounds;
            foreach (var r in renderers)
                b.Encapsulate(r.bounds);
            return b;
        }

        private static Texture2D NewSheet(int cols, int rows) =>
            new Texture2D(cols * CellW, rows * CellH, TextureFormat.RGB24, false);

        private static void DrawCell(Camera cam, Texture2D sheet, int col, int row)
        {
            var rt = RenderTexture.GetTemporary(CellW, CellH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB, 4);
            var previous = RenderTexture.active;
            cam.targetTexture = rt;
            cam.aspect = (float)CellW / CellH;
            cam.Render();
            RenderTexture.active = rt;
            sheet.ReadPixels(new Rect(0, 0, CellW, CellH), col * CellW, row * CellH);
            sheet.Apply(false);
            RenderTexture.active = previous;
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
        }

        private static void Save(Texture2D sheet, string path)
        {
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(sheet);
            log.AppendLine("wrote " + Path.GetFileName(path));
        }
    }
}
