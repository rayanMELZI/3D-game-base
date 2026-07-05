using System;
using System.Collections.Generic;
using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Procedurally synthesized sound effects — no audio files needed.
    /// Clips are generated once (decaying noise bursts, sine "dings", little
    /// jingles) and played through throwaway AudioSources.
    /// </summary>
    public static class SfxSynth
    {
        private const int SampleRate = 44100;
        private const float Tau = Mathf.PI * 2f;

        private static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();
        private static readonly System.Random rng = new System.Random();

        private static float Noise() => (float)(rng.NextDouble() * 2.0 - 1.0);

        // ------------------------------------------------------------------
        // Clips
        // ------------------------------------------------------------------

        public static AudioClip Shot(WeaponModelType type)
        {
            switch (type)
            {
                case WeaponModelType.Pistol:
                    return Make("shot_pistol", 0.14f, t =>
                        Noise() * Mathf.Exp(-t * 70f) * 0.8f
                        + Mathf.Sin(Tau * 180f * t) * Mathf.Exp(-t * 55f) * 0.6f);
                case WeaponModelType.Sniper:
                    return Make("shot_sniper", 0.5f, t =>
                        Noise() * Mathf.Exp(-t * 18f) * 0.85f
                        + Mathf.Sin(Tau * (75f - 40f * t) * t) * Mathf.Exp(-t * 9f) * 0.8f);
                default: // rifle
                    return Make("shot_rifle", 0.16f, t =>
                        Noise() * Mathf.Exp(-t * 55f) * 0.85f
                        + Mathf.Sin(Tau * 115f * t) * Mathf.Exp(-t * 45f) * 0.65f);
            }
        }

        public static AudioClip Hit() =>
            Make("hit", 0.12f, t => Mathf.Sin(Tau * 1300f * t) * Mathf.Exp(-t * 24f) * 0.45f);

        public static AudioClip Headshot() =>
            Make("headshot", 0.28f, t =>
                (Mathf.Sin(Tau * 1568f * t) + 0.6f * Mathf.Sin(Tau * 2093f * t))
                * Mathf.Exp(-t * 13f) * 0.42f);

        public static AudioClip Reload() =>
            Make("reload", 0.22f, t =>
                Noise() * 0.55f * (Mathf.Exp(-t * 300f) + (t > 0.12f ? Mathf.Exp(-(t - 0.12f) * 300f) : 0f)));

        public static AudioClip Death() =>
            Make("death", 0.5f, t =>
                Mathf.Sin(Tau * (110f - 120f * t) * t) * Mathf.Exp(-t * 7f) * 0.75f
                + Noise() * Mathf.Exp(-t * 22f) * 0.4f);

        public static AudioClip UiClick() =>
            Make("click", 0.06f, t => Mathf.Sin(Tau * 900f * t) * Mathf.Exp(-t * 90f) * 0.4f);

        public static AudioClip WinJingle() =>
            Make("win", 1.2f, t => Note(t, 0f, 523f) + Note(t, 0.22f, 659f) + Note(t, 0.44f, 784f) + Note(t, 0.66f, 1047f));

        private static float Note(float t, float start, float freq)
        {
            if (t < start)
                return 0f;
            float local = t - start;
            return Mathf.Sin(Tau * freq * local) * Mathf.Exp(-local * 5f) * 0.3f;
        }

        // ------------------------------------------------------------------
        // Playback
        // ------------------------------------------------------------------

        /// <summary>Positional 3D sound (gunshots, deaths).</summary>
        public static void PlayAt(AudioClip clip, Vector3 position, float volume = 1f)
        {
            var source = MakeSource(clip, volume);
            source.transform.position = position;
            source.spatialBlend = 1f;
            source.minDistance = 4f;
            source.maxDistance = 70f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.Play();
        }

        /// <summary>Non-positional UI/feedback sound (hit marker, clicks).</summary>
        public static void Play2D(AudioClip clip, float volume = 1f)
        {
            var source = MakeSource(clip, volume);
            source.spatialBlend = 0f;
            source.Play();
        }

        private static AudioSource MakeSource(AudioClip clip, float volume)
        {
            var go = new GameObject("Sfx_" + clip.name);
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.volume = volume;
            source.pitch = 1f + UnityEngine.Random.Range(-0.05f, 0.05f);
            UnityEngine.Object.Destroy(go, clip.length + 0.2f);
            return source;
        }

        // ------------------------------------------------------------------

        private static AudioClip Make(string key, float duration, Func<float, float> wave)
        {
            if (cache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            int samples = (int)(duration * SampleRate);
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)SampleRate;
                // Tiny fade-out at the very end prevents clicks.
                float tail = Mathf.Clamp01((duration - t) * 40f);
                data[i] = Mathf.Clamp(wave(t), -1f, 1f) * tail;
            }

            var clip = AudioClip.Create(key, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            cache[key] = clip;
            return clip;
        }
    }
}
