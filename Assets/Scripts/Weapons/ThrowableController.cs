using System.Collections;
using UnityEngine;

namespace FpsBase
{
    public class ThrowableController : MonoBehaviour
    {
        public Camera viewCamera;
        public Transform ownerRoot;
        private readonly float[] cooldowns = new float[4];

        private void Update()
        {
            if (Cursor.lockState != CursorLockMode.Locked || viewCamera == null) return;
            if (Input.GetKeyDown(KeyCode.G)) Throw(0);
            if (Input.GetKeyDown(KeyCode.F)) Throw(1);
            if (Input.GetKeyDown(KeyCode.V)) Throw(2);
            if (Input.GetKeyDown(KeyCode.X)) Throw(3);
        }

        private void Throw(int type)
        {
            if (Time.time < cooldowns[type]) return;
            cooldowns[type] = Time.time + 8f;
            var go = GameObject.CreatePrimitive(type == 3 ? PrimitiveType.Cube : PrimitiveType.Sphere);
            go.name = type == 0 ? "Grenade" : type == 1 ? "Flashbang" : type == 2 ? "StickyBomb" : "ThrowingKnife";
            go.transform.position = viewCamera.transform.position + viewCamera.transform.forward * 0.7f;
            go.transform.localScale = type == 3 ? new Vector3(0.05f, 0.05f, 0.3f) : Vector3.one * 0.18f;
            var rb = go.AddComponent<Rigidbody>();
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.linearVelocity = viewCamera.transform.forward * (type == 3 ? 24f : 13f) + Vector3.up * 2.5f;
            var payload = go.AddComponent<ThrowablePayload>();
            payload.type = type;
            payload.ownerRoot = ownerRoot;
        }
    }

    public class ThrowablePayload : MonoBehaviour
    {
        public int type;
        public Transform ownerRoot;
        private bool stuck;
        private IEnumerator Start()
        {
            yield return new WaitForSeconds(type == 3 ? 5f : 2.2f);
            if (this != null) Detonate();
        }
        private void OnCollisionEnter(Collision collision)
        {
            if (collision.transform.root == ownerRoot) return;
            if (type == 3)
            {
                collision.collider.GetComponentInParent<IDamageable>()?.TakeDamage(100f, false);
                Destroy(gameObject);
            }
            else if (type == 2 && !stuck)
            {
                stuck = true;
                GetComponent<Rigidbody>().isKinematic = true;
                transform.SetParent(collision.transform, true);
            }
        }
        private void Detonate()
        {
            float radius = type == 1 ? 9f : 5f;
            foreach (var hit in Physics.OverlapSphere(transform.position, radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
            {
                if (hit.transform.root == ownerRoot) continue;
                float falloff = 1f - Mathf.Clamp01(Vector3.Distance(hit.transform.position, transform.position) / radius);
                if (type == 1) HudOverlay.Local?.NotifyFlash(falloff);
                else hit.GetComponentInParent<IDamageable>()?.TakeDamage(100f * falloff, false);
            }
            Effects.SpawnExplosion(transform.position);
            SfxSynth.PlayAt(SfxSynth.Explosion(), transform.position, type == 1 ? 0.5f : 0.9f);
            Destroy(gameObject);
        }
    }
}
