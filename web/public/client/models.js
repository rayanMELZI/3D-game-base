// Procedural models: blocky humanoid (HumanoidBuilder look, team-coloured)
// and weapons per type (WeaponModelBuilder's procedural fallback), with the
// four add-ons. Weapons point down -Z, origin at the grip.

import * as THREE from 'three';
import { Type, Att, hasAtt } from '../shared/weapons.js';

const cache = new Map();
function std(hex, opts = {}) {
  const key = hex + JSON.stringify(opts);
  if (!cache.has(key)) cache.set(key, new THREE.MeshStandardMaterial({ color: new THREE.Color(hex), roughness: 0.6, metalness: 0.2, ...opts }));
  return cache.get(key);
}
const box = new THREE.BoxGeometry(1, 1, 1);
const cyl = new THREE.CylinderGeometry(0.5, 0.5, 1, 14);

function part(parent, geo, m, pos, scale, rot) {
  const mesh = new THREE.Mesh(geo, m);
  mesh.position.set(...pos);
  mesh.scale.set(...scale);
  if (rot) mesh.rotation.set(...rot);
  mesh.castShadow = true;
  parent.add(mesh);
  return mesh;
}

// ---------------------------------------------------------------------------
// Humanoid
// ---------------------------------------------------------------------------

/** kind: 'player' | 'zombie'. color: THREE.Color of the team. */
export function makeHumanoid(color, kind = 'player', skin = 0) {
  const g = new THREE.Group();
  const body = new THREE.Group(); // posed for crouch / prone
  g.add(body);
  const zombie = kind === 'zombie';
  const skinTones = ['#c8a07e', '#8d5e3c', '#e0b896', '#6b4a32'];
  const skinM = zombie ? std(['#6f7f5e', '#7c8466', '#5f6e57', '#88866a'][skin % 4], { roughness: 0.9 }) : std(skinTones[skin % 4], { roughness: 0.8 });
  const shirt = zombie ? std(['#4a4136', '#3d4a3f', '#55473a', '#3a3a44'][skin % 4], { roughness: 0.95 }) : new THREE.MeshStandardMaterial({ color, roughness: 0.7 });
  const pants = std(zombie ? '#2e2a26' : '#2b2f36', { roughness: 0.85 });
  const boots = std('#1b1b1b', { roughness: 0.7 });
  const vest = std('#3b3f35', { roughness: 0.8 });

  const hips = new THREE.Group(); hips.position.set(0, 0.9, 0); body.add(hips);
  const torso = new THREE.Group(); torso.position.set(0, 0.9, 0); body.add(torso);
  part(torso, box, shirt, [0, 0.27, 0], [0.5, 0.55, 0.28]);
  if (!zombie) part(torso, box, vest, [0, 0.3, 0], [0.52, 0.36, 0.31]);
  const head = new THREE.Group(); head.position.set(0, 0.6, 0); torso.add(head);
  part(head, box, skinM, [0, 0.14, 0], [0.26, 0.28, 0.26]);
  if (zombie) {
    const eye = std('#ff2a10', { emissive: new THREE.Color('#ff2a10'), emissiveIntensity: 3 });
    part(head, box, eye, [-0.06, 0.17, -0.131], [0.05, 0.03, 0.01]);
    part(head, box, eye, [0.06, 0.17, -0.131], [0.05, 0.03, 0.01]);
  } else {
    part(head, box, new THREE.MeshStandardMaterial({ color, roughness: 0.6 }), [0, 0.29, 0.01], [0.28, 0.07, 0.28]); // cap in team colour
    part(head, box, std('#111'), [0, 0.17, -0.131], [0.18, 0.04, 0.01]);
  }
  const limb = (parent, x, y, len, w, m, footM) => {
    const pivot = new THREE.Group(); pivot.position.set(x, y, 0); parent.add(pivot);
    part(pivot, box, m, [0, -len / 2, 0], [w, len, w]);
    if (footM) part(pivot, box, footM, [0, -len + 0.04, -0.05], [w + 0.02, 0.09, w + 0.12]);
    return pivot;
  };
  const legL = limb(hips, -0.13, 0, 0.88, 0.18, pants, boots);
  const legR = limb(hips, 0.13, 0, 0.88, 0.18, pants, boots);
  const armL = limb(torso, -0.33, 0.52, 0.62, 0.14, shirt);
  const armR = limb(torso, 0.33, 0.52, 0.62, 0.14, shirt);
  part(armL, box, skinM, [0, -0.64, 0], [0.12, 0.1, 0.12]);
  part(armR, box, skinM, [0, -0.64, 0], [0.12, 0.1, 0.12]);
  const gunMount = new THREE.Group();
  gunMount.position.set(0.18, 1.3, -0.35);
  body.add(gunMount);

  let phase = 0;
  const glow = new THREE.MeshStandardMaterial({ color: '#ffffff', transparent: true, opacity: 0.25, emissive: new THREE.Color('#9fd8ff'), emissiveIntensity: 1.5 });
  const shield = new THREE.Mesh(new THREE.SphereGeometry(1, 16, 10), glow);
  shield.scale.set(0.55, 1.0, 0.55); shield.position.y = 0.95; shield.visible = false;
  g.add(shield);

  return {
    group: g, body, head, torso, gunMount, armL, armR, legL, legR, shirt,
    setTeamColor(c) { if (!zombie) shirt.color.copy(c); },
    /** s: { speed (m/s), pitch, crouch, prone, slide, protected, attack (0..1), dt } */
    pose(s) {
      phase += s.dt * Math.min(s.speed, 8) * 1.6;
      const swing = Math.min(1, s.speed / 4) * 0.7 * Math.sin(phase);
      legL.rotation.x = swing; legR.rotation.x = -swing;
      shield.visible = !!s.protected;
      if (zombie) {
        const reach = -1.35 - (s.attack || 0) * 0.5;
        armL.rotation.x = reach + Math.sin(phase * 0.5) * 0.1;
        armR.rotation.x = reach - Math.sin(phase * 0.5) * 0.1;
        torso.rotation.x = -0.15;
      } else {
        // Both hands on the gun, following the aim pitch.
        const p = s.pitch || 0;
        armR.rotation.set(-Math.PI / 2 - p * 0.9 + 0.25, 0, 0.1);
        armL.rotation.set(-Math.PI / 2 - p * 0.9 + 0.1, 0, -0.55);
        head.rotation.x = p * 0.6;
        gunMount.rotation.x = p;
        gunMount.position.set(0.17, 1.33 + p * 0.25, -0.38);
      }
      // Crouch: squash; prone: lying full-length (PlayerMovement.ApplyCrouchVisual).
      const crouch = s.crouch || 0, prone = s.prone || 0;
      body.scale.y = 1 + (0.68 - 1) * crouch * (1 - prone);
      body.rotation.x = -prone * (80 * Math.PI / 180);
      body.position.y = prone * 0.28;
      body.position.z = prone * 0.6;
      if (s.slide) { body.rotation.x = 0.35; body.scale.y = 0.75; }
    },
  };
}

// ---------------------------------------------------------------------------
// Weapons
// ---------------------------------------------------------------------------

const gunMetal = () => std('#2a2c30', { metalness: 0.7, roughness: 0.35 });
const polymer = () => std('#1c1d20', { metalness: 0.1, roughness: 0.6 });
const wood = () => std('#6b4426', { metalness: 0, roughness: 0.7 });
const tan = () => std('#7d6a4a', { roughness: 0.7 });
const olive = () => std('#4c5638', { roughness: 0.65 });

export const SKIN_COLORS = [null, '#c8a04a', '#3d6fb8', '#a83232', '#3f8f4e', '#e8e8ea', '#151515', '#8d5fd0'];

/** Returns { group, muzzle: Object3D, sightY, magazine } */
export function makeWeapon(def, mask = 0, skin = 0) {
  const g = new THREE.Group();
  const muzzle = new THREE.Object3D();
  const metal = gunMetal(), poly = polymer();
  const body = skin && SKIN_COLORS[skin] ? std(SKIN_COLORS[skin], { metalness: 0.5, roughness: 0.4 }) : metal;
  let magazine = null;
  let barrelEnd = -0.5, barrelY = 0.05, railY = 0.1, railZ = -0.1, gripZ = -0.3, hasRail = true;
  const P = (geo, m, pos, scale, rot) => part(g, geo, m, pos, scale, rot);

  switch (def.model) {
    case Type.Knife:
      P(box, poly, [0, 0, 0.02], [0.03, 0.035, 0.12]);
      P(box, std('#c9ccd1', { metalness: 0.9, roughness: 0.2 }), [0, 0.005, -0.12], [0.008, 0.03, 0.2]);
      barrelEnd = -0.22; hasRail = false;
      break;
    case Type.Pistol:
      P(box, body, [0, 0.055, -0.07], [0.035, 0.035, 0.2]);
      P(box, poly, [0, -0.03, 0.0], [0.03, 0.11, 0.045], [0.2, 0, 0]);
      P(box, poly, [0, 0.02, -0.06], [0.03, 0.03, 0.12]);
      magazine = P(box, metal, [0, -0.09, 0.012], [0.025, 0.03, 0.035], [0.2, 0, 0]);
      barrelEnd = -0.18; barrelY = 0.055; railY = 0.075; railZ = -0.07; gripZ = -0.12;
      break;
    case Type.Smg:
      P(box, body, [0, 0.05, -0.12], [0.05, 0.08, 0.32]);
      P(cyl, metal, [0, 0.06, -0.33], [0.02, 0.12, 0.02], [Math.PI / 2, 0, 0]);
      P(box, poly, [0, -0.04, 0.0], [0.035, 0.11, 0.045], [0.25, 0, 0]);
      P(box, poly, [0, 0.04, 0.12], [0.03, 0.05, 0.14]);
      magazine = P(box, metal, [0, -0.07, -0.12], [0.03, 0.16, 0.04]);
      barrelEnd = -0.4; barrelY = 0.06; railY = 0.095; railZ = -0.12; gripZ = -0.24;
      break;
    case Type.Shotgun:
      P(box, body, [0, 0.05, -0.05], [0.055, 0.08, 0.3]);
      P(cyl, metal, [0, 0.07, -0.42], [0.032, 0.5, 0.032], [Math.PI / 2, 0, 0]);
      P(cyl, metal, [0, 0.035, -0.38], [0.03, 0.4, 0.03], [Math.PI / 2, 0, 0]);
      P(box, wood(), [0, 0.03, -0.35], [0.06, 0.05, 0.16]); // pump
      P(box, wood(), [0, 0.01, 0.2], [0.05, 0.09, 0.28], [-0.12, 0, 0]);
      P(box, poly, [0, -0.04, 0.03], [0.035, 0.1, 0.04], [0.3, 0, 0]);
      barrelEnd = -0.67; barrelY = 0.07; railY = 0.095; railZ = -0.05; gripZ = -0.35;
      break;
    case Type.Sniper:
      P(box, body, [0, 0.05, -0.05], [0.05, 0.07, 0.36]);
      P(cyl, metal, [0, 0.06, -0.55], [0.024, 0.65, 0.024], [Math.PI / 2, 0, 0]);
      P(box, olive(), [0, 0.02, 0.24], [0.05, 0.11, 0.34], [-0.08, 0, 0]);
      P(box, poly, [0, -0.04, 0.04], [0.035, 0.1, 0.04], [0.3, 0, 0]);
      // Built-in scope.
      P(cyl, poly, [0, 0.135, -0.05], [0.045, 0.32, 0.045], [Math.PI / 2, 0, 0]);
      P(cyl, std('#5aa0ff', { emissive: new THREE.Color('#1a3355'), metalness: 0.9, roughness: 0.1 }), [0, 0.135, 0.111], [0.04, 0.005, 0.04], [Math.PI / 2, 0, 0]);
      magazine = P(box, metal, [0, -0.03, -0.08], [0.035, 0.08, 0.07]);
      barrelEnd = -0.88; barrelY = 0.06; hasRail = false; gripZ = -0.3;
      break;
    case Type.Rpg:
      P(cyl, olive(), [0, 0.06, -0.15], [0.085, 1.0, 0.085], [Math.PI / 2, 0, 0]);
      P(cyl, std('#556b2f'), [0, 0.06, -0.72], [0.07, 0.16, 0.07], [Math.PI / 2, 0, 0]);
      P(box, poly, [0, -0.04, -0.05], [0.035, 0.1, 0.04], [0.3, 0, 0]);
      P(box, poly, [0, -0.04, -0.3], [0.035, 0.1, 0.04], [0.2, 0, 0]);
      P(box, metal, [0.06, 0.12, -0.25], [0.02, 0.06, 0.08]);
      barrelEnd = -0.8; barrelY = 0.06; hasRail = false;
      break;
    case Type.Lmg:
      P(box, body, [0, 0.05, -0.12], [0.07, 0.1, 0.42]);
      P(cyl, metal, [0, 0.06, -0.55], [0.03, 0.5, 0.03], [Math.PI / 2, 0, 0]);
      P(box, poly, [0, 0.03, 0.25], [0.05, 0.1, 0.28]);
      P(box, poly, [0, -0.05, 0.02], [0.035, 0.11, 0.045], [0.25, 0, 0]);
      magazine = P(box, olive(), [0.05, -0.04, -0.12], [0.08, 0.12, 0.12]);
      P(box, metal, [0, -0.02, -0.65], [0.01, 0.12, 0.01], [0.4, 0, 0]);
      barrelEnd = -0.8; barrelY = 0.06; railY = 0.11; railZ = -0.08; gripZ = -0.38;
      break;
    case Type.GrenadeLauncher:
      P(box, body, [0, 0.05, -0.05], [0.06, 0.08, 0.26]);
      P(cyl, metal, [0, 0.07, -0.34], [0.07, 0.38, 0.07], [Math.PI / 2, 0, 0]);
      P(cyl, tan(), [0, 0.03, -0.08], [0.15, 0.12, 0.15], [Math.PI / 2, 0, 0]); // drum
      P(box, poly, [0, 0.03, 0.18], [0.04, 0.08, 0.22]);
      P(box, poly, [0, -0.05, 0.05], [0.035, 0.1, 0.04], [0.3, 0, 0]);
      barrelEnd = -0.53; barrelY = 0.07; railY = 0.12; railZ = -0.05; gripZ = -0.3;
      break;
    default: // rifle
      P(box, body, [0, 0.05, -0.1], [0.05, 0.08, 0.4]);
      P(box, poly, [0, 0.055, -0.38], [0.055, 0.06, 0.2]);
      P(cyl, metal, [0, 0.06, -0.55], [0.022, 0.24, 0.022], [Math.PI / 2, 0, 0]);
      P(box, poly, [0, 0.03, 0.22], [0.045, 0.09, 0.26], [-0.05, 0, 0]);
      P(box, poly, [0, -0.04, 0.02], [0.035, 0.11, 0.045], [0.25, 0, 0]);
      magazine = P(box, metal, [0, -0.08, -0.14], [0.035, 0.17, 0.06], [-0.2, 0, 0]);
      P(box, metal, [0, 0.115, 0.04], [0.02, 0.03, 0.02]); // rear iron sight
      P(box, metal, [0, 0.115, -0.44], [0.01, 0.04, 0.01]); // front iron sight
      barrelEnd = -0.68; barrelY = 0.06; railY = 0.1; railZ = -0.08; gripZ = -0.38;
  }

  let sightY = railY + 0.03;
  const integral = def.integralSuppressor;
  if (hasAtt(mask, Att.Optic) && hasRail) {
    P(box, poly, [0, railY + 0.03, railZ], [0.035, 0.05, 0.09]);
    P(box, std('#ff2020', { emissive: new THREE.Color('#ff2020'), emissiveIntensity: 4 }), [0, railY + 0.045, railZ - 0.046], [0.006, 0.006, 0.002]);
    sightY = railY + 0.045;
  }
  if ((hasAtt(mask, Att.Suppressor) || integral) && def.model !== Type.Knife && def.model !== Type.Rpg) {
    P(cyl, poly, [0, barrelY, barrelEnd - 0.09], [0.04, 0.18, 0.04], [Math.PI / 2, 0, 0]);
    barrelEnd -= 0.18;
  }
  if (hasAtt(mask, Att.Foregrip) && def.model !== Type.Pistol && def.model !== Type.Knife) {
    P(box, poly, [0, -0.03, gripZ], [0.03, 0.09, 0.035]);
  }
  if (hasAtt(mask, Att.ExtMag) && magazine) {
    magazine.scale.y *= 1.5;
    magazine.position.y -= magazine.scale.y * 0.17;
  }
  muzzle.position.set(0, barrelY, barrelEnd);
  g.add(muzzle);
  return { group: g, muzzle, sightY, magazine };
}

export function makeProjectileMesh(kind) {
  if (kind === 'rocket') {
    const g = new THREE.Group();
    part(g, cyl, olive(), [0, 0, 0], [0.08, 0.5, 0.08], [Math.PI / 2, 0, 0]);
    part(g, new THREE.ConeGeometry(0.05, 0.15, 10), std('#555'), [0, 0, -0.32], [1, 1, 1], [-Math.PI / 2, 0, 0]);
    const flame = part(g, new THREE.SphereGeometry(0.09, 8, 6), std('#ffaa33', { emissive: new THREE.Color('#ff7a1a'), emissiveIntensity: 4 }), [0, 0, 0.3], [1, 1, 1.6]);
    flame.castShadow = false;
    return g;
  }
  const colors = { grenade: '#3d4a2a', t0: '#334d33', t1: '#d9d980', t2: '#4d3326', t3: '#cccfd9' };
  if (kind === 't3') {
    const g = new THREE.Group();
    part(g, box, std(colors.t3, { metalness: 0.8, roughness: 0.3 }), [0, 0, 0], [0.05, 0.05, 0.3]);
    return g;
  }
  const m = new THREE.Mesh(new THREE.SphereGeometry(kind === 'grenade' ? 0.07 : 0.09, 10, 8), std(colors[kind] || '#444', { metalness: 0.4, roughness: 0.5 }));
  m.castShadow = true;
  return m;
}
