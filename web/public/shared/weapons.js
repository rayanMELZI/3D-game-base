// Port of Assets/Scripts/Weapons/WeaponDefinition.cs + WeaponAttachments.cs.
// Indexes are global (Gun Game ladder + network weapon index): APPEND ONLY.

export const Type = {
  Knife: 'knife', Pistol: 'pistol', Smg: 'smg', Shotgun: 'shotgun', Rifle: 'rifle',
  Sniper: 'sniper', Rpg: 'rpg', Lmg: 'lmg', GrenadeLauncher: 'gl',
};

const DEFAULTS = {
  damage: 20, fireRate: 8, range: 200, magazineSize: 30, reloadTime: 1.5, automatic: true,
  recoil: 1, shotPitch: 1, isMelee: false, pellets: 1, spreadDegrees: 0, isProjectile: false,
  explosionRadius: 0, projectileSpeed: 28, projectileGravity: 0, integralSuppressor: false,
  mobility: 1, adsSpeed: 1, zoomFov: 48, hideWhenZoomed: false,
};

const W = (name, model, stats) => Object.freeze({ ...DEFAULTS, name, model, ...stats });

export const WEAPONS = [
  W('KNIFE', Type.Knife, { isMelee: true, magazineSize: 0, damage: 100, fireRate: 1.8, range: 2.4, automatic: false, recoil: 0.2, zoomFov: 0 }),
  W('PISTOL', Type.Pistol, { damage: 20, fireRate: 15, range: 120, magazineSize: 12, reloadTime: 1.1, automatic: false, recoil: 1.1, zoomFov: 48 }),
  W('SMG', Type.Smg, { damage: 14, fireRate: 13, range: 150, magazineSize: 35, reloadTime: 1.8, recoil: 0.38, zoomFov: 50 }),
  W('SHOTGUN', Type.Shotgun, { damage: 12, fireRate: 1.3, range: 40, pellets: 8, spreadDegrees: 4.5, magazineSize: 6, reloadTime: 2.4, automatic: false, recoil: 2.2, zoomFov: 52 }),
  W('RIFLE', Type.Rifle, { damage: 22, fireRate: 9, range: 250, magazineSize: 30, reloadTime: 1.7, recoil: 0.65, zoomFov: 45 }),
  W('SNIPER', Type.Sniper, { damage: 100, fireRate: 0.9, range: 500, magazineSize: 5, reloadTime: 2.4, automatic: false, recoil: 3, zoomFov: 16, hideWhenZoomed: true }),
  W('RPG', Type.Rpg, { damage: 120, fireRate: 0.8, range: 300, isProjectile: true, explosionRadius: 4.5, projectileSpeed: 28, magazineSize: 1, reloadTime: 3, automatic: false, recoil: 2.5, zoomFov: 50 }),
  W('LMG', Type.Lmg, { shotPitch: 0.86, damage: 19, fireRate: 10.5, range: 230, magazineSize: 75, reloadTime: 4.2, recoil: 0.82, zoomFov: 48, mobility: 0.85, adsSpeed: 0.55 }),
  W('GRENADE LAUNCHER', Type.GrenadeLauncher, { damage: 110, fireRate: 0.75, range: 180, magazineSize: 1, reloadTime: 2.8, automatic: false, recoil: 2.1, zoomFov: 50, isProjectile: true, explosionRadius: 3.8, projectileSpeed: 30, projectileGravity: 9.81, mobility: 0.92, adsSpeed: 0.8 }),
  // ---- Pack arsenal (9+) ----
  W('STORM RIFLE', Type.Rifle, { shotPitch: 0.97, damage: 24, fireRate: 8, range: 260, magazineSize: 30, reloadTime: 1.9, recoil: 0.72, zoomFov: 45 }),
  W('AK-47', Type.Rifle, { shotPitch: 0.88, damage: 27, fireRate: 7.5, range: 240, magazineSize: 30, reloadTime: 2.1, recoil: 0.95, zoomFov: 45 }),
  W('BATTLE RIFLE', Type.Rifle, { shotPitch: 0.8, damage: 34, fireRate: 6, range: 300, magazineSize: 20, reloadTime: 2.2, recoil: 1.15, zoomFov: 42 }),
  W('MARKSMAN RIFLE', Type.Rifle, { shotPitch: 0.85, damage: 40, fireRate: 5, range: 350, magazineSize: 20, reloadTime: 2, automatic: false, recoil: 1.3, zoomFov: 32 }),
  W('WHISPER', Type.Rifle, { shotPitch: 1.1, integralSuppressor: true, damage: 25, fireRate: 8.5, range: 220, magazineSize: 20, reloadTime: 1.9, recoil: 0.55, zoomFov: 34 }),
  W('SEMI-AUTO SNIPER', Type.Sniper, { shotPitch: 0.9, damage: 75, fireRate: 2.2, range: 450, magazineSize: 10, reloadTime: 2.6, automatic: false, recoil: 2.2, zoomFov: 20, hideWhenZoomed: true }),
  W('MACHINE PISTOL', Type.Smg, { shotPitch: 1.25, damage: 13, fireRate: 15, range: 110, magazineSize: 32, reloadTime: 1.7, recoil: 0.45, zoomFov: 52 }),
  W('PDW', Type.Smg, { shotPitch: 1.15, damage: 15, fireRate: 14, range: 160, magazineSize: 40, reloadTime: 1.8, recoil: 0.35, zoomFov: 50 }),
  W('BULLPUP SMG', Type.Smg, { shotPitch: 1.2, damage: 12, fireRate: 15, range: 140, magazineSize: 50, reloadTime: 2.3, recoil: 0.3, zoomFov: 50 }),
  W('AUTO PISTOL', Type.Pistol, { shotPitch: 1.15, damage: 13, fireRate: 16, range: 90, magazineSize: 18, reloadTime: 1.3, recoil: 0.6, zoomFov: 50 }),
  W('TACTICAL PISTOL', Type.Pistol, { shotPitch: 1.07, damage: 18, fireRate: 15, range: 130, magazineSize: 20, reloadTime: 1.2, automatic: false, recoil: 0.9, zoomFov: 48 }),
  W('HAND CANNON', Type.Pistol, { shotPitch: 0.72, damage: 48, fireRate: 3, range: 150, magazineSize: 7, reloadTime: 1.6, automatic: false, recoil: 3, zoomFov: 46 }),
  W('FIELD RIFLE', Type.Rifle, { shotPitch: 0.93, damage: 23, fireRate: 8.5, range: 260, magazineSize: 35, reloadTime: 1.9, recoil: 0.7, zoomFov: 45 }),
  W('COMPACT CARBINE', Type.Rifle, { shotPitch: 1.08, damage: 20, fireRate: 10.5, range: 200, magazineSize: 30, reloadTime: 1.6, recoil: 0.6, zoomFov: 47 }),
  W('TACTICAL CARBINE', Type.Rifle, { shotPitch: 1.03, damage: 21, fireRate: 9.5, range: 230, magazineSize: 30, reloadTime: 1.5, recoil: 0.55, zoomFov: 45 }),
  W('AK-74', Type.Rifle, { shotPitch: 0.95, damage: 23, fireRate: 9, range: 250, magazineSize: 30, reloadTime: 1.9, recoil: 0.6, zoomFov: 45 }),
  W('MICRO SMG', Type.Smg, { shotPitch: 1.3, damage: 11, fireRate: 17, range: 90, magazineSize: 25, reloadTime: 1.4, recoil: 0.5, zoomFov: 52 }),
  W('TACTICAL SMG', Type.Smg, { shotPitch: 1.05, damage: 16, fireRate: 12, range: 170, magazineSize: 30, reloadTime: 1.9, recoil: 0.42, zoomFov: 50 }),
  W('PUMP SHOTGUN', Type.Shotgun, { shotPitch: 0.85, damage: 14, fireRate: 1, range: 35, pellets: 9, spreadDegrees: 5.5, magazineSize: 8, reloadTime: 2.8, automatic: false, recoil: 2.6, zoomFov: 52 }),
  W('RECON SNIPER', Type.Sniper, { shotPitch: 0.92, damage: 100, fireRate: 0.75, range: 550, magazineSize: 5, reloadTime: 2.6, automatic: false, recoil: 3.2, zoomFov: 14, hideWhenZoomed: true }),
  W('HEAVY SNIPER', Type.Sniper, { shotPitch: 0.68, damage: 100, fireRate: 1.2, range: 600, magazineSize: 7, reloadTime: 3.4, automatic: false, recoil: 4, zoomFov: 14, hideWhenZoomed: true }),
  W('REVOLVER', Type.Pistol, { shotPitch: 0.8, damage: 55, fireRate: 2.2, range: 140, magazineSize: 6, reloadTime: 2.2, automatic: false, recoil: 3.2, zoomFov: 46 }),
];

/** Index of the weapon players spawn with (rifle). */
export const DEFAULT_INDEX = 4;
export const KNIFE = 0;
export const SNIPER = 5;

/** Gun Game weapon order by loadout index (knife last). */
export const GUN_GAME_ORDER = [1, 2, 3, 4, 7, 5, 8, 6, 0];

// ---- Add-ons (WeaponAttachments.cs) ----
export const Att = { Optic: 0, Suppressor: 1, Foregrip: 2, ExtMag: 3 };
export const ATT_NAMES = ['OPTIC', 'SUPPRESSOR', 'FOREGRIP', 'EXT. MAG'];
export const attBit = (t) => 1 << t;
export const hasAtt = (mask, t) => (mask & attBit(t)) !== 0;

export function allowedMask(model) {
  switch (model) {
    case Type.Knife: case Type.Rpg: return 0;
    case Type.Sniper: return attBit(Att.Suppressor) | attBit(Att.Foregrip) | attBit(Att.ExtMag);
    case Type.GrenadeLauncher: return attBit(Att.Optic) | attBit(Att.Foregrip) | attBit(Att.ExtMag);
    default: return 15;
  }
}
export const sanitizeAtt = (mask, model) => mask & allowedMask(model);
export const effectiveAtt = (mask, def) => (def && def.integralSuppressor ? mask | attBit(Att.Suppressor) : mask);
export const recoilMultiplier = (mask) =>
  (hasAtt(mask, Att.Suppressor) ? 0.82 : 1) * (hasAtt(mask, Att.Foregrip) ? 0.8 : 1);
export const magazineSize = (base, mask) =>
  base > 0 && hasAtt(mask, Att.ExtMag) ? Math.ceil(base * 1.5) : base;
export const zoomFovMultiplier = (mask) => (hasAtt(mask, Att.Optic) ? 0.9 : 1);
export const suppressesRadar = (mask) => hasAtt(mask, Att.Suppressor);
export const shotVolumeMultiplier = (mask) => (hasAtt(mask, Att.Suppressor) ? 0.45 : 1);

// ---- Throwables (ThrowableController.cs) ----
export const THROWABLES = ['FRAG', 'FLASH', 'STICKY', 'KNIFE'];
