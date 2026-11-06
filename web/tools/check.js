// Headless checks for the web port: shared modules load, every map builds a
// collision world + nav grid with a path between the two sides, each mode
// plays two simulated minutes with bots, and the server accepts WebSocket
// clients into a room.  Run: npm run check
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { getMap, MAP_NAMES } from '../public/shared/maps.js';
import { World } from '../public/shared/physics.js';
import { NavGrid } from '../public/shared/nav.js';
import { Match, Mode, MODE_NAMES } from '../public/shared/match.js';
import { WEAPONS } from '../public/shared/weapons.js';

let failed = 0;
const ok = (cond, msg) => { console.log(`${cond ? 'ok  ' : 'FAIL'} ${msg}`); if (!cond) failed++; };

ok(WEAPONS.length === 31, `arsenal has 31 weapons (got ${WEAPONS.length})`);
ok(WEAPONS[0].name === 'KNIFE' && WEAPONS[4].name === 'RIFLE' && WEAPONS[30].name === 'REVOLVER', 'weapon indexes match the Unity loadout');

for (let i = 0; i < MAP_NAMES.length; i++) {
  const map = getMap(i);
  const world = new World(map);
  const nav = new NavGrid(world, map);
  const path_ = nav.findPath(map.spawns[0][0], map.spawns[1][0]);
  ok(!!path_, `${map.name}: nav path between the two spawn sides`);
  for (const side of [0, 1])
    for (const s of map.spawns[side]) {
      const y = world.groundHeight(s[0], s[2], s[1] + 1.5, 6);
      if (y === null) ok(false, `${map.name}: spawn ${s} has no ground`);
    }
}

for (const [mode, map] of [[Mode.Duel, 0], [Mode.TeamDeathmatch, 1], [Mode.FreeForAll, 4], [Mode.GunGame, 2], [Mode.ZombieSurvival, 5]]) {
  let kills = 0;
  const m = new Match((to, msg) => { if (msg.t === 'death') kills++; }, { mode, map, bots: 5 });
  m.addPlayer('p1', 'TEST', 1);
  m.handle('p1', { t: 'start' });
  for (let k = 0; k < 30 * 120; k++) m.tick(1 / 30);
  ok(kills > 0, `${MODE_NAMES[mode]} on ${MAP_NAMES[m.settings.map]}: ${kills} deaths in 2 simulated minutes`);
}

// Server smoke test on a spare port.
const root = path.join(path.dirname(fileURLToPath(import.meta.url)), '..');
const port = 8000 + Math.floor(Math.random() * 900);
const server = spawn(process.execPath, ['server.js'], { cwd: root, env: { ...process.env, PORT: String(port) }, stdio: 'ignore' });
await new Promise((r) => setTimeout(r, 700));
try {
  const seen = new Set();
  await new Promise((resolve, reject) => {
    const ws = new WebSocket(`ws://localhost:${port}/ws?name=CHECK`);
    const timer = setTimeout(() => reject(new Error('timeout')), 4000);
    ws.onmessage = (ev) => {
      const msg = JSON.parse(ev.data);
      seen.add(msg.t);
      if (msg.t === 'welcome') ws.send(JSON.stringify({ t: 'start' }));
      if (seen.has('spawn') && seen.has('state')) { clearTimeout(timer); ws.close(); resolve(); }
    };
    ws.onerror = () => reject(new Error('socket error'));
  });
  ok(seen.has('room') && seen.has('state') && seen.has('spawn'), `server: room, state and spawn received (${[...seen].join(', ')})`);
  const res = await fetch(`http://localhost:${port}/index.html`);
  ok(res.ok, 'server: serves index.html');
  const bad = await fetch(`http://localhost:${port}/..%2fserver.js`);
  ok(bad.status === 404 || bad.status === 403, 'server: no path traversal');
} catch (e) {
  ok(false, 'server smoke test: ' + e.message);
} finally {
  server.kill();
}

console.log(failed ? `\n${failed} check(s) failed` : '\nall checks passed');
process.exit(failed ? 1 : 0);
