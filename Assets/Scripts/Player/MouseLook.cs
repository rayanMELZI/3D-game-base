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

        [Header("Third person (P Story mode only) — tweak these live in the Inspector")]
        [Tooltip("How far behind the player the camera sits.")]
        public float tpDistance = 4.4f;
        [Tooltip("Camera height above the player's feet (higher = looks down more).")]
        public float tpHeight = 2.15f;
        [Tooltip("Sideways shoulder offset. 0 = centered behind the head (crosshair tracks like FPS); higher = more over-the-shoulder.")]
        public float tpShoulder = 0.35f;

        /// <summary>Current view pitch in degrees (up = negative). Replicated for remote aim pose.</summary>
        public float CurrentPitch => pitch;

        private float pitch;
        private float recoil;  // extra upward kick from shooting, recovers over time
        private float zoomFov; // 0 = not zoomed
        private Camera cam;

        private Vector3 fpLocalPos;      // saved first-person camera position
        private bool thirdPerson;
        private float aimBlend;          // 0 hip → 1 aiming, for the TPS camera pull-in
        private PlayerRigRefs rig;
        private WeaponController weaponController;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            fpLocalPos = transform.localPosition;
            rig = GetComponentInParent<PlayerRigRefs>();
            weaponController = rig != null ? rig.weaponController : GetComponentInParent<WeaponController>();
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

            // Aiming (right-click) blend, used to tighten the third-person camera.
            aimBlend = Mathf.MoveTowards(aimBlend, zoomFov > 0f ? 1f : 0f, 6f * Time.deltaTime);

            // Smoothly move toward the target FOV (zoom, plus a subtle sprint boost).
            if (cam != null)
            {
                float targetFov;
                if (zoomFov > 0f)
                    // In third person a full sniper-scope FOV looks wrong, so aiming
                    // there is a gentle zoom (the camera pulls in instead).
                    targetFov = thirdPerson ? Mathf.Max(zoomFov, 46f) : zoomFov;
                else
                    targetFov = BaseFovNow * (movement != null && movement.IsSprinting ? 1.07f : 1f);
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

            ApplyCameraRig(pitch - recoil, roll);
        }

        /// <summary>
        /// Places the camera: first person (at the eye) in every normal mode, or
        /// over-the-shoulder third person in P Story only. Switching also flips the
        /// owner's body between shadows-only (FPS) and visible (TPS) and swaps the
        /// viewmodel for the body-held weapon.
        /// </summary>
        private void ApplyCameraRig(float pitchNow, float roll)
        {
            bool wantThirdPerson = GameModeManager.Instance != null
                && GameModeManager.Instance.IsSpawned
                && GameModeManager.Instance.CurrentMode == GameMode.PStory;

            if (wantThirdPerson != thirdPerson)
            {
                thirdPerson = wantThirdPerson;
                if (rig != null)
                    rig.SetFirstPerson(!thirdPerson);   // TPS → show the body
                if (weaponController != null)
                    weaponController.SetThirdPersonView(thirdPerson);
                if (!thirdPerson)
                    transform.localPosition = fpLocalPos; // restore the eye position
            }

            if (!thirdPerson)
            {
                transform.localRotation = Quaternion.Euler(pitchNow, 0f, roll);
                return;
            }

            // Over-the-shoulder: orbit a shoulder pivot by pitch, pull in on walls.
            // Aiming pulls the camera closer and centers it for a precise reticle.
            float dist = Mathf.Lerp(tpDistance, tpDistance * 0.58f, aimBlend);
            float shoulder = Mathf.Lerp(tpShoulder, 0.12f, aimBlend);
            Transform root = playerBody != null ? playerBody : transform.parent;
            Vector3 pivotLocal = new Vector3(0f, tpHeight, 0f);
            Vector3 desiredLocal = pivotLocal
                + Quaternion.Euler(pitchNow, 0f, 0f) * new Vector3(shoulder, 0f, -dist);

            Vector3 pivotWorld = root.TransformPoint(pivotLocal);
            Vector3 desiredWorld = root.TransformPoint(desiredLocal);
            Vector3 dir = desiredWorld - pivotWorld;
            float castLen = dir.magnitude;
            if (castLen > 0.01f && CameraCastIgnoringSelf(pivotWorld, dir / castLen, castLen, root, out float hitDist))
                desiredWorld = pivotWorld + dir / castLen * Mathf.Max(0.6f, hitDist - 0.15f);

            transform.position = desiredWorld;
            transform.localRotation = Quaternion.Euler(pitchNow, 0f, roll);
        }

        /// <summary>Sphere-cast for the follow camera, skipping the player's own colliders.</summary>
        private bool CameraCastIgnoringSelf(Vector3 origin, Vector3 dir, float maxDist, Transform self, out float hitDist)
        {
            hitDist = maxDist;
            var hits = Physics.SphereCastAll(origin, 0.25f, dir, maxDist,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            bool found = false;
            foreach (var h in hits)
            {
                if (h.collider.transform.root == self.root)
                    continue; // never collide with ourselves
                if (h.distance < hitDist)
                {
                    hitDist = h.distance;
                    found = true;
                }
            }
            return found;
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
