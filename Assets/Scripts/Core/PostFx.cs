using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace FpsBase
{
    /// <summary>
    /// Reference to the Post Processing package's internal resources, baked into
    /// Assets/Resources/PostFxResources.prefab by the setup tool so the package
    /// shaders are included in builds and available at runtime.
    /// </summary>
    public class PostFxResourcesHolder : MonoBehaviour
    {
        public PostProcessResources resources;
    }

    /// <summary>
    /// Runtime post-processing setup: bloom (makes all the emissive trims and
    /// visors glow), ACES filmic tonemapping with warm grading, a soft vignette
    /// and FXAA. Attached to every camera the game creates. If the setup tool
    /// hasn't baked the resources holder yet, this silently does nothing.
    /// </summary>
    public static class PostFx
    {
        private const int VolumeLayer = 1; // built-in "TransparentFX" layer

        private static PostProcessVolume globalVolume;

        public static void Attach(Camera cam)
        {
            if (cam == null || cam.GetComponent<PostProcessLayer>() != null)
                return;

            var holderGo = Resources.Load<GameObject>("PostFxResources");
            var holder = holderGo != null ? holderGo.GetComponent<PostFxResourcesHolder>() : null;
            if (holder == null || holder.resources == null)
                return; // tool not run yet — the game still works, just without post fx

            cam.allowHDR = true;
            var layer = cam.gameObject.AddComponent<PostProcessLayer>();
            layer.Init(holder.resources);
            layer.volumeTrigger = cam.transform;
            layer.volumeLayer = 1 << VolumeLayer;
            layer.antialiasingMode = PostProcessLayer.Antialiasing.FastApproximateAntialiasing;

            EnsureGlobalVolume();
        }

        private static void EnsureGlobalVolume()
        {
            if (globalVolume != null)
                return;

            var profile = ScriptableObject.CreateInstance<PostProcessProfile>();

            var bloom = profile.AddSettings<Bloom>();
            bloom.enabled.Override(true);
            bloom.intensity.Override(2.6f);
            bloom.threshold.Override(1.05f);
            bloom.softKnee.Override(0.6f);

            var grading = profile.AddSettings<ColorGrading>();
            grading.enabled.Override(true);
            grading.tonemapper.Override(Tonemapper.ACES);
            grading.saturation.Override(12f);
            grading.contrast.Override(10f);
            grading.temperature.Override(8f);
            grading.postExposure.Override(0.2f);

            var vignette = profile.AddSettings<Vignette>();
            vignette.enabled.Override(true);
            vignette.intensity.Override(0.28f);
            vignette.smoothness.Override(0.4f);

            var go = new GameObject("PostFxVolume");
            go.layer = VolumeLayer;
            globalVolume = go.AddComponent<PostProcessVolume>();
            globalVolume.isGlobal = true;
            globalVolume.sharedProfile = profile;
        }
    }
}
