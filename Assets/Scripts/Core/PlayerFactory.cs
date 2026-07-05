using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Builds the player rig (controller, humanoid body, camera, weapon holder)
    /// on a given root object. Used at runtime by the single-player bootstrap
    /// and in the editor by the multiplayer setup tool to bake the player prefab —
    /// which is why materials are NOT assigned here (PlayerRigRefs does that at
    /// runtime; code-generated materials can't be saved inside a prefab).
    /// </summary>
    public static class PlayerFactory
    {
        public static PlayerRigRefs BuildPlayerRig(GameObject root)
        {
            // The capsule deliberately stops at the shoulders (1.55m): body shots
            // hit it, while the head above it only has the trigger hitbox —
            // that's what makes headshots possible.
            var controller = root.AddComponent<CharacterController>();
            controller.height = 1.55f;
            controller.radius = 0.35f;
            controller.center = new Vector3(0, 0.78f, 0);
            controller.slopeLimit = 50f;

            // --- Humanoid body with full hitboxes (head trigger = lethal) ---
            var body = HumanoidBuilder.Build(root.transform, addHitboxes: true);

            var limbAnimator = root.AddComponent<LimbAnimator>();
            limbAnimator.armL = body.armL;
            limbAnimator.armR = body.armR;
            limbAnimator.legL = body.legL;
            limbAnimator.legR = body.legR;
            limbAnimator.aimPose = true; // players hold their weapon with both hands

            // --- Camera at eye height ---
            var camGo = new GameObject("PlayerCamera");
            camGo.transform.SetParent(root.transform, false);
            camGo.transform.localPosition = new Vector3(0, 1.62f, 0);
            var cam = camGo.AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.nearClipPlane = 0.05f;
            camGo.AddComponent<AudioListener>();

            var look = camGo.AddComponent<MouseLook>();
            look.playerBody = root.transform;

            // --- Viewmodel holder (weapon models are generated at runtime) ---
            var weaponHolder = new GameObject("WeaponHolder");
            weaponHolder.transform.SetParent(camGo.transform, false);

            // --- Third-person weapon anchor: between the raised hands, used for the
            // owner's shadows-only gun and for the model other players see ---
            var tpAnchor = new GameObject("ThirdPersonWeaponAnchor");
            tpAnchor.transform.SetParent(root.transform, false);
            tpAnchor.transform.localPosition = new Vector3(0.12f, 1.32f, 0.35f);

            // --- Gameplay components ---
            var movement = root.AddComponent<PlayerMovement>();
            movement.cameraTransform = camGo.transform;
            movement.bodyVisual = body.root.transform;
            look.movement = movement; // sprint FOV boost + slide roll

            var weaponController = root.AddComponent<WeaponController>();
            weaponController.shootCamera = cam;
            weaponController.mouseLook = look;
            weaponController.viewmodelHolder = weaponHolder.transform;
            weaponController.selfRoot = root.transform;
            weaponController.thirdPersonAnchor = tpAnchor.transform;

            // --- Reference hub ---
            var rig = root.AddComponent<PlayerRigRefs>();
            rig.cameraObject = camGo;
            rig.playerCamera = cam;
            rig.mouseLook = look;
            rig.movement = movement;
            rig.weaponController = weaponController;
            rig.thirdPersonWeaponAnchor = tpAnchor.transform;
            rig.bodyRoot = body.root.transform;
            rig.teamColorRenderers = body.teamRenderers;
            rig.darkTeamRenderers = body.darkTeamRenderers;
            rig.headRenderer = body.headRenderer;
            rig.visorRenderer = body.visorRenderer;
            rig.chestStripeRenderer = body.chestStripeRenderer;
            rig.headRenderers = body.headParts;
            rig.allRenderers = body.allRenderers;
            rig.RuntimeInit(); // no-op in the editor prefab-baking path
            return rig;
        }
    }
}
