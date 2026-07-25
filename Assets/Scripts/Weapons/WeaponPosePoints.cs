using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Runtime bridge between authored weapon markers and the third-person
    /// character pose. The weapon stays at its normal aim anchor while held,
    /// then this component aligns its RightHandGrip with the animated right
    /// hand during reloads so the gun follows the animation.
    /// </summary>
    public sealed class WeaponPosePoints : MonoBehaviour
    {
        public Transform RightHandGrip { get; private set; }
        public Transform LeftHandGrip { get; private set; }

        private Vector3 baseLocalPosition;
        private Quaternion baseLocalRotation;

        public void Configure(Transform rightHandGrip, Transform leftHandGrip)
        {
            RightHandGrip = rightHandGrip;
            LeftHandGrip = leftHandGrip;
            baseLocalPosition = transform.localPosition;
            baseLocalRotation = transform.localRotation;
        }

        /// <summary>Undo the previous frame's reload-follow adjustment.</summary>
        public void RestoreAimPose()
        {
            transform.localPosition = baseLocalPosition;
            transform.localRotation = baseLocalRotation;
        }

        /// <summary>
        /// Blend the weapon from its aimed pose to a pose where its authored
        /// RightHandGrip exactly matches the animated humanoid right hand.
        /// Call after Animator IK has evaluated for the frame.
        /// </summary>
        public void FollowRightHand(Transform hand, float weight)
        {
            RestoreAimPose();
            if (RightHandGrip == null || hand == null || weight <= 0f)
                return;

            Vector3 aimPosition = transform.position;
            Quaternion aimRotation = transform.rotation;

            // Marker rotation describes the desired hand orientation relative
            // to the gun. Rotate the gun so that marker and hand axes coincide.
            Quaternion gripRotationInRoot =
                Quaternion.Inverse(transform.rotation) * RightHandGrip.rotation;
            Quaternion followRotation = hand.rotation * Quaternion.Inverse(gripRotationInRoot);

            transform.rotation = followRotation;
            transform.position += hand.position - RightHandGrip.position;
            Vector3 followPosition = transform.position;

            transform.SetPositionAndRotation(
                Vector3.Lerp(aimPosition, followPosition, Mathf.Clamp01(weight)),
                Quaternion.Slerp(aimRotation, followRotation, Mathf.Clamp01(weight)));
        }
    }
}
