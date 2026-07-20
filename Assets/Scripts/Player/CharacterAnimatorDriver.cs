using UnityEngine;

namespace FpsBase
{
    public class CharacterAnimatorDriver : MonoBehaviour
    {
        [Header("References")]
        public Animator animator;
        public PlayerMovement movement;
        public WeaponController weaponController;
        public Transform playerRoot;

        private static readonly int SpeedHash =
            Animator.StringToHash("Speed");

        private static readonly int MoveXHash =
            Animator.StringToHash("MoveX");

        private static readonly int MoveYHash =
            Animator.StringToHash("MoveY");

        private static readonly int GroundedHash =
            Animator.StringToHash("Grounded");

        private static readonly int MotionSpeedHash =
            Animator.StringToHash("MotionSpeed");

        private static readonly int CrouchingHash =
            Animator.StringToHash("Crouching");

        private static readonly int SprintingHash =
            Animator.StringToHash("Sprinting");

        private static readonly int SlidingHash =
            Animator.StringToHash("Sliding");

        private static readonly int ReloadingHash =
            Animator.StringToHash("Reloading");

        private Vector3 lastPosition;
        private float smoothedSpeed;
        private Vector2 smoothedDirection;

        private void Start()
        {
            if (playerRoot == null)
                playerRoot = transform.parent;

            lastPosition = playerRoot != null
                ? playerRoot.position
                : transform.position;
        }
        public void SetNetworkAnimationState(
            Vector2 moveInput,
            float speed,
            bool grounded,
            bool crouching,
            bool sprinting,
            bool sliding,
            bool reloading
        )
        {
            animator.SetFloat(MoveXHash, moveInput.x);
            animator.SetFloat(MoveYHash, moveInput.y);
            animator.SetFloat(SpeedHash, speed);

            animator.SetBool(GroundedHash, grounded);
            animator.SetBool(CrouchingHash, crouching);
            animator.SetBool(SprintingHash, sprinting);
            animator.SetBool(SlidingHash, sliding);
            animator.SetBool(ReloadingHash, reloading);
        }
        private void Update()
        {
            if (animator == null || Time.deltaTime <= 0f)
                return;

            Transform root = playerRoot != null ? playerRoot : transform;

            Vector3 currentPosition = root.position;
            Vector3 worldVelocity =
                (currentPosition - lastPosition) / Time.deltaTime;

            lastPosition = currentPosition;
            worldVelocity.y = 0f;

            float targetSpeed = worldVelocity.magnitude;

            // smoothedSpeed = Mathf.Lerp(
            //     smoothedSpeed,
            //     targetSpeed,
            //     12f * Time.deltaTime
            // );
            // smoothedSpeed = targetSpeed;
            float smoothing = targetSpeed > smoothedSpeed ? 30f : 18f;

            smoothedSpeed = Mathf.Lerp(
                smoothedSpeed,
                targetSpeed,
                smoothing * Time.deltaTime
            );
            Vector3 localVelocity = root.InverseTransformDirection(worldVelocity);

            Vector2 targetDirection = targetSpeed > 0.05f
                ? new Vector2(
                    localVelocity.x / Mathf.Max(targetSpeed, 0.01f),
                    localVelocity.z / Mathf.Max(targetSpeed, 0.01f)
                )
                : Vector2.zero;

            // smoothedDirection = Vector2.Lerp(
            //     smoothedDirection,
            //     targetDirection,
            //     12f * Time.deltaTime
            // );
            smoothedDirection = targetDirection;

            
            animator.SetFloat(SpeedHash, smoothedSpeed);
            animator.SetFloat(MoveXHash, smoothedDirection.x);
            animator.SetFloat(MoveYHash, smoothedDirection.y);
            animator.SetFloat(MotionSpeedHash, 1f);

            if (movement != null)
            {
                CharacterController controller =
                    movement.GetComponent<CharacterController>();

                animator.SetBool(
                    GroundedHash,
                    controller != null && controller.isGrounded
                );

                animator.SetBool(CrouchingHash, movement.IsCrouching);
                animator.SetBool(SprintingHash, movement.IsSprinting);
                animator.SetBool(SlidingHash, movement.IsSliding);
            }

            if (weaponController != null)
            {
                animator.SetBool(
                    ReloadingHash,
                    weaponController.IsReloading
                );
            }
        }
    }
}