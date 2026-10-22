using System.Collections.Generic;
using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// The grenade launcher's round: an arcing (gravity) projectile that explodes
    /// on impact with splash damage. Online, the SERVER's copy is the only one
    /// that deals damage (NetworkWeapon spawns it from the shooter's launch
    /// parameters); every other client flies a visual replica along the same
    /// path. Offline, the shooter's copy deals damage directly. The splash hurts
    /// the shooter too.
    /// </summary>
    public class GrenadeProjectile : MonoBehaviour
    {
        public enum DamageMode
        {
            None,   // visual replica
            Local,  // offline: damage through IDamageable
            Server, // online, on the server: credit goes to attackerId
        }

        private Vector3 velocity;
        private float gravity;
        private float damage;
        private float radius;
        private Transform ignoreRoot;
        private DamageMode mode;
        private ulong attackerId;
        private float life = 6f;

        public static void Launch(Vector3 origin, Vector3 velocity, float gravity, float damage, float radius,
            Transform ignoreRoot, DamageMode mode, ulong attackerId = 0)
        {
            var go = new GameObject(mode == DamageMode.None ? "Grenade (replica)" : "Grenade");
            go.transform.position = origin;
            if (velocity.sqrMagnitude > 0.0001f)
                go.transform.rotation = Quaternion.LookRotation(velocity);

            // 40 mm round: olive body, brass base, glowing fuse so it reads in flight.
            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(go.transform, false);
            body.transform.localRotation = Quaternion.Euler(90, 0, 0);
            body.transform.localScale = new Vector3(0.07f, 0.045f, 0.07f);
            body.GetComponent<Renderer>().sharedMaterial =
                EnvironmentBuilder.SharedMaterial(new Color(0.28f, 0.33f, 0.2f), 0.3f, 0.45f);

            var nose = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(nose.GetComponent<Collider>());
            nose.transform.SetParent(go.transform, false);
            nose.transform.localPosition = new Vector3(0, 0, 0.045f);
            nose.transform.localScale = new Vector3(0.07f, 0.07f, 0.06f);
            nose.GetComponent<Renderer>().sharedMaterial =
                EnvironmentBuilder.SharedMaterial(new Color(0.28f, 0.33f, 0.2f), 0.3f, 0.45f);

            var fuse = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(fuse.GetComponent<Collider>());
            fuse.transform.SetParent(go.transform, false);
            fuse.transform.localPosition = new Vector3(0, 0, -0.05f);
            fuse.transform.localScale = Vector3.one * 0.05f;
            fuse.GetComponent<Renderer>().sharedMaterial =
                EnvironmentBuilder.SharedMaterial(new Color(1f, 0.55f, 0.2f), emission: 2.5f);

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.6f, 0.25f);
            light.intensity = 1.6f;
            light.range = 4f;

            var grenade = go.AddComponent<GrenadeProjectile>();
            grenade.velocity = velocity;
            grenade.gravity = gravity;
            grenade.damage = damage;
            grenade.radius = radius;
            grenade.ignoreRoot = ignoreRoot;
            grenade.mode = mode;
            grenade.attackerId = attackerId;
        }

        private void Update()
        {
            life -= Time.deltaTime;
            if (life <= 0f)
            {
                Explode(transform.position);
                return;
            }

            float dt = Time.deltaTime;
            velocity += Vector3.down * gravity * dt;
            Vector3 step = velocity * dt;
            float stepLength = step.magnitude;
            if (stepLength < 0.0001f)
                return;
            Vector3 dir = step / stepLength;

            // Segment cast so the round never tunnels through walls.
            var hits = Physics.RaycastAll(transform.position, dir, stepLength + 0.05f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            RaycastHit best = default;
            float bestDistance = float.MaxValue;
            bool found = false;
            foreach (var hit in hits)
            {
                if (ignoreRoot != null && hit.collider.transform.root == ignoreRoot.root)
                    continue;
                // Triggers only stop it when they are hitboxes (head), not pads/zones.
                if (hit.collider.isTrigger && hit.collider.GetComponent<Hitbox>() == null)
                    continue;
                if (hit.distance < bestDistance)
                {
                    bestDistance = hit.distance;
                    best = hit;
                    found = true;
                }
            }

            if (found)
            {
                Explode(best.point + best.normal * 0.05f);
                return;
            }
            transform.position += step;
            transform.rotation = Quaternion.LookRotation(dir);
        }

        private void Explode(Vector3 point)
        {
            Effects.SpawnExplosion(point);
            SfxSynth.PlayAt(SfxSynth.Explosion(), point, 1f);
            if (mode != DamageMode.None)
                ApplySplash(point);
            Destroy(gameObject);
        }

        private void ApplySplash(Vector3 point)
        {
            // Closest collider of each target decides its falloff; each target is hit once.
            var closest = new Dictionary<IDamageable, float>();
            foreach (var col in Physics.OverlapSphere(point, radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
            {
                var damageable = col.GetComponentInParent<IDamageable>();
                if (damageable == null)
                    continue;
                float distance = Vector3.Distance(col.bounds.ClosestPoint(point), point);
                if (!closest.TryGetValue(damageable, out float previous) || distance < previous)
                    closest[damageable] = distance;
            }

            foreach (var pair in closest)
            {
                float falloff = Mathf.Clamp01(1f - pair.Value / radius) * 0.75f + 0.25f;
                float amount = damage * falloff;
                if (mode == DamageMode.Server && pair.Key is NetworkHealth networkHealth)
                    networkHealth.ServerExplosionDamage(amount, attackerId, point);
                else if (mode == DamageMode.Server && pair.Key is ZombieAgent zombie)
                    zombie.ServerExplosionDamage(amount, attackerId); // credit the shooter, not the host
                else if (mode == DamageMode.Server && pair.Key is BotAgent bot)
                    bot.ServerApplyDamage(amount, false, attackerId); // same for networked bots
                else
                    pair.Key.TakeDamage(amount, false);
            }
        }
    }
}
