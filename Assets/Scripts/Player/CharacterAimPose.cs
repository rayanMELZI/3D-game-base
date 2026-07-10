using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Makes the imported character actually hold its weapon and aim it.
    /// Lives on the skin instance (same object as the Animator, required for
    /// OnAnimatorIK). Every frame it:
    ///  - pitches the third-person weapon anchor around the shoulder so the gun
    ///    follows where the player is looking vertically,
    ///  - IKs both hands onto the gun (grip + foregrip) and turns the head/chest
    ///    toward the aim point, replacing the "carrying nothing" stance,
    ///  - leans the whole body back while sliding.
    /// Pitch/slide come from the local rig for the owner (and offline), or are
    /// fed by NetworkPlayer from replicated values for remote players.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class CharacterAimPose : MonoBehaviour
    {
        [Tooltip("Wired by PlayerRigRefs — used to self-source pitch/slide for the local player.")]
        public PlayerRigRefs rig;
        public Transform weaponAnchor;

        /// <summary>Set true for remote players; pitch/slide then come from the fields below.</summary>
        [System.NonSerialized] public bool remoteDriven;
        [System.NonSerialized] public float remotePitch;
        [System.NonSerialized] public float remoteSlide;

        private Animator animator;
        private Vector3 anchorBasePos;
        private Quaternion baseLocalRot; // skin's authored local rotation (yaw offset)
        private float pitch;      // smoothed, degrees (up = negative)
        private float slideBlend; // 0..1

        // The anchor arcs around this local point (shoulder height) with pitch.
        private static readonly Vector3 ShoulderPivot = new Vector3(0f, 1.38f, 0f);

        private void Awake()
        {
            animator = GetComponent<Animator>();
        }

        private void Start()
        {
            if (weaponAnchor != null)
                anchorBasePos = weaponAnchor.localPosition;
            baseLocalRot = transform.localRotation;
        }

        private void Update()
        {
            // Source the pose inputs.
            float targetPitch, targetSlide;
            if (remoteDriven)
            {
                targetPitch = remotePitch;
                targetSlide = remoteSlide;
            }
            else
            {
                targetPitch = rig != null && rig.mouseLook != null ? rig.mouseLook.CurrentPitch : 0f;
                targetSlide = rig != null && rig.movement != null ? rig.movement.SlideBlend : 0f;
            }
            pitch = Mathf.Lerp(pitch, targetPitch, 14f * Time.deltaTime);
            slideBlend = Mathf.MoveTowards(slideBlend, targetSlide, 6f * Time.deltaTime);

            // Weapon anchor follows aim pitch, arcing around the shoulder.
            // (Done in Update so the IK pass below sees the final gun position.)
            if (weaponAnchor != null)
            {
                var rot = Quaternion.Euler(pitch, 0f, 0f);
                weaponAnchor.localRotation = rot;
                weaponAnchor.localPosition = ShoulderPivot + rot * (anchorBasePos - ShoulderPivot);
            }
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (animator == null)
                return;

            // Head + upper body track the aim point.
            Vector3 aimDir = Quaternion.AngleAxis(pitch, transform.right) * transform.forward;
            Vector3 eye = transform.position + Vector3.up * 1.55f;
            animator.SetLookAtPosition(eye + aimDir * 10f);
            animator.SetLookAtWeight(1f, 0.35f, 0.85f, 0f, 0.55f);

            if (weaponAnchor == null)
                return;

            // Hands onto the gun: right on the grip, left on the foregrip.
            Vector3 grip = weaponAnchor.TransformPoint(new Vector3(0f, -0.06f, -0.08f));
            Vector3 foregrip = weaponAnchor.TransformPoint(new Vector3(-0.02f, -0.05f, 0.22f));
            Quaternion handRot = weaponAnchor.rotation;

            animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 1f);
            animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 1f);
            animator.SetIKPosition(AvatarIKGoal.RightHand, grip);
            animator.SetIKRotation(AvatarIKGoal.RightHand, handRot);

            animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 1f);
            animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 1f);
            animator.SetIKPosition(AvatarIKGoal.LeftHand, foregrip);
            animator.SetIKRotation(AvatarIKGoal.LeftHand, handRot * Quaternion.Euler(0f, 0f, 80f));
        }

        private void LateUpdate()
        {
            // Slide: lean the whole skin back (after Animator + IK so it sticks),
            // composed with the skin's authored yaw offset.
            transform.localRotation = baseLocalRot * Quaternion.Euler(-48f * slideBlend, 0f, 0f);
        }
    }
}
