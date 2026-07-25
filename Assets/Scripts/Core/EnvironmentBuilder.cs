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
        // Maps
        //
        // The playable map list (built-ins + your custom prefabs) lives in
        // MapCatalog. This class only holds the built-in geometry + their spawn
        // candidates. Make your own maps with Tools > FPS Base > New Map.
        // ------------------------------------------------------------------

        public const string MapRootName = "MapRoot";

        /// <summary>Candidate spawn positions for a built-in map (backrooms vs arena), per side.</summary>
        public static Vector3[] BuiltinSpawnCandidates(bool backrooms, int side)
        {
            float sign = side == 0 ? -1f : 1f;
            if (backrooms)
            {
                // Open ring between the maze and the outer wall (avoids the x=0 wall).
                return new[]
                {
                    new Vector3(-14f, 0.3f, 17.5f * sign), new Vector3(-7f, 0.3f, 17.5f * sign),
                    new Vector3(7f, 0.3f, 17.5f * sign), new Vector3(14f, 0.3f, 17.5f * sign),
                    new Vector3(-11f, 0.3f, 13f * sign), new Vector3(11f, 0.3f, 13f * sign),
                };
            }
            // Arena: spread across the back of each side, clear of the center platform.
            return new[]
            {
                new Vector3(0f, 0.3f, 24f * sign), new Vector3(-16f, 0.3f, 24f * sign),
                new Vector3(16f, 0.3f, 24f * sign), new Vector3(-8f, 0.3f, 20f * sign),
                new Vector3(8f, 0.3f, 20f * sign),
            };
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
        // Built-in map: Backrooms — yellow maze with a low ceiling and lights
        // ------------------------------------------------------------------

        public static void BuildBackrooms()
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

        public static void BuildThemeArena(string theme, Color baseColor, Color accentColor, bool dark)
        {
            var root = new GameObject(MapRootName).transform;
            Ground(root, 58f, 58f, baseColor * 0.75f, baseColor);
            var wall = SharedMaterial(baseColor, theme == "FlyingPlane" ? 0.75f : 0.05f, 0.42f);
            var accent = SharedMaterial(accentColor, 0.15f, 0.55f, dark ? 2.2f : 0.25f);
            PerimeterWalls(root, 29f, 29f, theme == "FlyingPlane" ? 5f : 4f, wall);
            SpawnStrips(root, 24f, 18f);

            for (int side = -1; side <= 1; side += 2)
            {
                Box(root, theme + "Base", new Vector3(-12f, 1.5f, side * 17f), Vector3.zero, new Vector3(10f, 3f, 7f), wall);
                Box(root, theme + "Base", new Vector3(12f, 1.5f, side * 17f), Vector3.zero, new Vector3(10f, 3f, 7f), wall);
                Box(root, "Accent", new Vector3(0f, 1f, side * 11f), new Vector3(0, side * 18f, 0), new Vector3(8f, 2f, 1f), accent);
            }
            Box(root, theme == "Western" ? "Saloon" : theme == "Underwater" ? "ResearchDome" : theme == "FlyingPlane" ? "CargoBay" : "RuinedChapel",
                new Vector3(0f, 2f, 0f), Vector3.zero, new Vector3(12f, 4f, 9f), wall);
            for (int i = -2; i <= 2; i++)
                Box(root, "Cover", new Vector3(i * 8f, 0.85f, (i % 2) * 8f), new Vector3(0, i * 13f, 0), new Vector3(3f, 1.7f, 2f), accent);

            if (dark)
            {
                RenderSettings.ambientLight = new Color(0.025f, 0.025f, 0.04f);
                RenderSettings.fog = true;
                RenderSettings.fogColor = new Color(0.015f, 0.015f, 0.025f);
                RenderSettings.fogDensity = 0.025f;
            }
            else if (theme == "Underwater")
            {
                RenderSettings.fog = true;
                RenderSettings.fogColor = new Color(0.02f, 0.2f, 0.25f);
                RenderSettings.fogDensity = 0.018f;
            }
        }

        // ------------------------------------------------------------------
        // Built-in map: Nuketown 2025 — low-poly homage to the BO2 layout.
        // Real map: ~3,000 m² playable, two 2-story houses facing a central
        // street (bus + truck + cars as cover), a garage beside each house,
        // backyard spawns behind each, low vaultable yard fences, side lanes
        // along the borders, 180° rotational symmetry.
        // Here: 44 x 68 m envelope, symmetric via a rotated per-side container.
        // ------------------------------------------------------------------

        public static void BuildNuketown()
        {
            var root = new GameObject(MapRootName).transform;
            const float halfX = 22f, halfZ = 34f;

            // Suburban grass lot + central asphalt street.
            Ground(root, halfX * 2f, halfZ * 2f, new Color(0.36f, 0.42f, 0.23f), new Color(0.33f, 0.39f, 0.21f));
            var asphalt = MakeMaterial(new Color(0.16f, 0.16f, 0.17f), 0f, 0.25f);
            Box(root, "Street", new Vector3(0, 0.02f, 0), Vector3.zero, new Vector3(20f, 0.04f, 26f), asphalt);
            var sidewalk = MakeMaterial(new Color(0.55f, 0.53f, 0.5f), 0f, 0.3f);
            Box(root, "Sidewalk", new Vector3(-10.6f, 0.03f, 0), Vector3.zero, new Vector3(1.2f, 0.06f, 26f), sidewalk);
            Box(root, "Sidewalk", new Vector3(10.6f, 0.03f, 0), Vector3.zero, new Vector3(1.2f, 0.06f, 26f), sidewalk);

            // Perimeter fence.
            var fenceMat = MakeMaterial(new Color(0.5f, 0.38f, 0.24f), 0f, 0.3f);
            PerimeterWalls(root, halfX, halfZ, 3f, fenceMat);

            // --- The two sides (house + garage + yard), 180° rotational twins ---
            var yellowWall = MakeMaterial(new Color(0.85f, 0.68f, 0.22f), 0f, 0.35f);
            var tealWall = MakeMaterial(new Color(0.3f, 0.52f, 0.46f), 0f, 0.35f);
            BuildNuketownSide(root, north: true, tealWall);    // +Z: team 1, teal house
            BuildNuketownSide(root, north: false, yellowWall); // -Z: team 0, yellow house

            // --- Center street cover ---
            // School bus: THE central cover, slightly angled like the original.
            var busMat = MakeMaterial(new Color(0.22f, 0.5f, 0.5f), 0.1f, 0.45f);
            var busTrim = MakeMaterial(new Color(0.45f, 0.28f, 0.18f), 0.05f, 0.3f);
            Box(root, "Bus", new Vector3(-2.2f, 1.55f, 0f), new Vector3(0, 8f, 0), new Vector3(2.5f, 2.9f, 10.5f), busMat);
            Box(root, "BusStripe", new Vector3(-2.2f, 1.1f, 0f), new Vector3(0, 8f, 0), new Vector3(2.6f, 0.5f, 10.6f), busTrim);
            // Moving truck north of the bus; two cars on the south half balance it.
            var truckMat = MakeMaterial(new Color(0.72f, 0.7f, 0.66f), 0.1f, 0.4f);
            Box(root, "TruckBox", new Vector3(4.4f, 1.5f, 4.2f), new Vector3(0, -6f, 0), new Vector3(2.3f, 2.6f, 6.2f), truckMat);
            Box(root, "TruckCab", new Vector3(4.9f, 0.9f, 8.1f), new Vector3(0, -6f, 0), new Vector3(2.2f, 1.6f, 1.8f), busTrim);
            var carRed = MakeMaterial(new Color(0.55f, 0.15f, 0.12f), 0.2f, 0.5f);
            var carBlue = MakeMaterial(new Color(0.4f, 0.5f, 0.6f), 0.2f, 0.5f);
            Box(root, "Car", new Vector3(5.2f, 0.7f, -8f), new Vector3(0, 12f, 0), new Vector3(1.8f, 1.3f, 4.2f), carRed);
            Box(root, "Car", new Vector3(-6.4f, 0.7f, -9.5f), new Vector3(0, -20f, 0), new Vector3(1.8f, 1.3f, 4.2f), carBlue);

            // NUKETOWN welcome sign near the south cul-de-sac end.
            var signPost = MakeMaterial(new Color(0.35f, 0.35f, 0.38f), 0.4f, 0.5f);
            var signFace = MakeMaterial(new Color(0.9f, 0.88f, 0.8f), 0f, 0.4f);
            Box(root, "SignPost", new Vector3(-13.4f, 1.1f, -12.5f), Vector3.zero, new Vector3(0.18f, 2.2f, 0.18f), signPost);
            Box(root, "SignPost", new Vector3(-15.6f, 1.1f, -12.5f), Vector3.zero, new Vector3(0.18f, 2.2f, 0.18f), signPost);
            Box(root, "SignPanel", new Vector3(-14.5f, 2.6f, -12.5f), Vector3.zero, new Vector3(3.2f, 1.6f, 0.15f), signFace);
            Box(root, "SignMeter", new Vector3(-14.5f, 3.55f, -12.5f), Vector3.zero, new Vector3(1.4f, 0.3f, 0.18f),
                MakeEmissiveMaterial(new Color(1f, 0.8f, 0.2f), 2f));

            SpawnStrips(root, 29.5f, 10f);
        }

        /// <summary>
        /// One Nuketown side, built in +Z coordinates inside a container that is
        /// rotated 180° for the south side — exact rotational symmetry for free.
        /// House spans x -10..-1, garage x 3..8.5, both at z 16.5..23.5; the
        /// backyard (spawns) is behind them, z 23.5..33.
        /// </summary>
        private static void BuildNuketownSide(Transform mapRoot, bool north, Material wallMat)
        {
            var side = new GameObject(north ? "SideNorth" : "SideSouth").transform;
            side.SetParent(mapRoot);

            var trimMat = MakeMaterial(new Color(0.25f, 0.23f, 0.21f), 0.1f, 0.4f);   // roofs / dark trim
            var floorMat = MakeMaterial(new Color(0.45f, 0.31f, 0.18f), 0f, 0.35f);   // interior wood
            var fenceMat = MakeMaterial(new Color(0.5f, 0.38f, 0.24f), 0f, 0.3f);
            var shedMat = MakeMaterial(new Color(0.42f, 0.33f, 0.22f), 0f, 0.3f);

            // Local helper: wall piece from x0..x1, y0..y1 at depth z (thickness 0.25).
            void WallX(float x0, float x1, float y0, float y1, float z) =>
                Box(side, "HouseWall", new Vector3((x0 + x1) / 2f, (y0 + y1) / 2f, z), Vector3.zero,
                    new Vector3(x1 - x0, y1 - y0, 0.25f), wallMat);
            void WallZ(float z0, float z1, float y0, float y1, float x) =>
                Box(side, "HouseWall", new Vector3(x, (y0 + y1) / 2f, (z0 + z1) / 2f), Vector3.zero,
                    new Vector3(0.25f, y1 - y0, z1 - z0), wallMat);

            // ---------------- House (two floors) ----------------
            // Front wall (faces the street, z = 16.5).
            // Ground: window x -8.5..-6 (sill 0.9-2.2), door x -4..-2.8.
            WallX(-10f, -8.5f, 0f, 2.7f, 16.5f);
            WallX(-8.5f, -6f, 0f, 0.9f, 16.5f);   // under window
            WallX(-8.5f, -6f, 2.2f, 2.7f, 16.5f); // above window
            WallX(-6f, -4f, 0f, 2.7f, 16.5f);
            WallX(-4f, -2.8f, 2.2f, 2.7f, 16.5f); // above door
            WallX(-2.8f, -1f, 0f, 2.7f, 16.5f);
            // Upstairs: the iconic street-facing window x -8..-4.5 (sill 3.6-4.8).
            WallX(-10f, -8f, 2.7f, 5.4f, 16.5f);
            WallX(-8f, -4.5f, 2.7f, 3.6f, 16.5f);  // under window
            WallX(-8f, -4.5f, 4.8f, 5.4f, 16.5f);  // above window
            WallX(-4.5f, -1f, 2.7f, 5.4f, 16.5f);

            // Back wall (z = 23.5): yard door x -6.2..-5, small upstairs window.
            WallX(-10f, -6.2f, 0f, 2.7f, 23.5f);
            WallX(-6.2f, -5f, 2.2f, 2.7f, 23.5f); // above door
            WallX(-5f, -1f, 0f, 2.7f, 23.5f);
            WallX(-10f, -8.5f, 2.7f, 5.4f, 23.5f);
            WallX(-8.5f, -7f, 2.7f, 3.6f, 23.5f);
            WallX(-8.5f, -7f, 4.8f, 5.4f, 23.5f);
            WallX(-7f, -1f, 2.7f, 5.4f, 23.5f);

            // Outer side wall (x = -10) solid; inner (x = -1) with a side door.
            WallZ(16.5f, 23.5f, 0f, 5.4f, -10f);
            WallZ(16.5f, 19f, 0f, 5.4f, -1f);
            WallZ(19f, 20.2f, 2.2f, 5.4f, -1f);   // above side door
            WallZ(20.2f, 23.5f, 0f, 5.4f, -1f);

            // Interior: wood ground floor, divider stub, stairs, upper slab, roof.
            Box(side, "HouseFloor", new Vector3(-5.5f, 0.06f, 20f), Vector3.zero, new Vector3(9f, 0.12f, 7f), floorMat);
            Box(side, "Divider", new Vector3(-6f, 1.35f, 22f), Vector3.zero, new Vector3(0.25f, 2.7f, 3f), wallMat);
            // Stairs along the inner wall, up from the back (z 23.4) to the front
            // (z 18), landing flush with the slab top; the whole stair column
            // (x -3..-1) stays open so a climbing player's head never hits slab.
            Box(side, "Stairs", new Vector3(-2f, 1.48f, 20.7f), new Vector3(28.6f, 0, 0), new Vector3(1.8f, 0.25f, 6.15f), floorMat);
            Box(side, "UpperFloor", new Vector3(-6.5f, 2.82f, 20f), Vector3.zero, new Vector3(7f, 0.25f, 7f), floorMat);
            Box(side, "Roof", new Vector3(-5.5f, 5.52f, 20f), Vector3.zero, new Vector3(9.4f, 0.25f, 7.4f), trimMat);

            // ---------------- Garage (drive-through cover) ----------------
            // Open street face; back door to the yard; flat roof.
            WallZ(17.5f, 23.5f, 0f, 2.9f, 3f);     // inner garage wall
            WallZ(17.5f, 23.5f, 0f, 2.9f, 8.5f);   // outer garage wall
            WallX(3f, 4.6f, 0f, 2.9f, 23.5f);      // back wall left of door
            WallX(4.6f, 5.8f, 2.2f, 2.9f, 23.5f);  // above back door
            WallX(5.8f, 8.5f, 0f, 2.9f, 23.5f);
            Box(side, "GarageRoof", new Vector3(5.75f, 3f, 20.5f), Vector3.zero, new Vector3(5.9f, 0.25f, 6.4f), trimMat);

            // ---------------- Yard & side lanes ----------------
            // Low vaultable fences sealing the yard from the side lanes (jump over).
            Box(side, "YardFence", new Vector3(-16f, 0.5f, 24.5f), Vector3.zero, new Vector3(12f, 1f, 0.15f), fenceMat);
            Box(side, "YardFence", new Vector3(15.25f, 0.5f, 24.5f), Vector3.zero, new Vector3(13.5f, 1f, 0.15f), fenceMat);
            // Backyard shed (spawn cover).
            Box(side, "Shed", new Vector3(-8f, 1.1f, 28.5f), new Vector3(0, 6f, 0), new Vector3(2.5f, 2.2f, 2f), shedMat);
            // Side-lane barriers (mid-lane cover on the long border paths).
            Box(side, "LaneCover", new Vector3(-13f, 0.7f, 7f), new Vector3(0, 14f, 0), new Vector3(2.4f, 1.4f, 0.4f), fenceMat);
            Box(side, "LaneCover", new Vector3(13.5f, 0.7f, 9f), new Vector3(0, -10f, 0), new Vector3(2.4f, 1.4f, 0.4f), fenceMat);

            // Mailbox at the driveway edge (Mason / Woods in the original).
            Box(side, "MailPost", new Vector3(-3.5f, 0.5f, 15.9f), Vector3.zero, new Vector3(0.12f, 1f, 0.12f), trimMat);
            Box(side, "MailBox", new Vector3(-3.5f, 1.1f, 15.9f), Vector3.zero, new Vector3(0.5f, 0.35f, 0.35f),
                MakeMaterial(new Color(0.7f, 0.12f, 0.1f), 0.1f, 0.5f));

            if (!north)
                side.localRotation = Quaternion.Euler(0f, 180f, 0f); // exact rotational twin
        }

        /// <summary>Nuketown spawn candidates: the backyard behind each side's house/garage.</summary>
        public static Vector3[] NuketownSpawnCandidates(int side)
        {
            float s = side == 0 ? -1f : 1f; // side 0 = south (-Z), matching the other built-ins
            // Positions authored for the north side, mirrored through the origin
            // for the south side (matches the rotated side container).
            return new[]
            {
                new Vector3(-5.5f * s, 0.3f, 29.5f * s),  // behind the house door
                new Vector3(-8.5f * s, 0.3f, 26.5f * s),  // beside the shed
                new Vector3(-2.5f * s, 0.3f, 27f * s),    // house/garage gap
                new Vector3(5.2f * s, 0.3f, 28.5f * s),   // behind the garage
                new Vector3(7.5f * s, 0.3f, 26f * s),     // garage back door
                new Vector3(-1f * s, 0.3f, 31.5f * s),    // deep yard center
            };
        }

        // ------------------------------------------------------------------
        // P Story — a big crescent island of four biome wedges around a central
        // transition hub, with a teleporter linking the left tip to the hub.
        // Used only by the P Story game mode (third-person).
        // ------------------------------------------------------------------

        // Biome region centers (crescent arc); shared by the builder and spawns.
        private static readonly Vector3[] PStoryCenters =
        {
            new Vector3(-40f, 0f, 8f),   // Area 1 — forest
            new Vector3(-18f, 0f, 17f),  // Area 2 — plains
            new Vector3(18f, 0f, 17f),   // Area 3 — desert
            new Vector3(40f, 0f, 8f),    // Area 4 — volcanic
        };
        private static readonly Vector3 PStoryHub = new Vector3(0f, 0f, 5f);

        public static void BuildPStoryIsland()
        {
            var root = new GameObject(MapRootName).transform;

            // Ocean.
            var sea = GameObject.CreatePrimitive(PrimitiveType.Plane);
            sea.name = "Sea";
            sea.transform.SetParent(root);
            sea.transform.position = new Vector3(0, -5f, 10f);
            sea.transform.localScale = new Vector3(60f, 1f, 60f);
            sea.GetComponent<Renderer>().material = MakeMaterial(new Color(0.06f, 0.28f, 0.4f), 0.2f, 0.85f);

            var rock = MakeMaterial(new Color(0.32f, 0.3f, 0.28f), 0.1f, 0.35f);       // exposed rock / cliffs

            // THE walkable surface: solid box slabs (reliable BoxColliders) shaped
            // to the crescent, tops at y ~ 0. Every decorative cylinder below has
            // its collider stripped (a Unity cylinder's default CapsuleCollider,
            // scaled flat and wide, becomes a giant invisible dome — that caused
            // the void spawns and the lurching third-person camera). These boxes
            // are dark rock and sit just under the biome discs, so you never see
            // them but you always have solid ground.
            Box(root, "IslandFloor", new Vector3(-40f, -1.02f, 9f), Vector3.zero, new Vector3(56f, 2f, 42f), rock);
            Box(root, "IslandFloor", new Vector3(0f, -1.02f, 6f), Vector3.zero, new Vector3(42f, 2f, 38f), rock);
            Box(root, "IslandFloor", new Vector3(40f, -1.02f, 9f), Vector3.zero, new Vector3(56f, 2f, 42f), rock);
            var greenBiome = MakeMaterial(new Color(0.28f, 0.55f, 0.16f));
            var limeBiome = MakeMaterial(new Color(0.55f, 0.74f, 0.22f));
            var yellowBiome = MakeMaterial(new Color(0.86f, 0.72f, 0.28f));
            var redBiome = MakeMaterial(new Color(0.5f, 0.16f, 0.13f));
            var sandHub = MakeMaterial(new Color(0.82f, 0.75f, 0.55f));

            // Rounded landmass: overlapping discs (rounded tops at y=0) sitting on a
            // rock shelf that fills every seam, so the island reads as one organic
            // shape and nobody can fall between biomes.
            void Disc(string name, Vector3 c, float radius, float top, Material mat) =>
                Cylinder(root, name, new Vector3(c.x, top - 0.4f, c.z), new Vector3(radius * 2f, 0.4f, radius * 2f), mat);

            // Rock shelf (safety floor + visible rock between biomes), just under the grass.
            Disc("RockShelf_L", new Vector3(-40, 0, 9), 26f, -0.35f, rock);
            Disc("RockShelf_C", PStoryHub, 24f, -0.35f, rock);
            Disc("RockShelf_R", new Vector3(40, 0, 9), 26f, -0.35f, rock);

            // Cliff bodies below the shelf so the island has real sides, not thin slabs.
            void Cliff(Vector3 c, float radius) =>
                Cylinder(root, "Cliff", new Vector3(c.x, -3.4f, c.z), new Vector3(radius * 2f, 6f, radius * 2f), rock);
            Cliff(new Vector3(-40, 0, 9), 24f);
            Cliff(PStoryHub, 22f);
            Cliff(new Vector3(40, 0, 9), 24f);

            // Biome surfaces.
            Disc("Area1_Forest", PStoryCenters[0], 18f, 0f, greenBiome);
            Disc("Area1_Tip", new Vector3(-56, 0, 13), 10f, 0f, greenBiome);
            Disc("Area2_Plains", PStoryCenters[1], 17f, 0f, limeBiome);
            Disc("Area3_Desert", PStoryCenters[2], 17f, 0f, yellowBiome);
            Disc("Area4_Volcanic", PStoryCenters[3], 18f, 0f, redBiome);
            Disc("Area4_Tip", new Vector3(56, 0, 13), 10f, 0f, redBiome);
            Disc("Hub_Sand", PStoryHub, 15f, 0.02f, sandHub);

            BuildPStoryHub(root, rock);
            BuildForest(root);
            BuildPlains(root);
            BuildDesert(root);
            BuildVolcano(root);

            // Teleporter: Area 1 tip <-> hub.
            var padA = TeleportPadObject(root, "Teleporter_Area1", new Vector3(-56f, 0.1f, 13f));
            var padB = TeleportPadObject(root, "Teleporter_Hub", new Vector3(6f, 0.1f, 12f));
            padA.linked = padB;
            padB.linked = padA;

            // Ground signposts (diegetic, replaces the floating labels).
            Signpost(root, "AREA 1", new Vector3(-30f, 0, 8f), greenBiome);
            Signpost(root, "AREA 2", new Vector3(-10f, 0, 12f), limeBiome);
            Signpost(root, "AREA 3", new Vector3(10f, 0, 12f), yellowBiome);
            Signpost(root, "AREA 4", new Vector3(30f, 0, 8f), redBiome);

            // Strip every decorative collider so only the IslandFloor boxes and the
            // teleporter triggers remain — no capsule domes to void-spawn on or snag
            // the camera. Decorations become visual-only (you can brush past props).
            foreach (var col in root.GetComponentsInChildren<Collider>())
            {
                if (col.isTrigger) continue;                 // teleporter pads
                if (col.gameObject.name == "IslandFloor") continue; // the walkable floor
                Object.Destroy(col);
            }
        }

        private static void BuildPStoryHub(Transform root, Material rock)
        {
            // Raised stone dais with a ring of pillars — the "transition" nexus.
            Cylinder(root, "Hub_Dais", new Vector3(PStoryHub.x, 0.25f, PStoryHub.z), new Vector3(11f, 0.5f, 11f), rock);
            var pillar = MakeMaterial(new Color(0.45f, 0.43f, 0.4f), 0.2f, 0.4f);
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                Cylinder(root, "Hub_Pillar",
                    new Vector3(PStoryHub.x + Mathf.Cos(a) * 6f, 2f, PStoryHub.z + Mathf.Sin(a) * 6f),
                    new Vector3(0.9f, 2.2f, 0.9f), pillar);
            }
            Sphere(root, "Hub_Orb", new Vector3(PStoryHub.x, 2.6f, PStoryHub.z), Vector3.one * 1.6f,
                MakeEmissiveMaterial(new Color(0.5f, 0.85f, 1f), 2.2f));
        }

        private static void BuildForest(Transform root)
        {
            var trunk = MakeMaterial(new Color(0.32f, 0.22f, 0.13f));
            var leaves = MakeMaterial(new Color(0.16f, 0.42f, 0.14f));
            var leaves2 = MakeMaterial(new Color(0.22f, 0.5f, 0.18f));
            var c = PStoryCenters[0];
            for (int i = 0; i < 9; i++)
            {
                float a = i * 40f * Mathf.Deg2Rad;
                float r = 5f + (i % 3) * 4.5f;
                var p = new Vector3(c.x + Mathf.Cos(a) * r, 0, c.z + Mathf.Sin(a) * r);
                float h = 2f + (i % 3) * 0.6f;
                Cylinder(root, "TreeTrunk", p + new Vector3(0, h, 0), new Vector3(0.6f, h, 0.6f), trunk);
                Sphere(root, "TreeCanopy", p + new Vector3(0, h * 2f + 0.6f, 0), Vector3.one * (3f + (i % 2)), i % 2 == 0 ? leaves : leaves2);
            }
        }

        private static void BuildPlains(Transform root)
        {
            var boulder = MakeMaterial(new Color(0.5f, 0.52f, 0.5f), 0.2f, 0.4f);
            var bush = MakeMaterial(new Color(0.3f, 0.55f, 0.22f));
            var c = PStoryCenters[1];
            foreach (var o in new[] { new Vector3(-6, 0, 4), new Vector3(5, 0, -3), new Vector3(-2, 0, 8), new Vector3(8, 0, 5) })
                Sphere(root, "Boulder", c + o + new Vector3(0, 0.8f, 0), new Vector3(2.2f, 1.4f, 2.2f), boulder);
            foreach (var o in new[] { new Vector3(-9, 0, -2), new Vector3(2, 0, 9), new Vector3(10, 0, -4), new Vector3(-5, 0, 10) })
                Sphere(root, "Bush", c + o + new Vector3(0, 0.5f, 0), new Vector3(1.5f, 1f, 1.5f), bush);
        }

        private static void BuildDesert(Transform root)
        {
            var cactus = MakeMaterial(new Color(0.24f, 0.48f, 0.27f));
            var c = PStoryCenters[2];
            foreach (var o in new[] { new Vector3(-6, 0, 3), new Vector3(6, 0, -2), new Vector3(0, 0, 8), new Vector3(9, 0, 4) })
            {
                Cylinder(root, "Cactus", c + o + new Vector3(0, 1.8f, 0), new Vector3(0.7f, 1.8f, 0.7f), cactus);
                Cylinder(root, "CactusArm", c + o + new Vector3(0.6f, 2.2f, 0), new Vector3(0.4f, 0.7f, 0.4f), cactus);
            }
            Sphere(root, "Dune", c + new Vector3(-8, -0.6f, -4), new Vector3(10f, 2.4f, 8f),
                MakeMaterial(new Color(0.9f, 0.78f, 0.34f)));
            Sphere(root, "Dune2", c + new Vector3(7, -0.7f, 7), new Vector3(8f, 2f, 7f),
                MakeMaterial(new Color(0.88f, 0.74f, 0.3f)));
        }

        private static void BuildVolcano(Transform root)
        {
            var darkRock = MakeMaterial(new Color(0.14f, 0.11f, 0.11f), 0.3f, 0.3f);
            var lava = MakeEmissiveMaterial(new Color(1f, 0.4f, 0.1f), 2.8f);
            var c = PStoryCenters[3];
            // A cone volcano (stacked shrinking cylinders) with a glowing crater.
            for (int i = 0; i < 5; i++)
            {
                float t = i / 4f;
                Cylinder(root, "VolcanoTier", c + new Vector3(0, 1f + i * 1.4f, 0),
                    new Vector3(Mathf.Lerp(14f, 3f, t), 0.8f, Mathf.Lerp(14f, 3f, t)), darkRock);
            }
            Cylinder(root, "Crater", c + new Vector3(0, 7.4f, 0), new Vector3(3.2f, 0.4f, 3.2f), lava);
            foreach (var o in new[] { new Vector3(-9, 0, 5), new Vector3(8, 0, -6), new Vector3(11, 0, 4) })
                Sphere(root, "VolcanicRock", c + o + new Vector3(0, 1f, 0), new Vector3(2.6f, 2f, 2.6f), darkRock);
            foreach (var o in new[] { new Vector3(-6, 0.05f, -3), new Vector3(4, 0.05f, 6), new Vector3(-2, 0.05f, 8) })
                Box(root, "LavaCrack", c + o, new Vector3(0, Random.Range(0, 90), 0), new Vector3(3.5f, 0.1f, 0.7f), lava);
        }

        private static void Signpost(Transform root, string text, Vector3 pos, Material signMat)
        {
            Cylinder(root, "SignPost", pos + new Vector3(0, 1.2f, 0), new Vector3(0.2f, 1.2f, 0.2f),
                MakeMaterial(new Color(0.3f, 0.22f, 0.13f)));
            Box(root, "SignPanel", pos + new Vector3(0, 2.4f, 0), Vector3.zero, new Vector3(3f, 1f, 0.15f), signMat);
            AreaLabel(root, text, pos + new Vector3(0, 2.4f, 0.12f), 0.28f);
        }

        /// <summary>Spawn points spread across the four biomes (P Story is free-roam).</summary>
        public static Vector3[] PStorySpawnCandidates()
        {
            var list = new List<Vector3>();
            foreach (var c in PStoryCenters)
            {
                list.Add(c + new Vector3(0, 1.2f, 0));
                list.Add(c + new Vector3(6f, 1.2f, -4f));
            }
            list.Add(PStoryHub + new Vector3(0, 1.5f, 0));
            list.Add(new Vector3(-56f, 1.2f, 13f));
            list.Add(new Vector3(56f, 1.2f, 13f));
            return list.ToArray();
        }

        private static TeleportPad TeleportPadObject(Transform root, string name, Vector3 pos)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(root);
            go.transform.position = pos;
            go.transform.localScale = new Vector3(3f, 0.15f, 3f);
            go.GetComponent<Renderer>().material = MakeEmissiveMaterial(new Color(0.3f, 0.7f, 1f), 2.4f);
            var col = go.GetComponent<Collider>();
            col.isTrigger = true; // trigger only — you walk onto it
            // A thin solid disc under the trigger so players don't fall through.
            Cylinder(root, name + "_Base", pos + Vector3.down * 0.1f, new Vector3(3.2f, 0.1f, 3.2f),
                MakeMaterial(new Color(0.2f, 0.3f, 0.4f), 0.3f, 0.5f));
            return go.AddComponent<TeleportPad>();
        }

        private static void AreaLabel(Transform root, string text, Vector3 pos, float size = 0.5f)
        {
            var go = new GameObject(text);
            go.transform.SetParent(root);
            go.transform.position = pos;
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.characterSize = size;
            tm.fontSize = 64;
            tm.fontStyle = FontStyle.Bold;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(1f, 1f, 1f, 0.6f);

            Font font = null;
            try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (font == null) { try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } }
            if (font != null)
            {
                tm.font = font;
                go.GetComponent<MeshRenderer>().material = font.material;
            }
            go.AddComponent<Nametag>(); // billboards toward the camera
        }

        private static void Sphere(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().material = mat;
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
