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
            var wall = SharedMaterial(baseColor, 0.05f, 0.42f);
            var accent = SharedMaterial(accentColor, 0.15f, 0.55f, dark ? 2.2f : 0.25f);
            PerimeterWalls(root, 29f, 29f, 4f, wall);
            SpawnStrips(root, 24f, 18f);

            for (int side = -1; side <= 1; side += 2)
            {
                Box(root, theme + "Base", new Vector3(-12f, 1.5f, side * 17f), Vector3.zero, new Vector3(10f, 3f, 7f), wall);
                Box(root, theme + "Base", new Vector3(12f, 1.5f, side * 17f), Vector3.zero, new Vector3(10f, 3f, 7f), wall);
                Box(root, "Accent", new Vector3(0f, 1f, side * 11f), new Vector3(0, side * 18f, 0), new Vector3(8f, 2f, 1f), accent);
            }
            Box(root, theme == "Western" ? "Saloon" : theme == "Underwater" ? "ResearchDome" : "RuinedChapel",
                new Vector3(0f, 2f, 0f), Vector3.zero, new Vector3(12f, 4f, 9f), wall);
            for (int i = -2; i <= 2; i++)
                Box(root, "Cover", new Vector3(i * 8f, 0.85f, (i % 2) * 8f), new Vector3(0, i * 13f, 0), new Vector3(3f, 1.7f, 2f), accent);

            // (NIGHTFALL has its own builder + NightfallAtmosphere now.)
            if (theme == "Underwater")
            {
                RenderSettings.fog = true;
                RenderSettings.fogColor = new Color(0.02f, 0.2f, 0.25f);
                RenderSettings.fogDensity = 0.018f;
            }
        }

        // ------------------------------------------------------------------
        // Built-in map: NIGHTFALL — a walled village graveyard at night, made
        // for zombie survival. 64 × 64 m, a roofless chapel ruin in the middle
        // (survivors start inside), houses in the corners, wrecked cars, a
        // graveyard to the east. Lighting comes from street lamps and fire
        // barrels; NightfallAtmosphere dims the sky and adds the fog.
        // Obstacles are straight walls and convex blocks with open corners, so
        // the zombies' simple steering can always walk around them.
        // ------------------------------------------------------------------

        public static void BuildNightfall()
        {
            var root = new GameObject(MapRootName).transform;
            const float half = 32f;

            Ground(root, half * 2f, half * 2f, new Color(0.13f, 0.14f, 0.12f), new Color(0.11f, 0.12f, 0.1f));

            var stone = SharedMaterial(new Color(0.3f, 0.3f, 0.33f), 0.05f, 0.3f);
            var wood = SharedMaterial(new Color(0.24f, 0.17f, 0.11f), 0f, 0.25f);
            var rust = SharedMaterial(new Color(0.32f, 0.16f, 0.1f), 0.4f, 0.3f);
            var metal = SharedMaterial(new Color(0.18f, 0.19f, 0.2f), 0.7f, 0.5f);
            var lampGlow = SharedMaterial(new Color(1f, 0.82f, 0.5f), 0f, 0.6f, 3f);
            var fireGlow = SharedMaterial(new Color(1f, 0.45f, 0.1f), 0f, 0.6f, 3.2f);
            var candleGlow = SharedMaterial(new Color(0.9f, 0.08f, 0.05f), 0f, 0.6f, 2.6f);

            PerimeterWalls(root, half, half, 3.5f, stone);
            SpawnStrips(root, 26f, 14f);

            // Chapel ruin: four straight walls, doorways north/south, open corners.
            void Wall(float x1, float z1, float x2, float z2) => WallSegment(root, x1, z1, x2, z2, 3.2f, stone);
            Wall(-5f, 4.5f, -1.6f, 4.5f); Wall(1.6f, 4.5f, 5f, 4.5f);
            Wall(-5f, -4.5f, -1.6f, -4.5f); Wall(1.6f, -4.5f, 5f, -4.5f);
            Wall(-5.6f, -3f, -5.6f, 3f); Wall(5.6f, -3f, 5.6f, 3f);
            Box(root, "Altar", new Vector3(0f, 0.5f, 3.2f), Vector3.zero, new Vector3(2.2f, 1f, 0.9f), stone);
            Visual(Box(root, "Candle", new Vector3(-0.8f, 1.15f, 3.2f), Vector3.zero, new Vector3(0.08f, 0.3f, 0.08f), candleGlow));
            Visual(Box(root, "Candle", new Vector3(0.8f, 1.15f, 3.2f), Vector3.zero, new Vector3(0.08f, 0.3f, 0.08f), candleGlow));
            PointLight(root, "AltarLight", new Vector3(0f, 1.8f, 2.6f), new Color(1f, 0.25f, 0.15f), 1.6f, 9f);

            // Houses in the corners: solid blocks with a roof and dim windows.
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                {
                    var c = new Vector3(19f * sx, 0f, 18f * sz);
                    Box(root, "House", c + new Vector3(0f, 2.5f, 0f), Vector3.zero, new Vector3(8f, 5f, 7f), wood);
                    Visual(Box(root, "Roof", c + new Vector3(0f, 5.4f, 0f), new Vector3(0f, 0f, 8f * sx), new Vector3(8.8f, 0.4f, 7.6f), metal));
                    Visual(Box(root, "Window", c + new Vector3(-4.02f * sx, 2.6f, 1.4f), Vector3.zero, new Vector3(0.05f, 1f, 1.2f), lampGlow));
                }

            // Wrecked cars (convex cover).
            void Car(float x, float z, float yaw)
            {
                Box(root, "WreckedCar", new Vector3(x, 0.7f, z), new Vector3(0f, yaw, 0f), new Vector3(4.2f, 1.4f, 1.9f), rust);
                Visual(Box(root, "CarCabin", new Vector3(x, 1.65f, z), new Vector3(0f, yaw, 0f), new Vector3(2f, 0.6f, 1.7f), rust));
            }
            Car(-10f, 14f, 20f);
            Car(12f, -12f, -35f);
            Car(-14f, -13f, 70f);
            Car(4f, 20f, 95f);

            // Graveyard (east): low stones in loose rows.
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 5; col++)
                {
                    float x = 10f + col * 3f;
                    float z = -2f + row * 3.4f + (col % 2) * 0.6f;
                    float tilt = ((row * 7 + col * 3) % 5 - 2) * 4f;
                    Box(root, "Gravestone", new Vector3(x, 0.5f, z), new Vector3(0f, 90f, tilt), new Vector3(0.7f, 1f, 0.22f), stone);
                }

            // Dead trees.
            foreach (var p in new[] { new Vector2(-20f, 2f), new Vector2(-8f, -22f), new Vector2(22f, 8f), new Vector2(-24f, -24f), new Vector2(9f, 26f) })
                Cylinder(root, "DeadTree", new Vector3(p.x, 2f, p.y), new Vector3(0.5f, 2f, 0.5f), wood);

            // Street lamps: pools of warm light to fight in.
            foreach (var p in new[]
                     {
                         new Vector2(-10f, 0f), new Vector2(7.8f, 0f), new Vector2(0f, -14f), new Vector2(0f, 14f),
                         new Vector2(-22f, -5f), new Vector2(22f, -6f), new Vector2(-12f, 24f), new Vector2(14f, 24f),
                         new Vector2(-14f, -24f), new Vector2(14f, -24f),
                     })
            {
                Cylinder(root, "LampPost", new Vector3(p.x, 2f, p.y), new Vector3(0.18f, 2f, 0.18f), metal);
                Visual(Sphere(root, "LampBulb", new Vector3(p.x, 4.1f, p.y), Vector3.one * 0.45f, lampGlow));
                PointLight(root, "LampLight", new Vector3(p.x, 3.8f, p.y), new Color(1f, 0.78f, 0.5f), 1.8f, 13f);
            }

            var atmosphere = root.gameObject.AddComponent<NightfallAtmosphere>();

            // Fire barrels: flickering orange light (flicker in NightfallAtmosphere).
            foreach (var p in new[] { new Vector2(-3f, -9f), new Vector2(8f, 15f), new Vector2(-20f, 9f), new Vector2(20f, -11f), new Vector2(-25f, -11f) })
            {
                Cylinder(root, "FireBarrel", new Vector3(p.x, 0.5f, p.y), new Vector3(0.7f, 0.5f, 0.7f), rust);
                Visual(Cylinder(root, "Fire", new Vector3(p.x, 1.05f, p.y), new Vector3(0.55f, 0.08f, 0.55f), fireGlow));
                atmosphere.fireLights.Add(PointLight(root, "FireLight", new Vector3(p.x, 1.8f, p.y), new Color(1f, 0.5f, 0.15f), 2f, 10f));
            }
        }

        private static Light PointLight(Transform root, string name, Vector3 pos, Color color, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root);
            go.transform.position = pos;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            return light;
        }

        /// <summary>NIGHTFALL team spawns: behind the spawn strips at each end (team modes / FFA).</summary>
        public static Vector3[] NightfallSpawnCandidates(int side)
        {
            float s = side == 0 ? -1f : 1f;
            return new[]
            {
                new Vector3(0f, 0.3f, 25f * s), new Vector3(-10f, 0.3f, 24f * s), new Vector3(10f, 0.3f, 24f * s),
                new Vector3(-5f, 0.3f, 21f * s), new Vector3(5f, 0.3f, 21f * s),
            };
        }

        /// <summary>NIGHTFALL zombie survival: the survivors start together inside the chapel.</summary>
        public static Vector3[] NightfallSurvivorSpawns()
        {
            return new[]
            {
                new Vector3(0f, 0.3f, -1.5f), new Vector3(-2.5f, 0.3f, 0f), new Vector3(2.5f, 0.3f, 0f),
                new Vector3(0f, 0.3f, 1.4f), new Vector3(-2.8f, 0.3f, -2.8f), new Vector3(2.8f, 0.3f, -2.8f),
                new Vector3(-1.3f, 0.3f, -3.4f), new Vector3(1.3f, 0.3f, -3.4f),
            };
        }

        /// <summary>NIGHTFALL zombie entry points along the outer wall (the director keeps the ones far from players).</summary>
        public static Vector3[] NightfallZombieSpawns()
        {
            var list = new List<Vector3>();
            foreach (float t in new[] { -24f, -8f, 8f, 24f })
            {
                list.Add(new Vector3(29f, 0.3f, t));
                list.Add(new Vector3(-29f, 0.3f, t));
                list.Add(new Vector3(t, 0.3f, 29f));
                list.Add(new Vector3(t, 0.3f, -29f));
            }
            return list.ToArray();
        }

        // ------------------------------------------------------------------
        // Built-in map: SKYFREIGHT — inside a giant cargo plane in flight.
        // The plane flies toward +Z. Cargo deck y = 0 from the open rear ramp
        // (z = -30, team 0 end) to the front bulkhead (z = 22); an upper deck
        // (y = 4.2) covers the front half and leads to the cockpit (z 22..29,
        // team 1 end); side catwalks run back along the walls. The ramp is
        // down and open: clouds stream past behind it, and anyone who falls
        // out dies (SkyfreightFlight). Exterior wings/tail are visual only.
        // ------------------------------------------------------------------

        public static void BuildSkyfreight()
        {
            var root = new GameObject(MapRootName).transform;

            const float halfW = 6.4f;     // inner wall line
            const float rearZ = -30f;     // ramp hinge
            const float frontZ = 22f;     // front bulkhead
            const float holdLen = frontZ - rearZ;
            const float holdMid = (frontZ + rearZ) / 2f;
            const float deckY = 4.2f;     // upper deck / catwalk floor height
            const float ceilY = 8.6f;

            var hull = SharedMaterial(new Color(0.42f, 0.45f, 0.46f), 0.55f, 0.4f);
            var hullDark = SharedMaterial(new Color(0.24f, 0.26f, 0.27f), 0.6f, 0.45f);
            var deck = SharedMaterial(new Color(0.3f, 0.31f, 0.3f), 0.5f, 0.35f);
            var rail = SharedMaterial(new Color(0.55f, 0.5f, 0.2f), 0.4f, 0.4f);
            var crate = SharedMaterial(new Color(0.5f, 0.37f, 0.22f), 0.05f, 0.3f);
            var crateDark = SharedMaterial(new Color(0.4f, 0.29f, 0.17f), 0.05f, 0.3f);
            var container = SharedMaterial(new Color(0.28f, 0.36f, 0.26f), 0.35f, 0.35f);
            var containerTan = SharedMaterial(new Color(0.55f, 0.48f, 0.34f), 0.3f, 0.35f);
            var exterior = SharedMaterial(new Color(0.62f, 0.65f, 0.67f), 0.5f, 0.5f);
            var skyGlass = SharedMaterial(new Color(0.62f, 0.78f, 0.95f), 0f, 0.9f, 1.3f);
            var lamp = SharedMaterial(new Color(1f, 0.93f, 0.8f), 0f, 0.6f, 2.4f);
            var jumpLight = SharedMaterial(new Color(1f, 0.15f, 0.1f), 0f, 0.6f, 2.6f);
            var hazardYellow = SharedMaterial(new Color(0.95f, 0.75f, 0.1f), 0f, 0.4f);
            var hazardBlack = SharedMaterial(new Color(0.08f, 0.08f, 0.08f), 0f, 0.4f);

            // ---------------- Fuselage shell ----------------
            // Cargo deck: tiled metal floor plates.
            var floorMat = MakeMaterial(Color.white, 0.45f, 0.35f);
            floorMat.mainTexture = MakeTileTexture(new Color(0.34f, 0.35f, 0.35f), new Color(0.3f, 0.31f, 0.31f));
            floorMat.mainTextureScale = new Vector2(3f, 13f);
            Box(root, "CargoDeck", new Vector3(0f, -0.15f, holdMid), Vector3.zero, new Vector3(halfW * 2f, 0.3f, holdLen), floorMat);

            // Straight lower walls, then panels slanting in to a narrower ceiling.
            foreach (float s in new[] { -1f, 1f })
            {
                Box(root, "FuselageWall", new Vector3(halfW * s, 2.8f, holdMid), Vector3.zero, new Vector3(0.4f, 5.6f, holdLen), hull);
                Box(root, "FuselageUpper", new Vector3(5.3f * s, 7.1f, holdMid), new Vector3(0f, 0f, 36.25f * s),
                    new Vector3(0.4f, 3.9f, holdLen), hull);
            }
            Box(root, "FuselageCeiling", new Vector3(0f, ceilY + 0.15f, holdMid), Vector3.zero, new Vector3(8.8f, 0.3f, holdLen), hull);

            // Ribs (frames) every 4 m — visual, so they never snag on the catwalks.
            for (float z = rearZ + 2f; z < frontZ; z += 4f)
            {
                foreach (float s in new[] { -1f, 1f })
                    Visual(Box(root, "Rib", new Vector3(6.1f * s, 2.8f, z), Vector3.zero, new Vector3(0.25f, 5.6f, 0.3f), hullDark));
                Visual(Box(root, "RibCeiling", new Vector3(0f, ceilY - 0.05f, z), Vector3.zero, new Vector3(8.4f, 0.15f, 0.3f), hullDark));
            }

            // Portholes (bright sky glass), lower deck and upper level.
            for (float z = rearZ + 4f; z < frontZ - 1f; z += 4f)
                foreach (float s in new[] { -1f, 1f })
                {
                    Visual(Box(root, "Porthole", new Vector3(6.17f * s, 2f, z), Vector3.zero, new Vector3(0.1f, 0.55f, 0.55f), skyGlass));
                    Visual(Box(root, "Porthole", new Vector3(6.17f * s, 5.05f, z), Vector3.zero, new Vector3(0.1f, 0.5f, 0.5f), skyGlass));
                }

            // Front bulkhead: solid below the upper deck, cockpit door above.
            Box(root, "Bulkhead", new Vector3(0f, deckY / 2f, frontZ + 0.2f), Vector3.zero, new Vector3(halfW * 2f, deckY, 0.4f), hull);
            foreach (float s in new[] { -1f, 1f })
                Box(root, "Bulkhead", new Vector3(3.7f * s, 6.4f, frontZ + 0.2f), Vector3.zero, new Vector3(5.4f, 4.4f, 0.4f), hull);
            Box(root, "BulkheadDoorTop", new Vector3(0f, 7.55f, frontZ + 0.2f), Vector3.zero, new Vector3(2f, 2.1f, 0.4f), hull);

            // ---------------- Upper level ----------------
            Box(root, "UpperDeck", new Vector3(0f, deckY - 0.15f, 13f), Vector3.zero, new Vector3(halfW * 2f, 0.3f, 18f), deck);
            foreach (float s in new[] { -1f, 1f })
            {
                // Catwalk along the wall, z -18..4, joined to the upper deck.
                Box(root, "Catwalk", new Vector3(5.6f * s, deckY - 0.15f, -7f), Vector3.zero, new Vector3(1.6f, 0.3f, 22f), deck);
                Box(root, "CatwalkRail", new Vector3(4.85f * s, deckY + 0.5f, -7f), Vector3.zero, new Vector3(0.1f, 1f, 22f), rail);
                Box(root, "CatwalkRail", new Vector3(5.6f * s, deckY + 0.5f, -18f), Vector3.zero, new Vector3(1.6f, 1f, 0.1f), rail);

                // Stairs from the cargo deck (z -4) up to the upper deck edge (z 4).
                Box(root, "Stairs", new Vector3(2.6f * s, 2f, 0f), new Vector3(-27.7f, 0f, 0f), new Vector3(1.8f, 0.25f, 9.1f), deck);

                // Rail along the upper deck edge between the stairs and the catwalk.
                Box(root, "DeckRail", new Vector3(4.15f * s, deckY + 0.5f, 4.1f), Vector3.zero, new Vector3(1.3f, 1f, 0.1f), rail);

                // Troop benches along the upper deck walls.
                Box(root, "Bench", new Vector3(5.9f * s, deckY + 0.25f, 13f), Vector3.zero, new Vector3(0.6f, 0.5f, 14f), hullDark);
            }
            Box(root, "DeckRail", new Vector3(0f, deckY + 0.5f, 4.1f), Vector3.zero, new Vector3(3.4f, 1f, 0.1f), rail);

            // ---------------- Cockpit (upper level, z 22..29) ----------------
            Box(root, "CockpitFloor", new Vector3(0f, deckY - 0.15f, 25.5f), Vector3.zero, new Vector3(7.4f, 0.3f, 7f), deck);
            Box(root, "NoseBody", new Vector3(0f, 1.95f, 25.5f), Vector3.zero, new Vector3(7.4f, 3.9f, 7f), exterior);
            foreach (float s in new[] { -1f, 1f })
            {
                Box(root, "CockpitWall", new Vector3(3.7f * s, 6.1f, 25.5f), Vector3.zero, new Vector3(0.3f, 3.8f, 7f), hull);
                Visual(Box(root, "CockpitWindow", new Vector3(3.56f * s, 6.4f, 26.8f), Vector3.zero, new Vector3(0.05f, 0.9f, 2.4f), skyGlass));
                Box(root, "PilotSeat", new Vector3(1.4f * s, deckY + 0.4f, 26f), Vector3.zero, new Vector3(0.9f, 0.8f, 0.9f), hullDark);
                Box(root, "PilotSeatBack", new Vector3(1.4f * s, deckY + 1.1f, 25.5f), new Vector3(-8f, 0f, 0f), new Vector3(0.9f, 1.4f, 0.2f), hullDark);
            }
            Box(root, "CockpitRoof", new Vector3(0f, 8.15f, 25.5f), Vector3.zero, new Vector3(7.7f, 0.3f, 7f), hull);
            Box(root, "Dashboard", new Vector3(0f, 5f, 29.1f), Vector3.zero, new Vector3(7.4f, 1.6f, 0.3f), hull);
            Box(root, "Windshield", new Vector3(0f, 6.9f, 28.75f), new Vector3(-20f, 0f, 0f), new Vector3(7.4f, 2.4f, 0.2f), skyGlass);
            Visual(Box(root, "WindshieldFrame", new Vector3(0f, 6.9f, 28.62f), new Vector3(-20f, 0f, 0f), new Vector3(0.15f, 2.4f, 0.1f), hullDark));
            Box(root, "Console", new Vector3(0f, deckY + 0.6f, 28.1f), Vector3.zero, new Vector3(6f, 1.2f, 1.2f), hullDark);
            Visual(Box(root, "Instruments", new Vector3(0f, deckY + 1.22f, 28f), Vector3.zero, new Vector3(5.4f, 0.05f, 0.8f),
                SharedMaterial(new Color(0.3f, 1f, 0.55f), 0f, 0.6f, 1.8f)));
            Box(root, "Throttles", new Vector3(0f, deckY + 0.45f, 27.1f), Vector3.zero, new Vector3(0.45f, 0.9f, 0.6f), hullDark);
            Visual(Box(root, "NoseCone", new Vector3(0f, 3.1f, 30.4f), Vector3.zero, new Vector3(6.4f, 5f, 2.4f), exterior));

            // ---------------- Rear: the open ramp ----------------
            // 9 m ramp hinged at the deck edge, down 15° into the open sky.
            Box(root, "Ramp", new Vector3(0f, -1.315f, rearZ - 4.35f), new Vector3(-15f, 0f, 0f), new Vector3(11.6f, 0.3f, 9f), deck);
            // Yellow/black hazard stripes on the hinge line.
            for (int i = 0; i < 12; i++)
                Visual(Box(root, "HazardStripe", new Vector3(-5.5f + i, 0.01f, rearZ + 0.3f), Vector3.zero, new Vector3(1f, 0.02f, 0.5f),
                    i % 2 == 0 ? hazardYellow : hazardBlack));
            foreach (float s in new[] { -1f, 1f })
            {
                Visual(Box(root, "RampArm", new Vector3(5.9f * s, 0.6f, rearZ - 1.6f), new Vector3(-50f, 0f, 0f), new Vector3(0.18f, 0.18f, 4.6f), hullDark));
                Visual(Box(root, "JumpLight", new Vector3(5.95f * s, 6.4f, rearZ + 0.25f), Vector3.zero, new Vector3(0.3f, 0.3f, 0.15f), jumpLight));
            }
            // Upper cargo door swung up under the tail; tail cone, fin and T-tail.
            Visual(Box(root, "CargoDoor", new Vector3(0f, 8.3f, rearZ - 2f), new Vector3(12f, 0f, 0f), new Vector3(8.4f, 0.25f, 4f), hull));
            Visual(Box(root, "TailCone", new Vector3(0f, 9.7f, rearZ - 7f), new Vector3(-8f, 0f, 0f), new Vector3(9f, 1.6f, 14f), exterior));
            Visual(Box(root, "TailFin", new Vector3(0f, 14f, rearZ - 10.5f), new Vector3(-20f, 0f, 0f), new Vector3(0.5f, 8f, 6f), exterior));
            Visual(Box(root, "Stabilizer", new Vector3(0f, 17.6f, rearZ - 12.5f), Vector3.zero, new Vector3(22f, 0.35f, 4.5f), exterior));

            // ---------------- Exterior: wings and engines (visual) ----------------
            foreach (float s in new[] { -1f, 1f })
            {
                Visual(Box(root, "Wing", new Vector3(21.4f * s, 7.2f, 4f), new Vector3(0f, 8f * s, -3f * s), new Vector3(32.2f, 0.6f, 8f), exterior));
                foreach (float x in new[] { 13f, 24f })
                {
                    var engine = Cylinder(root, "Engine", new Vector3(x * s, 5.9f, 6.5f - (x - 13f) * 0.12f), new Vector3(1.9f, 2.4f, 1.9f), hullDark);
                    engine.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                    Visual(engine);
                }
            }

            // ---------------- Cargo (cover) ----------------
            void Crate(float x, float y, float z, float size, float yaw, Material mat) =>
                Box(root, "Crate", new Vector3(x, y + size / 2f, z), new Vector3(0f, yaw, 0f), Vector3.one * size, mat);

            // Rear (team 0 end): spawn cover, a pallet guarding the ramp.
            Crate(-4.6f, 0f, -22f, 1.2f, 6f, crate);
            Crate(4.6f, 0f, -24f, 1.2f, -4f, crate);
            Crate(4.6f, 1.2f, -24f, 1f, 20f, crateDark);
            Box(root, "Pallet", new Vector3(0f, 0.45f, -27.6f), Vector3.zero, new Vector3(2.2f, 0.9f, 1.2f), crateDark);

            // Middle hold: staggered containers, side crates, a strapped jeep.
            Box(root, "Container", new Vector3(-2.4f, 1.3f, -13f), Vector3.zero, new Vector3(2.6f, 2.6f, 6f), container);
            Box(root, "Container", new Vector3(2.4f, 1.3f, -9f), Vector3.zero, new Vector3(2.6f, 2.6f, 5.5f), containerTan);
            Crate(4.9f, 0f, -14f, 1.1f, 12f, crate);
            Crate(-4.9f, 0f, -6f, 1.1f, -8f, crate);
            Crate(-4.6f, 0f, -4.6f, 1f, 18f, crateDark);
            Crate(0f, 0f, -1f, 1.2f, 0f, crate);

            Box(root, "JeepBody", new Vector3(-4.6f, 0.85f, 0f), Vector3.zero, new Vector3(2f, 1.1f, 3.8f), container);
            Box(root, "JeepCab", new Vector3(-4.6f, 1.75f, -0.3f), Vector3.zero, new Vector3(1.8f, 0.7f, 1.6f), hullDark);
            foreach (float wx in new[] { -5.65f, -3.55f })
                foreach (float wz in new[] { -1.3f, 1.3f })
                {
                    var wheel = Cylinder(root, "JeepWheel", new Vector3(wx, 0.4f, wz), new Vector3(0.8f, 0.15f, 0.8f), hazardBlack);
                    wheel.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
                    Visual(wheel);
                }
            Crate(4.6f, 0f, 0.6f, 1.2f, 3f, crate);
            Crate(4.6f, 0f, -0.8f, 1.2f, -5f, crateDark);
            Crate(4.6f, 1.2f, 0f, 1f, 25f, crate);

            // Under the upper deck (front, team 1 end).
            Crate(-3.5f, 0f, 7f, 1.2f, 10f, crate);
            Crate(3.2f, 0f, 9f, 1.2f, -6f, crateDark);
            Crate(3.6f, 0f, 10.2f, 1f, 15f, crate);
            Box(root, "Container", new Vector3(-2f, 0.9f, 12.5f), Vector3.zero, new Vector3(2.4f, 1.8f, 3f), containerTan);
            Crate(2.5f, 0f, 14f, 1.2f, 0f, crate);
            Crate(4.6f, 0f, 18f, 1.2f, 8f, crateDark);

            // Upper deck cover.
            Crate(-3f, deckY, 11f, 1.2f, 5f, crate);
            Crate(3f, deckY, 16f, 1f, -10f, crateDark);
            Crate(2.4f, deckY, 16.8f, 1f, 12f, crate);
            Crate(0f, deckY, 8f, 1.2f, 0f, crateDark);

            // ---------------- Lights ----------------
            void Lamp(Vector3 pos, Color color, float intensity, float range)
            {
                var go = new GameObject("CabinLight");
                go.transform.SetParent(root);
                go.transform.position = pos;
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = color;
                light.intensity = intensity;
                light.range = range;
                light.shadows = LightShadows.None;
            }
            var warm = new Color(1f, 0.92f, 0.78f);
            foreach (float z in new[] { -26f, -17f, -8f, 1f })
                Lamp(new Vector3(0f, 7.6f, z), warm, 1.3f, 12f);
            foreach (float z in new[] { 9f, 17f })
            {
                Lamp(new Vector3(0f, 3.4f, z), warm, 1.1f, 9f);
                Lamp(new Vector3(0f, 7.6f, z), warm, 1.1f, 10f);
            }
            Lamp(new Vector3(0f, 7.2f, 25.5f), new Color(0.7f, 0.9f, 1f), 0.9f, 7f);
            Lamp(new Vector3(0f, 6.5f, rearZ + 1f), new Color(1f, 0.25f, 0.2f), 1.2f, 8f); // red jump light glow
            Visual(Box(root, "LightStrip", new Vector3(0f, ceilY - 0.02f, holdMid), Vector3.zero, new Vector3(0.35f, 0.06f, holdLen - 2f), lamp));
            Visual(Box(root, "LightStrip", new Vector3(0f, deckY - 0.33f, 13f), Vector3.zero, new Vector3(0.35f, 0.06f, 17f), lamp));

            // ---------------- Sky: a cloud sea below, clouds streaming past ----------------
            var cloudMat = SharedMaterial(new Color(0.96f, 0.97f, 1f), 0f, 0.05f, 0.35f);
            var cloudSea = GameObject.CreatePrimitive(PrimitiveType.Plane);
            cloudSea.name = "CloudSea";
            cloudSea.transform.SetParent(root);
            cloudSea.transform.position = new Vector3(0f, -70f, 0f);
            cloudSea.transform.localScale = new Vector3(120f, 1f, 120f);
            cloudSea.GetComponent<Renderer>().material = SharedMaterial(new Color(0.86f, 0.9f, 0.97f), 0f, 0.1f, 0.25f);
            Visual(cloudSea);

            var clouds = new GameObject("Clouds").transform;
            clouds.SetParent(root);
            var rng = new System.Random(1337); // same sky on every client
            float R(float min, float max) => min + (float)rng.NextDouble() * (max - min);
            for (int i = 0; i < 70; i++)
            {
                Vector3 c;
                if (i % 10 < 7)
                    c = new Vector3(R(-160f, 160f), R(-40f, -18f), R(-320f, 320f)); // layer below
                else
                    c = new Vector3((i % 2 == 0 ? 1f : -1f) * R(55f, 150f), R(-12f, 18f), R(-320f, 320f)); // passing alongside
                var cloud = new GameObject("Cloud").transform;
                cloud.SetParent(clouds);
                cloud.position = c;
                float w = R(14f, 38f), d = R(14f, 38f);
                var main = Sphere(cloud, "Puff", c, new Vector3(w, R(4f, 8f), d), cloudMat);
                var puff = Sphere(cloud, "Puff", c + new Vector3(R(-w, w) * 0.3f, R(1f, 3f), R(-d, d) * 0.3f),
                    new Vector3(w * 0.6f, R(5f, 9f), d * 0.6f), cloudMat);
                foreach (var go in new[] { main, puff })
                {
                    var r = go.GetComponent<Renderer>();
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    Visual(go);
                }
            }

            root.gameObject.AddComponent<SkyfreightFlight>().clouds = clouds;
        }

        /// <summary>
        /// SKYFREIGHT spawns: team 0 in the rear hold by the ramp (kept clear of
        /// the edge), team 1 at the front, on the cargo deck and the upper deck.
        /// </summary>
        public static Vector3[] SkyfreightSpawnCandidates(int side)
        {
            if (side == 0)
                return new[]
                {
                    new Vector3(-3.5f, 0.3f, -25.5f), new Vector3(3f, 0.3f, -25.5f), new Vector3(0f, 0.3f, -23f),
                    new Vector3(-4.5f, 0.3f, -19f), new Vector3(4.3f, 0.3f, -18.5f), new Vector3(1.8f, 0.3f, -20.5f),
                };
            return new[]
            {
                new Vector3(-3f, 0.3f, 19f), new Vector3(2.5f, 0.3f, 19.5f), new Vector3(0f, 0.3f, 16.5f),
                new Vector3(-3.5f, 4.5f, 18f), new Vector3(3.5f, 4.5f, 13f), new Vector3(0f, 4.5f, 20.5f),
            };
        }

        /// <summary>
        /// Decoration only: drop the collider (disabled at once so this frame's
        /// spawn raycasts already ignore it). Works in edit mode for the bake tool.
        /// </summary>
        private static void Visual(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col == null)
                return;
            col.enabled = false;
            if (Application.isPlaying) Object.Destroy(col);
            else Object.DestroyImmediate(col);
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

        private static GameObject Sphere(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().material = mat;
            return go;
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

        private static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 euler, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().material = mat;
            return go;
        }

        private static GameObject Cylinder(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().material = mat;
            return go;
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
