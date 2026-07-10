using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Kill cam. Two modes:
    ///  - Replay: plays back the killer's buffered movement from their own eyes
    ///    (a real CoD-style killcam, skippable with click/space), then falls
    ///    back to the orbit.
    ///  - Orbit: live spectate slowly circling a target (used after the replay,
    ///    and for the match-end winner cam).
    /// The camera is restored exactly where it was when End() is called.
    /// </summary>
    public class DeathCam : MonoBehaviour
    {
        private static DeathCam instance;

        /// <summary>Label shown by the HUD while the cam is active (null = inactive).</summary>
        public static string CurrentLabel =>
            instance != null && instance.active
                ? (instance.replaying ? instance.label + "   [click to skip]" : instance.label)
                : null;

        private Camera cam;
        private Transform target;
        private string label;
        private bool active;
        private float startTime;
        private Vector3 savedLocalPos;
        private Quaternion savedLocalRot;

        // Replay state.
        private NetworkPlayer.KillcamSample[] replay;
        private bool replaying;
        private float replayClock;    // current playback time (in the samples' timebase)
        private const float ReplayLength = 3.2f; // seconds of the killer's past to show
        private const float EyeHeight = 1.62f;

        public static void Begin(Transform target, string label)
        {
            if (target == null || Camera.main == null)
                return;
            if (instance == null)
                instance = new GameObject("DeathCam").AddComponent<DeathCam>();
            instance.StartSpectate(Camera.main, target, label);
        }

        /// <summary>Killcam: replay the killer's buffered last seconds from their eyes, then orbit.</summary>
        public static void BeginReplay(NetworkPlayer.KillcamSample[] history, Transform target, string label)
        {
            if (Camera.main == null)
                return;
            if (instance == null)
                instance = new GameObject("DeathCam").AddComponent<DeathCam>();
            instance.StartSpectate(Camera.main, target, label);

            if (history != null && history.Length >= 2)
            {
                instance.replay = history;
                instance.replaying = true;
                float end = history[history.Length - 1].time;
                instance.replayClock = Mathf.Max(history[0].time, end - ReplayLength);
            }
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
            replaying = false;
            replay = null;
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
            if (cam == null)
            {
                StopSpectate();
                return;
            }

            if (replaying)
            {
                UpdateReplay();
                return;
            }

            if (target == null)
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

        private void UpdateReplay()
        {
            // Skippable with click or space → drop to the orbit spectate.
            bool finished = replayClock >= replay[replay.Length - 1].time;
            if (finished || Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space))
            {
                replaying = false;
                replay = null;
                startTime = Time.time; // restart the orbit angle from behind
                return;
            }

            replayClock += Time.deltaTime;

            // Find the two samples around the playback clock and interpolate.
            int i = 0;
            while (i < replay.Length - 2 && replay[i + 1].time < replayClock)
                i++;
            var a = replay[i];
            var b = replay[Mathf.Min(i + 1, replay.Length - 1)];
            float span = Mathf.Max(0.0001f, b.time - a.time);
            float t = Mathf.Clamp01((replayClock - a.time) / span);

            Vector3 pos = Vector3.Lerp(a.position, b.position, t) + Vector3.up * EyeHeight;
            float yaw = Mathf.LerpAngle(a.yaw, b.yaw, t);
            float pitch = Mathf.Lerp(a.pitch, b.pitch, t);

            cam.transform.position = pos;
            cam.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
    }
}
