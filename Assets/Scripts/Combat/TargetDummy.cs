using System.Collections;
using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// A shootable practice dummy. When its Health reaches zero it "dies"
    /// (falls over and fades out of play) and respawns a few seconds later.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class TargetDummy : MonoBehaviour
    {
        public float respawnDelay = 3f;

        private Health health;
        private Renderer[] renderers;
        private Collider[] colliders;
        private Color[] baseColors;
        private Quaternion uprightRotation;
        private Coroutine flashRoutine;

        private void Awake()
        {
            health = GetComponent<Health>();
            renderers = GetComponentsInChildren<Renderer>();
            colliders = GetComponentsInChildren<Collider>();
            uprightRotation = transform.rotation;

            health.OnDeath += HandleDeath;
            health.OnDamaged += HandleDamaged;
        }

        private void Start()
        {
            // Cache original colors (in Start so bootstrap-assigned materials are in place).
            baseColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
                baseColors[i] = renderers[i].material.color;
        }

        private void HandleDamaged(float amount)
        {
            if (flashRoutine != null)
                StopCoroutine(flashRoutine);
            flashRoutine = StartCoroutine(HitFlash());
        }

        private IEnumerator HitFlash()
        {
            foreach (var r in renderers)
                r.material.color = Color.white;

            yield return new WaitForSeconds(0.07f);

            for (int i = 0; i < renderers.Length; i++)
                renderers[i].material.color = baseColors[i];
            flashRoutine = null;
        }

        private void HandleDeath()
        {
            StartCoroutine(DeathAndRespawn());
        }

        private IEnumerator DeathAndRespawn()
        {
            // Fall over.
            transform.rotation = uprightRotation * Quaternion.Euler(90f, 0f, 0f);
            foreach (var c in colliders)
                c.enabled = false;

            yield return new WaitForSeconds(respawnDelay);

            // Stand back up, fully healed.
            transform.rotation = uprightRotation;
            foreach (var c in colliders)
                c.enabled = true;
            health.ResetHealth();
        }
    }
}
