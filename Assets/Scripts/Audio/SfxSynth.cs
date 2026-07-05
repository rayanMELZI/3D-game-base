using System;
using System.Collections.Generic;
using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Procedurally synthesized sound effects — no audio files needed.
    /// Gunshots are layered (sharp crack + filtered body + sub-bass thump +
    /// decaying tail, then saturated) so they punch instead of hissing.
    /// Clips are generated once and cached.
    /// </summary>
    public static class SfxSynth
    {
        private const int SampleRate = 44100;
        private const float Tau = Mathf.PI * 2f;

        private static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();
        private static readonly System.Random rng = new System.Random();

        private static float Noise() => (float)(rng.NextDouble() * 2.0 - 1.0);

        // ------------------------------------------------------------------
        // Gunshots
        // ------------------------------------------------------------------

        public static AudioClip Shot(WeaponModelType type)
        {
            switch (type)
            {
                case WeaponModelType.Knife:
                    return MakeBuffered("shot_knife", 0.14f, b =>
                    {
                        AddFilteredNoise(b, 0f, 0.12f, 0.5f, 26f, 0.35f); // whoosh
                        Saturate(b, 1.2f);
                    });
                case WeaponModelType.Pistol:
                    return MakeBuffered("shot_pistol", 0.3f, b =>
                    {
                        AddFilteredNoise(b, 0f, 0.015f, 1.1f, 90f, 0.9f);   // crack
                        AddFilteredNoise(b, 0f, 0.1f, 0.9f, 40f, 0.3f);     // body
                        AddSine(b, 0f, 0.15f, 160f, 90f, 0.7f, 22f);       // thump
                        AddFilteredNoise(b, 0.02f, 0.25f, 0.3f, 9f, 0.08f); // tail
                        Saturate(b, 1.9f);
                    });
                case WeaponModelType.Smg:
                    return MakeBuffered("shot_smg", 0.22f, b =>
                    {
                        AddFilteredNoise(b, 0f, 0.012f, 1.1f, 110f, 0.85f);
                        AddFilteredNoise(b, 0f, 0.08f, 0.85f, 45f, 0.35f);
                        AddSine(b, 0f, 0.12f, 180f, 100f, 0.6f, 26f);
                        AddFilteredNoise(b, 0.015f, 0.18f, 0.25f, 11f, 0.09f);
                        Saturate(b, 1.9f);
                    });
                case WeaponModelType.Shotgun:
                    return MakeBuffered("shot_shotgun", 0.55f, b =>
                    {
                        AddFilteredNoise(b, 0f, 0.03f, 1.2f, 60f, 0.7f);
                        AddFilteredNoise(b, 0f, 0.25f, 1.1f, 16f, 0.18f);   // wide boom
                        AddSine(b, 0f, 0.3f, 95f, 50f, 1f, 11f);
                        AddSine(b, 0.01f, 0.35f, 60f, 40f, 0.7f, 8f);
                        AddFilteredNoise(b, 0.05f, 0.5f, 0.35f, 6f, 0.06f);
                        Saturate(b, 2.2f);
                    });
                case WeaponModelType.Sniper:
                    return MakeBuffered("shot_sniper", 0.8f, b =>
                    {
                        AddFilteredNoise(b, 0f, 0.02f, 1.3f, 80f, 0.95f);   // supersonic crack
                        AddFilteredNoise(b, 0f, 0.22f, 1f, 14f, 0.22f);
                        AddSine(b, 0f, 0.4f, 85f, 42f, 1f, 9f);
                        AddFilteredNoise(b, 0.06f, 0.7f, 0.4f, 4.5f, 0.05f); // long echo tail
                        Saturate(b, 2.1f);
                    });
                case WeaponModelType.Rpg:
                    return MakeBuffered("shot_rpg", 0.6f, b =>
                    {
                        AddFilteredNoise(b, 0f, 0.45f, 1f, 8f, 0.2f);       // launch whoosh
                        AddSine(b, 0f, 0.3f, 70f, 45f, 0.8f, 10f);
                        AddFilteredNoise(b, 0.1f, 0.45f, 0.4f, 5f, 0.5f);   // hiss
                        Saturate(b, 1.7f);
                    });
                default: // rifle
                    return MakeBuffered("shot_rifle", 0.4f, b =>
                    {
                        AddFilteredNoise(b, 0f, 0.015f, 1.2f, 100f, 0.9f);
                        AddFilteredNoise(b, 0f, 0.12f, 1f, 32f, 0.28f);
                        AddSine(b, 0f, 0.2f, 140f, 75f, 0.8f, 16f);
                        AddFilteredNoise(b, 0.025f, 0.35f, 0.32f, 7f, 0.07f);
                        Saturate(b, 2f);
                    });
            }
        }

        public static AudioClip Explosion() =>
            MakeBuffered("explosion", 1.1f, b =>
            {
                AddFilteredNoise(b, 0f, 0.05f, 1.3f, 40f, 0.5f);   // blast crack
                AddFilteredNoise(b, 0f, 0.7f, 1.2f, 5f, 0.1f);     // roar
                AddSine(b, 0f, 0.8f, 65f, 28f, 1.2f, 5f);          // deep boom
                AddSine(b, 0.02f, 0.6f, 42f, 25f, 0.8f, 5f);
                AddFilteredNoise(b, 0.15f, 0.9f, 0.35f, 3f, 0.05f); // rumble tail
                Saturate(b, 2.4f);
            });

        // ------------------------------------------------------------------
        // Feedback & UI
        // ------------------------------------------------------------------

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

        /// <summary>Positional 3D sound (gunshots, deaths, explosions).</summary>
        public static void PlayAt(AudioClip clip, Vector3 position, float volume = 1f)
        {
            var source = MakeSource(clip, volume);
            source.transform.position = position;
            source.spatialBlend = 1f;
            source.minDistance = 5f;
            source.maxDistance = 90f;
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
            source.pitch = 1f + UnityEngine.Random.Range(-0.07f, 0.07f);
            UnityEngine.Object.Destroy(go, clip.length + 0.25f);
            return source;
        }

        // ------------------------------------------------------------------
        // Synthesis helpers
        // ------------------------------------------------------------------

        /// <summary>Layered noise through a one-pole lowpass with exponential decay.</summary>
        private static void AddFilteredNoise(float[] buffer, float start, float duration, float amp, float decayPerSec, float lowpass01)
        {
            int from = (int)(start * SampleRate);
            int to = Mathf.Min(buffer.Length, from + (int)(duration * SampleRate));
            float filtered = 0f;
            for (int i = from; i < to; i++)
            {
                float t = (i - from) / (float)SampleRate;
                float x = Noise() * amp * Mathf.Exp(-t * decayPerSec);
                filtered += lowpass01 * (x - filtered);
                buffer[i] += filtered;
            }
        }

        /// <summary>Sine sweep from freqStart to freqEnd with exponential decay.</summary>
        private static void AddSine(float[] buffer, float start, float duration, float freqStart, float freqEnd, float amp, float decayPerSec)
        {
            int from = (int)(start * SampleRate);
            int to = Mathf.Min(buffer.Length, from + (int)(duration * SampleRate));
            float phase = 0f;
            for (int i = from; i < to; i++)
            {
                float t = (i - from) / (float)SampleRate;
                float freq = Mathf.Lerp(freqStart, freqEnd, duration > 0f ? t / duration : 0f);
                phase += Tau * freq / SampleRate;
                buffer[i] += Mathf.Sin(phase) * amp * Mathf.Exp(-t * decayPerSec);
            }
        }

        /// <summary>Soft-clip saturation: adds punch and glues the layers together.</summary>
        private static void Saturate(float[] buffer, float drive)
        {
            for (int i = 0; i < buffer.Length; i++)
                buffer[i] = Mathf.Atan(buffer[i] * drive) / (Mathf.PI / 2f) * 0.85f;
        }

        private static AudioClip MakeBuffered(string key, float duration, Action<float[]> fill)
        {
            if (cache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            int samples = (int)(duration * SampleRate);
            var data = new float[samples];
            fill(data);

            // Fade-out at the very end prevents clicks.
            int fade = Mathf.Min(samples, SampleRate / 100);
            for (int i = 0; i < fade; i++)
                data[samples - 1 - i] *= i / (float)fade;

            var clip = AudioClip.Create(key, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            cache[key] = clip;
            return clip;
        }

        private static AudioClip Make(string key, float duration, Func<float, float> wave)
        {
            return MakeBuffered(key, duration, buffer =>
            {
                for (int i = 0; i < buffer.Length; i++)
                    buffer[i] = Mathf.Clamp(wave(i / (float)SampleRate), -1f, 1f);
            });
        }
    }
}
