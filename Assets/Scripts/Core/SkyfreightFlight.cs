using Unity.Netcode;
using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Runtime life of the SKYFREIGHT plane map, added to its map root by
    /// EnvironmentBuilder.BuildSkyfreight (and kept when the map is baked):
    /// clouds stream past the open ramp, gentle turbulence shakes the camera,
    /// and falling out of the plane kills you. Everything here is local and
    /// visual except the fall, which goes through the server like any damage
    /// (the owner reports it, the server counts it as a suicide).
    /// </summary>
    public class SkyfreightFlight : MonoBehaviour
    {
        [Tooltip("Parent of the cloud objects that scroll past the plane.")]
        public Transform clouds;
        [Tooltip("How fast the clouds stream backwards (m/s).")]
        public float airspeed = 38f;
        [Tooltip("Clouds wrap around inside this Z span, centered on the plane.")]
        public float cloudSpan = 640f;
        [Tooltip("Below this height the local player has fallen out of the plane.")]
        public float fallKillY = -5f;
        [Tooltip("0 = no turbulence, 1 = default (light), keep it low to avoid motion sickness.")]
        [Range(0f, 2f)] public float turbulence = 1f;

        private bool fallReported;
        private float nextGust;
        private float gustStart = -10f;
        private float gustSign = 1f;

        // Fog is a global setting: take it over while the map exists, give it back after.
        private bool savedFog;
        private Color savedFogColor;
        private float savedFogDensity;
        private FogMode savedFogMode;

        private void OnEnable()
        {
            savedFog = RenderSettings.fog;
            savedFogColor = RenderSettings.fogColor;
            savedFogDensity = RenderSettings.fogDensity;
            savedFogMode = RenderSettings.fogMode;

            // High-altitude haze: pale blue, thin, fades the cloud sea far below.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.72f, 0.8f, 0.9f);
            RenderSettings.fogDensity = 0.0045f;

            nextGust = Time.time + 5f;
        }

        private void OnDisable()
        {
            RenderSettings.fog = savedFog;
            RenderSettings.fogColor = savedFogColor;
            RenderSettings.fogDensity = savedFogDensity;
            RenderSettings.fogMode = savedFogMode;
            MouseLook.MapShake = Vector3.zero;
        }

        private void Update()
        {
            ScrollClouds();
            MouseLook.MapShake = Turbulence();
            CheckFall();
        }

        /// <summary>The plane flies toward +Z: clouds slide toward -Z and wrap.</summary>
        private void ScrollClouds()
        {
            if (clouds == null)
                return;
            float step = airspeed * Time.deltaTime;
            float half = cloudSpan * 0.5f;
            foreach (Transform cloud in clouds)
            {
                var p = cloud.localPosition;
                p.z -= step;
                if (p.z < -half)
                    p.z += cloudSpan;
                cloud.localPosition = p;
            }
        }

        /// <summary>
        /// Slow Perlin sway (a fraction of a degree) plus an occasional soft bump.
        /// Mostly roll, which doesn't move the crosshair; pitch stays tiny.
        /// </summary>
        private Vector3 Turbulence()
        {
            if (turbulence <= 0f)
                return Vector3.zero;

            float t = Time.time;
            float roll = (Mathf.PerlinNoise(t * 0.45f, 3.1f) - 0.5f) * 0.7f;
            float pitch = (Mathf.PerlinNoise(7.7f, t * 0.6f) - 0.5f) * 0.25f;

            if (t >= nextGust)
            {
                gustStart = t;
                gustSign = Random.value < 0.5f ? -1f : 1f;
                nextGust = t + Random.Range(7f, 15f);
            }
            const float gustLength = 1.4f;
            float u = (t - gustStart) / gustLength;
            if (u >= 0f && u < 1f)
            {
                // Rises and settles once (sin envelope), with a light wobble.
                float bump = Mathf.Sin(u * Mathf.PI) * Mathf.Sin(u * Mathf.PI * 3f);
                roll += bump * 0.9f * gustSign;
                pitch += bump * 0.3f;
            }

            return new Vector3(pitch, 0f, roll) * turbulence;
        }

        /// <summary>
        /// Out the ramp = death. Only the owner knows its true position
        /// (client-authoritative movement), so it reports its own fall; the
        /// server ignores it outside an active match, and PlayerMovement's
        /// own killY then puts the player back at their spawn.
        /// </summary>
        private void CheckFall()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsClient || nm.LocalClient == null || nm.LocalClient.PlayerObject == null)
                return;

            var player = nm.LocalClient.PlayerObject;
            if (player.transform.position.y >= fallKillY)
            {
                fallReported = false;
                return;
            }
            if (fallReported)
                return;

            var health = player.GetComponent<NetworkHealth>();
            if (health != null && health.IsSpawned && health.CurrentHealth > 0f)
            {
                health.TakeDamage(health.MaxHealth * 10f);
                fallReported = true;
            }
        }
    }
}
