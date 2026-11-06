// Port of UI/HudOverlay.cs + Network/NetworkGameHud.cs as DOM/canvas:
// crosshair, hit markers, ammo, health, radar, kill feed, scores,
// scoreboard, damage-direction indicators, flashbang, scope, banners, XP.

import { WEAPONS, GUN_GAME_ORDER, THROWABLES } from '../shared/weapons.js';
import { teamColor } from '../shared/maps.js';
import { MODE_NAMES, Mode, isTeamMode, scoreLimit, F } from '../shared/match.js';
import { levelFor, xpForLevel, S } from './settings.js';

const $ = (id) => document.getElementById(id);
const css = (c) => `rgb(${Math.round(c[0] * 255)},${Math.round(c[1] * 255)},${Math.round(c[2] * 255)})`;

export class Hud {
  constructor() {
    this.root = $('hud');
    this.feed = $('killfeed');
    this.radar = $('radar');
    this.rctx = this.radar.getContext('2d');
    this.dmg = $('damage');
    this.hitTimer = 0;
    this.hurtFlash = 0;
    this.flash = 0;
    this.indicators = [];
    this.xpTimer = 0;
  }

  show(on) { this.root.classList.toggle('hidden', !on); }

  addFeed(msg) {
    const div = document.createElement('div');
    div.textContent = msg;
    if (/HEADSHOT|NO-SCOPE|KILLSTREAK/.test(msg)) div.classList.add('accent');
    this.feed.prepend(div);
    while (this.feed.children.length > 6) this.feed.lastChild.remove();
    setTimeout(() => div.classList.add('fade'), 5500);
    setTimeout(() => div.remove(), 6500);
  }

  hitMarker(head, kill) {
    const hm = $('hitmarker');
    hm.className = kill ? 'kill' : head ? 'head' : '';
    hm.style.opacity = 1;
    this.hitTimer = kill ? 0.35 : 0.18;
  }

  /** Damage from a world position: an arc on the screen edge pointing at it. */
  hurt(from, me) {
    this.hurtFlash = 1;
    if (!from || !me) return;
    const el = document.createElement('div');
    el.className = 'dmg-arc';
    this.dmg.appendChild(el);
    this.indicators.push({ el, from, life: 2 });
  }

  flashbang(strength) { this.flash = Math.max(this.flash, Math.min(1, strength) * 3.5); }

  xp(amount, reason) {
    const el = $('xp');
    el.textContent = `+${amount}  ${reason}`;
    el.style.opacity = 1;
    this.xpTimer = 1.6;
  }

  banner(text, sub = '', time = 2.5) {
    $('banner-main').textContent = text;
    $('banner-sub').textContent = sub;
    $('banner').style.opacity = 1;
    clearTimeout(this.bannerTimer);
    if (time > 0) this.bannerTimer = setTimeout(() => ($('banner').style.opacity = 0), time * 1000);
  }
  hideBanner() { $('banner').style.opacity = 0; }

  center(text) { $('center-msg').textContent = text || ''; }

  update(dt, g) {
    const { player, weapons, state, me, myId, alive } = g;
    // ---- Crosshair & scope ----
    const def = weapons.def;
    const showCross = alive && !weapons.aiming && !g.deathcam;
    const cross = $('crosshair');
    cross.style.display = showCross ? 'block' : 'none';
    const spread = 6 + Math.min(20, player.speed * 2) + weapons.kick * 14 + (def.pellets > 1 ? 10 : 0);
    cross.style.setProperty('--gap', spread + 'px');
    $('scope').style.display = alive && weapons.scoped ? 'block' : 'none';
    this.hitTimer -= dt;
    if (this.hitTimer <= 0) $('hitmarker').style.opacity = 0;

    // ---- Ammo / weapon / throwables ----
    $('weapon-name').textContent = def.name;
    $('ammo').textContent = def.isMelee ? '∞' : `${weapons.mag}`;
    $('ammo-max').textContent = def.isMelee ? '' : `/ ${weapons.magMax}`;
    $('ammo').classList.toggle('low', !def.isMelee && weapons.mag <= Math.ceil(weapons.magMax * 0.25));
    $('reload-hint').textContent = weapons.reloading ? 'RELOADING' : !def.isMelee && weapons.mag === 0 ? 'PRESS R' : '';
    const thr = $('throwables');
    thr.innerHTML = THROWABLES.map((n, i) => {
      const cd = weapons.throwCooldown(i);
      return `<span class="${cd > 0 ? 'cd' : ''}">${'GFVX'[i]} ${n}${cd > 0 ? ' ' + cd.toFixed(1) : ''}</span>`;
    }).join('');

    // ---- Health ----
    const hp = me ? me.hp : 0;
    $('hp-fill').style.width = Math.max(0, hp) + '%';
    $('hp-fill').classList.toggle('low', hp < 35);
    $('hp-text').textContent = Math.round(hp);
    this.hurtFlash = Math.max(0, this.hurtFlash - dt * 2.5);
    const low = alive && hp < 40 ? (40 - hp) / 40 : 0;
    $('vignette').style.opacity = Math.min(1, low * 0.8 + this.hurtFlash * 0.5);
    $('protect').style.display = me && me.flags & F.Protected && alive ? 'block' : 'none';

    // ---- Damage arcs ----
    for (let k = this.indicators.length - 1; k >= 0; k--) {
      const ind = this.indicators[k];
      ind.life -= dt;
      if (ind.life <= 0) { ind.el.remove(); this.indicators.splice(k, 1); continue; }
      const dx = ind.from[0] - player.p[0], dz = ind.from[2] - player.p[2];
      const world = Math.atan2(-dx, -dz);
      const rel = world - player.yaw; // 0 = in front
      ind.el.style.transform = `translate(-50%,-50%) rotate(${-rel}rad) translateY(-120px)`;
      ind.el.style.opacity = Math.min(1, ind.life);
    }

    // ---- Flash ----
    this.flash = Math.max(0, this.flash - dt);
    $('flash').style.opacity = Math.min(1, this.flash);

    // ---- XP popup ----
    this.xpTimer -= dt;
    if (this.xpTimer <= 0) $('xp').style.opacity = 0;

    // ---- Scores ----
    if (state) this.#scores(state, myId);
    this.#radar(g);

    // ---- Scoreboard ----
    const showBoard = g.scoreboard || (state && !state.active && !state.lobby);
    $('scoreboard').style.display = showBoard ? 'block' : 'none';
    if (showBoard && state) this.#scoreboard(state, myId);
  }

  #scores(st, myId) {
    const mode = st.settings.mode;
    let html = '';
    if (isTeamMode(mode)) {
      html = `<span style="color:${css(teamColor(0))}">${st.scores[0]}</span> <small>${MODE_NAMES[mode]} · ${scoreLimit(mode)}</small> <span style="color:${css(teamColor(1))}">${st.scores[1]}</span>`;
    } else if (mode === Mode.ZombieSurvival) {
      html = st.waveIn > 0 ? `<small>WAVE ${st.wave + 1} IN</small> ${Math.ceil(st.waveIn)}` : `<small>WAVE</small> ${st.wave} <small>· ${st.zombiesLeft} LEFT</small>`;
    } else {
      const players = st.players.filter((p) => !p.spec);
      const me = players.find((p) => p.id === myId);
      const key = mode === Mode.GunGame ? 'gg' : 'kills';
      const lead = Math.max(0, ...players.map((p) => p[key]));
      const mine = me ? me[key] : 0;
      html = mode === Mode.GunGame
        ? `<small>GUN GAME</small> ${mine + 1}/${GUN_GAME_ORDER.length} <small>· ${WEAPONS[GUN_GAME_ORDER[Math.min(mine, GUN_GAME_ORDER.length - 1)]].name} · LEAD ${lead + 1}</small>`
        : `<small>${MODE_NAMES[mode]}</small> ${mine} <small>· LEAD ${lead} / ${scoreLimit(mode)}</small>`;
    }
    $('scores').innerHTML = html;
  }

  #scoreboard(st, myId) {
    const mode = st.settings.mode;
    const rows = (list) => list.map((p) => `<tr class="${p.id === myId ? 'me' : ''}${p.alive ? '' : ' dead'}">
      <td><span class="lvl">${p.bot ? 'BOT' : p.level}</span> ${esc(p.name)}</td>
      <td>${mode === Mode.ZombieSurvival ? p.score : mode === Mode.GunGame ? p.gg + 1 : p.kills}</td>
      <td>${p.kills}</td><td>${p.deaths}</td></tr>`).join('');
    const head = `<tr><th>PLAYER</th><th>${mode === Mode.ZombieSurvival ? 'POINTS' : mode === Mode.GunGame ? 'LEVEL' : 'SCORE'}</th><th>K</th><th>D</th></tr>`;
    const playing = st.players.filter((p) => !p.spec);
    const sort = (a, b) => (b.score - a.score) || (b.gg - a.gg) || (b.kills - a.kills) || (a.deaths - b.deaths);
    let body;
    if (isTeamMode(mode)) {
      body = [0, 1].map((t) => `<div class="team" style="--tc:${css(teamColor(t))}"><h3>${t === 0 ? 'BLUE' : 'ORANGE'} · ${st.scores[t]}</h3><table>${head}${rows(playing.filter((p) => p.team === t).sort((a, b) => b.kills - a.kills))}</table></div>`).join('');
    } else body = `<div class="team"><table>${head}${rows(playing.sort(sort))}</table></div>`;
    const spec = st.players.filter((p) => p.spec);
    const end = !st.active && !st.lobby ? `<div class="end">${st.winnerName ? esc(st.winnerName) + (mode === Mode.ZombieSurvival ? '' : ' WINS') : 'MATCH OVER'} · NEXT MATCH IN ${Math.ceil(st.restartIn)}</div>` : '';
    const lvl = levelFor(S.experience);
    $('scoreboard').innerHTML = `${end}<div class="sb-title">${MODE_NAMES[mode]}</div><div class="teams">${body}</div>
      ${spec.length ? `<div class="spec">SPECTATING: ${spec.map((p) => esc(p.name)).join(', ')}</div>` : ''}
      <div class="career">LEVEL ${lvl} · ${S.experience - xpForLevel(lvl)} / ${xpForLevel(lvl + 1) - xpForLevel(lvl)} XP</div>`;
  }

  #radar(g) {
    const ctx = this.rctx, W = this.radar.width, R = W / 2;
    ctx.clearRect(0, 0, W, W);
    const st = g.state;
    if (!st || st.settings.radar === 0 || !g.alive) { this.radar.style.display = 'none'; return; }
    this.radar.style.display = 'block';
    ctx.fillStyle = 'rgba(10,12,16,0.55)';
    ctx.beginPath(); ctx.arc(R, R, R - 1, 0, Math.PI * 2); ctx.fill();
    ctx.strokeStyle = 'rgba(255,160,70,0.5)'; ctx.lineWidth = 1.5; ctx.stroke();
    ctx.strokeStyle = 'rgba(255,255,255,0.08)';
    ctx.beginPath(); ctx.arc(R, R, R * 0.5, 0, Math.PI * 2); ctx.stroke();
    const range = 40, scale = (R - 6) / range;
    const yaw = g.player.yaw;
    const plot = (p, color, size) => {
      const dx = p[0] - g.player.p[0], dz = p[2] - g.player.p[2];
      // Rotate so the player's forward is up.
      const rx = dx * Math.cos(yaw) - dz * Math.sin(yaw);
      const rz = dx * Math.sin(yaw) + dz * Math.cos(yaw);
      let x = rx * scale, y = rz * scale;
      const l = Math.hypot(x, y);
      if (l > R - 6) { x *= (R - 6) / l; y *= (R - 6) / l; }
      ctx.fillStyle = color;
      ctx.beginPath(); ctx.arc(R + x, R + y, size, 0, Math.PI * 2); ctx.fill();
    };
    for (const e of g.remotes.values()) {
      if (!(e.flags & F.Alive)) continue;
      const ally = g.isAlly(e);
      const visible = ally || st.settings.radar === 1 || (e.flags & F.Firing);
      if (!visible) continue;
      plot(e.p, ally ? '#5fa8ff' : '#ff4a3a', 3.5);
    }
    for (const z of g.zombieViews.values()) if (z.alive) plot(z.p, '#ff4a3a', 2.5);
    // Me: arrow.
    ctx.fillStyle = '#ffd27a';
    ctx.beginPath(); ctx.moveTo(R, R - 7); ctx.lineTo(R - 5, R + 5); ctx.lineTo(R + 5, R + 5); ctx.closePath(); ctx.fill();
  }
}

export function esc(s) {
  return String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}
