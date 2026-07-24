using System.Collections;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// The multiplayer brain of the player prefab: owner setup (input, camera,
    /// HUD), team colors and switching, crouch replication, per-mode weapon
    /// locking (Gun Game progression / sniper only), death with kill cam,
    /// respawn with fresh ammo, and K/D stats for the scoreboard.
    /// </summary>
    [RequireComponent(typeof(PlayerRigRefs))]
    public class NetworkPlayer : NetworkBehaviour
    {
        public NetworkVariable<int> Team = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<bool> IsDead = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> Kills = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> Deaths = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> GunGameLevel = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> KillStreak = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> CareerLevel = new NetworkVariable<int>(
            1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<FixedString64Bytes> PlayerName = new NetworkVariable<FixedString64Bytes>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<bool> Crouched = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        /// <summary>View pitch in degrees so remote players see vertical aim.</summary>
        public NetworkVariable<float> Pitch = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<bool> Sliding = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<bool> Prone = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<bool> Spectating = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public float respawnDelay = 3f;

        private Nametag nametag;
        private PlayerRigRefs rig;
        private NetworkHealth health;
        private NetworkWeapon networkWeapon;
        private NetworkTransform netTransform;
        private CharacterController characterController;
        private float remoteCrouchBlend;
        private bool respawning;

        private void Awake()
        {
            rig = GetComponent<PlayerRigRefs>();
            health = GetComponent<NetworkHealth>();
            networkWeapon = GetComponent<NetworkWeapon>();
            netTransform = GetComponent<NetworkTransform>();
            characterController = GetComponent<CharacterController>();
            if (GetComponent<ThrowableController>() == null)
            {
                var throwables = gameObject.AddComponent<ThrowableController>();
                throwables.viewCamera = rig.playerCamera;
                throwables.ownerRoot = transform;
            }

            // Everything input/camera related stays off until we know the owner.
            rig.movement.enabled = false;
            rig.weaponController.enabled = false;
            rig.cameraObject.SetActive(false);
        }

        
        private readonly NetworkVariable<Vector2> NetworkMoveInput =
            new(
                Vector2.zero,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Owner
            );

        private readonly NetworkVariable<float> NetworkMoveSpeed =
            new(
                0f,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Owner
            );

        private readonly NetworkVariable<float> NetworkVerticalSpeed =
            new(
                0f,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Owner
            );

        private readonly NetworkVariable<bool> NetworkGrounded =
            new(
                true,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Owner
            );

        private readonly NetworkVariable<bool> NetworkSprinting =
            new(
                false,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Owner
            );

        private readonly NetworkVariable<bool> NetworkReloading =
            new(
                false,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Owner
            );

        private readonly NetworkVariable<bool> NetworkAiming =
            new(
                false,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Owner
            );
        
        
        public override void OnNetworkSpawn()
        {
            Team.OnValueChanged += OnTeamChanged;
            IsDead.OnValueChanged += OnDeadChanged;
            PlayerName.OnValueChanged += OnNameChanged;

            // Give each player an imported character skin by join order
            // (client 0 → skin 1 … client 4 → skin 1, wrapping). Visual only —
            // hitboxes are unchanged.
            rig.ApplyCharacterSkin((int)OwnerClientId);

            ApplyTeamColor(Team.Value);

            if (IsServer)
            {
                if (GameModeManager.Instance != null)
                    Team.Value = GameModeManager.Instance.AssignTeam(OwnerClientId);
                ServerRespawn(); // put the new player on their team's side
            }

            if (!IsOwner)
            {
                nametag = Nametag.Create(transform, PlayerName.Value.ToString(), CurrentTeamColor);
            }

            if (IsOwner)
            {
                PlayerName.Value = GameSettings.PlayerName;
                CareerLevel.Value = GameSettings.Level;
                rig.cameraObject.SetActive(true);
                rig.movement.enabled = true;
                rig.weaponController.enabled = true;
                rig.SetFirstPerson(true);

                var hud = gameObject.AddComponent<HudOverlay>();
                hud.weaponController = rig.weaponController;
                hud.HealthSource = health;

                if (MultiplayerBootstrap.Instance != null)
                    MultiplayerBootstrap.Instance.SetMenuCamera(false);
                MouseLook.LockCursor(true);
            }
        }

        public override void OnNetworkDespawn()
        {
            Team.OnValueChanged -= OnTeamChanged;
            IsDead.OnValueChanged -= OnDeadChanged;
            PlayerName.OnValueChanged -= OnNameChanged;

            if (IsOwner)
            {
                DeathCam.End();
                if (MultiplayerBootstrap.Instance != null)
                    MultiplayerBootstrap.Instance.SetMenuCamera(true);
                MouseLook.LockCursor(false);
            }
        }

        private void Update()
        {
            if (!IsSpawned)
                return;

            RecordKillcamSample();
            
            if (!IsOwner)
            {
                if (rig.characterAnimator != null)
                {
                    rig.characterAnimator.useNetworkState = true;
                    rig.characterAnimator.SetNetworkAnimationState(
                        NetworkMoveInput.Value,
                        NetworkMoveSpeed.Value,
                        NetworkGrounded.Value,
                        NetworkVerticalSpeed.Value,
                        Crouched.Value,
                        NetworkSprinting.Value,
                        Sliding.Value,
                        NetworkReloading.Value,
                        NetworkAiming.Value);
                }

                // Preserve the primitive-character crouch squash, but do not
                // distort an imported animated character unless explicitly enabled.
                remoteCrouchBlend = Mathf.MoveTowards(remoteCrouchBlend, Crouched.Value ? 1f : 0f, 8f * Time.deltaTime);
                bool useLegacyCrouchVisual = rig.characterAnimator == null
                    || (rig.movement != null && rig.movement.useProceduralBodyCrouch);
                if (useLegacyCrouchVisual)
                    PlayerMovement.ApplyCrouchVisual(rig.bodyRoot, remoteCrouchBlend, Prone.Value);

                // Feed replicated aim pitch, slide and reload into the body pose.
                if (rig.characterPose != null)
                {
                    rig.characterPose.remoteDriven = true;
                    rig.characterPose.remotePitch = Pitch.Value;
                    rig.characterPose.remoteSlide = Sliding.Value ? 1f : 0f;
                    rig.characterPose.remoteReloading = NetworkReloading.Value;
                }
                return;
            }

            if (rig.characterAnimator != null)
                rig.characterAnimator.useNetworkState = false;

            // Publish crouch / slide / aim pitch.
            if (Crouched.Value != rig.movement.IsCrouching)
                Crouched.Value = rig.movement.IsCrouching;
            if (Sliding.Value != rig.movement.IsSliding)
                Sliding.Value = rig.movement.IsSliding;
            if (Prone.Value != rig.movement.IsProne)
                Prone.Value = rig.movement.IsProne;
            float pitch = rig.mouseLook != null ? rig.mouseLook.CurrentPitch : 0f;
            if (Mathf.Abs(Pitch.Value - pitch) > 0.5f)
                Pitch.Value = pitch;

            var gameMode = GameModeManager.Instance;

            // Freeze input while dead, between matches, or during a respawn teleport
            // (the respawn coroutine holds control until the map is built locally).
            bool canPlay = !Spectating.Value && !IsDead.Value && !respawning && (gameMode == null || gameMode.IsMatchActive);
            if (rig.movement.enabled != canPlay)
                rig.movement.enabled = canPlay;
            if (rig.weaponController.enabled != canPlay)
                rig.weaponController.enabled = canPlay;

            if (rig.movement != null && rig.weaponController != null)
            {
                Vector2 moveInput = canPlay ? rig.movement.MoveInput : Vector2.zero;
                float moveSpeed = canPlay ? rig.movement.CurrentHorizontalSpeed : 0f;
                float verticalSpeed = canPlay ? rig.movement.VerticalSpeed : 0f;
                bool grounded = canPlay ? rig.movement.IsGrounded : true;
                bool sprinting = canPlay && rig.movement.IsSprinting;
                bool reloading = canPlay && rig.weaponController.IsReloading;
                bool aiming = canPlay && rig.weaponController.IsZoomed;

                bool stoppedMoving = moveInput == Vector2.zero
                    && NetworkMoveInput.Value != Vector2.zero;
                if (stoppedMoving
                    || Vector2.Distance(NetworkMoveInput.Value, moveInput) > 0.01f)
                    NetworkMoveInput.Value = moveInput;
                bool stoppedSpeed = moveSpeed == 0f && NetworkMoveSpeed.Value != 0f;
                if (stoppedSpeed
                    || Mathf.Abs(NetworkMoveSpeed.Value - moveSpeed) > 0.05f)
                    NetworkMoveSpeed.Value = moveSpeed;
                bool stoppedVertically = verticalSpeed == 0f
                    && NetworkVerticalSpeed.Value != 0f;
                if (stoppedVertically
                    || Mathf.Abs(NetworkVerticalSpeed.Value - verticalSpeed) > 0.05f)
                    NetworkVerticalSpeed.Value = verticalSpeed;
                if (NetworkGrounded.Value != grounded)
                    NetworkGrounded.Value = grounded;
                if (NetworkSprinting.Value != sprinting)
                    NetworkSprinting.Value = sprinting;
                if (NetworkReloading.Value != reloading)
                    NetworkReloading.Value = reloading;
                if (NetworkAiming.Value != aiming)
                    NetworkAiming.Value = aiming;
            }

            // Per-mode weapon locking. Gun Game's whole point is its weapon
            // ladder, so it wins over the sniper-only toggle.
            if (gameMode != null && gameMode.IsSpawned)
            {
                if (gameMode.SniperOnly.Value && gameMode.CurrentMode != GameMode.GunGame)
                {
                    rig.weaponController.lockSwitching = true;
                    rig.weaponController.ForceWeapon(5); // sniper
                }
                else if (gameMode.CurrentMode == GameMode.GunGame)
                {
                    rig.weaponController.lockSwitching = true;
                    int level = Mathf.Clamp(GunGameLevel.Value, 0, GameModeManager.GunGameOrder.Length - 1);
                    rig.weaponController.ForceWeapon(GameModeManager.GunGameOrder[level]);
                }
                else
                {
                    rig.weaponController.lockSwitching = false;
                }
            }
        }

        // ------------------------------------------------------------------
        // Killcam recording — every client buffers every player's recent pose
        // (position is already replicated; pitch via the Pitch variable), so the
        // victim can replay its killer's last seconds from the killer's eyes.
        // ------------------------------------------------------------------

        public struct KillcamSample
        {
            public float time;
            public Vector3 position;
            public float yaw;
            public float pitch;
        }

        private const float KillcamKeep = 6f;      // seconds of history
        private const float KillcamInterval = 0.05f; // 20 Hz
        private readonly System.Collections.Generic.List<KillcamSample> killcamBuffer
            = new System.Collections.Generic.List<KillcamSample>(160);
        private float nextKillcamSample;

        private void RecordKillcamSample()
        {
            if (Time.time < nextKillcamSample || IsDead.Value)
                return;
            nextKillcamSample = Time.time + KillcamInterval;

            killcamBuffer.Add(new KillcamSample
            {
                time = Time.time,
                position = transform.position,
                yaw = transform.eulerAngles.y,
                pitch = IsOwner && rig.mouseLook != null ? rig.mouseLook.CurrentPitch : Pitch.Value,
            });
            while (killcamBuffer.Count > 0 && killcamBuffer[0].time < Time.time - KillcamKeep)
                killcamBuffer.RemoveAt(0);
        }

        /// <summary>Snapshot of this player's recent movement (for the victim's killcam).</summary>
        public KillcamSample[] GetKillcamHistory() => killcamBuffer.ToArray();

        // ------------------------------------------------------------------
        // Team
        // ------------------------------------------------------------------

        private Color CurrentTeamColor => EnvironmentBuilder.TeamColor(Team.Value);

        private void OnTeamChanged(int previous, int next) => ApplyTeamColor(next);

        private void ApplyTeamColor(int team)
        {
            rig.ApplyTeamColor(EnvironmentBuilder.TeamColor(team));
            if (nametag != null)
                nametag.SetColor(CurrentTeamColor);
        }

        private void OnNameChanged(FixedString64Bytes previous, FixedString64Bytes next)
        {
            if (nametag != null)
                nametag.SetText(next.ToString());
        }

        /// <summary>Called from the pause menu (team modes only; server validates balance).</summary>
        public void RequestTeamChange() => ChangeTeamServerRpc();

        public void RequestSpectatorToggle() => SpectatorServerRpc(!Spectating.Value);

        [ServerRpc]
        private void SpectatorServerRpc(bool spectate)
        {
            Spectating.Value = spectate;
            if (spectate) IsDead.Value = true;
            else ServerRespawn();
        }

        [ClientRpc]
        public void AwardXpClientRpc(int amount, bool playStreakSound, int streak,
            ClientRpcParams rpcParams = default)
        {
            if (!IsOwner) return;
            GameSettings.AwardExperience(amount);
            CareerLevel.Value = GameSettings.Level;
            if (playStreakSound) SfxSynth.Play2D(SfxSynth.Killstreak(streak), 0.9f);
        }

        [ServerRpc]
        private void ChangeTeamServerRpc()
        {
            if (GameModeManager.Instance != null)
                GameModeManager.Instance.ServerTryChangeTeam(this);
        }

        // ------------------------------------------------------------------
        // Death, kill cam & respawn (server-driven)
        // ------------------------------------------------------------------

        private void OnDeadChanged(bool previous, bool dead)
        {
            rig.SetVisible(!dead);
            if (IsOwner && !dead)
                rig.SetFirstPerson(true); // SetVisible re-enabled the head parts
            if (networkWeapon != null)
                networkWeapon.SetThirdPersonVisible(!dead);
            if (nametag != null)
                nametag.gameObject.SetActive(!dead);

            if (dead && !previous)
            {
                Effects.SpawnDeathBurst(transform.position + Vector3.up * 1.1f, CurrentTeamColor);
                SfxSynth.PlayAt(SfxSynth.Death(), transform.position, 0.9f);
            }
        }

        /// <summary>Called by NetworkHealth on the server when HP hits zero.</summary>
        public void ServerDie(ulong attackerId, bool headshot, bool noScope = false)
        {
            if (!IsServer || IsDead.Value)
                return;

            IsDead.Value = true;
            if (GameModeManager.Instance != null)
                GameModeManager.Instance.ReportKill(attackerId, OwnerClientId, headshot, noScope);

            DeathCamClientRpc(attackerId, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } },
            });
            StartCoroutine(RespawnAfterDelay());
        }

        [ClientRpc]
        private void DeathCamClientRpc(ulong killerId, ClientRpcParams rpcParams = default)
        {
            if (killerId == OwnerClientId)
                return; // no cam for suicides

            foreach (var player in FindObjectsByType<NetworkPlayer>())
            {
                if (player.OwnerClientId == killerId)
                {
                    string killerName = player.PlayerName.Value.IsEmpty
                        ? $"Player {killerId + 1}" : player.PlayerName.Value.ToString();
                    // Replay the killer's last seconds from their eyes (skippable),
                    // then fall back to the orbiting spectate until respawn.
                    DeathCam.BeginReplay(player.GetKillcamHistory(), player.transform,
                        $"KILLCAM  —  {killerName}");
                    return;
                }
            }
        }

        private IEnumerator RespawnAfterDelay()
        {
            yield return new WaitForSeconds(respawnDelay);
            ServerRespawn();
        }

        /// <summary>Server-side anti-spawnkill: brief immunity after spawning, ends when you shoot.</summary>
        public bool IsSpawnProtected => Time.time < spawnProtectedUntil;
        private float spawnProtectedUntil;
        private const float SpawnProtectionSeconds = 3f;

        public void ServerClearSpawnProtection() => spawnProtectedUntil = 0f;

        public void ServerRespawn()
        {
            if (!IsServer)
                return;

            health.ServerResetHealth();
            IsDead.Value = false;
            spawnProtectedUntil = Time.time + SpawnProtectionSeconds;

            Vector3 pos = GameModeManager.Instance != null
                ? GameModeManager.Instance.GetSpawnPoint(Team.Value)
                : new Vector3(0, 0.1f, -20f);

            // Face the middle of the map (works whatever axis the map spawns on).
            Vector3 toCenter = new Vector3(-pos.x, 0f, -pos.z);
            float yaw = toCenter.sqrMagnitude > 0.01f
                ? Quaternion.LookRotation(toCenter).eulerAngles.y
                : 0f;

            RespawnClientRpc(pos, yaw, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } },
            });
        }

        /// <summary>Runs only on the owner — with client-authoritative movement, the owner must do the teleport.</summary>
        [ClientRpc]
        private void RespawnClientRpc(Vector3 position, float yaw, ClientRpcParams rpcParams = default)
        {
            StartCoroutine(RespawnRoutine(position, yaw));
        }

        private IEnumerator RespawnRoutine(Vector3 position, float yaw)
        {
            DeathCam.End();

            // Hold the player frozen until the correct map is actually built on
            // THIS client — otherwise a joining player teleports before the ground
            // exists and falls through the world (respawning into the void forever).
            respawning = true;
            characterController.enabled = false;

            float timeout = Time.time + 6f;
            while (!LocalMapReady() && Time.time < timeout)
                yield return null;

            var rotation = Quaternion.Euler(0, yaw, 0);
            if (netTransform != null)
                netTransform.Teleport(position, rotation, transform.localScale);
            else
            {
                transform.position = position;
                transform.rotation = rotation;
            }
            characterController.enabled = true;
            rig.movement.spawnPoint = position; // fall-out-of-world safety respawn
            rig.weaponController.ResetAmmo();    // fresh magazines every life
            rig.weaponController.ApplySelectedClass(); // spawn with the chosen class
            respawning = false;
        }

        /// <summary>True once this client has finished building the map the match is on.</summary>
        private bool LocalMapReady()
        {
            var boot = MultiplayerBootstrap.Instance;
            var gameMode = GameModeManager.Instance;
            if (boot == null || gameMode == null || !gameMode.IsSpawned)
                return false;
            return boot.CurrentMap == gameMode.MapIndex.Value
                && GameObject.Find(EnvironmentBuilder.MapRootName) != null;
        }
    }
}
