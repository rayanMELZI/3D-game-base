using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace FpsBase
{
    public enum GameMode
    {
        Duel = 0,           // 1v1, first to 10 kills
        TeamDeathmatch = 1, // up to 4v4, first team to 30 kills
    }

    /// <summary>
    /// Server-authoritative match logic: teams, scores, win condition and match
    /// restart. Lives on an in-scene NetworkObject in the multiplayer scene.
    /// Both modes use two teams — a duel is simply teams of one.
    /// </summary>
    public class GameModeManager : NetworkBehaviour
    {
        public static GameModeManager Instance { get; private set; }

        /// <summary>Chosen in the menu before StartHost is called.</summary>
        public static GameMode PendingHostMode = GameMode.Duel;

        public NetworkVariable<int> Mode = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> ScoreTeam0 = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> ScoreTeam1 = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<bool> MatchActive = new NetworkVariable<bool>(
            true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> WinnerTeam = new NetworkVariable<int>(
            -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public float restartDelay = 8f;

        public GameMode CurrentMode => (GameMode)Mode.Value;
        public bool IsMatchActive => MatchActive.Value;
        public int ScoreLimit => CurrentMode == GameMode.Duel ? 10 : 30;

        public static int MaxPlayersFor(GameMode mode) => mode == GameMode.Duel ? 2 : 8;

        // Client-side kill feed (filled by ClientRpc, read by NetworkGameHud).
        public struct KillFeedEntry
        {
            public string message;
            public float time;
        }
        public readonly List<KillFeedEntry> KillFeed = new List<KillFeedEntry>();

        private int spawnCounter;

        private void Awake()
        {
            Instance = this;
        }

        public override void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                Mode.Value = (int)PendingHostMode;
                ScoreTeam0.Value = 0;
                ScoreTeam1.Value = 0;
                WinnerTeam.Value = -1;
                MatchActive.Value = true;
            }
            KillFeed.Clear();
        }

        // ------------------------------------------------------------------
        // Teams & spawns (server)
        // ------------------------------------------------------------------

        /// <summary>Put the joining player on the smaller team (alternates in a duel).</summary>
        public int AssignTeam(ulong newClientId)
        {
            int team0 = 0, team1 = 0;
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.ClientId == newClientId || client.PlayerObject == null)
                    continue;
                var player = client.PlayerObject.GetComponent<NetworkPlayer>();
                if (player == null)
                    continue;
                if (player.Team.Value == 0) team0++;
                else team1++;
            }
            return team0 <= team1 ? 0 : 1;
        }

        public int TeamOf(ulong clientId)
        {
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client)
                && client.PlayerObject != null)
            {
                var player = client.PlayerObject.GetComponent<NetworkPlayer>();
                if (player != null)
                    return player.Team.Value;
            }
            return -1;
        }

        public bool AreSameTeam(ulong a, ulong b)
        {
            int teamA = TeamOf(a);
            return teamA >= 0 && teamA == TeamOf(b);
        }

        /// <summary>Team 0 spawns on the -Z side, team 1 on the +Z side.</summary>
        public Vector3 GetSpawnPoint(int team)
        {
            float z = 24f * (team == 0 ? -1f : 1f);
            float[] lanes = { 0f, -18f, 18f, -9f, 9f };
            float x = lanes[spawnCounter++ % lanes.Length];
            return new Vector3(x, 0.1f, z);
        }

        // ------------------------------------------------------------------
        // Scoring (server)
        // ------------------------------------------------------------------

        public void ReportKill(ulong attackerId, ulong victimId, bool headshot)
        {
            if (!IsServer || !MatchActive.Value)
                return;

            if (attackerId != victimId)
            {
                int team = TeamOf(attackerId);
                if (team == 0) ScoreTeam0.Value++;
                else if (team == 1) ScoreTeam1.Value++;
            }

            string verb = headshot ? "HEADSHOT" : "eliminated";
            KillFeedClientRpc($"{NameOf(attackerId)}  {verb}  {NameOf(victimId)}");

            if (ScoreTeam0.Value >= ScoreLimit) EndMatch(0);
            else if (ScoreTeam1.Value >= ScoreLimit) EndMatch(1);
        }

        /// <summary>Display name of a connected player (server-side lookup).</summary>
        public string NameOf(ulong clientId)
        {
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client)
                && client.PlayerObject != null)
            {
                var player = client.PlayerObject.GetComponent<NetworkPlayer>();
                if (player != null && !player.PlayerName.Value.IsEmpty)
                    return player.PlayerName.Value.ToString();
            }
            return $"Player {clientId + 1}";
        }

        private void EndMatch(int winner)
        {
            MatchActive.Value = false;
            WinnerTeam.Value = winner;
            StartCoroutine(RestartRoutine());
        }

        private IEnumerator RestartRoutine()
        {
            yield return new WaitForSeconds(restartDelay);

            ScoreTeam0.Value = 0;
            ScoreTeam1.Value = 0;
            WinnerTeam.Value = -1;
            MatchActive.Value = true;

            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject == null)
                    continue;
                var player = client.PlayerObject.GetComponent<NetworkPlayer>();
                if (player != null)
                    player.ServerRespawn();
            }
        }

        // ------------------------------------------------------------------
        // Kill feed (all clients)
        // ------------------------------------------------------------------

        [ClientRpc]
        private void KillFeedClientRpc(string message)
        {
            KillFeed.Add(new KillFeedEntry { message = message, time = Time.time });
            if (KillFeed.Count > 6)
                KillFeed.RemoveAt(0);
        }
    }
}
