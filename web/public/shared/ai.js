// Server-side AI: bots (BotAgent) and zombies (ZombieAgent).
// Yaw convention (three.js): forward = (-sin yaw, 0, -cos yaw); pitch > 0 looks up.

import { WEAPONS, Type, magazineSize } from './weapons.js';

export const BOT_NAMES = ['VIPER', 'GHOST', 'RAVEN', 'NOMAD', 'SABLE', 'FALCON', 'BISHOP', 'ECHO', 'HAWK', 'ONYX'];

const DEG = Math.PI / 180;
const SIGHT_RANGE = 90, HALF_FOV = 75;
const INITIAL_AIM_ERROR = 7, SETTLED_AIM_ERROR = 1.3, AIM_SETTLE = 1.4, TURN_SPEED = 230;
const WALK = 4.4, RUN = 6.8, GRAVITY = -20, EYE = 1.62;

const rand = (a, b) => a + Math.random() * (b - a);
const wrap = (a) => Math.atan2(Math.sin(a), Math.cos(a));
const dist = (a, b) => Math.hypot(a[0] - b[0], a[1] - b[1], a[2] - b[2]);
const flat = (a, b) => Math.hypot(a[0] - b[0], a[2] - b[2]);

export function dirFrom(yaw, pitch) {
  const c = Math.cos(pitch);
  return [-Math.sin(yaw) * c, Math.sin(pitch), -Math.cos(yaw) * c];
}

function turnToward(current, target, maxStep) {
  const d = wrap(target - current);
  return current + Math.max(-maxStep, Math.min(maxStep, d));
}

/** Follows a nav path, returns the horizontal unit direction to walk (or null). */
function steer(agent, nav, goal, time, repathEvery) {
  if (!goal) return null;
  if (!agent.path || time >= agent.repathAt || (agent.goal && flat(agent.goal, goal) > 2)) {
    agent.path = nav.findPath(agent.e.p, goal) || [goal];
    agent.goal = goal.slice();
    agent.repathAt = time + repathEvery;
  }
  while (agent.path.length && flat(agent.path[0], agent.e.p) < 0.6) agent.path.shift();
  const next = agent.path[0];
  if (!next) return null;
  const dx = next[0] - agent.e.p[0], dz = next[2] - agent.e.p[2];
  const len = Math.hypot(dx, dz);
  return len > 1e-3 ? [dx / len, dz / len] : null;
}

export class BotBrain {
  constructor(e) {
    this.e = e;
    this.vy = 0;
    this.ammo = 30;
    this.reloadUntil = 0;
    this.nextShot = 0;
    this.burstEnd = 0;
    this.burstPauseUntil = 0;
    this.target = null;
    this.lastTarget = null;
    this.lastSeen = null;
    this.lastSeenAt = -99;
    this.reactionReady = 0;
    this.trackStart = 0;
    this.nextLook = 0;
    this.aimNoise = [0, 0];
    this.nextNoise = 0;
    this.strafe = 1;
    this.nextStrafe = 0;
    this.path = null;
    this.repathAt = 0;
    this.roam = null;
    this.stuckCheck = { at: 0, p: [0, 0, 0] };
  }

  onSpawn(m) {
    this.e.w = m.botWeapon(this.e);
    this.ammo = magazineSize(WEAPONS[this.e.w].magazineSize, 0);
    this.reloadUntil = 0;
    this.target = null;
    this.path = null;
    this.roam = null;
    this.vy = 0;
  }

  refreshWeapon(m) {
    const w = m.botWeapon(this.e);
    if (w === this.e.w) return;
    this.e.w = w;
    this.ammo = WEAPONS[w].magazineSize;
    this.reloadUntil = 0;
    this.e.reloading = false;
  }

  onDamaged(m, attacker) {
    if (attacker === this.e || !attacker.alive) return;
    if (!this.target) {
      this.lastSeen = attacker.p.slice();
      this.lastSeenAt = m.time;
      // Turn toward the shooter.
      this.e.yaw = turnToward(this.e.yaw, Math.atan2(-(attacker.p[0] - this.e.p[0]), -(attacker.p[2] - this.e.p[2])), 1.2);
    }
  }

  #perceive(m) {
    const e = this.e;
    const eye = [e.p[0], e.p[1] + EYE, e.p[2]];
    let best = null, bestD = Infinity;
    for (const o of m.ents.values()) {
      if (!o.alive || o === e || m.sameTeam(o, e)) continue;
      const d = dist(o.p, e.p);
      if (d > SIGHT_RANGE || d >= bestD) continue;
      const toYaw = Math.atan2(-(o.p[0] - e.p[0]), -(o.p[2] - e.p[2]));
      const off = Math.abs(wrap(toYaw - e.yaw)) / DEG;
      if (off > HALF_FOV && d > 6) continue;
      const chest = [o.p[0], o.p[1] + (o.prone > 0.5 ? 0.3 : o.crouch > 0.5 ? 0.9 : 1.25), o.p[2]];
      if (!m.world.lineOfSight(eye, chest)) continue;
      best = o; bestD = d;
    }
    if (best) {
      if (best !== this.target) {
        const reacquired = best === this.lastTarget && m.time - this.lastSeenAt < 2;
        this.reactionReady = m.time + (reacquired ? rand(0.12, 0.25) : rand(0.3, 0.6));
        this.trackStart = m.time;
      }
      this.target = best;
      this.lastTarget = best;
      this.lastSeen = best.p.slice();
      this.lastSeenAt = m.time;
    } else {
      this.target = null;
    }
  }

  update(m, dt) {
    const e = this.e;
    const def = WEAPONS[e.w];
    if (m.time >= this.nextLook) { this.nextLook = m.time + 0.2; this.#perceive(m); }
    if (this.target && !this.target.alive) this.target = null;

    // ---- Reload ----
    if (this.reloadUntil && m.time >= this.reloadUntil) {
      this.reloadUntil = 0;
      this.ammo = def.magazineSize;
      e.reloading = false;
    }
    if (!def.isMelee && this.ammo <= 0 && !this.reloadUntil) {
      this.reloadUntil = m.time + def.reloadTime;
      e.reloading = true;
      m.emit(null, { t: 'reload', id: e.id, w: e.w });
    }

    // ---- Aim ----
    const t = this.target;
    if (t) {
      if (m.time >= this.nextNoise) {
        const settle = Math.min(1, (m.time - this.trackStart) / AIM_SETTLE);
        const err = (INITIAL_AIM_ERROR + (SETTLED_AIM_ERROR - INITIAL_AIM_ERROR) * settle) * DEG;
        this.aimNoise = [rand(-err, err), rand(-err, err) * 0.6];
        this.nextNoise = m.time + 0.25;
      }
      const aimY = t.prone > 0.5 ? 0.3 : t.crouch > 0.5 ? 0.85 : 1.3;
      const dx = t.p[0] - e.p[0], dy = t.p[1] + aimY - (e.p[1] + EYE), dz = t.p[2] - e.p[2];
      const yaw = Math.atan2(-dx, -dz) + this.aimNoise[0];
      const pitch = Math.atan2(dy, Math.hypot(dx, dz)) + this.aimNoise[1];
      const step = TURN_SPEED * DEG * dt;
      e.yaw = turnToward(e.yaw, yaw, step);
      e.pitch += Math.max(-step, Math.min(step, pitch - e.pitch));

      // ---- Fire ----
      const d = dist(t.p, e.p);
      const aligned = Math.abs(wrap(yaw - e.yaw)) < 10 * DEG;
      const inRange = def.isMelee ? d < 2.2 : d < def.range;
      if (aligned && inRange && m.time >= this.reactionReady && m.time >= this.nextShot && !this.reloadUntil && m.time >= this.burstPauseUntil) {
        if (def.automatic && !def.isMelee) {
          if (!this.burstEnd) this.burstEnd = m.time + rand(0.35, 0.9);
          else if (m.time > this.burstEnd) {
            this.burstEnd = 0;
            this.burstPauseUntil = m.time + rand(0.25, 0.5);
          }
        }
        if (m.time >= this.burstPauseUntil) this.#fire(m, def);
      }
    } else {
      e.pitch *= 0.9;
    }

    // ---- Move ----
    let goal = null, speed = RUN;
    if (t) {
      const d = flat(t.p, e.p);
      const close = def.isMelee || def.model === Type.Shotgun ? 3 : def.model === Type.Sniper ? 30 : 16;
      speed = WALK;
      if (d > close) goal = t.p;
      else {
        if (m.time >= this.nextStrafe) { this.strafe = Math.random() < 0.5 ? -1 : 1; this.nextStrafe = m.time + rand(0.8, 2.2); }
        const side = [Math.cos(e.yaw) * this.strafe, -Math.sin(e.yaw) * this.strafe];
        goal = [e.p[0] + side[0] * 2, e.p[1], e.p[2] + side[1] * 2];
        this.path = [goal];
        this.repathAt = m.time + 0.3;
      }
    } else if (this.lastSeen && m.time - this.lastSeenAt < 6) {
      goal = this.lastSeen;
    } else {
      // Hunt: head toward the nearest enemy's area, else roam.
      let near = null, nd = Infinity;
      for (const o of m.ents.values()) {
        if (!o.alive || o === e || m.sameTeam(o, e)) continue;
        const d = flat(o.p, e.p);
        if (d < nd) { nd = d; near = o; }
      }
      if (!this.roam || flat(this.roam, e.p) < 1.5) this.roam = near && Math.random() < 0.7 ? near.p.slice() : m.nav.randomWalkable();
      goal = this.roam;
    }
    const dir = steer(this, m.nav, goal, m.time, 1);
    const mob = def.mobility;
    const move = dir ? [dir[0] * speed * mob * dt, dir[1] * speed * mob * dt] : [0, 0];
    if (!t && dir) e.yaw = turnToward(e.yaw, Math.atan2(-dir[0], -dir[1]), 6 * dt);
    this.vy += GRAVITY * dt;
    const res = m.world.moveCharacter(e.p, [move[0], this.vy * dt, move[1]], 0.4, 1.8);
    if (res.grounded && this.vy < 0) this.vy = -1;
    if (res.hitCeiling && this.vy > 0) this.vy = 0;

    // Stuck: jump, then pick another route.
    if (m.time >= this.stuckCheck.at) {
      if (dir && flat(this.stuckCheck.p, e.p) < 0.4) {
        if (res.grounded) this.vy = 6.9;
        this.roam = m.nav.randomWalkable();
        this.path = null;
      }
      this.stuckCheck = { at: m.time + 1.2, p: e.p.slice() };
    }
    if (e.p[1] < m.world.killY) m.damage(e, null, 1000, false, null);
  }

  #fire(m, def) {
    const e = this.e;
    this.nextShot = m.time + 1 / def.fireRate + (def.automatic ? 0 : rand(0.08, 0.25));
    const eye = [e.p[0], e.p[1] + EYE, e.p[2]];
    const fwd = dirFrom(e.yaw, e.pitch);
    if (def.isProjectile) {
      this.ammo--;
      const o = eye.map((v, i) => v + fwd[i] * 0.8);
      m.spawnProjectile(e, e.w, o, fwd.map((v) => v * def.projectileSpeed));
      e.lastShotAt = m.time;
      m.emit(null, { t: 'shot', id: e.id, w: e.w, o, e: [], sup: false });
      return;
    }
    if (!def.isMelee) this.ammo--;
    const dirs = [];
    for (let i = 0; i < def.pellets; i++) {
      if (!def.spreadDegrees) { dirs.push(fwd); continue; }
      const s = def.spreadDegrees * DEG;
      dirs.push(dirFrom(e.yaw + rand(-s, s), e.pitch + rand(-s, s)));
    }
    if (def.isMelee) {
      // Knife: straight short reach.
      m.botFire(e, eye, [fwd]);
      return;
    }
    m.botFire(e, eye, dirs);
  }
}

export class ZombieBrain {
  constructor(z) {
    this.e = z;
    this.vy = 0;
    this.path = null;
    this.repathAt = 0;
    this.nextAttack = 0;
    this.windupEnd = 0;
    this.target = null;
    this.nextGrowl = rand(3, 9);
  }

  update(m, dt) {
    const z = this.e;
    let target = null, td = Infinity;
    for (const o of m.ents.values()) {
      if (!o.alive) continue;
      const d = dist(o.p, z.p);
      if (d < td) { td = d; target = o; }
    }
    this.target = target;

    // Swipe: wind-up, then it lands only if still in reach.
    if (this.windupEnd && m.time >= this.windupEnd) {
      this.windupEnd = 0;
      if (target && dist(target.p, z.p) < 2.2) m.damage(target, null, z.damage, false, z.p);
    }
    if (target && td < 1.5 && m.time >= this.nextAttack && !this.windupEnd) {
      this.windupEnd = m.time + 0.35;
      this.nextAttack = m.time + 1.1;
      z.attacking = m.time + 0.6;
      if (Math.random() < 0.4) m.emit(null, { t: 'growl', p: z.p });
    }
    if (m.time >= this.nextGrowl) {
      this.nextGrowl = m.time + rand(5, 12);
      m.emit(null, { t: 'growl', p: z.p });
    }

    let dir = null;
    if (target) {
      const eye = [z.p[0], z.p[1] + 1.5, z.p[2]];
      const direct = flat(target.p, z.p) < 14 && m.world.lineOfSight(eye, [target.p[0], target.p[1] + 1, target.p[2]]);
      if (direct) {
        const dx = target.p[0] - z.p[0], dz = target.p[2] - z.p[2];
        const len = Math.hypot(dx, dz) || 1;
        dir = [dx / len, dz / len];
        this.path = null;
      } else dir = steer(this, m.nav, target.p, m.time, 0.6);
    }
    let mx = 0, mz = 0;
    if (dir && td > 1.1) {
      mx = dir[0] * z.speed * dt;
      mz = dir[1] * z.speed * dt;
      z.yaw = turnToward(z.yaw, Math.atan2(-dir[0], -dir[1]), 8 * dt);
    } else if (target) {
      z.yaw = turnToward(z.yaw, Math.atan2(-(target.p[0] - z.p[0]), -(target.p[2] - z.p[2])), 8 * dt);
    }
    // Separation so the horde doesn't merge into one body.
    for (const o of m.zombies.values()) {
      if (o === z || o.hp <= 0) continue;
      const dx = z.p[0] - o.p[0], dz = z.p[2] - o.p[2];
      const d = Math.hypot(dx, dz);
      if (d > 0.01 && d < 0.8) { mx += (dx / d) * (0.8 - d) * 0.5; mz += (dz / d) * (0.8 - d) * 0.5; }
    }
    this.vy += GRAVITY * dt;
    const res = m.world.moveCharacter(z.p, [mx, this.vy * dt, mz], 0.35, 1.8);
    if (res.grounded && this.vy < 0) this.vy = -1;
    if (z.p[1] < m.world.killY) { z.hp = 0; z.deadAt = m.time; }
  }
}
