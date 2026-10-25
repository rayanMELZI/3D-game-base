using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Self-contained post-processing (no packages needed): ambient occlusion
    /// from the camera depth (grounds the boxes and props), two-level bloom
    /// that makes the emissive trims/visors/tracers glow, ACES filmic
    /// tonemapping with a warm/cool split-tone grade, a soft vignette and
    /// FXAA when MSAA is off. Implemented as a classic OnRenderImage effect
    /// using Assets/Shaders/SundownPost.shader.
    /// The expensive parts scale with the quality level: no AO below Medium,
    /// no FXAA when the quality level already gives MSAA.
    /// ExecuteAlways so editor preview renders (Tools > FPS Base > Render
    /// Preview Sheets) show the same grade as the game.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    public class SundownPostEffect : MonoBehaviour
    {
        // Threshold above plain sunlit surfaces: only emissives/tracers glow,
        // bright floors no longer bleed a milky haze over the whole frame.
        [Range(0f, 3f)] public float bloomIntensity = 0.35f;
        [Range(0f, 4f)] public float bloomThreshold = 2.2f;
        [Range(0.2f, 3f)] public float exposure = 1.08f;
        [Range(0f, 2f)] public float saturation = 1.1f;
        [Range(0.5f, 1.5f)] public float contrast = 1.06f;
        [Range(0f, 1f)] public float vignette = 0.18f;
        /// <summary>Added to the shadows (cool), multiplies the highlights (warm).</summary>
        public Color lift = new Color(0.0f, 0.012f, 0.03f);
        public Color gain = new Color(1.03f, 1.0f, 0.95f);

        [Header("Ambient occlusion")]
        public bool ambientOcclusion = true;
        [Range(0f, 1.5f)] public float aoIntensity = 0.85f;
        [Range(0.1f, 3f)] public float aoRadius = 0.7f;
        public float aoFadeStart = 45f;
        public float aoFadeEnd = 90f;

        [Header("Anti-aliasing")]
        public bool fxaa = true;

        private Material material;
        private Camera cam;

        private void OnEnable()
        {
            var shader = Shader.Find("Hidden/SundownPost");
            if (shader == null || !shader.isSupported)
            {
                enabled = false; // fail soft: game renders without post fx
                return;
            }
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            cam = GetComponent<Camera>();
            cam.allowHDR = true; // needed for bloom on emissives
            OnPreCull();
        }

        private void OnDisable()
        {
            if (material != null)
                DestroyImmediate(material);
            material = null;
        }

        /// <summary>AO costs a depth pass and ~12 taps per half-res pixel: Medium quality and up.</summary>
        private bool UseAO => ambientOcclusion && aoIntensity > 0f && QualitySettings.GetQualityLevel() >= 2
                              && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf);

        /// <summary>FXAA only when the camera isn't already multisampled.</summary>
        private bool UseFxaa => fxaa && (QualitySettings.antiAliasing <= 1 || !cam.allowMSAA);

        private void OnPreCull()
        {
            if (cam != null && UseAO)
                cam.depthTextureMode |= DepthTextureMode.Depth;
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (material == null)
            {
                Graphics.Blit(source, destination);
                return;
            }

            // Ambient occlusion at half resolution, blurred without bleeding across depth edges.
            RenderTexture ao = null;
            if (UseAO)
            {
                int aw = Mathf.Max(1, source.width / 2);
                int ah = Mathf.Max(1, source.height / 2);
                var fmt = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGHalf)
                    ? RenderTextureFormat.RGHalf : RenderTextureFormat.ARGBHalf;
                ao = RenderTexture.GetTemporary(aw, ah, 0, fmt, RenderTextureReadWrite.Linear);
                var aoTmp = RenderTexture.GetTemporary(aw, ah, 0, fmt, RenderTextureReadWrite.Linear);
                var proj = cam.projectionMatrix;
                material.SetVector("_ProjScale", new Vector4(1f / proj.m00, 1f / proj.m11, 0f, 0f));
                material.SetVector("_AOParams", new Vector4(aoRadius, 0.03f, aoFadeStart, aoFadeEnd));
                Graphics.Blit(source, ao, material, 3);
                material.SetVector("_BlurDir", new Vector4(1, 0, 0, 0));
                Graphics.Blit(ao, aoTmp, material, 4);
                material.SetVector("_BlurDir", new Vector4(0, 1, 0, 0));
                Graphics.Blit(aoTmp, ao, material, 4);
                RenderTexture.ReleaseTemporary(aoTmp);
                material.SetTexture("_AOTex", ao);
                material.SetFloat("_AOIntensity", aoIntensity);
                material.EnableKeyword("SUNDOWN_AO");
            }
            else
            {
                material.DisableKeyword("SUNDOWN_AO");
            }

            // Bloom: bright-pass at quarter resolution, blurred, plus a wider eighth-res level.
            int w = Mathf.Max(1, source.width / 4);
            int h = Mathf.Max(1, source.height / 4);
            var bloomA = RenderTexture.GetTemporary(w, h, 0, source.format);
            var bloomB = RenderTexture.GetTemporary(w, h, 0, source.format);
            var wideA = RenderTexture.GetTemporary(Mathf.Max(1, w / 2), Mathf.Max(1, h / 2), 0, source.format);
            var wideB = RenderTexture.GetTemporary(Mathf.Max(1, w / 2), Mathf.Max(1, h / 2), 0, source.format);

            material.SetFloat("_Threshold", bloomThreshold);
            Graphics.Blit(source, bloomA, material, 0);
            Blur(bloomA, bloomB, 2);
            Graphics.Blit(bloomA, wideA);
            Blur(wideA, wideB, 2);

            // Composite with AO, tonemapping, grading and vignette.
            material.SetTexture("_BloomTex", bloomA);
            material.SetTexture("_BloomTexWide", wideA);
            material.SetFloat("_BloomIntensity", bloomIntensity);
            material.SetFloat("_Exposure", exposure);
            material.SetFloat("_Saturation", saturation);
            material.SetFloat("_Contrast", contrast);
            material.SetFloat("_Vignette", vignette);
            material.SetVector("_Lift", lift);
            material.SetVector("_Gain", gain);

            if (UseFxaa)
            {
                var graded = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.Default);
                graded.filterMode = FilterMode.Bilinear;
                Graphics.Blit(source, graded, material, 2);
                Graphics.Blit(graded, destination, material, 5);
                RenderTexture.ReleaseTemporary(graded);
            }
            else
            {
                Graphics.Blit(source, destination, material, 2);
            }

            RenderTexture.ReleaseTemporary(bloomA);
            RenderTexture.ReleaseTemporary(bloomB);
            RenderTexture.ReleaseTemporary(wideA);
            RenderTexture.ReleaseTemporary(wideB);
            if (ao != null)
                RenderTexture.ReleaseTemporary(ao);
        }

        private void Blur(RenderTexture a, RenderTexture b, int rounds)
        {
            for (int i = 0; i < rounds; i++)
            {
                material.SetVector("_BlurDir", new Vector4(1, 0, 0, 0));
                Graphics.Blit(a, b, material, 1);
                material.SetVector("_BlurDir", new Vector4(0, 1, 0, 0));
                Graphics.Blit(b, a, material, 1);
            }
        }
    }

    /// <summary>Attaches the post effect to every camera the game creates.</summary>
    public static class PostFx
    {
        public static void Attach(Camera cam)
        {
            if (cam != null && cam.GetComponent<SundownPostEffect>() == null)
                cam.gameObject.AddComponent<SundownPostEffect>();
        }
    }
}
