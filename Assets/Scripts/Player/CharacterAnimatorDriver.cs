using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Drives an imported humanoid character's Animator from how fast it is
    /// actually moving (measured from world-position deltas, so it works for the
    /// local player, network-replicated players and dummies alike — no input or
    /// network wiring needed). Feeds the StarterAssets-style locomotion
    /// parameters (Speed / Grounded / MotionSpeed) used by the shared
    /// SurvivalistLocomotion controller.
    /// </summary>
    public class CharacterAnimatorDriver : MonoBehaviour
    {
        public Animator animator;

        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int GroundedHash = Animator.StringToHash("Grounded");
        private static readonly int MotionSpeedHash = Animator.StringToHash("MotionSpeed");

        private Vector3 lastPosition;
        private float smoothedSpeed;

        private void Start()
        {
            lastPosition = transform.position;
        }

        private void Update()
        {
            if (animator == null || Time.deltaTime <= 0f)
                return;

            Vector3 delta = transform.position - lastPosition;
            lastPosition = transform.position;
            delta.y = 0f;
            float speed = delta.magnitude / Time.deltaTime;

            // Light smoothing so brief hitches don't pop the blend tree.
            smoothedSpeed = Mathf.Lerp(smoothedSpeed, speed, 12f * Time.deltaTime);

            animator.SetFloat(SpeedHash, smoothedSpeed);
            animator.SetFloat(MotionSpeedHash, 1f);
            animator.SetBool(GroundedHash, true);
        }
    }
}
