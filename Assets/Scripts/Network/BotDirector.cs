using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Server-only, on the GameModeManager object: keeps the match filled with
    /// the host's number of networked bots (GameModeManager.BotCount), capped by
    /// the free player slots, keeps team modes balanced, and re-bakes the bots'
    /// NavMesh whenever the map changes. No bots in the lobby, in Zombie
    /// Survival or in P Story.
    /// </summary>
    public class BotDirector : MonoBehaviour
    {
        private static readonly string[] Names =
        {
            "VIPER", "GHOST", "RAVEN", "NOMAD", "SABLE", "FALCON", "BISHOP", "ECHO", "HAWK", "ONYX",
        };

        private float nextCheck;
        private GameObject prefab;
        private bool warnedMissingPrefab;

        private void Update()
        {
            var nm = NetworkManager.Singleton;
            var gameMode = GameModeManager.Instance;
            if (nm == null || !nm.IsServer || gameMode == null || !gameMode.IsSpawned)
                return;

            UpdateNavigation(gameMode);

            if (Time.time < nextCheck)
                return;
            nextCheck = Time.time + 1f;

            int desired = DesiredBots(gameMode);
            var bots = new List<BotAgent>();
            foreach (var bot in BotAgent.All)
                if (bot != null && bot.IsSpawned)
                    bots.Add(bot);

            if (bots.Count < desired)
                Spawn(gameMode, bots);
            else if (bots.Count > desired && bots.Count > 0)
                bots[bots.Count - 1].NetworkObject.Despawn(true);
            else if (gameMode.IsTeamMode)
                Rebalance(bots);
        }

        private static int DesiredBots(GameModeManager gameMode)
        {
            if (gameMode.LobbyOpen.Value || gameMode.CurrentMode == GameMode.ZombieSurvival
                || gameMode.CurrentMode == GameMode.PStory)
                return 0;
            // Spectators don't take a slot; bots never push a human out.
            int humans = 0;
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                var player = client.PlayerObject != null ? client.PlayerObject.GetComponent<NetworkPlayer>() : null;
                if (player != null && !player.Spectating.Value)
                    humans++;
            }
            int free = GameModeManager.MaxPlayersFor(gameMode.CurrentMode) - humans;
            return Mathf.Clamp(gameMode.BotCount.Value, 0, Mathf.Max(0, free));
        }

        private void UpdateNavigation(GameModeManager gameMode)
        {
            var boot = MultiplayerBootstrap.Instance;
            if (boot == null || BotNavigation.BuiltMap == boot.CurrentMap
                || (BotAgent.All.Count == 0 && DesiredBots(gameMode) == 0))
                return;
            if (GameObject.Find(EnvironmentBuilder.MapRootName) == null)
                return; // the new map isn't built yet
            BotNavigation.Rebuild(boot.CurrentMap);
        }

        private GameObject Prefab()
        {
            if (prefab != null)
                return prefab;
            foreach (var entry in NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs)
                if (entry.Prefab != null && entry.Prefab.GetComponent<BotAgent>() != null)
                    return prefab = entry.Prefab;
            if (!warnedMissingPrefab)
            {
                warnedMissingPrefab = true;
                Debug.LogWarning("[Bots] NetworkBot prefab missing from the network prefab list " +
                                 "(Assets/DefaultNetworkPrefabs.asset): run Tools/FPS Base/Setup Multiplayer.");
            }
            return null;
        }

        private void Spawn(GameModeManager gameMode, List<BotAgent> existing)
        {
            var source = Prefab();
            if (source == null)
                return;

            // First free name, so a replaced bot doesn't duplicate a living one's name.
            string name = null;
            foreach (var candidate in Names)
            {
                bool taken = false;
                foreach (var bot in existing)
                    if (bot.DisplayName == "BOT " + candidate) { taken = true; break; }
                if (!taken) { name = "BOT " + candidate; break; }
            }

            var go = Instantiate(source);
            var agent = go.GetComponent<BotAgent>();
            agent.pendingTeam = gameMode.AssignTeam(ulong.MaxValue);
            agent.pendingName = name ?? $"BOT {existing.Count + 1}";
            go.GetComponent<NetworkObject>().Spawn(true);
        }

        /// <summary>Team modes: move one bot at a time until the teams differ by one at most.</summary>
        private static void Rebalance(List<BotAgent> bots)
        {
            int team0 = 0, team1 = 0;
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                var player = client.PlayerObject != null ? client.PlayerObject.GetComponent<NetworkPlayer>() : null;
                if (player == null || player.Spectating.Value)
                    continue;
                if (player.Team.Value == 0) team0++; else team1++;
            }
            foreach (var bot in bots)
            {
                if (bot.Team.Value == 0) team0++; else team1++;
            }
            if (Mathf.Abs(team0 - team1) <= 1)
                return;
            int from = team0 > team1 ? 0 : 1;
            for (int i = bots.Count - 1; i >= 0; i--)
            {
                if (bots[i].Team.Value == from)
                {
                    bots[i].ServerSetTeam(1 - from);
                    return;
                }
            }
        }

        private void OnDestroy()
        {
            BotNavigation.Clear();
        }
    }
}
