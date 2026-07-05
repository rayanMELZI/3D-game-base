using System;
using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Manages the player's weapons: switching (1/2/3 or scroll wheel), firing
    /// (raycast hitscan), reloading, sniper zoom, recoil and shot effects.
    /// Damage goes through IDamageable, so the same code works offline (Health)
    /// and online (NetworkHealth routes it to the server).
    /// </summary>
    public class WeaponController : MonoBehaviour
    {
        [Header("References (set by PlayerFactory)")]
        public Camera shootCamera;
        public MouseLook mouseLook;
        public Transform viewmodelHolder;
        [Tooltip("Root of this player — its own colliders are ignored by shots.")]
        public Transform selfRoot;

        // Not serialized on purpose: the loadout always comes from
        // WeaponDefinition.CreateDefaultLoadout(), so balance edits in code apply
        // everywhere (including the baked multiplayer prefab).
        [NonSerialized]
        public WeaponDefinition[] weapons = WeaponDefinition.CreateDefaultLoadout();

        /// <summary>Raised after every local shot with the end point (for network replication).</summary>
        public event Action<Vector3> ShotFired;
        /// <summary>Raised when a shot damaged something; true = headshot (HUD hit marker + sound).</summary>
        public event Action<bool> TargetHit;

        public int CurrentIndex { get; private set; } = 1; // start with the rifle
        public WeaponDefinition CurrentWeapon => weapons[CurrentIndex];
        public int CurrentAmmo => ammo[CurrentIndex];
        public bool IsReloading => reloadPending;
        public bool IsZoomed { get; private set; }

        private WeaponModelInstance[] models;
        private int[] ammo;
        private float nextFireTime;
        private float reloadEndTime;
        private bool reloadPending;
        private float flashOffTime;
        private Vector3 currentKick; // viewmodel kickback offset

        private static Material tracerMaterial;
        private static Material impactMaterial;

        private void Start()
        {
            ammo = new int[weapons.Length];
            models = new WeaponModelInstance[weapons.Length];
            for (int i = 0; i < weapons.Length; i++)
            {
                ammo[i] = weapons[i].magazineSize;
                models[i] = WeaponModelBuilder.Build(weapons[i], viewmodelHolder, weapons[i].viewOffset, castShadows: false);
                models[i].root.SetActive(i == CurrentIndex);
            }
        }

        private void OnDisable()
        {
            // Clean state when control is taken away (death, match end, cursor free).
            SetZoom(false);
            reloadPending = false;
            if (models != null && models[CurrentIndex].muzzleFlash != null)
                models[CurrentIndex].muzzleFlash.enabled = false;
        }

        private void Update()
        {
            if (models == null)
                return;

            var weapon = CurrentWeapon;
            var model = models[CurrentIndex];

            // Muzzle flash timeout + viewmodel kick recovery run even while paused.
            if (model.muzzleFlash != null && model.muzzleFlash.enabled && Time.time >= flashOffTime)
                model.muzzleFlash.enabled = false;
            currentKick = Vector3.Lerp(currentKick, Vector3.zero, 12f * Time.deltaTime);
            model.root.transform.localPosition = weapon.viewOffset + currentKick;

            // Finish a pending reload.
            if (reloadPending && Time.time >= reloadEndTime)
            {
                ammo[CurrentIndex] = weapon.magazineSize;
                reloadPending = false;
            }

            if (Cursor.lockState != CursorLockMode.Locked)
            {
                SetZoom(false);
                return;
            }

            HandleSwitching();
            weapon = CurrentWeapon; // may have changed

            // Zoom (right mouse, only for weapons that have one).
            SetZoom(Input.GetMouseButton(1) && weapon.zoomFov > 0f && !reloadPending);

            // Manual reload.
            if (Input.GetKeyDown(KeyCode.R) && !reloadPending && ammo[CurrentIndex] < weapon.magazineSize)
                StartReload();

            if (reloadPending)
                return;

            bool firePressed = weapon.automatic ? Input.GetButton("Fire1") : Input.GetButtonDown("Fire1");
            if (firePressed && Time.time >= nextFireTime)
            {
                if (ammo[CurrentIndex] > 0)
                    Shoot();
                else
                    StartReload();
            }
        }

        // ------------------------------------------------------------------

        private void HandleSwitching()
        {
            int target = -1;
            if (Input.GetKeyDown(KeyCode.Alpha1)) target = 0;
            if (Input.GetKeyDown(KeyCode.Alpha2)) target = 1;
            if (Input.GetKeyDown(KeyCode.Alpha3)) target = 2;

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll > 0.01f) target = (CurrentIndex + 1) % weapons.Length;
            if (scroll < -0.01f) target = (CurrentIndex - 1 + weapons.Length) % weapons.Length;

            if (target >= 0 && target < weapons.Length && target != CurrentIndex)
                SwitchTo(target);
        }

        public void SwitchTo(int index)
        {
            SetZoom(false);
            reloadPending = false;
            models[CurrentIndex].root.SetActive(false);
            CurrentIndex = index;
            models[CurrentIndex].root.SetActive(true);
            nextFireTime = Time.time + 0.25f; // draw time
        }

        private void StartReload()
        {
            SetZoom(false);
            reloadPending = true;
            reloadEndTime = Time.time + CurrentWeapon.reloadTime;
            SfxSynth.Play2D(SfxSynth.Reload(), 0.6f);
        }

        private void SetZoom(bool zoom)
        {
            if (zoom == IsZoomed)
                return;
            IsZoomed = zoom;

            var weapon = CurrentWeapon;
            if (mouseLook != null)
                mouseLook.SetZoom(zoom ? weapon.zoomFov : 0f);
            if (weapon.hideWhenZoomed && models != null)
                models[CurrentIndex].root.SetActive(!zoom);
        }

        private void Shoot()
        {
            var weapon = CurrentWeapon;
            var model = models[CurrentIndex];

            ammo[CurrentIndex]--;
            nextFireTime = Time.time + 1f / weapon.fireRate;

            Ray ray = shootCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            Vector3 endPoint = ray.GetPoint(weapon.range);

            if (TryRaycastIgnoringSelf(ray, weapon.range, out RaycastHit hit))
            {
                endPoint = hit.point;

                var damageable = hit.collider.GetComponentInParent<IDamageable>();
                if (damageable != null)
                {
                    var hitbox = hit.collider.GetComponent<Hitbox>();
                    bool headshot = hitbox != null && hitbox.isHead;
                    damageable.TakeDamage(weapon.damage, headshot);
                    TargetHit?.Invoke(headshot);
                    SfxSynth.Play2D(headshot ? SfxSynth.Headshot() : SfxSynth.Hit(), 0.7f);
                }

                SpawnImpactMarker(hit.point, hit.normal);
            }

            // Effects.
            if (model.muzzleFlash != null && model.root.activeSelf)
            {
                model.muzzleFlash.enabled = true;
                flashOffTime = Time.time + 0.045f;
            }
            SpawnTracerLine(model.root.activeSelf ? model.muzzle.position : shootCamera.transform.position, endPoint);
            SfxSynth.PlayAt(SfxSynth.Shot(weapon.model), shootCamera.transform.position, 0.8f);
            currentKick += new Vector3(0, 0.01f, -0.07f);

            if (mouseLook != null)
                mouseLook.AddRecoil(weapon.recoil * (IsZoomed ? 0.6f : 1f));

            ShotFired?.Invoke(endPoint);
        }

        /// <summary>Raycast that skips the shooter's own colliders.</summary>
        private bool TryRaycastIgnoringSelf(Ray ray, float range, out RaycastHit best)
        {
            best = default;
            // Triggers included: head hitboxes are trigger colliders.
            var hits = Physics.RaycastAll(ray, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            float bestDistance = float.MaxValue;
            bool found = false;
            foreach (var hit in hits)
            {
                if (selfRoot != null && hit.collider.transform.root == selfRoot.root)
                    continue;
                if (hit.distance < bestDistance)
                {
                    bestDistance = hit.distance;
                    best = hit;
                    found = true;
                }
            }
            return found;
        }

        // ------------------------------------------------------------------
        // Shared shot effects (also used by the network layer for remote players)
        // ------------------------------------------------------------------

        public static void SpawnTracerLine(Vector3 from, Vector3 to)
        {
            if (tracerMaterial == null)
                tracerMaterial = new Material(Shader.Find("Sprites/Default"));

            var go = new GameObject("Tracer");
            var line = go.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.startWidth = 0.02f;
            line.endWidth = 0.02f;
            line.material = tracerMaterial;
            line.startColor = new Color(1f, 0.9f, 0.4f);
            line.endColor = new Color(1f, 0.9f, 0.4f, 0.25f);
            UnityEngine.Object.Destroy(go, 0.05f);
        }

        public static void SpawnImpactMarker(Vector3 point, Vector3 normal)
        {
            if (impactMaterial == null)
                impactMaterial = EnvironmentBuilder.MakeEmissiveMaterial(new Color(1f, 0.75f, 0.3f), 1.5f);

            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Impact";
            UnityEngine.Object.Destroy(marker.GetComponent<Collider>());
            marker.transform.position = point + normal * 0.02f;
            marker.transform.localScale = Vector3.one * 0.12f;
            marker.GetComponent<Renderer>().material = impactMaterial;
            marker.AddComponent<TimedShrink>().lifetime = 0.3f;
        }
    }
}
