// Minimal collision world shared by the browser and the server: oriented
// boxes + vertical capsules (Unity's cylinder primitive collider), a
// CharacterController-like mover made of three spheres, raycasts, and the
// player hitboxes (head sphere = always lethal, like the head trigger).

const DEG = Math.PI / 180;

/** Rotation matrix columns (local X/Y/Z axes in world) for Euler angles in degrees, order YXZ. */
export function axesFromEuler(r) {
  const [x, y, z] = [r[0] * DEG, r[1] * DEG, r[2] * DEG];
  const a = Math.cos(x), b = Math.sin(x), c = Math.cos(y), d = Math.sin(y), e = Math.cos(z), f = Math.sin(z);
  // three.js Matrix4.makeRotationFromEuler, order 'YXZ'
  const ce = c * e, cf = c * f, de = d * e, df = d * f;
  const m11 = ce + df * b, m12 = de * b - cf, m13 = a * d;
  const m21 = a * f, m22 = a * e, m23 = -b;
  const m31 = cf * b - de, m32 = df + ce * b, m33 = a * c;
  return [[m11, m21, m31], [m12, m22, m32], [m13, m23, m33]];
}

const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

export class World {
  constructor(map) {
    this.obbs = [];
    this.caps = [];
    this.killY = map.killY ?? -20;
    if (map.ground) {
      // The ground plane becomes a thick slab so nothing tunnels through it.
      this.addBox([0, -0.5, 0], [0, 0, 0], [map.ground.sizeX, 1, map.ground.sizeZ], 'Ground');
    }
    for (const b of map.boxes) if (!b.visual) this.addBox(b.p, b.r, b.s, b.name);
    for (const c of map.cylinders) {
      if (c.visual || c.r[0] || c.r[2]) continue;
      const radius = Math.max(c.s[0], c.s[2]) / 2;
      const half = Math.max(c.s[1], radius);
      this.caps.push({ c: c.p, r: radius, a: c.p[1] - half + radius, b: c.p[1] + half - radius, bound: half + radius, name: c.name });
    }
  }

  addBox(p, r, s, name) {
    const h = [s[0] / 2, s[1] / 2, s[2] / 2];
    this.obbs.push({ c: p, h, ax: axesFromEuler(r), bound: Math.hypot(h[0], h[1], h[2]), name });
  }

  // ------------------------------------------------------------------
  // Sphere push-out
  // ------------------------------------------------------------------

  /** Pushes the sphere out of everything. Returns the deepest upward-facing normal y (ground). */
  resolveSphere(p, r, groundSnap) {
    let groundN = 0;
    for (let iter = 0; iter < 3; iter++) {
      let moved = false;
      for (const o of this.obbs) {
        const d = [p[0] - o.c[0], p[1] - o.c[1], p[2] - o.c[2]];
        if (dot(d, d) > (o.bound + r) * (o.bound + r)) continue;
        const l = [dot(d, o.ax[0]), dot(d, o.ax[1]), dot(d, o.ax[2])];
        const cl = [0, 1, 2].map((i) => Math.max(-o.h[i], Math.min(o.h[i], l[i])));
        const diff = [l[0] - cl[0], l[1] - cl[1], l[2] - cl[2]];
        const dist = Math.hypot(diff[0], diff[1], diff[2]);
        let n, depth;
        if (dist > 1e-6) {
          if (dist >= r) continue;
          n = diff.map((v) => v / dist);
          depth = r - dist;
        } else {
          // Centre inside the box: leave through the closest face.
          let best = 0, bestPen = Infinity;
          for (let i = 0; i < 3; i++) {
            const pen = o.h[i] - Math.abs(l[i]);
            if (pen < bestPen) { bestPen = pen; best = i; }
          }
          n = [0, 0, 0];
          n[best] = l[best] >= 0 ? 1 : -1;
          depth = bestPen + r;
        }
        const wn = [
          o.ax[0][0] * n[0] + o.ax[1][0] * n[1] + o.ax[2][0] * n[2],
          o.ax[0][1] * n[0] + o.ax[1][1] * n[1] + o.ax[2][1] * n[2],
          o.ax[0][2] * n[0] + o.ax[1][2] * n[1] + o.ax[2][2] * n[2],
        ];
        groundN = Math.max(groundN, this.#push(p, wn, depth, groundSnap));
        moved = true;
      }
      for (const c of this.caps) {
        const dx = p[0] - c.c[0], dz = p[2] - c.c[2];
        if (Math.abs(dx) > c.r + r || Math.abs(dz) > c.r + r) continue;
        const y = Math.max(c.a, Math.min(c.b, p[1]));
        const dy = p[1] - y;
        const dist = Math.hypot(dx, dy, dz);
        const min = c.r + r;
        if (dist >= min) continue;
        const n = dist > 1e-6 ? [dx / dist, dy / dist, dz / dist] : [1, 0, 0];
        groundN = Math.max(groundN, this.#push(p, n, min - dist, groundSnap));
        moved = true;
      }
      if (!moved) break;
    }
    return groundN;
  }

  #push(p, n, depth, groundSnap) {
    if (groundSnap && n[1] > 0.55) {
      // Walkable surface: lift straight up so ramps don't slide the player.
      p[1] += depth / n[1];
      return n[1];
    }
    p[0] += n[0] * depth; p[1] += n[1] * depth; p[2] += n[2] * depth;
    return n[1] > 0.55 ? n[1] : 0;
  }

  /**
   * Moves a standing capsule (feet position) by `delta`. Returns
   * { grounded, hitCeiling }. `pos` is modified in place.
   */
  moveCharacter(pos, delta, radius, height) {
    const len = Math.hypot(delta[0], delta[1], delta[2]);
    const steps = Math.max(1, Math.ceil(len / (radius * 0.5)));
    let grounded = false, hitCeiling = false;
    for (let s = 0; s < steps; s++) {
      pos[0] += delta[0] / steps; pos[1] += delta[1] / steps; pos[2] += delta[2] / steps;
      // Bottom sphere (ground), then mid and top (walls/ceiling).
      const bottom = [pos[0], pos[1] + radius, pos[2]];
      if (this.resolveSphere(bottom, radius, true) > 0) grounded = true;
      pos[0] = bottom[0]; pos[1] = bottom[1] - radius; pos[2] = bottom[2];
      const topY = Math.max(radius, height - radius);
      for (const off of [height / 2, topY]) {
        if (off <= radius) continue;
        const sp = [pos[0], pos[1] + off, pos[2]];
        const before = sp[1];
        this.resolveSphere(sp, radius, false);
        if (off === topY && sp[1] < before - 1e-4) hitCeiling = true;
        pos[0] = sp[0]; pos[2] = sp[2];
        pos[1] += Math.min(0, sp[1] - before); // ceilings push down, never up
      }
    }
    return { grounded, hitCeiling };
  }

  /** True when a capsule of this height fits at pos (used to stand up from crouch/prone). */
  fits(pos, radius, height) {
    const sp = [pos[0], pos[1] + height - radius, pos[2]];
    const before = sp.slice();
    this.resolveSphere(sp, radius * 0.9, false);
    return Math.hypot(sp[0] - before[0], sp[1] - before[1], sp[2] - before[2]) < 1e-3;
  }

  // ------------------------------------------------------------------
  // Raycasts
  // ------------------------------------------------------------------

  /** Nearest hit along a normalized direction: { t, point, normal, name } or null. */
  raycast(o, dir, maxDist) {
    let best = null, bestT = maxDist;
    for (const b of this.obbs) {
      const d = [b.c[0] - o[0], b.c[1] - o[1], b.c[2] - o[2]];
      // Bounding-sphere reject.
      const proj = dot(d, dir);
      if (proj < -b.bound || proj - b.bound > bestT) continue;
      const perp2 = dot(d, d) - proj * proj;
      if (perp2 > b.bound * b.bound) continue;
      let tmin = -Infinity, tmax = Infinity, axis = -1;
      let ok = true;
      for (let i = 0; i < 3; i++) {
        const e = dot(b.ax[i], d), f = dot(b.ax[i], dir);
        if (Math.abs(f) > 1e-9) {
          let t1 = (e + b.h[i]) / f, t2 = (e - b.h[i]) / f;
          if (t1 > t2) { const tmp = t1; t1 = t2; t2 = tmp; }
          if (t1 > tmin) { tmin = t1; axis = i; }
          if (t2 < tmax) tmax = t2;
          if (tmin > tmax || tmax < 0) { ok = false; break; }
        } else if (-e - b.h[i] > 0 || -e + b.h[i] < 0) { ok = false; break; }
      }
      if (!ok || tmin < 0 || tmin >= bestT) continue; // origin inside a box is ignored
      bestT = tmin;
      // Entry face normal: the hit axis, turned to face the ray.
      const n = dot(b.ax[axis], dir) > 0 ? b.ax[axis].map((v) => -v) : b.ax[axis].slice();
      best = { t: tmin, normal: n, name: b.name };
    }
    for (const c of this.caps) {
      const hit = rayVerticalCapsule(o, dir, c.c[0], c.c[2], c.a, c.b, c.r, bestT);
      if (hit) { bestT = hit.t; best = { t: hit.t, normal: hit.normal, name: c.name }; }
    }
    if (best) best.point = [o[0] + dir[0] * best.t, o[1] + dir[1] * best.t, o[2] + dir[2] * best.t];
    return best;
  }

  /** Clear line of sight between two points. */
  lineOfSight(a, b) {
    const d = [b[0] - a[0], b[1] - a[1], b[2] - a[2]];
    const len = Math.hypot(d[0], d[1], d[2]);
    if (len < 1e-4) return true;
    return !this.raycast(a, d.map((v) => v / len), len - 0.05);
  }

  /** Height of the walkable surface under (x, z), probing down from fromY. */
  groundHeight(x, z, fromY = 2.2, maxDrop = 50) {
    const hit = this.raycast([x, fromY, z], [0, -1, 0], maxDrop);
    return hit && hit.normal[1] > 0.55 ? hit.point[1] : null;
  }
}

/** Ray vs vertical capsule segment (x,z) from y=a to y=b, radius r. */
export function rayVerticalCapsule(o, dir, x, z, a, b, r, maxT) {
  let best = null;
  // Cylinder side.
  const ox = o[0] - x, oz = o[2] - z;
  const A = dir[0] * dir[0] + dir[2] * dir[2];
  if (A > 1e-9) {
    const B = 2 * (ox * dir[0] + oz * dir[2]);
    const Cc = ox * ox + oz * oz - r * r;
    const disc = B * B - 4 * A * Cc;
    if (disc >= 0) {
      const t = (-B - Math.sqrt(disc)) / (2 * A);
      const y = o[1] + dir[1] * t;
      if (t >= 0 && t < maxT && y >= a && y <= b) {
        const px = ox + dir[0] * t, pz = oz + dir[2] * t;
        best = { t, normal: [px / r, 0, pz / r] };
        maxT = t;
      }
    }
  }
  // End spheres.
  for (const cy of [a, b]) {
    const t = raySphere(o, dir, [x, cy, z], r, maxT);
    if (t !== null) {
      const p = [o[0] + dir[0] * t - x, o[1] + dir[1] * t - cy, o[2] + dir[2] * t - z];
      best = { t, normal: p.map((v) => v / r) };
      maxT = t;
    }
  }
  return best;
}

export function raySphere(o, dir, c, r, maxT) {
  const m = [o[0] - c[0], o[1] - c[1], o[2] - c[2]];
  const b = dot(m, dir);
  const cc = dot(m, m) - r * r;
  if (cc > 0 && b > 0) return null;
  const disc = b * b - cc;
  if (disc < 0) return null;
  const t = Math.max(0, -b - Math.sqrt(disc));
  return t < maxT ? t : null;
}

// ---------------------------------------------------------------------------
// Character hitboxes (HumanoidBuilder colliders: lethal head + body)
// ---------------------------------------------------------------------------

export const STAND_HEIGHT = 1.8;
export const RADIUS = 0.4;

/**
 * Ray vs a character. `c` = { p: feet, yaw, crouch (0..1), prone (0..1) }.
 * Returns { t, head } or null.
 */
export function rayCharacter(o, dir, c, maxT) {
  const h = characterHeight(c);
  let best = null;
  if (c.prone > 0.5) {
    // Lying full-length along the facing direction.
    const fx = -Math.sin(c.yaw), fz = -Math.cos(c.yaw);
    const headC = [c.p[0] + fx * 0.85, c.p[1] + 0.3, c.p[2] + fz * 0.85];
    const th = raySphere(o, dir, headC, 0.17, maxT);
    if (th !== null) { best = { t: th, head: true }; maxT = th; }
    for (let i = -2; i <= 1; i++) {
      const bc = [c.p[0] + fx * i * 0.32, c.p[1] + 0.25, c.p[2] + fz * i * 0.32];
      const tb = raySphere(o, dir, bc, 0.27, maxT);
      if (tb !== null) { best = { t: tb, head: false }; maxT = tb; }
    }
    return best;
  }
  const headY = c.p[1] + h - 0.17;
  const th = raySphere(o, dir, [c.p[0], headY, c.p[2]], 0.17, maxT);
  if (th !== null) { best = { t: th, head: true }; maxT = th; }
  const body = rayVerticalCapsule(o, dir, c.p[0], c.p[2], c.p[1] + 0.3, headY - 0.45, 0.3, maxT);
  if (body) best = { t: body.t, head: false };
  return best;
}

export function characterHeight(c) {
  const crouchH = 1.2, proneH = 0.55;
  const h = STAND_HEIGHT + (crouchH - STAND_HEIGHT) * (c.crouch || 0);
  return h + (proneH - h) * (c.prone || 0);
}
