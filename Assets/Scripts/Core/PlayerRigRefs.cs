using UnityEngine;

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
        public Renderer[] teamColorRenderers;
        public Renderer headRenderer;
        public Renderer visorRenderer;
        public Renderer chestStripeRenderer;
        [Tooltip("Head parts hidden in first person so they don't block the view.")]
        public Renderer[] headRenderers;
        [Tooltip("Every visual renderer (hidden while dead in multiplayer).")]
        public Renderer[] allRenderers;

        private bool materialsApplied;

        // Prefab path: fields are already serialized when Awake runs.
        // Factory path: fields are wired after AddComponent, so PlayerFactory
        // calls TryApplyMaterials() again once wiring is done.
        private void Awake() => TryApplyMaterials();

        public void TryApplyMaterials()
        {
            if (materialsApplied || !Application.isPlaying
                || teamColorRenderers == null || teamColorRenderers.Length == 0)
                return;
            materialsApplied = true;

            // Generated materials are applied at runtime (default team: blue).
            HumanoidBuilder.ApplyMaterials(
                teamColorRenderers, headRenderer, visorRenderer,
                chestStripeRenderer, allRenderers, EnvironmentBuilder.Team0Color);
        }

        /// <summary>Tint the body with a team color (multiplayer).</summary>
        public void ApplyTeamColor(Color color)
        {
            HumanoidBuilder.ApplyTeamColor(teamColorRenderers, chestStripeRenderer, color);
        }

        /// <summary>Hide head parts for the local player so they never block the camera.</summary>
        public void SetFirstPerson(bool firstPerson)
        {
            foreach (var r in headRenderers)
                r.enabled = !firstPerson;
        }

        /// <summary>Show/hide the whole body (used while dead in multiplayer).</summary>
        public void SetVisible(bool visible)
        {
            foreach (var r in allRenderers)
                r.enabled = visible;
        }
    }
}
