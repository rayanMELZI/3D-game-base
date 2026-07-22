using UnityEngine;

namespace FpsBase
{
    /// <summary>Development utility for measuring and visually calibrating locomotion clips.</summary>
    public class LocomotionClipSpeedCalibrator : MonoBehaviour
    {
        public enum CalibrationMode
        {
            RootMotionMeasurement,
            ManualInPlaceCalibration
        }

        [Header("Calibration")]
        [Tooltip("Root Motion Measurement reports translation embedded in a clip. Manual In Place Calibration moves this calibration object so planted feet can be matched visually.")]
        [SerializeField] private CalibrationMode mode;
        [Tooltip("Animator playing the single locomotion clip being calibrated at playback speed 1.")]
        [SerializeField] private Animator animator;
        [Tooltip("Optional controller used only to move the calibration object in Manual In Place Calibration mode.")]
        [SerializeField] private CharacterController characterController;

        [Header("Manual In-Place Calibration")]
        [Tooltip("Horizontal local travel direction. +Z forward, -Z backward, +X right, and -X left.")]
        [SerializeField] private Vector3 manualLocalDirection = Vector3.forward;
        [Tooltip("Current test speed in metres per second. Adjust until a planted foot no longer slides against the floor.")]
        [SerializeField, Min(0f)] private float manualTestSpeed = 2f;

        private int measuredStateHash;
        private int observedLoop;
        private bool initialized;
        private bool firstCycleComplete;
        private float cycleDuration;
        private float cycleHorizontalDistance;
        private Vector3 cycleLocalDisplacement;
        private float previousAnimatorSpeed;

        private void Reset()
        {
            animator = GetComponent<Animator>();
            characterController = GetComponent<CharacterController>();
        }

        private void OnEnable()
        {
            if (animator == null)
                animator = GetComponent<Animator>();

            if (animator != null)
            {
                previousAnimatorSpeed = animator.speed;
                animator.speed = 1f;
            }

            ResetMeasurement();
        }

        private void OnDisable()
        {
            if (animator != null)
                animator.speed = previousAnimatorSpeed;
        }

        private void Start()
        {
            if (animator == null)
                Debug.LogWarning("Locomotion calibration requires an Animator reference.", this);
            else if (mode == CalibrationMode.RootMotionMeasurement && !animator.applyRootMotion)
                Debug.LogWarning("Root-motion measurement is selected, but Animator.applyRootMotion is disabled. Enable it temporarily on the calibration object; this utility never moves the object from deltaPosition.", this);
        }

        private void Update()
        {
            if (mode != CalibrationMode.ManualInPlaceCalibration || animator == null)
                return;

            Vector3 direction = new Vector3(manualLocalDirection.x, 0f, manualLocalDirection.z);
            if (direction.sqrMagnitude <= Mathf.Epsilon)
                return;

            Vector3 worldDirection = transform.TransformDirection(direction.normalized);
            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude <= Mathf.Epsilon)
                return;
            Vector3 worldMovement = worldDirection.normalized
                * manualTestSpeed * Time.deltaTime;

            if (characterController != null && characterController.enabled)
                characterController.Move(worldMovement);
            else
                transform.position += worldMovement;
        }

        private void OnAnimatorMove()
        {
            if (mode != CalibrationMode.RootMotionMeasurement || animator == null)
                return;

            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            int stateHash = state.fullPathHash;
            int currentLoop = Mathf.FloorToInt(state.normalizedTime);

            if (!initialized || stateHash != measuredStateHash || currentLoop < observedLoop)
            {
                measuredStateHash = stateHash;
                observedLoop = currentLoop;
                initialized = true;
                firstCycleComplete = false;
                ClearCycle();
                return;
            }

            if (currentLoop != observedLoop)
            {
                if (firstCycleComplete)
                    LogCycleResult();
                else
                    firstCycleComplete = true;

                ClearCycle();
                observedLoop = currentLoop;
            }

            if (!firstCycleComplete)
                return;

            Vector3 horizontalDelta = animator.deltaPosition;
            horizontalDelta.y = 0f;
            cycleHorizontalDistance += horizontalDelta.magnitude;
            cycleLocalDisplacement += transform.InverseTransformVector(horizontalDelta);
            cycleDuration += Time.deltaTime;
        }

        [ContextMenu("Reset Calibration Measurement")]
        public void ResetMeasurement()
        {
            initialized = false;
            firstCycleComplete = false;
            measuredStateHash = 0;
            observedLoop = 0;
            ClearCycle();
        }

        private void ClearCycle()
        {
            cycleDuration = 0f;
            cycleHorizontalDistance = 0f;
            cycleLocalDisplacement = Vector3.zero;
        }

        private void LogCycleResult()
        {
            if (cycleDuration <= Mathf.Epsilon)
                return;

            float averageSpeed = cycleHorizontalDistance / cycleDuration;
            float averageLocalX = cycleLocalDisplacement.x / cycleDuration;
            float averageLocalZ = cycleLocalDisplacement.z / cycleDuration;

            Debug.Log(
                "LOCOMOTION CLIP CALIBRATION\n"
                + $"Cycle Duration: {cycleDuration:F4} s\n"
                + $"Horizontal Distance: {cycleHorizontalDistance:F4} m\n"
                + $"Average Horizontal Speed: {averageSpeed:F4} m/s\n"
                + $"Average Local X Velocity: {averageLocalX:F4} m/s\n"
                + $"Average Local Z Velocity: {averageLocalZ:F4} m/s",
                this);
        }
    }
}
