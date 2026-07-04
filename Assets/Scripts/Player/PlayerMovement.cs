using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Classic FPS movement on a CharacterController:
    /// WASD to move, Left Shift to sprint, Space to jump.
    /// Uses the legacy Input Manager so it works out of the box in any Unity version.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMovement : MonoBehaviour
    {
        [Header("Speeds")]
        public float walkSpeed = 5f;
        public float sprintSpeed = 8.5f;

        [Header("Jumping / Gravity")]
        public float jumpHeight = 1.2f;
        public float gravity = -20f;

        [Header("Respawn")]
        public Vector3 spawnPoint;
        public float killY = -20f;

        private CharacterController controller;
        private float verticalVelocity;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (spawnPoint == Vector3.zero)
                spawnPoint = transform.position;
        }

        private void Update()
        {
            bool grounded = controller.isGrounded;

            // --- Horizontal movement (relative to where the player is facing) ---
            float inputX = Input.GetAxisRaw("Horizontal");
            float inputZ = Input.GetAxisRaw("Vertical");
            Vector3 moveDirection = (transform.right * inputX + transform.forward * inputZ).normalized;

            float speed = Input.GetKey(KeyCode.LeftShift) ? sprintSpeed : walkSpeed;

            // --- Vertical movement (jump + gravity) ---
            if (grounded && verticalVelocity < 0f)
                verticalVelocity = -2f; // small downward force keeps the controller glued to the ground

            if (grounded && Input.GetButtonDown("Jump"))
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

            verticalVelocity += gravity * Time.deltaTime;

            Vector3 velocity = moveDirection * speed + Vector3.up * verticalVelocity;
            controller.Move(velocity * Time.deltaTime);

            // --- Safety respawn if we somehow fall out of the world ---
            if (transform.position.y < killY)
                Respawn();
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
