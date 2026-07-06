using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FpsBase
{
    /// <summary>
    /// Builds the environment (lighting, sky, maps) from code. Fully
    /// deterministic — every client builds the exact same level.
    /// Maps: 0 = Arena, 1 = Nuketown-style, 2 = Backrooms.
    /// </summary>
    public static class EnvironmentBuilder
    {
        public static readonly string[] MapNames = { "ARENA", "NUKETOWN", "BACKROOMS" };

        // Team palette: 0/1 are the classic team colors, the rest are used for
        // free-for-all / gun game where every player has their own color.
        private static readonly Color[] TeamPalette =
        {
            new Color(0.25f, 0.5f, 1f),    // blue
            new Color(1f, 0.55f, 0.15f),   // orange
            new Color(0.35f, 0.85f, 0.4f), // green
            new Color(0.7f, 0.4f, 1f),     // purple
            new Color(0.3f, 0.9f, 0.9f),   // cyan
            new Color(1f, 0.45f, 0.7f),    // pink
            new Color(0.95f, 0.9f, 0.3f),  // yellow
            new Color(0.9f, 0.3f, 0.25f),  // red
        };

        public static Color Team0Color => TeamPalette[0];
        public static Color Team1Color => TeamPalette[1];
        public static Color TeamColor(int team) => TeamPalette[Mathf.Abs(team) % TeamPalette.Length];

        // ------------------------------------------------------------------
        // Shared material cache
        //
        // IMPORTANT: Unity does NOT free a `new Material(...)` when the object
        // using it is destroyed — it leaks in native graphics memory. Anything
        // spawned repeatedly at runtime (weapon models, death/explosion cubes,
        // rockets) must reuse cached materials instead of allocating fresh ones,
        // or memory grows the whole session and the game slowly chokes.
        // These materials are never mutated, so sharing them is safe.
        // ------------------------------------------------------------------

        private static readonly Dictionary<long, Material> sharedMaterials = new Dictionary<long, Material>();

        public static Material SharedMaterial(Color color, float metallic = 0f, float smoothness = 0.5f, float emission = 0f)
        {
            // Key on quantized appearance so visually-identical requests share one material.
            long key = Quant(color.r) | (Quant(color.g) << 8) | (Quant(color.b) << 16)
                       | ((long)Quant(metallic) << 24) | ((long)Quant(smoothness) << 32)
                       | ((long)Quant(Mathf.Clamp01(emission / 4f)) << 40);

            if (sharedMaterials.TryGetValue(key, out var cached) && cached != null)
                return cached;

            var mat = emission > 0f ? MakeEmissiveMaterial(color, emission) : MakeMaterial(color, metallic, smoothness);
            sharedMaterials[key] = mat;
            return mat;
        }

        private static long Quant(float v) => (long)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

        // ------------------------------------------------------------------
        // Map catalog
        // ------------------------------------------------------------------

        public const string MapRootName = "MapRoot";

        public static void BuildMap(int index)
        {
            switch (index)
            {
                case 1: BuildNuketown(); break;
                case 2: BuildBackrooms(); break;
                default: BuildArena(60f); break;
            }
        }

        /// <summary>Spawn point for a side (0 = -Z, 1 = +Z); counter cycles the lanes.</summary>
        public static Vector3 GetSpawnPoint(int mapIndex, int side, int counter)
        {
            float sign = side == 0 ? -1f : 1f;
            float[] lanes;
            float z;
            switch (mapIndex)
            {
                case 1: lanes = new[] { 0f, -6f, 6f, -10f, 10f }; z = 21f; break;
                case 2: lanes = new[] { 0f, -12f, 12f, -6f, 6f }; z = 17f; break;
                default: lanes = new[] { 0f, -18f, 18f, -9f, 9f }; z = 24f; break;
            }
            return new Vector3(lanes[counter % lanes.Length], 0.1f, z * sign);
        }

        // ------------------------------------------------------------------
        // Lighting & sky (shared golden-hour look)
        // ------------------------------------------------------------------

        public static void SetupLightingAndSky()
        {
            var lightGo = new GameObject("Directional Light");
            var sun = lightGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.color = new Color(1f, 0.87f, 0.7f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.8f;
            lightGo.transform.rotation = Quaternion.Euler(28f, -40f, 0f);

            var sky = new Material(Shader.Find("Skybox/Procedural"));
            sky.SetFloat("_SunSize", 0.05f);
            sky.SetFloat("_SunSizeConvergence", 4f);
            sky.SetFloat("_AtmosphereThickness", 1.1f);
            sky.SetColor("_SkyTint", new Color(0.52f, 0.44f, 0.56f));
            sky.SetColor("_GroundColor", new Color(0.38f, 0.32f, 0.3f));
            sky.SetFloat("_Exposure", 1.3f);
            RenderSettings.skybox = sky;
            RenderSettings.sun = sun;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.6f, 0.56f, 0.62f);
            RenderSettings.ambientEquatorColor = new Color(0.48f, 0.42f, 0.44f);
            RenderSettings.ambientGroundColor = new Color(0.26f, 0.21f, 0.2f);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.008f;
            RenderSettings.fogColor = new Color(0.76f, 0.64f, 0.6f);
        }

        // ------------------------------------------------------------------
        // Map 0: Arena (symmetric across Z: side 0 spawns at -Z, side 1 at +Z)
        // ------------------------------------------------------------------

        public static void BuildArena(float size)
        {
            var root = new GameObject(MapRootName).transform;
            float half = size / 2f;

            Ground(root, size, size, new Color(0.46f, 0.43f, 0.41f), new Color(0.4f, 0.38f, 0.37f));

            // Perimeter walls with an emissive trim on top.
            var wallMat = MakeMaterial(new Color(0.5f, 0.52f, 0.55f), 0.1f, 0.35f);
            var trimMat = MakeEmissiveMaterial(new Color(0.2f, 0.8f, 0.9f), 2.4f);
            float wallH = 4f;
            for (int i = 0; i < 4; i++)
            {
                bool alongX = i < 2;
                float sign = (i % 2 == 0) ? 1f : -1f;
                Vector3 pos = alongX ? new Vector3(0, wallH / 2f, half * sign) : new Vector3(half * sign, wallH / 2f, 0);
                Vector3 scale = alongX ? new Vector3(size, wallH, 1f) : new Vector3(1f, wallH, size);
                Box(root, "Wall", pos, Vector3.zero, scale, wallMat);
                Vector3 trimScale = alongX ? new Vector3(size, 0.12f, 0.2f) : new Vector3(0.2f, 0.12f, size);
                Box(root, "WallTrim", pos + new Vector3(0, wallH / 2f + 0.06f, 0), Vector3.zero, trimScale, trimMat);
            }

            // Center platform with ramps and pillars.
            var platMat = MakeMaterial(new Color(0.44f, 0.46f, 0.5f), 0.15f, 0.4f);
            var pillarMat = MakeMaterial(new Color(0.38f, 0.4f, 0.44f), 0.2f, 0.5f);
            Box(root, "CenterPlatform", new Vector3(0, 0.5f, 0), Vector3.zero, new Vector3(12f, 1f, 12f), platMat);
            Box(root, "RampSouth", new Vector3(0, 0.45f, -9f), new Vector3(-8f, 0, 0), new Vector3(5f, 0.3f, 7f), platMat);
            Box(root, "RampNorth", new Vector3(0, 0.45f, 9f), new Vector3(8f, 0, 0), new Vector3(5f, 0.3f, 7f), platMat);
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    Cylinder(root, "Pillar", new Vector3(5f * sx, 2f, 5f * sz), new Vector3(1f, 2f, 1f), pillarMat);

            // Mirrored cover and crates.
            var coverMat = MakeMaterial(new Color(0.55f, 0.5f, 0.42f), 0.05f, 0.3f);
            var crateMat = MakeMaterial(new Color(0.55f, 0.4f, 0.25f), 0.05f, 0.3f);
            foreach (float sz in new[] { -1f, 1f })
            {
                Box(root, "Cover", new Vector3(-10f, 0.8f, 10f * sz), Vector3.zero, new Vector3(5f, 1.6f, 0.5f), coverMat);
                Box(root, "Cover", new Vector3(10f, 0.8f, 10f * sz), Vector3.zero, new Vector3(5f, 1.6f, 0.5f), coverMat);
                Box(root, "Cover", new Vector3(0, 0.8f, 17f * sz), Vector3.zero, new Vector3(6f, 1.6f, 0.5f), coverMat);
                Box(root, "CoverSide", new Vector3(-half + 6f, 0.8f, 6f * sz), Vector3.zero, new Vector3(0.5f, 1.6f, 5f), coverMat);
                Box(root, "CoverSide", new Vector3(half - 6f, 0.8f, 6f * sz), Vector3.zero, new Vector3(0.5f, 1.6f, 5f), coverMat);

                foreach (float sx in new[] { -1f, 1f })
                {
                    Vector3 corner = new Vector3((half - 8f) * sx, 0, (half - 8f) * sz);
                    Box(root, "Crate", corner + new Vector3(0, 0.7f, 0), new Vector3(0, 15f, 0), Vector3.one * 1.4f, crateMat);
                    Box(root, "Crate", corner + new Vector3(1.6f * sx, 0.5f, 0.4f * sz), new Vector3(0, -10f, 0), Vector3.one * 1f, crateMat);
                    Box(root, "Crate", corner + new Vector3(0, 1.9f, 0), new Vector3(0, 40f, 0), Vector3.one * 1f, crateMat);
                }
            }

            SpawnStrips(root, half - 3f, 14f);
        }

        // ------------------------------------------------------------------
        // Map 1: Nuketown-style — two houses facing a street with a bus
        // ------------------------------------------------------------------

        private static void BuildNuketown()
        {
            var root = new GameObject(MapRootName).transform;
            const float halfX = 16f, halfZ = 26f;

            Ground(root, halfX * 2f, halfZ * 2f, new Color(0.5f, 0.46f, 0.36f), new Color(0.44f, 0.41f, 0.32f));

            // Street strip across the middle.
            var asphalt = MakeMaterial(new Color(0.3f, 0.3f, 0.32f), 0.05f, 0.25f);
            Box(root, "Street", new Vector3(0, 0.03f, 0), Vector3.zero, new Vector3(halfX * 2f, 0.06f, 17f), asphalt);

            // Perimeter fence.
            var fenceMat = MakeMaterial(new Color(0.85f, 0.83f, 0.78f), 0f, 0.35f);
            PerimeterWalls(root, halfX, halfZ, 2.2f, fenceMat);

            // The two houses (pastel colors, facing each other).
            BuildHouse(root, -1, MakeMaterial(new Color(0.35f, 0.62f, 0.62f), 0f, 0.35f), fenceMat);
            BuildHouse(root, 1, MakeMaterial(new Color(0.82f, 0.68f, 0.32f), 0f, 0.35f), fenceMat);

            // Yard fences along the street with a center gap.
            foreach (float sz in new[] { -1f, 1f })
                foreach (float sx in new[] { -1f, 1f })
                    Box(root, "YardFence", new Vector3(8.25f * sx, 0.9f, 8.5f * sz), Vector3.zero, new Vector3(13.5f, 1.8f, 0.3f), fenceMat);

            // Center bus and two cars.
            var busMat = MakeMaterial(new Color(0.72f, 0.62f, 0.2f), 0.2f, 0.5f);
            var carMat1 = MakeMaterial(new Color(0.65f, 0.25f, 0.2f), 0.5f, 0.7f);
            var carMat2 = MakeMaterial(new Color(0.25f, 0.4f, 0.6f), 0.5f, 0.7f);
            Box(root, "Bus", new Vector3(2.5f, 1.5f, 0), Vector3.zero, new Vector3(2.8f, 3f, 8.5f), busMat);
            Box(root, "Car", new Vector3(-5f, 0.55f, 4f), new Vector3(0, 12f, 0), new Vector3(1.9f, 1.1f, 4f), carMat1);
            Box(root, "Car", new Vector3(-4.5f, 0.55f, -4.5f), new Vector3(0, -8f, 0), new Vector3(1.9f, 1.1f, 4f), carMat2);

            // Barrels for flavor.
            var barrelMat = MakeMaterial(new Color(0.45f, 0.5f, 0.35f), 0.3f, 0.5f);
            Cylinder(root, "Barrel", new Vector3(9f, 0.6f, 2f), new Vector3(0.8f, 0.6f, 0.8f), barrelMat);
            Cylinder(root, "Barrel", new Vector3(-9.5f, 0.6f, -1.5f), new Vector3(0.8f, 0.6f, 0.8f), barrelMat);

            SpawnStrips(root, halfZ - 2.5f, 10f);
        }

        /// <summary>Two-story house: doorways, an upper window over the street, side ramp to a balcony.</summary>
        private static void BuildHouse(Transform root, int sideSign, Material wallMat, Material trimMat)
        {
            float s = sideSign;
            var c = new Vector3(0, 0, 13f * s); // house center
            const float w = 10f, d = 7f, floorH = 2.6f;
            float front = -d / 2f * s; // toward the street

            // Ground floor walls: front + back with door gaps, solid sides.
            foreach (float f in new[] { front, -front })
            {
                Box(root, "HouseWall", c + new Vector3(-3f, floorH / 2f, f), Vector3.zero, new Vector3(4f, floorH, 0.25f), wallMat);
                Box(root, "HouseWall", c + new Vector3(3f, floorH / 2f, f), Vector3.zero, new Vector3(4f, floorH, 0.25f), wallMat);
                Box(root, "HouseWall", c + new Vector3(0, 2.35f, f), Vector3.zero, new Vector3(2f, 0.5f, 0.25f), wallMat); // door header
            }
            Box(root, "HouseWall", c + new Vector3(-w / 2f, floorH / 2f, 0), Vector3.zero, new Vector3(0.25f, floorH, d), wallMat);
            Box(root, "HouseWall", c + new Vector3(w / 2f, floorH / 2f, 0), Vector3.zero, new Vector3(0.25f, floorH, d), wallMat);

            // Second floor slab + walls with a wide window over the street.
            Box(root, "HouseFloor2", c + new Vector3(0, floorH + 0.1f, 0), Vector3.zero, new Vector3(w, 0.2f, d), trimMat);
            Box(root, "HouseWall2", c + new Vector3(0, floorH + 0.65f, front), Vector3.zero, new Vector3(w, 0.9f, 0.25f), wallMat); // sill
            Box(root, "HouseWall2", c + new Vector3(0, floorH + 2.35f, front), Vector3.zero, new Vector3(w, 0.5f, 0.25f), wallMat); // header
            Box(root, "HouseWall2", c + new Vector3(-4f, floorH + 1.55f, front), Vector3.zero, new Vector3(2f, 1.3f, 0.25f), wallMat);
            Box(root, "HouseWall2", c + new Vector3(4f, floorH + 1.55f, front), Vector3.zero, new Vector3(2f, 1.3f, 0.25f), wallMat);
            Box(root, "HouseWall2", c + new Vector3(0, floorH + 1.3f, -front), Vector3.zero, new Vector3(w, floorH, 0.25f), wallMat); // solid back
            Box(root, "HouseWall2", c + new Vector3(-w / 2f, floorH + 1.3f, 0), Vector3.zero, new Vector3(0.25f, floorH, d), wallMat);
            Box(root, "HouseWall2", c + new Vector3(w / 2f, floorH + 1.3f, 0), Vector3.zero, new Vector3(0.25f, floorH, d), wallMat);

            // Roof.
            Box(root, "HouseRoof", c + new Vector3(0, floorH * 2f + 0.3f, 0), Vector3.zero, new Vector3(w + 0.6f, 0.25f, d + 0.6f), trimMat);

            // Balcony over the street + side ramp up to it.
            Box(root, "Balcony", c + new Vector3(0, floorH + 0.1f, front - 1.1f * s), Vector3.zero, new Vector3(w, 0.2f, 2.2f), trimMat);
            Box(root, "BalconyRail", c + new Vector3(0, floorH + 0.6f, front - 2.1f * s), Vector3.zero, new Vector3(w, 1f, 0.15f), trimMat);
            // Ramp beside the house: bottom at the back yard, top meeting the balcony.
            // Positive X euler tilts the +Z end down, so the street-side end rises for s=1.
            Box(root, "BalconyRamp",
                c + new Vector3(w / 2f + 1.2f, (floorH + 0.2f) / 2f, -1.55f * s),
                new Vector3(22f * s, 0, 0),
                new Vector3(1.8f, 0.2f, 6.8f), trimMat);
        }

        // ------------------------------------------------------------------
        // Map 2: Backrooms — yellow maze with a low ceiling and buzzing lights
        // ------------------------------------------------------------------

        private static void BuildBackrooms()
        {
            var root = new GameObject(MapRootName).transform;
            const float half = 20f;
            const float ceilingH = 3f;

            Ground(root, half * 2f, half * 2f, new Color(0.56f, 0.5f, 0.3f), new Color(0.5f, 0.45f, 0.27f));

            var wallMat = MakeMaterial(new Color(0.78f, 0.71f, 0.42f), 0f, 0.25f);
            var ceilMat = MakeMaterial(new Color(0.82f, 0.8f, 0.72f), 0f, 0.2f);
            var lightMat = MakeEmissiveMaterial(new Color(1f, 0.97f, 0.85f), 2.6f);

            PerimeterWalls(root, half, half, ceilingH, wallMat);

            // Ceiling slab with glowing light panels.
            Box(root, "Ceiling", new Vector3(0, ceilingH + 0.15f, 0), Vector3.zero, new Vector3(half * 2f, 0.3f, half * 2f), ceilMat);
            for (float x = -13f; x <= 13f; x += 13f)
            {
                for (float z = -13f; z <= 13f; z += 13f)
                {
                    Box(root, "LightPanel", new Vector3(x, ceilingH - 0.02f, z), Vector3.zero, new Vector3(2.4f, 0.08f, 1.2f), lightMat);
                    var lightGo = new GameObject("BackroomsLight");
                    lightGo.transform.SetParent(root);
                    lightGo.transform.position = new Vector3(x, ceilingH - 0.5f, z);
                    var pl = lightGo.AddComponent<Light>();
                    pl.type = LightType.Point;
                    pl.color = new Color(1f, 0.95f, 0.8f);
                    pl.intensity = 1.4f;
                    pl.range = 12f;
                    pl.shadows = LightShadows.None;
                }
            }

            // Maze walls: rotationally symmetric, always multiple routes.
            void Wall(float x1, float z1, float x2, float z2) => WallSegment(root, x1, z1, x2, z2, ceilingH, wallMat);
            // Long walls with center gaps.
            Wall(-16, -8, -3, -8); Wall(3, -8, 16, -8);
            Wall(-16, 8, -3, 8); Wall(3, 8, 16, 8);
            Wall(-8, -16, -8, -3); Wall(-8, 3, -8, 16);
            Wall(8, -16, 8, -3); Wall(8, 3, 8, 16);
            // Edge stubs on the mid axes.
            Wall(-20, 0, -14, 0); Wall(14, 0, 20, 0);
            Wall(0, -20, 0, -14); Wall(0, 14, 0, 20);
            // Center block corners (small L pieces).
            Wall(-3, -3, 3, -3); Wall(3, 3, -3, 3);

            // Support pillars at wall crossings.
            var pillarMat = MakeMaterial(new Color(0.7f, 0.64f, 0.38f), 0f, 0.3f);
            foreach (float x in new[] { -8f, 8f })
                foreach (float z in new[] { -8f, 8f })
                    Box(root, "Pillar", new Vector3(x, ceilingH / 2f, z), Vector3.zero, new Vector3(0.8f, ceilingH, 0.8f), pillarMat);

            SpawnStrips(root, half - 2f, 8f);
        }

        // ------------------------------------------------------------------
        // Shared pieces
        // ------------------------------------------------------------------

        private static void Ground(Transform root, float sizeX, float sizeZ, Color a, Color b)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(root);
            ground.transform.localScale = new Vector3(sizeX / 10f, 1f, sizeZ / 10f);
            var mat = MakeMaterial(Color.white, 0f, 0.25f);
            mat.mainTexture = MakeTileTexture(a, b);
            mat.mainTextureScale = new Vector2(sizeX / 8f, sizeZ / 8f);
            ground.GetComponent<Renderer>().material = mat;
        }

        private static void PerimeterWalls(Transform root, float halfX, float halfZ, float height, Material mat)
        {
            Box(root, "Wall", new Vector3(0, height / 2f, halfZ), Vector3.zero, new Vector3(halfX * 2f, height, 0.5f), mat);
            Box(root, "Wall", new Vector3(0, height / 2f, -halfZ), Vector3.zero, new Vector3(halfX * 2f, height, 0.5f), mat);
            Box(root, "Wall", new Vector3(halfX, height / 2f, 0), Vector3.zero, new Vector3(0.5f, height, halfZ * 2f), mat);
            Box(root, "Wall", new Vector3(-halfX, height / 2f, 0), Vector3.zero, new Vector3(0.5f, height, halfZ * 2f), mat);
        }

        private static void WallSegment(Transform root, float x1, float z1, float x2, float z2, float height, Material mat)
        {
            var from = new Vector3(x1, 0, z1);
            var to = new Vector3(x2, 0, z2);
            var center = (from + to) / 2f + Vector3.up * (height / 2f);
            float length = Vector3.Distance(from, to);
            bool alongX = Mathf.Abs(x2 - x1) > Mathf.Abs(z2 - z1);
            var scale = alongX ? new Vector3(length, height, 0.4f) : new Vector3(0.4f, height, length);
            Box(root, "MazeWall", center, Vector3.zero, scale, mat);
        }

        /// <summary>Glowing team-colored strips marking the spawn zones.</summary>
        private static void SpawnStrips(Transform root, float z, float width)
        {
            Box(root, "SpawnZone0", new Vector3(0, 0.03f, -z), Vector3.zero, new Vector3(width, 0.06f, 1.2f), MakeEmissiveMaterial(Team0Color, 1.4f));
            Box(root, "SpawnZone1", new Vector3(0, 0.03f, z), Vector3.zero, new Vector3(width, 0.06f, 1.2f), MakeEmissiveMaterial(Team1Color, 1.4f));
        }

        private static void Box(Transform parent, string name, Vector3 pos, Vector3 euler, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().material = mat;
        }

        private static void Cylinder(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().material = mat;
        }

        public static Material MakeMaterial(Color color, float metallic = 0f, float smoothness = 0.5f)
        {
            var mat = new Material(Shader.Find("Standard"));
            mat.color = color;
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Glossiness", smoothness);
            return mat;
        }

        public static Material MakeEmissiveMaterial(Color color, float intensity)
        {
            var mat = MakeMaterial(color, 0f, 0.6f);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * intensity);
            return mat;
        }

        /// <summary>Concrete-style floor tiles: subtle checker + noise grain + dark seams.</summary>
        private static Texture2D MakeTileTexture(Color a, Color b)
        {
            const int texSize = 256;
            const int cellPx = 32;

            var tex = new Texture2D(texSize, texSize, TextureFormat.RGB24, true);
            for (int y = 0; y < texSize; y++)
            {
                for (int x = 0; x < texSize; x++)
                {
                    bool even = ((x / cellPx) + (y / cellPx)) % 2 == 0;
                    Color c = even ? a : b;
                    float noise = Mathf.PerlinNoise(x * 0.11f, y * 0.11f) * 0.06f - 0.03f;
                    c = new Color(c.r + noise, c.g + noise, c.b + noise);
                    if (x % cellPx < 2 || y % cellPx < 2)
                        c *= 0.78f;
                    tex.SetPixel(x, y, c);
                }
            }
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            tex.anisoLevel = 4;
            tex.Apply();
            return tex;
        }
    }
}
