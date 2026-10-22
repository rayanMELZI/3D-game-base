using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace FpsBase
{
    public enum GameMode
    {
        Duel = 0,           // 1v1, first to 10 kills
        TeamDeathmatch = 1, // teams, first team to 30 kills
        FreeForAll = 2,     // everyone for themselves, first to 20 kills
        GunGame = 3,        // race through the arsenal, knife kill wins
        ZombieSurvival = 4,
        PStory = 5,         // free-roam story island, third-person camera
    }

    /// <summary>
    /// Server-authoritative match logic: teams, scores, map selection,
    /// win conditions and match restart. Lives on an in-scene NetworkObject.
    /// Team modes use two teams; FFA/Gun Game give every player their own
    /// "team" (unique color, everyone damages everyone).
    /// </summary>
    public class GameModeManager : NetworkBehaviour
    {
        public static GameModeManager Instance { get; private set; }

        // Chosen in the menu before StartHost is called.
        public static GameMode PendingHostMode = GameMode.Duel;
        public static int PendingHostMap = 0;
        public static bool PendingSniperOnly = false;
        /// <summary>0 = off, 1 = players always visible, 2 = visible while firing.</summary>
        public static int PendingRadarMode = 2;
        /// <summary>Host's initial weapons rule: true = class loadout (2 weapons), false = all unlocked.</summary>
        public static bool PendingClassLoadout = true;

        /// <summary>The active weapons rule: the host's networked choice in a game, else the local setting (offline).</summary>
        public static bool ClassLoadoutActive =>
            Instance != null && Instance.IsSpawned ? Instance.ClassLoadout.Value : GameSettings.UseClassLoadout;

        /// <summary>Gun Game weapon order by loadout index (knife last).</summary>
        public static readonly int[] GunGameOrder = { 1, 2, 3, 4, 7, 5, 8, 6, 0 };

        public NetworkVariable<int> Mode = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> MapIndex = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<bool> SniperOnly = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        /// <summary>Minimap radar: 0 = off, 1 = always show players, 2 = show while firing.</summary>
        public NetworkVariable<int> RadarMode = new NetworkVariable<int>(
            2, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        /// <summary>Host-chosen for the whole match: true = class loadout (2 weapons),
        /// false = every weapon unlocked. Replaces the old per-player toggle.</summary>
        public NetworkVariable<bool> ClassLoadout = new NetworkVariable<bool>(
            true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> ScoreTeam0 = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> ScoreTeam1 = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<bool> MatchActive = new NetworkVariable<bool>(
            true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<bool> LobbyOpen = new NetworkVariable<bool>(
            true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        /// <summary>Bots added to the match by the host (0 = none). Capped by the free player
        /// slots; never in Zombie Survival or P Story. See BotDirector.</summary>
        public NetworkVariable<int> BotCount = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public const int MaxBots = 7;
        private const string BotCountPref = "botCount";
        public NetworkVariable<int> WinnerTeam = new NetworkVariable<int>(
            -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<FixedString64Bytes> WinnerName = new NetworkVariable<FixedString64Bytes>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<ulong> LastKillerId = new NetworkVariable<ulong>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Zombie survival, written by ZombieDirector on the server, read by the HUD.
        public NetworkVariable<int> ZombieWave = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> ZombiesRemaining = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        /// <summary>Server time (NetworkManager.ServerTime) at which the next wave starts.</summary>
        public NetworkVariable<double> ZombieWaveStartsAt = new NetworkVariable<double>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public float restartDelay = 8f;

        public GameMode CurrentMode => (GameMode)Mode.Value;
        public bool IsMatchActive => MatchActive.Value && !LobbyOpen.Value;
        public bool IsTeamMode => CurrentMode == GameMode.Duel || CurrentMode == GameMode.TeamDeathmatch;
        public bool IsZombieMode => CurrentMode == GameMode.ZombieSurvival;

        public int ScoreLimit
        {
            get
            {
                switch (CurrentMode)
                {
                    case GameMode.Duel: return 10;
                    case GameMode.TeamDeathmatch: return 30;
                    case GameMode.FreeForAll: return 20;
                    case GameMode.PStory: return 9999; // free-roam, effectively no round end
                    default: return GunGameOrder.Length;
                }
            }
        }

        public static int MaxPlayersFor(GameMode mode) => mode == GameMode.Duel ? 5 : 8;

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

        private void Update()
        {
            if (IsServer && IsSpawned && CurrentMode == GameMode.ZombieSurvival && !LobbyOpen.Value
                && GetComponent<ZombieDirector>() == null)
                gameObject.AddComponent<ZombieDirector>();
            // Bots are disabled for now (buggy) — the toggle stays in the UI but
            // never spawns a BotDirector. Re-enable by restoring this block.
            // if (IsServer && IsSpawned && CurrentMode != GameMode.ZombieSurvival && !LobbyOpen.Value && BotsEnabled.Value
            //     && GetComponent<BotDirector>() == null)
            //     gameObject.AddComponent<BotDirector>();
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
                MapIndex.Value = PendingHostMap;
                SniperOnly.Value = PendingSniperOnly;
                RadarMode.Value = PendingRadarMode;
                ClassLoadout.Value = PendingClassLoadout;
                BotCount.Value = Mathf.Clamp(PlayerPrefs.GetInt(BotCountPref, 0), 0, MaxBots);
                ScoreTeam0.Value = 0;
                ScoreTeam1.Value = 0;
                WinnerTeam.Value = -1;
                WinnerName.Value = default;
                MatchActive.Value = true;
                LobbyOpen.Value = true;
            }
            KillFeed.Clear();

            // Everyone (host + clients) builds the selected map.
            ApplyMap(0, MapIndex.Value);
            MapIndex.OnValueChanged += ApplyMap;
        }

        public void HostStartMatch()
        {
            if (!IsServer || !LobbyOpen.Value) return;
            if (CurrentMode == GameMode.ZombieSurvival)
                MapIndex.Value = MapCatalog.DarkMapIndex();
            if (CurrentMode == GameMode.PStory)
                MapIndex.Value = MapCatalog.PStoryMapIndex();
            LobbyOpen.Value = false;
            if (CurrentMode == GameMode.ZombieSurvival && GetComponent<ZombieDirector>() == null)
                gameObject.AddComponent<ZombieDirector>();
            ForEachPlayer(p => { if (!p.Spectating.Value) p.ServerRespawn(); });
        }

        public void HostCycleMap(int direction)
        {
            if (!IsServer || !LobbyOpen.Value) return;
            MapIndex.Value = (MapIndex.Value + direction + MapCatalog.Count) % MapCatalog.Count;
        }

        public void HostCycleMode()
        {
            if (!IsServer || !LobbyOpen.Value) return;
            Mode.Value = (Mode.Value + 1) % 6;
            // Preview the forced maps in the lobby card as the host cycles.
            if (CurrentMode == GameMode.PStory)
                MapIndex.Value = MapCatalog.PStoryMapIndex();
            if (CurrentMode == GameMode.ZombieSurvival)
                MapIndex.Value = MapCatalog.DarkMapIndex();
        }

        /// <summary>Lobby: cycle the bot count 0..MaxBots (remembered for the next host).</summary>
        public void HostCycleBots(int direction)
        {
            if (!IsServer || !LobbyOpen.Value) return;
            BotCount.Value = (BotCount.Value + direction + MaxBots + 1) % (MaxBots + 1);
            PlayerPrefs.SetInt(BotCountPref, BotCount.Value);
            PlayerPrefs.Save();
        }

        public void HostToggleClassLoadout()
        {
            if (IsServer && LobbyOpen.Value) ClassLoadout.Value = !ClassLoadout.Value;
        }

        public void HostToggleSniper()
        {
            if (IsServer && LobbyOpen.Value) SniperOnly.Value = !SniperOnly.Value;
        }

        public void HostCycleRadar()
        {
            if (IsServer && LobbyOpen.Value) RadarMode.Value = (RadarMode.Value + 1) % 3;
        }

        public override void OnNetworkDespawn()
        {
            MapIndex.OnValueChanged -= ApplyMap;
        }

        private void ApplyMap(int previous, int index)
        {
            if (MultiplayerBootstrap.Instance != null)
                MultiplayerBootstrap.Instance.SetMap(index);

            // Changing map pulls the ground out from under everyone standing on
            // the old one (old spawns can be outside the new map's bounds) —
            // respawn all players onto the new map so nobody is left falling
            // into the void in a kill-Y loop.
            if (IsServer && previous != index)
                ForEachPlayer(p => p.ServerRespawn());
        }

        // ------------------------------------------------------------------
        // Teams & spawns (server)
        // ------------------------------------------------------------------

        public int AssignTeam(ulong newClientId)
        {
            if (!IsTeamMode)
            {
                // FFA / Gun Game: give each player their own color slot.
                var used = new bool[8];
                ForEachPlayer(player =>
                {
                    if (player.OwnerClientId != newClientId)
                        used[Mathf.Abs(player.Team.Value) % 8] = true;
                });
                for (int i = 0; i < used.Length; i++)
                    if (!used[i])
                        return i;
                return (int)(newClientId % 8);
            }

            int team0 = 0, team1 = 0;
            ForEachPlayer(player =>
            {
                if (player.OwnerClientId == newClientId)
                    return;
                if (player.Team.Value == 0) team0++;
                else team1++;
            });
            return team0 <= team1 ? 0 : 1;
        }

        /// <summary>Switch a player to the other team if it doesn't unbalance (team modes only).</summary>
        public bool ServerTryChangeTeam(NetworkPlayer player)
        {
            if (!IsServer || !IsTeamMode || player == null)
                return false;

            int mine = 0, other = 0;
            int myTeam = player.Team.Value;
            ForEachPlayer(p =>
            {
                if (p.Team.Value == myTeam) mine++;
                else other++;
            });

            if (other >= mine)
                return false; // would unbalance

            player.Team.Value = 1 - myTeam;
            player.ServerRespawn();
            return true;
        }

        public int TeamOf(ulong clientId)
        {
            var player = PlayerOf(clientId);
            return player != null ? player.Team.Value : -1;
        }

        public bool AreSameTeam(ulong a, ulong b)
        {
            if (IsZombieMode)
                return true;  // co-op: survivors never hurt each other
            if (!IsTeamMode)
                return false; // FFA / Gun Game: everyone is a target
            int teamA = TeamOf(a);
            return teamA >= 0 && teamA == TeamOf(b);
        }

        /// <summary>
        /// Picks a spawn among the map's candidates: snaps each to solid ground
        /// (so nobody ever spawns in the void) and chooses the one farthest from
        /// living enemies (so nobody gets spawn-killed).
        /// </summary>
        public Vector3 GetSpawnPoint(int team)
        {
            int side = IsTeamMode ? Mathf.Abs(team) % 2 : -1;
            var candidates = IsZombieMode
                ? MapCatalog.SurvivorSpawnCandidates(MapIndex.Value)
                : MapCatalog.SpawnCandidates(MapIndex.Value, side);

            // Positions of everyone currently alive except this team's members
            // (zombie survival: the living zombies are the enemies).
            var enemies = new List<Vector3>();
            if (IsZombieMode)
            {
                foreach (var z in ZombieAgent.Active)
                    if (z != null && z.IsAlive)
                        enemies.Add(z.transform.position);
            }
            else
            {
                ForEachPlayer(p =>
                {
                    if (p == null || !p.IsSpawned || p.IsDead.Value)
                        return;
                    bool sameTeam = IsTeamMode && p.Team.Value == team;
                    if (!sameTeam)
                        enemies.Add(p.transform.position);
                });
            }

            Vector3 best = SnapToGround(candidates[0]);
            float bestScore = float.NegativeInfinity;
            foreach (var raw in candidates)
            {
                Vector3 grounded = SnapToGround(raw);
                float nearestEnemy = float.MaxValue;
                foreach (var e in enemies)
                    nearestEnemy = Mathf.Min(nearestEnemy, Vector3.Distance(e, grounded));

                // No enemies yet → rotate through candidates so players don't stack.
                float score = enemies.Count == 0
                    ? (spawnCounter + Vector3.Dot(grounded, Vector3.one)) % 97f
                    : nearestEnemy;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = grounded;
                }
            }
            spawnCounter++;
            return best;
        }

        /// <summary>Raycast down onto real geometry so the spawn is never floating over a hole.</summary>
        public static Vector3 SnapToGround(Vector3 pos)
        {
            // Cast from just above the candidate — NOT from high up, or indoor
            // maps snap the spawn onto their own ceiling/rooftops (this put
            // everyone on the Backrooms roof).
            if (Physics.Raycast(pos + Vector3.up * 1.5f, Vector3.down, out var hit, 40f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * 0.3f;
            // Candidate authored below its floor? Try the tall cast as a fallback.
            if (Physics.Raycast(pos + Vector3.up * 30f, Vector3.down, out hit, 60f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * 0.3f;
            return pos + Vector3.up * 0.3f;
        }

        // ------------------------------------------------------------------
        // Scoring (server)
        // ------------------------------------------------------------------

        public void ReportKill(ulong attackerId, ulong victimId, bool headshot, bool noScope = false)
        {
            if (!IsServer || !MatchActive.Value)
                return;

            var victim = PlayerOf(victimId);
            if (victim != null)
            {
                victim.Deaths.Value++;
                victim.KillStreak.Value = 0;
            }

            var attacker = PlayerOf(attackerId);
            if (attackerId != victimId && attacker != null)
            {
                attacker.Kills.Value++;
                attacker.KillStreak.Value++;
                LastKillerId.Value = attackerId;
                int streak = attacker.KillStreak.Value;
                bool milestone = streak >= 5 && streak % 5 == 0;
                attacker.AwardXpClientRpc(headshot ? 140 : 100, headshot ? XpReason.Headshot : XpReason.Kill,
                    milestone, streak, OwnerOnly(attackerId));
                if (milestone)
                    KillFeedClientRpc($"{NameOf(attackerId)}  KILLSTREAK  {streak}");

                switch (CurrentMode)
                {
                    case GameMode.Duel:
                    case GameMode.TeamDeathmatch:
                        int team = attacker.Team.Value;
                        if (team == 0) ScoreTeam0.Value++;
                        else ScoreTeam1.Value++;
                        if (ScoreTeam0.Value >= ScoreLimit) EndMatch(0, null);
                        else if (ScoreTeam1.Value >= ScoreLimit) EndMatch(1, null);
                        break;

                    case GameMode.FreeForAll:
                        if (attacker.Kills.Value >= ScoreLimit)
                            EndMatch(attacker.Team.Value, attacker);
                        break;

                    case GameMode.GunGame:
                        attacker.GunGameLevel.Value++;
                        if (attacker.GunGameLevel.Value >= GunGameOrder.Length)
                            EndMatch(attacker.Team.Value, attacker);
                        break;
                }
            }

            if (attackerId == ulong.MaxValue)
            {
                // Killed by a zombie (or a bot): no player to credit.
                KillFeedClientRpc(IsZombieMode ? $"{NameOf(victimId)}  WAS OVERRUN" : $"{NameOf(victimId)}  DOWN");
                return;
            }
            string verb = noScope ? "NO-SCOPE" : (headshot ? "HEADSHOT" : "eliminated");
            KillFeedClientRpc($"{NameOf(attackerId)}  {verb}  {NameOf(victimId)}");
        }

        // ------------------------------------------------------------------
        // Zombie survival (server, called by ZombieAgent / ZombieDirector)
        // ------------------------------------------------------------------

        public const int ZombieKillPoints = 100;
        public const int ZombieHeadshotPoints = 150;
        public const int WaveSurvivedPoints = 50;

        /// <summary>A zombie died: points, kill count and a little XP for whoever killed it.</summary>
        public void ServerZombieKilled(ulong attackerId, bool headshot)
        {
            if (!IsServer || !IsMatchActive)
                return;
            var attacker = PlayerOf(attackerId);
            if (attacker == null)
                return;
            attacker.Kills.Value++;
            attacker.Score.Value += headshot ? ZombieHeadshotPoints : ZombieKillPoints;
            attacker.AwardXpClientRpc(headshot ? 35 : 25, headshot ? XpReason.Headshot : XpReason.Kill,
                false, 0, OwnerOnly(attackerId));
        }

        /// <summary>Wave cleared: every survivor still standing earns points for it.</summary>
        public void ServerZombieWaveCleared(int wave)
        {
            if (!IsServer)
                return;
            ForEachPlayer(p =>
            {
                if (!p.Spectating.Value && !p.IsDead.Value)
                    p.Score.Value += WaveSurvivedPoints * wave;
            });
            KillFeedClientRpc($"WAVE {wave} CLEARED");
        }

        /// <summary>New wave: the fallen get back up.</summary>
        public void ServerReviveSurvivors()
        {
            if (!IsServer)
                return;
            ForEachPlayer(p =>
            {
                if (!p.Spectating.Value && p.IsDead.Value)
                    p.ServerRespawn();
            });
        }

        /// <summary>Every survivor is down at once: the match ends (no winner), XP by waves reached.</summary>
        public void ServerZombieGameOver(int wave)
        {
            if (!IsServer || !MatchActive.Value)
                return;
            MatchActive.Value = false;
            WinnerTeam.Value = 0; // shows the end banner (the HUD prints OVERRUN in this mode)
            WinnerName.Value = default;
            int xp = MatchCompleteXp + 50 * Mathf.Max(0, wave - 1);
            ForEachPlayer(player =>
            {
                if (!player.Spectating.Value)
                    player.AwardXpClientRpc(xp, XpReason.MatchComplete, false, 0, OwnerOnly(player.OwnerClientId));
            });
            KillFeedClientRpc($"OVERRUN AT WAVE {wave}");
            StartCoroutine(RestartRoutine());
        }

        public string NameOf(ulong clientId)
        {
            var player = PlayerOf(clientId);
            if (player != null && !player.PlayerName.Value.IsEmpty)
                return player.PlayerName.Value.ToString();
            return $"Player {clientId + 1}";
        }

        /// <param name="winner">The winning player in solo modes; null = the whole team won.</param>
        private void EndMatch(int winnerTeam, NetworkPlayer winner)
        {
            if (!MatchActive.Value)
                return; // already ended: never award match XP twice
            MatchActive.Value = false;
            WinnerTeam.Value = winnerTeam;
            WinnerName.Value = winner != null ? NameOf(winner.OwnerClientId) : "";
            AwardMatchXp(winnerTeam, winner);
            StartCoroutine(RestartRoutine());
        }

        public const int WinXp = 500;
        public const int MatchCompleteXp = 150;

        /// <summary>Everyone who played (spectators excluded) gets XP once per match end;
        /// the winners get the bigger reward.</summary>
        private void AwardMatchXp(int winnerTeam, NetworkPlayer winner)
        {
            ForEachPlayer(player =>
            {
                if (player.Spectating.Value)
                    return;
                bool won = winner != null ? player == winner : player.Team.Value == winnerTeam;
                player.AwardXpClientRpc(won ? WinXp : MatchCompleteXp,
                    won ? XpReason.Win : XpReason.MatchComplete, false, 0, OwnerOnly(player.OwnerClientId));
            });
        }

        private IEnumerator RestartRoutine()
        {
            yield return new WaitForSeconds(restartDelay);

            ScoreTeam0.Value = 0;
            ScoreTeam1.Value = 0;
            WinnerTeam.Value = -1;
            WinnerName.Value = default;
            MatchActive.Value = true;

            ForEachPlayer(player =>
            {
                player.Kills.Value = 0;
                player.Deaths.Value = 0;
                player.GunGameLevel.Value = 0;
                player.KillStreak.Value = 0;
                player.Score.Value = 0;
                player.ServerRespawn();
            });
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        public NetworkPlayer PlayerOf(ulong clientId)
        {
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client)
                && client.PlayerObject != null)
                return client.PlayerObject.GetComponent<NetworkPlayer>();
            return null;
        }

        private static ClientRpcParams OwnerOnly(ulong clientId) =>
            new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } } };

        private void ForEachPlayer(System.Action<NetworkPlayer> action)
        {
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject == null)
                    continue;
                var player = client.PlayerObject.GetComponent<NetworkPlayer>();
                if (player != null)
                    action(player);
            }
        }

        [ClientRpc]
        private void KillFeedClientRpc(string message)
        {
            KillFeed.Add(new KillFeedEntry { message = message, time = Time.time });
            if (KillFeed.Count > 6)
                KillFeed.RemoveAt(0);
        }
    }
}
