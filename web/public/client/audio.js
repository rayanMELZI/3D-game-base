// Port of Audio/SfxSynth.cs: every sound is synthesized (layered noise,
// sine sweeps, saturation), generated once into AudioBuffers and cached.
// Positional sounds use a linear roll-off like the Unity sources.

import { Type } from '../shared/weapons.js';
import { S } from './settings.js';

const SR = 44100;
const TAU = Math.PI * 2;
let ctx = null, master = null;
const cache = new Map();
let listener = { p: [0, 0, 0], yaw: 0 };

export function initAudio() {
  if (ctx) { if (ctx.state === 'suspended') ctx.resume(); return; }
  ctx = new (window.AudioContext || window.webkitAudioContext)();
  master = ctx.createGain();
  master.gain.value = S.volume;
  master.connect(ctx.destination);
}
export function setVolume(v) { if (master) master.gain.value = v; }
export function setListener(p, yaw) { listener = { p, yaw }; }

const noise = () => Math.random() * 2 - 1;

function addFilteredNoise(b, start, duration, amp, decay, lowpass) {
  const from = (start * SR) | 0, to = Math.min(b.length, from + ((duration * SR) | 0));
  let f = 0;
  for (let i = from; i < to; i++) {
    const t = (i - from) / SR;
    f += lowpass * (noise() * amp * Math.exp(-t * decay) - f);
    b[i] += f;
  }
}
function addSine(b, start, duration, f0, f1, amp, decay) {
  const from = (start * SR) | 0, to = Math.min(b.length, from + ((duration * SR) | 0));
  let phase = 0;
  for (let i = from; i < to; i++) {
    const t = (i - from) / SR;
    phase += (TAU * (f0 + (f1 - f0) * (duration > 0 ? t / duration : 0))) / SR;
    b[i] += Math.sin(phase) * amp * Math.exp(-t * decay);
  }
}
function saturate(b, drive) {
  for (let i = 0; i < b.length; i++) b[i] = (Math.atan(b[i] * drive) / (Math.PI / 2)) * 0.85;
}

function buffered(key, duration, fill) {
  if (cache.has(key)) return cache.get(key);
  if (!ctx) return null;
  const n = (duration * SR) | 0;
  const data = new Float32Array(n);
  fill(data);
  const fade = Math.min(n, SR / 100);
  for (let i = 0; i < fade; i++) data[n - 1 - i] *= i / fade;
  const buf = ctx.createBuffer(1, n, SR);
  buf.copyToChannel(data, 0);
  cache.set(key, buf);
  return buf;
}
const make = (key, duration, wave) => buffered(key, duration, (b) => {
  for (let i = 0; i < b.length; i++) b[i] = Math.max(-1, Math.min(1, wave(i / SR)));
});
const note = (t, start, f) => (t < start ? 0 : Math.sin(TAU * f * (t - start)) * Math.exp(-(t - start) * 5) * 0.3);

// ---------------------------------------------------------------------------
// Clips
// ---------------------------------------------------------------------------

export const clips = {
  shot(model) {
    switch (model) {
      case Type.Knife: return buffered('shot_knife', 0.14, (b) => { addFilteredNoise(b, 0, 0.12, 0.5, 26, 0.35); saturate(b, 1.2); });
      case Type.Pistol: return buffered('shot_pistol', 0.3, (b) => {
        addFilteredNoise(b, 0, 0.015, 1.1, 90, 0.9); addFilteredNoise(b, 0, 0.1, 0.9, 40, 0.3);
        addSine(b, 0, 0.15, 160, 90, 0.7, 22); addFilteredNoise(b, 0.02, 0.25, 0.3, 9, 0.08); saturate(b, 1.9);
      });
      case Type.Smg: return buffered('shot_smg', 0.22, (b) => {
        addFilteredNoise(b, 0, 0.012, 1.1, 110, 0.85); addFilteredNoise(b, 0, 0.08, 0.85, 45, 0.35);
        addSine(b, 0, 0.12, 180, 100, 0.6, 26); addFilteredNoise(b, 0.015, 0.18, 0.25, 11, 0.09); saturate(b, 1.9);
      });
      case Type.Shotgun: return buffered('shot_shotgun', 0.55, (b) => {
        addFilteredNoise(b, 0, 0.03, 1.2, 60, 0.7); addFilteredNoise(b, 0, 0.25, 1.1, 16, 0.18);
        addSine(b, 0, 0.3, 95, 50, 1, 11); addSine(b, 0.01, 0.35, 60, 40, 0.7, 8);
        addFilteredNoise(b, 0.05, 0.5, 0.35, 6, 0.06); saturate(b, 2.2);
      });
      case Type.Sniper: return buffered('shot_sniper', 0.8, (b) => {
        addFilteredNoise(b, 0, 0.02, 1.3, 80, 0.95); addFilteredNoise(b, 0, 0.22, 1, 14, 0.22);
        addSine(b, 0, 0.4, 85, 42, 1, 9); addFilteredNoise(b, 0.06, 0.7, 0.4, 4.5, 0.05); saturate(b, 2.1);
      });
      case Type.GrenadeLauncher: return buffered('shot_gl', 0.35, (b) => {
        addSine(b, 0, 0.18, 110, 55, 1, 14); addFilteredNoise(b, 0, 0.06, 0.8, 30, 0.25);
        addFilteredNoise(b, 0.02, 0.3, 0.25, 6, 0.3); saturate(b, 1.5);
      });
      case Type.Rpg: return buffered('shot_rpg', 0.6, (b) => {
        addFilteredNoise(b, 0, 0.45, 1, 8, 0.2); addSine(b, 0, 0.3, 70, 45, 0.8, 10);
        addFilteredNoise(b, 0.1, 0.45, 0.4, 5, 0.5); saturate(b, 1.7);
      });
      default: return buffered('shot_rifle', 0.4, (b) => {
        addFilteredNoise(b, 0, 0.015, 1.2, 100, 0.9); addFilteredNoise(b, 0, 0.12, 1, 32, 0.28);
        addSine(b, 0, 0.2, 140, 75, 0.8, 16); addFilteredNoise(b, 0.025, 0.35, 0.32, 7, 0.07); saturate(b, 2);
      });
    }
  },
  explosion: () => buffered('explosion', 1.1, (b) => {
    addFilteredNoise(b, 0, 0.05, 1.3, 40, 0.5); addFilteredNoise(b, 0, 0.7, 1.2, 5, 0.1);
    addSine(b, 0, 0.8, 65, 28, 1.2, 5); addSine(b, 0.02, 0.6, 42, 25, 0.8, 5);
    addFilteredNoise(b, 0.15, 0.9, 0.35, 3, 0.05); saturate(b, 2.4);
  }),
  hit: () => make('hit', 0.12, (t) => Math.sin(TAU * 1300 * t) * Math.exp(-t * 24) * 0.45),
  headshot: () => make('headshot', 0.28, (t) => (Math.sin(TAU * 1568 * t) + 0.6 * Math.sin(TAU * 2093 * t)) * Math.exp(-t * 13) * 0.42),
  reload: () => make('reload', 0.22, (t) => noise() * 0.55 * (Math.exp(-t * 300) + (t > 0.12 ? Math.exp(-(t - 0.12) * 300) : 0))),
  shell: () => make('shell', 0.14, (t) => noise() * 0.5 * Math.exp(-t * 260) + Math.sin(TAU * 2400 * t) * 0.18 * Math.exp(-t * 70)),
  pump: () => make('pump', 0.3, (t) => noise() * 0.55 * (Math.exp(-t * 220) + (t > 0.16 ? Math.exp(-(t - 0.16) * 220) : 0))),
  death: () => make('death', 0.5, (t) => Math.sin(TAU * (110 - 120 * t) * t) * Math.exp(-t * 7) * 0.75 + noise() * Math.exp(-t * 22) * 0.4),
  growl: () => make('growl', 0.9, (t) => {
    const env = Math.sin(Math.min(1, t / 0.9) * Math.PI);
    const pitch = 72 + Math.sin(TAU * 5.5 * t) * 9;
    const voice = Math.sin(TAU * pitch * t) + 0.5 * Math.sin(TAU * pitch * 2.03 * t);
    const rasp = noise() * (0.5 + 0.5 * Math.sin(TAU * pitch * t));
    return (voice * 0.22 + rasp * 0.2) * env;
  }),
  breathing: () => make('breathing', 2.4, (t) => {
    const env = Math.sin(Math.min(1, (t % 1.2) / 0.75) * Math.PI);
    return noise() * env * 0.18 + Math.sin(TAU * 95 * t) * env * 0.035;
  }),
  streak: (n) => make('streak_' + n, 0.8, (t) => (Math.sin(TAU * (360 + n * 7) * t) + 0.55 * Math.sin(TAU * (540 + n * 9) * t)) * Math.exp(-t * 3.8) * 0.32),
  click: () => make('click', 0.06, (t) => Math.sin(TAU * 900 * t) * Math.exp(-t * 90) * 0.4),
  win: () => make('win', 1.2, (t) => note(t, 0, 523) + note(t, 0.22, 659) + note(t, 0.44, 784) + note(t, 0.66, 1047)),
  levelUp: () => make('levelup', 0.9, (t) => note(t, 0, 784) + note(t, 0.12, 1047) + note(t, 0.24, 1319)),
  footstep: () => make('step', 0.08, (t) => noise() * 0.3 * Math.exp(-t * 60)),
};

// ---------------------------------------------------------------------------
// Playback
// ---------------------------------------------------------------------------

function source(buf, volume, pitch, delay = 0) {
  const src = ctx.createBufferSource();
  src.buffer = buf;
  src.playbackRate.value = Math.max(0.1, pitch) * (1 + (Math.random() * 0.14 - 0.07));
  const gain = ctx.createGain();
  gain.gain.value = volume;
  src.connect(gain);
  return { src, gain, start: () => src.start(ctx.currentTime + delay) };
}

export function play2D(buf, volume = 1, pitch = 1, delay = 0) {
  if (!ctx || !buf) return null;
  const s = source(buf, volume, pitch, delay);
  s.gain.connect(master);
  s.start();
  return s;
}

/** Positional sound: linear roll-off between min and max distance, stereo pan from the listener's yaw. */
export function playAt(buf, p, volume = 1, pitch = 1, minD = 5, maxD = 90, delay = 0) {
  if (!ctx || !buf) return;
  const dx = p[0] - listener.p[0], dy = p[1] - listener.p[1], dz = p[2] - listener.p[2];
  const d = Math.hypot(dx, dy, dz);
  if (d >= maxD) return;
  const att = d <= minD ? 1 : 1 - (d - minD) / (maxD - minD);
  // Right vector of the listener: (cos yaw, 0, -sin yaw).
  const right = (dx * Math.cos(listener.yaw) - dz * Math.sin(listener.yaw)) / (d || 1);
  const s = source(buf, volume * att, pitch, delay);
  const pan = ctx.createStereoPanner();
  pan.pan.value = Math.max(-1, Math.min(1, right * 0.85));
  s.gain.connect(pan);
  pan.connect(master);
  s.start();
}
