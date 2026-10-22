using System.Collections.Generic;
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

        /// <summary>
        /// Damage that comes from a point other than the attacker (rocket or
        /// grenade blast), so the victim's hit indicator points at the blast.
        /// </summary>
        public void TakeDamageFrom(float amount, bool headshot, Vector3 sourcePosition)
        {
            if (!IsSpawned) return;
            if (IsServer) ApplyDamageOnServer(amount, headshot, NetworkManager.LocalClientId, false, sourcePosition, true);
            else DamageFromRpc(amount, headshot, sourcePosition);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void DamageFromRpc(float amount, bool headshot, Vector3 sourcePosition, RpcParams rpcParams = default)
        {
            ApplyDamageOnServer(amount, headshot, rpcParams.Receive.SenderClientId, false, sourcePosition, true);
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

        private void ApplyDamageOnServer(float amount, bool headshot, ulong attackerId, bool noScope = false,
            Vector3 sourcePosition = default, bool hasSource = false, ulong sourceKey = ulong.MaxValue)
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

            // Hit indicator on the victim's screen: the blast point / bot / zombie
            // when given, otherwise the attacking player's position.
            if (!hasSource)
            {
                var attacker = gameMode != null ? gameMode.CombatantTransform(attackerId) : null;
                if (attacker != null)
                {
                    sourcePosition = attacker.position;
                    hasSource = true;
                }
            }
            if (sourceKey == ulong.MaxValue)
                sourceKey = attackerId;
            if (hasSource && FeedbackDue(sourceKey))
                DamageFeedbackClientRpc(sourcePosition, sourceKey,
                    new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } } });

            if (Hp.Value <= 0f)
            {
                var player = GetComponent<NetworkPlayer>();
                if (player != null)
                    player.ServerDie(attackerId, headshot, noScope);
            }
        }

        // Zombies hurt every frame and shotguns land 8 pellets at once: send at
        // most one indicator update per source every FeedbackInterval seconds.
        private const float FeedbackInterval = 0.12f;
        private readonly Dictionary<ulong, float> nextFeedback = new Dictionary<ulong, float>();

        private bool FeedbackDue(ulong sourceKey)
        {
            if (nextFeedback.TryGetValue(sourceKey, out float next) && Time.time < next)
                return false;
            if (nextFeedback.Count > 32)
                nextFeedback.Clear();
            nextFeedback[sourceKey] = Time.time + FeedbackInterval;
            return true;
        }

        /// <summary>Key for a non-player damage source (bot, zombie), distinct from client ids.</summary>
        private static ulong ObjectKey(Component source) =>
            source != null ? (1UL << 40) | (uint)source.GetHashCode() : ulong.MaxValue - 1;

        [ClientRpc]
        private void DamageFeedbackClientRpc(Vector3 sourcePosition, ulong sourceKey, ClientRpcParams rpcParams = default)
        {
            if (IsOwner)
                HudOverlay.Local?.NotifyDamage(sourcePosition, sourceKey);
        }

        public void ServerResetHealth()
        {
            if (IsServer)
                Hp.Value = maxHealth;
        }

        /// <summary>
        /// Server-simulated splash (grenade launcher): the server already knows who
        /// fired, so the attacker is passed in instead of read from an RPC sender.
        /// Hitting yourself is allowed (self-damage counts as a suicide).
        /// </summary>
        public void ServerExplosionDamage(float amount, ulong attackerId, Vector3 blastPosition)
        {
            if (IsServer) ApplyDamageOnServer(amount, false, attackerId, false, blastPosition, true);
        }

        public void ServerZombieDamage(float amount, Component zombie)
        {
            if (IsServer)
                ApplyDamageOnServer(amount, false, ulong.MaxValue, false,
                    zombie != null ? zombie.transform.position : default, zombie != null, ObjectKey(zombie));
        }

        /// <summary>A networked bot's shot (resolved on the server): the bot gets the kill
        /// credit like a player, and the victim's hit arrow points at it.</summary>
        public void ServerBotDamage(float amount, bool headshot, BotAgent bot)
        {
            if (IsServer && bot != null)
                ApplyDamageOnServer(amount, headshot, bot.BotId, false, bot.transform.position, true, bot.BotId);
        }
    }
}
