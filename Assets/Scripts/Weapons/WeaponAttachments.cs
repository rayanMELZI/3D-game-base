using UnityEngine;

namespace FpsBase
{
    /// <summary>The four add-ons a weapon can mount. The int value is a bit index.</summary>
    public enum AttachmentType
    {
        Optic = 0,       // red-dot: tighter ADS sight picture (sniper already scoped)
        Suppressor = 1,  // less recoil, quieter, and NO radar ping when you fire
        Foregrip = 2,    // less recoil (stacks with the suppressor)
        ExtendedMag = 3, // +50% magazine
    }

    /// <summary>
    /// Per-weapon add-on rules and effects. A weapon's configuration is a single
    /// int bitmask (bit N = 1 &lt;&lt; (int)AttachmentType.N). Attachments are chosen
    /// PER WEAPON globally (shared by every class) and saved in GameSettings.
    ///
    /// This is pure data/logic — the cosmetic models live in WeaponModelBuilder,
    /// the stat effects are read by WeaponController, and NetworkWeapon replicates
    /// the current weapon's mask so other players see your add-ons.
    /// </summary>
    public static class Attachments
    {
        public const int Count = 4;

        /// <summary>Menu labels, indexed by (int)AttachmentType.</summary>
        public static readonly string[] Names = { "OPTIC", "SUPPRESSOR", "FOREGRIP", "EXT. MAG" };

        public static int Bit(AttachmentType t) => 1 << (int)t;
        public static bool Has(int mask, AttachmentType t) => (mask & Bit(t)) != 0;
        public static int Toggle(int mask, AttachmentType t) => mask ^ Bit(t);

        /// <summary>
        /// Which add-ons a given weapon may mount. The knife and RPG stay bare
        /// (a suppressed rocket launcher is silly); the sniper already ships with
        /// a scope, so it can't take a red-dot optic.
        /// </summary>
        public static int AllowedMask(WeaponModelType model)
        {
            switch (model)
            {
                case WeaponModelType.Knife:
                case WeaponModelType.Rpg:
                    return 0;
                case WeaponModelType.Sniper:
                    return Bit(AttachmentType.Suppressor) | Bit(AttachmentType.Foregrip) | Bit(AttachmentType.ExtendedMag);
                default:
                    return Bit(AttachmentType.Optic) | Bit(AttachmentType.Suppressor)
                         | Bit(AttachmentType.Foregrip) | Bit(AttachmentType.ExtendedMag);
            }
        }

        public static bool IsAllowed(WeaponModelType model, AttachmentType t) => (AllowedMask(model) & Bit(t)) != 0;

        /// <summary>Drop any bits a weapon can't actually mount (defends against stale prefs).</summary>
        public static int Sanitize(int mask, WeaponModelType model) => mask & AllowedMask(model);

        // ------------------------------------------------------------------
        // Effects
        // ------------------------------------------------------------------

        /// <summary>Suppressor and foregrip each tame recoil; they stack multiplicatively.</summary>
        public static float RecoilMultiplier(int mask) =>
            (Has(mask, AttachmentType.Suppressor) ? 0.82f : 1f) *
            (Has(mask, AttachmentType.Foregrip) ? 0.8f : 1f);

        /// <summary>Extended mag = +50% capacity (never touches the ∞ melee slot).</summary>
        public static int MagazineSize(int baseSize, int mask) =>
            baseSize > 0 && Has(mask, AttachmentType.ExtendedMag) ? Mathf.CeilToInt(baseSize * 1.5f) : baseSize;

        /// <summary>Optic pulls the ADS FOV in a touch (a closer, cleaner sight picture).</summary>
        public static float ZoomFovMultiplier(int mask) => Has(mask, AttachmentType.Optic) ? 0.9f : 1f;

        /// <summary>A suppressed shot never paints you on the enemy radar.</summary>
        public static bool SuppressesRadar(int mask) => Has(mask, AttachmentType.Suppressor);

        /// <summary>Suppressed shots are noticeably quieter (relative volume).</summary>
        public static float ShotVolumeMultiplier(int mask) => Has(mask, AttachmentType.Suppressor) ? 0.45f : 1f;
    }
}
