using UnityEngine;

namespace FpsBase
{
    /// <summary>Imported weapon audio referenced by one Resources asset.</summary>
    public sealed class WeaponAudioLibrary : ScriptableObject
    {
        private const string ResourcePath = "Audio/WeaponAudioLibrary";
        private static WeaponAudioLibrary instance;

        public AudioClip[] handgunShots;
        public AudioClip[] handgunSuppressedShots;
        public AudioClip[] rifleShots;
        public AudioClip[] rifleSuppressedShots;
        public AudioClip[] shotgunShots;
        public AudioClip grenadeLauncherShot;
        public AudioClip grenadeLauncherExplosion;
        public AudioClip[] handgunReload;
        public AudioClip[] rifleReload;
        public AudioClip[] shotgunReload;
        public AudioClip[] grenadeLauncherReload;

        public static WeaponAudioLibrary Shared
        {
            get
            {
                if (instance == null)
                    instance = Resources.Load<WeaponAudioLibrary>(ResourcePath);
                return instance;
            }
        }

        public AudioClip RandomShot(WeaponModelType type, bool suppressed)
        {
            switch (type)
            {
                case WeaponModelType.Pistol:
                    return RandomFrom(suppressed ? handgunSuppressedShots : handgunShots);
                case WeaponModelType.Shotgun:
                    return RandomFrom(shotgunShots);
                case WeaponModelType.Rpg:
                case WeaponModelType.GrenadeLauncher:
                    return grenadeLauncherShot;
                case WeaponModelType.Smg:
                case WeaponModelType.Rifle:
                case WeaponModelType.Sniper:
                case WeaponModelType.Lmg:
                    return RandomFrom(suppressed ? rifleSuppressedShots : rifleShots);
                default:
                    return null;
            }
        }

        public AudioClip[] ReloadSequence(WeaponModelType type)
        {
            switch (type)
            {
                case WeaponModelType.Pistol:
                    return handgunReload;
                case WeaponModelType.Shotgun:
                    return shotgunReload;
                case WeaponModelType.Rpg:
                case WeaponModelType.GrenadeLauncher:
                    return grenadeLauncherReload;
                case WeaponModelType.Smg:
                case WeaponModelType.Rifle:
                case WeaponModelType.Sniper:
                case WeaponModelType.Lmg:
                    return rifleReload;
                default:
                    return null;
            }
        }

        private static AudioClip RandomFrom(AudioClip[] clips)
        {
            if (clips == null || clips.Length == 0)
                return null;
            return clips[Random.Range(0, clips.Length)];
        }
    }
}
