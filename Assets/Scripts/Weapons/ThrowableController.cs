using System.Collections;
using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Equipment throwing. Four throwables on dedicated keys:
    ///   G = Frag (lethal)   F = Flashbang (tactical)
    ///   V = Sticky bomb     X = Throwing knife
    /// Only the local OWNER reads input (added to every player object, so an
    /// un-gated Update used to fire on every remote copy at once — that was the
    /// "everyone throws when only I pressed" bug). Throws are replicated through
    /// NetworkPlayer so other clients see them; only the thrower's own copy
    /// deals damage (server-routed via IDamageable, exactly like rockets).
    /// </summary>
    public class ThrowableController : MonoBehaviour
    {
        public Camera viewCamera;
        public Transform ownerRoot;

        public const int Count = 4;
        public static readonly string[] Names = { "FRAG", "FLASH", "STICKY", "KNIFE" };
        public static readonly KeyCode[] Keys = { KeyCode.G, KeyCode.F, KeyCode.V, KeyCode.X };
        private static readonly float[] Cooldown = { 3.5f, 6f, 5f, 1.2f };

        private readonly float[] readyAt = new float[Count];
        private NetworkPlayer net;
        private bool checkedNet;

        /// <summary>Remaining cooldown fraction 0..1 for the HUD (0 = ready).</summary>
        public float CooldownFraction(int type) =>
            Mathf.Clamp01((readyAt[type] - Time.time) / Cooldown[type]);

        private void Update()
        {
            if (!checkedNet)
            {
                net = GetComponent<NetworkPlayer>();
                checkedNet = true;
            }
            // Only the local player throws (offline: net is null → allowed).
            if (net != null && !net.IsOwner)
                return;
            if (Cursor.lockState != CursorLockMode.Locked || viewCamera == null)
                return;

            for (int type = 0; type < Count; type++)
                if (Input.GetKeyDown(Keys[type]))
                    TryThrow(type);
        }

        private void TryThrow(int type)
        {
            if (Time.time < readyAt[type])
                return;
            readyAt[type] = Time.time + Cooldown[type];

            Vector3 origin = viewCamera.transform.position + viewCamera.transform.forward * 0.7f;
            Vector3 velocity = viewCamera.transform.forward * (type == 3 ? 24f : 13f) + Vector3.up * 2.5f;

            // My own authoritative copy (deals damage), plus tell everyone else.
            Spawn(type, origin, velocity, ownerRoot, authoritative: true);
            if (net != null)
                net.SendThrow(type, origin, velocity);
        }

        /// <summary>Spawn a throwable. Authoritative copies deal damage; replica copies are visual only.</summary>
        public static void Spawn(int type, Vector3 origin, Vector3 velocity, Transform ownerRoot, bool authoritative)
        {
            var go = GameObject.CreatePrimitive(type == 3 ? PrimitiveType.Cube : PrimitiveType.Sphere);
            go.name = (type >= 0 && type < Names.Length ? Names[type] : "Throwable")
                      + (authoritative ? "" : " (replica)");
            go.transform.position = origin;
            go.transform.localScale = type == 3 ? new Vector3(0.05f, 0.05f, 0.3f) : Vector3.one * 0.18f;

            Object.Destroy(go.GetComponent<Collider>()); // re-add non-trigger below so physics still works
            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.12f;

            var rb = go.AddComponent<Rigidbody>();
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.linearVelocity = velocity;

            var mat = EnvironmentBuilder.SharedMaterial(
                type == 0 ? new Color(0.2f, 0.3f, 0.2f) :
                type == 1 ? new Color(0.85f, 0.85f, 0.5f) :
                type == 2 ? new Color(0.3f, 0.2f, 0.15f) : new Color(0.8f, 0.8f, 0.85f),
                0.4f, 0.5f);
            go.GetComponent<Renderer>().sharedMaterial = mat;

            var payload = go.AddComponent<ThrowablePayload>();
            payload.type = type;
            payload.ownerRoot = ownerRoot;
            payload.authoritative = authoritative;
        }
    }

    /// <summary>
    /// A live throwable. Every client simulates its own copy so the visuals,
    /// explosion and (for flashbangs) the local-player flash happen everywhere;
    /// only the authoritative copy applies damage, so nothing is double-counted.
    /// </summary>
    public class ThrowablePayload : MonoBehaviour
    {
        public int type;
        public Transform ownerRoot;
        public bool authoritative;
        private bool stuck;

        private IEnumerator Start()
        {
            yield return new WaitForSeconds(type == 3 ? 5f : 2.2f);
            if (this != null)
                Detonate();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (ownerRoot != null && collision.transform.root == ownerRoot.root)
                return;

            if (type == 3) // throwing knife: impact kill
            {
                if (authoritative)
                    collision.collider.GetComponentInParent<IDamageable>()?.TakeDamage(100f, false);
                Effects.SpawnExplosion(transform.position);
                Destroy(gameObject);
            }
            else if (type == 2 && !stuck) // sticky bomb: stick, then timed detonate
            {
                stuck = true;
                GetComponent<Rigidbody>().isKinematic = true;
                transform.SetParent(collision.transform, true);
            }
        }

        private void Detonate()
        {
            float radius = type == 1 ? 9f : 5f;
            foreach (var hit in Physics.OverlapSphere(transform.position, radius,
                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
            {
                if (ownerRoot != null && hit.transform.root == ownerRoot.root && type != 1)
                    continue; // your own frag/sticky won't hurt you; a flash still blinds you
                float falloff = 1f - Mathf.Clamp01(
                    Vector3.Distance(hit.transform.position, transform.position) / radius);

                if (type == 1)
                {
                    // Flash only the LOCAL player when they're in range (IsOwner
                    // is true only for this client's own player object).
                    var np = hit.GetComponentInParent<NetworkPlayer>();
                    if (np != null && np.IsOwner && HudOverlay.Local != null)
                        HudOverlay.Local.NotifyFlash(falloff);
                }
                else if (authoritative)
                {
                    hit.GetComponentInParent<IDamageable>()?.TakeDamage(100f * falloff, false);
                }
            }
            Effects.SpawnExplosion(transform.position);
            SfxSynth.PlayAt(SfxSynth.Explosion(), transform.position, type == 1 ? 0.5f : 0.9f);
            Destroy(gameObject);
        }
    }
}
