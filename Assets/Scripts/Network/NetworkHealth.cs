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
                DamageServerRpc(amount, headshot);
        }

        [ServerRpc(RequireOwnership = false)]
        private void DamageServerRpc(float amount, bool headshot, ServerRpcParams rpcParams = default)
        {
            ApplyDamageOnServer(amount, headshot, rpcParams.Receive.SenderClientId);
        }

        private void ApplyDamageOnServer(float amount, bool headshot, ulong attackerId)
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

            if (headshot)
                amount = maxHealth; // headshots are always lethal

            Hp.Value = Mathf.Max(0f, Hp.Value - amount);

            if (Hp.Value <= 0f)
            {
                var player = GetComponent<NetworkPlayer>();
                if (player != null)
                    player.ServerDie(attackerId, headshot);
            }
        }

        public void ServerResetHealth()
        {
            if (IsServer)
                Hp.Value = maxHealth;
        }
    }
}
