using System.Collections;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// The multiplayer brain of the player prefab. Decides what runs locally
    /// (input, camera, HUD — owner only), applies team colors, and handles
    /// server-side death and respawning.
    /// </summary>
    [RequireComponent(typeof(PlayerRigRefs))]
    public class NetworkPlayer : NetworkBehaviour
    {
        public NetworkVariable<int> Team = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<bool> IsDead = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<FixedString64Bytes> PlayerName = new NetworkVariable<FixedString64Bytes>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public float respawnDelay = 3f;

        private Nametag nametag;
        private PlayerRigRefs rig;
        private NetworkHealth health;
        private NetworkWeapon networkWeapon;
        private NetworkTransform netTransform;
        private CharacterController characterController;

        private void Awake()
        {
            rig = GetComponent<PlayerRigRefs>();
            health = GetComponent<NetworkHealth>();
            networkWeapon = GetComponent<NetworkWeapon>();
            netTransform = GetComponent<NetworkTransform>();
            characterController = GetComponent<CharacterController>();

            // Everything input/camera related stays off until we know the owner.
            rig.movement.enabled = false;
            rig.weaponController.enabled = false;
            rig.cameraObject.SetActive(false);
        }

        public override void OnNetworkSpawn()
        {
            Team.OnValueChanged += OnTeamChanged;
            IsDead.OnValueChanged += OnDeadChanged;
            PlayerName.OnValueChanged += OnNameChanged;
            ApplyTeamColor(Team.Value);

            if (IsServer)
            {
                if (GameModeManager.Instance != null)
                    Team.Value = GameModeManager.Instance.AssignTeam(OwnerClientId);
                ServerRespawn(); // put the new player on their team's side
            }

            if (!IsOwner)
            {
                // Floating name above other players' heads.
                nametag = Nametag.Create(transform, PlayerName.Value.ToString(), CurrentTeamColor);
            }

            if (IsOwner)
            {
                PlayerName.Value = GameSettings.PlayerName;
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
                if (MultiplayerBootstrap.Instance != null)
                    MultiplayerBootstrap.Instance.SetMenuCamera(true);
                MouseLook.LockCursor(false);
            }
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner)
                return;

            // Freeze input while dead or between matches.
            bool canPlay = !IsDead.Value
                && (GameModeManager.Instance == null || GameModeManager.Instance.IsMatchActive);
            if (rig.movement.enabled != canPlay)
                rig.movement.enabled = canPlay;
            if (rig.weaponController.enabled != canPlay)
                rig.weaponController.enabled = canPlay;
        }

        // ------------------------------------------------------------------
        // Team
        // ------------------------------------------------------------------

        private Color CurrentTeamColor =>
            Team.Value == 0 ? EnvironmentBuilder.Team0Color : EnvironmentBuilder.Team1Color;

        private void OnTeamChanged(int previous, int next) => ApplyTeamColor(next);

        private void ApplyTeamColor(int team)
        {
            rig.ApplyTeamColor(team == 0 ? EnvironmentBuilder.Team0Color : EnvironmentBuilder.Team1Color);
            if (nametag != null)
                nametag.SetColor(CurrentTeamColor);
        }

        private void OnNameChanged(FixedString64Bytes previous, FixedString64Bytes next)
        {
            if (nametag != null)
                nametag.SetText(next.ToString());
        }

        // ------------------------------------------------------------------
        // Death & respawn (server-driven)
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
                // Runs on every client: the satisfying cube-burst death.
                Effects.SpawnDeathBurst(transform.position + Vector3.up * 1.1f, CurrentTeamColor);
                SfxSynth.PlayAt(SfxSynth.Death(), transform.position, 0.9f);
            }
        }

        /// <summary>Called by NetworkHealth on the server when HP hits zero.</summary>
        public void ServerDie(ulong attackerId, bool headshot)
        {
            if (!IsServer || IsDead.Value)
                return;

            IsDead.Value = true;
            if (GameModeManager.Instance != null)
                GameModeManager.Instance.ReportKill(attackerId, OwnerClientId, headshot);
            StartCoroutine(RespawnAfterDelay());
        }

        private IEnumerator RespawnAfterDelay()
        {
            yield return new WaitForSeconds(respawnDelay);
            ServerRespawn();
        }

        public void ServerRespawn()
        {
            if (!IsServer)
                return;

            health.ServerResetHealth();
            IsDead.Value = false;

            Vector3 pos = GameModeManager.Instance != null
                ? GameModeManager.Instance.GetSpawnPoint(Team.Value)
                : new Vector3(0, 0.1f, -20f);
            float yaw = Team.Value == 0 ? 0f : 180f; // face the enemy side

            RespawnClientRpc(pos, yaw, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } },
            });
        }

        /// <summary>Runs only on the owner — with client-authoritative movement, the owner must do the teleport.</summary>
        [ClientRpc]
        private void RespawnClientRpc(Vector3 position, float yaw, ClientRpcParams rpcParams = default)
        {
            var rotation = Quaternion.Euler(0, yaw, 0);
            characterController.enabled = false;
            if (netTransform != null)
                netTransform.Teleport(position, rotation, transform.localScale);
            else
            {
                transform.position = position;
                transform.rotation = rotation;
            }
            characterController.enabled = true;
            rig.movement.spawnPoint = position; // fall-out-of-world safety respawn
        }
    }
}
