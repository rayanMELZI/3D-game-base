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

        public float sensitivity = 2.5f;
        public float minPitch = -89f;
        public float maxPitch = 89f;

        [Header("Recoil")]
        public float recoilRecoverySpeed = 8f;

        private float pitch;
        private float recoil; // extra upward kick from shooting, recovers over time

        private void Start()
        {
            LockCursor(true);
        }

        private void Update()
        {
            // Cursor lock handling.
            if (Input.GetKeyDown(KeyCode.Escape))
                LockCursor(false);
            if (Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0))
                LockCursor(true);

            if (Cursor.lockState != CursorLockMode.Locked)
                return; // don't rotate while the cursor is free

            float mouseX = Input.GetAxis("Mouse X") * sensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * sensitivity;

            // Yaw rotates the whole body so movement direction follows the view.
            if (playerBody != null)
                playerBody.Rotate(Vector3.up * mouseX);

            // Pitch only rotates the camera.
            pitch = Mathf.Clamp(pitch - mouseY, minPitch, maxPitch);

            // Recoil decays back to zero.
            recoil = Mathf.Lerp(recoil, 0f, recoilRecoverySpeed * Time.deltaTime);

            transform.localRotation = Quaternion.Euler(pitch - recoil, 0f, 0f);
        }

        /// <summary>Called by the gun to kick the view upward.</summary>
        public void AddRecoil(float amount)
        {
            recoil += amount;
        }

        public static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
