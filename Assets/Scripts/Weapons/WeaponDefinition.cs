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
            };
        }
    }
}
