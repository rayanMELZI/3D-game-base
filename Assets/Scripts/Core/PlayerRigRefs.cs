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
        /// First person: the local player's whole body becomes shadows-only —
        /// it never clips into the camera, but the full shadow (with limbs and
        /// weapon) stays on the ground. Other players still see the body.
        /// </summary>
        public void SetFirstPerson(bool firstPerson)
        {
            foreach (var r in allRenderers)
                r.shadowCastingMode = firstPerson ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
        }

        /// <summary>Show/hide the whole body (used while dead in multiplayer).</summary>
        public void SetVisible(bool visible)
        {
            foreach (var r in allRenderers)
                r.enabled = visible;
        }
    }
}
