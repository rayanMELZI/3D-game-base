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

        [Header("Authored Locomotion Speeds")]
        [SerializeField, Min(0.01f)] private float walkForwardAuthoredSpeed = 2f;
        [SerializeField, Min(0.01f)] private float walkBackwardAuthoredSpeed = 1.7f;
        [SerializeField, Min(0.01f)] private float strafeLeftAuthoredSpeed = 1.8f;
        [SerializeField, Min(0.01f)] private float strafeRightAuthoredSpeed = 1.8f;
        [SerializeField, Min(0.01f)] private float sprintForwardAuthoredSpeed = 5.5f;

        [Header("Motion Speed Matching")]
        [SerializeField, Min(0f)] private float motionSpeedDampTime = 0.08f;
        [SerializeField] private Vector2 motionSpeedLimits = new Vector2(0.65f, 1.5f);
        [SerializeField, Min(0f)] private float motionSpeedIdleThreshold = 0.05f;

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


        public void ConfigureAuthoredSpeeds(
            float walkForward,
            float walkBackward,
            float strafeLeft,
            float strafeRight,
            float sprintForward)
        {
            walkForwardAuthoredSpeed = walkForward;
            walkBackwardAuthoredSpeed = walkBackward;
            strafeLeftAuthoredSpeed = strafeLeft;
            strafeRightAuthoredSpeed = strafeRight;
            sprintForwardAuthoredSpeed = sprintForward;
        }


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
                float networkMotionSpeed = CalculateMotionSpeed(
                    targetNetworkSpeed,
                    targetNetworkMoveInput,
                    networkSprinting);
                animator.SetFloat(
                    MotionSpeedHash,
                    networkMotionSpeed,
                    motionSpeedDampTime,
                    Time.deltaTime);

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

            float targetSpeed = movement != null
                ? movement.CurrentHorizontalSpeed
                : worldVelocity.magnitude;

            float speedResponse = targetSpeed > smoothedSpeed ? 40f : 25f;
            smoothedSpeed = Mathf.MoveTowards(
                smoothedSpeed, targetSpeed, speedResponse * Time.deltaTime);
            Vector3 localVelocity = root.InverseTransformDirection(worldVelocity);

            Vector2 calculatedDirection = targetSpeed > 0.05f
                ? new Vector2(
                    localVelocity.x / Mathf.Max(targetSpeed, 0.01f),
                    localVelocity.z / Mathf.Max(targetSpeed, 0.01f)
                )
                : Vector2.zero;
            Vector2 targetDirection = movement != null
                ? movement.MoveInput
                : calculatedDirection;

            float directionResponse = targetSpeed > 0.05f ? 30f : 20f;
            smoothedDirection = Vector2.MoveTowards(
                smoothedDirection, targetDirection, directionResponse * Time.deltaTime);
            
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
            float localMotionSpeed = CalculateMotionSpeed(
                targetSpeed,
                targetDirection,
                movement != null && movement.IsSprinting);
            animator.SetFloat(
                MotionSpeedHash,
                localMotionSpeed,
                motionSpeedDampTime,
                Time.deltaTime);

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

        private float CalculateAuthoredReferenceSpeed(Vector2 moveInput, bool sprinting)
        {
            Vector2 input = moveInput.sqrMagnitude > 1f
                ? moveInput.normalized
                : moveInput;

            float forwardWeight = Mathf.Max(0f, input.y);
            float backwardWeight = Mathf.Max(0f, -input.y);
            float rightWeight = Mathf.Max(0f, input.x);
            float leftWeight = Mathf.Max(0f, -input.x);
            float totalWeight = forwardWeight + backwardWeight + rightWeight + leftWeight;

            if (totalWeight <= Mathf.Epsilon)
                return walkForwardAuthoredSpeed;

            float forwardSpeed = sprinting
                ? sprintForwardAuthoredSpeed
                : walkForwardAuthoredSpeed;

            return (
                forwardWeight * forwardSpeed
                + backwardWeight * walkBackwardAuthoredSpeed
                + rightWeight * strafeRightAuthoredSpeed
                + leftWeight * strafeLeftAuthoredSpeed) / totalWeight;
        }

        private float CalculateMotionSpeed(
            float actualHorizontalSpeed,
            Vector2 moveInput,
            bool sprinting)
        {
            if (actualHorizontalSpeed < motionSpeedIdleThreshold)
                return 1f;

            float referenceSpeed = CalculateAuthoredReferenceSpeed(moveInput, sprinting);
            if (referenceSpeed <= Mathf.Epsilon
                || float.IsNaN(referenceSpeed)
                || float.IsInfinity(referenceSpeed))
                return 1f;

            float multiplier = actualHorizontalSpeed / referenceSpeed;
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier))
                return 1f;

            float minimum = Mathf.Min(motionSpeedLimits.x, motionSpeedLimits.y);
            float maximum = Mathf.Max(motionSpeedLimits.x, motionSpeedLimits.y);
            return Mathf.Clamp(multiplier, minimum, maximum);
        }
    }
}
