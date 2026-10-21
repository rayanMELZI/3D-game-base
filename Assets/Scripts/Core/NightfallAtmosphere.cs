using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FpsBase
{
    /// <summary>
    /// Lives on the NIGHTFALL map root. Lighting is global (sun, sky, ambient,
    /// fog), so the map takes it over while it exists and gives it back when it
    /// is unloaded. Also flickers the fire barrels and lends the local camera a
    /// weak flashlight so the dark stays playable. Purely visual: every client
    /// runs its own copy, nothing is networked.
    /// </summary>
    public class NightfallAtmosphere : MonoBehaviour
    {
        [Tooltip("Moonlight intensity (the shared sun is dimmed and turned blue).")]
        public float moonIntensity = 0.16f;
        public float fogDensity = 0.032f;
        public float flashlightIntensity = 2.2f;
        public float flashlightRange = 24f;

        /// <summary>Fire barrel lights, flickered every frame.</summary>
        public readonly List<Light> fireLights = new List<Light>();

        private struct Saved
        {
            public float sunIntensity;
            public Color sunColor;
            public Quaternion sunRotation;
            public Material skybox;
            public AmbientMode ambientMode;
            public Color ambientSky, ambientEquator, ambientGround;
            public bool fog;
            public FogMode fogMode;
            public Color fogColor;
            public float fogDensity;
        }

        private Saved saved;
        private Light sun;
        private Material nightSky;
        private Light flashlight;
        private Camera flashlightCamera;

        private void OnEnable()
        {
            sun = RenderSettings.sun;
            saved = new Saved
            {
                sunIntensity = sun != null ? sun.intensity : 0f,
                sunColor = sun != null ? sun.color : Color.white,
                sunRotation = sun != null ? sun.transform.rotation : Quaternion.identity,
                skybox = RenderSettings.skybox,
                ambientMode = RenderSettings.ambientMode,
                ambientSky = RenderSettings.ambientSkyColor,
                ambientEquator = RenderSettings.ambientEquatorColor,
                ambientGround = RenderSettings.ambientGroundColor,
                fog = RenderSettings.fog,
                fogMode = RenderSettings.fogMode,
                fogColor = RenderSettings.fogColor,
                fogDensity = RenderSettings.fogDensity,
            };

            if (sun != null)
            {
                sun.intensity = moonIntensity;
                sun.color = new Color(0.55f, 0.65f, 1f);
                sun.transform.rotation = Quaternion.Euler(38f, 120f, 0f);
            }

            var shader = Shader.Find("Skybox/Procedural");
            if (shader != null)
            {
                if (nightSky == null)
                {
                    nightSky = new Material(shader);
                    nightSky.SetFloat("_SunSize", 0.03f);
                    nightSky.SetFloat("_AtmosphereThickness", 0.35f);
                    nightSky.SetColor("_SkyTint", new Color(0.12f, 0.14f, 0.25f));
                    nightSky.SetColor("_GroundColor", new Color(0.02f, 0.02f, 0.03f));
                    nightSky.SetFloat("_Exposure", 0.12f);
                }
                RenderSettings.skybox = nightSky;
            }

            // Dark, but never pitch black: silhouettes stay readable.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.1f, 0.12f, 0.2f);
            RenderSettings.ambientEquatorColor = new Color(0.06f, 0.07f, 0.1f);
            RenderSettings.ambientGroundColor = new Color(0.03f, 0.03f, 0.04f);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.03f, 0.035f, 0.06f);
            RenderSettings.fogDensity = fogDensity;
        }

        private void OnDisable()
        {
            if (sun != null)
            {
                sun.intensity = saved.sunIntensity;
                sun.color = saved.sunColor;
                sun.transform.rotation = saved.sunRotation;
            }
            RenderSettings.skybox = saved.skybox;
            RenderSettings.ambientMode = saved.ambientMode;
            RenderSettings.ambientSkyColor = saved.ambientSky;
            RenderSettings.ambientEquatorColor = saved.ambientEquator;
            RenderSettings.ambientGroundColor = saved.ambientGround;
            RenderSettings.fog = saved.fog;
            RenderSettings.fogMode = saved.fogMode;
            RenderSettings.fogColor = saved.fogColor;
            RenderSettings.fogDensity = saved.fogDensity;

            if (flashlight != null)
                Destroy(flashlight.gameObject);
            flashlight = null;
            flashlightCamera = null;
        }

        private void OnDestroy()
        {
            if (nightSky != null)
                Destroy(nightSky);
        }

        private void Update()
        {
            float t = Time.time;
            for (int i = 0; i < fireLights.Count; i++)
            {
                var l = fireLights[i];
                if (l == null) continue;
                float n = Mathf.PerlinNoise(t * 7f, i * 3.1f);
                l.intensity = 1.4f + n * 1.3f;
            }
        }

        private void LateUpdate()
        {
            // Follow whichever camera renders (player, killcam, menu camera).
            var cam = Camera.main;
            if (cam == null)
            {
                foreach (var c in Camera.allCameras)
                    if (c.isActiveAndEnabled) { cam = c; break; }
            }
            if (cam == flashlightCamera && flashlight != null)
                return;
            if (cam == null)
                return;

            if (flashlight == null)
            {
                var go = new GameObject("NightfallFlashlight");
                flashlight = go.AddComponent<Light>();
                flashlight.type = LightType.Spot;
                flashlight.spotAngle = 52f;
                flashlight.innerSpotAngle = 22f;
                flashlight.color = new Color(1f, 0.95f, 0.85f);
                flashlight.shadows = LightShadows.None;
            }
            flashlight.intensity = flashlightIntensity;
            flashlight.range = flashlightRange;
            flashlight.transform.SetParent(cam.transform, false);
            flashlight.transform.localPosition = new Vector3(0.15f, -0.1f, 0.1f);
            flashlight.transform.localRotation = Quaternion.identity;
            flashlightCamera = cam;
        }
    }
}
