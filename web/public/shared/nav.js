// Walkable grid baked from the collision world (stands in for the runtime
// NavMesh of BotNavigation): 1 m cells, ground height probed from above,
// cells blocked when a body would not fit, A* over 8 neighbours.

const CELL = 1;
const PROBE_Y = 2.6;

export class NavGrid {
  constructor(world, map) {
    let minX = Infinity, maxX = -Infinity, minZ = Infinity, maxZ = -Infinity;
    for (const o of world.obbs) {
      if (o.name === 'Ground') {
        minX = Math.min(minX, o.c[0] - o.h[0]); maxX = Math.max(maxX, o.c[0] + o.h[0]);
        minZ = Math.min(minZ, o.c[2] - o.h[2]); maxZ = Math.max(maxZ, o.c[2] + o.h[2]);
      }
    }
    if (minX === Infinity) {
      // No ground plane (SKYFREIGHT): span the spawn candidates generously.
      for (const list of map.spawns) for (const p of list) {
        minX = Math.min(minX, p[0] - 8); maxX = Math.max(maxX, p[0] + 8);
        minZ = Math.min(minZ, p[2] - 8); maxZ = Math.max(maxZ, p[2] + 8);
      }
    }
    this.x0 = minX; this.z0 = minZ;
    this.w = Math.max(1, Math.ceil((maxX - minX) / CELL));
    this.h = Math.max(1, Math.ceil((maxZ - minZ) / CELL));
    this.height = new Float32Array(this.w * this.h).fill(NaN);
    for (let j = 0; j < this.h; j++)
      for (let i = 0; i < this.w; i++) {
        const x = this.x0 + (i + 0.5) * CELL, z = this.z0 + (j + 0.5) * CELL;
        const y = world.groundHeight(x, z, PROBE_Y, 12);
        if (y === null || y > PROBE_Y - 1.6) continue;
        // Body clearance: a 0.32 m sphere at waist height must not touch anything.
        const p = [x, y + 0.95, z];
        world.resolveSphere(p, 0.32, false);
        if (Math.abs(p[0] - x) + Math.abs(p[2] - z) + Math.abs(p[1] - (y + 0.95)) > 0.02) continue;
        this.height[j * this.w + i] = y;
      }
  }

  cellOf(x, z) {
    const i = Math.floor((x - this.x0) / CELL), j = Math.floor((z - this.z0) / CELL);
    if (i < 0 || j < 0 || i >= this.w || j >= this.h) return -1;
    return j * this.w + i;
  }
  center(c) {
    const i = c % this.w, j = (c / this.w) | 0;
    return [this.x0 + (i + 0.5) * CELL, this.height[c], this.z0 + (j + 0.5) * CELL];
  }
  walkable(c) { return c >= 0 && !Number.isNaN(this.height[c]); }

  nearestWalkable(x, z, maxRing = 4) {
    const c = this.cellOf(x, z);
    if (this.walkable(c)) return c;
    const i0 = Math.floor((x - this.x0) / CELL), j0 = Math.floor((z - this.z0) / CELL);
    for (let r = 1; r <= maxRing; r++)
      for (let dj = -r; dj <= r; dj++)
        for (let di = -r; di <= r; di++) {
          if (Math.max(Math.abs(di), Math.abs(dj)) !== r) continue;
          const i = i0 + di, j = j0 + dj;
          if (i < 0 || j < 0 || i >= this.w || j >= this.h) continue;
          const cc = j * this.w + i;
          if (this.walkable(cc)) return cc;
        }
    return -1;
  }

  /** Path of world points from a to b (feet positions), or null. */
  findPath(a, b, maxNodes = 4000) {
    const start = this.nearestWalkable(a[0], a[2]);
    const goal = this.nearestWalkable(b[0], b[2]);
    if (start < 0 || goal < 0) return null;
    if (start === goal) return [b];
    const W = this.w;
    const gi = goal % W, gj = (goal / W) | 0;
    const hfn = (c) => {
      const dx = Math.abs((c % W) - gi), dz = Math.abs(((c / W) | 0) - gj);
      return Math.max(dx, dz) + 0.414 * Math.min(dx, dz);
    };
    const g = new Map([[start, 0]]);
    const came = new Map();
    const open = [[hfn(start), start]];
    const closed = new Set();
    let expanded = 0;
    while (open.length && expanded < maxNodes) {
      // Small binary heap would be faster; the grids are tiny (≤ 70×70).
      let bi = 0;
      for (let k = 1; k < open.length; k++) if (open[k][0] < open[bi][0]) bi = k;
      const [, cur] = open[bi];
      open[bi] = open[open.length - 1]; open.pop();
      if (closed.has(cur)) continue;
      if (cur === goal) break;
      closed.add(cur);
      expanded++;
      const ci = cur % W, cj = (cur / W) | 0;
      for (let dj = -1; dj <= 1; dj++)
        for (let di = -1; di <= 1; di++) {
          if (!di && !dj) continue;
          const ni = ci + di, nj = cj + dj;
          if (ni < 0 || nj < 0 || ni >= W || nj >= this.h) continue;
          const n = nj * W + ni;
          if (!this.walkable(n) || closed.has(n)) continue;
          if (Math.abs(this.height[n] - this.height[cur]) > 0.55) continue;
          // No corner cutting.
          if (di && dj && (!this.walkable(cj * W + ni) || !this.walkable(nj * W + ci))) continue;
          const cost = g.get(cur) + (di && dj ? 1.414 : 1);
          if (cost < (g.get(n) ?? Infinity)) {
            g.set(n, cost);
            came.set(n, cur);
            open.push([cost + hfn(n), n]);
          }
        }
    }
    if (!came.has(goal)) return null;
    const cells = [];
    for (let c = goal; c !== start; c = came.get(c)) cells.push(c);
    cells.reverse();
    const pts = cells.map((c) => this.center(c));
    pts[pts.length - 1] = b.slice();
    return pts;
  }

  randomWalkable(rand = Math.random) {
    for (let k = 0; k < 200; k++) {
      const c = (rand() * this.w * this.h) | 0;
      if (this.walkable(c)) return this.center(c);
    }
    return null;
  }
}
