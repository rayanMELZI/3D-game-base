using System;
using UnityEngine;

namespace FpsBase
{
    public enum WeaponModelType
    {
        Pistol,
        Rifle,
        Sniper,
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
        public int magazineSize = 30;
        public float reloadTime = 1.5f;
        public bool automatic = true;        // hold vs click per shot
        public float recoil = 1f;            // camera kick per shot (degrees)

        [Tooltip("Right-click zoom FOV. 0 = this weapon has no zoom.")]
        public float zoomFov = 0f;
        [Tooltip("Hide the viewmodel while zoomed (sniper scope).")]
        public bool hideWhenZoomed = false;

        [Tooltip("Viewmodel position relative to the camera.")]
        public Vector3 viewOffset = new Vector3(0.26f, -0.24f, 0.42f);

        public static WeaponDefinition[] CreateDefaultLoadout()
        {
            return new[]
            {
                new WeaponDefinition
                {
                    displayName = "PISTOL",
                    model = WeaponModelType.Pistol,
                    damage = 20f, fireRate = 5f, range = 120f,
                    magazineSize = 12, reloadTime = 1.1f,
                    automatic = false, recoil = 1.1f,
                    viewOffset = new Vector3(0.24f, -0.21f, 0.38f),
                },
                new WeaponDefinition
                {
                    displayName = "RIFLE",
                    model = WeaponModelType.Rifle,
                    damage = 22f, fireRate = 9f, range = 250f,
                    magazineSize = 30, reloadTime = 1.7f,
                    automatic = true, recoil = 0.65f,
                    viewOffset = new Vector3(0.26f, -0.24f, 0.42f),
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
            };
        }
    }
}
