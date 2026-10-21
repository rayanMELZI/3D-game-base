using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// FPS movement on a CharacterController:
    /// WASD move · Left Shift sprint · Space jump (hold to bunny hop) ·
    /// Left Ctrl crouch · sprint+Ctrl slide. Controller (see GamepadInput):
    /// left-stick click sprint (latched) · A jump · B tap crouch / hold prone /
    /// sprint+B slide.
    /// Uses the legacy Input Manager so it works out of the box.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMovement : MonoBehaviour
    {
        [Header("Speeds")]
        public float walkSpeed = 5f;
        public float sprintSpeed = 8.5f;
        public float crouchSpeed = 2.6f;
        /// <summary>Set every frame by the held weapon (WeaponDefinition.mobility): heavy guns slow you down.</summary>
        [System.NonSerialized] public float speedMultiplier = 1f;

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
        public float proneHeight = 0.55f;
        public float proneEyeHeight = 0.58f;
        public float proneSpeed = 1.45f;
        [Tooltip("Prone: the eye slides this far forward, over the lying head.")]
        public float proneEyeForward = 0.25f;

        [Header("Crouch Visual")]
        [Tooltip("Pose the Body (primitive meshes + hitboxes) for crouch/prone. The imported skin is not under the Body, so this only moves what is hit.")]
        public bool useProceduralBodyCrouch = true;

        [Header("References (wired by PlayerFactory)")]
        public Transform cameraTransform;
        public Transform bodyVisual;

        [Header("Respawn")]
        public Vector3 spawnPoint;
        public float killY = -20f;

        public bool IsSprinting { get; private set; }
        public bool IsCrouching { get; private set; }
        public bool IsSliding => slideTimer > 0f;
        public bool IsProne { get; private set; }
        public bool IsGrounded =>
            controller != null && controller.isGrounded;
        public float VerticalSpeed => verticalVelocity;
        public Vector2 MoveInput => moveInput;
        public float CurrentHorizontalSpeed => horizontalVelocity.magnitude;
        /// <summary>0 = standing, 1 = fully crouched — also drives the body squash.</summary>
        public float CrouchBlend { get; private set; }
        /// <summary>0..1 while sliding, for camera roll.</summary>
        public float SlideBlend { get; private set; }
        /// <summary>0 = up, 1 = lying flat — drives the hitbox pose, capsule and eye.</summary>
        public float ProneBlend { get; private set; }

        private CharacterController controller;
        private float verticalVelocity;
        private float slideTimer;
        private Vector3 slideDirection;
        private bool proneToggle;
        private Vector2 moveInput;
        private Vector3 horizontalVelocity;
        // Controller state: a stick click can't be held while steering, so sprint
        // is latched and crouch is a toggle; B held this long goes prone.
        private const float ProneHoldTime = 0.4f;
        private bool padSprint;
        private bool padCrouch;
        private bool padBUsed = true;
        private float padBHeldSince;


        
        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (spawnPoint == Vector3.zero)
                spawnPoint = transform.position;
        }

        // Disabled while dead / between matches: come back standing, not prone.
        private void OnDisable()
        {
            proneToggle = false;
            padCrouch = false;
            IsProne = false;
        }

        private void Update()
        {
            bool grounded = controller.isGrounded;

            float inputX = Input.GetAxisRaw("Horizontal");
            float inputZ = Input.GetAxisRaw("Vertical");
            // Clamped, not normalized: keys still give full speed, a half-tilted
            // left stick walks slower.
            Vector3 moveDirection = Vector3.ClampMagnitude(transform.right * inputX + transform.forward * inputZ, 1f);
            bool moving = moveDirection.sqrMagnitude > 0.1f;

            if (Input.GetKeyDown(KeyCode.Z))
                proneToggle = !proneToggle;
            if (Input.GetButtonDown("Jump"))
            {
                proneToggle = false;
                padCrouch = false;
            }

            // --- Controller sprint latch + B crouch/prone/slide ---
            bool padSlide = false;
            if (Input.GetKeyDown(KeyCode.LeftShift))
                padCrouch = false;
            if (Input.GetKeyDown(GamepadInput.LeftStick))
            {
                padSprint = true; // also stands up, CoD style
                padCrouch = false;
                proneToggle = false;
            }
            if (inputZ <= 0.1f || GamepadInput.AimHeld || GamepadInput.FireHeld)
                padSprint = false; // stopping, aiming or firing ends the sprint
            if (Input.GetKeyDown(GamepadInput.B))
            {
                padBHeldSince = Time.time;
                padBUsed = false;
                if (padSprint && grounded && slideTimer <= 0f && moving)
                {
                    padSlide = true;
                    padBUsed = true;
                }
            }
            if (!padBUsed && Input.GetKey(GamepadInput.B) && Time.time - padBHeldSince >= ProneHoldTime)
            {
                padBUsed = true;
                proneToggle = !proneToggle;
                padCrouch = !proneToggle; // getting up from prone stops at crouch
            }
            if (!padBUsed && Input.GetKeyUp(GamepadInput.B))
            {
                padBUsed = true;
                if (proneToggle)
                {
                    proneToggle = false;
                    padCrouch = true;
                }
                else
                    padCrouch = !padCrouch;
            }

            IsProne = proneToggle;
            bool wantCrouch = !IsProne && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C)
                || padCrouch);
            bool sprintHeld = Input.GetKey(KeyCode.LeftShift) || padSprint;
            IsSprinting = sprintHeld && inputZ > 0.1f
                && moving && !wantCrouch && !IsProne && !IsSliding;

            // Sprinting is a committed forward movement. Ignore strafe input while
            // sprinting so diagonal input cannot receive sprint speed, and expose
            // the direction actually travelled to animation/networking.
            moveInput = IsSprinting
                ? new Vector2(0f, inputZ)
                : new Vector2(inputX, inputZ);

            // --- Slide: press crouch while sprinting on the ground ---
            if (grounded && slideTimer <= 0f
                && sprintHeld && inputZ > 0.1f && moving
                && (Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.C) || padSlide))
            {
                slideTimer = slideDuration;
                slideDirection = moveDirection.normalized;
                if (padSlide)
                {
                    // CoD style: a controller slide ends crouched.
                    padSprint = false;
                    padCrouch = true;
                }
            }
            if (slideTimer > 0f)
            {
                slideTimer -= Time.deltaTime;
                if (!grounded)
                    slideTimer = 0f; // sliding off a ledge ends the slide
            }
            SlideBlend = Mathf.Clamp01(slideTimer / (slideDuration * 0.5f));

            IsCrouching = wantCrouch || IsSliding || IsProne;

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
                speed = IsProne ? proneSpeed : (IsCrouching ? crouchSpeed : (IsSprinting ? sprintSpeed : walkSpeed));
                speed *= speedMultiplier;
                horizontal = IsSprinting
                    ? transform.forward * speed
                    : moveDirection * speed;
            }
            horizontalVelocity = horizontal;

            // --- Crouch dimensions (controller + camera + hitbox pose) ---
            CrouchBlend = Mathf.MoveTowards(CrouchBlend, IsCrouching ? 1f : 0f, 8f * Time.deltaTime);
            ProneBlend = Mathf.MoveTowards(ProneBlend, IsProne ? 1f : 0f, 3.5f * Time.deltaTime);
            ApplyControllerHeight(controller, CrouchBlend, ProneBlend);
            if (cameraTransform != null)
            {
                var camPos = cameraTransform.localPosition;
                camPos.y = EyeHeight(CrouchBlend, ProneBlend);
                camPos.z = proneEyeForward * ProneBlend;
                cameraTransform.localPosition = camPos;
            }
            if (useProceduralBodyCrouch)
                ApplyCrouchVisual(bodyVisual, CrouchBlend, ProneBlend);

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

        /// <summary>Capsule height for a crouch/prone blend (remote players use it too: the capsule is the torso hitbox).</summary>
        public float HeightFor(float crouchBlend, float proneBlend) =>
            Mathf.Lerp(Mathf.Lerp(standHeight, crouchHeight, crouchBlend), proneHeight, proneBlend);

        /// <summary>Eye height for a crouch/prone blend.</summary>
        public float EyeHeight(float crouchBlend, float proneBlend) =>
            Mathf.Lerp(Mathf.Lerp(standEyeHeight, crouchEyeHeight, crouchBlend), proneEyeHeight, proneBlend);

        public void ApplyControllerHeight(CharacterController cc, float crouchBlend, float proneBlend)
        {
            if (cc == null)
                return;
            float height = HeightFor(crouchBlend, proneBlend);
            cc.height = height;
            cc.center = new Vector3(0, height / 2f, 0);
        }

        // Prone hitbox pose: the Body tips forward around the feet and slides back
        // so the hips sit over the capsule — head ~0.75m in front, feet ~0.9m
        // behind, chest just off the ground, like the crawl animation.
        private const float ProneTilt = 80f;
        private static readonly Vector3 ProneBodyOffset = new Vector3(0f, 0.1f, -0.93f);

        /// <summary>
        /// Pose the Body (primitive meshes + every limb/head hitbox) for crouch
        /// and prone. Used for the local player and for remote players, so the
        /// hitboxes everyone shoots at match the pose they see.
        /// </summary>
        public static void ApplyCrouchVisual(Transform body, float crouchBlend, float proneBlend = 0f)
        {
            if (body != null)
            {
                // Crouch squashes; prone lies the full-length body down instead.
                float squash = Mathf.Lerp(Mathf.Lerp(1f, 0.68f, crouchBlend), 1f, proneBlend);
                body.localScale = new Vector3(1f, squash, 1f);
                body.localRotation = Quaternion.Euler(ProneTilt * proneBlend, 0f, 0f);
                body.localPosition = ProneBodyOffset * proneBlend;
            }
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
