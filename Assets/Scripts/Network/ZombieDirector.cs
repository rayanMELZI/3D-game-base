using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Server-only wave logic for Zombie Survival (added to the GameModeManager
    /// object by the server). Spawns networked NetworkZombie prefabs away from
    /// the players, publishes wave / remaining / countdown through
    /// GameModeManager's NetworkVariables (the HUD reads those), revives the
    /// fallen survivors at the start of each wave and ends the match when every
    /// survivor is down at the same time.
    /// </summary>
    public class ZombieDirector : MonoBehaviour
    {
        public float firstWaveDelay = 6f;
        public float intermission = 10f;
        public float spawnInterval = 0.9f;
        public float minSpawnDistance = 18f;

        private enum State { Idle, Intermission, Wave }

        private State state = State.Idle;
        private int wave;
        private int toSpawn;
        private float nextWaveAt;
        private float nextSpawnAt;
        private GameObject prefab;

        private static bool IsServer => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        private void Update()
        {
            if (!IsServer)
                return;
            var mode = GameModeManager.Instance;
            if (mode == null || !mode.IsSpawned)
                return;

            // Not playing zombies (lobby, other mode) or the match is over:
            // clear the horde and wait for the next match.
            if (mode.CurrentMode != GameMode.ZombieSurvival || mode.LobbyOpen.Value || !mode.MatchActive.Value)
            {
                if (state != State.Idle)
                    ResetDirector(mode);
                return;
            }

            if (state == State.Idle)
                BeginIntermission(mode, firstWaveDelay);

            int alive = AliveZombies();

            switch (state)
            {
                case State.Intermission:
                    if (Time.time >= nextWaveAt)
                        StartWave(mode);
                    break;

                case State.Wave:
                    if (toSpawn > 0 && alive < MaxAlive() && Time.time >= nextSpawnAt)
                    {
                        if (SpawnZombie())
                        {
                            toSpawn--;
                            alive++;
                        }
                        nextSpawnAt = Time.time + spawnInterval;
                    }
                    if (toSpawn == 0 && alive == 0)
                    {
                        mode.ServerZombieWaveCleared(wave);
                        BeginIntermission(mode, intermission);
                    }
                    break;
            }

            mode.ZombiesRemaining.Value = state == State.Wave ? toSpawn + alive : 0;

            if (EveryoneDown())
                mode.ServerZombieGameOver(wave);
        }

        private void ResetDirector(GameModeManager mode)
        {
            foreach (var zombie in ZombieAgent.Active.ToArray())
                if (zombie != null && zombie.IsSpawned)
                    zombie.NetworkObject.Despawn(true);
            state = State.Idle;
            wave = 0;
            toSpawn = 0;
            if (mode.MatchActive.Value)
            {
                // Lobby / other mode: clear the counters. At match end they stay
                // visible on the game-over banner until the restart.
                mode.ZombieWave.Value = 0;
                mode.ZombiesRemaining.Value = 0;
            }
        }

        private void BeginIntermission(GameModeManager mode, float delay)
        {
            state = State.Intermission;
            nextWaveAt = Time.time + delay;
            if (wave == 0)
                mode.ZombieWave.Value = 0; // fresh match: clear the last game's wave
            mode.ZombieWaveStartsAt.Value = NetworkManager.Singleton.ServerTime.Time + delay;
        }

        private void StartWave(GameModeManager mode)
        {
            wave++;
            state = State.Wave;
            toSpawn = 4 + wave * 3;
            nextSpawnAt = Time.time;
            mode.ZombieWave.Value = wave;
            mode.ServerReviveSurvivors();
        }

        private int MaxAlive() => Mathf.Min(24, 8 + 3 * SurvivorCount());

        private static int AliveZombies()
        {
            int n = 0;
            foreach (var z in ZombieAgent.Active)
                if (z != null && z.IsAlive)
                    n++;
            return n;
        }

        private static IEnumerable<NetworkPlayer> Survivors()
        {
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                var player = client.PlayerObject != null ? client.PlayerObject.GetComponent<NetworkPlayer>() : null;
                if (player != null && player.IsSpawned && !player.Spectating.Value)
                    yield return player;
            }
        }

        private static int SurvivorCount()
        {
            int n = 0;
            foreach (var _ in Survivors())
                n++;
            return n;
        }

        private static bool EveryoneDown()
        {
            bool any = false;
            foreach (var p in Survivors())
            {
                any = true;
                if (!p.IsDead.Value)
                    return false;
            }
            return any;
        }

        // ------------------------------------------------------------------
        // Spawning
        // ------------------------------------------------------------------

        private GameObject Prefab()
        {
            if (prefab != null)
                return prefab;
            var prefabs = NetworkManager.Singleton.NetworkConfig.Prefabs;
            if (prefabs != null)
                foreach (var entry in prefabs.Prefabs)
                    if (entry.Prefab != null && entry.Prefab.GetComponent<ZombieAgent>() != null)
                        return prefab = entry.Prefab;
            Debug.LogError("[ZombieDirector] NetworkZombie prefab is not in the NetworkManager prefab list " +
                           "(Assets/DefaultNetworkPrefabs.asset).");
            return null;
        }

        private bool SpawnZombie()
        {
            var source = Prefab();
            if (source == null || !TryPickSpawn(out Vector3 position))
                return false;

            var go = Instantiate(source, position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            var agent = go.GetComponent<ZombieAgent>();
            bool runner = wave >= 3 && Random.value < Mathf.Min(0.1f * (wave - 2), 0.5f);
            float speed = runner
                ? Random.Range(4.2f, 4.8f)
                : Mathf.Min(1.5f + 0.12f * wave, 3.2f) * Random.Range(0.85f, 1.15f);
            float health = Mathf.Min(60f + 20f * (wave - 1), 400f);
            agent.ServerSetup(health, speed, runner, (byte)Random.Range(0, 4));
            agent.attackDamage = Mathf.Min(25f + 2f * wave, 45f);
            go.GetComponent<NetworkObject>().Spawn(true);
            return true;
        }

        /// <summary>
        /// A random zombie entry point at least minSpawnDistance from every living
        /// survivor (else the farthest one), snapped to the ground.
        /// </summary>
        public bool TryPickSpawn(out Vector3 position)
        {
            position = default;
            var mode = GameModeManager.Instance;
            if (mode == null)
                return false;
            var candidates = MapCatalog.ZombieSpawnCandidates(mode.MapIndex.Value);
            if (candidates == null || candidates.Length == 0)
                return false;

            var players = new List<Vector3>();
            foreach (var p in Survivors())
                if (!p.IsDead.Value)
                    players.Add(p.transform.position);

            var far = new List<Vector3>();
            Vector3 farthest = candidates[0];
            float farthestDistance = -1f;
            foreach (var c in candidates)
            {
                float nearest = float.MaxValue;
                foreach (var p in players)
                    nearest = Mathf.Min(nearest, Vector3.Distance(p, c));
                if (nearest >= minSpawnDistance)
                    far.Add(c);
                if (nearest > farthestDistance)
                {
                    farthestDistance = nearest;
                    farthest = c;
                }
            }

            Vector3 chosen = far.Count > 0 ? far[Random.Range(0, far.Count)] : farthest;
            // Spread a little so a group doesn't spawn inside itself.
            chosen += new Vector3(Random.Range(-1.2f, 1.2f), 0f, Random.Range(-1.2f, 1.2f));
            position = GameModeManager.SnapToGround(chosen);
            return true;
        }
    }
}
