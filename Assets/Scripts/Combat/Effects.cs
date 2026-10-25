using System.Collections.Generic;
using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Small code-generated effects: cube-burst deaths, explosions, bullet
    /// impacts, muzzle flashes and decals.
    /// Particles come from a handful of shared world-space systems fed with
    /// ParticleSystem.Emit (no GameObject per shot); decals are a fixed ring
    /// of quads parented to the map so they vanish with it.
    /// </summary>
    public static class Effects
    {
        /// <summary>Editor preview renders (PreviewRenderer): build the effects outside play mode.</summary>
        public static bool AllowInEditMode;

        /// <summary>Preview renders only: advance every shared particle system by this much time.</summary>
        public static void SimulateForPreview(float seconds)
        {
            foreach (var ps in new[] { Particles.sparks, Particles.dust, Particles.fire, Particles.smoke, Particles.flash })
                if (ps != null)
                    ps.Simulate(seconds, true, false, false);
        }

        /// <summary>Preview renders only: clear every particle.</summary>
        public static void ClearForPreview()
        {
            foreach (var ps in new[] { Particles.sparks, Particles.dust, Particles.fire, Particles.smoke, Particles.flash })
                if (ps != null)
                    ps.Clear(true);
        }

        /// <summary>Classic charm: the character bursts into glowing physical cubes.</summary>
        public static void SpawnDeathBurst(Vector3 center, Color color)
        {
            var mat = EnvironmentBuilder.SharedMaterial(color, emission: 1.4f);
            SpawnCubeBurst(center, mat, 12, 3.5f, 5f, 0.12f, 0.28f, 1.1f, 1.7f);
        }

        /// <summary>Explosion: fading flash light, fireball, smoke column, sparks and a scorch mark.</summary>
        public static void SpawnExplosion(Vector3 center)
        {
            var flashGo = new GameObject("ExplosionFlash");
            flashGo.transform.position = center + Vector3.up * 0.5f;
            var flash = flashGo.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = new Color(1f, 0.7f, 0.35f);
            flash.intensity = 5f;
            flash.range = 16f;
            flashGo.AddComponent<LightFade>().lifetime = 0.35f;

            if (!Particles.Ready())
            {
                // No particle shader: the old glowing cubes.
                var hotMat = EnvironmentBuilder.SharedMaterial(new Color(1f, 0.55f, 0.15f), emission: 2.6f);
                SpawnCubeBurst(center, hotMat, 16, 7f, 8f, 0.15f, 0.35f, 0.5f, 0.9f);
                return;
            }

            for (int i = 0; i < 10; i++)
                Particles.Emit(Particles.fire, center + Random.insideUnitSphere * 0.7f + Vector3.up * 0.5f,
                    Random.insideUnitSphere * 3.5f + Vector3.up * 1.5f, Random.Range(1.3f, 2.4f),
                    Random.Range(0.35f, 0.65f), new Color(1f, Random.Range(0.35f, 0.55f), 0.12f, 0.8f));
            for (int i = 0; i < 12; i++)
                Particles.Emit(Particles.smoke, center + Random.insideUnitSphere * 1f + Vector3.up * 0.6f,
                    Random.insideUnitSphere * 1.2f + Vector3.up * Random.Range(0.8f, 2f), Random.Range(2f, 3.6f),
                    Random.Range(2.2f, 3.8f), new Color(0.32f, 0.3f, 0.28f, 0.6f));
            for (int i = 0; i < 28; i++)
                Particles.Emit(Particles.sparks, center + Vector3.up * 0.3f,
                    Random.insideUnitSphere * 13f + Vector3.up * 4f, Random.Range(0.04f, 0.08f),
                    Random.Range(0.4f, 0.9f), new Color(1f, 0.75f, 0.35f));

            // Dark debris chunks (not glowing): weight to the blast.
            var debrisMat = EnvironmentBuilder.SharedMaterial(new Color(0.12f, 0.11f, 0.1f));
            SpawnCubeBurst(center, debrisMat, 6, 6f, 7f, 0.08f, 0.18f, 0.8f, 1.3f);

            if (Physics.Raycast(center + Vector3.up * 0.5f, Vector3.down, out var ground, 2.5f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                Decals.Place(ground.point, ground.normal, ground.collider, Random.Range(2.6f, 3.4f), Decals.scorch);
        }

        /// <summary>
        /// Bullet impact: sparks + dust puff and a bullet hole on surfaces,
        /// a red puff on characters (no hole).
        /// </summary>
        public static void SpawnImpact(Vector3 point, Vector3 normal, Collider surface, bool flesh)
        {
            if (!Particles.Ready())
                return;
            if (flesh)
            {
                for (int i = 0; i < 8; i++)
                    Particles.Emit(Particles.dust, point + normal * 0.05f,
                        (normal + Random.insideUnitSphere * 0.7f) * Random.Range(0.6f, 1.6f), Random.Range(0.18f, 0.32f),
                        Random.Range(0.25f, 0.45f), new Color(0.45f, 0.03f, 0.03f, 0.85f));
                return;
            }

            for (int i = 0; i < 5; i++)
                Particles.Emit(Particles.sparks, point + normal * 0.02f,
                    (Vector3.Reflect(Random.onUnitSphere, normal) * 0.6f + normal) * Random.Range(3f, 7f),
                    Random.Range(0.025f, 0.045f), Random.Range(0.12f, 0.3f), new Color(1f, 0.78f, 0.4f));

            // Dust in the colour of what was hit.
            Color dust = new Color(0.55f, 0.52f, 0.48f, 0.55f);
            var r = surface != null ? surface.GetComponent<Renderer>() : null;
            if (r != null && r.sharedMaterial != null && r.sharedMaterial.HasProperty("_Color"))
            {
                Color c = r.sharedMaterial.color;
                dust = new Color(c.r * 0.8f + 0.1f, c.g * 0.8f + 0.1f, c.b * 0.8f + 0.1f, 0.55f);
            }
            for (int i = 0; i < 3; i++)
                Particles.Emit(Particles.dust, point + normal * 0.06f,
                    (normal + Random.insideUnitSphere * 0.5f) * Random.Range(0.4f, 1.1f), Random.Range(0.18f, 0.32f),
                    Random.Range(0.5f, 0.9f), dust);

            Decals.Place(point, normal, surface, Random.Range(0.07f, 0.1f), Decals.hole);
        }

        /// <summary>Muzzle flash sprite burst (the gun's own light handles the lighting).</summary>
        public static void SpawnMuzzleFlash(Transform muzzle, bool suppressed, float scale = 1f)
        {
            if (muzzle == null || !Particles.Ready())
                return;
            Vector3 fwd = muzzle.forward;
            float size = (suppressed ? 0.07f : 0.16f) * scale;
            Color c = new Color(1f, 0.72f, 0.35f, suppressed ? 0.5f : 1f);
            Particles.Emit(Particles.flash, muzzle.position + fwd * 0.03f * scale, fwd * 0.4f, size * 1.4f, 0.05f, c);
            if (suppressed)
                return;
            for (int i = 1; i <= 3; i++)
                Particles.Emit(Particles.flash, muzzle.position + fwd * (0.05f * i) * scale, fwd * (1.5f + i),
                    size * (1.1f - i * 0.22f), 0.04f, c);
        }

        private static void SpawnCubeBurst(
            Vector3 center, Material mat, int count,
            float velocity, float upVelocity, float minSize, float maxSize,
            float minLife, float maxLife)
        {
            for (int i = 0; i < count; i++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "BurstCube";
                cube.transform.position = center + Random.insideUnitSphere * 0.35f;
                cube.transform.rotation = Random.rotation;
                cube.transform.localScale = Vector3.one * Random.Range(minSize, maxSize);
                cube.GetComponent<Renderer>().material = mat;

                var body = cube.AddComponent<Rigidbody>();
                body.mass = 0.3f;
                body.linearVelocity = Random.insideUnitSphere * velocity + Vector3.up * Random.Range(upVelocity * 0.5f, upVelocity);
                body.angularVelocity = Random.insideUnitSphere * 13f;

                cube.AddComponent<TimedShrink>().lifetime = Random.Range(minLife, maxLife);
            }
        }

        // ------------------------------------------------------------------
        // Shared particle systems
        // ------------------------------------------------------------------

        private static class Particles
        {
            public static ParticleSystem sparks, dust, fire, smoke, flash;
            private static GameObject root;
            private static bool failed;

            public static bool Ready()
            {
                if ((!Application.isPlaying && !Effects.AllowInEditMode) || failed)
                    return false;
                if (root != null)
                    return true;
                var shader = Shader.Find("Sundown/Particle");
                if (shader == null || !shader.isSupported)
                {
                    failed = true;
                    return false;
                }

                root = new GameObject("SharedEffects");
                if (Application.isPlaying)
                    Object.DontDestroyOnLoad(root);
                var additive = MakeMaterial(shader, true, 1.1f);
                var blended = MakeMaterial(shader, false, 1f);
                var hot = MakeMaterial(shader, true, 3f);

                sparks = Make("Sparks", hot, 1.4f, ParticleSystemRenderMode.Stretch,
                    Fade(0f, 1f, 0.6f), Curve(1f, 0.4f), 0f);
                dust = Make("Dust", blended, -0.05f, ParticleSystemRenderMode.Billboard,
                    Fade(0.05f, 1f, 0f), Curve(0.6f, 2.2f), 2.5f);
                fire = Make("Fire", additive, -0.15f, ParticleSystemRenderMode.Billboard,
                    FireFade(), Curve(0.5f, 1.4f), 3f);
                smoke = Make("Smoke", blended, -0.03f, ParticleSystemRenderMode.Billboard,
                    Fade(0.1f, 0.8f, 0f), Curve(0.5f, 2.4f), 0.9f);
                flash = Make("MuzzleFlash", hot, 0f, ParticleSystemRenderMode.Billboard,
                    Fade(0f, 1f, 0.3f), Curve(1f, 1.3f), 0f);
                return true;
            }

            public static void Emit(ParticleSystem ps, Vector3 position, Vector3 velocity, float size, float lifetime, Color color)
            {
                var p = new ParticleSystem.EmitParams
                {
                    position = position,
                    velocity = velocity,
                    startSize = size,
                    startLifetime = lifetime,
                    startColor = color,
                    rotation = Random.Range(0f, 360f)
                };
                ps.Emit(p, 1);
            }

            private static Material MakeMaterial(Shader shader, bool additiveBlend, float intensity)
            {
                var m = new Material(shader);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                m.SetFloat("_DstBlend", additiveBlend
                    ? (float)UnityEngine.Rendering.BlendMode.One
                    : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_Intensity", intensity);
                m.SetFloat("_Softness", additiveBlend ? 1.8f : 1.2f);
                return m;
            }

            private static ParticleSystem Make(string name, Material mat, float gravity, ParticleSystemRenderMode mode,
                Gradient fade, AnimationCurve size, float drag)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root.transform, false);
                var ps = go.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                var main = ps.main;
                main.loop = true;
                main.playOnAwake = false;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 1500;
                main.gravityModifier = gravity;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;

                var emission = ps.emission;
                emission.enabled = false;
                var shape = ps.shape;
                shape.enabled = false;

                var col = ps.colorOverLifetime;
                col.enabled = true;
                col.color = new ParticleSystem.MinMaxGradient(fade);

                var sol = ps.sizeOverLifetime;
                sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, size);

                if (drag > 0f)
                {
                    var limit = ps.limitVelocityOverLifetime;
                    limit.enabled = true;
                    limit.limit = new ParticleSystem.MinMaxCurve(0.4f);
                    limit.dampen = Mathf.Clamp01(drag * 0.05f);
                }

                var rot = ps.rotationOverLifetime;
                rot.enabled = mode == ParticleSystemRenderMode.Billboard;
                rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);

                var renderer = go.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial = mat;
                renderer.renderMode = mode;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.maxParticleSize = 2f;
                if (mode == ParticleSystemRenderMode.Stretch)
                {
                    renderer.velocityScale = 0.035f;
                    renderer.lengthScale = 1.5f;
                }

                ps.Play();
                return ps;
            }

            private static Gradient Fade(float fadeIn, float peak, float holdUntil)
            {
                var g = new Gradient();
                g.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[]
                    {
                        new GradientAlphaKey(fadeIn > 0f ? 0f : peak, 0f),
                        new GradientAlphaKey(peak, Mathf.Max(fadeIn, holdUntil, 0.01f)),
                        new GradientAlphaKey(0f, 1f)
                    });
                return g;
            }

            private static Gradient FireFade()
            {
                // Bright yellow core cooling to dull red before it fades.
                var g = new Gradient();
                g.SetKeys(
                    new[]
                    {
                        new GradientColorKey(new Color(1f, 0.95f, 0.8f), 0f),
                        new GradientColorKey(new Color(1f, 0.6f, 0.25f), 0.35f),
                        new GradientColorKey(new Color(0.35f, 0.1f, 0.05f), 1f)
                    },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
                return g;
            }

            private static AnimationCurve Curve(float start, float end) => AnimationCurve.EaseInOut(0f, start, 1f, end);
        }

        // ------------------------------------------------------------------
        // Decals
        // ------------------------------------------------------------------

        private static class Decals
        {
            public static Material hole, scorch;
            private const int Max = 96;
            private static readonly List<Transform> ring = new List<Transform>();
            private static int next;
            private static Mesh quad;

            public static void Place(Vector3 point, Vector3 normal, Collider surface, float size, Material mat)
            {
                if (!Ensure() || mat == null)
                    return;
                // Only on the static map (its root): never on players/props that move or get destroyed alone.
                if (surface == null || surface.attachedRigidbody != null
                    || surface.transform.root.name != EnvironmentBuilder.MapRootName)
                    return;

                Transform t;
                if (ring.Count < Max)
                {
                    t = Create();
                    ring.Add(t);
                }
                else
                {
                    next %= Max;
                    t = ring[next];
                    if (t == null)
                        ring[next] = t = Create();
                    next++;
                }

                t.SetParent(surface.transform.root, true);
                t.position = point + normal * 0.006f;
                t.rotation = Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                t.localScale = Vector3.one * size;
                t.GetComponent<MeshRenderer>().sharedMaterial = mat;
                t.gameObject.SetActive(true);
            }

            private static bool Ensure()
            {
                if (hole != null)
                    return true;
                var shader = Shader.Find("Sundown/Particle");
                if (shader == null || !shader.isSupported)
                    return false;
                hole = new Material(shader) { color = new Color(0.035f, 0.03f, 0.028f, 0.92f) };
                hole.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                hole.SetFloat("_Softness", 1.6f);
                hole.SetFloat("_Ring", 1f);
                hole.renderQueue = 2450; // after opaque, before particles
                scorch = new Material(shader) { color = new Color(0.03f, 0.028f, 0.025f, 0.8f) };
                scorch.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                scorch.SetFloat("_Softness", 0.9f);
                scorch.SetFloat("_Ring", 0.35f);
                scorch.renderQueue = 2449;

                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad = go.GetComponent<MeshFilter>().sharedMesh;
                if (Application.isPlaying)
                    Object.Destroy(go);
                else
                    Object.DestroyImmediate(go);
                return true;
            }

            private static Transform Create()
            {
                var go = new GameObject("Decal");
                go.AddComponent<MeshFilter>().sharedMesh = quad;
                var r = go.AddComponent<MeshRenderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                return go.transform;
            }
        }
    }

    /// <summary>Shrinks an object to nothing over its lifetime, then destroys it.</summary>
    public class TimedShrink : MonoBehaviour
    {
        public float lifetime = 1.5f;

        private Vector3 initialScale;
        private float age;

        private void Start()
        {
            initialScale = transform.localScale;
        }

        private void Update()
        {
            age += Time.deltaTime;
            if (age >= lifetime)
            {
                Destroy(gameObject);
                return;
            }
            transform.localScale = initialScale * (1f - age / lifetime);
        }
    }

    /// <summary>Fades a light out (quadratic) over its lifetime, then destroys it.</summary>
    public class LightFade : MonoBehaviour
    {
        public float lifetime = 0.3f;

        private Light target;
        private float startIntensity;
        private float age;

        private void Start()
        {
            target = GetComponent<Light>();
            startIntensity = target != null ? target.intensity : 0f;
        }

        private void Update()
        {
            age += Time.deltaTime;
            if (age >= lifetime || target == null)
            {
                Destroy(gameObject);
                return;
            }
            float k = 1f - age / lifetime;
            target.intensity = startIntensity * k * k;
        }
    }
}
