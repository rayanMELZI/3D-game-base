// Authoritative match logic (port of GameModeManager + NetworkHealth +
// NetworkPlayer death/respawn + projectiles + ZombieDirector + BotDirector).
// Runs inside the Node server for online rooms and inside the browser for
// offline play; both talk to it with the same plain-object messages.

import { WEAPONS, Type, GUN_GAME_ORDER, SNIPER, magazineSize } from './weapons.js';
import { getMap, clampMap, spawnCandidates, survivorSpawns, zombieSpawnCandidates, DARK_MAP_INDEX, MAP_NAMES } from './maps.js';
import { World, rayCharacter, raySphere, characterHeight } from './physics.js';
import { NavGrid } from './nav.js';
import { BotBrain, ZombieBrain, BOT_NAMES } from './ai.js';

export const Mode = { Duel: 0, TeamDeathmatch: 1, FreeForAll: 2, GunGame: 3, ZombieSurvival: 4 };
export const MODE_NAMES = ['DUEL', 'TEAM DEATHMATCH', 'FREE FOR ALL', 'GUN GAME', 'ZOMBIE SURVIVAL'];
export const RADAR_NAMES = ['OFF', 'ALWAYS', 'WHILE FIRING'];
export const MAX_BOTS = 7;
export const maxPlayersFor = (mode) => (mode === Mode.Duel ? 5 : 8);
export const isTeamMode = (mode) => mode === Mode.Duel || mode === Mode.TeamDeathmatch;
export const scoreLimit = (mode) =>
  mode === Mode.Duel ? 10 : mode === Mode.TeamDeathmatch ? 30 : mode === Mode.FreeForAll ? 20 : GUN_GAME_ORDER.length;

export const F = { Alive: 1, Crouch: 2, Prone: 4, Slide: 8, Protected: 16, Reloading: 32, Firing: 64, Spectating: 128, Bot: 256 };

export const XP = { Kill: 100, Headshot: 140, Win: 500, MatchComplete: 150, Zombie: 25, ZombieHead: 35 };
const ZOMBIE_KILL_POINTS = 100, ZOMBIE_HEAD_POINTS = 150, WAVE_POINTS = 50;
const RESPAWN_DELAY = 3, PLAYER_PROTECTION = 3, BOT_PROTECTION = 2, RESTART_DELAY = 8;
const MAX_HP = 100;
const THROW_COOLDOWN = [3.5, 6, 5, 1.2];

const dist = (a, b) => Math.hypot(a[0] - b[0], a[1] - b[1], a[2] - b[2]);

export class Match {
  /** emit(toId | null, msg): null = everyone in the room. */
  constructor(emit, settings = {}) {
    this.emit = emit;
    this.time = 0;
    this.settings = { mode: Mode.TeamDeathmatch, map: 0, bots: 3, radar: 2, sniperOnly: false, classLoadout: true, ...settings };
    this.ents = new Map();      // players + bots
    this.zombies = new Map();
    this.projs = new Map();
    this.nextId = 1;
    this.hostId = null;
    this.lobby = true;
    this.active = true;
    this.scores = [0, 0];
    this.winner = -1;
    this.winnerName = '';
    this.restartAt = 0;
    this.ffaTeamCounter = 2;
    this.zstate = { state: 'idle', wave: 0, toSpawn: 0, nextWaveAt: 0, nextSpawnAt: 0 };
    this.stateDirty = true;
    this.nextStateAt = 0;
    this.nextSnapAt = 0;
    this.loadMap();
  }

  get mode() { return this.settings.mode; }
  get matchActive() { return this.active && !this.lobby; }

  loadMap() {
    if (this.settings.mode === Mode.ZombieSurvival) this.settings.map = DARK_MAP_INDEX;
    this.settings.map = clampMap(this.settings.map);
    this.map = getMap(this.settings.map);
    this.world = new World(this.map);
    this.nav = new NavGrid(this.world, this.map);
  }

  // ------------------------------------------------------------------
  // Players
  // ------------------------------------------------------------------

  addPlayer(id, name, level = 1) {
    const humans = [...this.ents.values()].filter((e) => !e.bot);
    if (humans.length >= maxPlayersFor(this.mode)) return false;
    const e = this.#newCombatant(id, cleanName(name), false);
    e.level = level | 0;
    this.ents.set(id, e);
    if (!this.hostId) this.hostId = id;
    this.#assignTeam(e);
    this.feed(`${e.name}  JOINED`);
    this.emit(id, { t: 'welcome', id, host: this.hostId === id });
    if (this.matchActive) this.#spawn(e);
    this.#balanceBots();
    this.stateDirty = true;
    return true;
  }

  removePlayer(id) {
    const e = this.ents.get(id);
    if (!e) return;
    this.ents.delete(id);
    this.feed(`${e.name}  LEFT`);
    if (this.hostId === id) {
      const next = [...this.ents.values()].find((x) => !x.bot);
      this.hostId = next ? next.id : null;
      if (next) this.emit(next.id, { t: 'host' });
    }
    this.#balanceBots();
    this.stateDirty = true;
  }

  get humanCount() { return [...this.ents.values()].filter((e) => !e.bot).length; }

  #newCombatant(id, name, bot) {
    return {
      id, name, bot, team: 0, level: 1, p: [0, 0, 0], yaw: 0, pitch: 0, crouch: 0, prone: 0, slide: false,
      w: 4, att: 0, hp: MAX_HP, alive: false, respawnAt: 0, protectedUntil: 0, kills: 0, deaths: 0, score: 0,
      streak: 0, gg: 0, spectating: false, lastShotAt: -99, suppressed: false, reloading: false,
      throwReady: [0, 0, 0, 0], lastAttacker: null, brain: null,
    };
  }

  #assignTeam(e) {
    if (e.spectating) { e.team = -1; return; }
    if (this.mode === Mode.ZombieSurvival) e.team = 0;
    else if (isTeamMode(this.mode)) {
      const counts = [0, 0];
      for (const o of this.ents.values()) if (o !== e && !o.spectating && o.team >= 0 && o.team < 2) counts[o.team]++;
      e.team = counts[0] <= counts[1] ? 0 : 1;
    } else e.team = this.ffaTeamCounter++;
  }

  sameTeam(a, b) {
    if (this.mode === Mode.ZombieSurvival) return true;
    if (!isTeamMode(this.mode)) return a.id === b.id;
    return a.team === b.team;
  }

  // ------------------------------------------------------------------
  // Messages from clients
  // ------------------------------------------------------------------

  handle(id, msg) {
    const e = this.ents.get(id);
    if (!e || !msg || typeof msg.t !== 'string') return;
    switch (msg.t) {
      case 'state': return this.#onState(e, msg);
      case 'shot': return this.#onShot(e, msg);
      case 'proj': return this.#onProjectile(e, msg);
      case 'throw': return this.#onThrow(e, msg);
      case 'reload': e.reloading = !!msg.on; if (msg.on) this.emit(null, { t: 'reload', id: e.id, w: e.w, ex: e.id }); return;
      case 'level': e.level = Math.max(1, msg.level | 0); this.stateDirty = true; return;
      case 'name': e.name = cleanName(msg.name); this.stateDirty = true; return;
      case 'spectate': return this.#setSpectating(e, !!msg.on);
      case 'team': return this.#switchTeam(e);
      case 'fell': if (e.alive) this.#kill(e, null, false, false, 'FELL'); return;
      case 'settings': return this.#onSettings(e, msg.s || {});
      case 'start': if (id === this.hostId && this.lobby) this.#startMatch(); return;
      case 'toLobby': if (id === this.hostId) this.#backToLobby(); return;
    }
  }

  #onState(e, m) {
    if (!e.alive || !Array.isArray(m.p)) return;
    // Client-authoritative movement (ClientAuthNetworkTransform), sanity-clamped.
    const p = m.p.map(Number);
    if (p.some((v) => !Number.isFinite(v))) return;
    e.p = p;
    e.yaw = +m.yaw || 0;
    e.pitch = +m.pitch || 0;
    e.crouch = clamp01(+m.crouch || 0);
    e.prone = clamp01(+m.prone || 0);
    e.slide = !!m.slide;
    const w = m.w | 0;
    if (w >= 0 && w < WEAPONS.length) e.w = this.#forcedWeapon(e) ?? w;
    e.att = (m.att | 0) & 15;
    if (e.p[1] < this.world.killY) this.#kill(e, null, false, false, 'FELL');
  }

  #forcedWeapon(e) {
    if (this.mode === Mode.GunGame) return GUN_GAME_ORDER[Math.min(e.gg, GUN_GAME_ORDER.length - 1)];
    return null;
  }

  #onShot(e, m) {
    if (!e.alive || !this.matchActive) return;
    const def = WEAPONS[e.w];
    if (!def) return;
    e.protectedUntil = 0; // spawn protection ends on the first shot
    e.lastShotAt = this.time;
    e.suppressed = !!m.sup;
    const ends = Array.isArray(m.e) ? m.e.slice(0, 12) : [];
    this.emit(null, { t: 'shot', id: e.id, w: e.w, o: m.o, e: ends, sup: e.suppressed, ex: e.id });
    // Shooter-side hits (the shooter's client hits the proxy), loosely validated.
    if (!Array.isArray(m.hits)) return;
    for (const h of m.hits.slice(0, 12)) {
      const n = Math.max(1, Math.min(def.pellets, h.n | 0 || 1));
      const amount = def.damage * n;
      if (h.z !== undefined) {
        const z = this.zombies.get(h.z);
        if (z && z.hp > 0 && dist(z.p, e.p) <= def.range + 3) this.#damageZombie(z, e, amount, !!h.head);
        continue;
      }
      const v = this.ents.get(h.id);
      if (!v || !v.alive || v === e) continue;
      if (dist(v.p, e.p) > def.range + 4) continue;
      this.damage(v, e, amount, !!h.head, e.p, !!m.noScope && def.model === Type.Sniper);
    }
  }

  #onProjectile(e, m) {
    if (!e.alive || !this.matchActive) return;
    const def = WEAPONS[e.w];
    if (!def || !def.isProjectile || !Array.isArray(m.o) || !Array.isArray(m.v)) return;
    e.protectedUntil = 0;
    e.lastShotAt = this.time;
    this.spawnProjectile(e, e.w, m.o.map(Number), m.v.map(Number));
    this.emit(null, { t: 'shot', id: e.id, w: e.w, o: m.o, e: [], sup: false, ex: e.id });
  }

  spawnProjectile(owner, w, o, v) {
    const def = WEAPONS[w];
    const id = this.nextId++;
    const kind = def.model === Type.Rpg ? 'rocket' : 'grenade';
    this.projs.set(id, { id, kind, owner: owner.id, w, p: o, v, g: def.projectileGravity, born: this.time, life: 8 });
  }

  #onThrow(e, m) {
    if (!e.alive || !this.matchActive) return;
    const type = m.type | 0;
    if (type < 0 || type > 3 || this.time < e.throwReady[type]) return;
    if (!Array.isArray(m.o) || !Array.isArray(m.v)) return;
    e.throwReady[type] = this.time + THROW_COOLDOWN[type] - 0.15;
    const id = this.nextId++;
    this.projs.set(id, { id, kind: 'throw', type, owner: e.id, p: m.o.map(Number), v: m.v.map(Number), g: 9.81, born: this.time, life: type === 3 ? 5 : 2.2, stuck: false });
  }

  #setSpectating(e, on) {
    if (e.spectating === on) return;
    e.spectating = on;
    if (on) { e.alive = false; e.team = -1; }
    else { this.#assignTeam(e); if (this.matchActive) this.#spawn(e); }
    this.#balanceBots();
    this.stateDirty = true;
  }

  #switchTeam(e) {
    if (!isTeamMode(this.mode) || e.spectating) return;
    e.team = 1 - e.team;
    if (e.alive) this.#kill(e, null, false, false, 'SWITCHED TEAM', true);
    this.#balanceBots();
    this.stateDirty = true;
  }

  #onSettings(e, s) {
    if (e.id !== this.hostId || !this.lobby) return;
    const prevMap = this.settings.map, prevMode = this.settings.mode;
    if (s.mode !== undefined) this.settings.mode = Math.max(0, Math.min(4, s.mode | 0));
    if (s.map !== undefined) this.settings.map = clampMap(s.map);
    if (s.bots !== undefined) this.settings.bots = Math.max(0, Math.min(MAX_BOTS, s.bots | 0));
    if (s.radar !== undefined) this.settings.radar = Math.max(0, Math.min(2, s.radar | 0));
    if (s.sniperOnly !== undefined) this.settings.sniperOnly = !!s.sniperOnly;
    if (s.classLoadout !== undefined) this.settings.classLoadout = !!s.classLoadout;
    if (this.settings.mode === Mode.ZombieSurvival) this.settings.map = DARK_MAP_INDEX;
    if (this.settings.mode !== prevMode) for (const x of this.ents.values()) this.#assignTeam(x);
    if (this.settings.map !== prevMap || this.settings.mode !== prevMode) {
      this.loadMap();
      this.emit(null, { t: 'map', map: this.settings.map });
    }
    this.#balanceBots();
    this.stateDirty = true;
  }

  // ------------------------------------------------------------------
  // Match flow
  // ------------------------------------------------------------------

  #startMatch() {
    this.lobby = false;
    this.#resetMatch();
    this.feed(`${MODE_NAMES[this.mode]}  ·  ${MAP_NAMES[this.settings.map]}`);
  }

  #backToLobby() {
    this.lobby = true;
    this.active = true;
    for (const e of this.ents.values()) e.alive = false;
    this.zombies.clear();
    this.projs.clear();
    this.zstate = { state: 'idle', wave: 0, toSpawn: 0, nextWaveAt: 0, nextSpawnAt: 0 };
    this.stateDirty = true;
  }

  #resetMatch() {
    this.active = true;
    this.scores = [0, 0];
    this.winner = -1;
    this.winnerName = '';
    this.zombies.clear();
    this.projs.clear();
    this.#balanceBots();
    for (const e of this.ents.values()) {
      e.kills = 0; e.deaths = 0; e.score = 0; e.streak = 0; e.gg = 0;
      if (!e.spectating) this.#spawn(e);
    }
    this.zstate = { state: 'idle', wave: 0, toSpawn: 0, nextWaveAt: 0, nextSpawnAt: 0 };
    if (this.mode === Mode.ZombieSurvival) this.#beginIntermission(6);
    this.stateDirty = true;
  }

  #endMatch(winnerTeam, winner, label) {
    if (!this.active) return;
    this.active = false;
    this.winner = winnerTeam;
    this.winnerName = label ?? (winner ? winner.name : winnerTeam >= 0 && isTeamMode(this.mode) ? (winnerTeam === 0 ? 'BLUE' : 'ORANGE') : '');
    this.restartAt = this.time + RESTART_DELAY;
    const botWon = winner && winner.bot;
    for (const e of this.ents.values()) {
      if (e.bot || e.spectating) continue;
      const won = !botWon && (winner ? winner === e : winnerTeam >= 0 && isTeamMode(this.mode) && e.team === winnerTeam);
      if (this.mode !== Mode.ZombieSurvival) this.emit(e.id, { t: 'xp', amount: won ? XP.Win : XP.MatchComplete, reason: won ? 'VICTORY' : 'MATCH COMPLETE' });
    }
    this.emit(null, { t: 'end', winner: this.winnerName, team: winnerTeam });
    this.stateDirty = true;
  }

  // ------------------------------------------------------------------
  // Spawning
  // ------------------------------------------------------------------

  #spawn(e) {
    if (e.spectating) return;
    const pos = this.pickSpawn(e);
    e.p = pos;
    e.hp = MAX_HP;
    e.alive = true;
    e.reloading = false;
    e.lastAttacker = null;
    e.protectedUntil = this.time + (e.bot ? BOT_PROTECTION : PLAYER_PROTECTION);
    // Face the middle of the map.
    e.yaw = Math.atan2(pos[0], pos[2]);
    if (e.bot && e.brain) e.brain.onSpawn(this);
    if (!e.bot) this.emit(e.id, { t: 'spawn', p: pos, yaw: e.yaw, w: this.#forcedWeapon(e) });
    this.stateDirty = true;
  }

  pickSpawn(e) {
    let candidates;
    if (this.mode === Mode.ZombieSurvival) candidates = survivorSpawns(this.map);
    else candidates = spawnCandidates(this.map, isTeamMode(this.mode) ? e.team : -1);
    const enemies = [...this.ents.values()].filter((o) => o.alive && o !== e && !this.sameTeam(o, e));
    let best = null, bestScore = -Infinity;
    for (const c of candidates) {
      // Snap to the ground from 1.5 m up (higher probes land on roofs).
      const y = this.world.groundHeight(c[0], c[2], c[1] + 1.5, 6);
      const p = [c[0], y ?? c[1], c[2]];
      let score = enemies.length ? Math.min(...enemies.map((o) => dist(o.p, p))) : Math.random() * 10;
      // Avoid stacking on allies.
      for (const o of this.ents.values()) if (o.alive && o !== e && dist(o.p, p) < 1.2) score -= 50;
      score += Math.random() * 2;
      if (score > bestScore) { bestScore = score; best = p; }
    }
    return best || [0, 1, 0];
  }

  // ------------------------------------------------------------------
  // Damage & kills
  // ------------------------------------------------------------------

  /** attacker: combatant, or null (zombie / world). */
  damage(victim, attacker, amount, headshot, sourcePos, noScope = false) {
    if (!victim.alive || !this.matchActive) return;
    if (attacker && attacker !== victim && this.sameTeam(attacker, victim)) return;
    if (this.time < victim.protectedUntil) return;
    if (attacker && attacker.bot) amount *= 0.85;
    if (headshot) amount = MAX_HP; // headshots are always lethal
    victim.hp = Math.max(0, victim.hp - amount);
    if (attacker) victim.lastAttacker = attacker.id;
    if (!victim.bot) this.emit(victim.id, { t: 'hurt', hp: victim.hp, from: sourcePos || null });
    if (attacker && !attacker.bot && attacker !== victim)
      this.emit(attacker.id, { t: 'hitmark', head: headshot, kill: victim.hp <= 0 });
    if (victim.bot && victim.brain && attacker) victim.brain.onDamaged(this, attacker);
    if (victim.hp <= 0) this.#kill(victim, attacker, headshot, noScope);
    this.stateDirty = true;
  }

  #kill(victim, attacker, headshot, noScope, reason, silent = false) {
    if (!victim.alive) return;
    victim.alive = false;
    victim.hp = 0;
    victim.reloading = false;
    victim.respawnAt = this.time + RESPAWN_DELAY;
    this.emit(null, { t: 'death', id: victim.id, by: attacker ? attacker.id : null, head: headshot, p: victim.p });
    if (silent || !this.active) { this.stateDirty = true; return; }
    victim.deaths++;
    victim.streak = 0;
    if (attacker && attacker !== victim) {
      attacker.kills++;
      attacker.streak++;
      const streak = attacker.streak;
      const milestone = streak >= 5 && streak % 5 === 0;
      if (!attacker.bot) {
        this.emit(attacker.id, { t: 'xp', amount: headshot ? XP.Headshot : XP.Kill, reason: headshot ? 'HEADSHOT' : 'KILL' });
        if (milestone) this.emit(attacker.id, { t: 'streak', n: streak });
      }
      if (milestone) this.feed(`${attacker.name}  KILLSTREAK  ${streak}`);
      const verb = noScope ? 'NO-SCOPE' : headshot ? 'HEADSHOT' : 'eliminated';
      this.feed(`${attacker.name}  ${verb}  ${victim.name}`);
      switch (this.mode) {
        case Mode.Duel: case Mode.TeamDeathmatch:
          this.scores[attacker.team === 0 ? 0 : 1]++;
          if (this.scores[0] >= scoreLimit(this.mode)) this.#endMatch(0, null);
          else if (this.scores[1] >= scoreLimit(this.mode)) this.#endMatch(1, null);
          break;
        case Mode.FreeForAll:
          if (attacker.kills >= scoreLimit(this.mode)) this.#endMatch(attacker.team, attacker);
          break;
        case Mode.GunGame:
          attacker.gg++;
          if (attacker.gg >= GUN_GAME_ORDER.length) this.#endMatch(attacker.team, attacker);
          else if (attacker.bot && attacker.brain) attacker.brain.refreshWeapon(this);
          else this.emit(attacker.id, { t: 'gg', level: attacker.gg, w: GUN_GAME_ORDER[attacker.gg] });
          break;
      }
    } else if (this.mode === Mode.ZombieSurvival) {
      this.feed(`${victim.name}  WAS OVERRUN`);
    } else {
      this.feed(`${victim.name}  ${reason || 'DOWN'}`);
    }
    this.stateDirty = true;
  }

  explode(point, radius, dmg, owner, sourcePos) {
    this.emit(null, { t: 'boom', p: point, r: radius });
    const ownerEnt = owner ? this.ents.get(owner) : null;
    for (const v of [...this.ents.values()]) {
      if (!v.alive) continue;
      const c = [v.p[0], v.p[1] + 0.9, v.p[2]];
      const d = dist(c, point);
      if (d > radius) continue;
      if (!this.world.lineOfSight(point, c) && d > 1) continue;
      const falloff = Math.max(0, Math.min(1, 1 - d / radius)) * 0.75 + 0.25;
      // Rockets/grenades hurt their owner too (rocket jumps), friendly fire off otherwise.
      if (ownerEnt && v !== ownerEnt && this.sameTeam(ownerEnt, v)) continue;
      this.damage(v, ownerEnt, dmg * falloff, false, sourcePos || point);
    }
    for (const z of this.zombies.values()) {
      if (z.hp <= 0) continue;
      const d = dist([z.p[0], z.p[1] + 0.9, z.p[2]], point);
      if (d > radius) continue;
      const falloff = Math.max(0, Math.min(1, 1 - d / radius)) * 0.75 + 0.25;
      this.#damageZombie(z, ownerEnt, dmg * falloff, false);
    }
  }

  // ------------------------------------------------------------------
  // Tick
  // ------------------------------------------------------------------

  tick(dt) {
    this.time += dt;
    if (!this.lobby) {
      if (!this.active && this.time >= this.restartAt) this.#resetMatch();
      for (const e of this.ents.values()) {
        if (!e.alive && !e.spectating && this.active && this.time >= e.respawnAt && this.mode !== Mode.ZombieSurvival) this.#spawn(e);
        if (e.alive && e.bot && e.brain) e.brain.update(this, dt);
      }
      this.#tickProjectiles(dt);
      if (this.mode === Mode.ZombieSurvival && this.active) this.#tickZombies(dt);
    }
    if (this.time >= this.nextSnapAt) { this.nextSnapAt = this.time + 0.05; this.emit(null, this.snapshot()); }
    if (this.stateDirty || this.time >= this.nextStateAt) {
      this.stateDirty = false;
      this.nextStateAt = this.time + 1;
      this.emit(null, this.stateMessage());
    }
  }

  #tickProjectiles(dt) {
    for (const pr of [...this.projs.values()]) {
      const age = this.time - pr.born;
      if (pr.kind === 'throw') { this.#tickThrowable(pr, dt, age); continue; }
      const def = WEAPONS[pr.w];
      pr.v[1] -= pr.g * dt;
      const step = pr.v.map((v) => v * dt);
      const len = Math.hypot(...step);
      const dir = step.map((v) => v / (len || 1));
      let hitT = len;
      let hitPoint = null;
      const wh = this.world.raycast(pr.p, dir, len);
      if (wh) { hitT = wh.t; hitPoint = wh.point; }
      // Direct hit on a body (never its own shooter in the first 0.15 s).
      for (const v of this.ents.values()) {
        if (!v.alive || (v.id === pr.owner && age < 0.15)) continue;
        const h = rayCharacter(pr.p, dir, v, hitT);
        if (h) { hitT = h.t; hitPoint = pr.p.map((x, i) => x + dir[i] * h.t); }
      }
      for (const z of this.zombies.values()) {
        if (z.hp <= 0) continue;
        const h = rayCharacter(pr.p, dir, { p: z.p, yaw: z.yaw, crouch: 0, prone: 0 }, hitT);
        if (h) { hitT = h.t; hitPoint = pr.p.map((x, i) => x + dir[i] * h.t); }
      }
      if (hitPoint || age > pr.life || pr.p[1] < this.world.killY) {
        const at = hitPoint || pr.p;
        this.projs.delete(pr.id);
        this.explode(at, def.explosionRadius, def.damage, pr.owner, at);
        continue;
      }
      pr.p = pr.p.map((x, i) => x + step[i]);
    }
  }

  #tickThrowable(pr, dt, age) {
    if (age >= pr.life) {
      this.projs.delete(pr.id);
      if (pr.type === 1) {
        // Flashbang: blinds everyone near it (thrower included), no damage.
        this.emit(null, { t: 'flash', p: pr.p, r: 15 });
      } else if (pr.type !== 3) {
        this.#throwableBlast(pr);
      } else {
        this.emit(null, { t: 'boom', p: pr.p, r: 0.6, small: true });
      }
      return;
    }
    if (pr.stuck) {
      if (pr.stuckTo) {
        const t = this.ents.get(pr.stuckTo);
        if (t && t.alive) pr.p = [t.p[0] + pr.off[0], t.p[1] + pr.off[1], t.p[2] + pr.off[2]];
      }
      return;
    }
    pr.v[1] -= pr.g * dt;
    const step = pr.v.map((v) => v * dt);
    const len = Math.hypot(...step);
    if (len < 1e-6) return;
    const dir = step.map((v) => v / len);
    // Bodies first (knife = impact kill, sticky sticks to people).
    for (const v of this.ents.values()) {
      if (!v.alive || (v.id === pr.owner && age < 0.3)) continue;
      const h = rayCharacter(pr.p, dir, v, len + 0.1);
      if (!h) continue;
      if (pr.type === 3) {
        const owner = this.ents.get(pr.owner) || null;
        this.projs.delete(pr.id);
        this.emit(null, { t: 'boom', p: pr.p, r: 0.6, small: true });
        if (owner && owner !== v) this.damage(v, owner, MAX_HP, false, owner.p);
        return;
      }
      if (pr.type === 2) {
        pr.stuck = true; pr.stuckTo = v.id;
        pr.off = [pr.p[0] - v.p[0], pr.p[1] - v.p[1], pr.p[2] - v.p[2]];
        return;
      }
    }
    if (pr.type === 3) {
      for (const z of this.zombies.values()) {
        if (z.hp <= 0) continue;
        if (rayCharacter(pr.p, dir, { p: z.p, yaw: 0, crouch: 0, prone: 0 }, len + 0.1)) {
          this.projs.delete(pr.id);
          this.#damageZombie(z, this.ents.get(pr.owner) || null, MAX_HP, false);
          return;
        }
      }
    }
    const hit = this.world.raycast(pr.p, dir, len + 0.12);
    if (hit) {
      if (pr.type === 3) { this.projs.delete(pr.id); this.emit(null, { t: 'boom', p: hit.point, r: 0.4, small: true }); return; }
      if (pr.type === 2) { pr.stuck = true; pr.p = hit.point.map((x, i) => x + hit.normal[i] * 0.1); return; }
      // Bounce with damping.
      const n = hit.normal;
      const vn = pr.v[0] * n[0] + pr.v[1] * n[1] + pr.v[2] * n[2];
      pr.v = pr.v.map((v, i) => (v - 2 * vn * n[i]) * 0.45);
      pr.p = hit.point.map((x, i) => x + n[i] * 0.13);
      return;
    }
    pr.p = pr.p.map((x, i) => x + step[i]);
    if (pr.p[1] < this.world.killY) this.projs.delete(pr.id);
  }

  #throwableBlast(pr) {
    const radius = 5;
    const owner = this.ents.get(pr.owner) || null;
    this.emit(null, { t: 'boom', p: pr.p, r: radius });
    for (const v of [...this.ents.values()]) {
      if (!v.alive || v === owner) continue; // your own frag/sticky won't hurt you
      const d = dist([v.p[0], v.p[1] + 0.9, v.p[2]], pr.p);
      if (d > radius) continue;
      this.damage(v, owner, 100 * (1 - d / radius), false, pr.p);
    }
    for (const z of this.zombies.values()) {
      const d = dist([z.p[0], z.p[1] + 0.9, z.p[2]], pr.p);
      if (z.hp > 0 && d <= radius) this.#damageZombie(z, owner, 100 * (1 - d / radius), false);
    }
  }

  // ------------------------------------------------------------------
  // Bots (BotDirector)
  // ------------------------------------------------------------------

  #balanceBots() {
    const wanted = this.lobby || this.mode === Mode.ZombieSurvival
      ? 0
      : Math.max(0, Math.min(this.settings.bots, maxPlayersFor(this.mode) - this.humanCount));
    const bots = [...this.ents.values()].filter((e) => e.bot);
    while (bots.length > wanted) {
      const b = bots.pop();
      this.ents.delete(b.id);
    }
    while (bots.length < wanted) {
      const used = new Set(bots.map((b) => b.name));
      const name = BOT_NAMES.find((n) => !used.has(n)) || 'BOT' + this.nextId;
      const id = 'b' + this.nextId++;
      const b = this.#newCombatant(id, name, true);
      b.brain = new BotBrain(b);
      this.ents.set(id, b);
      bots.push(b);
      this.#assignTeam(b);
      if (this.matchActive) this.#spawn(b);
    }
    // Team modes: keep the bots balancing the sides.
    if (isTeamMode(this.mode)) {
      const count = (t) => [...this.ents.values()].filter((e) => !e.spectating && e.team === t).length;
      for (const b of bots) {
        const c0 = count(0), c1 = count(1);
        if (b.team === 0 && c0 > c1 + 1) b.team = 1;
        else if (b.team === 1 && c1 > c0 + 1) b.team = 0;
      }
    }
    this.stateDirty = true;
  }

  /** Bot weapon choice (BotAgent.PickWeapon). */
  botWeapon(bot) {
    if (this.mode === Mode.GunGame) return GUN_GAME_ORDER[Math.min(bot.gg, GUN_GAME_ORDER.length - 1)];
    if (this.settings.sniperOnly) return SNIPER;
    for (let attempt = 0; attempt < 12; attempt++) {
      const i = (Math.random() * WEAPONS.length) | 0;
      const m = WEAPONS[i].model;
      if (m === Type.Rifle || m === Type.Smg || m === Type.Shotgun || m === Type.Lmg) return i;
      if (m === Type.Sniper && Math.random() < 0.35) return i;
    }
    return 4;
  }

  /** Hitscan fired by a bot, resolved on the server. */
  botFire(bot, origin, dirs) {
    const def = WEAPONS[bot.w];
    const ends = [];
    const hits = new Map();
    for (const dir of dirs) {
      let bestT = def.range, target = null, head = false;
      const wh = this.world.raycast(origin, dir, def.range);
      if (wh) bestT = wh.t;
      for (const v of this.ents.values()) {
        if (!v.alive || v === bot) continue;
        const h = rayCharacter(origin, dir, v, bestT);
        if (h) { bestT = h.t; target = v; head = h.head; }
      }
      // Zombies are only in co-op, where bots never play: skipped here.
      ends.push(origin.map((x, i) => +(x + dir[i] * bestT).toFixed(2)));
      if (target) {
        const k = target.id;
        const prev = hits.get(k) || { v: target, n: 0, head: false };
        prev.n++; prev.head = prev.head || head;
        hits.set(k, prev);
      }
    }
    bot.lastShotAt = this.time;
    bot.protectedUntil = 0;
    this.emit(null, { t: 'shot', id: bot.id, w: bot.w, o: origin.map((v) => +v.toFixed(2)), e: ends, sup: false });
    for (const h of hits.values()) this.damage(h.v, bot, def.damage * h.n, h.head, bot.p);
  }

  // ------------------------------------------------------------------
  // Zombie survival (ZombieDirector)
  // ------------------------------------------------------------------

  #beginIntermission(delay) {
    this.zstate.state = 'intermission';
    this.zstate.nextWaveAt = this.time + delay;
    this.stateDirty = true;
  }

  #tickZombies(dt) {
    const zs = this.zstate;
    let alive = 0;
    for (const z of this.zombies.values()) {
      if (z.hp > 0) { alive++; z.brain.update(this, dt); }
      else if (this.time > z.deadAt + 4) this.zombies.delete(z.id);
    }
    if (zs.state === 'intermission' && this.time >= zs.nextWaveAt) {
      zs.wave++;
      zs.state = 'wave';
      zs.toSpawn = 4 + zs.wave * 3;
      zs.nextSpawnAt = this.time;
      // Revive the fallen for the new wave.
      for (const e of this.ents.values()) if (!e.spectating && !e.alive) this.#spawn(e);
      this.feed(`WAVE  ${zs.wave}`);
      this.stateDirty = true;
    } else if (zs.state === 'wave') {
      const survivors = [...this.ents.values()].filter((e) => !e.spectating).length;
      const maxAlive = Math.min(24, 8 + 3 * survivors);
      if (zs.toSpawn > 0 && alive < maxAlive && this.time >= zs.nextSpawnAt) {
        if (this.#spawnZombie()) { zs.toSpawn--; alive++; }
        zs.nextSpawnAt = this.time + 0.9;
      }
      if (zs.toSpawn === 0 && alive === 0) {
        for (const e of this.ents.values()) if (e.alive) e.score += WAVE_POINTS * zs.wave;
        this.feed(`WAVE  ${zs.wave}  SURVIVED`);
        this.#beginIntermission(10);
      }
    }
    const players = [...this.ents.values()].filter((e) => !e.spectating);
    if (players.length && players.every((e) => !e.alive)) {
      const xp = XP.MatchComplete + 50 * Math.max(0, zs.wave - 1);
      for (const e of players) this.emit(e.id, { t: 'xp', amount: xp, reason: `WAVE ${zs.wave}` });
      this.#endMatch(-1, null, `REACHED WAVE ${zs.wave}`);
    }
  }

  #spawnZombie() {
    const players = [...this.ents.values()].filter((e) => e.alive);
    const cands = zombieSpawnCandidates(this.map).filter((c) => players.every((p) => dist(p.p, c) >= 18));
    const list = cands.length ? cands : zombieSpawnCandidates(this.map);
    const c = list[(Math.random() * list.length) | 0];
    const wave = this.zstate.wave;
    const runner = wave >= 3 && Math.random() < Math.min(0.1 * (wave - 2), 0.5);
    const speed = runner ? 4.2 + Math.random() * 0.6 : Math.min(1.5 + 0.12 * wave, 3.2) * (0.85 + Math.random() * 0.3);
    const hp = Math.min(60 + 20 * (wave - 1), 400);
    const id = 'z' + this.nextId++;
    const z = { id, p: [c[0], this.world.groundHeight(c[0], c[2], c[1] + 1.5, 6) ?? 0, c[2]], yaw: Math.random() * 6.28, hp, maxHp: hp, speed, runner, damage: Math.min(25 + 2 * wave, 45), skin: (Math.random() * 4) | 0, deadAt: 0, attacking: 0 };
    z.brain = new ZombieBrain(z);
    this.zombies.set(id, z);
    this.emit(null, { t: 'growl', p: z.p });
    return true;
  }

  #damageZombie(z, attacker, amount, headshot) {
    if (z.hp <= 0) return;
    z.hp = Math.max(0, z.hp - amount * (headshot ? 3 : 1));
    if (attacker && !attacker.bot) this.emit(attacker.id, { t: 'hitmark', head: headshot, kill: z.hp <= 0 });
    if (z.hp <= 0) {
      z.deadAt = this.time;
      this.emit(null, { t: 'zdeath', id: z.id, p: z.p });
      if (attacker) {
        attacker.kills++;
        attacker.score += headshot ? ZOMBIE_HEAD_POINTS : ZOMBIE_KILL_POINTS;
        if (!attacker.bot) this.emit(attacker.id, { t: 'xp', amount: headshot ? XP.ZombieHead : XP.Zombie, reason: headshot ? 'HEADSHOT' : 'ZOMBIE' });
      }
      this.stateDirty = true;
    }
  }

  // ------------------------------------------------------------------
  // Outgoing state
  // ------------------------------------------------------------------

  snapshot() {
    const ents = [];
    for (const e of this.ents.values()) {
      let f = 0;
      if (e.alive) f |= F.Alive;
      if (e.crouch > 0.5) f |= F.Crouch;
      if (e.prone > 0.5) f |= F.Prone;
      if (e.slide) f |= F.Slide;
      if (this.time < e.protectedUntil) f |= F.Protected;
      if (e.reloading) f |= F.Reloading;
      if (this.time - e.lastShotAt < 1.2 && !e.suppressed) f |= F.Firing;
      if (e.spectating) f |= F.Spectating;
      if (e.bot) f |= F.Bot;
      ents.push([e.id, r2(e.p[0]), r2(e.p[1]), r2(e.p[2]), r3(e.yaw), r3(e.pitch), f, e.w, e.att, Math.round(e.hp), e.team]);
    }
    const zombies = [];
    for (const z of this.zombies.values())
      zombies.push([z.id, r2(z.p[0]), r2(z.p[1]), r2(z.p[2]), r3(z.yaw), Math.round(z.hp), (z.hp > 0 ? 1 : 0) | (z.runner ? 2 : 0) | (z.attacking > this.time ? 4 : 0), z.skin]);
    const projs = [];
    for (const p of this.projs.values()) projs.push([p.id, p.kind === 'throw' ? 't' + p.type : p.kind, r2(p.p[0]), r2(p.p[1]), r2(p.p[2])]);
    return { t: 'snap', time: r3(this.time), ents, zombies, projs };
  }

  stateMessage() {
    return {
      t: 'state', settings: this.settings, lobby: this.lobby, active: this.active, host: this.hostId,
      scores: this.scores, winner: this.winner, winnerName: this.winnerName,
      restartIn: this.active ? 0 : Math.max(0, this.restartAt - this.time),
      wave: this.zstate.wave, zombiesLeft: this.zstate.state === 'wave' ? this.zstate.toSpawn + [...this.zombies.values()].filter((z) => z.hp > 0).length : 0,
      waveIn: this.zstate.state === 'intermission' ? Math.max(0, this.zstate.nextWaveAt - this.time) : 0,
      players: [...this.ents.values()].map((e) => ({ id: e.id, name: e.name, team: e.team, kills: e.kills, deaths: e.deaths, score: e.score, level: e.level, gg: e.gg, bot: e.bot, spec: e.spectating, alive: e.alive })),
    };
  }

  feed(msg) { this.emit(null, { t: 'feed', msg }); }
}

function cleanName(n) {
  const s = String(n || 'PLAYER').replace(/[^\p{L}\p{N} _\-.]/gu, '').trim().slice(0, 16);
  return s || 'PLAYER';
}
const clamp01 = (v) => Math.max(0, Math.min(1, v));
const r2 = (v) => Math.round(v * 100) / 100;
const r3 = (v) => Math.round(v * 1000) / 1000;

export { magazineSize, characterHeight, raySphere };
