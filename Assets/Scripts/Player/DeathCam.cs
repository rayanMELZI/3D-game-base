using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Kill cam: takes over the local camera and slowly orbits a target —
    /// your killer while you wait to respawn, or the match's final killer
    /// when the game ends. This is a live spectate (not a recorded replay).
    /// The camera is restored exactly where it was when End() is called.
    /// </summary>
    public class DeathCam : MonoBehaviour
    {
        private static DeathCam instance;

        /// <summary>Label shown by the HUD while the cam is active (null = inactive).</summary>
        public static string CurrentLabel =>
            instance != null && instance.active ? instance.label : null;

        private Camera cam;
        private Transform target;
        private string label;
        private bool active;
        private float startTime;
        private Vector3 savedLocalPos;
        private Quaternion savedLocalRot;

        public static void Begin(Transform target, string label)
        {
            if (target == null || Camera.main == null)
                return;
            if (instance == null)
                instance = new GameObject("DeathCam").AddComponent<DeathCam>();
            instance.StartSpectate(Camera.main, target, label);
        }

        public static void End()
        {
            if (instance != null)
                instance.StopSpectate();
        }

        private void StartSpectate(Camera camera, Transform newTarget, string newLabel)
        {
            if (!active)
            {
                cam = camera;
                savedLocalPos = cam.transform.localPosition;
                savedLocalRot = cam.transform.localRotation;
                startTime = Time.time;
            }
            target = newTarget;
            label = newLabel;
            active = true;
        }

        private void StopSpectate()
        {
            if (!active)
                return;
            active = false;
            if (cam != null)
            {
                cam.transform.localPosition = savedLocalPos;
                cam.transform.localRotation = savedLocalRot;
            }
        }

        private void LateUpdate()
        {
            if (!active)
                return;
            if (target == null || cam == null)
            {
                StopSpectate();
                return;
            }

            // Slow orbit around the target at head height.
            float angle = (Time.time - startTime) * 35f + 180f;
            var offset = Quaternion.Euler(0, angle, 0) * new Vector3(0, 2f, -3.4f);
            var focus = target.position + Vector3.up * 1.3f;
            cam.transform.position = focus + offset;
            cam.transform.rotation = Quaternion.LookRotation(focus - cam.transform.position);
        }
    }
}
