// Two interchangeable transports with the same interface:
//   send(msg), onMessage(cb), close()
// Online: WebSocket to the Node server (one room = one Match).
// Offline: the same Match runs in the page at 30 Hz (solo vs bots, zombies).

import { Match } from '../shared/match.js';

export class OnlineTransport {
  constructor(url) {
    this.ws = new WebSocket(url);
    this.handlers = [];
    this.queue = [];
    this.ws.onmessage = (ev) => {
      let msg;
      try { msg = JSON.parse(ev.data); } catch { return; }
      for (const h of this.handlers) h(msg);
    };
    this.ws.onopen = () => { for (const q of this.queue) this.ws.send(q); this.queue = []; };
    this.ws.onclose = () => { for (const h of this.handlers) h({ t: 'closed' }); };
    this.ws.onerror = () => { for (const h of this.handlers) h({ t: 'error', msg: 'CONNECTION FAILED' }); };
  }
  send(msg) {
    const text = JSON.stringify(msg);
    if (this.ws.readyState === 1) this.ws.send(text);
    else if (this.ws.readyState === 0) this.queue.push(text);
  }
  onMessage(cb) { this.handlers.push(cb); }
  close() { this.ws.onclose = null; this.ws.close(); }
}

export class LocalTransport {
  constructor(name, level, settings) {
    this.handlers = [];
    this.id = 'p1';
    this.match = new Match((to, msg) => {
      if (to !== null && to !== this.id) return;
      if (msg.ex === this.id) return;
      // Structured clone keeps the match's own arrays out of the client's hands.
      const copy = JSON.parse(JSON.stringify(msg));
      for (const h of this.handlers) h(copy);
    }, settings);
    this.started = false;
    this.pending = [];
    queueMicrotask(() => {
      for (const h of this.handlers) h({ t: 'room', code: 'SOLO' });
      this.match.addPlayer(this.id, name, level);
      this.started = true;
    });
    this.last = performance.now();
    this.acc = 0;
    this.timer = setInterval(() => {
      const now = performance.now();
      this.acc += Math.min(0.25, (now - this.last) / 1000);
      this.last = now;
      while (this.acc >= 1 / 30) { this.match.tick(1 / 30); this.acc -= 1 / 30; }
    }, 1000 / 60);
  }
  send(msg) { this.match.handle(this.id, JSON.parse(JSON.stringify(msg))); }
  onMessage(cb) { this.handlers.push(cb); }
  close() { clearInterval(this.timer); }
}

export function serverUrl(room, name, level) {
  const proto = location.protocol === 'https:' ? 'wss:' : 'ws:';
  const params = new URLSearchParams({ name, level: String(level) });
  if (room) params.set('room', room);
  return `${proto}//${location.host}/ws?${params}`;
}
