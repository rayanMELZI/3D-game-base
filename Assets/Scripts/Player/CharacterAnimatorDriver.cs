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
        public bool useNetworkState;

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
        private CharacterController characterController;
        
        [SerializeField] private float networkBlendSpeed = 10f;

        private Vector2 targetNetworkMoveInput;
        private float targetNetworkSpeed;

        private bool networkGrounded;
        private bool networkCrouching;
        private bool networkSprinting;
        private bool networkSliding;
        private bool networkReloading;

        private void Start()
        {
            if (playerRoot == null)
                playerRoot = transform.parent;

            lastPosition = playerRoot != null
                ? playerRoot.position
                : transform.position;

            if (movement != null)
                characterController = movement.GetComponent<CharacterController>();
        }

        public void SetNetworkAnimationState(
            Vector2 moveInput,
            float speed,
            bool grounded,
            bool crouching,
            bool sprinting,
            bool sliding,
            bool reloading)
        {
            targetNetworkMoveInput = moveInput;
            targetNetworkSpeed = speed;

            networkGrounded = grounded;
            networkCrouching = crouching;
            networkSprinting = sprinting;
            networkSliding = sliding;
            networkReloading = reloading;
        }
        private void Update()
        {
            if (animator == null || Time.deltaTime <= 0f)
                return;

            if (useNetworkState)
            {
                float blend = 1f - Mathf.Exp(-networkBlendSpeed * Time.deltaTime);

                smoothedDirection = Vector2.Lerp(
                    smoothedDirection,
                    targetNetworkMoveInput,
                    blend
                );

                smoothedSpeed = Mathf.Lerp(
                    smoothedSpeed,
                    targetNetworkSpeed,
                    blend
                );

                animator.SetFloat(MoveXHash, smoothedDirection.x);
                animator.SetFloat(MoveYHash, smoothedDirection.y);
                animator.SetFloat(SpeedHash, smoothedSpeed);
                animator.SetFloat(MotionSpeedHash, 1f);

                animator.SetBool(GroundedHash, networkGrounded);
                animator.SetBool(CrouchingHash, networkCrouching);
                animator.SetBool(SprintingHash, networkSprinting);
                animator.SetBool(SlidingHash, networkSliding);
                animator.SetBool(ReloadingHash, networkReloading);

                return;
            }


            Transform root = playerRoot != null ? playerRoot : transform;

            Vector3 currentPosition = root.position;
            Vector3 worldVelocity =
                (currentPosition - lastPosition) / Time.deltaTime;

            lastPosition = currentPosition;
            worldVelocity.y = 0f;

            float targetSpeed = worldVelocity.magnitude;

            float speedResponse = targetSpeed > smoothedSpeed ? 40f : 25f;
            smoothedSpeed = Mathf.MoveTowards(
                smoothedSpeed, targetSpeed, speedResponse * Time.deltaTime);
            Vector3 localVelocity = root.InverseTransformDirection(worldVelocity);

            Vector2 targetDirection = targetSpeed > 0.05f
                ? new Vector2(
                    localVelocity.x / Mathf.Max(targetSpeed, 0.01f),
                    localVelocity.z / Mathf.Max(targetSpeed, 0.01f)
                )
                : Vector2.zero;

            float directionResponse = targetSpeed > 0.05f ? 30f : 20f;
            smoothedDirection = Vector2.MoveTowards(
                smoothedDirection, targetDirection, directionResponse * Time.deltaTime);
            
            float blend2 = 1f - Mathf.Exp(-networkBlendSpeed * Time.deltaTime);

            smoothedDirection = Vector2.Lerp(
                smoothedDirection,
                targetNetworkMoveInput,
                blend2
            );
    
            smoothedSpeed = Mathf.Lerp(
                smoothedSpeed,
                targetNetworkSpeed,
                blend2
            );
            animator.SetFloat(
                SpeedHash,
                smoothedSpeed,
                0.12f,
                Time.deltaTime
            );

            animator.SetFloat(
                MoveXHash,
                smoothedDirection.x,
                0.12f,
                Time.deltaTime
            );

            animator.SetFloat(
                MoveYHash,
                smoothedDirection.y,
                0.12f,
                Time.deltaTime
            );
            animator.SetFloat(MotionSpeedHash, 1f);

            if (movement != null)
            {
                animator.SetBool(
                    GroundedHash,
                    characterController != null && characterController.isGrounded
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
