using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Self-contained post-processing (no packages needed): bloom that makes
    /// the emissive trims/visors/tracers glow, ACES filmic tonemapping with a
    /// warm grade, and a soft vignette. Implemented as a classic OnRenderImage
    /// effect using Assets/Shaders/SundownPost.shader.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class SundownPostEffect : MonoBehaviour
    {
        [Range(0f, 3f)] public float bloomIntensity = 1.2f;
        [Range(0f, 2f)] public float bloomThreshold = 1f;
        [Range(0f, 2f)] public float saturation = 1.15f;
        [Range(0.5f, 1.5f)] public float contrast = 1.05f;
        [Range(0f, 1f)] public float vignette = 0.35f;

        private Material material;

        private void OnEnable()
        {
            var shader = Shader.Find("Hidden/SundownPost");
            if (shader == null || !shader.isSupported)
            {
                enabled = false; // fail soft: game renders without post fx
                return;
            }
            material = new Material(shader);
            GetComponent<Camera>().allowHDR = true; // needed for bloom on emissives
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (material == null)
            {
                Graphics.Blit(source, destination);
                return;
            }

            // Bloom: bright-pass at quarter resolution, then two blur rounds.
            int w = Mathf.Max(1, source.width / 4);
            int h = Mathf.Max(1, source.height / 4);
            var bloomA = RenderTexture.GetTemporary(w, h, 0, source.format);
            var bloomB = RenderTexture.GetTemporary(w, h, 0, source.format);

            material.SetFloat("_Threshold", bloomThreshold);
            Graphics.Blit(source, bloomA, material, 0);
            for (int i = 0; i < 2; i++)
            {
                material.SetVector("_BlurDir", new Vector4(1, 0, 0, 0));
                Graphics.Blit(bloomA, bloomB, material, 1);
                material.SetVector("_BlurDir", new Vector4(0, 1, 0, 0));
                Graphics.Blit(bloomB, bloomA, material, 1);
            }

            // Composite with tonemapping, grading and vignette.
            material.SetTexture("_BloomTex", bloomA);
            material.SetFloat("_BloomIntensity", bloomIntensity);
            material.SetFloat("_Saturation", saturation);
            material.SetFloat("_Contrast", contrast);
            material.SetFloat("_Vignette", vignette);
            Graphics.Blit(source, destination, material, 2);

            RenderTexture.ReleaseTemporary(bloomA);
            RenderTexture.ReleaseTemporary(bloomB);
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
