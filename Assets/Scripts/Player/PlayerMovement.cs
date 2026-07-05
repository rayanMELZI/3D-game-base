using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// FPS movement on a CharacterController:
    /// WASD move · Left Shift sprint · Space jump (hold to bunny hop) ·
    /// Left Ctrl crouch · sprint+Ctrl slide.
    /// Uses the legacy Input Manager so it works out of the box.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMovement : MonoBehaviour
    {
        [Header("Speeds")]
        public float walkSpeed = 5f;
        public float sprintSpeed = 8.5f;
        public float crouchSpeed = 2.6f;

        [Header("Slide")]
        public float slideSpeed = 10.5f;
        public float slideDuration = 0.85f;

        [Header("Jumping / Gravity")]
        public float jumpHeight = 1.2f;
        public float gravity = -20f;

        [Header("Crouch dimensions")]
        public float standHeight = 1.55f;
        public float crouchHeight = 1.05f;
        public float standEyeHeight = 1.62f;
        public float crouchEyeHeight = 1.12f;

        [Header("References (wired by PlayerFactory)")]
        public Transform cameraTransform;
        public Transform bodyVisual;

        [Header("Respawn")]
        public Vector3 spawnPoint;
        public float killY = -20f;

        public bool IsSprinting { get; private set; }
        public bool IsCrouching { get; private set; }
        public bool IsSliding => slideTimer > 0f;
        /// <summary>0 = standing, 1 = fully crouched — also drives the body squash.</summary>
        public float CrouchBlend { get; private set; }
        /// <summary>0..1 while sliding, for camera roll.</summary>
        public float SlideBlend { get; private set; }

        private CharacterController controller;
        private float verticalVelocity;
        private float slideTimer;
        private Vector3 slideDirection;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (spawnPoint == Vector3.zero)
                spawnPoint = transform.position;
        }

        private void Update()
        {
            bool grounded = controller.isGrounded;

            float inputX = Input.GetAxisRaw("Horizontal");
            float inputZ = Input.GetAxisRaw("Vertical");
            Vector3 moveDirection = (transform.right * inputX + transform.forward * inputZ).normalized;
            bool moving = moveDirection.sqrMagnitude > 0.1f;

            bool wantCrouch = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C);
            IsSprinting = Input.GetKey(KeyCode.LeftShift) && moving && !wantCrouch && !IsSliding;

            // --- Slide: press crouch while sprinting on the ground ---
            if (grounded && slideTimer <= 0f
                && Input.GetKey(KeyCode.LeftShift) && moving
                && (Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.C)))
            {
                slideTimer = slideDuration;
                slideDirection = moveDirection;
            }
            if (slideTimer > 0f)
            {
                slideTimer -= Time.deltaTime;
                if (!grounded)
                    slideTimer = 0f; // sliding off a ledge ends the slide
            }
            SlideBlend = Mathf.Clamp01(slideTimer / (slideDuration * 0.5f));

            IsCrouching = wantCrouch || IsSliding;

            // --- Speed ---
            float speed;
            Vector3 horizontal;
            if (IsSliding)
            {
                float slideT = 1f - slideTimer / slideDuration;
                speed = Mathf.Lerp(slideSpeed, crouchSpeed, slideT);
                horizontal = slideDirection * speed; // locked direction, slight steer:
                horizontal += (transform.right * inputX) * 1.2f;
            }
            else
            {
                speed = IsCrouching ? crouchSpeed : (IsSprinting ? sprintSpeed : walkSpeed);
                horizontal = moveDirection * speed;
            }

            // --- Crouch dimensions (controller + camera + visual squash) ---
            CrouchBlend = Mathf.MoveTowards(CrouchBlend, IsCrouching ? 1f : 0f, 8f * Time.deltaTime);
            float height = Mathf.Lerp(standHeight, crouchHeight, CrouchBlend);
            controller.height = height;
            controller.center = new Vector3(0, height / 2f, 0);
            if (cameraTransform != null)
            {
                var camPos = cameraTransform.localPosition;
                camPos.y = Mathf.Lerp(standEyeHeight, crouchEyeHeight, CrouchBlend);
                cameraTransform.localPosition = camPos;
            }
            ApplyCrouchVisual(bodyVisual, CrouchBlend);

            // --- Jump (hold Space to bunny hop) + gravity ---
            if (grounded && verticalVelocity < 0f)
                verticalVelocity = -2f;

            if (grounded && Input.GetButton("Jump"))
            {
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                slideTimer = 0f; // jumping cancels a slide
            }

            verticalVelocity += gravity * Time.deltaTime;

            controller.Move((horizontal + Vector3.up * verticalVelocity) * Time.deltaTime);

            if (transform.position.y < killY)
                Respawn();
        }

        /// <summary>Squash the visual body when crouching (also used for remote players).</summary>
        public static void ApplyCrouchVisual(Transform body, float blend)
        {
            if (body != null)
                body.localScale = new Vector3(1f, Mathf.Lerp(1f, 0.68f, blend), 1f);
        }

        public void Respawn()
        {
            // CharacterController overrides transform changes unless disabled first.
            controller.enabled = false;
            transform.position = spawnPoint;
            verticalVelocity = 0f;
            controller.enabled = true;
        }
    }
}
