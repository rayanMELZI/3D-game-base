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

        [Header("Viewmodel Motion")]
        [Tooltip("How quickly the gun settles into procedural poses.")]
        public float viewmodelFollowSpeed = 14f;
        [Tooltip("Extra local Y added at full ADS. Raise this to move the aimed weapon higher.")]
        public float adsVerticalOffset = 0.07f;
        [Tooltip("Slow, irregular motion while the player is standing still.")]
        public Vector3 idleSwayPosition = new Vector3(0.007f, 0.006f, 0.005f);
        public Vector3 idleSwayRotation = new Vector3(0.7f, 0.9f, 0.65f);
        [Tooltip("Multiplier applied to irregular sway while walking.")]
        public float walkingSwayMultiplier = 2.2f;
        public float walkBobSpeed = 9f;
        public Vector3 walkBobPosition = new Vector3(0.014f, 0.012f, 0.008f);
        [Tooltip("How far the gun trails opposite the player's movement input.")]
        public Vector2 movementLag = new Vector2(0.035f, 0.025f);

        [Header("Sprint Viewmodel Pose")]
        [Tooltip("Offset from the weapon's normal hip position while sprinting.")]
        public Vector3 sprintPositionOffset = new Vector3(0.04f, 0, 0);
        [Tooltip("Sideways, across-the-body rotation while sprinting.")]
        public Vector3 sprintRotation = new Vector3(39.5f, -45.3f, 28.2f);
        public float sprintBobSpeed = 11f;

        [Header("Weapon Switch Animation")]
        public float switchDuration = 0.5f;
        public Vector3 switchPositionOffset = new Vector3(0.05f, -0.36f, -0.18f);
        public Vector3 switchRotation = new Vector3(35f, 0f, 16f);

        [Header("Reload Viewmodel Animation")]
        [Tooltip("Weapon pose while the magazine and pull are being operated.")]
        public Vector3 reloadGunPositionOffset = new Vector3(0, 0, 0.15f);
        public Vector3 reloadGunRotation = new Vector3(10f, -20f, 0);
        [Tooltip("Magazine travel in weapon-root space. Negative Z moves it behind the camera/player.")]
        public Vector3 reloadMagazineOffset = new Vector3(-0.08f, -0.22f, -0.6f);
        public Vector3 reloadMagazineRotation = new Vector3(18f, 8f, 24f);
        [Tooltip("How far an imported *_pull part travels backward along the gun.")]
        public Vector3 reloadPullOffset = new Vector3(0f, 0f, -0.075f);
        public Vector3 reloadPullRotation = new Vector3(0f, 0f, -5f);

        // Not serialized on purpose: the loadout always comes from
        // WeaponDefinition.CreateDefaultLoadout(), so balance edits in code apply
        // everywhere (including the baked multiplayer prefab).
        [NonSerialized]
        public WeaponDefinition[] weapons = WeaponDefinition.CreateDefaultLoadout();

        /// <summary>Game modes can pin the weapon (Gun Game, sniper only).</summary>
        [NonSerialized]
        public bool lockSwitching;

        /// <summary>
        /// The player's class loadout as loadout indexes (knife, secondary,
        /// primary). Keys 1/2/3 map to these; other weapons can't be selected.
        /// Global weapon indexes stay untouched so network replication and Gun
        /// Game (which uses ForceWeapon) are unaffected.
        /// </summary>
        [NonSerialized]
        public int[] classSlots;

        /// <summary>Raised after every local shot with the end point (for network replication).</summary>
        public event Action<Vector3> ShotFired;
        /// <summary>Raised when a shot damaged something; true = headshot (HUD hit marker + sound).</summary>
        public event Action<bool> TargetHit;

        public int CurrentIndex { get; private set; } = WeaponDefinition.DefaultIndex;
        public WeaponDefinition CurrentWeapon => weapons[CurrentIndex];
        public int CurrentAmmo => ammo[CurrentIndex];
        /// <summary>Magazine size of the held weapon including its extended-mag add-on.</summary>
        public int CurrentMagSize => MagSize(CurrentIndex);
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
        private bool shellReload;
        private float reloadStepDuration;
        private float flashOffTime;
        private float adsBlend;
        private Vector3 currentKick; // viewmodel kickback offset
        private Vector3 currentKickRotation;
        private PlayerMovement movement;
        private Vector2 smoothedMoveInput;
        private float walkingBlend;
        private float sprintBlend;
        private float locomotionCycle;
        private float noiseSeed;
        private int pendingSwitchIndex = -1;
        private float switchAnimationStart;
        private bool switchSwapped;

        private static Material tracerMaterial;
        private static Material impactMaterial;

        private bool IsSwitching => pendingSwitchIndex >= 0;

        // Add-on helpers — the mask is chosen per weapon and lives in GameSettings.
        private int MaskFor(int index) =>
            index >= 0 && index < GameSettings.WeaponAttachments.Length ? GameSettings.WeaponAttachments[index] : 0;
        private int MagSize(int index) => Attachments.MagazineSize(weapons[index].magazineSize, MaskFor(index));

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            noiseSeed = transform.position.x * 13.37f + transform.position.z * 7.91f + 137.21f;
        }

        private void Start()
        {
            ammo = new int[weapons.Length];
            models = new WeaponModelInstance[weapons.Length];
            for (int i = 0; i < weapons.Length; i++)
            {
                ammo[i] = MagSize(i);
                models[i] = WeaponModelBuilder.Build(weapons[i], viewmodelHolder, weapons[i].viewOffset, castShadows: false, MaskFor(i), GameSettings.WeaponColors[i]);
                models[i].root.SetActive(i == CurrentIndex);
            }
            RebuildShadowModel();
            ApplySelectedClass();
        }

        /// <summary>
        /// Restrict switching to the selected class (knife + secondary + primary)
        /// and draw the primary. Called on spawn/respawn; Gun Game / sniper-only
        /// still override via ForceWeapon + lockSwitching.
        /// </summary>
        public void ApplySelectedClass()
        {
            if (!GameSettings.UseClassLoadout)
            {
                classSlots = null;
                if (models != null && !lockSwitching && CurrentIndex != WeaponDefinition.DefaultIndex)
                    SwitchTo(WeaponDefinition.DefaultIndex);
                return;
            }

            int c = Mathf.Clamp(GameSettings.SelectedClass, 0, GameSettings.ClassCount - 1);
            int primary = Mathf.Clamp(GameSettings.ClassPrimary[c], 0, weapons.Length - 1);
            int secondary = Mathf.Clamp(GameSettings.ClassSecondary[c], 0, weapons.Length - 1);
            classSlots = new[] { 0, secondary, primary }; // knife always in slot 1

            if (models != null && !lockSwitching && CurrentIndex != primary)
                SwitchTo(primary);
        }

        /// <summary>Refill every magazine (called on respawn).</summary>
        public void ResetAmmo()
        {
            if (ammo == null)
                return;
            for (int i = 0; i < weapons.Length; i++)
                ammo[i] = MagSize(i);
            reloadPending = false;
            shellReload = false;
            ResetReloadParts(models[CurrentIndex]);
        }

        /// <summary>Force a specific weapon (Gun Game level, sniper-only).</summary>
        public void ForceWeapon(int index)
        {
            index = Mathf.Clamp(index, 0, weapons.Length - 1);
            if (index == CurrentIndex || pendingSwitchIndex == index)
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
            shellReload = false;
            pendingSwitchIndex = -1;
            switchSwapped = false;
            if (models != null && models[CurrentIndex].root != null)
            {
                ResetReloadParts(models[CurrentIndex]);
                if (models[CurrentIndex].muzzleFlash != null)
                    models[CurrentIndex].muzzleFlash.enabled = false;
                // Hide the viewmodel too — while dead the camera belongs to the
                // kill cam, and a floating first-person gun ruins the replay.
                models[CurrentIndex].root.SetActive(false);
            }
            if (hasShadowModel && shadowModel.root != null)
                shadowModel.root.SetActive(false); // no floating gun shadow while dead
        }

        private void OnEnable()
        {
            if (models != null && models[CurrentIndex].root != null)
                models[CurrentIndex].root.SetActive(true);
            if (hasShadowModel && shadowModel.root != null)
                shadowModel.root.SetActive(true);
        }

        private void Update()
        {
            if (models == null)
                return;

            UpdateWeaponSwitch();

            var weapon = CurrentWeapon;
            var model = models[CurrentIndex];
            bool sprinting = movement != null && movement.IsSprinting;
            bool switching = IsSwitching;

            if (model.muzzleFlash != null && model.muzzleFlash.enabled && Time.time >= flashOffTime)
                model.muzzleFlash.enabled = false;

            // Finish a pending reload.
            if (reloadPending && Time.time >= reloadEndTime)
            {
                if (shellReload)
                {
                    ammo[CurrentIndex]++;
                    if (ammo[CurrentIndex] < MagSize(CurrentIndex)) reloadEndTime = Time.time + reloadStepDuration;
                    else { reloadPending = false; shellReload = false; }
                }
                else { ammo[CurrentIndex] = MagSize(CurrentIndex); reloadPending = false; }
            }

            // Sprinting always lowers ADS before the sprint pose is evaluated.
            if (sprinting || switching)
                SetZoom(false);

            AnimateViewmodel(weapon, model, sprinting);

            if (Cursor.lockState != CursorLockMode.Locked)
            {
                SetZoom(false);
                return;
            }

            if (!lockSwitching)
                HandleSwitching();
            weapon = CurrentWeapon; // may have changed
            switching = IsSwitching;

            // Aim down sights (every weapon; sniper scopes).
            SetZoom((Input.GetMouseButton(1) || Input.GetKey(KeyCode.JoystickButton4))
                && weapon.zoomFov > 0f && !reloadPending && !sprinting && !switching);

            // Manual reload.
            if (!switching && (Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.JoystickButton2)) && CanReload(weapon))
                StartReload();

            if (reloadPending)
            {
                if (!sprinting && !switching && shellReload && ammo[CurrentIndex] > 0 && Input.GetMouseButtonDown(0))
                {
                    reloadPending = false;
                    shellReload = false;
                    Shoot();
                }
                return;
            }

            // Left mouse only — the legacy "Fire1" axis also maps Left Ctrl,
            // which made crouching fire the weapon.
            bool controllerFire = Input.GetKey(KeyCode.JoystickButton5);
            bool firePressed = weapon.automatic
                ? (Input.GetMouseButton(0) || controllerFire)
                : (Input.GetMouseButtonDown(0) || (controllerFire && Time.time >= nextFireTime));
            if (!sprinting && !switching && firePressed && Time.time >= nextFireTime)
            {
                if (weapon.magazineSize <= 0 || ammo[CurrentIndex] > 0)
                    Shoot();
                else
                    StartReload();
            }
        }

        private bool CanReload(WeaponDefinition weapon) =>
            !reloadPending && !IsSwitching && weapon.magazineSize > 0 && ammo[CurrentIndex] < CurrentMagSize;

        // ------------------------------------------------------------------
        // Viewmodel motion: idle sway, movement lag/bob, ADS, recoil and sprint pose
        // ------------------------------------------------------------------

        private void AnimateViewmodel(WeaponDefinition weapon, WeaponModelInstance model, bool sprinting)
        {
            float deltaTime = Time.deltaTime;
            adsBlend = Mathf.MoveTowards(adsBlend, IsZoomed && !weapon.hideWhenZoomed ? 1f : 0f, 9f * deltaTime);
            sprintBlend = Mathf.MoveTowards(sprintBlend, sprinting ? 1f : 0f, 7f * deltaTime);
            currentKick = Vector3.Lerp(currentKick, Vector3.zero, 13f * deltaTime);
            currentKickRotation = Vector3.Lerp(currentKickRotation, Vector3.zero, 15f * deltaTime);

            float speed01 = movement != null
                ? Mathf.Clamp01(movement.CurrentHorizontalSpeed / Mathf.Max(0.01f, movement.walkSpeed))
                : 0f;
            float walkingTarget = !sprinting && speed01 > 0.05f ? speed01 : 0f;
            walkingBlend = Mathf.MoveTowards(walkingBlend, walkingTarget, 6f * deltaTime);

            Vector2 moveInput = movement != null ? Vector2.ClampMagnitude(movement.MoveInput, 1f) : Vector2.zero;
            smoothedMoveInput = Vector2.Lerp(smoothedMoveInput, moveInput, 10f * deltaTime);
            locomotionCycle += deltaTime * Mathf.Lerp(2f, walkBobSpeed, walkingBlend);

            Vector3 adsPosition = weapon.adsOffset + Vector3.up * adsVerticalOffset;
            Vector3 basePos = Vector3.Lerp(weapon.viewOffset, adsPosition, adsBlend);
            Vector3 baseEuler = Vector3.zero;

            // Perlin noise gives idle motion a drifting, non-repeating feel instead
            // of making the weapon follow an obvious sine wave. Walking amplifies it.
            float noiseTime = Time.time * 0.55f;
            Vector3 noise = new Vector3(
                SignedNoise(noiseSeed + 11.3f, noiseTime),
                SignedNoise(noiseSeed + 29.7f, noiseTime * 0.87f),
                SignedNoise(noiseSeed + 47.1f, noiseTime * 0.73f));
            float swayScale = Mathf.Lerp(1f, walkingSwayMultiplier, walkingBlend);
            float adsMotionScale = Mathf.Lerp(1f, 0.18f, adsBlend);
            Vector3 proceduralPos = Vector3.Scale(noise, idleSwayPosition) * swayScale;
            Vector3 proceduralEuler = Vector3.Scale(noise, idleSwayRotation) * swayScale;

            // A soft step rhythm rides on top of the irregular walking sway.
            float step = Mathf.Sin(locomotionCycle);
            float doubleStep = Mathf.Cos(locomotionCycle * 2f);
            proceduralPos += new Vector3(
                step * walkBobPosition.x,
                -Mathf.Abs(step) * walkBobPosition.y,
                doubleStep * walkBobPosition.z) * walkingBlend;
            proceduralEuler += new Vector3(
                -doubleStep * 0.8f,
                step * 1.15f,
                -step * 1.8f) * walkingBlend;

            // The held weapon has inertia: it trails opposite the direction of travel.
            proceduralPos += new Vector3(
                -smoothedMoveInput.x * movementLag.x,
                0f,
                -smoothedMoveInput.y * movementLag.y) * walkingBlend;
            proceduralEuler += new Vector3(
                smoothedMoveInput.y * 0.9f,
                -smoothedMoveInput.x * 2.2f,
                smoothedMoveInput.x * 2.8f) * walkingBlend;

            proceduralPos *= adsMotionScale;
            proceduralEuler *= adsMotionScale;

            // Tilt into a readable reload pose, operate the named model parts,
            // then blend the entire weapon back to its normal pose.
            float reload = ReloadAnimationProgress();
            float reloadPoseWeight = Phase(reload, 0f, 0.12f)
                * (1f - Phase(reload, 0.88f, 1f));
            basePos += reloadGunPositionOffset * reloadPoseWeight;
            baseEuler += reloadGunRotation * reloadPoseWeight;

            Vector3 normalPosition = basePos + proceduralPos + currentKick;
            Quaternion normalRotation = Quaternion.Euler(baseEuler + proceduralEuler + currentKickRotation);

            // Sideways across the body, with a faster running cadence. This pose
            // wins over normal sway/reload motion as sprintBlend reaches one.
            float sprintTime = Time.time * sprintBobSpeed;
            Vector3 sprintPosition = weapon.viewOffset + sprintPositionOffset + new Vector3(
                Mathf.Sin(sprintTime * 0.5f) * 0.014f,
                Mathf.Abs(Mathf.Sin(sprintTime)) * 0.018f,
                Mathf.Cos(sprintTime) * 0.012f);
            Quaternion sprintPoseRotation = Quaternion.Euler(sprintRotation + new Vector3(
                Mathf.Sin(sprintTime) * 2.5f,
                Mathf.Cos(sprintTime * 0.5f) * 2f,
                Mathf.Sin(sprintTime * 0.5f) * 3.5f));

            Vector3 targetPosition = Vector3.Lerp(normalPosition, sprintPosition, sprintBlend);
            Quaternion targetRotation = Quaternion.Slerp(normalRotation, sprintPoseRotation, sprintBlend);

            // Switching lowers the outgoing weapon completely, swaps models at
            // the midpoint, then raises the incoming weapon from the same pose.
            float switchWeight = WeaponSwitchPoseWeight();
            Vector3 switchPosition = weapon.viewOffset + switchPositionOffset;
            Quaternion switchPoseRotation = Quaternion.Euler(switchRotation);
            targetPosition = Vector3.Lerp(targetPosition, switchPosition, switchWeight);
            targetRotation = Quaternion.Slerp(targetRotation, switchPoseRotation, switchWeight);

            float follow = 1f - Mathf.Exp(-viewmodelFollowSpeed * deltaTime);
            model.root.transform.localPosition = Vector3.Lerp(model.root.transform.localPosition, targetPosition, follow);
            model.root.transform.localRotation = Quaternion.Slerp(model.root.transform.localRotation, targetRotation, follow);
            AnimateReloadParts(model, reload);
        }

        private static float SignedNoise(float seed, float time) =>
            Mathf.PerlinNoise(seed, time) * 2f - 1f;

        private float ReloadAnimationProgress()
        {
            if (!reloadPending)
                return 0f;
            float duration = shellReload ? reloadStepDuration : CurrentWeapon.reloadTime;
            return 1f - Mathf.Clamp01((reloadEndTime - Time.time) / Mathf.Max(0.01f, duration));
        }

        private void AnimateReloadParts(WeaponModelInstance model, float progress)
        {
            float magazineWeight = shellReload
                ? 0f
                : Envelope(progress, 0.1f, 0.34f, 0.5f, 0.7f);
            float pullWeight = shellReload
                ? Envelope(progress, 0.5f, 0.68f, 0.72f, 0.92f)
                : Envelope(progress, 0.72f, 0.82f, 0.86f, 0.98f);

            SetAnimatedPartPose(
                model.root.transform,
                model.magazine,
                model.magazineRestPosition,
                model.magazineRestRotation,
                reloadMagazineOffset,
                reloadMagazineRotation,
                magazineWeight);
            SetAnimatedPartPose(
                model.root.transform,
                model.pull,
                model.pullRestPosition,
                model.pullRestRotation,
                reloadPullOffset,
                reloadPullRotation,
                pullWeight);
        }

        private static void ResetReloadParts(WeaponModelInstance model)
        {
            if (model.magazine != null)
            {
                model.magazine.localPosition = model.magazineRestPosition;
                model.magazine.localRotation = model.magazineRestRotation;
            }
            if (model.pull != null)
            {
                model.pull.localPosition = model.pullRestPosition;
                model.pull.localRotation = model.pullRestRotation;
            }
        }

        private static void SetAnimatedPartPose(
            Transform weaponRoot,
            Transform part,
            Vector3 restPosition,
            Quaternion restRotation,
            Vector3 rootSpaceOffset,
            Vector3 rotation,
            float weight)
        {
            if (part == null)
                return;

            part.localPosition = restPosition;
            part.localRotation = restRotation * Quaternion.Euler(rotation * weight);
            if (weight > 0f)
                part.position += weaponRoot.TransformVector(rootSpaceOffset * weight);
        }

        private static float Phase(float value, float start, float end) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, value));

        private static float Envelope(float value, float inStart, float inEnd, float outStart, float outEnd)
        {
            if (value <= inStart || value >= outEnd)
                return 0f;
            if (value < inEnd)
                return Phase(value, inStart, inEnd);
            if (value <= outStart)
                return 1f;
            return 1f - Phase(value, outStart, outEnd);
        }

        // ------------------------------------------------------------------
        // Switching
        // ------------------------------------------------------------------

        private void UpdateWeaponSwitch()
        {
            if (!IsSwitching)
                return;

            float duration = Mathf.Max(0.1f, switchDuration);
            float progress = Mathf.Clamp01((Time.time - switchAnimationStart) / duration);
            if (!switchSwapped && progress >= 0.5f)
            {
                ResetReloadParts(models[CurrentIndex]);
                models[CurrentIndex].root.SetActive(false);
                CurrentIndex = pendingSwitchIndex;
                ResetReloadParts(models[CurrentIndex]);
                models[CurrentIndex].root.SetActive(true);
                SetWeaponSwitchPose(models[CurrentIndex], weapons[CurrentIndex]);
                switchSwapped = true;
                RebuildShadowModel();
            }

            if (progress >= 1f)
            {
                pendingSwitchIndex = -1;
                switchSwapped = false;
            }
        }

        private float WeaponSwitchPoseWeight()
        {
            if (!IsSwitching)
                return 0f;

            float progress = Mathf.Clamp01(
                (Time.time - switchAnimationStart) / Mathf.Max(0.1f, switchDuration));
            return progress < 0.5f
                ? Phase(progress, 0f, 0.5f)
                : 1f - Phase(progress, 0.5f, 1f);
        }

        private void SetWeaponSwitchPose(WeaponModelInstance model, WeaponDefinition weapon)
        {
            model.root.transform.localPosition = weapon.viewOffset + switchPositionOffset;
            model.root.transform.localRotation = Quaternion.Euler(switchRotation);
        }

        private void HandleSwitching()
        {
            if (IsSwitching)
                return;

            // With a class: keys 1/2/3 = knife/secondary/primary, scroll cycles
            // the class slots. Without one (safety fallback): the full arsenal.
            var slots = classSlots;
            int slotCount = slots != null ? slots.Length : weapons.Length;
            int currentSlot = 0;
            if (slots != null)
            {
                for (int i = 0; i < slots.Length; i++)
                    if (slots[i] == CurrentIndex)
                        currentSlot = i;
            }

            int target = -1;
            for (int i = 0; i < slotCount && i < 9; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                    target = slots != null ? slots[i] : i;
            }

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (slots != null)
            {
                if (scroll > 0.01f) target = slots[(currentSlot + 1) % slots.Length];
                if (scroll < -0.01f) target = slots[(currentSlot - 1 + slots.Length) % slots.Length];
            }
            else
            {
                if (scroll > 0.01f) target = (CurrentIndex + 1) % weapons.Length;
                if (scroll < -0.01f) target = (CurrentIndex - 1 + weapons.Length) % weapons.Length;
            }

            if (target >= 0 && target < weapons.Length && target != CurrentIndex)
                SwitchTo(target);
        }

        public void SwitchTo(int index)
        {
            index = Mathf.Clamp(index, 0, weapons.Length - 1);
            if (models == null)
            {
                CurrentIndex = index;
                return;
            }
            if (index == CurrentIndex || pendingSwitchIndex == index)
                return;

            // A forced mode change can supersede an incoming weapon. Once the
            // first swap has happened, begin a fresh lower/swap/raise cycle.
            if (IsSwitching && !switchSwapped)
            {
                pendingSwitchIndex = index;
                return;
            }
            pendingSwitchIndex = -1;
            switchSwapped = false;

            SetZoom(false);
            ResetReloadParts(models[CurrentIndex]);
            reloadPending = false;
            shellReload = false;
            pendingSwitchIndex = index;
            switchAnimationStart = Time.time;
            nextFireTime = Time.time + Mathf.Max(0.1f, switchDuration) + 0.05f;
        }

        private void StartReload()
        {
            SetZoom(false);
            ResetReloadParts(models[CurrentIndex]);
            reloadPending = true;
            shellReload = CurrentWeapon.model == WeaponModelType.Shotgun;
            reloadStepDuration = shellReload ? 0.58f : CurrentWeapon.reloadTime;
            reloadEndTime = Time.time + reloadStepDuration;
            SfxSynth.PlayReload(CurrentWeapon.model, reloadStepDuration, 0.6f);
        }

        private void SetZoom(bool zoom)
        {
            if (zoom == IsZoomed)
                return;
            IsZoomed = zoom;

            var weapon = CurrentWeapon;
            if (mouseLook != null)
                mouseLook.SetZoom(zoom ? weapon.zoomFov * Attachments.ZoomFovMultiplier(MaskFor(CurrentIndex)) : 0f);
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
            int mask = MaskFor(CurrentIndex);
            bool suppressed = Attachments.Has(mask, AttachmentType.Suppressor);
            SfxSynth.PlayAt(SfxSynth.Shot(weapon.model, suppressed), shootCamera.transform.position, 0.85f * Attachments.ShotVolumeMultiplier(mask));
            float recoilScale = Attachments.RecoilMultiplier(mask);
            float visualRecoil = Mathf.Clamp(weapon.recoil * recoilScale, 0.35f, 3f);
            if (weapon.isMelee)
            {
                currentKick += new Vector3(0.025f, 0.015f, 0.12f);
                currentKickRotation += new Vector3(-5f, -4f, -7f);
            }
            else
            {
                currentKick += new Vector3(
                    UnityEngine.Random.Range(-0.004f, 0.004f) * visualRecoil,
                    0.006f * visualRecoil,
                    -0.038f * visualRecoil);
                currentKickRotation += new Vector3(
                    -2.4f * visualRecoil,
                    UnityEngine.Random.Range(-0.45f, 0.45f) * visualRecoil,
                    UnityEngine.Random.Range(-0.35f, 0.35f) * visualRecoil);
                currentKick.x = Mathf.Clamp(currentKick.x, -0.025f, 0.025f);
                currentKick.y = Mathf.Clamp(currentKick.y, 0f, 0.045f);
                currentKick.z = Mathf.Clamp(currentKick.z, -0.14f, 0f);
                currentKickRotation.x = Mathf.Clamp(currentKickRotation.x, -12f, 0f);
                currentKickRotation.y = Mathf.Clamp(currentKickRotation.y, -4f, 4f);
                currentKickRotation.z = Mathf.Clamp(currentKickRotation.z, -3f, 3f);
            }
            if (mouseLook != null)
                mouseLook.AddRecoil(weapon.recoil * (IsZoomed ? 0.6f : 1f) * recoilScale);

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
                        if (damageable is NetworkHealth networkHealth)
                            networkHealth.TakeDamageShot(damagePerPellet, headshot,
                                weapon.model == WeaponModelType.Sniper && !IsZoomed);
                        else
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
            // Aim from the barrel toward where the crosshair points, not straight
            // along the camera (which sits behind/above the gun).
            Vector3 muzzle = CurrentViewMuzzle();
            Vector3 aimPoint = CameraAimPoint(weapon.range);
            Vector3 dir = (aimPoint - muzzle).sqrMagnitude > 0.01f
                ? (aimPoint - muzzle).normalized
                : shootCamera.transform.forward;

            RocketProjectile.Launch(
                muzzle, dir,
                weapon.projectileSpeed, weapon.damage, weapon.explosionRadius, selfRoot);
            return aimPoint;
        }

        /// <summary>Where the crosshair points: first thing the camera ray hits, or a far point.</summary>
        private Vector3 CameraAimPoint(float range)
        {
            var ray = new Ray(shootCamera.transform.position, shootCamera.transform.forward);
            if (TryRaycastIgnoringSelf(ray, range, out RaycastHit hit))
                return hit.point;
            return ray.origin + ray.direction * range;
        }

        /// <summary>
        /// The live gun barrel to spawn tracers/rockets from: the first-person
        /// viewmodel muzzle when it's shown, else the body-held weapon's muzzle
        /// (third-person / P Story), else the camera as a last resort.
        /// </summary>
        private Vector3 CurrentViewMuzzle()
        {
            var model = models[CurrentIndex];
            // activeInHierarchy (not activeSelf): when the viewmodel holder is
            // disabled for third-person, the model's own activeSelf is still true,
            // so activeSelf wrongly returned the hidden viewmodel muzzle at the
            // camera. In hierarchy terms it's inactive, so we fall through to the
            // body-held weapon's muzzle instead.
            if (model.root != null && model.root.activeInHierarchy && model.muzzle != null)
                return model.muzzle.position;
            if (hasShadowModel && shadowModel.muzzle != null)
                return shadowModel.muzzle.position;
            return shootCamera.transform.position;
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

        private bool thirdPersonView;

        /// <summary>
        /// Third-person mode (P Story): hide the first-person viewmodel and make
        /// the body-held weapon fully visible (it's normally shadows-only), so the
        /// character is seen holding the gun instead of a floating viewmodel.
        /// </summary>
        public void SetThirdPersonView(bool on)
        {
            if (thirdPersonView == on)
                return;
            thirdPersonView = on;
            if (viewmodelHolder != null)
                viewmodelHolder.gameObject.SetActive(!on);
            ApplyShadowModelVisibility();
        }

        private void ApplyShadowModelVisibility()
        {
            if (!hasShadowModel || shadowModel.root == null)
                return;
            var mode = thirdPersonView
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            foreach (var r in shadowModel.root.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = mode;
        }

        private void RebuildShadowModel()
        {
            if (hasShadowModel)
            {
                Destroy(shadowModel.root);
                hasShadowModel = false;
            }
            if (thirdPersonAnchor == null)
                return;

            shadowModel = WeaponModelBuilder.Build(CurrentWeapon, thirdPersonAnchor, Vector3.zero, castShadows: true, MaskFor(CurrentIndex), GameSettings.WeaponColors[CurrentIndex]);
            hasShadowModel = true;
            ApplyShadowModelVisibility(); // shadows-only normally, fully visible in third-person
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
