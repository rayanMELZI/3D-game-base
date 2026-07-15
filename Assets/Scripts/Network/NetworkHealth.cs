using Unity.Netcode;
using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Networked health: the server owns the value; shooters call TakeDamage
    /// locally (via IDamageable, same as offline) and it is routed to the server
    /// with an RPC. The sender's client id gives the server the attacker for
    /// kill credit and friendly-fire checks.
    /// </summary>
    public class NetworkHealth : NetworkBehaviour, IDamageable, IHealthSource
    {
        public float maxHealth = 100f;

        public NetworkVariable<float> Hp = new NetworkVariable<float>(
            100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public float CurrentHealth => Hp.Value;
        public float MaxHealth => maxHealth;

        public override void OnNetworkSpawn()
        {
            if (IsServer)
                Hp.Value = maxHealth;
        }

        /// <summary>Called by WeaponController on whoever landed the shot.</summary>
        public void TakeDamage(float amount, bool headshot = false)
        {
            if (!IsSpawned)
                return;

            if (IsServer)
                ApplyDamageOnServer(amount, headshot, NetworkManager.LocalClientId); // host shot someone
            else
                DamageRpc(amount, headshot);
        }

        public void TakeDamageShot(float amount, bool headshot, bool noScope)
        {
            if (!IsSpawned) return;
            if (IsServer) ApplyDamageOnServer(amount, headshot, NetworkManager.LocalClientId, noScope);
            else DamageShotRpc(amount, headshot, noScope);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void DamageShotRpc(float amount, bool headshot, bool noScope, RpcParams rpcParams = default)
        {
            ApplyDamageOnServer(amount, headshot, rpcParams.Receive.SenderClientId, noScope);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void DamageRpc(float amount, bool headshot, RpcParams rpcParams = default)
        {
            ApplyDamageOnServer(amount, headshot, rpcParams.Receive.SenderClientId);
        }

        private void ApplyDamageOnServer(float amount, bool headshot, ulong attackerId, bool noScope = false)
        {
            if (Hp.Value <= 0f)
                return;

            var gameMode = GameModeManager.Instance;
            if (gameMode != null)
            {
                if (!gameMode.IsMatchActive)
                    return;
                // No friendly fire (self-damage can't happen; shots skip own colliders).
                if (attackerId != OwnerClientId && gameMode.AreSameTeam(attackerId, OwnerClientId))
                    return;
            }

            // Anti-spawnkill: freshly spawned players are briefly immune
            // (protection drops as soon as they fire a shot themselves).
            var victimPlayer = GetComponent<NetworkPlayer>();
            if (victimPlayer != null && victimPlayer.IsSpawnProtected && attackerId != OwnerClientId)
                return;

            if (headshot)
                amount = maxHealth; // headshots are always lethal

            Hp.Value = Mathf.Max(0f, Hp.Value - amount);

            var attacker = gameMode != null ? gameMode.PlayerOf(attackerId) : null;
            DamageFeedbackClientRpc(attacker != null ? attacker.transform.position : transform.position,
                new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } } });

            if (Hp.Value <= 0f)
            {
                var player = GetComponent<NetworkPlayer>();
                if (player != null)
                    player.ServerDie(attackerId, headshot, noScope);
            }
        }

        [ClientRpc]
        private void DamageFeedbackClientRpc(Vector3 sourcePosition, ClientRpcParams rpcParams = default)
        {
            HudOverlay.Local?.NotifyDamage(sourcePosition);
        }

        public void ServerResetHealth()
        {
            if (IsServer)
                Hp.Value = maxHealth;
        }

        public void ServerZombieDamage(float amount)
        {
            if (IsServer) ApplyDamageOnServer(amount, false, ulong.MaxValue);
        }

        public void ServerBotDamage(float amount)
        {
            if (IsServer) ApplyDamageOnServer(amount, false, ulong.MaxValue);
        }
    }
}
