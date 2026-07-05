using System.Collections.Generic;
using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// The RPG rocket: a glowing projectile simulated by the shooter's client.
    /// On impact it explodes with area damage (through IDamageable, so it works
    /// offline and online — including self-damage for rocket jumps).
    /// </summary>
    public class RocketProjectile : MonoBehaviour
    {
        private float speed;
        private float damage;
        private float radius;
        private Transform ignoreRoot;
        private float life = 6f;

        public static void Launch(Vector3 origin, Vector3 direction, float speed, float damage, float radius, Transform ignoreRoot)
        {
            var go = new GameObject("Rocket");
            go.transform.position = origin;
            go.transform.rotation = Quaternion.LookRotation(direction);

            // Visual: small cylinder body + glowing tip + light.
            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(go.transform, false);
            body.transform.localRotation = Quaternion.Euler(90, 0, 0);
            body.transform.localScale = new Vector3(0.07f, 0.16f, 0.07f);
            body.GetComponent<Renderer>().material =
                EnvironmentBuilder.MakeMaterial(new Color(0.35f, 0.3f, 0.25f), 0.4f, 0.5f);

            var tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(tip.GetComponent<Collider>());
            tip.transform.SetParent(go.transform, false);
            tip.transform.localPosition = new Vector3(0, 0, 0.18f);
            tip.transform.localScale = Vector3.one * 0.09f;
            tip.GetComponent<Renderer>().material =
                EnvironmentBuilder.MakeEmissiveMaterial(new Color(1f, 0.6f, 0.2f), 2.5f);

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.6f, 0.25f);
            light.intensity = 2.5f;
            light.range = 6f;

            var rocket = go.AddComponent<RocketProjectile>();
            rocket.speed = speed;
            rocket.damage = damage;
            rocket.radius = radius;
            rocket.ignoreRoot = ignoreRoot;
        }

        private void Update()
        {
            life -= Time.deltaTime;
            if (life <= 0f)
            {
                Explode(transform.position);
                return;
            }

            float stepLength = speed * Time.deltaTime;

            // Segment cast so fast rockets never tunnel through walls.
            var hits = Physics.RaycastAll(transform.position, transform.forward,
                stepLength + 0.1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            RaycastHit best = default;
            float bestDistance = float.MaxValue;
            bool found = false;
            foreach (var hit in hits)
            {
                if (ignoreRoot != null && hit.collider.transform.root == ignoreRoot.root)
                    continue;
                if (hit.distance < bestDistance)
                {
                    bestDistance = hit.distance;
                    best = hit;
                    found = true;
                }
            }

            if (found)
                Explode(best.point);
            else
                transform.position += transform.forward * stepLength;
        }

        private void Explode(Vector3 point)
        {
            Effects.SpawnExplosion(point);
            SfxSynth.PlayAt(SfxSynth.Explosion(), point, 1f);

            // Area damage with distance falloff — each target damaged once.
            var damaged = new HashSet<IDamageable>();
            foreach (var col in Physics.OverlapSphere(point, radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
            {
                var damageable = col.GetComponentInParent<IDamageable>();
                if (damageable == null || damaged.Contains(damageable))
                    continue;
                damaged.Add(damageable);

                float distance = Vector3.Distance(col.transform.position, point);
                float falloff = Mathf.Clamp01(1f - distance / radius) * 0.75f + 0.25f;
                damageable.TakeDamage(damage * falloff, false);
            }

            Destroy(gameObject);
        }
    }
}
