using System.Collections;
using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Simple hitscan (raycast) gun. Hold Mouse1 for automatic fire, R to reload.
    /// Shows a muzzle flash, a short tracer line and an impact marker, and applies
    /// damage to anything with a Health component.
    /// </summary>
    public class Gun : MonoBehaviour
    {
        [Header("References (set by GameBootstrap)")]
        public Camera shootCamera;
        public Transform muzzle;
        public Light muzzleFlash;

        [Header("Stats")]
        public float damage = 25f;
        public float range = 200f;
        public float fireRate = 8f;      // shots per second
        public int magazineSize = 30;
        public float reloadTime = 1.5f;
        public float recoilPerShot = 0.8f;

        public int CurrentAmmo { get; private set; }
        public bool IsReloading { get; private set; }

        private float nextFireTime;
        private MouseLook mouseLook;
        private Material tracerMaterial;
        private Material impactMaterial;

        private void Awake()
        {
            CurrentAmmo = magazineSize;
            // Unlit shader so the tracer stays bright regardless of lighting.
            tracerMaterial = new Material(Shader.Find("Sprites/Default"));
            impactMaterial = GameBootstrap.MakeMaterial(new Color(1f, 0.8f, 0.2f));
        }

        private void Start()
        {
            if (shootCamera != null)
                mouseLook = shootCamera.GetComponent<MouseLook>();
        }

        private void Update()
        {
            // Don't shoot while the cursor is free (e.g. user pressed Escape).
            if (Cursor.lockState != CursorLockMode.Locked)
                return;

            if (Input.GetKeyDown(KeyCode.R) && !IsReloading && CurrentAmmo < magazineSize)
                StartCoroutine(Reload());

            if (IsReloading)
                return;

            if (Input.GetButton("Fire1") && Time.time >= nextFireTime)
            {
                if (CurrentAmmo > 0)
                {
                    nextFireTime = Time.time + 1f / fireRate;
                    Shoot();
                }
                else
                {
                    StartCoroutine(Reload());
                }
            }
        }

        private void Shoot()
        {
            CurrentAmmo--;

            // Ray from the center of the screen.
            Ray ray = shootCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            Vector3 hitPoint = ray.origin + ray.direction * range;

            if (Physics.Raycast(ray, out RaycastHit hit, range))
            {
                hitPoint = hit.point;

                // Damage anything that has a Health component (on itself or a parent).
                var health = hit.collider.GetComponentInParent<Health>();
                if (health != null)
                    health.TakeDamage(damage);

                SpawnImpactMarker(hit.point, hit.normal);
            }

            StartCoroutine(FlashMuzzle());
            SpawnTracer(muzzle != null ? muzzle.position : ray.origin, hitPoint);

            if (mouseLook != null)
                mouseLook.AddRecoil(recoilPerShot);
        }

        private IEnumerator Reload()
        {
            IsReloading = true;
            yield return new WaitForSeconds(reloadTime);
            CurrentAmmo = magazineSize;
            IsReloading = false;
        }

        private IEnumerator FlashMuzzle()
        {
            if (muzzleFlash != null)
            {
                muzzleFlash.enabled = true;
                yield return new WaitForSeconds(0.04f);
                muzzleFlash.enabled = false;
            }
        }

        private void SpawnTracer(Vector3 from, Vector3 to)
        {
            var go = new GameObject("Tracer");
            var line = go.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.startWidth = 0.02f;
            line.endWidth = 0.02f;
            line.material = tracerMaterial;
            line.startColor = new Color(1f, 0.9f, 0.4f);
            line.endColor = new Color(1f, 0.9f, 0.4f, 0.3f);
            Destroy(go, 0.05f);
        }

        private void SpawnImpactMarker(Vector3 point, Vector3 normal)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Impact";
            Destroy(marker.GetComponent<Collider>());
            marker.transform.position = point + normal * 0.02f;
            marker.transform.localScale = Vector3.one * 0.12f;
            marker.GetComponent<Renderer>().material = impactMaterial;
            Destroy(marker, 0.4f);
        }
    }
}
