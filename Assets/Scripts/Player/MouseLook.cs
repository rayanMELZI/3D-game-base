using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Mouse look for an FPS camera. Lives on the camera object.
    /// Rotates the player body left/right (yaw) and the camera up/down (pitch).
    /// Escape unlocks the cursor; clicking in the game view locks it again.
    /// </summary>
    public class MouseLook : MonoBehaviour
    {
        [Tooltip("The player root that should rotate horizontally.")]
        public Transform playerBody;
        [Tooltip("Optional — enables the subtle sprint FOV boost.")]
        public PlayerMovement movement;

        public float sensitivity = 2.5f;
        public float minPitch = -89f;
        public float maxPitch = 89f;

        [Header("Zoom")]
        public float baseFov = 60f;
        public float zoomLerpSpeed = 14f;

        [Header("Recoil")]
        public float recoilRecoverySpeed = 8f;

        /// <summary>Current view pitch in degrees (up = negative). Replicated for remote aim pose.</summary>
        public float CurrentPitch => pitch;

        private float pitch;
        private float recoil;  // extra upward kick from shooting, recovers over time
        private float zoomFov; // 0 = not zoomed
        private Camera cam;

        private void Awake()
        {
            cam = GetComponent<Camera>();
        }

        // The user-configurable FOV from the settings menu.
        private float BaseFovNow => GameSettings.Fov > 0 ? GameSettings.Fov : baseFov;

        private void Start()
        {
            if (cam != null)
                cam.fieldOfView = BaseFovNow;
            LockCursor(true);
        }

        private void Update()
        {
            // Escape opens the pause menu (the menu's RESUME button re-locks).
            if (Input.GetKeyDown(KeyCode.Escape))
                LockCursor(false);

            // Smoothly move toward the target FOV (zoom, plus a subtle sprint boost).
            if (cam != null)
            {
                float targetFov = zoomFov > 0f
                    ? zoomFov
                    : BaseFovNow * (movement != null && movement.IsSprinting ? 1.07f : 1f);
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, zoomLerpSpeed * Time.deltaTime);
            }

            if (Cursor.lockState != CursorLockMode.Locked)
                return; // don't rotate while the cursor is free

            // Lower sensitivity proportionally while zoomed in; scale by the settings slider.
            float zoomFactor = cam != null ? cam.fieldOfView / BaseFovNow : 1f;
            float effective = sensitivity * GameSettings.MouseSensitivity * Mathf.Min(zoomFactor, 1f);
            float mouseX = Input.GetAxis("Mouse X") * effective;
            float mouseY = Input.GetAxis("Mouse Y") * effective;
            mouseX += Input.GetAxis("Controller Look X") * effective * 1.4f;
            mouseY += Input.GetAxis("Controller Look Y") * effective * 1.4f;

            // Yaw rotates the whole body so movement direction follows the view.
            if (playerBody != null)
                playerBody.Rotate(Vector3.up * mouseX);

            // Pitch only rotates the camera.
            pitch = Mathf.Clamp(pitch - mouseY, minPitch, maxPitch);

            // Recoil decays back to zero.
            recoil = Mathf.Lerp(recoil, 0f, recoilRecoverySpeed * Time.deltaTime);

            // Camera rolls a little while sliding.
            float roll = movement != null ? movement.SlideBlend * -7f : 0f;

            transform.localRotation = Quaternion.Euler(pitch - recoil, 0f, roll);
        }

        /// <summary>Called by the gun to kick the view upward.</summary>
        public void AddRecoil(float amount)
        {
            recoil += amount;
        }

        /// <summary>Set the zoom FOV, or 0 to return to normal view.</summary>
        public void SetZoom(float fov)
        {
            zoomFov = fov;
        }

        public static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
