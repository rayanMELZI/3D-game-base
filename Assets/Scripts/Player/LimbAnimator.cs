using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Simple procedural walk animation: swings the arm/leg pivots based on how
    /// fast the character is actually moving (measured from position deltas, so
    /// it works for the local player, network-replicated players and dummies
    /// alike), plus a subtle idle breathing sway.
    /// </summary>
    public class LimbAnimator : MonoBehaviour
    {
        [Header("Pivots (wired by the builder)")]
        public Transform armL;
        public Transform armR;
        public Transform legL;
        public Transform legR;

        public float stepFrequency = 2.6f; // swing cycles per meter-ish
        public float maxSwingDegrees = 38f;

        [Tooltip("Arms raised forward holding a weapon instead of hanging at the sides.")]
        public bool aimPose;

        private Vector3 lastPosition;
        private float phase;
        private float swingWeight; // 0 idle → 1 running

        private void Start()
        {
            lastPosition = transform.position;
        }

        private void Update()
        {
            if (Time.deltaTime <= 0f)
                return;

            // Horizontal speed from position deltas.
            Vector3 delta = transform.position - lastPosition;
            lastPosition = transform.position;
            delta.y = 0;
            float speed = delta.magnitude / Time.deltaTime;

            float targetWeight = Mathf.Clamp01(speed / 4.5f);
            swingWeight = Mathf.Lerp(swingWeight, targetWeight, 8f * Time.deltaTime);
            phase += speed * stepFrequency * Time.deltaTime;

            float swing = Mathf.Sin(phase) * maxSwingDegrees * swingWeight;
            float breathe = Mathf.Sin(Time.time * 1.7f) * 2.5f * (1f - swingWeight);

            if (legL != null) legL.localRotation = Quaternion.Euler(swing, 0, 0);
            if (legR != null) legR.localRotation = Quaternion.Euler(-swing, 0, 0);

            if (aimPose)
            {
                // Two hands up holding the weapon; only a slight bob while moving.
                if (armL != null) armL.localRotation = Quaternion.Euler(-52f + swing * 0.15f, 20f, breathe * 0.5f - 6f);
                if (armR != null) armR.localRotation = Quaternion.Euler(-70f + swing * 0.15f, -14f, breathe * 0.5f + 6f);
            }
            else
            {
                if (armL != null) armL.localRotation = Quaternion.Euler(-swing * 0.8f, 0, breathe + 4f);
                if (armR != null) armR.localRotation = Quaternion.Euler(swing * 0.8f, 0, -breathe - 4f);
            }
        }
    }
}
