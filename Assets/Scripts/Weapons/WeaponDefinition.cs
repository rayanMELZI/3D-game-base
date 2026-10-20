using System;
using UnityEngine;

namespace FpsBase
{
    public enum WeaponModelType
    {
        Knife,
        Pistol,
        Smg,
        Shotgun,
        Rifle,
        Sniper,
        Rpg,
        Lmg,
        GrenadeLauncher,
    }

    /// <summary>
    /// Plain data describing one weapon. The default loadout is defined here;
    /// tweak numbers or add entries to change the game's arsenal.
    /// (For bigger games, turn this into a ScriptableObject.)
    /// </summary>
    [Serializable]
    public class WeaponDefinition
    {
        public string displayName = "WEAPON";
        public WeaponModelType model = WeaponModelType.Rifle;
        [Tooltip("Imported model under Resources/Weapons (e.g. \"Modern/Rifle_2_Used\"). " +
                 "Empty = the default model of this weapon type. Fit tuned in WeaponModelBuilder.FitFor.")]
        public string modelName = "";

        public float damage = 20f;
        public float fireRate = 8f;          // shots per second
        public float range = 200f;
        [Tooltip("0 = melee/no ammo (knife).")]
        public int magazineSize = 30;
        public float reloadTime = 1.5f;
        public bool automatic = true;        // hold vs click per shot
        public float recoil = 1f;            // camera kick per shot (degrees)

        [Header("Special behaviour")]
        public bool isMelee = false;
        [Tooltip("Pellets per shot (shotgun).")]
        public int pellets = 1;
        [Tooltip("Random cone spread in degrees (shotgun pellets).")]
        public float spreadDegrees = 0f;
        [Tooltip("Fires a rocket instead of a hitscan ray.")]
        public bool isProjectile = false;
        public float explosionRadius = 0f;
        public float projectileSpeed = 28f;

        [Header("Aiming")]
        [Tooltip("Right-click FOV. Every weapon can aim; the sniper scopes.")]
        public float zoomFov = 48f;
        [Tooltip("Hide the viewmodel while zoomed (sniper scope).")]
        public bool hideWhenZoomed = false;
        [Tooltip("Viewmodel position relative to the camera (hip).")]
        public Vector3 viewOffset = new Vector3(0.26f, -0.24f, 0.42f);
        [Tooltip("Viewmodel position while aiming down sights (centered).")]
        public Vector3 adsOffset = new Vector3(0f, -0.17f, 0.5f);

        /// <summary>Index of the weapon players spawn with (rifle).</summary>
        public const int DefaultIndex = 4;

        public static WeaponDefinition[] CreateDefaultLoadout()
        {
            return new[]
            {
                new WeaponDefinition
                {
                    displayName = "KNIFE",
                    model = WeaponModelType.Knife,
                    isMelee = true, magazineSize = 0,
                    damage = 100f, fireRate = 1.8f, range = 2.4f, // one-shot, like the sniper
                    automatic = false, recoil = 0.2f, zoomFov = 0f,
                    viewOffset = new Vector3(0.28f, -0.24f, 0.45f),
                },
                new WeaponDefinition
                {
                    displayName = "PISTOL",
                    model = WeaponModelType.Pistol,
                    // Semi-auto, near click-speed cap: fast trigger fingers win.
                    damage = 20f, fireRate = 15f, range = 120f,
                    magazineSize = 12, reloadTime = 1.1f,
                    automatic = false, recoil = 1.1f,
                    zoomFov = 48f,
                    viewOffset = new Vector3(0.24f, -0.21f, 0.38f),
                    adsOffset = new Vector3(0f, -0.15f, 0.4f),
                },
                new WeaponDefinition
                {
                    displayName = "SMG",
                    model = WeaponModelType.Smg,
                    damage = 14f, fireRate = 13f, range = 150f,
                    magazineSize = 35, reloadTime = 1.8f,
                    automatic = true, recoil = 0.38f,
                    zoomFov = 50f,
                    viewOffset = new Vector3(0.25f, -0.22f, 0.4f),
                    adsOffset = new Vector3(0f, -0.15f, 0.42f),
                },
                new WeaponDefinition
                {
                    displayName = "SHOTGUN",
                    model = WeaponModelType.Shotgun,
                    damage = 12f, fireRate = 1.3f, range = 40f,
                    pellets = 8, spreadDegrees = 4.5f,
                    magazineSize = 6, reloadTime = 2.4f,
                    automatic = false, recoil = 2.2f,
                    zoomFov = 52f,
                    viewOffset = new Vector3(0.26f, -0.24f, 0.45f),
                    adsOffset = new Vector3(0f, -0.17f, 0.48f),
                },
                new WeaponDefinition
                {
                    displayName = "RIFLE",
                    model = WeaponModelType.Rifle,
                    damage = 22f, fireRate = 9f, range = 250f,
                    magazineSize = 30, reloadTime = 1.7f,
                    automatic = true, recoil = 0.65f,
                    zoomFov = 45f,
                    viewOffset = new Vector3(0.26f, -0.24f, 0.42f),
                    adsOffset = new Vector3(0f, -0.17f, 0.46f),
                },
                new WeaponDefinition
                {
                    displayName = "SNIPER",
                    model = WeaponModelType.Sniper,
                    damage = 100f, fireRate = 0.9f, range = 500f, // one-shot kill
                    magazineSize = 5, reloadTime = 2.4f,
                    automatic = false, recoil = 3f,
                    zoomFov = 16f, hideWhenZoomed = true,
                    viewOffset = new Vector3(0.26f, -0.26f, 0.4f),
                },
                new WeaponDefinition
                {
                    displayName = "RPG",
                    model = WeaponModelType.Rpg,
                    damage = 120f, fireRate = 0.8f, range = 300f,
                    isProjectile = true, explosionRadius = 4.5f, projectileSpeed = 28f,
                    magazineSize = 1, reloadTime = 3f,
                    automatic = false, recoil = 2.5f,
                    zoomFov = 50f,
                    viewOffset = new Vector3(0.3f, -0.26f, 0.4f),
                    adsOffset = new Vector3(0f, -0.2f, 0.42f),
                },
                new WeaponDefinition
                {
                    displayName = "LMG", model = WeaponModelType.Lmg,
                    damage = 19f, fireRate = 10.5f, range = 230f, magazineSize = 75,
                    reloadTime = 4.2f, automatic = true, recoil = 0.82f, zoomFov = 48f,
                    viewOffset = new Vector3(0.29f, -0.27f, 0.44f), adsOffset = new Vector3(0f, -0.18f, 0.48f),
                },
                new WeaponDefinition
                {
                    displayName = "GRENADE LAUNCHER", model = WeaponModelType.GrenadeLauncher,
                    damage = 95f, fireRate = 0.75f, range = 180f, magazineSize = 1,
                    reloadTime = 2.8f, automatic = false, recoil = 2.1f, zoomFov = 50f,
                    isProjectile = true, explosionRadius = 3.8f, projectileSpeed = 20f,
                    viewOffset = new Vector3(0.28f, -0.25f, 0.42f), adsOffset = new Vector3(0f, -0.18f, 0.45f),
                },

                // ---- Pack arsenal (9+): every model of the imported weapon packs
                // as its own weapon. APPEND ONLY — indexes are global.
                new WeaponDefinition // 9
                {
                    displayName = "STORM RIFLE", model = WeaponModelType.Rifle, modelName = "Modern/Rifle_2_Used",
                    damage = 24f, fireRate = 8f, range = 260f, magazineSize = 30,
                    reloadTime = 1.9f, automatic = true, recoil = 0.72f, zoomFov = 45f,
                    viewOffset = new Vector3(0.26f, -0.24f, 0.42f),
                    adsOffset = new Vector3(0f, -0.17f, 0.46f),
                },
                new WeaponDefinition // 10 — hits harder than the rifle, kicks harder
                {
                    displayName = "AK-47", model = WeaponModelType.Rifle, modelName = "Modern/Rifle_11_Used",
                    damage = 27f, fireRate = 7.5f, range = 240f, magazineSize = 30,
                    reloadTime = 2.1f, automatic = true, recoil = 0.95f, zoomFov = 45f,
                    viewOffset = new Vector3(0.26f, -0.24f, 0.42f),
                    adsOffset = new Vector3(0f, -0.17f, 0.46f),
                },
                new WeaponDefinition // 11 — 3-shot kill, slow and heavy
                {
                    displayName = "BATTLE RIFLE", model = WeaponModelType.Rifle, modelName = "Modern/Rifle_10_Used",
                    damage = 34f, fireRate = 6f, range = 300f, magazineSize = 20,
                    reloadTime = 2.2f, automatic = true, recoil = 1.15f, zoomFov = 42f,
                    viewOffset = new Vector3(0.26f, -0.24f, 0.42f),
                    adsOffset = new Vector3(0f, -0.17f, 0.46f),
                },
                new WeaponDefinition // 12 — semi-auto, 3-shot kill
                {
                    displayName = "MARKSMAN RIFLE", model = WeaponModelType.Rifle, modelName = "Modern/Rifle_9_Used",
                    damage = 40f, fireRate = 5f, range = 350f, magazineSize = 20,
                    reloadTime = 2f, automatic = false, recoil = 1.3f, zoomFov = 32f,
                    viewOffset = new Vector3(0.26f, -0.24f, 0.42f),
                    adsOffset = new Vector3(0f, -0.17f, 0.46f),
                },
                new WeaponDefinition // 13 — VSS-style carbine, low recoil
                {
                    displayName = "WHISPER", model = WeaponModelType.Rifle, modelName = "Modern/Rifle_4_Used",
                    damage = 25f, fireRate = 8.5f, range = 220f, magazineSize = 20,
                    reloadTime = 1.9f, automatic = true, recoil = 0.55f, zoomFov = 34f,
                    viewOffset = new Vector3(0.26f, -0.24f, 0.42f),
                    adsOffset = new Vector3(0f, -0.17f, 0.46f),
                },
                new WeaponDefinition // 14 — 2 body shots, headshots still lethal
                {
                    displayName = "SEMI-AUTO SNIPER", model = WeaponModelType.Sniper, modelName = "Modern/Rifle_14_Used",
                    damage = 75f, fireRate = 2.2f, range = 450f, magazineSize = 10,
                    reloadTime = 2.6f, automatic = false, recoil = 2.2f,
                    zoomFov = 20f, hideWhenZoomed = true,
                    viewOffset = new Vector3(0.26f, -0.26f, 0.4f),
                },
                new WeaponDefinition // 15
                {
                    displayName = "MACHINE PISTOL", model = WeaponModelType.Smg, modelName = "Modern/Rifle_3_Used",
                    damage = 13f, fireRate = 15f, range = 110f, magazineSize = 32,
                    reloadTime = 1.7f, automatic = true, recoil = 0.45f, zoomFov = 52f,
                    viewOffset = new Vector3(0.25f, -0.22f, 0.4f),
                    adsOffset = new Vector3(0f, -0.15f, 0.42f),
                },
                new WeaponDefinition // 16
                {
                    displayName = "PDW", model = WeaponModelType.Smg, modelName = "Modern/Rifle_7_Used",
                    damage = 15f, fireRate = 14f, range = 160f, magazineSize = 40,
                    reloadTime = 1.8f, automatic = true, recoil = 0.35f, zoomFov = 50f,
                    viewOffset = new Vector3(0.25f, -0.22f, 0.4f),
                    adsOffset = new Vector3(0f, -0.15f, 0.42f),
                },
                new WeaponDefinition // 17 — huge mag
                {
                    displayName = "BULLPUP SMG", model = WeaponModelType.Smg, modelName = "Modern/Rifle_8_Used",
                    damage = 12f, fireRate = 15f, range = 140f, magazineSize = 50,
                    reloadTime = 2.3f, automatic = true, recoil = 0.3f, zoomFov = 50f,
                    viewOffset = new Vector3(0.25f, -0.22f, 0.4f),
                    adsOffset = new Vector3(0f, -0.15f, 0.42f),
                },
                new WeaponDefinition // 18 — full-auto sidearm
                {
                    displayName = "AUTO PISTOL", model = WeaponModelType.Pistol, modelName = "Modern/Pistol_1_Used",
                    damage = 13f, fireRate = 16f, range = 90f, magazineSize = 18,
                    reloadTime = 1.3f, automatic = true, recoil = 0.6f, zoomFov = 50f,
                    viewOffset = new Vector3(0.24f, -0.21f, 0.38f),
                    adsOffset = new Vector3(0f, -0.15f, 0.4f),
                },
                new WeaponDefinition // 19
                {
                    displayName = "TACTICAL PISTOL", model = WeaponModelType.Pistol, modelName = "Modern/Pistol_3_Used",
                    damage = 18f, fireRate = 15f, range = 130f, magazineSize = 20,
                    reloadTime = 1.2f, automatic = false, recoil = 0.9f, zoomFov = 48f,
                    viewOffset = new Vector3(0.24f, -0.21f, 0.38f),
                    adsOffset = new Vector3(0f, -0.15f, 0.4f),
                },
                new WeaponDefinition // 20 — 2-shot kill, big kick
                {
                    displayName = "HAND CANNON", model = WeaponModelType.Pistol, modelName = "Modern/Pistol_4_Used",
                    damage = 48f, fireRate = 3f, range = 150f, magazineSize = 7,
                    reloadTime = 1.6f, automatic = false, recoil = 3f, zoomFov = 46f,
                    viewOffset = new Vector3(0.24f, -0.21f, 0.38f),
                    adsOffset = new Vector3(0f, -0.15f, 0.4f),
                },
                new WeaponDefinition // 21
                {
                    displayName = "FIELD RIFLE", model = WeaponModelType.Rifle, modelName = "AR_T",
                    damage = 23f, fireRate = 8.5f, range = 260f, magazineSize = 35,
                    reloadTime = 1.9f, automatic = true, recoil = 0.7f, zoomFov = 45f,
                    viewOffset = new Vector3(0.26f, -0.24f, 0.42f),
                    adsOffset = new Vector3(0f, -0.17f, 0.46f),
                },
                new WeaponDefinition // 22 — short rifle, fast fire, shorter reach
                {
                    displayName = "COMPACT CARBINE", model = WeaponModelType.Rifle, modelName = "AR_U",
                    damage = 20f, fireRate = 10.5f, range = 200f, magazineSize = 30,
                    reloadTime = 1.6f, automatic = true, recoil = 0.6f, zoomFov = 47f,
                    viewOffset = new Vector3(0.26f, -0.24f, 0.42f),
                    adsOffset = new Vector3(0f, -0.17f, 0.46f),
                },
                new WeaponDefinition // 23
                {
                    displayName = "TACTICAL CARBINE", model = WeaponModelType.Rifle, modelName = "AR_W",
                    damage = 21f, fireRate = 9.5f, range = 230f, magazineSize = 30,
                    reloadTime = 1.5f, automatic = true, recoil = 0.55f, zoomFov = 45f,
                    viewOffset = new Vector3(0.26f, -0.24f, 0.42f),
                    adsOffset = new Vector3(0f, -0.17f, 0.46f),
                },
                new WeaponDefinition // 24
                {
                    displayName = "AK-74", model = WeaponModelType.Rifle, modelName = "AR_X",
                    damage = 23f, fireRate = 9f, range = 250f, magazineSize = 30,
                    reloadTime = 1.9f, automatic = true, recoil = 0.6f, zoomFov = 45f,
                    viewOffset = new Vector3(0.26f, -0.24f, 0.42f),
                    adsOffset = new Vector3(0f, -0.17f, 0.46f),
                },
                new WeaponDefinition // 25 — pistol-sized SMG
                {
                    displayName = "MICRO SMG", model = WeaponModelType.Smg, modelName = "Pistol_P",
                    damage = 11f, fireRate = 17f, range = 90f, magazineSize = 25,
                    reloadTime = 1.4f, automatic = true, recoil = 0.5f, zoomFov = 52f,
                    viewOffset = new Vector3(0.24f, -0.21f, 0.38f),
                    adsOffset = new Vector3(0f, -0.15f, 0.4f),
                },
                new WeaponDefinition // 26
                {
                    displayName = "TACTICAL SMG", model = WeaponModelType.Smg, modelName = "SMG_P",
                    damage = 16f, fireRate = 12f, range = 170f, magazineSize = 30,
                    reloadTime = 1.9f, automatic = true, recoil = 0.42f, zoomFov = 50f,
                    viewOffset = new Vector3(0.25f, -0.22f, 0.4f),
                    adsOffset = new Vector3(0f, -0.15f, 0.42f),
                },
                new WeaponDefinition // 27 — slower than the shotgun, deadlier up close
                {
                    displayName = "PUMP SHOTGUN", model = WeaponModelType.Shotgun, modelName = "ShotGun_P",
                    damage = 14f, fireRate = 1f, range = 35f,
                    pellets = 9, spreadDegrees = 5.5f,
                    magazineSize = 8, reloadTime = 2.8f, automatic = false, recoil = 2.6f, zoomFov = 52f,
                    viewOffset = new Vector3(0.26f, -0.24f, 0.45f),
                    adsOffset = new Vector3(0f, -0.17f, 0.48f),
                },
                new WeaponDefinition // 28 — bolt action, deeper zoom
                {
                    displayName = "RECON SNIPER", model = WeaponModelType.Sniper, modelName = "Recon_P",
                    damage = 100f, fireRate = 0.75f, range = 550f, magazineSize = 5,
                    reloadTime = 2.6f, automatic = false, recoil = 3.2f,
                    zoomFov = 14f, hideWhenZoomed = true,
                    viewOffset = new Vector3(0.26f, -0.26f, 0.4f),
                },
                new WeaponDefinition // 29 — semi-auto .50, slow reload
                {
                    displayName = "HEAVY SNIPER", model = WeaponModelType.Sniper, modelName = "Launcher_G",
                    damage = 100f, fireRate = 1.2f, range = 600f, magazineSize = 7,
                    reloadTime = 3.4f, automatic = false, recoil = 4f,
                    zoomFov = 14f, hideWhenZoomed = true,
                    viewOffset = new Vector3(0.26f, -0.26f, 0.4f),
                },
                new WeaponDefinition // 30 — 2-shot kill
                {
                    displayName = "REVOLVER", model = WeaponModelType.Pistol, modelName = "Pistol_R",
                    damage = 55f, fireRate = 2.2f, range = 140f, magazineSize = 6,
                    reloadTime = 2.2f, automatic = false, recoil = 3.2f, zoomFov = 46f,
                    viewOffset = new Vector3(0.24f, -0.21f, 0.38f),
                    adsOffset = new Vector3(0f, -0.15f, 0.4f),
                },
            };
        }
    }
}
