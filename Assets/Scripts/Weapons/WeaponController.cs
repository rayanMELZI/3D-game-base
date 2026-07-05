using System;
using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Manages the player's weapons: switching (1–7 / scroll), firing (hitscan,
    /// shotgun pellets, melee swings or rockets), reloading with a viewmodel
    /// animation, aim-down-sights on every weapon (sniper scopes), recoil and
    /// effects. Damage goes through IDamageable so the same code works offline
    /// and online.
    /// </summary>
    public class WeaponController : MonoBehaviour
    {
        [Header("References (set by PlayerFactory)")]
        public Camera shootCamera;
        public MouseLook mouseLook;
        public Transform viewmodelHolder;
        [Tooltip("Root of this player — its own colliders are ignored by shots.")]
        public Transform selfRoot;
        [Tooltip("Body anchor for the shadows-only held weapon (so your shadow holds a gun).")]
        public Transform thirdPersonAnchor;

        // Not serialized on purpose: the loadout always comes from
        // WeaponDefinition.CreateDefaultLoadout(), so balance edits in code apply
        // everywhere (including the baked multiplayer prefab).
        [NonSerialized]
        public WeaponDefinition[] weapons = WeaponDefinition.CreateDefaultLoadout();

        /// <summary>Game modes can pin the weapon (Gun Game, sniper only).</summary>
        [NonSerialized]
        public bool lockSwitching;

        /// <summary>Raised after every local shot with the end point (for network replication).</summary>
        public event Action<Vector3> ShotFired;
        /// <summary>Raised when a shot damaged something; true = headshot (HUD hit marker + sound).</summary>
        public event Action<bool> TargetHit;

        public int CurrentIndex { get; private set; } = WeaponDefinition.DefaultIndex;
        public WeaponDefinition CurrentWeapon => weapons[CurrentIndex];
        public int CurrentAmmo => ammo[CurrentIndex];
        public bool IsReloading => reloadPending;
        /// <summary>0..1 while reloading (drives the HUD bar and viewmodel animation).</summary>
        public float ReloadProgress =>
            reloadPending ? 1f - Mathf.Clamp01((reloadEndTime - Time.time) / CurrentWeapon.reloadTime) : 0f;
        public bool IsZoomed { get; private set; }

        private WeaponModelInstance[] models;
        private WeaponModelInstance shadowModel; // shadows-only copy held at the body
        private bool hasShadowModel;
        private int[] ammo;
        private float nextFireTime;
        private float reloadEndTime;
        private bool reloadPending;
        private float flashOffTime;
        private float adsBlend;
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
            RebuildShadowModel();
        }

        /// <summary>Refill every magazine (called on respawn).</summary>
        public void ResetAmmo()
        {
            if (ammo == null)
                return;
            for (int i = 0; i < weapons.Length; i++)
                ammo[i] = weapons[i].magazineSize;
            reloadPending = false;
        }

        /// <summary>Force a specific weapon (Gun Game level, sniper-only).</summary>
        public void ForceWeapon(int index)
        {
            index = Mathf.Clamp(index, 0, weapons.Length - 1);
            if (index == CurrentIndex)
                return;
            if (models == null)
                CurrentIndex = index; // before Start: just set the starting weapon
            else
                SwitchTo(index);
        }

        private void OnDisable()
        {
            SetZoom(false);
            reloadPending = false;
            if (models != null && models[CurrentIndex].muzzleFlash != null)
                models[CurrentIndex].muzzleFlash.enabled = false;
            if (hasShadowModel)
                shadowModel.root.SetActive(false); // no floating gun shadow while dead
        }

        private void OnEnable()
        {
            if (hasShadowModel)
                shadowModel.root.SetActive(true);
        }

        private void Update()
        {
            if (models == null)
                return;

            var weapon = CurrentWeapon;
            var model = models[CurrentIndex];

            if (model.muzzleFlash != null && model.muzzleFlash.enabled && Time.time >= flashOffTime)
                model.muzzleFlash.enabled = false;

            // Finish a pending reload.
            if (reloadPending && Time.time >= reloadEndTime)
            {
                ammo[CurrentIndex] = weapon.magazineSize;
                reloadPending = false;
            }

            AnimateViewmodel(weapon, model);

            if (Cursor.lockState != CursorLockMode.Locked)
            {
                SetZoom(false);
                return;
            }

            if (!lockSwitching)
                HandleSwitching();
            weapon = CurrentWeapon; // may have changed

            // Aim down sights (every weapon; sniper scopes).
            SetZoom(Input.GetMouseButton(1) && weapon.zoomFov > 0f && !reloadPending);

            // Manual reload.
            if (Input.GetKeyDown(KeyCode.R) && CanReload(weapon))
                StartReload();

            if (reloadPending)
                return;

            bool firePressed = weapon.automatic ? Input.GetButton("Fire1") : Input.GetButtonDown("Fire1");
            if (firePressed && Time.time >= nextFireTime)
            {
                if (weapon.magazineSize <= 0 || ammo[CurrentIndex] > 0)
                    Shoot();
                else
                    StartReload();
            }
        }

        private bool CanReload(WeaponDefinition weapon) =>
            !reloadPending && weapon.magazineSize > 0 && ammo[CurrentIndex] < weapon.magazineSize;

        // ------------------------------------------------------------------
        // Viewmodel motion: ADS blend, kick recovery, reload dip
        // ------------------------------------------------------------------

        private void AnimateViewmodel(WeaponDefinition weapon, WeaponModelInstance model)
        {
            adsBlend = Mathf.MoveTowards(adsBlend, IsZoomed && !weapon.hideWhenZoomed ? 1f : 0f, 9f * Time.deltaTime);
            currentKick = Vector3.Lerp(currentKick, Vector3.zero, 12f * Time.deltaTime);

            Vector3 basePos = Vector3.Lerp(weapon.viewOffset, weapon.adsOffset, adsBlend);
            Quaternion baseRot = Quaternion.identity;

            // Reload: dip the weapon down and tilt it, smoothly in and out.
            float reload = ReloadProgress;
            if (reload > 0f)
            {
                float wave = Mathf.Sin(Mathf.Clamp01(reload) * Mathf.PI); // 0→1→0
                basePos += new Vector3(0, -0.09f * wave, -0.03f * wave);
                baseRot = Quaternion.Euler(38f * wave, 0f, 24f * wave);
            }

            model.root.transform.localPosition = basePos + currentKick;
            model.root.transform.localRotation = baseRot;
        }

        // ------------------------------------------------------------------
        // Switching
        // ------------------------------------------------------------------

        private void HandleSwitching()
        {
            int target = -1;
            for (int i = 0; i < weapons.Length && i < 9; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                    target = i;
            }

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
            RebuildShadowModel();
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

        // ------------------------------------------------------------------
        // Firing
        // ------------------------------------------------------------------

        private void Shoot()
        {
            var weapon = CurrentWeapon;
            var model = models[CurrentIndex];

            if (weapon.magazineSize > 0)
                ammo[CurrentIndex]--;
            nextFireTime = Time.time + 1f / weapon.fireRate;

            Vector3 endPoint;
            if (weapon.isProjectile)
                endPoint = FireRocket(weapon);
            else
                endPoint = FireHitscan(weapon);

            // Shared feedback.
            if (model.muzzleFlash != null && model.root.activeSelf && !weapon.isMelee)
            {
                model.muzzleFlash.enabled = true;
                flashOffTime = Time.time + 0.045f;
            }
            SfxSynth.PlayAt(SfxSynth.Shot(weapon.model), shootCamera.transform.position, 0.85f);
            currentKick += weapon.isMelee ? new Vector3(0, 0.01f, 0.12f) : new Vector3(0, 0.01f, -0.07f);
            if (mouseLook != null)
                mouseLook.AddRecoil(weapon.recoil * (IsZoomed ? 0.6f : 1f));

            ShotFired?.Invoke(endPoint);
        }

        private Vector3 FireHitscan(WeaponDefinition weapon)
        {
            Vector3 forward = shootCamera.transform.forward;
            Vector3 endPoint = shootCamera.transform.position + forward * weapon.range;
            bool anyHit = false;
            bool anyHeadshot = false;

            int pellets = Mathf.Max(1, weapon.pellets);
            float damagePerPellet = weapon.damage;

            for (int p = 0; p < pellets; p++)
            {
                Vector3 dir = forward;
                if (weapon.spreadDegrees > 0f && pellets > 1)
                {
                    Vector2 spread = UnityEngine.Random.insideUnitCircle
                        * Mathf.Tan(weapon.spreadDegrees * Mathf.Deg2Rad);
                    dir = (forward + shootCamera.transform.right * spread.x + shootCamera.transform.up * spread.y).normalized;
                }

                var ray = new Ray(shootCamera.transform.position, dir);
                Vector3 pelletEnd = ray.origin + dir * weapon.range;

                if (TryRaycastIgnoringSelf(ray, weapon.range, out RaycastHit hit))
                {
                    pelletEnd = hit.point;
                    var damageable = hit.collider.GetComponentInParent<IDamageable>();
                    if (damageable != null)
                    {
                        var hitbox = hit.collider.GetComponent<Hitbox>();
                        bool headshot = hitbox != null && hitbox.isHead;
                        damageable.TakeDamage(damagePerPellet, headshot);
                        anyHit = true;
                        anyHeadshot |= headshot;
                    }
                    SpawnImpactMarker(hit.point, hit.normal);
                }

                if (!weapon.isMelee)
                    SpawnTracerLine(CurrentViewMuzzle(), pelletEnd);
                if (p == 0)
                    endPoint = pelletEnd;
            }

            if (anyHit)
            {
                TargetHit?.Invoke(anyHeadshot);
                SfxSynth.Play2D(anyHeadshot ? SfxSynth.Headshot() : SfxSynth.Hit(), 0.7f);
            }
            return endPoint;
        }

        private Vector3 FireRocket(WeaponDefinition weapon)
        {
            RocketProjectile.Launch(
                CurrentViewMuzzle(), shootCamera.transform.forward,
                weapon.projectileSpeed, weapon.damage, weapon.explosionRadius, selfRoot);

            // Predicted end point for remote tracer/explosion effects.
            var ray = new Ray(shootCamera.transform.position, shootCamera.transform.forward);
            if (TryRaycastIgnoringSelf(ray, weapon.range, out RaycastHit hit))
                return hit.point;
            return ray.origin + ray.direction * weapon.range;
        }

        private Vector3 CurrentViewMuzzle()
        {
            var model = models[CurrentIndex];
            return model.root.activeSelf ? model.muzzle.position : shootCamera.transform.position;
        }

        /// <summary>Raycast that skips the shooter's own colliders (triggers included: hitboxes).</summary>
        private bool TryRaycastIgnoringSelf(Ray ray, float range, out RaycastHit best)
        {
            best = default;
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
        // Shadow model (your shadow holds the current gun)
        // ------------------------------------------------------------------

        private void RebuildShadowModel()
        {
            if (hasShadowModel)
            {
                Destroy(shadowModel.root);
                hasShadowModel = false;
            }
            if (thirdPersonAnchor == null)
                return;

            shadowModel = WeaponModelBuilder.Build(CurrentWeapon, thirdPersonAnchor, Vector3.zero, castShadows: true);
            foreach (var r in shadowModel.root.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            hasShadowModel = true;
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
            // HDR color > 1 so the tracer picks up bloom.
            line.startColor = new Color(1.8f, 1.5f, 0.7f);
            line.endColor = new Color(1.8f, 1.5f, 0.7f, 0.25f);
            UnityEngine.Object.Destroy(go, 0.05f);
        }

        public static void SpawnImpactMarker(Vector3 point, Vector3 normal)
        {
            if (impactMaterial == null)
                impactMaterial = EnvironmentBuilder.MakeEmissiveMaterial(new Color(1f, 0.75f, 0.3f), 2.2f);

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
