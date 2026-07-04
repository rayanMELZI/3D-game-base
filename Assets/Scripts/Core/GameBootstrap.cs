using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Builds the entire test level from code: ground, walls, lighting,
    /// player (with camera + gun) and target dummies.
    /// No prefabs, models or materials are needed — everything is generated,
    /// so this project works as a clean base for any future game.
    /// Replace pieces of this with real prefabs/scenes as your games grow.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        [Header("Arena")]
        public float arenaSize = 50f;
        public float wallHeight = 3f;

        [Header("Targets")]
        public int targetCount = 6;

        private void Awake()
        {
            BuildLighting();
            BuildGround();
            BuildWalls();
            BuildTargets();
            BuildPlayer();
        }

        // ------------------------------------------------------------------
        // Environment
        // ------------------------------------------------------------------

        private void BuildLighting()
        {
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.color = new Color(1f, 0.96f, 0.88f);
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private void BuildGround()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            // A default plane is 10x10 units, so scale it to arenaSize.
            ground.transform.localScale = Vector3.one * (arenaSize / 10f);
            ground.GetComponent<Renderer>().material = MakeMaterial(new Color(0.35f, 0.42f, 0.35f));
        }

        private void BuildWalls()
        {
            float half = arenaSize / 2f;
            var wallMat = MakeMaterial(new Color(0.45f, 0.45f, 0.5f));

            CreateWall("Wall North", new Vector3(0, wallHeight / 2f, half), new Vector3(arenaSize, wallHeight, 1f), wallMat);
            CreateWall("Wall South", new Vector3(0, wallHeight / 2f, -half), new Vector3(arenaSize, wallHeight, 1f), wallMat);
            CreateWall("Wall East", new Vector3(half, wallHeight / 2f, 0), new Vector3(1f, wallHeight, arenaSize), wallMat);
            CreateWall("Wall West", new Vector3(-half, wallHeight / 2f, 0), new Vector3(1f, wallHeight, arenaSize), wallMat);

            // A few crates to shoot around / jump on.
            var crateMat = MakeMaterial(new Color(0.55f, 0.4f, 0.25f));
            CreateCrate(new Vector3(4, 0.75f, 8), 1.5f, crateMat);
            CreateCrate(new Vector3(-6, 1f, 5), 2f, crateMat);
            CreateCrate(new Vector3(0, 0.5f, 12), 1f, crateMat);
        }

        private void CreateWall(string name, Vector3 position, Vector3 scale, Material mat)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.position = position;
            wall.transform.localScale = scale;
            wall.GetComponent<Renderer>().material = mat;
        }

        private void CreateCrate(Vector3 position, float size, Material mat)
        {
            var crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crate.name = "Crate";
            crate.transform.position = position;
            crate.transform.localScale = Vector3.one * size;
            crate.GetComponent<Renderer>().material = mat;
        }

        // ------------------------------------------------------------------
        // Targets (simple "characters": capsule body + sphere head)
        // ------------------------------------------------------------------

        private void BuildTargets()
        {
            var bodyMat = MakeMaterial(new Color(0.8f, 0.2f, 0.2f));
            var headMat = MakeMaterial(new Color(0.95f, 0.75f, 0.6f));

            for (int i = 0; i < targetCount; i++)
            {
                // Spread the dummies in an arc in front of the spawn point.
                float angle = Mathf.Lerp(-60f, 60f, targetCount > 1 ? i / (float)(targetCount - 1) : 0.5f);
                float distance = Random.Range(10f, 18f);
                Vector3 pos = Quaternion.Euler(0, angle, 0) * Vector3.forward * distance;
                CreateTargetDummy(pos, bodyMat, headMat);
            }
        }

        private void CreateTargetDummy(Vector3 position, Material bodyMat, Material headMat)
        {
            var root = new GameObject("TargetDummy");
            root.transform.position = position;
            // Face the player spawn (origin).
            root.transform.rotation = Quaternion.LookRotation(-position.normalized, Vector3.up);

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0, 1f, 0);
            body.GetComponent<Renderer>().material = bodyMat;

            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0, 2.25f, 0);
            head.transform.localScale = Vector3.one * 0.5f;
            head.GetComponent<Renderer>().material = headMat;

            var health = root.AddComponent<Health>();
            health.maxHealth = 100f;
            health.ResetHealth(); // Awake ran during AddComponent, before maxHealth was set

            root.AddComponent<TargetDummy>();
        }

        // ------------------------------------------------------------------
        // Player
        // ------------------------------------------------------------------

        private void BuildPlayer()
        {
            var player = new GameObject("Player");
            player.transform.position = new Vector3(0, 1.1f, -15f);

            var controller = player.AddComponent<CharacterController>();
            controller.height = 2f;
            controller.radius = 0.4f;
            controller.center = new Vector3(0, 1f, 0);

            // Visible body (its collider is removed — the CharacterController handles collision).
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(player.transform, false);
            body.transform.localPosition = new Vector3(0, 1f, 0);
            body.GetComponent<Renderer>().material = MakeMaterial(new Color(0.2f, 0.4f, 0.8f));

            // Camera at eye height.
            var camGo = new GameObject("PlayerCamera");
            camGo.transform.SetParent(player.transform, false);
            camGo.transform.localPosition = new Vector3(0, 1.65f, 0);
            var cam = camGo.AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.nearClipPlane = 0.05f;
            camGo.AddComponent<AudioListener>();

            var look = camGo.AddComponent<MouseLook>();
            look.playerBody = player.transform;

            var movement = player.AddComponent<PlayerMovement>();
            movement.spawnPoint = player.transform.position;

            var playerHealth = player.AddComponent<Health>();
            playerHealth.maxHealth = 100f;
            playerHealth.ResetHealth(); // Awake ran during AddComponent, before maxHealth was set

            // Gun: simple primitive model hanging off the camera, script does raycast shooting.
            var gun = BuildGun(camGo.transform, cam);

            var hud = player.AddComponent<HudOverlay>();
            hud.gun = gun;
            hud.playerHealth = playerHealth;
        }

        private Gun BuildGun(Transform cameraTransform, Camera cam)
        {
            var holder = new GameObject("WeaponHolder");
            holder.transform.SetParent(cameraTransform, false);
            holder.transform.localPosition = new Vector3(0.3f, -0.25f, 0.45f);

            var gunMat = MakeMaterial(new Color(0.15f, 0.15f, 0.15f));

            // Gun body.
            var bodyPart = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bodyPart.name = "GunBody";
            Destroy(bodyPart.GetComponent<Collider>());
            bodyPart.transform.SetParent(holder.transform, false);
            bodyPart.transform.localScale = new Vector3(0.07f, 0.12f, 0.35f);
            bodyPart.GetComponent<Renderer>().material = gunMat;

            // Barrel.
            var barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            barrel.name = "Barrel";
            Destroy(barrel.GetComponent<Collider>());
            barrel.transform.SetParent(holder.transform, false);
            barrel.transform.localPosition = new Vector3(0, 0.03f, 0.28f);
            barrel.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            barrel.transform.localScale = new Vector3(0.045f, 0.12f, 0.045f);
            barrel.GetComponent<Renderer>().material = gunMat;

            // Muzzle point (used for the flash + tracer origin).
            var muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(holder.transform, false);
            muzzle.transform.localPosition = new Vector3(0, 0.03f, 0.42f);

            var flashLight = muzzle.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.color = new Color(1f, 0.85f, 0.5f);
            flashLight.intensity = 3f;
            flashLight.range = 4f;
            flashLight.enabled = false;

            var gun = holder.AddComponent<Gun>();
            gun.shootCamera = cam;
            gun.muzzle = muzzle.transform;
            gun.muzzleFlash = flashLight;
            return gun;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        /// <summary>Creates a plain colored material using the built-in Standard shader.</summary>
        public static Material MakeMaterial(Color color)
        {
            var mat = new Material(Shader.Find("Standard"));
            mat.color = color;
            return mat;
        }
    }
}
