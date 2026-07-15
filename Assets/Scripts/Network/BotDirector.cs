using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace FpsBase
{
    public class BotDirector : MonoBehaviour
    {
        private readonly List<BotTarget> bots = new List<BotTarget>();
        private float nextFill;
        private void Update()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
                return;
            if (Time.time < nextFill) return;
            nextFill = Time.time + 1f;
            bots.RemoveAll(b => b == null);
            int humans = FindObjectsByType<NetworkPlayer>().Length;
            while (bots.Count + humans < 6) Spawn(bots.Count);
        }
        private void Spawn(int index)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "BOT " + (index + 1);
            go.transform.position = GameModeManager.Instance.GetSpawnPoint(index % 2);
            go.GetComponent<Renderer>().sharedMaterial = EnvironmentBuilder.SharedMaterial(EnvironmentBuilder.TeamColor(index % 2));
            var bot = go.AddComponent<BotTarget>();
            bot.team = index % 2;
            bots.Add(bot);
        }
    }

    public class BotTarget : MonoBehaviour, IDamageable
    {
        public int team;
        private float health = 100f;
        private float nextShot;
        private void Update()
        {
            var gameMode = GameModeManager.Instance;
            if (gameMode == null || !NetworkManager.Singleton || !NetworkManager.Singleton.IsServer)
                return;
            NetworkPlayer target = null;
            float best = float.MaxValue;
            foreach (var player in FindObjectsByType<NetworkPlayer>())
            {
                if (player == null || !player.IsSpawned || player.IsDead.Value || player.Spectating.Value
                    || (gameMode.IsTeamMode && player.Team.Value == team)) continue;
                float d = (player.transform.position - transform.position).sqrMagnitude;
                if (d < best) { best = d; target = player; }
            }
            if (target == null) return;
            Vector3 delta = target.transform.position - transform.position;
            delta.y = 0f;
            if (delta.sqrMagnitude > 45f) transform.position += delta.normalized * 3.4f * Time.deltaTime;
            transform.rotation = Quaternion.LookRotation(delta.normalized);
            if (Time.time >= nextShot && delta.sqrMagnitude < 625f)
            {
                nextShot = Time.time + 0.65f;
                if (NetworkManager.Singleton.IsServer) target.GetComponent<NetworkHealth>()?.ServerBotDamage(9f);
            }
        }
        public void TakeDamage(float amount, bool headshot = false)
        {
            health -= headshot ? amount * 2f : amount;
            if (health <= 0f) Destroy(gameObject);
        }
    }
}
