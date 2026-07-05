using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Single-player / offline bootstrap: builds the arena, target dummies and
    /// a local player when the scene starts. This is the offline sandbox — the
    /// multiplayer scene has its own bootstrap (MultiplayerBootstrap).
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        [Header("Arena")]
        public float arenaSize = 60f;

        [Header("Targets")]
        public int targetCount = 6;

        private void Awake()
        {
            EnvironmentBuilder.SetupLightingAndSky();
            EnvironmentBuilder.BuildArena(arenaSize);

            Vector3 playerSpawn = new Vector3(0, 0.1f, -(arenaSize / 2f - 5f));
            BuildTargets(playerSpawn);
            BuildPlayer(playerSpawn);

            new GameObject("OfflineMenu").AddComponent<OfflineMenu>();
        }

        // ------------------------------------------------------------------
        // Target dummies (fixed, deterministic arc on the far side)
        // ------------------------------------------------------------------

        private void BuildTargets(Vector3 playerSpawn)
        {
            for (int i = 0; i < targetCount; i++)
            {
                float angle = targetCount > 1 ? Mathf.Lerp(-50f, 50f, i / (float)(targetCount - 1)) : 0f;
                float distance = 12f + 4f * (i % 2); // alternate near/far, no randomness
                Vector3 pos = Quaternion.Euler(0, angle, 0) * Vector3.forward * distance;
                CreateTargetDummy(pos, playerSpawn);
            }
        }

        private void CreateTargetDummy(Vector3 position, Vector3 faceTowards)
        {
            var root = new GameObject("TargetDummy");
            root.transform.position = position;
            Vector3 lookDir = faceTowards - position;
            lookDir.y = 0;
            root.transform.rotation = Quaternion.LookRotation(lookDir.normalized, Vector3.up);

            // Same humanoid body as players, in hostile red — with a head hitbox,
            // so headshots one-shot dummies too.
            var body = HumanoidBuilder.Build(root.transform, addHeadHitbox: true);
            HumanoidBuilder.ApplyMaterials(
                body.teamRenderers, body.headRenderer, body.visorRenderer,
                body.chestStripeRenderer, body.allRenderers, new Color(0.85f, 0.25f, 0.2f));

            // Body hitbox (matches the player: capsule up to the shoulders).
            var collider = root.AddComponent<CapsuleCollider>();
            collider.height = 1.55f;
            collider.radius = 0.35f;
            collider.center = new Vector3(0, 0.78f, 0);

            var limbs = root.AddComponent<LimbAnimator>();
            limbs.armL = body.armL;
            limbs.armR = body.armR;
            limbs.legL = body.legL;
            limbs.legR = body.legR;

            var health = root.AddComponent<Health>();
            health.maxHealth = 100f;
            health.ResetHealth(); // Awake ran during AddComponent, before maxHealth was set

            root.AddComponent<TargetDummy>();
        }

        // ------------------------------------------------------------------
        // Player
        // ------------------------------------------------------------------

        private void BuildPlayer(Vector3 spawn)
        {
            var player = new GameObject("Player");
            player.transform.position = spawn;

            var rig = PlayerFactory.BuildPlayerRig(player);
            rig.SetFirstPerson(true); // hide own head so it never blocks the camera
            rig.movement.spawnPoint = spawn;

            var health = player.AddComponent<Health>();
            health.maxHealth = 100f;
            health.ResetHealth();

            var hud = player.AddComponent<HudOverlay>();
            hud.weaponController = rig.weaponController;
            hud.HealthSource = health;
        }
    }
}
