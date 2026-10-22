using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Builds a bot's body on its root at runtime: the same controller capsule,
    /// primitive humanoid hitboxes (lethal head trigger included) and
    /// third-person weapon anchor as a player rig (PlayerFactory), but no
    /// camera, input or first-person weapon. PlayerRigRefs then swaps in an
    /// imported character skin exactly like for players.
    /// </summary>
    public static class BotRig
    {
        /// <summary>Eye height above the feet (head hitbox center is 1.72).</summary>
        public const float EyeHeight = 1.62f;

        public static PlayerRigRefs Build(GameObject root)
        {
            // Same capsule as players: it stops at the shoulders, so the head
            // above it is only hit through its trigger hitbox.
            var controller = root.AddComponent<CharacterController>();
            controller.height = 1.55f;
            controller.radius = 0.35f;
            controller.center = new Vector3(0, 0.78f, 0);
            controller.slopeLimit = 50f;
            controller.stepOffset = 0.4f;

            var body = HumanoidBuilder.Build(root.transform, addHitboxes: true);

            var tpAnchor = new GameObject("ThirdPersonWeaponAnchor");
            tpAnchor.transform.SetParent(root.transform, false);
            tpAnchor.transform.localPosition = new Vector3(0.12f, 1.32f, 0.35f);

            var rig = root.AddComponent<PlayerRigRefs>();
            rig.thirdPersonWeaponAnchor = tpAnchor.transform;
            rig.bodyRoot = body.root.transform;
            rig.teamColorRenderers = body.teamRenderers;
            rig.darkTeamRenderers = body.darkTeamRenderers;
            rig.headRenderer = body.headRenderer;
            rig.visorRenderer = body.visorRenderer;
            rig.chestStripeRenderer = body.chestStripeRenderer;
            rig.headRenderers = body.headParts;
            rig.allRenderers = body.allRenderers;
            rig.RuntimeInit(); // materials + runtime torso hitboxes
            return rig;
        }
    }
}
