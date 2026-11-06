// Port of Core/GameSettings.cs: persisted settings (localStorage instead of
// PlayerPrefs), weapon classes, per-class add-ons, XP / levels.

import { WEAPONS, sanitizeAtt } from '../shared/weapons.js';

const KEY = 'sundown.settings.v1';
export const CLASS_COUNT = 3;
export const CLASS_NAMES = ['ASSAULT', 'CLOSE QUARTERS', 'LONG RANGE'];

const defaults = () => ({
  name: 'Player' + (100 + Math.floor(Math.random() * 900)),
  sensitivity: 2.0,
  fov: 75,
  volume: 0.8,
  quality: 'high',
  invertY: false,
  showFps: false,
  selectedClass: 0,
  classPrimary: [4, 2, 5],   // rifle, smg, sniper
  classSecondary: [1, 3, 1], // pistol, shotgun, pistol
  classAtt: [{}, {}, {}],    // [class][weapon] = mask
  lethal: 0,                 // frag
  tactical: 1,               // flash
  experience: 0,
  lastServer: '',
});

export const S = load();

function load() {
  const d = defaults();
  try {
    const saved = JSON.parse(localStorage.getItem(KEY) || '{}');
    Object.assign(d, saved);
  } catch { /* corrupted prefs: keep defaults */ }
  const clampW = (i) => Math.max(0, Math.min(WEAPONS.length - 1, i | 0));
  for (let c = 0; c < CLASS_COUNT; c++) {
    d.classPrimary[c] = clampW(d.classPrimary[c]);
    d.classSecondary[c] = clampW(d.classSecondary[c]);
    d.classAtt[c] = d.classAtt[c] || {};
  }
  d.selectedClass = Math.max(0, Math.min(CLASS_COUNT - 1, d.selectedClass | 0));
  return d;
}

export function save() {
  localStorage.setItem(KEY, JSON.stringify(S));
}

export function getAtt(cls, weapon) {
  return sanitizeAtt(S.classAtt[cls]?.[weapon] | 0, WEAPONS[weapon].model);
}
export function setAtt(cls, weapon, mask) {
  S.classAtt[cls][weapon] = sanitizeAtt(mask, WEAPONS[weapon].model);
  save();
}

// ---- Levels: level n at 100 × (n-1)² XP ----
export const levelFor = (xp) => Math.floor(Math.sqrt(Math.max(0, xp) / 100)) + 1;
export const xpForLevel = (n) => 100 * (n - 1) * (n - 1);
export function awardXp(amount) {
  const before = levelFor(S.experience);
  S.experience += Math.max(0, amount | 0);
  save();
  return levelFor(S.experience) > before;
}
