// Port of Network/MainMenu.cs screens as DOM: main menu, pre-game lobby
// (host configures, everyone sees it live), CLASSES, SETTINGS, pause.

import { WEAPONS, Type, ATT_NAMES, allowedMask, attBit, THROWABLES } from '../shared/weapons.js';
import { MAP_NAMES, DARK_MAP_INDEX } from '../shared/maps.js';
import { MODE_NAMES, RADAR_NAMES, MAX_BOTS, Mode, maxPlayersFor } from '../shared/match.js';
import { S, save, CLASS_NAMES, CLASS_COUNT, getAtt, setAtt, levelFor, xpForLevel } from './settings.js';
import { clips, play2D, setVolume } from './audio.js';
import { esc } from './hud.js';

const root = () => document.getElementById('menu');
const click = () => play2D(clips.click(), 0.5);

const PRIMARY_TYPES = [Type.Rifle, Type.Smg, Type.Shotgun, Type.Sniper, Type.Lmg, Type.Rpg, Type.GrenadeLauncher];
const SECONDARY_TYPES = [Type.Pistol, Type.Shotgun, Type.Smg, Type.Rpg, Type.GrenadeLauncher];
const indexesOf = (types) => WEAPONS.map((w, i) => [w, i]).filter(([w]) => types.includes(w.model)).map(([, i]) => i);

export class Menu {
  constructor(api) {
    this.api = api; // { solo(settings), host(), join(code), sendSettings(s), start(), resume(), leave(), toLobby(), switchTeam(), spectate(on), onSettings() }
    this.screen = 'main';
    this.back = 'main';
    this.lobbyState = null;
    this.soloSettings = { mode: Mode.TeamDeathmatch, map: 0, bots: 5, radar: 2, sniperOnly: false, classLoadout: true };
    this.classIdx = S.selectedClass;
    root().addEventListener('click', (e) => {
      const el = e.target.closest('[data-act]');
      if (!el) return;
      click();
      this.#act(el.dataset.act, el.dataset);
    });
    root().addEventListener('input', (e) => this.#input(e.target));
    root().addEventListener('change', (e) => this.#input(e.target));
  }

  open(screen) { this.screen = screen; root().classList.remove('hidden'); this.render(); }
  close() { root().classList.add('hidden'); }
  get isOpen() { return !root().classList.contains('hidden'); }

  setLobby(state, isHost, code) {
    this.lobbyState = state;
    this.isHost = isHost;
    this.code = code;
    if (this.screen === 'lobby' && this.isOpen) this.render();
  }

  render() {
    const fn = this['screen_' + this.screen];
    root().innerHTML = `<div class="panel ${this.screen}">${fn.call(this)}</div>`;
    const nameInput = root().querySelector('#name-input');
    if (nameInput) nameInput.value = S.name;
  }

  // ------------------------------------------------------------------
  // Screens
  // ------------------------------------------------------------------

  screen_main() {
    const lvl = levelFor(S.experience);
    const next = xpForLevel(lvl + 1), cur = xpForLevel(lvl);
    return `
      <div class="title">SUNDOWN <span>ARENA</span></div>
      <div class="career">LEVEL ${lvl} <div class="bar"><i style="width:${((S.experience - cur) / (next - cur)) * 100}%"></i></div></div>
      <label class="field">NAME <input id="name-input" data-key="name" maxlength="16"></label>
      <div class="cols">
        <div class="col">
          <h3>SOLO</h3>
          ${this.#settingsRows(this.soloSettings, true)}
          <button class="big" data-act="solo">PLAY OFFLINE</button>
        </div>
        <div class="col">
          <h3>ONLINE</h3>
          <button class="big" data-act="host">HOST A MATCH</button>
          <label class="field">ROOM CODE <input id="room-input" maxlength="4" placeholder="ABCD" style="text-transform:uppercase"></label>
          <button data-act="join">JOIN</button>
          <div class="rooms" id="room-list"></div>
          <button class="small" data-act="refresh">REFRESH ROOMS</button>
        </div>
      </div>
      <div class="row">
        <button data-act="goto" data-to="classes">CLASSES</button>
        <button data-act="goto" data-to="settings">SETTINGS</button>
        <button data-act="goto" data-to="controls">CONTROLS</button>
      </div>
      <div class="hint" id="menu-error"></div>`;
  }

  screen_lobby() {
    const st = this.lobbyState;
    if (!st) return '<div class="title small">CONNECTING…</div>';
    const s = st.settings;
    const players = st.players.filter((p) => !p.bot);
    return `
      <div class="title small">LOBBY <span>${esc(this.code || '')}</span></div>
      <div class="hint">Share the room code with your friends. ${this.isHost ? 'You are the host.' : 'The host sets up the match.'}</div>
      <div class="cols">
        <div class="col">
          <h3>MATCH</h3>
          ${this.isHost ? this.#settingsRows(s, false) : `
            <div class="kv"><b>MODE</b> ${MODE_NAMES[s.mode]}</div>
            <div class="kv"><b>MAP</b> ${MAP_NAMES[s.map]}</div>
            <div class="kv"><b>BOTS</b> ${s.mode === Mode.ZombieSurvival ? 'NONE' : s.bots}</div>
            <div class="kv"><b>RADAR</b> ${RADAR_NAMES[s.radar]}</div>
            <div class="kv"><b>WEAPONS</b> ${s.classLoadout ? 'CLASS LOADOUT' : 'FULL ARSENAL'}${s.sniperOnly ? ' · SNIPERS ONLY' : ''}</div>`}
        </div>
        <div class="col">
          <h3>PLAYERS ${players.length}/${maxPlayersFor(s.mode)}</h3>
          <ul class="players">${players.map((p) => `<li><span class="lvl">${p.level}</span> ${esc(p.name)}${p.id === st.host ? ' <em>HOST</em>' : ''}</li>`).join('')}</ul>
        </div>
      </div>
      <div class="row">
        ${this.isHost ? '<button class="big" data-act="start">START MATCH</button>' : '<div class="hint">Waiting for the host…</div>'}
        <button data-act="goto" data-to="classes">CLASSES</button>
        <button data-act="leave">LEAVE</button>
      </div>`;
  }

  #settingsRows(s, solo) {
    const zombie = s.mode === Mode.ZombieSurvival;
    const k = solo ? 'solo' : 'host';
    return `
      <div class="opt"><b>MODE</b><button data-act="cycle" data-k="${k}" data-f="mode" data-d="-1">‹</button><span>${MODE_NAMES[s.mode]}</span><button data-act="cycle" data-k="${k}" data-f="mode" data-d="1">›</button></div>
      <div class="opt"><b>MAP</b><button data-act="cycle" data-k="${k}" data-f="map" data-d="-1" ${zombie ? 'disabled' : ''}>‹</button><span>${MAP_NAMES[zombie ? DARK_MAP_INDEX : s.map]}</span><button data-act="cycle" data-k="${k}" data-f="map" data-d="1" ${zombie ? 'disabled' : ''}>›</button></div>
      <div class="opt"><b>BOTS</b><button data-act="cycle" data-k="${k}" data-f="bots" data-d="-1" ${zombie ? 'disabled' : ''}>‹</button><span>${zombie ? 'NONE' : s.bots}</span><button data-act="cycle" data-k="${k}" data-f="bots" data-d="1" ${zombie ? 'disabled' : ''}>›</button></div>
      <div class="opt"><b>RADAR</b><button data-act="cycle" data-k="${k}" data-f="radar" data-d="-1">‹</button><span>${RADAR_NAMES[s.radar]}</span><button data-act="cycle" data-k="${k}" data-f="radar" data-d="1">›</button></div>
      <div class="opt"><b>WEAPONS</b><button data-act="toggle" data-k="${k}" data-f="classLoadout">${s.classLoadout ? 'CLASS LOADOUT' : 'FULL ARSENAL'}</button></div>
      <div class="opt"><b>SNIPERS ONLY</b><button data-act="toggle" data-k="${k}" data-f="sniperOnly">${s.sniperOnly ? 'ON' : 'OFF'}</button></div>`;
  }

  screen_classes() {
    const c = this.classIdx;
    const opt = (list, sel) => list.map((i) => `<option value="${i}" ${i === sel ? 'selected' : ''}>${WEAPONS[i].name}</option>`).join('');
    const addons = (w) => {
      const allowed = allowedMask(WEAPONS[w].model);
      const mask = getAtt(c, w);
      if (!allowed) return '<div class="hint">No add-ons for this weapon.</div>';
      return ATT_NAMES.map((n, t) => (allowed & attBit(t))
        ? `<button class="chip ${mask & attBit(t) ? 'on' : ''}" data-act="att" data-w="${w}" data-t="${t}">${n}</button>` : '').join('')
        + (WEAPONS[w].integralSuppressor ? '<div class="hint">Built-in suppressor.</div>' : '');
    };
    const stats = (w) => {
      const d = WEAPONS[w];
      const bar = (v, max) => `<div class="bar"><i style="width:${Math.min(100, (v / max) * 100)}%"></i></div>`;
      return `<div class="stats"><span>DAMAGE</span>${bar(d.damage * d.pellets, 120)}<span>FIRE RATE</span>${bar(d.fireRate, 17)}<span>RANGE</span>${bar(d.range, 600)}<span>MOBILITY</span>${bar(d.mobility, 1)}<span>MAG</span><b>${d.magazineSize}</b></div>`;
    };
    return `
      <div class="title small">CLASSES</div>
      <div class="row tabs">${CLASS_NAMES.map((n, i) => `<button class="${i === c ? 'on' : ''}" data-act="class" data-i="${i}">${n}${i === S.selectedClass ? ' ✓' : ''}</button>`).join('')}</div>
      <div class="cols">
        <div class="col">
          <h3>PRIMARY</h3>
          <select data-key="primary">${opt(indexesOf(PRIMARY_TYPES), S.classPrimary[c])}</select>
          ${stats(S.classPrimary[c])}
          <div class="chips">${addons(S.classPrimary[c])}</div>
        </div>
        <div class="col">
          <h3>SECONDARY</h3>
          <select data-key="secondary">${opt(indexesOf(SECONDARY_TYPES), S.classSecondary[c])}</select>
          ${stats(S.classSecondary[c])}
          <div class="chips">${addons(S.classSecondary[c])}</div>
        </div>
      </div>
      <div class="cols">
        <div class="col"><h3>LETHAL (G / RB)</h3><div class="chips">${THROWABLES.map((n, i) => `<button class="chip ${S.lethal === i ? 'on' : ''}" data-act="lethal" data-i="${i}">${n}</button>`).join('')}</div></div>
        <div class="col"><h3>TACTICAL (LB)</h3><div class="chips">${THROWABLES.map((n, i) => `<button class="chip ${S.tactical === i ? 'on' : ''}" data-act="tactical" data-i="${i}">${n}</button>`).join('')}</div></div>
      </div>
      <div class="hint">The class loadout applies from your next spawn. In full-arsenal matches the selected class's add-ons apply to every weapon.</div>
      <div class="row"><button class="big" data-act="useclass">EQUIP ${CLASS_NAMES[c]}</button><button data-act="back">BACK</button></div>`;
  }

  screen_settings() {
    return `
      <div class="title small">SETTINGS</div>
      <label class="field">MOUSE SENSITIVITY <input type="range" min="0.2" max="6" step="0.05" data-key="sensitivity" value="${S.sensitivity}"><output>${S.sensitivity.toFixed(2)}</output></label>
      <label class="field">FIELD OF VIEW <input type="range" min="60" max="110" step="1" data-key="fov" value="${S.fov}"><output>${S.fov}</output></label>
      <label class="field">VOLUME <input type="range" min="0" max="1" step="0.01" data-key="volume" value="${S.volume}"><output>${Math.round(S.volume * 100)}</output></label>
      <label class="field">QUALITY <select data-key="quality">${['low', 'medium', 'high'].map((q) => `<option ${S.quality === q ? 'selected' : ''}>${q}</option>`).join('')}</select></label>
      <label class="field check"><input type="checkbox" data-key="invertY" ${S.invertY ? 'checked' : ''}> INVERT LOOK</label>
      <label class="field check"><input type="checkbox" data-key="showFps" ${S.showFps ? 'checked' : ''}> SHOW FPS</label>
      <div class="hint">Quality changes apply on the next map load.</div>
      <div class="row"><button data-act="back">BACK</button></div>`;
  }

  screen_controls() {
    const rows = [
      ['WASD / Shift / Space (hold)', 'Move / sprint / jump & bunny hop'],
      ['Left Ctrl (or C)', 'Crouch — while sprinting: slide'],
      ['Left / Right mouse', 'Shoot / aim (sniper scopes)'],
      ['1–3 (class) / 1–9 + wheel (all weapons)', 'Weapons · Q last weapon · B knife'],
      ['R', 'Reload (shotguns: shell by shell, fire to interrupt)'],
      ['G / F / V / X', 'Grenade / flashbang / sticky bomb / throwing knife'],
      ['Z', 'Toggle prone / crawl'],
      ['Tab (hold)', 'Scoreboard'],
      ['Escape', 'Pause (resume · switch team · spectate · settings · leave)'],
      ['Xbox pad', 'Sticks · RT/LT fire/aim · A jump · B crouch/slide · X reload · Y next · RB/LB throw'],
    ];
    return `<div class="title small">CONTROLS</div><table class="controls">${rows.map(([a, b]) => `<tr><td>${a}</td><td>${b}</td></tr>`).join('')}</table>
      <div class="row"><button data-act="back">BACK</button></div>`;
  }

  screen_pause() {
    const st = this.lobbyState;
    const teamMode = st && (st.settings.mode === Mode.Duel || st.settings.mode === Mode.TeamDeathmatch);
    const me = st && st.players.find((p) => p.id === this.api.myId());
    return `
      <div class="title small">PAUSED</div>
      <div class="col narrow">
        <button class="big" data-act="resume">RESUME</button>
        ${teamMode && me && !me.spec ? '<button data-act="team">SWITCH TEAM</button>' : ''}
        <button data-act="spectate">${me && me.spec ? 'JOIN THE MATCH' : 'SPECTATE'}</button>
        <button data-act="goto" data-to="classes">CLASSES</button>
        <button data-act="goto" data-to="settings">SETTINGS</button>
        <button data-act="goto" data-to="controls">CONTROLS</button>
        ${this.isHost ? '<button data-act="tolobby">BACK TO LOBBY</button>' : ''}
        <button data-act="leave">LEAVE MATCH</button>
      </div>
      ${this.code && this.code !== 'SOLO' ? `<div class="hint">ROOM ${esc(this.code)}</div>` : ''}`;
  }

  // ------------------------------------------------------------------
  // Actions
  // ------------------------------------------------------------------

  #act(act, d) {
    switch (act) {
      case 'goto': this.back = this.screen; this.screen = d.to; this.render(); break;
      case 'back': this.screen = this.back === this.screen ? 'main' : this.back; this.back = this.api.inGame() ? 'pause' : 'main'; if (this.screen === 'lobby' && !this.api.inGame()) this.screen = 'main'; this.render(); break;
      case 'solo': save(); this.api.solo({ ...this.soloSettings }); break;
      case 'host': save(); this.api.host(); break;
      case 'join': {
        const code = (root().querySelector('#room-input').value || '').trim().toUpperCase();
        if (code.length !== 4) return this.error('Enter a 4-letter room code.');
        save(); this.api.join(code); break;
      }
      case 'joincode': save(); this.api.join(d.code); break;
      case 'refresh': this.refreshRooms(); break;
      case 'cycle': this.#cycle(d.k, d.f, +d.d); break;
      case 'toggle': this.#toggle(d.k, d.f); break;
      case 'start': this.api.start(); break;
      case 'leave': this.api.leave(); break;
      case 'resume': this.api.resume(); break;
      case 'team': this.api.switchTeam(); this.api.resume(); break;
      case 'spectate': { const me = this.lobbyState?.players.find((p) => p.id === this.api.myId()); this.api.spectate(!(me && me.spec)); this.api.resume(); break; }
      case 'tolobby': this.api.toLobby(); break;
      case 'class': this.classIdx = +d.i; this.render(); break;
      case 'useclass': S.selectedClass = this.classIdx; save(); this.api.onSettings(); this.render(); break;
      case 'att': {
        const w = +d.w, t = +d.t;
        setAtt(this.classIdx, w, getAtt(this.classIdx, w) ^ attBit(t));
        this.api.onSettings();
        this.render(); break;
      }
      case 'lethal': S.lethal = +d.i; save(); this.render(); break;
      case 'tactical': S.tactical = +d.i; save(); this.render(); break;
    }
  }

  #cycle(k, f, dir) {
    const s = k === 'solo' ? this.soloSettings : { ...this.lobbyState.settings };
    const n = { mode: 5, map: MAP_NAMES.length, bots: MAX_BOTS + 1, radar: 3 }[f];
    s[f] = (s[f] + dir + n) % n;
    if (f === 'mode' && s.mode === Mode.ZombieSurvival) s.map = DARK_MAP_INDEX;
    if (k === 'solo') this.render();
    else this.api.sendSettings({ [f]: s[f] });
  }

  #toggle(k, f) {
    if (k === 'solo') { this.soloSettings[f] = !this.soloSettings[f]; this.render(); }
    else this.api.sendSettings({ [f]: !this.lobbyState.settings[f] });
  }

  #input(el) {
    const key = el.dataset.key;
    if (!key) return;
    const c = this.classIdx;
    if (key === 'name') { S.name = el.value.slice(0, 16); save(); this.api.onName?.(); return; }
    if (key === 'primary') { S.classPrimary[c] = +el.value; save(); this.api.onSettings(); this.render(); return; }
    if (key === 'secondary') { S.classSecondary[c] = +el.value; save(); this.api.onSettings(); this.render(); return; }
    if (el.type === 'checkbox') S[key] = el.checked;
    else if (el.type === 'range') S[key] = +el.value;
    else S[key] = el.value;
    const out = el.parentElement.querySelector('output');
    if (out) out.textContent = key === 'volume' ? Math.round(S.volume * 100) : key === 'sensitivity' ? S.sensitivity.toFixed(2) : el.value;
    if (key === 'volume') setVolume(S.volume);
    save();
    this.api.onSettings();
  }

  error(msg) {
    const el = root().querySelector('#menu-error');
    if (el) el.textContent = msg;
  }

  async refreshRooms() {
    const list = root().querySelector('#room-list');
    if (!list) return;
    try {
      const rooms = await (await fetch('/api/rooms', { cache: 'no-store' })).json();
      list.innerHTML = rooms.length
        ? rooms.map((r) => `<button class="room" data-act="joincode" data-code="${esc(r.code)}">${esc(r.code)} · ${MODE_NAMES[r.mode]} · ${MAP_NAMES[r.map]} · ${r.players}P${r.lobby ? ' · LOBBY' : ''}</button>`).join('')
        : '<div class="hint">No open rooms.</div>';
    } catch {
      list.innerHTML = '<div class="hint">Online play needs the Node server (npm start).</div>';
    }
  }
}

export { CLASS_COUNT };
