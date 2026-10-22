using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// One networked zombie (Assets/Prefabs/NetworkZombie.prefab, registered in
    /// DefaultNetworkPrefabs). The server spawns it (ZombieDirector), moves it,
    /// decides its attacks and owns its health; ZombieNetworkTransform carries
    /// the movement to every client. Each peer builds the visuals locally from
    /// the replicated Skin: an imported Survivalist skin, tinted, arms out,
    /// glowing eyes. Hit detection is the same as players: the capsule is the
    /// body, a trigger sphere above it is the head (Hitbox.isHead).
    /// Shots land on whichever client fired and reach the server through
    /// IDamageable.TakeDamage → DamageRpc, exactly like NetworkHealth.
    /// </summary>
    public class ZombieAgent : NetworkBehaviour, IDamageable
    {
        /// <summary>Every spawned zombie on this peer (alive or dying).</summary>
        public static readonly List<ZombieAgent> Active = new List<ZombieAgent>();

        public NetworkVariable<float> Hp = new NetworkVariable<float>(
            100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<bool> Dead = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        /// <summary>Which Survivalist skin (0..3) every client shows.</summary>
        public NetworkVariable<byte> Skin = new NetworkVariable<byte>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        /// <summary>Fast zombie: sprint animation on every client.</summary>
        public NetworkVariable<bool> Runner = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Server tuning, set by ZombieDirector before Spawn().
        [System.NonSerialized] public float moveSpeed = 1.8f;
        [System.NonSerialized] public float attackDamage = 30f;
        [System.NonSerialized] public float attackInterval = 1.1f;

        private const float AttackRange = 1.5f;
        private const float AttackReach = 2.2f;   // still in reach when the swipe lands
        private const float AttackWindup = 0.35f;
        private const float HeadshotMultiplier = 3f;
        private const float DespawnDelay = 4f;
        private const float WalkAuthoredSpeed = 3.22f;  // Survivalist walk clip
        private const float RunAuthoredSpeed = 5f;      // Survivalist sprint clip

        public bool IsAlive => IsSpawned && !Dead.Value;

        private CharacterController controller;
        private Collider headCollider;

        // Server state
        private NetworkPlayer target;
        private float nextRetarget;
        private float verticalVelocity;
        private float nextAttack;
        private float attackLandsAt = -1f;
        private float sidestepSign = 1f;
        private float sidestepUntil;
        private Vector3 progressAnchor;
        private float progressCheckAt;
        private float stuckSeconds;

        // Visuals (every peer)
        private Transform skin;
        private Animator animator;
        private Transform armL, armR, headBone;
        private Transform eyeL, eyeR;
        private Vector3 lastPosition;
        private float smoothedSpeed;
        private float swipeTime = -10f;
        private float deathTime = -1f;

        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int MoveXHash = Animator.StringToHash("MoveX");
        private static readonly int MoveYHash = Animator.StringToHash("MoveY");
        private static readonly int MotionSpeedHash = Animator.StringToHash("MotionSpeed");
        private static readonly int GroundedHash = Animator.StringToHash("Grounded");
        private static readonly int SprintingHash = Animator.StringToHash("Sprinting");

        // Tinted copies of the skin materials, shared by every zombie (no per-zombie leak).
        private static readonly Dictionary<Material, Material> tinted = new Dictionary<Material, Material>();
        private static Material eyeMaterial;
        private static Material fallbackMaterial;

        private void Awake()
        {
            // Same proportions as a player: the capsule stops at the shoulders,
            // the head trigger above it is the lethal-ish headshot zone.
            controller = gameObject.AddComponent<CharacterController>();
            controller.height = 1.55f;
            controller.radius = 0.35f;
            controller.center = new Vector3(0f, 0.78f, 0f);
            controller.slopeLimit = 50f;
            controller.stepOffset = 0.4f;

            var head = new GameObject("Head");
            head.transform.SetParent(transform, false);
            head.transform.localPosition = new Vector3(0f, 1.72f, 0f);
            var sphere = head.AddComponent<SphereCollider>();
            sphere.radius = 0.17f;
            sphere.isTrigger = true;
            head.AddComponent<Hitbox>().isHead = true;
            headCollider = sphere;
        }

        public override void OnNetworkSpawn()
        {
            Active.Add(this);
            Dead.OnValueChanged += OnDeadChanged;
            BuildVisual();
            lastPosition = transform.position;
            progressAnchor = transform.position;
            progressCheckAt = Time.time + 2f;
            nextAttack = Time.time + 0.8f;
            if (Dead.Value)
                OnDeadChanged(false, true); // late joiner seeing a zombie mid-fall
            SfxSynth.PlayAt(SfxSynth.ZombieGrowl(), transform.position + Vector3.up * 1.5f, 0.55f,
                Random.Range(0.8f, 1.15f));
        }

        public override void OnNetworkDespawn()
        {
            Active.Remove(this);
            Dead.OnValueChanged -= OnDeadChanged;
        }

        public override void OnDestroy()
        {
            Active.Remove(this);
            base.OnDestroy();
        }

        /// <summary>Server, before Spawn(): wave stats.</summary>
        public void ServerSetup(float health, float speed, bool runner, byte skinIndex)
        {
            Hp.Value = health;
            moveSpeed = speed;
            Runner.Value = runner;
            Skin.Value = skinIndex;
        }

        // ------------------------------------------------------------------
        // Damage (any peer → server)
        // ------------------------------------------------------------------

        public void TakeDamage(float amount, bool headshot = false)
        {
            if (!IsSpawned || Dead.Value)
                return;
            if (IsServer)
                ServerApplyDamage(amount, headshot, NetworkManager.LocalClientId);
            else
                DamageRpc(amount, headshot);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void DamageRpc(float amount, bool headshot, RpcParams rpcParams = default)
        {
            ServerApplyDamage(amount, headshot, rpcParams.Receive.SenderClientId);
        }

        /// <summary>Server-simulated splash (grenade launcher): the attacker is known.</summary>
        public void ServerExplosionDamage(float amount, ulong attackerId)
        {
            if (IsServer)
                ServerApplyDamage(amount, false, attackerId);
        }

        private void ServerApplyDamage(float amount, bool headshot, ulong attackerId)
        {
            if (Dead.Value || amount <= 0f)
                return;
            var mode = GameModeManager.Instance;
            if (mode != null && !mode.IsMatchActive)
                return;

            // A single RPC can't be worth more than a rocket to the face.
            amount = Mathf.Min(amount, 250f) * (headshot ? HeadshotMultiplier : 1f);
            Hp.Value = Mathf.Max(0f, Hp.Value - amount);
            if (Hp.Value > 0f)
                return;

            Dead.Value = true;
            if (mode != null)
                mode.ServerZombieKilled(attackerId, headshot);
            StartCoroutine(DespawnLater());
        }

        private IEnumerator DespawnLater()
        {
            yield return new WaitForSeconds(DespawnDelay);
            if (IsSpawned)
                NetworkObject.Despawn(true);
        }

        // ------------------------------------------------------------------
        // Server brain
        // ------------------------------------------------------------------

        private void Update()
        {
            if (IsSpawned && IsServer && !Dead.Value)
                ServerThink();
        }

        private void ServerThink()
        {
            var mode = GameModeManager.Instance;
            bool active = mode != null && mode.IsMatchActive;

            if (Time.time >= nextRetarget || target == null || !IsValidTarget(target))
            {
                target = FindTarget();
                nextRetarget = Time.time + 0.5f;
            }

            Vector3 move = Vector3.zero;
            if (active && target != null)
            {
                Vector3 toTarget = target.transform.position - transform.position;
                float dy = toTarget.y;
                toTarget.y = 0f;
                float distance = toTarget.magnitude;
                Vector3 wish = distance > 0.01f ? toTarget / distance : transform.forward;

                if (distance > AttackRange * 0.8f)
                    move = Steer(wish) * moveSpeed;

                // Face the player (or the way we walk while going around something).
                Vector3 face = move.sqrMagnitude > 0.01f ? move : wish;
                if (face.sqrMagnitude > 0.001f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation,
                        Quaternion.LookRotation(face), 360f * Time.deltaTime);

                // Swipe: wind up, then land if the player is still in reach.
                if (attackLandsAt < 0f && distance < AttackRange && Mathf.Abs(dy) < 1.6f && Time.time >= nextAttack)
                {
                    nextAttack = Time.time + attackInterval;
                    attackLandsAt = Time.time + AttackWindup;
                    AttackClientRpc();
                }
            }

            if (attackLandsAt >= 0f && Time.time >= attackLandsAt)
            {
                attackLandsAt = -1f;
                if (active && target != null && IsValidTarget(target))
                {
                    Vector3 d = target.transform.position - transform.position;
                    if (new Vector2(d.x, d.z).magnitude < AttackReach && Mathf.Abs(d.y) < 1.8f)
                        target.GetComponent<NetworkHealth>()?.ServerZombieDamage(attackDamage, this);
                }
            }

            move += Separation();

            if (controller.isGrounded && verticalVelocity < 0f)
                verticalVelocity = -1f;
            verticalVelocity += Physics.gravity.y * Time.deltaTime;
            controller.Move((move + Vector3.up * verticalVelocity) * Time.deltaTime);

            CheckProgress(move);
        }

        private static bool IsValidTarget(NetworkPlayer p) =>
            p != null && p.IsSpawned && !p.IsDead.Value && !p.Spectating.Value;

        private NetworkPlayer FindTarget()
        {
            NetworkPlayer best = null;
            float bestDistance = float.MaxValue;
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                var player = client.PlayerObject != null ? client.PlayerObject.GetComponent<NetworkPlayer>() : null;
                if (!IsValidTarget(player))
                    continue;
                float d = (player.transform.position - transform.position).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = player;
                }
            }
            return best;
        }

        /// <summary>
        /// Walk toward the player; if a wall or prop is in the way, try angles
        /// to one side (sticky for a moment so it commits to going around).
        /// Players never count as obstacles: they are what we want to reach.
        /// </summary>
        private Vector3 Steer(Vector3 wish)
        {
            if (IsClear(wish))
            {
                if (Time.time > sidestepUntil)
                    return wish;
            }
            float[] angles = { 35f, 70f, 105f, 140f };
            for (int pass = 0; pass < 2; pass++)
            {
                float sign = pass == 0 ? sidestepSign : -sidestepSign;
                foreach (float a in angles)
                {
                    Vector3 dir = Quaternion.Euler(0f, a * sign, 0f) * wish;
                    if (IsClear(dir))
                    {
                        if (pass == 1)
                            sidestepSign = sign;
                        if (a > 35f || !IsClear(wish))
                            sidestepUntil = Time.time + 0.6f;
                        return dir;
                    }
                }
            }
            return wish; // boxed in: push on, the stuck check will relocate us
        }

        private bool IsClear(Vector3 dir)
        {
            Vector3 origin = transform.position + Vector3.up * 0.9f;
            if (!Physics.SphereCast(origin, 0.3f, dir, out var hit, 1.4f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return true;
            // Players and other zombies don't block the path (separation handles the crowd).
            return hit.collider.GetComponentInParent<NetworkPlayer>() != null
                   || hit.collider.GetComponentInParent<ZombieAgent>() != null;
        }

        /// <summary>Small push away from zombies that stand too close, so the horde doesn't merge.</summary>
        private Vector3 Separation()
        {
            Vector3 push = Vector3.zero;
            foreach (var other in Active)
            {
                if (other == this || other == null || other.Dead.Value)
                    continue;
                Vector3 d = transform.position - other.transform.position;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq < 0.81f && sq > 0.0001f)
                    push += d.normalized * (0.9f - Mathf.Sqrt(sq)) * 2.5f;
            }
            return push;
        }

        /// <summary>Stuck behind something for a while with nobody near: re-enter from a far spawn.</summary>
        private void CheckProgress(Vector3 move)
        {
            if (Time.time < progressCheckAt)
                return;
            float moved = Vector3.Distance(progressAnchor, transform.position);
            bool wantsToMove = move.sqrMagnitude > 0.25f;
            stuckSeconds = wantsToMove && moved < 0.6f ? stuckSeconds + 2f : 0f;
            progressAnchor = transform.position;
            progressCheckAt = Time.time + 2f;

            if (stuckSeconds >= 6f)
            {
                stuckSeconds = 0f;
                var director = GameModeManager.Instance != null
                    ? GameModeManager.Instance.GetComponent<ZombieDirector>() : null;
                if (director != null && director.TryPickSpawn(out Vector3 pos))
                    ServerTeleport(pos);
            }
            // Fell out of the map: same treatment.
            if (transform.position.y < -20f)
            {
                var director = GameModeManager.Instance != null
                    ? GameModeManager.Instance.GetComponent<ZombieDirector>() : null;
                if (director != null && director.TryPickSpawn(out Vector3 pos))
                    ServerTeleport(pos);
            }
        }

        private void ServerTeleport(Vector3 position)
        {
            controller.enabled = false;
            var netTransform = GetComponent<ZombieNetworkTransform>();
            if (netTransform != null)
                netTransform.Teleport(position, transform.rotation, transform.localScale);
            else
                transform.position = position;
            controller.enabled = true;
            verticalVelocity = 0f;
        }

        [ClientRpc]
        private void AttackClientRpc()
        {
            swipeTime = Time.time;
            SfxSynth.PlayAt(SfxSynth.ZombieGrowl(), transform.position + Vector3.up * 1.5f, 0.7f,
                Random.Range(1.05f, 1.3f));
        }

        // ------------------------------------------------------------------
        // Visuals (every peer)
        // ------------------------------------------------------------------

        private void BuildVisual()
        {
            int n = Skin.Value % 4 + 1;
            var prefab = Resources.Load<GameObject>($"Characters/Survivalist_{n}");
            if (prefab != null)
            {
                var instance = Instantiate(prefab, transform);
                instance.name = "ZombieSkin";
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                skin = instance.transform;

                foreach (var col in instance.GetComponentsInChildren<Collider>(true))
                    Destroy(col); // our capsule + head trigger are the hitboxes

                animator = instance.GetComponent<Animator>();
                if (animator != null)
                {
                    var ctrl = Resources.Load<RuntimeAnimatorController>("Characters/SurvivalistLocomotion");
                    if (ctrl != null)
                        animator.runtimeAnimatorController = ctrl;
                    animator.applyRootMotion = false;
                    int upper = animator.GetLayerIndex("Upper Body Layer");
                    if (upper >= 0)
                        animator.SetLayerWeight(upper, 0f); // no rifle hold: arms are posed in LateUpdate
                    if (animator.isHuman)
                    {
                        armL = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                        armR = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                        headBone = animator.GetBoneTransform(HumanBodyBones.Head);
                    }
                }

                foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                        mats[i] = Tinted(mats[i]);
                    r.sharedMaterials = mats;
                }
            }
            else
            {
                // Skins missing: a plain green capsule so it still reads as a zombie.
                if (fallbackMaterial == null)
                    fallbackMaterial = EnvironmentBuilder.MakeMaterial(new Color(0.2f, 0.36f, 0.16f), 0f, 0.2f);
                var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Destroy(body.GetComponent<Collider>());
                body.name = "ZombieBody";
                body.transform.SetParent(transform, false);
                body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                body.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
                body.GetComponent<Renderer>().sharedMaterial = fallbackMaterial;
                skin = body.transform;
            }

            // Glowing eyes: the first thing you see in the dark.
            if (eyeMaterial == null)
                eyeMaterial = EnvironmentBuilder.MakeEmissiveMaterial(new Color(1f, 0.15f, 0.05f), 4f);
            eyeL = MakeEye("EyeL");
            eyeR = MakeEye("EyeR");
        }

        private Transform MakeEye(string name)
        {
            var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(eye.GetComponent<Collider>());
            eye.name = name;
            eye.transform.SetParent(transform, false);
            eye.transform.localScale = Vector3.one * 0.045f;
            var r = eye.GetComponent<Renderer>();
            r.sharedMaterial = eyeMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return eye.transform;
        }

        private static Material Tinted(Material source)
        {
            if (source == null)
                return null;
            if (tinted.TryGetValue(source, out var mat) && mat != null)
                return mat;
            mat = new Material(source) { name = source.name + "_Zombie" };
            var tint = new Color(0.5f, 0.68f, 0.45f);
            if (mat.HasProperty("_Color"))
                mat.color = mat.color * tint;
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", mat.GetColor("_BaseColor") * tint);
            tinted[source] = mat;
            return mat;
        }

        private void LateUpdate()
        {
            if (!IsSpawned)
                return;

            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            Vector3 delta = transform.position - lastPosition;
            lastPosition = transform.position;
            delta.y = 0f;
            float speed = Dead.Value ? 0f : Mathf.Min(delta.magnitude / dt, 8f);
            smoothedSpeed = Mathf.MoveTowards(smoothedSpeed, speed, 20f * dt);

            if (animator != null && animator.enabled)
            {
                bool runner = Runner.Value;
                animator.SetFloat(SpeedHash, smoothedSpeed, 0.12f, dt);
                animator.SetFloat(MoveXHash, 0f);
                animator.SetFloat(MoveYHash, smoothedSpeed > 0.1f ? 1f : 0f, 0.12f, dt);
                float authored = runner ? RunAuthoredSpeed : WalkAuthoredSpeed;
                animator.SetFloat(MotionSpeedHash,
                    smoothedSpeed > 0.1f ? Mathf.Clamp(smoothedSpeed / authored, 0.5f, 1.5f) : 1f);
                animator.SetBool(GroundedHash, true);
                animator.SetBool(SprintingHash, runner && smoothedSpeed > 2.5f);
            }

            PoseArms();
            PlaceEyes();
            AnimateDeath();
        }

        /// <summary>Classic zombie reach: both arms raised forward, slashing on attack.</summary>
        private void PoseArms()
        {
            if (Dead.Value)
                return;
            float swipe = Mathf.Clamp01(1f - Mathf.Abs(Time.time - swipeTime - 0.25f) / 0.25f);
            float sway = Mathf.Sin(Time.time * 3f + NetworkObjectId) * 6f;
            if (armL != null)
                armL.rotation = Quaternion.AngleAxis(-75f - sway + swipe * 35f, transform.right) * armL.rotation;
            if (armR != null)
                armR.rotation = Quaternion.AngleAxis(-75f + sway + swipe * 50f, transform.right) * armR.rotation;
        }

        private void PlaceEyes()
        {
            if (eyeL == null || eyeR == null)
                return;
            Vector3 head = headBone != null ? headBone.position : transform.position + Vector3.up * 1.68f;
            Vector3 fwd = transform.forward, right = transform.right;
            Vector3 center = head + fwd * 0.11f + Vector3.up * 0.04f;
            eyeL.position = center - right * 0.035f;
            eyeR.position = center + right * 0.035f;
        }

        private void OnDeadChanged(bool previous, bool dead)
        {
            if (!dead)
                return;
            deathTime = Time.time;
            // Bullets go through corpses; the server stops moving it.
            if (controller != null) controller.enabled = false;
            if (headCollider != null) headCollider.enabled = false;
            if (animator != null) animator.enabled = false;
            if (eyeL != null) eyeL.gameObject.SetActive(false);
            if (eyeR != null) eyeR.gameObject.SetActive(false);
            Effects.SpawnDeathBurst(transform.position + Vector3.up * 1.1f, new Color(0.3f, 0.5f, 0.2f));
            SfxSynth.PlayAt(SfxSynth.Death(), transform.position, 0.8f, 0.7f);
        }

        /// <summary>Topple backward, then sink into the ground before the server despawns it.</summary>
        private void AnimateDeath()
        {
            if (deathTime < 0f || skin == null)
                return;
            float t = Time.time - deathTime;
            float fall = Mathf.SmoothStep(0f, 1f, t / 0.6f);
            skin.localRotation = Quaternion.Euler(-85f * fall, 0f, 0f);
            float sink = Mathf.Clamp01((t - 2.2f) / 1.5f);
            skin.localPosition = new Vector3(0f, -0.6f * sink, 0f);
        }
    }
}
