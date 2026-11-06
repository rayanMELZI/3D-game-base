// Port of Weapons/WeaponController.cs + ThrowableController.cs (client side):
// fire / reload (shell by shell on shotguns) / ADS / switch, recoil, the
// first-person viewmodel, and throwables. Hits are found by the shooter and
// reported to the match (the shooter's client hits the proxy).

import * as THREE from 'three';
import {
  WEAPONS, Type, KNIFE, SNIPER, recoilMultiplier, magazineSize, zoomFovMultiplier, effectiveAtt,
  shotVolumeMultiplier, Att, hasAtt,
} from '../shared/weapons.js';
import { rayCharacter } from '../shared/physics.js';
import { makeWeapon } from './models.js';
import { Input, PAD } from './input.js';
import { clips, play2D } from './audio.js';
import { S, getAtt } from './settings.js';

const DEG = Math.PI / 180;
const THROW_KEYS = ['KeyG', 'KeyF', 'KeyV', 'KeyX'];
const THROW_COOLDOWN = [3.5, 6, 5, 1.2];
// Unity viewOffset/adsOffset are (x right, y up, z forward): three's z is negated.
const V = (x, y, z) => new THREE.Vector3(x, y, -z);
const VIEW = {
  [Type.Knife]: [V(0.28, -0.24, 0.45), V(0.28, -0.24, 0.45)],
  [Type.Pistol]: [V(0.24, -0.21, 0.38), V(0, -0.15, 0.4)],
  [Type.Smg]: [V(0.25, -0.22, 0.4), V(0, -0.15, 0.42)],
  [Type.Shotgun]: [V(0.26, -0.24, 0.45), V(0, -0.17, 0.48)],
  [Type.Rifle]: [V(0.26, -0.24, 0.42), V(0, -0.17, 0.46)],
  [Type.Sniper]: [V(0.26, -0.26, 0.4), V(0, -0.2, 0.4)],
  [Type.Rpg]: [V(0.3, -0.26, 0.4), V(0, -0.2, 0.42)],
  [Type.Lmg]: [V(0.29, -0.27, 0.44), V(0, -0.18, 0.48)],
  [Type.GrenadeLauncher]: [V(0.28, -0.25, 0.42), V(0, -0.18, 0.45)],
};

export class WeaponController {
  constructor(camera, effects) {
    this.camera = camera;
    this.effects = effects;
    this.holder = new THREE.Group();
    camera.add(this.holder);
    this.index = 4;
    this.slots = [];
    this.ammo = new Map();
    this.att = 0;
    this.nextFire = 0;
    this.reloadEnd = 0;
    this.shellReload = null;
    this.ads = 0;
    this.kick = 0;
    this.switchT = 1;
    this.bob = 0;
    this.time = 0;
    this.model = null;
    this.throwReady = [0, 0, 0, 0];
    this.triggerHeld = false;
    this.meleeSwing = 0;
    this.lastShotLocal = -9;
  }

  get def() { return WEAPONS[this.index]; }
  get reloading() { return this.reloadEnd > 0 || !!this.shellReload; }
  get aiming() { return this.ads > 0.5; }
  get scoped() { return this.def.hideWhenZoomed && this.ads > 0.92; }

  /** Sets the switchable weapons for this life (class, full arsenal, Gun Game, sniper only). */
  configure({ classLoadout, sniperOnly, forced }) {
    const c = S.selectedClass;
    if (forced !== null && forced !== undefined) this.slots = [forced];
    else if (sniperOnly) this.slots = [SNIPER, KNIFE];
    else if (classLoadout) this.slots = [S.classPrimary[c], S.classSecondary[c], KNIFE];
    else this.slots = WEAPONS.map((_, i) => i);
    this.ammo.clear();
    for (const w of this.slots) this.ammo.set(w, magazineSize(WEAPONS[w].magazineSize, effectiveAtt(getAtt(c, w), WEAPONS[w])));
    const first = forced ?? (sniperOnly ? SNIPER : classLoadout ? this.slots[0] : S.classPrimary[c]);
    this.equip(first, true);
  }

  equip(index, instant = false) {
    if (!this.slots.includes(index)) return;
    if (index === this.index && this.model && !instant) return;
    this.index = index;
    this.att = effectiveAtt(getAtt(S.selectedClass, index), WEAPONS[index]);
    if (!this.ammo.has(index)) this.ammo.set(index, magazineSize(WEAPONS[index].magazineSize, this.att));
    this.reloadEnd = 0;
    this.shellReload = null;
    this.switchT = instant ? 1 : 0;
    this.nextFire = Math.max(this.nextFire, this.time + (instant ? 0 : 0.35));
    if (this.model) this.holder.remove(this.model.group);
    this.model = makeWeapon(this.def, this.att);
    this.model.group.scale.setScalar(0.6); // the guns are life-size: shrink in first person
    this.model.group.traverse((o) => { o.castShadow = false; o.renderOrder = 10; if (o.material) { o.material = o.material.clone(); o.material.depthTest = true; } });
    this.holder.add(this.model.group);
  }

  /** Forced weapon change (Gun Game promotion). */
  force(index) {
    this.slots = [index];
    this.ammo.set(index, magazineSize(WEAPONS[index].magazineSize, 0));
    this.equip(index, true);
  }

  get mag() { return this.ammo.get(this.index) ?? 0; }
  get magMax() { return magazineSize(this.def.magazineSize, this.att); }

  /** ctx: { world, eye, player, targets, zombies, send, canFire } → returns recoil etc. */
  update(dt, ctx) {
    this.time += dt;
    const def = this.def;
    const t = this.time;

    // ---- Switching ----
    if (ctx.canFire) {
      if (this.slots.length <= 3) {
        for (let k = 0; k < this.slots.length; k++) if (Input.hit('Digit' + (k + 1))) this.equip(this.slots[k]);
      } else {
        for (let k = 0; k < 9; k++) if (Input.hit('Digit' + (k + 1)) && this.slots[k] !== undefined) this.equip(this.slots[k]);
      }
      const cycle = Input.wheel + (Input.padHit(PAD.Y) || Input.padHit(PAD.Right) ? 1 : 0) - (Input.padHit(PAD.Left) ? 1 : 0);
      if (cycle && this.slots.length > 1) {
        const k = this.slots.indexOf(this.index);
        this.equip(this.slots[(k + Math.sign(cycle) + this.slots.length) % this.slots.length]);
      }
      if (Input.hit('KeyQ') && this.prevIndex !== undefined) this.equip(this.prevIndex);
    }
    if (this.lastEquipped !== this.index) { this.prevIndex = this.lastEquipped; this.lastEquipped = this.index; }
    this.switchT = Math.min(1, this.switchT + dt / 0.35);

    // ---- Reload ----
    if (this.reloadEnd && t >= this.reloadEnd) {
      this.reloadEnd = 0;
      this.ammo.set(this.index, this.magMax);
      ctx.send({ t: 'reload', on: false });
    }
    if (this.shellReload) {
      if (t >= this.shellReload.next) {
        if (this.mag < this.magMax) {
          this.ammo.set(this.index, this.mag + 1);
          play2D(clips.shell(), 0.6);
          this.shellReload.next = t + this.shellReload.per;
        }
        if (this.mag >= this.magMax) { this.shellReload = null; play2D(clips.pump(), 0.6); ctx.send({ t: 'reload', on: false }); }
      }
    }
    const wantReload = Input.hit('KeyR') || Input.padHit(PAD.X);
    if (ctx.canFire && !def.isMelee && !this.reloading && this.mag < this.magMax && (wantReload || (this.mag === 0 && t >= this.nextFire))) this.startReload(ctx);

    // ---- ADS ----
    const aimHeld = ctx.canFire && def.zoomFov > 0 && (Input.mouse[2] || Input.padAxis(6) > 0.4 || Input.padDown(PAD.LT)) && !ctx.player.sprinting;
    const adsRate = 7 * def.adsSpeed;
    this.ads = Math.max(0, Math.min(1, this.ads + (aimHeld ? 1 : -1) * adsRate * dt));

    // ---- Fire ----
    const fireHeld = ctx.canFire && (Input.mouse[0] || Input.padDown(PAD.RT));
    const firePressed = ctx.canFire && (Input.mousePressed[0] || Input.padHit(PAD.RT));
    const knifeQuick = ctx.canFire && (Input.padHit(PAD.RS) || Input.hit('KeyB'));
    if (knifeQuick && this.index !== KNIFE && this.slots.includes(KNIFE)) this.equip(KNIFE);
    const wants = def.automatic ? fireHeld : firePressed;
    if (wants && t >= this.nextFire && this.switchT >= 1) {
      if (this.shellReload && this.mag > 0) { this.shellReload = null; ctx.send({ t: 'reload', on: false }); } // interrupt
      if (!this.reloading) {
        if (def.isMelee) this.#melee(ctx);
        else if (this.mag > 0) this.#fire(ctx);
        else play2D(clips.click(), 0.5);
      }
    }

    // ---- Throwables ----
    if (ctx.canFire) {
      for (let type = 0; type < 4; type++) if (Input.hit(THROW_KEYS[type])) this.#throw(type, ctx);
      if (Input.padHit(PAD.RB)) this.#throw(S.lethal, ctx);
      if (Input.padHit(PAD.LB)) this.#throw(S.tactical, ctx);
    }

    this.#viewmodel(dt, ctx);
  }

  startReload(ctx) {
    const def = this.def;
    if (def.model === Type.Shotgun) {
      // Shell by shell, interruptible by firing.
      const per = def.reloadTime / def.magazineSize;
      this.shellReload = { per, next: this.time + per * 0.8 };
    } else {
      this.reloadEnd = this.time + def.reloadTime;
      play2D(clips.reload(), 0.7);
      setTimeout(() => play2D(clips.reload(), 0.6), def.reloadTime * 700);
    }
    ctx.send({ t: 'reload', on: true });
  }

  #muzzleWorld() {
    const p = new THREE.Vector3();
    this.model.muzzle.getWorldPosition(p);
    return [p.x, p.y, p.z];
  }

  #aimDir(spreadDeg) {
    const dir = new THREE.Vector3();
    this.camera.getWorldDirection(dir);
    if (spreadDeg > 0) {
      const s = spreadDeg * DEG;
      const up = new THREE.Vector3(0, 1, 0).applyQuaternion(this.camera.getWorldQuaternion(new THREE.Quaternion()));
      const right = new THREE.Vector3().crossVectors(dir, up).normalize();
      const a = Math.random() * Math.PI * 2, r = Math.sqrt(Math.random()) * Math.tan(s);
      dir.addScaledVector(right, Math.cos(a) * r).addScaledVector(up, Math.sin(a) * r).normalize();
    }
    return [dir.x, dir.y, dir.z];
  }

  /** Nearest hit along a ray: world, other combatants, zombies. */
  #trace(ctx, o, d, range) {
    const wh = ctx.world.raycast(o, d, range);
    let best = { t: wh ? wh.t : range, world: wh };
    for (const target of ctx.targets) {
      const h = rayCharacter(o, d, target.char, best.t);
      if (h) best = { t: h.t, target, head: h.head };
    }
    for (const z of ctx.zombies) {
      const h = rayCharacter(o, d, z.char, best.t);
      if (h) best = { t: h.t, zombie: z, head: h.head };
    }
    best.point = [o[0] + d[0] * best.t, o[1] + d[1] * best.t, o[2] + d[2] * best.t];
    return best;
  }

  #fire(ctx) {
    const def = this.def;
    this.nextFire = this.time + 1 / def.fireRate;
    this.ammo.set(this.index, this.mag - 1);
    this.lastShotLocal = this.time;
    const eye = ctx.eye;
    const muzzle = this.#muzzleWorld();
    const suppressed = hasAtt(this.att, Att.Suppressor);
    const vol = shotVolumeMultiplier(this.att);
    play2D(clips.shot(def.model), 0.75 * vol, def.shotPitch);
    if (!suppressed) this.effects.muzzleFlash(muzzle, null, def.model === Type.Pistol);
    this.#recoil(def);

    if (def.isProjectile) {
      const d = this.#aimDir(0);
      const o = [eye[0] + d[0] * 0.8, eye[1] + d[1] * 0.8 - 0.05, eye[2] + d[2] * 0.8];
      ctx.send({ t: 'proj', o, v: d.map((v) => v * def.projectileSpeed) });
      return;
    }
    const hits = new Map();
    const ends = [];
    // Hip fire is a touch less precise than aiming (except shotgun pellets which use their cone).
    const hipSpread = def.pellets > 1 ? 0 : (1 - this.ads) * (ctx.player.speed > 1 ? 1.4 : 0.6) * (def.model === Type.Sniper ? 3 : 1);
    for (let k = 0; k < def.pellets; k++) {
      const d = this.#aimDir(def.spreadDegrees * (this.aiming ? 0.8 : 1) + hipSpread);
      const h = this.#trace(ctx, eye, d, def.range);
      ends.push(h.point.map((v) => +v.toFixed(2)));
      if (k < 3 || Math.random() < 0.4) this.effects.tracer(muzzle, h.point);
      if (h.target || h.zombie) {
        this.effects.impact(h.point, d.map((v) => -v), true);
        const key = h.target ? 'p' + h.target.id : 'z' + h.zombie.id;
        const prev = hits.get(key) || { id: h.target?.id, z: h.zombie?.id, n: 0, head: false };
        prev.n++; prev.head = prev.head || h.head;
        hits.set(key, prev);
      } else if (h.world) {
        this.effects.impact(h.point, h.world.normal, false, ctx.surfaceColor?.(h.world) );
      }
    }
    const noScope = def.model === Type.Sniper && this.ads < 0.5;
    ctx.send({ t: 'shot', o: muzzle.map((v) => +v.toFixed(2)), e: ends, hits: [...hits.values()], sup: suppressed, noScope });
  }

  #melee(ctx) {
    const def = this.def;
    this.nextFire = this.time + 1 / def.fireRate;
    this.meleeSwing = 1;
    play2D(clips.shot(Type.Knife), 0.8);
    const d = this.#aimDir(0);
    const h = this.#trace(ctx, ctx.eye, d, def.range);
    const hits = [];
    if (h.target) hits.push({ id: h.target.id, n: 1, head: false });
    if (h.zombie) hits.push({ z: h.zombie.id, n: 1, head: false });
    if (hits.length) this.effects.impact(h.point, d.map((v) => -v), true);
    ctx.send({ t: 'shot', o: ctx.eye, e: [], hits, sup: true });
  }

  #recoil(def) {
    const k = def.recoil * recoilMultiplier(this.att) * (this.aiming ? 0.7 : 1);
    this.recoilKick = (this.recoilKick || 0) + k * DEG;
    this.recoilSide = (Math.random() - 0.5) * k * 0.35 * DEG;
    this.kick = Math.min(1, this.kick + 0.35 + def.recoil * 0.1);
  }

  /** Consumes the camera recoil to apply this frame (pitch up, slight yaw). */
  takeRecoil(dt) {
    const out = [0, 0];
    if (this.recoilKick) {
      const step = Math.min(this.recoilKick, this.recoilKick * 25 * dt + 0.0005);
      out[0] = step;
      this.recoilKick -= step;
      if (this.recoilKick < 1e-5) this.recoilKick = 0;
    }
    if (this.recoilSide) { out[1] = this.recoilSide; this.recoilSide = 0; }
    return out;
  }

  #throw(type, ctx) {
    if (this.time < this.throwReady[type]) return;
    this.throwReady[type] = this.time + THROW_COOLDOWN[type];
    const d = this.#aimDir(0);
    const o = [ctx.eye[0] + d[0] * 0.7, ctx.eye[1] + d[1] * 0.7, ctx.eye[2] + d[2] * 0.7];
    const speed = type === 3 ? 24 : 13;
    ctx.send({ t: 'throw', type, o, v: [d[0] * speed, d[1] * speed + 2.5, d[2] * speed] });
    play2D(clips.shot(Type.Knife), 0.5, 0.7);
  }

  throwCooldown(type) { return Math.max(0, this.throwReady[type] - this.time); }

  #viewmodel(dt, ctx) {
    const def = this.def;
    const [hip, adsPos] = VIEW[def.model];
    // ADS: the sight line (model sightY, scaled) sits exactly on the camera axis.
    const pos = hip.clone().lerp(new THREE.Vector3(0, -this.model.sightY * this.model.group.scale.y, adsPos.z), this.ads);
    // Bob and sway.
    const speed = ctx.player.speed;
    this.bob += dt * speed * (ctx.player.sprinting ? 1.6 : 1.8);
    const bobAmt = Math.min(1, speed / 5) * (1 - this.ads * 0.85) * 0.012;
    pos.x += Math.cos(this.bob) * bobAmt;
    pos.y += Math.abs(Math.sin(this.bob)) * bobAmt;
    // Kick back.
    this.kick = Math.max(0, this.kick - dt * 9);
    pos.z += this.kick * 0.05;
    // Switch / reload dip.
    const dip = (1 - easeOut(this.switchT)) * 0.35 + (this.reloading ? 0.12 : 0);
    pos.y -= dip;
    const g = this.model.group;
    g.position.copy(pos);
    g.rotation.set(this.kick * 0.12 + (this.reloading ? -0.5 : 0) - dip * 1.2, 0, ctx.player.sprinting ? 0.5 : 0);
    if (ctx.player.sprinting) g.rotation.y = 0.5, g.position.x -= 0.05;
    if (this.meleeSwing > 0) {
      this.meleeSwing = Math.max(0, this.meleeSwing - dt * 5);
      const s = Math.sin(this.meleeSwing * Math.PI);
      g.position.z -= s * 0.25; g.position.x -= s * 0.12; g.rotation.y = s * 0.6;
    }
    g.visible = !this.scoped && ctx.alive;
  }

  zoomFov(baseFov) {
    const def = this.def;
    if (!def.zoomFov) return baseFov;
    const z = def.zoomFov * zoomFovMultiplier(this.att);
    return baseFov + (z - baseFov) * this.ads;
  }
}

const easeOut = (t) => 1 - (1 - t) * (1 - t);
