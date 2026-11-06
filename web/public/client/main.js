// SUNDOWN ARENA (web) — game client: render loop, local player, remote
// proxies with snapshot interpolation, network events, death cam, spectator.

import * as THREE from 'three';
import { getMap, teamColor, clampMap } from '../shared/maps.js';
import { World } from '../shared/physics.js';
import { WEAPONS, Type } from '../shared/weapons.js';
import { F, Mode, isTeamMode } from '../shared/match.js';
import { buildWorld } from './world.js';
import { makeHumanoid, makeWeapon, makeProjectileMesh } from './models.js';
import { Effects } from './effects.js';
import { LocalPlayer } from './player.js';
import { WeaponController } from './weapons.js';
import { Hud } from './hud.js';
import { Menu } from './menu.js';
import { Input, PAD } from './input.js';
import { OnlineTransport, LocalTransport, serverUrl } from './net.js';
import { initAudio, clips, play2D, playAt, setListener } from './audio.js';
import { S, save, levelFor, awardXp } from './settings.js';

const canvas = document.getElementById('game');
const renderer = new THREE.WebGLRenderer({ canvas, antialias: S.quality !== 'low', powerPreference: 'high-performance', preserveDrawingBuffer: location.search.includes('shot') });
renderer.setPixelRatio(Math.min(devicePixelRatio, S.quality === 'high' ? 2 : 1));
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = 1.05;
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.shadowMap.enabled = S.quality !== 'low';
renderer.shadowMap.type = THREE.PCFSoftShadowMap;

const scene = new THREE.Scene();
const camera = new THREE.PerspectiveCamera(S.fov, 1, 0.03, 1500);
camera.rotation.order = 'YXZ';
scene.add(camera);

const effects = new Effects(scene);
const hud = new Hud();
const player = new LocalPlayer();
const weapons = new WeaponController(camera, effects);
Input.init(canvas);

// ---------------------------------------------------------------------------
// Game state
// ---------------------------------------------------------------------------

const G = {
  transport: null, myId: null, isHost: false, code: '', state: null,
  mapIndex: -1, world: null, view: null,
  remotes: new Map(), zombieViews: new Map(), projViews: new Map(),
  snaps: [], serverOffset: null,
  alive: false, me: null, spectating: false,
  deathcam: null, scoreboard: false, inGame: false,
  lastStateSent: 0, lastFell: 0, breathing: null, lastHurtAt: -9,
  lastConfig: '', time: 0,
};

const menu = new Menu({
  solo: (settings) => startSession(new LocalTransport(S.name, levelFor(S.experience), settings), true),
  host: () => startSession(new OnlineTransport(serverUrl('', S.name, levelFor(S.experience))), false),
  join: (code) => startSession(new OnlineTransport(serverUrl(code, S.name, levelFor(S.experience))), false),
  sendSettings: (s) => send({ t: 'settings', s }),
  start: () => send({ t: 'start' }),
  resume: () => { menu.close(); Input.lock(canvas); },
  leave: () => endSession(),
  toLobby: () => send({ t: 'toLobby' }),
  switchTeam: () => send({ t: 'team' }),
  spectate: (on) => send({ t: 'spectate', on }),
  onSettings: () => { camera.fov = S.fov; camera.updateProjectionMatrix(); },
  onName: () => { if (G.transport) send({ t: 'name', name: S.name }); },
  myId: () => G.myId,
  inGame: () => G.inGame,
});

function send(msg) { G.transport?.send(msg); }

function startSession(transport, solo) {
  initAudio();
  endSession(true);
  G.transport = transport;
  G.inGame = true;
  G.solo = solo;
  transport.onMessage(onMessage);
  menu.open('lobby');
  menu.setLobby(null, false, '');
}

function endSession(silent = false) {
  if (G.transport) G.transport.close();
  G.transport = null;
  G.inGame = false;
  G.myId = null;
  G.state = null;
  G.alive = false;
  G.snaps = [];
  G.serverOffset = null;
  for (const r of G.remotes.values()) scene.remove(r.model.group);
  G.remotes.clear();
  for (const z of G.zombieViews.values()) scene.remove(z.model.group);
  G.zombieViews.clear();
  for (const p of G.projViews.values()) scene.remove(p.mesh);
  G.projViews.clear();
  effects.clearDecals();
  hud.show(false);
  Input.unlock();
  if (!silent) { menu.open('main'); menu.refreshRooms(); }
}

// ---------------------------------------------------------------------------
// Map
// ---------------------------------------------------------------------------

function loadMap(index) {
  index = clampMap(index);
  if (index === G.mapIndex) return;
  G.mapIndex = index;
  if (G.view) G.view.dispose();
  const map = getMap(index);
  G.map = map;
  G.world = new World(map);
  G.view = buildWorld(scene, map, S.quality);
  effects.clearDecals();
}

// ---------------------------------------------------------------------------
// Network messages
// ---------------------------------------------------------------------------

function onMessage(m) {
  switch (m.t) {
    case 'room': G.code = m.code; break;
    case 'welcome': G.myId = m.id; G.isHost = m.host; break;
    case 'host': G.isHost = true; hud.addFeed('YOU ARE NOW THE HOST'); break;
    case 'error': endSession(); menu.error(m.msg); break;
    case 'closed': if (G.inGame) { endSession(); menu.error('DISCONNECTED FROM THE SERVER'); } break;
    case 'state': onState(m); break;
    case 'snap': onSnap(m); break;
    case 'map': loadMap(m.map); break;
    case 'spawn': onSpawn(m); break;
    case 'feed': hud.addFeed(m.msg); break;
    case 'shot': onRemoteShot(m); break;
    case 'reload': { const r = G.remotes.get(m.id); if (r) playAt(clips.reload(), r.p, 0.6, 1, 2, 30); break; }
    case 'hurt': onHurt(m); break;
    case 'hitmark':
      hud.hitMarker(m.head, m.kill);
      play2D(m.head ? clips.headshot() : clips.hit(), m.kill ? 0.9 : 0.6);
      break;
    case 'death': onDeath(m); break;
    case 'xp': {
      hud.xp(m.amount, m.reason);
      if (awardXp(m.amount)) { play2D(clips.levelUp(), 0.7); hud.banner('LEVEL UP', `LEVEL ${levelFor(S.experience)}`, 2); send({ t: 'level', level: levelFor(S.experience) }); }
      break;
    }
    case 'streak': play2D(clips.streak(m.n), 0.8); hud.banner(`${m.n} KILLSTREAK`, '', 1.8); break;
    case 'gg': weapons.force(m.w); hud.banner(WEAPONS[m.w].name, `GUN GAME ${m.level + 1}`, 1.5); break;
    case 'boom': effects.explosion(m.p, m.r, m.small); playAt(clips.explosion(), m.p, m.small ? 0.3 : 1, m.small ? 1.6 : 1, 6, 120); shakeFrom(m.p, m.r); break;
    case 'flash': onFlash(m); break;
    case 'growl': playAt(clips.growl(), m.p, 0.7, 0.9 + Math.random() * 0.2, 3, 40); break;
    case 'zdeath': playAt(clips.death(), m.p, 0.6, 0.7, 3, 40); break;
    case 'end': play2D(clips.win(), 0.6); hud.banner(m.winner ? (G.state?.settings.mode === Mode.ZombieSurvival ? m.winner : `${m.winner} WINS`) : 'MATCH OVER', 'NEXT MATCH SOON', 4); break;
  }
}

function onState(m) {
  const prevLobby = G.state ? G.state.lobby : true;
  G.state = m;
  G.isHost = m.host === G.myId;
  loadMap(m.settings.map);
  menu.setLobby(m, G.isHost, G.code);
  const me = m.players.find((p) => p.id === G.myId);
  G.spectating = !!(me && me.spec);
  if (m.lobby) {
    if (!menu.isOpen || (menu.screen !== 'lobby' && menu.screen !== 'classes')) menu.open('lobby');
    hud.show(false);
    if (!prevLobby) Input.unlock();
  } else if (menu.isOpen && menu.screen === 'lobby') {
    menu.close();
    hud.show(true);
    Input.lock(canvas);
  } else hud.show(true);
}

function onSnap(m) {
  const now = performance.now() / 1000;
  const offset = m.time - now;
  G.serverOffset = G.serverOffset === null ? offset : G.serverOffset + (offset - G.serverOffset) * 0.05;
  G.snaps.push(m);
  if (G.snaps.length > 30) G.snaps.shift();
  const seen = new Set();
  for (const e of m.ents) {
    const [id, x, y, z, yaw, pitch, flags, w, att, hp, team] = e;
    seen.add(id);
    if (id === G.myId) {
      const wasAlive = G.alive;
      G.alive = !!(flags & F.Alive);
      G.me = { hp, flags, team, w };
      if (wasAlive && !G.alive) weapons.ads = 0;
      continue;
    }
    let r = G.remotes.get(id);
    if (!r) {
      r = { id, model: makeHumanoid(srgbColor(teamColor(team))), w: -1, att: -1, team, buf: [], p: [x, y, z], yaw, pitch: 0, speed: 0, nametag: null };
      scene.add(r.model.group);
      G.remotes.set(id, r);
    }
    r.buf.push({ t: m.time, p: [x, y, z], yaw, pitch });
    if (r.buf.length > 20) r.buf.shift();
    r.flags = flags; r.hp = hp;
    if (r.team !== team) { r.team = team; r.model.setTeamColor(srgbColor(teamColor(team))); r.nametagDirty = true; }
    if (r.w !== w || r.att !== att) {
      r.w = w; r.att = att;
      r.model.gunMount.clear();
      const wm = makeWeapon(WEAPONS[w] || WEAPONS[4], att);
      r.model.gunMount.add(wm.group);
      r.muzzle = wm.muzzle;
    }
  }
  for (const [id, r] of G.remotes) if (!seen.has(id)) { scene.remove(r.model.group); G.remotes.delete(id); }

  const zs = new Set();
  for (const z of m.zombies) {
    const [id, x, y, zz, yaw, hp, flags, skin] = z;
    zs.add(id);
    let v = G.zombieViews.get(id);
    if (!v) {
      v = { id, model: makeHumanoid(null, 'zombie', skin), buf: [], p: [x, y, zz], yaw, alive: true, fall: 0 };
      scene.add(v.model.group);
      G.zombieViews.set(id, v);
    }
    v.buf.push({ t: m.time, p: [x, y, zz], yaw });
    if (v.buf.length > 20) v.buf.shift();
    v.alive = !!(flags & 1); v.runner = !!(flags & 2); v.attacking = !!(flags & 4); v.hp = hp;
  }
  for (const [id, v] of G.zombieViews) if (!zs.has(id)) { scene.remove(v.model.group); G.zombieViews.delete(id); }

  const ps = new Set();
  for (const pr of m.projs) {
    const [id, kind, x, y, z] = pr;
    ps.add(id);
    let v = G.projViews.get(id);
    if (!v) {
      v = { mesh: makeProjectileMesh(kind), prev: [x, y, z], cur: [x, y, z], t: m.time };
      scene.add(v.mesh);
      G.projViews.set(id, v);
    }
    v.prev = v.cur; v.cur = [x, y, z]; v.t = m.time;
  }
  for (const [id, v] of G.projViews) if (!ps.has(id)) { scene.remove(v.mesh); G.projViews.delete(id); }
}

function onSpawn(m) {
  player.teleport(m.p, m.yaw);
  G.deathcam = null;
  G.alive = true;
  configureWeapons(m.w);
  hud.center('');
}

function configureWeapons(forced) {
  const st = G.state?.settings;
  weapons.configure({ classLoadout: st ? st.classLoadout : true, sniperOnly: st ? st.sniperOnly : false, forced: forced ?? null });
  const def = weapons.def;
  player.speedMultiplier = def.mobility;
}

function onRemoteShot(m) {
  const r = G.remotes.get(m.id);
  const def = WEAPONS[m.w];
  if (!def) return;
  const o = m.o || (r ? r.p : [0, 0, 0]);
  playAt(clips.shot(def.model), o, m.sup ? 0.35 : 0.9, def.shotPitch, 5, m.sup ? 35 : 120);
  const from = r && r.muzzle ? worldPos(r.muzzle) : o;
  if (!m.sup && !def.isMelee) effects.muzzleFlash(from, null, def.model === Type.Pistol);
  for (const e of m.e || []) {
    effects.tracer(from, e);
    // Impact from the end point: find the surface right there.
    const d = [e[0] - from[0], e[1] - from[1], e[2] - from[2]];
    const len = Math.hypot(...d);
    if (len < 0.1 || !G.world) continue;
    const dir = d.map((v) => v / len);
    const hit = G.world.raycast(from, dir, len + 0.2);
    if (hit && Math.abs(hit.t - len) < 0.5) effects.impact(hit.point, hit.normal, false);
    else effects.impact(e, dir.map((v) => -v), true);
  }
}

function onHurt(m) {
  hud.hurt(m.from, player);
  G.lastHurtAt = G.time;
  if (m.hp < 35 && m.hp > 0 && (!G.breathing || G.time - G.breathingAt > 4)) {
    // Heavy breathing: starts after the hit, fades out on its own.
    G.breathing = play2D(clips.breathing(), 0.5);
    G.breathingAt = G.time;
  }
}

function onDeath(m) {
  const pos = m.p;
  playAt(clips.death(), pos, 0.8, 1, 3, 50);
  const r = G.remotes.get(m.id);
  effects.deathBurst([pos[0], pos[1] + 1.1, pos[2]], teamColor(r ? r.team : G.me?.team ?? 0));
  if (m.id === G.myId) {
    G.alive = false;
    const killer = m.by && m.by !== G.myId ? m.by : null;
    G.deathcam = { start: G.time, pos: player.eyePosition(), killer, yaw: player.yaw };
    const name = killer ? G.state?.players.find((p) => p.id === killer)?.name : null;
    hud.center(name ? `KILLED BY ${name}` : 'YOU DIED');
  }
}

function onFlash(m) {
  if (!G.alive) return;
  const eye = player.eyePosition();
  const d = Math.hypot(eye[0] - m.p[0], eye[1] - m.p[1], eye[2] - m.p[2]);
  if (d > m.r) return;
  const falloff = 1 - d / m.r;
  // Looking away halves it; walls block it.
  const fwd = [-Math.sin(player.yaw), -Math.cos(player.yaw)];
  const to = [(m.p[0] - eye[0]) / (d || 1), (m.p[2] - eye[2]) / (d || 1)];
  const facing = fwd[0] * to[0] + fwd[1] * to[1] > 0 ? 1 : 0.5;
  if (!G.world.lineOfSight(eye, m.p)) return;
  hud.flashbang(Math.sqrt(falloff) * facing);
  playAt(clips.explosion(), m.p, 0.5, 1.8, 5, 60);
}

let shake = 0;
function shakeFrom(p, r) {
  const d = Math.hypot(p[0] - player.p[0], p[2] - player.p[2]);
  shake = Math.max(shake, Math.max(0, 1 - d / (r * 4)) * 0.6);
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function srgbColor(c) { return c ? new THREE.Color().setRGB(c[0], c[1], c[2], THREE.SRGBColorSpace) : null; }
function worldPos(obj) { const v = new THREE.Vector3(); obj.getWorldPosition(v); return [v.x, v.y, v.z]; }

function isAlly(r) {
  const st = G.state;
  if (!st || !G.me) return false;
  if (st.settings.mode === Mode.ZombieSurvival) return true;
  return isTeamMode(st.settings.mode) && r.team === G.me.team;
}

function interp(buf, t) {
  if (!buf.length) return null;
  if (t <= buf[0].t) return buf[0];
  for (let k = buf.length - 1; k >= 0; k--) {
    if (buf[k].t <= t) {
      const a = buf[k], b = buf[k + 1];
      if (!b) return a;
      const u = (t - a.t) / Math.max(1e-4, b.t - a.t);
      const dyaw = Math.atan2(Math.sin(b.yaw - a.yaw), Math.cos(b.yaw - a.yaw));
      return { p: a.p.map((v, i) => v + (b.p[i] - v) * u), yaw: a.yaw + dyaw * u, pitch: a.pitch !== undefined ? a.pitch + (b.pitch - a.pitch) * u : 0 };
    }
  }
  return buf[buf.length - 1];
}

function makeNametag(text, color) {
  const c = document.createElement('canvas');
  c.width = 256; c.height = 48;
  const g = c.getContext('2d');
  g.font = 'bold 26px "Segoe UI", sans-serif';
  g.textAlign = 'center';
  g.lineWidth = 5; g.strokeStyle = 'rgba(0,0,0,0.75)';
  g.strokeText(text, 128, 32);
  g.fillStyle = color;
  g.fillText(text, 128, 32);
  const tex = new THREE.CanvasTexture(c);
  tex.colorSpace = THREE.SRGBColorSpace;
  const s = new THREE.Sprite(new THREE.SpriteMaterial({ map: tex, depthTest: false, transparent: true }));
  s.scale.set(1.3, 0.24, 1);
  s.renderOrder = 20;
  return s;
}

/** What the local weapon can hit: remote proxies and zombies, in the shared hitbox format. */
function targets() {
  const list = [];
  for (const r of G.remotes.values()) {
    if (!(r.flags & F.Alive)) continue;
    list.push({ id: r.id, char: { p: r.p, yaw: r.yaw, crouch: r.flags & F.Crouch ? 1 : 0, prone: r.flags & F.Prone ? 1 : 0 } });
  }
  return list;
}
function zombieTargets() {
  const list = [];
  for (const z of G.zombieViews.values()) if (z.alive) list.push({ id: z.id, char: { p: z.p, yaw: z.yaw, crouch: 0, prone: 0 } });
  return list;
}

// ---------------------------------------------------------------------------
// Frame
// ---------------------------------------------------------------------------

let last = performance.now();
let fpsAcc = 0, fpsFrames = 0;
const fpsEl = document.getElementById('fps');

function frame(now) {
  requestAnimationFrame(frame);
  const dt = Math.min(0.05, (now - last) / 1000);
  last = now;
  G.time += dt;
  Input.pollPad();
  resize();

  const playing = G.inGame && G.state && !G.state.lobby && !menu.isOpen;
  if (G.inGame && (Input.hit('Escape') || Input.padHit(PAD.Menu)) && !G.state?.lobby) {
    if (menu.isOpen && menu.screen === 'pause') { menu.close(); Input.lock(canvas); }
    else if (!menu.isOpen) { menu.open('pause'); Input.unlock(); }
  }
  if (G.inGame && !Input.locked && playing && !menu.isOpen) hud.center(G.alive ? 'CLICK TO PLAY' : hudCenterDead());
  G.scoreboard = Input.down('Tab') || Input.padDown(PAD.View);

  const active = G.state && G.state.active;
  if (playing && G.alive && G.world) {
    if (Input.locked || Input.pad) {
      player.look(dt, weapons.aiming ? weapons.zoomFov(S.fov) / S.fov : 1);
      const rec = weapons.takeRecoil(dt);
      player.pitch = Math.min(1.53, player.pitch + rec[0]);
      player.yaw += rec[1];
    }
    player.speedMultiplier = weapons.def.mobility;
    const firing = Input.mouse[0];
    const step = player.update(dt, G.world, { aiming: weapons.aiming, firing });
    if (step) play2D(clips.footstep(), player.sprinting ? 0.25 : 0.15, 0.8 + Math.random() * 0.3);
    weapons.update(dt, {
      world: G.world, eye: player.eyePosition(), player, targets: targets(), zombies: zombieTargets(),
      send, canFire: Input.locked && active, alive: true,
    });
    if (player.p[1] < (G.world.killY ?? -20) && G.time - G.lastFell > 1) { G.lastFell = G.time; send({ t: 'fell' }); }
    if (G.time - G.lastStateSent > 0.05) {
      G.lastStateSent = G.time;
      send({ t: 'state', p: player.p.map((v) => +v.toFixed(3)), yaw: +player.yaw.toFixed(4), pitch: +player.pitch.toFixed(4), crouch: +player.crouchBlend.toFixed(2), prone: +player.proneBlend.toFixed(2), slide: player.sliding, w: weapons.index, att: weapons.att });
    }
  } else if (playing && G.world) {
    weapons.update(dt, { world: G.world, eye: player.eyePosition(), player, targets: [], zombies: [], send, canFire: false, alive: false });
  }

  // ---- Remote proxies ----
  const renderT = (G.serverOffset !== null ? now / 1000 + G.serverOffset : 0) - 0.1;
  for (const r of G.remotes.values()) {
    const s = interp(r.buf, renderT);
    if (s) {
      const prev = r.p;
      r.p = s.p; r.yaw = s.yaw; r.pitch = s.pitch;
      r.speed = r.speed * 0.8 + (Math.hypot(s.p[0] - prev[0], s.p[2] - prev[2]) / Math.max(dt, 1e-3)) * 0.2;
    }
    const alive = !!(r.flags & F.Alive) && !(r.flags & F.Spectating);
    const g = r.model.group;
    g.visible = alive;
    g.position.set(r.p[0], r.p[1], r.p[2]);
    g.rotation.y = r.yaw;
    r.model.pose({ dt, speed: r.speed, pitch: r.pitch, crouch: r.flags & F.Crouch ? 1 : 0, prone: r.flags & F.Prone ? 1 : 0, slide: !!(r.flags & F.Slide), protected: !!(r.flags & F.Protected) });
    // Nametags: allies only (enemies must be spotted).
    const ally = isAlly(r);
    if (ally && (!r.nametag || r.nametagDirty)) {
      if (r.nametag) g.remove(r.nametag);
      const name = G.state?.players.find((p) => p.id === r.id)?.name || '';
      const c = teamColor(r.team);
      r.nametag = makeNametag(name, `rgb(${c.map((v) => Math.round(v * 255)).join(',')})`);
      r.nametag.position.y = 2.15;
      g.add(r.nametag);
      r.nametagDirty = false;
    }
    if (r.nametag) r.nametag.visible = ally;
  }
  for (const z of G.zombieViews.values()) {
    const s = interp(z.buf, renderT);
    if (s) {
      const prev = z.p;
      z.p = s.p; z.yaw = s.yaw;
      z.speed = Math.hypot(s.p[0] - prev[0], s.p[2] - prev[2]) / Math.max(dt, 1e-3);
    }
    const g = z.model.group;
    g.position.set(z.p[0], z.p[1], z.p[2]);
    g.rotation.y = z.yaw;
    if (!z.alive) { z.fall = Math.min(1, z.fall + dt * 2.5); g.rotation.x = z.fall * 1.45; g.position.y = z.p[1] + 0.15 * z.fall; }
    z.model.pose({ dt, speed: z.alive ? z.speed : 0, attack: z.attacking ? 1 : 0 });
  }
  for (const v of G.projViews.values()) {
    const u = Math.min(1.5, (renderT + 0.1 - v.t) / 0.05);
    const p = v.prev.map((x, i) => x + (v.cur[i] - x) * Math.max(0, u));
    v.mesh.position.set(p[0], p[1], p[2]);
    const d = new THREE.Vector3(v.cur[0] - v.prev[0], v.cur[1] - v.prev[1], v.cur[2] - v.prev[2]);
    if (d.lengthSq() > 1e-6) v.mesh.lookAt(v.mesh.position.clone().sub(d));
    if (v.mesh.children.length && Math.random() < 0.6) effects.emit(p, [0, 0.3, 0], [0.5, 0.5, 0.5], 0.4, 0.2, 0.8, -0.3, 0.6);
  }

  // ---- Camera ----
  updateCamera(dt, playing);

  if (G.view) G.view.atmosphere.update(dt, G.time);
  effects.update(dt);
  if (G.inGame && G.state && !G.state.lobby) {
    hud.update(dt, { player, weapons, state: G.state, me: G.me, myId: G.myId, alive: G.alive, deathcam: !!G.deathcam, scoreboard: G.scoreboard, remotes: G.remotes, zombieViews: G.zombieViews, isAlly });
    if (!G.alive && !G.spectating && Input.locked) hud.center(hudCenterDead());
    else if (G.alive && Input.locked) hud.center('');
  }
  setListener(camera.position.toArray(), player.yaw);

  renderer.render(scene, camera);
  Input.endFrame();

  fpsAcc += dt; fpsFrames++;
  if (fpsAcc > 0.5) { fpsEl.textContent = S.showFps ? `${Math.round(fpsFrames / fpsAcc)} FPS` : ''; fpsAcc = 0; fpsFrames = 0; }
}

function hudCenterDead() {
  if (G.spectating) return 'SPECTATING · ESC FOR MENU';
  const st = G.state;
  if (st && st.settings.mode === Mode.ZombieSurvival && st.active) return 'DOWN · BACK NEXT WAVE';
  if (G.deathcam && G.deathcam.killer) {
    const name = st?.players.find((p) => p.id === G.deathcam.killer)?.name;
    if (name) return `KILLED BY ${name}`;
  }
  return 'RESPAWNING…';
}

let freeCam = { p: [0, 12, 20], yaw: 0, pitch: -0.4 };
function updateCamera(dt, playing) {
  const shakeAmt = (shake + (G.view?.atmosphere.shake || 0) * (G.map?.flight ? 0.08 : 0));
  shake = Math.max(0, shake - dt * 1.5);
  const jx = (Math.random() - 0.5) * shakeAmt * 0.03, jy = (Math.random() - 0.5) * shakeAmt * 0.03;
  if (!G.inGame || !G.state || G.state.lobby) {
    // Menu camera: slow orbit over the map.
    const t = G.time * 0.05;
    camera.position.set(Math.sin(t) * 26, 11, Math.cos(t) * 26);
    camera.lookAt(0, 1, 0);
    if (!G.world) loadMap(0);
    weapons.holder.visible = false;
    return;
  }
  weapons.holder.visible = G.alive;
  if (G.alive) {
    const eye = player.eyePosition();
    camera.position.set(eye[0], eye[1], eye[2]);
    camera.rotation.set(player.pitch + jy, player.yaw + jx, player.sliding ? 0.06 : 0);
    const target = weapons.zoomFov(S.fov);
    if (Math.abs(camera.fov - target) > 0.01) { camera.fov = target; camera.updateProjectionMatrix(); }
    effects.setScale(renderer.domElement.height, camera.fov);
    return;
  }
  if (camera.fov !== S.fov) { camera.fov = S.fov; camera.updateProjectionMatrix(); }
  if (G.spectating || !G.deathcam) {
    // Free-fly spectator camera.
    if (Input.locked) {
      freeCam.yaw -= Input.dx * 0.0022 * S.sensitivity;
      freeCam.pitch = Math.max(-1.5, Math.min(1.5, freeCam.pitch - Input.dy * 0.0022 * S.sensitivity));
      const sp = (Input.down('ShiftLeft') ? 22 : 9) * dt;
      const f = [-Math.sin(freeCam.yaw) * Math.cos(freeCam.pitch), Math.sin(freeCam.pitch), -Math.cos(freeCam.yaw) * Math.cos(freeCam.pitch)];
      const r = [Math.cos(freeCam.yaw), 0, -Math.sin(freeCam.yaw)];
      const ax = (Input.down('KeyD') ? 1 : 0) - (Input.down('KeyA') ? 1 : 0), az = (Input.down('KeyW') ? 1 : 0) - (Input.down('KeyS') ? 1 : 0);
      const ay = (Input.down('Space') ? 1 : 0) - (Input.down('ControlLeft') ? 1 : 0);
      for (let i = 0; i < 3; i++) freeCam.p[i] += (f[i] * az + r[i] * ax) * sp + (i === 1 ? ay * sp : 0);
    }
    camera.position.set(...freeCam.p);
    camera.rotation.set(freeCam.pitch, freeCam.yaw, 0);
    return;
  }
  // Death cam: rise behind the body and look at the killer.
  const dc = G.deathcam;
  const u = Math.min(1, (G.time - dc.start) / 1.2);
  const k = dc.killer ? G.remotes.get(dc.killer) : null;
  const look = k ? [k.p[0], k.p[1] + 1.4, k.p[2]] : [dc.pos[0] - Math.sin(dc.yaw) * 3, dc.pos[1] - 1, dc.pos[2] - Math.cos(dc.yaw) * 3];
  const back = [dc.pos[0] - look[0], 0, dc.pos[2] - look[2]];
  const bl = Math.hypot(back[0], back[2]) || 1;
  const camPos = [dc.pos[0] + (back[0] / bl) * 2.5 * u, dc.pos[1] + 1.8 * u, dc.pos[2] + (back[2] / bl) * 2.5 * u];
  camera.position.set(...camPos);
  camera.lookAt(...look);
  freeCam = { p: camPos, yaw: camera.rotation.y, pitch: camera.rotation.x };
}

let lastW = 0, lastH = 0;
function resize() {
  const w = innerWidth, h = innerHeight;
  if (w === lastW && h === lastH) return;
  lastW = w; lastH = h;
  renderer.setSize(w, h, false);
  camera.aspect = w / h;
  camera.updateProjectionMatrix();
}

canvas.addEventListener('click', () => {
  initAudio();
  if (G.inGame && G.state && !G.state.lobby && !menu.isOpen) Input.lock(canvas);
});
document.addEventListener('pointerlockchange', () => {
  if (!document.pointerLockElement && G.inGame && G.state && !G.state.lobby && !menu.isOpen) { menu.open('pause'); }
});
addEventListener('beforeunload', () => save());

// Deep links: ?solo=mode,map,bots — and ?shot for automated screenshots.
const params = new URLSearchParams(location.search);
loadMap(0);
menu.open('main');
menu.refreshRooms();
if (params.has('solo')) {
  const [mode, map, bots] = (params.get('solo') || '1,0,5').split(',').map(Number);
  startSession(new LocalTransport(S.name, levelFor(S.experience), { mode, map, bots, radar: 2, sniperOnly: false, classLoadout: true }), true);
  setTimeout(() => send({ t: 'start' }), 300);
}
requestAnimationFrame(frame);
window.__game = { G, player, weapons, send, camera };
