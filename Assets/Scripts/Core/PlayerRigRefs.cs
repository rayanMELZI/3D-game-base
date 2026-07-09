using UnityEngine;
using UnityEngine.Rendering;

namespace FpsBase
{
    /// <summary>
    /// Holds references to the pieces of a player rig built by PlayerFactory.
    /// Lives on the player root (and inside the multiplayer player prefab) so
    /// both single-player code and network code can find everything.
    /// Also applies the rig's materials at runtime (materials can't be baked
    /// into the prefab because they're generated from code).
    /// </summary>
    public class PlayerRigRefs : MonoBehaviour
    {
        [Header("Wired by PlayerFactory")]
        public GameObject cameraObject;
        public Camera playerCamera;
        public MouseLook mouseLook;
        public PlayerMovement movement;
        public WeaponController weaponController;
        public Transform thirdPersonWeaponAnchor;

        [Header("Humanoid body (wired by PlayerFactory)")]
        public Transform bodyRoot;
        public Renderer[] teamColorRenderers;
        public Renderer[] darkTeamRenderers;
        public Renderer headRenderer;
        public Renderer visorRenderer;
        public Renderer chestStripeRenderer;
        [Tooltip("Head parts hidden in first person so they don't block the view.")]
        public Renderer[] headRenderers;
        [Tooltip("Every visual renderer (hidden while dead in multiplayer).")]
        public Renderer[] allRenderers;

        [Header("Imported character skin")]
        [Tooltip("Swap the primitive body for an imported Survivalist skin at runtime (visuals only; hitboxes are unchanged).")]
        public bool useImportedCharacter = true;
        [Tooltip("Uniform scale of the imported character under the body root.")]
        public float characterScale = 1f;
        [Tooltip("Local offset of the imported character under the body root.")]
        public Vector3 characterOffset = Vector3.zero;
        [Tooltip("Extra yaw if the imported character faces the wrong way (degrees).")]
        public float characterYaw = 0f;

        private bool skinApplied;
        // Once an imported skin is applied, visibility/first-person target these
        // (the visible mesh) instead of the now-hidden primitive renderers.
        private Renderer[] characterRenderers;

        private bool initialized;

        // Prefab path: fields are already serialized when Awake runs.
        // Factory path: fields are wired after AddComponent, so PlayerFactory
        // calls RuntimeInit() again once wiring is done.
        private void Awake() => RuntimeInit();

        public void RuntimeInit()
        {
            if (initialized || !Application.isPlaying
                || teamColorRenderers == null || teamColorRenderers.Length == 0)
                return;
            initialized = true;

            // Generated materials are applied at runtime (default team: blue).
            HumanoidBuilder.ApplyMaterials(
                teamColorRenderers, darkTeamRenderers, headRenderer, visorRenderer,
                chestStripeRenderer, allRenderers, EnvironmentBuilder.Team0Color);

            PostFx.Attach(playerCamera);
        }

        /// <summary>Tint the body with a team color (multiplayer).</summary>
        public void ApplyTeamColor(Color color)
        {
            HumanoidBuilder.ApplyTeamColor(teamColorRenderers, darkTeamRenderers, chestStripeRenderer, color);
        }

        /// <summary>
        /// Swap the primitive body for one of the imported Survivalist skins,
        /// chosen by player index (1..4, wrapping: index 4 → skin 1). The primitive
        /// meshes are only hidden — their colliders stay, so every existing hitbox
        /// (head trigger included) is unchanged. Safe to call once per player;
        /// falls back to the primitive body if the assets aren't present.
        /// </summary>
        public void ApplyCharacterSkin(int index)
        {
            if (!useImportedCharacter || skinApplied || !Application.isPlaying || bodyRoot == null)
                return;

            int n = ((index % 4) + 4) % 4 + 1; // 0→1, 1→2, 2→3, 3→4, 4→1 …
            var prefab = Resources.Load<GameObject>($"Characters/Survivalist_{n}");
            if (prefab == null)
                return; // assets missing — keep the primitive body

            skinApplied = true;

            var instance = Instantiate(prefab, bodyRoot);
            instance.name = "CharacterSkin";
            instance.transform.localPosition = characterOffset;
            instance.transform.localRotation = Quaternion.Euler(0f, characterYaw, 0f);
            instance.transform.localScale = Vector3.one * characterScale;

            var animator = instance.GetComponent<Animator>();
            if (animator != null)
            {
                var ctrl = Resources.Load<RuntimeAnimatorController>("Characters/SurvivalistLocomotion");
                if (ctrl != null)
                    animator.runtimeAnimatorController = ctrl;
                animator.applyRootMotion = false; // movement is driven by the controller, not the clip
                instance.AddComponent<CharacterAnimatorDriver>().animator = animator;
            }

            // Hide the primitive body meshes (their hitbox colliders remain).
            if (allRenderers != null)
                foreach (var r in allRenderers)
                    if (r != null) r.enabled = false;

            characterRenderers = instance.GetComponentsInChildren<Renderer>(true);

            // The procedural limb swing now drives hidden primitive pivots — stop it.
            var limb = GetComponent<LimbAnimator>();
            if (limb != null) limb.enabled = false;

            // Re-assert first-person shadows-only if it was already set on the primitives.
            if (firstPersonActive)
                SetFirstPerson(true);
        }

        /// <summary>
        /// First person: the local player's whole body becomes shadows-only —
        /// it never clips into the camera, but the full shadow (with limbs and
        /// weapon) stays on the ground. Other players still see the body.
        /// </summary>
        public void SetFirstPerson(bool firstPerson)
        {
            firstPersonActive = firstPerson;
            foreach (var r in VisibleRenderers)
                if (r != null)
                    r.shadowCastingMode = firstPerson ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
        }

        /// <summary>Show/hide the whole body (used while dead in multiplayer).</summary>
        public void SetVisible(bool visible)
        {
            foreach (var r in VisibleRenderers)
                if (r != null)
                    r.enabled = visible;
        }

        private bool firstPersonActive;

        /// <summary>The renderers that actually show the body — the imported skin once swapped in, else the primitives.</summary>
        private Renderer[] VisibleRenderers => characterRenderers ?? allRenderers;
    }
}
