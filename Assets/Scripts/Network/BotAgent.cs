using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.AI;

namespace FpsBase
{
    /// <summary>
    /// A networked bot (NetworkBot prefab, spawned by BotDirector on the server).
    /// Every client builds the same body (BotRig + imported skin, real hitboxes),
    /// a third-person weapon and a nametag, and sees its shots (tracer + gunshot),
    /// reloads and deaths. The server alone thinks for it: navigation on a runtime
    /// NavMesh (BotNavigation), line-of-sight checks, aim with reaction time and
    /// error, hitscan shots resolved on the server, and the same scoring as a
    /// player (kills/deaths/streaks/Gun Game level, kill feed, team scores).
    /// Bots are identified by <see cref="BotId"/>, a value no client id can take,
    /// wherever the match code uses a client id (kill credit, teams, names).
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class BotAgent : NetworkBehaviour, IDamageable
    {
        /// <summary>Bot ids start here: far above any client id, below NetworkHealth's object keys.</summary>
        public const ulong IdBase = 1UL << 36;

        public static bool IsBotId(ulong id) => id >= IdBase && id < IdBase + (1UL << 32);

        /// <summary>Every spawned bot on this peer (host and clients).</summary>
        public static readonly List<BotAgent> All = new List<BotAgent>();

        public NetworkVariable<int> Team = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<bool> IsDead = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> Kills = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> Deaths = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> KillStreak = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> GunGameLevel = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<FixedString32Bytes> BotName = new NetworkVariable<FixedString32Bytes>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> WeaponIndex = new NetworkVariable<int>(
            WeaponDefinition.DefaultIndex, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        /// <summary>Aim pitch in degrees (positive = looking down, like MouseLook).</summary>
        public NetworkVariable<float> Pitch = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<bool> Reloading = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<bool> Protected = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<float> Hp = new NetworkVariable<float>(
            100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public float maxHealth = 100f;
        public float respawnDelay = 3f;
        [Tooltip("Bot damage multiplier (bots aim worse than players but never miss a frame).")]
        public float damageScale = 0.85f;

        public ulong BotId => IdBase + NetworkObjectId;
        public string DisplayName => BotName.Value.IsEmpty ? "BOT" : BotName.Value.ToString();
        public bool Alive => IsSpawned && !IsDead.Value;
        /// <summary>Time.time of this bot's last shot on THIS peer (radar "on fire" mode).</summary>
        public float LastShotTime { get; private set; } = -999f;

        private static WeaponDefinition[] weapons;
        public static WeaponDefinition[] Weapons => weapons ??= WeaponDefinition.CreateDefaultLoadout();
        public WeaponDefinition CurrentWeapon => Weapons[Mathf.Clamp(WeaponIndex.Value, 0, Weapons.Length - 1)];

        private PlayerRigRefs rig;
        private CharacterController controller;
        private NetworkTransform netTransform;
        private Nametag nametag;
        private WeaponModelInstance weaponModel;
        private bool hasWeaponModel;
        private bool modelDirty;

        // Set by BotDirector before Spawn(); applied by the server in OnNetworkSpawn.
        [System.NonSerialized] public int pendingTeam;
        [System.NonSerialized] public string pendingName;

        private void Awake()
        {
            rig = BotRig.Build(gameObject);
            controller = GetComponent<CharacterController>();
            netTransform = GetComponent<NetworkTransform>();
            lastPosition = transform.position;
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            if (IsServer)
            {
                Team.Value = pendingTeam;
                BotName.Value = string.IsNullOrEmpty(pendingName) ? "BOT" : pendingName;
                Hp.Value = maxHealth;
            }

            rig.ApplyCharacterSkin((int)(NetworkObjectId % 4));
            ApplyTeamColor(Team.Value);
            nametag = Nametag.Create(transform, DisplayName, EnvironmentBuilder.TeamColor(Team.Value));
            RebuildWeaponModel();
            ApplyDeadState(IsDead.Value);
            rig.SetProtectedGlow(Protected.Value);

            Team.OnValueChanged += OnTeamChanged;
            BotName.OnValueChanged += OnNameChanged;
            WeaponIndex.OnValueChanged += OnWeaponChanged;
            IsDead.OnValueChanged += OnDeadChanged;
            Protected.OnValueChanged += OnProtectedChanged;

            if (IsServer)
                needsFirstRespawn = true; // next frame: the NetworkTransform must be spawned to teleport
        }

        private bool needsFirstRespawn;

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            Team.OnValueChanged -= OnTeamChanged;
            BotName.OnValueChanged -= OnNameChanged;
            WeaponIndex.OnValueChanged -= OnWeaponChanged;
            IsDead.OnValueChanged -= OnDeadChanged;
            Protected.OnValueChanged -= OnProtectedChanged;
        }

        public override void OnDestroy()
        {
            All.Remove(this);
            base.OnDestroy();
        }

        // ------------------------------------------------------------------
        // Visuals (every peer)
        // ------------------------------------------------------------------

        private void OnTeamChanged(int previous, int next) => ApplyTeamColor(next);

        private void ApplyTeamColor(int team)
        {
            var color = EnvironmentBuilder.TeamColor(team);
            rig.ApplyTeamColor(color);
            if (nametag != null)
                nametag.SetColor(color);
        }

        private void OnNameChanged(FixedString32Bytes previous, FixedString32Bytes next)
        {
            if (nametag != null)
                nametag.SetText(DisplayName);
        }

        private void OnWeaponChanged(int previous, int next) => modelDirty = true;

        private void OnProtectedChanged(bool previous, bool next) => rig.SetProtectedGlow(next);

        private void RebuildWeaponModel()
        {
            modelDirty = false;
            if (hasWeaponModel)
            {
                Destroy(weaponModel.root);
                hasWeaponModel = false;
            }
            weaponModel = WeaponModelBuilder.Build(CurrentWeapon, rig.thirdPersonWeaponAnchor, Vector3.zero, castShadows: true);
            hasWeaponModel = true;
            weaponModel.root.SetActive(!IsDead.Value);
        }

        private void OnDeadChanged(bool previous, bool dead)
        {
            ApplyDeadState(dead);
            if (dead && !previous)
            {
                Effects.SpawnDeathBurst(transform.position + Vector3.up * 1.1f, EnvironmentBuilder.TeamColor(Team.Value));
                SfxSynth.PlayAt(SfxSynth.Death(), transform.position, 0.9f);
            }
        }

        /// <summary>Dead bots vanish: no body, gun or nametag, and no colliders to block shots.</summary>
        private void ApplyDeadState(bool dead)
        {
            rig.SetVisible(!dead);
            if (hasWeaponModel)
                weaponModel.root.SetActive(!dead);
            if (nametag != null)
                nametag.gameObject.SetActive(!dead);
            foreach (var col in GetComponentsInChildren<Collider>(true))
                if (col != null) col.enabled = !dead;
        }

        // Animation + aim pose from the replicated movement, on every peer
        // (the server moves the bot, clients see the interpolated transform).
        private Vector3 lastPosition;
        private Vector3 smoothedVelocity;

        private void UpdateAnimation()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;
            Vector3 velocity = (transform.position - lastPosition) / dt;
            lastPosition = transform.position;
            velocity.y = 0f;
            velocity = Vector3.ClampMagnitude(velocity, 10f); // respawn teleports
            smoothedVelocity = Vector3.Lerp(smoothedVelocity, velocity, 1f - Mathf.Exp(-12f * dt));

            float speed = smoothedVelocity.magnitude;
            Vector3 local = transform.InverseTransformDirection(smoothedVelocity);
            Vector2 moveInput = speed > 0.2f ? new Vector2(local.x, local.z) / speed : Vector2.zero;

            if (rig.characterAnimator != null)
            {
                rig.characterAnimator.useNetworkState = true;
                rig.characterAnimator.SetNetworkAnimationState(
                    moveInput, speed, true, 0f, false, speed > 6f, false, Reloading.Value, false, false);
            }
            if (rig.characterPose != null)
            {
                rig.characterPose.remoteDriven = true;
                rig.characterPose.remotePitch = Pitch.Value;
                rig.characterPose.remoteSlide = 0f;
                rig.characterPose.remoteReloading = Reloading.Value;
                rig.characterPose.remoteProne = false;
                rig.characterPose.remoteMoving = speed > 0.1f;
            }
        }

        private void Update()
        {
            if (!IsSpawned)
                return;
            UpdateAnimation();
            RecordKillcamSample();
            if (IsServer)
                ServerUpdate();
        }

        private void LateUpdate()
        {
            if (modelDirty && IsSpawned)
                RebuildWeaponModel();
        }

        // ------------------------------------------------------------------
        // Killcam: every peer buffers the bot's pose, like NetworkPlayer does,
        // so a player killed by a bot gets the same replay.
        // ------------------------------------------------------------------

        private const float KillcamKeep = 6f;
        private const float KillcamInterval = 0.05f;
        private readonly List<NetworkPlayer.KillcamSample> killcamBuffer = new List<NetworkPlayer.KillcamSample>(160);
        private float nextKillcamSample;

        private void RecordKillcamSample()
        {
            if (Time.time < nextKillcamSample || IsDead.Value)
                return;
            nextKillcamSample = Time.time + KillcamInterval;
            killcamBuffer.Add(new NetworkPlayer.KillcamSample
            {
                time = Time.time,
                position = transform.position,
                yaw = transform.eulerAngles.y,
                pitch = Pitch.Value,
                eyeHeight = BotRig.EyeHeight,
                eyeForward = 0f,
            });
            while (killcamBuffer.Count > 0 && killcamBuffer[0].time < Time.time - KillcamKeep)
                killcamBuffer.RemoveAt(0);
        }

        public NetworkPlayer.KillcamSample[] GetKillcamHistory() => killcamBuffer.ToArray();

        // ------------------------------------------------------------------
        // Damage in: players' shots hit the bot's hitboxes on their own client
        // (IDamageable) and are routed to the server, like NetworkHealth.
        // ------------------------------------------------------------------

        public void TakeDamage(float amount, bool headshot = false) => TakeDamageShot(amount, headshot, false);

        public void TakeDamageShot(float amount, bool headshot, bool noScope)
        {
            if (!IsSpawned)
                return;
            if (IsServer)
                ServerApplyDamage(amount, headshot, NetworkManager.LocalClientId, noScope);
            else
                DamageRpc(amount, headshot, noScope);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void DamageRpc(float amount, bool headshot, bool noScope, RpcParams rpcParams = default)
        {
            ServerApplyDamage(Mathf.Clamp(amount, 0f, 500f), headshot, rpcParams.Receive.SenderClientId, noScope);
        }

        /// <param name="attackerId">Client id of a player, or another bot's BotId.</param>
        public void ServerApplyDamage(float amount, bool headshot, ulong attackerId, bool noScope = false)
        {
            if (!IsServer || !IsSpawned || IsDead.Value || Hp.Value <= 0f)
                return;

            var gameMode = GameModeManager.Instance;
            if (gameMode != null)
            {
                if (!gameMode.IsMatchActive)
                    return;
                if (attackerId != BotId && gameMode.AreSameTeam(attackerId, BotId))
                    return; // no friendly fire
            }
            if (Time.time < protectedUntil && attackerId != BotId)
                return;

            if (headshot)
                amount = maxHealth; // headshots are always lethal
            Hp.Value = Mathf.Max(0f, Hp.Value - amount);

            if (attackerId != BotId)
                NoticeAttacker(attackerId);

            if (Hp.Value <= 0f)
                ServerDie(attackerId, headshot, noScope);
        }

        private void ServerDie(ulong attackerId, bool headshot, bool noScope)
        {
            IsDead.Value = true;
            Reloading.Value = false;
            Protected.Value = false;
            target = null;
            if (GameModeManager.Instance != null)
                GameModeManager.Instance.ReportKill(attackerId, BotId, headshot, noScope);
            respawnRoutine = StartCoroutine(RespawnAfterDelay());
        }

        private Coroutine respawnRoutine;

        private IEnumerator RespawnAfterDelay()
        {
            yield return new WaitForSeconds(respawnDelay);
            respawnRoutine = null;
            ServerRespawn();
        }

        // ------------------------------------------------------------------
        // Server: spawning, teams, stats
        // ------------------------------------------------------------------

        private float protectedUntil;
        private const float SpawnProtectionSeconds = 2f;

        public void ServerRespawn()
        {
            if (!IsServer)
                return;
            if (respawnRoutine != null)
            {
                StopCoroutine(respawnRoutine); // respawned early (map change / restart while dead)
                respawnRoutine = null;
            }

            var gameMode = GameModeManager.Instance;
            Hp.Value = maxHealth;
            WeaponIndex.Value = PickWeapon(gameMode);
            ammo = CurrentWeapon.magazineSize;
            Reloading.Value = false;
            protectedUntil = Time.time + SpawnProtectionSeconds;
            Protected.Value = true;
            target = null;
            hasLastSeen = false;
            corners.Clear();
            verticalVelocity = 0f;

            Vector3 pos = gameMode != null ? gameMode.GetSpawnPoint(Team.Value) : transform.position;
            Vector3 toCenter = new Vector3(-pos.x, 0f, -pos.z);
            var rotation = toCenter.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toCenter) : Quaternion.identity;
            aimDirection = rotation * Vector3.forward;

            controller.enabled = false;
            if (netTransform != null)
                netTransform.Teleport(pos, rotation, transform.localScale);
            else
                transform.SetPositionAndRotation(pos, rotation);
            lastPosition = pos;
            IsDead.Value = false;
            ApplyDeadState(false); // colliders back on now, not when the change callback runs
            stuckCheckPosition = pos;
            stuckCheckTime = Time.time;
        }

        public void ServerSetTeam(int team)
        {
            if (!IsServer || Team.Value == team)
                return;
            Team.Value = team;
            ServerRespawn();
        }

        public void ServerResetStats()
        {
            if (!IsServer)
                return;
            Kills.Value = 0;
            Deaths.Value = 0;
            KillStreak.Value = 0;
            GunGameLevel.Value = 0;
        }

        /// <summary>Gun Game: the bot climbed the ladder, switch to the next weapon now.</summary>
        public void ServerRefreshWeapon()
        {
            if (!IsServer)
                return;
            int index = PickWeapon(GameModeManager.Instance);
            if (index == WeaponIndex.Value)
                return;
            WeaponIndex.Value = index;
            ammo = CurrentWeapon.magazineSize;
            Reloading.Value = false;
        }

        private int PickWeapon(GameModeManager gameMode)
        {
            if (gameMode != null && gameMode.IsSpawned)
            {
                if (gameMode.CurrentMode == GameMode.GunGame)
                {
                    int level = Mathf.Clamp(GunGameLevel.Value, 0, GameModeManager.GunGameOrder.Length - 1);
                    return GameModeManager.GunGameOrder[level];
                }
                if (gameMode.SniperOnly.Value)
                    return 5;
            }
            // Any rifle / SMG / shotgun / LMG of the arsenal (pack weapons included),
            // snipers less often: they make for slow, campy bots.
            for (int attempt = 0; attempt < 12; attempt++)
            {
                int index = Random.Range(0, Weapons.Length);
                var model = Weapons[index].model;
                if (model == WeaponModelType.Rifle || model == WeaponModelType.Smg
                    || model == WeaponModelType.Shotgun || model == WeaponModelType.Lmg)
                    return index;
                if (model == WeaponModelType.Sniper && Random.value < 0.35f)
                    return index;
            }
            return WeaponDefinition.DefaultIndex;
        }

        // ------------------------------------------------------------------
        // Server: perception
        // ------------------------------------------------------------------

        private struct Combatant
        {
            public Transform root;
            public CharacterController body;
            public ulong id;
            public int team;
        }

        // Shared by every bot, rebuilt once per frame.
        private static readonly List<Combatant> combatants = new List<Combatant>();
        private static int combatantsFrame = -1;
        private static NetworkPlayer[] humanCache = new NetworkPlayer[0];
        private static float humanCacheTime = -10f;

        private static List<Combatant> Combatants()
        {
            if (combatantsFrame == Time.frameCount)
                return combatants;
            combatantsFrame = Time.frameCount;
            combatants.Clear();
            if (Time.time - humanCacheTime > 0.5f)
            {
                humanCache = FindObjectsByType<NetworkPlayer>();
                humanCacheTime = Time.time;
            }
            foreach (var player in humanCache)
            {
                if (player == null || !player.IsSpawned || player.IsDead.Value || player.Spectating.Value)
                    continue;
                combatants.Add(new Combatant
                {
                    root = player.transform,
                    body = player.GetComponent<CharacterController>(),
                    id = player.OwnerClientId,
                    team = player.Team.Value,
                });
            }
            foreach (var bot in All)
            {
                if (bot == null || !bot.Alive)
                    continue;
                combatants.Add(new Combatant { root = bot.transform, body = bot.controller, id = bot.BotId, team = bot.Team.Value });
            }
            return combatants;
        }

        private bool IsEnemy(Combatant c)
        {
            if (c.id == BotId)
                return false;
            var gameMode = GameModeManager.Instance;
            return gameMode == null || !gameMode.IsTeamMode || c.team != Team.Value;
        }

        private static Vector3 AimPointOf(Combatant c)
        {
            // Center of the (crouch/prone-aware) capsule, a bit up toward the chest.
            if (c.body != null)
                return c.body.bounds.center + Vector3.up * Mathf.Min(0.2f, c.body.height * 0.15f);
            return c.root.position + Vector3.up * 1.2f;
        }

        private Vector3 Eye => transform.position + Vector3.up * BotRig.EyeHeight;

        private const float SightRange = 90f;
        private const float HalfFov = 75f;

        private bool CanSee(Combatant c, bool ignoreFov)
        {
            Vector3 eye = Eye;
            Vector3 to = AimPointOf(c) - eye;
            float distance = to.magnitude;
            if (distance > SightRange || distance < 0.01f)
                return false;
            if (!ignoreFov && distance > 5f && Vector3.Angle(aimDirection, to) > HalfFov)
                return false;
            if (RaycastSolid(new Ray(eye, to / distance), distance, out var hit))
                return hit.collider.transform.root == c.root.root;
            return true;
        }

        /// <summary>First solid (non-trigger) hit that isn't this bot.</summary>
        private bool RaycastSolid(Ray ray, float range, out RaycastHit best)
        {
            best = default;
            float bestDistance = float.MaxValue;
            bool found = false;
            foreach (var hit in Physics.RaycastAll(ray, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.root == transform.root || hit.distance >= bestDistance)
                    continue;
                bestDistance = hit.distance;
                best = hit;
                found = true;
            }
            return found;
        }

        /// <summary>Shot ray: hitboxes (triggers) count, so headshots are possible.</summary>
        private bool RaycastShot(Ray ray, float range, out RaycastHit best)
        {
            best = default;
            float bestDistance = float.MaxValue;
            bool found = false;
            foreach (var hit in Physics.RaycastAll(ray, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
            {
                if (hit.collider.transform.root == transform.root || hit.distance >= bestDistance)
                    continue;
                // Triggers that aren't part of a damageable (pads, zones) don't stop bullets.
                if (hit.collider.isTrigger && hit.collider.GetComponentInParent<IDamageable>() == null)
                    continue;
                bestDistance = hit.distance;
                best = hit;
                found = true;
            }
            return found;
        }

        // Current target and memory.
        private Combatant? target;
        private float targetAcquiredTime;
        private float reactionReadyTime;
        private ulong lastTargetId = ulong.MaxValue;
        private float lastTargetLostTime = -10f;
        private bool targetVisible;
        private bool hasLastSeen;
        private Vector3 lastSeenPosition;
        private float lastSeenTime;
        private ulong lastAttackerId = ulong.MaxValue;
        private float attackedTime = -10f;
        private float nextThink;

        private void NoticeAttacker(ulong id)
        {
            lastAttackerId = id;
            attackedTime = Time.time;
            nextThink = 0f; // react right away
        }

        private void Think()
        {
            var list = Combatants();

            // Keep the current target while it stays visible.
            if (target.HasValue)
            {
                Combatant? refreshed = null;
                foreach (var c in list)
                    if (c.id == target.Value.id) { refreshed = c; break; }
                if (refreshed.HasValue && CanSee(refreshed.Value, true))
                {
                    target = refreshed;
                    targetVisible = true;
                    lastSeenPosition = refreshed.Value.root.position;
                    lastSeenTime = Time.time;
                    hasLastSeen = true;
                    return;
                }
                lastTargetId = target.Value.id;
                lastTargetLostTime = Time.time;
                target = null;
                targetVisible = false;
            }

            // Pick the closest visible enemy; whoever just shot us is seen even from behind.
            Combatant? best = null;
            float bestDistance = float.MaxValue;
            foreach (var c in list)
            {
                if (!IsEnemy(c))
                    continue;
                bool hurtByThem = c.id == lastAttackerId && Time.time - attackedTime < 2f;
                if (!CanSee(c, hurtByThem))
                    continue;
                float d = (c.root.position - transform.position).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = c; }
            }
            if (best.HasValue)
            {
                target = best;
                targetVisible = true;
                targetAcquiredTime = Time.time;
                // Reaction time: slower for a brand new target than for one just lost behind cover.
                bool reacquired = best.Value.id == lastTargetId && Time.time - lastTargetLostTime < 2f;
                reactionReadyTime = Time.time + (reacquired ? Random.Range(0.12f, 0.25f) : Random.Range(0.3f, 0.6f));
                lastSeenPosition = best.Value.root.position;
                lastSeenTime = Time.time;
                hasLastSeen = true;
                aimErrorAngle = InitialAimError;
                return;
            }

            // Shot by someone we can't see: go and look where it came from.
            if (Time.time - attackedTime < 3f)
            {
                var source = FindCombatant(list, lastAttackerId);
                if (source.HasValue)
                {
                    lastSeenPosition = source.Value.root.position;
                    lastSeenTime = Time.time;
                    hasLastSeen = true;
                }
            }
        }

        private static Combatant? FindCombatant(List<Combatant> list, ulong id)
        {
            foreach (var c in list)
                if (c.id == id)
                    return c;
            return null;
        }

        // ------------------------------------------------------------------
        // Server: aiming and shooting
        // ------------------------------------------------------------------

        private const float InitialAimError = 7f;   // degrees, right after spotting someone
        private const float SettledAimError = 1.3f; // degrees, after tracking for a while
        private const float AimSettleSeconds = 1.4f;
        private const float TurnSpeed = 230f;       // degrees per second

        private Vector3 aimDirection = Vector3.forward;
        private float aimErrorAngle = InitialAimError;
        private Vector2 aimErrorDir;
        private Vector2 aimErrorGoal;
        private float nextAimErrorRoll;
        private int ammo;
        private float reloadDoneTime;
        private float nextShotTime;
        private float burstEndTime;
        private Vector3 lastTargetPosition;

        private void UpdateAim(float dt, Vector3 moveDirection)
        {
            Vector3 desired;
            if (target.HasValue && targetVisible)
            {
                var c = target.Value;
                Vector3 to = AimPointOf(c) - Eye;

                // Error shrinks while tracking, grows with the target's sideways speed.
                Vector3 targetVelocity = (c.root.position - lastTargetPosition) / Mathf.Max(dt, 0.001f);
                lastTargetPosition = c.root.position;
                float lateral = Vector3.ProjectOnPlane(Vector3.ClampMagnitude(targetVelocity, 12f), to.normalized).magnitude;
                float settle = Mathf.Clamp01((Time.time - targetAcquiredTime) / AimSettleSeconds);
                float goalError = Mathf.Lerp(InitialAimError, SettledAimError, settle) + lateral * 0.35f;
                if (CurrentWeapon.model == WeaponModelType.Sniper)
                    goalError *= 0.7f;
                aimErrorAngle = Mathf.MoveTowards(aimErrorAngle, goalError, 12f * dt);

                if (Time.time >= nextAimErrorRoll)
                {
                    nextAimErrorRoll = Time.time + Random.Range(0.2f, 0.45f);
                    aimErrorGoal = Random.insideUnitCircle;
                }
                aimErrorDir = Vector2.Lerp(aimErrorDir, aimErrorGoal, 1f - Mathf.Exp(-6f * dt));

                var look = Quaternion.LookRotation(to);
                desired = look * Quaternion.Euler(-aimErrorDir.y * aimErrorAngle, aimErrorDir.x * aimErrorAngle, 0f) * Vector3.forward;
            }
            else if (Time.time - attackedTime < 1.5f && FindCombatant(Combatants(), lastAttackerId) is Combatant source)
            {
                desired = (source.root.position - transform.position).normalized; // turn toward the shooter
                desired.y = 0f;
            }
            else if (moveDirection.sqrMagnitude > 0.01f)
            {
                desired = moveDirection;
                desired.y = 0f;
            }
            else
            {
                desired = aimDirection;
                desired.y = 0f;
            }
            if (desired.sqrMagnitude < 0.0001f)
                return;

            aimDirection = Vector3.RotateTowards(aimDirection, desired.normalized, TurnSpeed * Mathf.Deg2Rad * dt, 1f).normalized;

            Vector3 flat = new Vector3(aimDirection.x, 0f, aimDirection.z);
            if (flat.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(flat);
            float pitch = -Mathf.Asin(Mathf.Clamp(aimDirection.y, -1f, 1f)) * Mathf.Rad2Deg;
            if (Mathf.Abs(Pitch.Value - pitch) > 0.5f)
                Pitch.Value = pitch;
        }

        private static float MaxFireDistance(WeaponDefinition w)
        {
            float limit;
            switch (w.model)
            {
                case WeaponModelType.Knife: limit = 2.2f; break;
                case WeaponModelType.Shotgun: limit = 22f; break;
                case WeaponModelType.Pistol: limit = 35f; break;
                case WeaponModelType.Smg: limit = 40f; break;
                case WeaponModelType.Sniper: limit = 120f; break;
                case WeaponModelType.GrenadeLauncher: limit = 45f; break;
                default: limit = 70f; break;
            }
            return Mathf.Min(limit, w.range);
        }

        private void UpdateShooting()
        {
            var weapon = CurrentWeapon;
            if (Reloading.Value)
            {
                if (Time.time < reloadDoneTime)
                    return;
                Reloading.Value = false;
                ammo = weapon.magazineSize;
            }
            if (!target.HasValue || !targetVisible || Time.time < reactionReadyTime || Time.time < nextShotTime)
                return;

            var c = target.Value;
            Vector3 to = AimPointOf(c) - Eye;
            float distance = to.magnitude;
            if (distance > MaxFireDistance(weapon))
                return;
            // Only pull the trigger once the gun points (roughly) at them.
            if (Vector3.Angle(aimDirection, to) > aimErrorAngle + (weapon.isMelee ? 25f : 3f))
                return;

            // Splash weapons: never fire into a wall right in front of our face.
            if (weapon.isProjectile && RaycastSolid(new Ray(Eye, aimDirection), 4f, out _))
                return;

            // Automatic weapons fire in bursts with a short pause in between.
            if (weapon.automatic && !weapon.isMelee && Time.time > burstEndTime)
            {
                if (burstEndTime > 0f)
                {
                    nextShotTime = Time.time + Random.Range(0.25f, 0.55f);
                    burstEndTime = 0f;
                    return;
                }
                burstEndTime = Time.time + Random.Range(0.35f, 0.9f);
            }

            Fire(weapon);
        }

        private void Fire(WeaponDefinition weapon)
        {
            float interval = 1f / Mathf.Max(0.3f, weapon.fireRate);
            if (!weapon.automatic || weapon.isMelee)
            {
                interval *= Random.Range(1.15f, 1.6f);
                if (weapon.model == WeaponModelType.Sniper)
                    interval = Mathf.Max(interval, 1.1f);
            }
            nextShotTime = Time.time + interval;

            protectedUntil = 0f; // shooting ends spawn protection, like for players
            if (Protected.Value)
                Protected.Value = false;

            Vector3 eye = Eye;
            Vector3 endPoint = eye + aimDirection * weapon.range;

            if (weapon.isProjectile)
            {
                float range = Mathf.Min(weapon.range, 120f);
                Vector3 impact = RaycastShot(new Ray(eye, aimDirection), range, out var hit) ? hit.point : eye + aimDirection * range;
                endPoint = impact;
                float delay = Vector3.Distance(eye, impact) / Mathf.Max(1f, weapon.projectileSpeed);
                StartCoroutine(ServerSplash(impact, delay, weapon.damage * damageScale, Mathf.Max(1f, weapon.explosionRadius)));
            }
            else
            {
                int pellets = Mathf.Max(1, weapon.pellets);
                for (int p = 0; p < pellets; p++)
                {
                    Vector3 dir = aimDirection;
                    if (weapon.spreadDegrees > 0f && pellets > 1)
                    {
                        Vector2 spread = Random.insideUnitCircle * Mathf.Tan(weapon.spreadDegrees * Mathf.Deg2Rad);
                        dir = (aimDirection + transform.right * spread.x + transform.up * spread.y).normalized;
                    }
                    Vector3 pelletEnd = eye + dir * weapon.range;
                    if (RaycastShot(new Ray(eye, dir), weapon.range, out var hit))
                    {
                        pelletEnd = hit.point;
                        var hitbox = hit.collider.GetComponent<Hitbox>();
                        ApplyHit(hit.collider, weapon.damage * damageScale, hitbox != null && hitbox.isHead);
                    }
                    if (p == 0)
                        endPoint = pelletEnd;
                }
            }

            FireClientRpc(endPoint);

            if (!weapon.isMelee && weapon.magazineSize > 0)
            {
                ammo--;
                if (ammo <= 0)
                    StartReload(weapon);
            }
        }

        private void ApplyHit(Collider collider, float damage, bool headshot)
        {
            var health = collider.GetComponentInParent<NetworkHealth>();
            if (health != null)
            {
                health.ServerBotDamage(damage, headshot, this);
                return;
            }
            var bot = collider.GetComponentInParent<BotAgent>();
            if (bot != null && bot != this)
                bot.ServerApplyDamage(damage, headshot, BotId);
        }

        private IEnumerator ServerSplash(Vector3 point, float delay, float damage, float radius)
        {
            yield return new WaitForSeconds(Mathf.Min(delay, 4f));
            // Closest collider of each target decides its falloff; each target is hit once.
            var closest = new Dictionary<Component, float>();
            foreach (var col in Physics.OverlapSphere(point, radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
            {
                Component victim = col.GetComponentInParent<NetworkHealth>();
                if (victim == null)
                    victim = col.GetComponentInParent<BotAgent>();
                if (victim == null)
                    continue;
                float distance = Vector3.Distance(col.bounds.ClosestPoint(point), point);
                if (!closest.TryGetValue(victim, out float previous) || distance < previous)
                    closest[victim] = distance;
            }
            foreach (var pair in closest)
            {
                float amount = damage * (Mathf.Clamp01(1f - pair.Value / radius) * 0.75f + 0.25f);
                if (pair.Key is NetworkHealth health)
                    health.ServerExplosionDamage(amount, BotId, point);
                else if (pair.Key is BotAgent bot)
                    bot.ServerApplyDamage(amount, false, BotId);
            }
        }

        private void StartReload(WeaponDefinition weapon)
        {
            Reloading.Value = true;
            reloadDoneTime = Time.time + Mathf.Max(0.3f, weapon.reloadTime);
            ReloadClientRpc();
        }

        [ClientRpc]
        private void FireClientRpc(Vector3 endPoint)
        {
            LastShotTime = Time.time;
            var weapon = CurrentWeapon;
            Vector3 from = hasWeaponModel && weaponModel.muzzle != null
                ? weaponModel.muzzle.position
                : transform.position + Vector3.up * 1.4f;

            if (!weapon.isMelee)
                WeaponController.SpawnTracerLine(from, endPoint);
            SfxSynth.PlayAt(SfxSynth.Shot(weapon.model, weapon.integralSuppressor), from, 0.8f, weapon.shotPitch);

            if (weapon.isProjectile)
                StartCoroutine(RemoteExplosion(endPoint, Vector3.Distance(from, endPoint) / Mathf.Max(1f, weapon.projectileSpeed)));
        }

        [ClientRpc]
        private void ReloadClientRpc()
        {
            var weapon = CurrentWeapon;
            Vector3 at = hasWeaponModel ? weaponModel.root.transform.position : transform.position + Vector3.up * 1.3f;
            SfxSynth.PlayReloadAt(weapon.model, weapon.reloadTime, at, 0.7f);
        }

        private IEnumerator RemoteExplosion(Vector3 point, float delay)
        {
            yield return new WaitForSeconds(Mathf.Min(delay, 4f));
            Effects.SpawnExplosion(point);
            SfxSynth.PlayAt(SfxSynth.Explosion(), point, 1f);
        }

        // ------------------------------------------------------------------
        // Server: movement
        // ------------------------------------------------------------------

        private const float WalkSpeed = 4.4f;
        private const float RunSpeed = 6.8f;
        private const float Gravity = -20f;
        private const float JumpVelocity = 6.9f; // ~1.2 m, like players

        private readonly NavMeshPath navPath = new NavMeshPath();
        private readonly List<Vector3> corners = new List<Vector3>();
        private int cornerIndex;
        private Vector3 pathGoal;
        private float nextRepath;
        private Vector3 huntGoal;
        private float nextHuntGoal;
        private float verticalVelocity;
        private float strafeSign = 1f;
        private float nextStrafeFlip;
        private Vector3 stuckCheckPosition;
        private float stuckCheckTime;
        private Vector3 detour;
        private float detourUntil;

        private static float PreferredRange(WeaponDefinition w)
        {
            switch (w.model)
            {
                case WeaponModelType.Knife: return 1.2f;
                case WeaponModelType.Shotgun: return 6f;
                case WeaponModelType.Smg: return 12f;
                case WeaponModelType.Pistol: return 14f;
                case WeaponModelType.Sniper: return 35f;
                case WeaponModelType.Rpg:
                case WeaponModelType.GrenadeLauncher: return 18f;
                default: return 22f;
            }
        }

        private void ServerUpdate()
        {
            var gameMode = GameModeManager.Instance;
            if (needsFirstRespawn)
            {
                needsFirstRespawn = false;
                ServerRespawn();
            }
            if (IsDead.Value)
                return;
            if (Protected.Value && Time.time >= protectedUntil)
                Protected.Value = false;

            // Fell out of the map (open plane ramp, void): counts as a suicide.
            if (transform.position.y < -12f)
            {
                ServerApplyDamage(maxHealth * 10f, false, BotId);
                return;
            }

            bool playing = gameMode == null || gameMode.IsMatchActive;
            if (!playing)
            {
                target = null;
                ApplyMovement(Vector3.zero, 0f, Time.deltaTime);
                return;
            }

            if (Time.time >= nextThink)
            {
                nextThink = Time.time + 0.15f;
                Think();
            }

            float dt = Time.deltaTime;
            Vector3 move = ChooseMovement(out float speed);
            UpdateAim(dt, move);
            UpdateShooting();
            ApplyMovement(move, speed, dt);
        }

        private Vector3 ChooseMovement(out float speed)
        {
            var weapon = CurrentWeapon;
            float mobility = Mathf.Clamp(weapon.mobility, 0.5f, 1.2f);
            speed = WalkSpeed * mobility;

            if (Time.time < detourUntil)
                return detour;

            if (target.HasValue && targetVisible)
            {
                Vector3 to = target.Value.root.position - transform.position;
                to.y = 0f;
                float distance = to.magnitude;
                Vector3 toDir = distance > 0.01f ? to / distance : transform.forward;
                float preferred = PreferredRange(weapon);

                if (Time.time >= nextStrafeFlip)
                {
                    nextStrafeFlip = Time.time + Random.Range(0.6f, 1.5f);
                    strafeSign = Random.value < 0.5f ? -1f : 1f;
                }
                Vector3 strafe = Vector3.Cross(Vector3.up, toDir) * strafeSign;

                if (distance > preferred * 1.3f)
                {
                    Vector3 approach = PathDirection(target.Value.root.position);
                    if (approach.sqrMagnitude < 0.01f)
                        approach = toDir;
                    return (approach + strafe * 0.35f).normalized;
                }
                if (distance < preferred * 0.55f && !weapon.isMelee)
                    return (-toDir + strafe * 0.6f).normalized;
                speed *= 0.75f;
                return strafe;
            }

            // Nobody in sight: check where we last saw / heard someone, else hunt.
            Vector3 goal;
            if (hasLastSeen && Time.time - lastSeenTime < 6f)
            {
                goal = lastSeenPosition;
                if ((goal - transform.position).sqrMagnitude < 2f)
                    hasLastSeen = false;
            }
            else
            {
                if (Time.time >= nextHuntGoal || (huntGoal - transform.position).sqrMagnitude < 4f)
                    PickHuntGoal();
                goal = huntGoal;
                speed = RunSpeed * mobility;
            }
            return PathDirection(goal);
        }

        /// <summary>Roam toward a random enemy's area (near, not exactly on them).</summary>
        private void PickHuntGoal()
        {
            nextHuntGoal = Time.time + Random.Range(6f, 10f);
            var enemies = new List<Vector3>();
            foreach (var c in Combatants())
                if (IsEnemy(c))
                    enemies.Add(c.root.position);
            Vector3 center = enemies.Count > 0
                ? enemies[Random.Range(0, enemies.Count)]
                : new Vector3(0f, transform.position.y, 0f);
            huntGoal = BotNavigation.RandomPoint(center, 6f, out var point) ? point : center;
        }

        /// <summary>Direction to walk now to reach <paramref name="goal"/>: along the NavMesh path when there is one.</summary>
        private Vector3 PathDirection(Vector3 goal)
        {
            if (Time.time >= nextRepath || (goal - pathGoal).sqrMagnitude > 4f)
            {
                nextRepath = Time.time + 1f;
                pathGoal = goal;
                cornerIndex = 1;
                if (!BotNavigation.TryPath(transform.position, goal, navPath, corners))
                    corners.Clear();
            }

            Vector3 next = goal;
            if (corners.Count > 0)
            {
                while (cornerIndex < corners.Count - 1 && FlatDistance(transform.position, corners[cornerIndex]) < 0.6f)
                    cornerIndex++;
                if (cornerIndex < corners.Count)
                    next = corners[cornerIndex];
            }

            Vector3 dir = next - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.04f)
                return Vector3.zero;
            dir.Normalize();
            return corners.Count > 0 ? dir : Steer(dir);
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f; b.y = 0f;
            return Vector3.Distance(a, b);
        }

        /// <summary>No NavMesh: probe ahead and turn away from walls.</summary>
        private Vector3 Steer(Vector3 dir)
        {
            float[] angles = { 0f, 35f, -35f, 70f, -70f, 110f, -110f };
            foreach (float angle in angles)
            {
                Vector3 d = Quaternion.Euler(0f, angle, 0f) * dir;
                if (!Blocked(d, 1.6f) && GroundAhead(d))
                    return d;
            }
            return Vector3.zero;
        }

        private bool Blocked(Vector3 dir, float distance)
        {
            Vector3 origin = transform.position + Vector3.up * 0.9f;
            return Physics.SphereCast(origin, 0.3f, dir, out var hit, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && hit.collider.transform.root != transform.root;
        }

        /// <summary>Ground within a short drop in that direction (don't walk off the plane's ramp or a roof).</summary>
        private bool GroundAhead(Vector3 dir)
        {
            Vector3 probe = transform.position + dir * 0.8f + Vector3.up * 0.6f;
            return Physics.Raycast(probe, Vector3.down, 2.4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        private void ApplyMovement(Vector3 move, float speed, float dt)
        {
            if (!controller.enabled)
                controller.enabled = true;

            if (move.sqrMagnitude > 0.01f && !GroundAhead(move))
            {
                move = Vector3.zero;
                strafeSign = -strafeSign;
                nextRepath = 0f;
            }

            if (controller.isGrounded && verticalVelocity < 0f)
                verticalVelocity = -2f;
            verticalVelocity += Gravity * dt;

            Vector3 velocity = move * speed;
            velocity.y = verticalVelocity;
            controller.Move(velocity * dt);

            // Stuck against something: hop, then take a short sideways detour.
            if (move.sqrMagnitude > 0.01f && Time.time - stuckCheckTime > 1.2f)
            {
                if (FlatDistance(transform.position, stuckCheckPosition) < 0.5f)
                {
                    if (controller.isGrounded)
                        verticalVelocity = JumpVelocity;
                    detour = Quaternion.Euler(0f, Random.value < 0.5f ? 90f : -90f, 0f) * move;
                    detourUntil = Time.time + Random.Range(0.6f, 1.1f);
                    nextRepath = 0f;
                }
                stuckCheckPosition = transform.position;
                stuckCheckTime = Time.time;
            }
            else if (move.sqrMagnitude <= 0.01f)
            {
                stuckCheckPosition = transform.position;
                stuckCheckTime = Time.time;
            }
        }
    }
}
