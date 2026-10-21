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
    ///  - leans the whole body back while sliding,
    ///  - prone: drops the aim pivot to the lying shoulders (pitch limited so
    ///    the gun stays off the ground); while crawling the hands follow the
    ///    crawl clip and the gun rides in the right hand, like during a reload.
    /// Pitch/slide/prone come from the local rig for the owner (and offline), or
    /// are fed by NetworkPlayer from replicated values for remote players.
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
        [System.NonSerialized] public bool remoteReloading;
        [System.NonSerialized] public bool remoteProne;
        [System.NonSerialized] public bool remoteMoving;

        [Header("Animation Compatibility")]
        [Tooltip("Enable only for characters that still need the legacy procedural slide lean.")]
        [SerializeField] private bool useProceduralSlideLean = false;

        private Animator animator;
        private Vector3 anchorBasePos;
        private Quaternion baseLocalRot; // skin's authored local rotation (yaw offset)
        private float pitch;      // smoothed, degrees (up = negative)
        private float slideBlend; // 0..1
        private float handIkWeight = 1f;
        private float lookAtWeight = 1f;
        private float reloadWeaponFollow;
        private float proneBlend; // 0..1
        private Transform rightHandBone;
        private WeaponPosePoints weaponPose;

        // The anchor arcs around this local point (shoulder height) with pitch.
        private static readonly Vector3 ShoulderPivot = new Vector3(0f, 1.38f, 0f);
        // Prone: shoulders just off the ground, ahead of the player origin
        // (matches the lying hitbox pose in PlayerMovement.ApplyCrouchVisual).
        private static readonly Vector3 ProneShoulderPivot = new Vector3(0f, 0.34f, 0.5f);
        private const float PronePitchLimit = 30f;
        private float reloadPoseBlendSpeed = 10f;

        private void Awake()
        {
            animator = GetComponent<Animator>();
        }

        private void Start()
        {
            if (weaponAnchor != null)
                anchorBasePos = weaponAnchor.localPosition;
            baseLocalRot = transform.localRotation;
            if (animator != null && animator.isHuman)
                rightHandBone = animator.GetBoneTransform(HumanBodyBones.RightHand);
        }

        private void Update()
        {
            // Source the pose inputs.
            float targetPitch, targetSlide;
            bool prone, moving;
            if (remoteDriven)
            {
                targetPitch = remotePitch;
                targetSlide = remoteSlide;
                prone = remoteProne;
                moving = remoteMoving;
            }
            else
            {
                targetPitch = rig != null && rig.mouseLook != null ? rig.mouseLook.CurrentPitch : 0f;
                targetSlide = rig != null && rig.movement != null ? rig.movement.SlideBlend : 0f;
                prone = rig != null && rig.movement != null && rig.movement.IsProne;
                moving = rig != null && rig.movement != null && rig.movement.CurrentHorizontalSpeed > 0.1f;
            }
            proneBlend = Mathf.MoveTowards(proneBlend, prone ? 1f : 0f, 3.5f * Time.deltaTime);
            float pitchLimit = Mathf.Lerp(90f, PronePitchLimit, proneBlend);
            targetPitch = Mathf.Clamp(targetPitch, -pitchLimit, pitchLimit);
            pitch = Mathf.Lerp(pitch, targetPitch, 14f * Time.deltaTime);
            slideBlend = Mathf.MoveTowards(slideBlend, targetSlide, 6f * Time.deltaTime);

            bool reloading = remoteDriven
                ? remoteReloading
                : rig != null
                    && rig.weaponController != null
                    && rig.weaponController.IsReloading;
            // Crawling needs the arms: let the clip have them and carry the gun
            // in the right hand, exactly like the reload path.
            bool crawling = prone && moving;
            bool handsBusy = reloading || crawling;
            float targetPoseWeight = handsBusy ? 0f : 1f;
            handIkWeight = Mathf.MoveTowards(
                handIkWeight,
                targetPoseWeight,
                reloadPoseBlendSpeed * Time.deltaTime);
            lookAtWeight = Mathf.MoveTowards(
                lookAtWeight,
                targetPoseWeight,
                reloadPoseBlendSpeed * Time.deltaTime);
            reloadWeaponFollow = Mathf.MoveTowards(
                reloadWeaponFollow,
                handsBusy ? 1f : 0f,
                reloadPoseBlendSpeed * Time.deltaTime);

            weaponPose = FindActiveWeaponPose();
            if (weaponPose != null)
                weaponPose.RestoreAimPose();

            // Weapon anchor follows aim pitch, arcing around the shoulder.
            // (Done in Update so the IK pass below sees the final gun position.)
            if (weaponAnchor != null)
            {
                var rot = Quaternion.Euler(pitch, 0f, 0f);
                Vector3 pivot = Vector3.Lerp(ShoulderPivot, ProneShoulderPivot, proneBlend);
                weaponAnchor.localRotation = rot;
                weaponAnchor.localPosition = pivot + rot * (anchorBasePos - ShoulderPivot);
            }
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (animator == null)
                return;

            // Head + upper body track the aim point.
            Vector3 aimDir = Quaternion.AngleAxis(pitch, transform.right) * transform.forward;
            Vector3 eye = transform.position
                + Vector3.up * Mathf.Lerp(1.55f, 0.42f, proneBlend)
                + transform.forward * Mathf.Lerp(0f, 0.6f, proneBlend);
            animator.SetLookAtPosition(eye + aimDir * 10f);
            // A lying spine barely twists toward the aim point; the head does the work.
            animator.SetLookAtWeight(lookAtWeight, Mathf.Lerp(0.35f, 0.05f, proneBlend), 0.85f, 0f, 0.55f);

            if (weaponAnchor == null)
                return;

            // Prefer authored per-weapon markers. Older weapons without markers
            // retain the generic placement until sockets are added to them.
            Transform rightGrip = weaponPose != null ? weaponPose.RightHandGrip : null;
            Transform leftGrip = weaponPose != null ? weaponPose.LeftHandGrip : null;
            Vector3 grip = rightGrip != null
                ? rightGrip.position
                : weaponAnchor.TransformPoint(new Vector3(0f, -0.06f, -0.08f));
            Vector3 foregrip = leftGrip != null
                ? leftGrip.position
                : weaponAnchor.TransformPoint(new Vector3(-0.02f, -0.05f, 0.22f));
            Quaternion rightHandRotation = rightGrip != null
                ? rightGrip.rotation
                : weaponAnchor.rotation;
            Quaternion leftHandRotation = leftGrip != null
                ? leftGrip.rotation
                : weaponAnchor.rotation * Quaternion.Euler(0f, 0f, 80f);

            animator.SetIKPositionWeight(AvatarIKGoal.RightHand, handIkWeight);
            animator.SetIKRotationWeight(AvatarIKGoal.RightHand, handIkWeight);
            animator.SetIKPosition(AvatarIKGoal.RightHand, grip);
            animator.SetIKRotation(AvatarIKGoal.RightHand, rightHandRotation);

            animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, handIkWeight);
            animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, handIkWeight);
            animator.SetIKPosition(AvatarIKGoal.LeftHand, foregrip);
            animator.SetIKRotation(AvatarIKGoal.LeftHand, leftHandRotation);
        }

        private void LateUpdate()
        {
            if (useProceduralSlideLean)
            {
                // Slide: lean the whole skin back (after Animator + IK so it sticks),
                // composed with the skin's authored yaw offset.
                transform.localRotation = baseLocalRot * Quaternion.Euler(-48f * slideBlend, 0f, 0f);
            }

            // IK is now out of the way and the reload clip has produced its final
            // hand pose. Move the gun onto that hand for remote players (and the
            // local shadows-only body gun) without touching the FPS viewmodel.
            if (rightHandBone == null && animator != null && animator.isHuman)
                rightHandBone = animator.GetBoneTransform(HumanBodyBones.RightHand);
            weaponPose = FindActiveWeaponPose();
            if (weaponPose != null && weaponPose.RightHandGrip != null)
                weaponPose.FollowRightHand(rightHandBone, reloadWeaponFollow);
        }

        private WeaponPosePoints FindActiveWeaponPose()
        {
            if (weaponAnchor == null)
                return null;

            var poses = weaponAnchor.GetComponentsInChildren<WeaponPosePoints>(false);
            foreach (var pose in poses)
                if (pose.gameObject.activeInHierarchy)
                    return pose;
            return null;
        }
    }
}
