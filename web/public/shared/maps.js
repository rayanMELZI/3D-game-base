// Port of Assets/Scripts/Core/EnvironmentBuilder.cs + MapCatalog.cs.
// Maps are pure data (shared by the browser and the Node server): boxes,
// cylinders, spheres, lights and spawn candidates. Every builder below is
// written in UNITY coordinates (left-handed, +Z forward) exactly like the C#
// source; `toThree` converts on the way out: z is negated and the Euler
// angles (Unity order Z→X→Y = three 'YXZ') become (-x, -y, z).

const C = (r, g, b) => [r, g, b];

export const TEAM_PALETTE = [
  C(0.25, 0.5, 1), C(1, 0.55, 0.15), C(0.35, 0.85, 0.4), C(0.7, 0.4, 1),
  C(0.3, 0.9, 0.9), C(1, 0.45, 0.7), C(0.95, 0.9, 0.3), C(0.9, 0.3, 0.25),
];
export const teamColor = (team) => TEAM_PALETTE[Math.abs(team | 0) % TEAM_PALETTE.length];

function mat(color, metallic = 0, smoothness = 0.5, emission = 0, extra) {
  return { color, metallic, smoothness, emission, ...extra };
}

class MapBuilder {
  constructor(name) {
    this.map = {
      name, boxes: [], cylinders: [], spheres: [], lights: [], ground: null,
      sky: 'sunset', fog: { color: C(0.72, 0.63, 0.6), density: 0.0045 },
      killY: -20, spawns: [[], []], survivors: null, zombieSpawns: null,
    };
  }
  box(name, pos, euler, scale, material, visual = false) {
    const b = { name, p: pos, r: euler, s: scale, m: material, visual };
    this.map.boxes.push(b);
    return b;
  }
  cyl(name, pos, scale, material, visual = false, euler = [0, 0, 0]) {
    // Unity cylinder primitive: 1 wide, 2 tall at scale 1 (capsule collider).
    const c = { name, p: pos, s: scale, r: euler, m: material, visual };
    this.map.cylinders.push(c);
    return c;
  }
  sphere(name, pos, scale, material) {
    this.map.spheres.push({ name, p: pos, s: scale, m: material });
  }
  light(pos, color, intensity, range) {
    this.map.lights.push({ p: pos, color, intensity, range });
  }
  ground(sizeX, sizeZ, a, b) {
    this.map.ground = { sizeX, sizeZ, a, b };
  }
  perimeterWalls(halfX, halfZ, height, m) {
    this.box('Wall', [0, height / 2, halfZ], [0, 0, 0], [halfX * 2, height, 0.5], m);
    this.box('Wall', [0, height / 2, -halfZ], [0, 0, 0], [halfX * 2, height, 0.5], m);
    this.box('Wall', [halfX, height / 2, 0], [0, 0, 0], [0.5, height, halfZ * 2], m);
    this.box('Wall', [-halfX, height / 2, 0], [0, 0, 0], [0.5, height, halfZ * 2], m);
  }
  wallSegment(x1, z1, x2, z2, height, m) {
    const cx = (x1 + x2) / 2, cz = (z1 + z2) / 2;
    const length = Math.hypot(x2 - x1, z2 - z1);
    const alongX = Math.abs(x2 - x1) > Math.abs(z2 - z1);
    const scale = alongX ? [length, height, 0.4] : [0.4, height, length];
    this.box('MazeWall', [cx, height / 2, cz], [0, 0, 0], scale, m);
  }
  spawnStrips(z, width) {
    this.box('SpawnZone0', [0, 0.03, -z], [0, 0, 0], [width, 0.06, 1.2], mat(TEAM_PALETTE[0], 0, 0.5, 1.4), true);
    this.box('SpawnZone1', [0, 0.03, z], [0, 0, 0], [width, 0.06, 1.2], mat(TEAM_PALETTE[1], 0, 0.5, 1.4), true);
  }
}

// ---------------------------------------------------------------------------
// Spawn candidates
// ---------------------------------------------------------------------------

function builtinSpawns(backrooms, side) {
  const s = side === 0 ? -1 : 1;
  if (backrooms)
    return [[-14, 0.3, 17.5 * s], [-7, 0.3, 17.5 * s], [7, 0.3, 17.5 * s], [14, 0.3, 17.5 * s], [-11, 0.3, 13 * s], [11, 0.3, 13 * s]];
  return [[0, 0.3, 24 * s], [-16, 0.3, 24 * s], [16, 0.3, 24 * s], [-8, 0.3, 20 * s], [8, 0.3, 20 * s]];
}

// ---------------------------------------------------------------------------
// Map 0: ARENA
// ---------------------------------------------------------------------------

function buildArena(size = 60) {
  const b = new MapBuilder('ARENA');
  const half = size / 2;
  b.ground(size, size, C(0.46, 0.43, 0.41), C(0.4, 0.38, 0.37));
  const wallMat = mat(C(0.5, 0.52, 0.55), 0.1, 0.35);
  const trimMat = mat(C(0.2, 0.8, 0.9), 0, 0.5, 2.4);
  const wallH = 4;
  for (let i = 0; i < 4; i++) {
    const alongX = i < 2;
    const sign = i % 2 === 0 ? 1 : -1;
    const pos = alongX ? [0, wallH / 2, half * sign] : [half * sign, wallH / 2, 0];
    const scale = alongX ? [size, wallH, 1] : [1, wallH, size];
    b.box('Wall', pos, [0, 0, 0], scale, wallMat);
    const trimScale = alongX ? [size, 0.12, 0.2] : [0.2, 0.12, size];
    b.box('WallTrim', [pos[0], pos[1] + wallH / 2 + 0.06, pos[2]], [0, 0, 0], trimScale, trimMat);
  }
  const platMat = mat(C(0.44, 0.46, 0.5), 0.15, 0.4);
  const pillarMat = mat(C(0.38, 0.4, 0.44), 0.2, 0.5);
  b.box('CenterPlatform', [0, 0.5, 0], [0, 0, 0], [12, 1, 12], platMat);
  b.box('RampSouth', [0, 0.45, -9], [-8, 0, 0], [5, 0.3, 7], platMat);
  b.box('RampNorth', [0, 0.45, 9], [8, 0, 0], [5, 0.3, 7], platMat);
  for (const sx of [-1, 1]) for (const sz of [-1, 1]) b.cyl('Pillar', [5 * sx, 2, 5 * sz], [1, 2, 1], pillarMat);

  const coverMat = mat(C(0.55, 0.5, 0.42), 0.05, 0.3);
  const crateMat = mat(C(0.55, 0.4, 0.25), 0.05, 0.3);
  for (const sz of [-1, 1]) {
    b.box('Cover', [-10, 0.8, 10 * sz], [0, 0, 0], [5, 1.6, 0.5], coverMat);
    b.box('Cover', [10, 0.8, 10 * sz], [0, 0, 0], [5, 1.6, 0.5], coverMat);
    b.box('Cover', [0, 0.8, 17 * sz], [0, 0, 0], [6, 1.6, 0.5], coverMat);
    b.box('CoverSide', [-half + 6, 0.8, 6 * sz], [0, 0, 0], [0.5, 1.6, 5], coverMat);
    b.box('CoverSide', [half - 6, 0.8, 6 * sz], [0, 0, 0], [0.5, 1.6, 5], coverMat);
    for (const sx of [-1, 1]) {
      const cx = (half - 8) * sx, cz = (half - 8) * sz;
      b.box('Crate', [cx, 0.7, cz], [0, 15, 0], [1.4, 1.4, 1.4], crateMat);
      b.box('Crate', [cx + 1.6 * sx, 0.5, cz + 0.4 * sz], [0, -10, 0], [1, 1, 1], crateMat);
      b.box('Crate', [cx, 1.9, cz], [0, 40, 0], [1, 1, 1], crateMat);
    }
  }
  b.spawnStrips(half - 3, 14);
  b.map.spawns = [builtinSpawns(false, 0), builtinSpawns(false, 1)];
  return b.map;
}

// ---------------------------------------------------------------------------
// BACKROOMS
// ---------------------------------------------------------------------------

function buildBackrooms() {
  const b = new MapBuilder('BACKROOMS');
  const half = 20, ceilingH = 3;
  b.ground(half * 2, half * 2, C(0.56, 0.5, 0.3), C(0.5, 0.45, 0.27));
  const wallMat = mat(C(0.78, 0.71, 0.42), 0, 0.25);
  const ceilMat = mat(C(0.82, 0.8, 0.72), 0, 0.2);
  const lightMat = mat(C(1, 0.97, 0.85), 0, 0.5, 2.6);
  b.perimeterWalls(half, half, ceilingH, wallMat);
  b.box('Ceiling', [0, ceilingH + 0.15, 0], [0, 0, 0], [half * 2, 0.3, half * 2], ceilMat);
  for (let x = -13; x <= 13; x += 13)
    for (let z = -13; z <= 13; z += 13) {
      b.box('LightPanel', [x, ceilingH - 0.02, z], [0, 0, 0], [2.4, 0.08, 1.2], lightMat, true);
      b.light([x, ceilingH - 0.5, z], C(1, 0.95, 0.8), 1.4, 12);
    }
  const wall = (x1, z1, x2, z2) => b.wallSegment(x1, z1, x2, z2, ceilingH, wallMat);
  wall(-16, -8, -3, -8); wall(3, -8, 16, -8);
  wall(-16, 8, -3, 8); wall(3, 8, 16, 8);
  wall(-8, -16, -8, -3); wall(-8, 3, -8, 16);
  wall(8, -16, 8, -3); wall(8, 3, 8, 16);
  wall(-20, 0, -14, 0); wall(14, 0, 20, 0);
  wall(0, -20, 0, -14); wall(0, 14, 0, 20);
  wall(-3, -3, 3, -3); wall(3, 3, -3, 3);
  const pillarMat = mat(C(0.7, 0.64, 0.38), 0, 0.3);
  for (const x of [-8, 8]) for (const z of [-8, 8]) b.box('Pillar', [x, ceilingH / 2, z], [0, 0, 0], [0.8, ceilingH, 0.8], pillarMat);
  b.spawnStrips(half - 2, 8);
  b.map.sky = 'indoor';
  b.map.fog = { color: C(0.55, 0.5, 0.32), density: 0.02 };
  b.map.spawns = [builtinSpawns(true, 0), builtinSpawns(true, 1)];
  return b.map;
}

// ---------------------------------------------------------------------------
// DUSTBOWL WEST / ABYSS (theme arenas)
// ---------------------------------------------------------------------------

function buildThemeArena(name, theme, baseColor, accentColor, dark) {
  const b = new MapBuilder(name);
  b.ground(58, 58, baseColor.map((v) => v * 0.75), baseColor);
  const wall = mat(baseColor, 0.05, 0.42);
  const accent = mat(accentColor, 0.15, 0.55, dark ? 2.2 : 0.25);
  b.perimeterWalls(29, 29, 4, wall);
  b.spawnStrips(24, 18);
  for (let side = -1; side <= 1; side += 2) {
    b.box(theme + 'Base', [-12, 1.5, side * 17], [0, 0, 0], [10, 3, 7], wall);
    b.box(theme + 'Base', [12, 1.5, side * 17], [0, 0, 0], [10, 3, 7], wall);
    b.box('Accent', [0, 1, side * 11], [0, side * 18, 0], [8, 2, 1], accent);
  }
  b.box(theme === 'Western' ? 'Saloon' : 'ResearchDome', [0, 2, 0], [0, 0, 0], [12, 4, 9], wall);
  for (let i = -2; i <= 2; i++) b.box('Cover', [i * 8, 0.85, (i % 2) * 8], [0, i * 13, 0], [3, 1.7, 2], accent);
  if (theme === 'Underwater') {
    b.map.sky = 'underwater';
    b.map.fog = { color: C(0.02, 0.2, 0.25), density: 0.018 };
  }
  b.map.spawns = [builtinSpawns(false, 0), builtinSpawns(false, 1)];
  return b.map;
}

// ---------------------------------------------------------------------------
// SKYFREIGHT — inside a giant cargo plane in flight
// ---------------------------------------------------------------------------

function buildSkyfreight() {
  const b = new MapBuilder('SKYFREIGHT');
  const halfW = 6.4, rearZ = -30, frontZ = 22, holdLen = frontZ - rearZ, holdMid = (frontZ + rearZ) / 2;
  const deckY = 4.2, ceilY = 8.6;
  const hull = mat(C(0.42, 0.45, 0.46), 0.55, 0.4);
  const hullDark = mat(C(0.24, 0.26, 0.27), 0.6, 0.45);
  const deck = mat(C(0.3, 0.31, 0.3), 0.5, 0.35);
  const rail = mat(C(0.55, 0.5, 0.2), 0.4, 0.4);
  const crate = mat(C(0.5, 0.37, 0.22), 0.05, 0.3);
  const crateDark = mat(C(0.4, 0.29, 0.17), 0.05, 0.3);
  const container = mat(C(0.28, 0.36, 0.26), 0.35, 0.35);
  const containerTan = mat(C(0.55, 0.48, 0.34), 0.3, 0.35);
  const exterior = mat(C(0.62, 0.65, 0.67), 0.5, 0.5);
  const skyGlass = mat(C(0.62, 0.78, 0.95), 0, 0.9, 1.3);
  const lamp = mat(C(1, 0.93, 0.8), 0, 0.6, 2.4);
  const jumpLight = mat(C(1, 0.15, 0.1), 0, 0.6, 2.6);
  const hazardYellow = mat(C(0.95, 0.75, 0.1), 0, 0.4);
  const hazardBlack = mat(C(0.08, 0.08, 0.08), 0, 0.4);
  const floorMat = mat(C(0.34, 0.35, 0.35), 0.45, 0.35, 0, { tiles: [C(0.34, 0.35, 0.35), C(0.3, 0.31, 0.31)], tileScale: [3, 13] });

  b.box('CargoDeck', [0, -0.15, holdMid], [0, 0, 0], [halfW * 2, 0.3, holdLen], floorMat);
  for (const s of [-1, 1]) {
    b.box('FuselageWall', [halfW * s, 2.8, holdMid], [0, 0, 0], [0.4, 5.6, holdLen], hull);
    b.box('FuselageUpper', [5.3 * s, 7.1, holdMid], [0, 0, 36.25 * s], [0.4, 3.9, holdLen], hull);
  }
  b.box('FuselageCeiling', [0, ceilY + 0.15, holdMid], [0, 0, 0], [8.8, 0.3, holdLen], hull);
  for (let z = rearZ + 2; z < frontZ; z += 4) {
    for (const s of [-1, 1]) b.box('Rib', [6.1 * s, 2.8, z], [0, 0, 0], [0.25, 5.6, 0.3], hullDark, true);
    b.box('RibCeiling', [0, ceilY - 0.05, z], [0, 0, 0], [8.4, 0.15, 0.3], hullDark, true);
  }
  for (let z = rearZ + 4; z < frontZ - 1; z += 4)
    for (const s of [-1, 1]) {
      b.box('Porthole', [6.17 * s, 2, z], [0, 0, 0], [0.1, 0.55, 0.55], skyGlass, true);
      b.box('Porthole', [6.17 * s, 5.05, z], [0, 0, 0], [0.1, 0.5, 0.5], skyGlass, true);
    }
  b.box('Bulkhead', [0, deckY / 2, frontZ + 0.2], [0, 0, 0], [halfW * 2, deckY, 0.4], hull);
  for (const s of [-1, 1]) b.box('Bulkhead', [3.7 * s, 6.4, frontZ + 0.2], [0, 0, 0], [5.4, 4.4, 0.4], hull);
  b.box('BulkheadDoorTop', [0, 7.55, frontZ + 0.2], [0, 0, 0], [2, 2.1, 0.4], hull);

  b.box('UpperDeck', [0, deckY - 0.15, 13], [0, 0, 0], [halfW * 2, 0.3, 18], deck);
  for (const s of [-1, 1]) {
    b.box('Catwalk', [5.6 * s, deckY - 0.15, -7], [0, 0, 0], [1.6, 0.3, 22], deck);
    b.box('CatwalkRail', [4.85 * s, deckY + 0.5, -7], [0, 0, 0], [0.1, 1, 22], rail);
    b.box('CatwalkRail', [5.6 * s, deckY + 0.5, -18], [0, 0, 0], [1.6, 1, 0.1], rail);
    b.box('Stairs', [2.6 * s, 2, 0], [-27.7, 0, 0], [1.8, 0.25, 9.1], deck);
    b.box('DeckRail', [4.15 * s, deckY + 0.5, 4.1], [0, 0, 0], [1.3, 1, 0.1], rail);
    b.box('Bench', [5.9 * s, deckY + 0.25, 13], [0, 0, 0], [0.6, 0.5, 14], hullDark);
  }
  b.box('DeckRail', [0, deckY + 0.5, 4.1], [0, 0, 0], [3.4, 1, 0.1], rail);

  b.box('CockpitFloor', [0, deckY - 0.15, 25.5], [0, 0, 0], [7.4, 0.3, 7], deck);
  b.box('NoseBody', [0, 1.95, 25.5], [0, 0, 0], [7.4, 3.9, 7], exterior);
  for (const s of [-1, 1]) {
    b.box('CockpitWall', [3.7 * s, 6.1, 25.5], [0, 0, 0], [0.3, 3.8, 7], hull);
    b.box('CockpitWindow', [3.56 * s, 6.4, 26.8], [0, 0, 0], [0.05, 0.9, 2.4], skyGlass, true);
    b.box('PilotSeat', [1.4 * s, deckY + 0.4, 26], [0, 0, 0], [0.9, 0.8, 0.9], hullDark);
    b.box('PilotSeatBack', [1.4 * s, deckY + 1.1, 25.5], [-8, 0, 0], [0.9, 1.4, 0.2], hullDark);
  }
  b.box('CockpitRoof', [0, 8.15, 25.5], [0, 0, 0], [7.7, 0.3, 7], hull);
  b.box('Dashboard', [0, 5, 29.1], [0, 0, 0], [7.4, 1.6, 0.3], hull);
  b.box('Windshield', [0, 6.9, 28.75], [-20, 0, 0], [7.4, 2.4, 0.2], skyGlass);
  b.box('WindshieldFrame', [0, 6.9, 28.62], [-20, 0, 0], [0.15, 2.4, 0.1], hullDark, true);
  b.box('Console', [0, deckY + 0.6, 28.1], [0, 0, 0], [6, 1.2, 1.2], hullDark);
  b.box('Instruments', [0, deckY + 1.22, 28], [0, 0, 0], [5.4, 0.05, 0.8], mat(C(0.3, 1, 0.55), 0, 0.6, 1.8), true);
  b.box('Throttles', [0, deckY + 0.45, 27.1], [0, 0, 0], [0.45, 0.9, 0.6], hullDark);
  b.box('NoseCone', [0, 3.1, 30.4], [0, 0, 0], [6.4, 5, 2.4], exterior, true);

  b.box('Ramp', [0, -1.315, rearZ - 4.35], [-15, 0, 0], [11.6, 0.3, 9], deck);
  for (let i = 0; i < 12; i++)
    b.box('HazardStripe', [-5.5 + i, 0.01, rearZ + 0.3], [0, 0, 0], [1, 0.02, 0.5], i % 2 === 0 ? hazardYellow : hazardBlack, true);
  for (const s of [-1, 1]) {
    b.box('RampArm', [5.9 * s, 0.6, rearZ - 1.6], [-50, 0, 0], [0.18, 0.18, 4.6], hullDark, true);
    b.box('JumpLight', [5.95 * s, 6.4, rearZ + 0.25], [0, 0, 0], [0.3, 0.3, 0.15], jumpLight, true);
  }
  b.box('CargoDoor', [0, 8.3, rearZ - 2], [12, 0, 0], [8.4, 0.25, 4], hull, true);
  b.box('TailCone', [0, 9.7, rearZ - 7], [-8, 0, 0], [9, 1.6, 14], exterior, true);
  b.box('TailFin', [0, 14, rearZ - 10.5], [-20, 0, 0], [0.5, 8, 6], exterior, true);
  b.box('Stabilizer', [0, 17.6, rearZ - 12.5], [0, 0, 0], [22, 0.35, 4.5], exterior, true);
  for (const s of [-1, 1]) {
    b.box('Wing', [21.4 * s, 7.2, 4], [0, 8 * s, -3 * s], [32.2, 0.6, 8], exterior, true);
    for (const x of [13, 24]) b.cyl('Engine', [x * s, 5.9, 6.5 - (x - 13) * 0.12], [1.9, 2.4, 1.9], hullDark, true, [90, 0, 0]);
  }

  const crateAt = (x, y, z, size, yaw, m) => b.box('Crate', [x, y + size / 2, z], [0, yaw, 0], [size, size, size], m);
  crateAt(-4.6, 0, -22, 1.2, 6, crate);
  crateAt(4.6, 0, -24, 1.2, -4, crate);
  crateAt(4.6, 1.2, -24, 1, 20, crateDark);
  b.box('Pallet', [0, 0.45, -27.6], [0, 0, 0], [2.2, 0.9, 1.2], crateDark);
  b.box('Container', [-2.4, 1.3, -13], [0, 0, 0], [2.6, 2.6, 6], container);
  b.box('Container', [2.4, 1.3, -9], [0, 0, 0], [2.6, 2.6, 5.5], containerTan);
  crateAt(4.9, 0, -14, 1.1, 12, crate);
  crateAt(-4.9, 0, -6, 1.1, -8, crate);
  crateAt(-4.6, 0, -4.6, 1, 18, crateDark);
  crateAt(0, 0, -1, 1.2, 0, crate);
  b.box('JeepBody', [-4.6, 0.85, 0], [0, 0, 0], [2, 1.1, 3.8], container);
  b.box('JeepCab', [-4.6, 1.75, -0.3], [0, 0, 0], [1.8, 0.7, 1.6], hullDark);
  for (const wx of [-5.65, -3.55]) for (const wz of [-1.3, 1.3]) b.cyl('JeepWheel', [wx, 0.4, wz], [0.8, 0.15, 0.8], hazardBlack, true, [0, 0, 90]);
  crateAt(4.6, 0, 0.6, 1.2, 3, crate);
  crateAt(4.6, 0, -0.8, 1.2, -5, crateDark);
  crateAt(4.6, 1.2, 0, 1, 25, crate);
  crateAt(-3.5, 0, 7, 1.2, 10, crate);
  crateAt(3.2, 0, 9, 1.2, -6, crateDark);
  crateAt(3.6, 0, 10.2, 1, 15, crate);
  b.box('Container', [-2, 0.9, 12.5], [0, 0, 0], [2.4, 1.8, 3], containerTan);
  crateAt(2.5, 0, 14, 1.2, 0, crate);
  crateAt(4.6, 0, 18, 1.2, 8, crateDark);
  crateAt(-3, deckY, 11, 1.2, 5, crate);
  crateAt(3, deckY, 16, 1, -10, crateDark);
  crateAt(2.4, deckY, 16.8, 1, 12, crate);
  crateAt(0, deckY, 8, 1.2, 0, crateDark);

  const warm = C(1, 0.92, 0.78);
  for (const z of [-26, -17, -8, 1]) b.light([0, 7.6, z], warm, 1.3, 12);
  for (const z of [9, 17]) { b.light([0, 3.4, z], warm, 1.1, 9); b.light([0, 7.6, z], warm, 1.1, 10); }
  b.light([0, 7.2, 25.5], C(0.7, 0.9, 1), 0.9, 7);
  b.light([0, 6.5, rearZ + 1], C(1, 0.25, 0.2), 1.2, 8);
  b.box('LightStrip', [0, ceilY - 0.02, holdMid], [0, 0, 0], [0.35, 0.06, holdLen - 2], lamp, true);
  b.box('LightStrip', [0, deckY - 0.33, 13], [0, 0, 0], [0.35, 0.06, 17], lamp, true);

  b.map.sky = 'flight';
  b.map.fog = { color: C(0.78, 0.84, 0.94), density: 0.004 };
  b.map.flight = true;
  b.map.killY = -12;
  b.map.spawns = [
    [[-3.5, 0.3, -25.5], [3, 0.3, -25.5], [0, 0.3, -23], [-4.5, 0.3, -19], [4.3, 0.3, -18.5], [1.8, 0.3, -20.5]],
    [[-3, 0.3, 19], [2.5, 0.3, 19.5], [0, 0.3, 16.5], [-3.5, 4.5, 18], [3.5, 4.5, 13], [0, 4.5, 20.5]],
  ];
  return b.map;
}

// ---------------------------------------------------------------------------
// NIGHTFALL — walled village graveyard at night (zombie survival)
// ---------------------------------------------------------------------------

function buildNightfall() {
  const b = new MapBuilder('NIGHTFALL');
  const half = 32;
  b.ground(half * 2, half * 2, C(0.13, 0.14, 0.12), C(0.11, 0.12, 0.1));
  const stone = mat(C(0.3, 0.3, 0.33), 0.05, 0.3);
  const wood = mat(C(0.24, 0.17, 0.11), 0, 0.25);
  const rust = mat(C(0.32, 0.16, 0.1), 0.4, 0.3);
  const metal = mat(C(0.18, 0.19, 0.2), 0.7, 0.5);
  const lampGlow = mat(C(1, 0.82, 0.5), 0, 0.6, 3);
  const fireGlow = mat(C(1, 0.45, 0.1), 0, 0.6, 3.2);
  const candleGlow = mat(C(0.9, 0.08, 0.05), 0, 0.6, 2.6);

  b.perimeterWalls(half, half, 3.5, stone);
  b.spawnStrips(26, 14);
  const wall = (x1, z1, x2, z2) => b.wallSegment(x1, z1, x2, z2, 3.2, stone);
  wall(-5, 4.5, -1.6, 4.5); wall(1.6, 4.5, 5, 4.5);
  wall(-5, -4.5, -1.6, -4.5); wall(1.6, -4.5, 5, -4.5);
  wall(-5.6, -3, -5.6, 3); wall(5.6, -3, 5.6, 3);
  b.box('Altar', [0, 0.5, 3.2], [0, 0, 0], [2.2, 1, 0.9], stone);
  b.box('Candle', [-0.8, 1.15, 3.2], [0, 0, 0], [0.08, 0.3, 0.08], candleGlow, true);
  b.box('Candle', [0.8, 1.15, 3.2], [0, 0, 0], [0.08, 0.3, 0.08], candleGlow, true);
  b.light([0, 1.8, 2.6], C(1, 0.25, 0.15), 1.6, 9);

  for (const sx of [-1, 1])
    for (const sz of [-1, 1]) {
      const cx = 19 * sx, cz = 18 * sz;
      b.box('House', [cx, 2.5, cz], [0, 0, 0], [8, 5, 7], wood);
      b.box('Roof', [cx, 5.4, cz], [0, 0, 8 * sx], [8.8, 0.4, 7.6], metal, true);
      b.box('Window', [cx - 4.02 * sx, 2.6, cz + 1.4], [0, 0, 0], [0.05, 1, 1.2], lampGlow, true);
    }
  const car = (x, z, yaw) => {
    b.box('WreckedCar', [x, 0.7, z], [0, yaw, 0], [4.2, 1.4, 1.9], rust);
    b.box('CarCabin', [x, 1.65, z], [0, yaw, 0], [2, 0.6, 1.7], rust, true);
  };
  car(-10, 14, 20); car(12, -12, -35); car(-14, -13, 70); car(4, 20, 95);
  for (let row = 0; row < 3; row++)
    for (let col = 0; col < 5; col++) {
      const x = 10 + col * 3;
      const z = -2 + row * 3.4 + (col % 2) * 0.6;
      const tilt = (((row * 7 + col * 3) % 5) - 2) * 4;
      b.box('Gravestone', [x, 0.5, z], [0, 90, tilt], [0.7, 1, 0.22], stone);
    }
  for (const [x, z] of [[-20, 2], [-8, -22], [22, 8], [-24, -24], [9, 26]]) b.cyl('DeadTree', [x, 2, z], [0.5, 2, 0.5], wood);
  for (const [x, z] of [[-10, 0], [7.8, 0], [0, -14], [0, 14], [-22, -5], [22, -6], [-12, 24], [14, 24], [-14, -24], [14, -24]]) {
    b.cyl('LampPost', [x, 2, z], [0.18, 2, 0.18], metal);
    b.sphere('LampBulb', [x, 4.1, z], [0.45, 0.45, 0.45], lampGlow);
    b.light([x, 3.8, z], C(1, 0.78, 0.5), 1.8, 13);
  }
  b.map.fires = [];
  for (const [x, z] of [[-3, -9], [8, 15], [-20, 9], [20, -11], [-25, -11]]) {
    b.cyl('FireBarrel', [x, 0.5, z], [0.7, 0.5, 0.7], rust);
    b.cyl('Fire', [x, 1.05, z], [0.55, 0.08, 0.55], fireGlow, true);
    b.light([x, 1.8, z], C(1, 0.5, 0.15), 2, 10);
    b.map.fires.push(b.map.lights.length - 1);
  }
  b.map.sky = 'night';
  b.map.fog = { color: C(0.03, 0.035, 0.05), density: 0.035 };
  b.map.night = true;
  const spawnsFor = (side) => {
    const s = side === 0 ? -1 : 1;
    return [[0, 0.3, 25 * s], [-10, 0.3, 24 * s], [10, 0.3, 24 * s], [-5, 0.3, 21 * s], [5, 0.3, 21 * s]];
  };
  b.map.spawns = [spawnsFor(0), spawnsFor(1)];
  b.map.survivors = [[0, 0.3, -1.5], [-2.5, 0.3, 0], [2.5, 0.3, 0], [0, 0.3, 1.4], [-2.8, 0.3, -2.8], [2.8, 0.3, -2.8], [-1.3, 0.3, -3.4], [1.3, 0.3, -3.4]];
  const zs = [];
  for (const t of [-24, -8, 8, 24]) zs.push([29, 0.3, t], [-29, 0.3, t], [t, 0.3, 29], [t, 0.3, -29]);
  b.map.zombieSpawns = zs;
  return b.map;
}

// ---------------------------------------------------------------------------
// Catalog
// ---------------------------------------------------------------------------

const BUILDERS = [
  () => buildArena(60),
  () => buildBackrooms(),
  () => buildThemeArena('DUSTBOWL WEST', 'Western', C(0.58, 0.3, 0.12), C(0.9, 0.68, 0.3), false),
  () => buildThemeArena('ABYSS', 'Underwater', C(0.02, 0.18, 0.24), C(0.1, 0.75, 0.8), false),
  () => buildSkyfreight(),
  () => buildNightfall(),
];
export const MAP_NAMES = ['ARENA', 'BACKROOMS', 'DUSTBOWL WEST', 'ABYSS', 'SKYFREIGHT', 'NIGHTFALL'];
export const DARK_MAP_INDEX = 5;
export const clampMap = (i) => Math.max(0, Math.min(BUILDERS.length - 1, i | 0));

const cache = new Map();

/** Map data converted to three.js coordinates (cached, never mutate). */
export function getMap(index) {
  index = clampMap(index);
  if (!cache.has(index)) cache.set(index, toThree(BUILDERS[index]()));
  return cache.get(index);
}

const P = (p) => [p[0], p[1], -p[2]];
const R = (r) => [-r[0], -r[1], r[2]];

function toThree(map) {
  const out = { ...map };
  out.boxes = map.boxes.map((b) => ({ ...b, p: P(b.p), r: R(b.r) }));
  out.cylinders = map.cylinders.map((c) => ({ ...c, p: P(c.p), r: R(c.r) }));
  out.spheres = map.spheres.map((s) => ({ ...s, p: P(s.p) }));
  out.lights = map.lights.map((l) => ({ ...l, p: P(l.p) }));
  out.spawns = map.spawns.map((list) => list.map(P));
  out.survivors = map.survivors ? map.survivors.map(P) : null;
  out.zombieSpawns = map.zombieSpawns ? map.zombieSpawns.map(P) : null;
  return out;
}

/** Candidate spawns for a side (0/1), or all of them for side < 0. */
export function spawnCandidates(map, side) {
  if (side < 0) return [...map.spawns[0], ...map.spawns[1]];
  return map.spawns[side];
}
export const survivorSpawns = (map) => map.survivors || spawnCandidates(map, -1);
export const zombieSpawnCandidates = (map) => map.zombieSpawns || spawnCandidates(map, -1);
