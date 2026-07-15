using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace FpsBase
{
    public class ZombieDirector : MonoBehaviour
    {
        private readonly List<GameObject> zombies = new List<GameObject>();
        private int wave;
        private float nextWave;
        private Material zombieMaterial;

        private void Start()
        {
            zombieMaterial = EnvironmentBuilder.SharedMaterial(new Color(0.16f, 0.32f, 0.12f), 0f, 0.25f, 0.15f);
            nextWave = Time.time + 2f;
        }

        private void Update()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
                return;
            var mode = GameModeManager.Instance;
            if (mode == null || mode.CurrentMode != GameMode.ZombieSurvival || mode.LobbyOpen.Value) return;
            zombies.RemoveAll(z => z == null);
            if (zombies.Count == 0 && Time.time >= nextWave)
            {
                wave++;
                for (int i = 0; i < 3 + wave * 2; i++) Spawn(i);
                nextWave = Time.time + 4f;
            }
            var players = FindObjectsByType<NetworkPlayer>();
            foreach (var zombie in zombies)
            {
                NetworkPlayer target = null;
                float best = float.MaxValue;
                foreach (var player in players)
                {
                    if (player.IsDead.Value || player.Spectating.Value) continue;
                    float d = (player.transform.position - zombie.transform.position).sqrMagnitude;
                    if (d < best) { best = d; target = player; }
                }
                if (target == null) continue;
                Vector3 delta = target.transform.position - zombie.transform.position;
                delta.y = 0f;
                zombie.transform.position += delta.normalized * (2.2f + wave * 0.08f) * Time.deltaTime;
                if (delta.sqrMagnitude < 2.1f && NetworkManager.Singleton.IsServer)
                    target.GetComponent<NetworkHealth>()?.ServerZombieDamage(18f * Time.deltaTime);
            }
        }

        private void Spawn(int index)
        {
            float angle = index * 2.39996f + wave;
            var zombie = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            zombie.name = "Zombie_W" + wave;
            zombie.transform.position = new Vector3(Mathf.Sin(angle) * 25f, 1f, Mathf.Cos(angle) * 25f);
            zombie.GetComponent<Renderer>().sharedMaterial = zombieMaterial;
            zombie.AddComponent<ZombieTarget>();
            zombies.Add(zombie);
        }
    }

    public class ZombieTarget : MonoBehaviour, IDamageable
    {
        private float health = 65f;
        public void TakeDamage(float amount, bool headshot = false)
        {
            health -= headshot ? amount * 2f : amount;
            if (health <= 0f) Destroy(gameObject);
        }
    }
}
