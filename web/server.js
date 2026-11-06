// SUNDOWN ARENA web server: static files + WebSocket rooms, zero dependencies.
// Each room runs the shared authoritative Match at 30 Hz.
//   PORT=8080 node server.js

import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { Match } from './public/shared/match.js';

const ROOT = path.join(path.dirname(fileURLToPath(import.meta.url)), 'public');
const PORT = Number(process.env.PORT) || 8080;
const TICK = 1 / 30;
const MAX_ROOMS = 64;

const TYPES = {
  '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8',
  '.json': 'application/json', '.png': 'image/png', '.svg': 'image/svg+xml', '.ico': 'image/x-icon', '.txt': 'text/plain',
};

// ---------------------------------------------------------------------------
// Static files
// ---------------------------------------------------------------------------

const server = http.createServer((req, res) => {
  const url = new URL(req.url, 'http://x');
  if (url.pathname === '/api/rooms') {
    const list = [...rooms.values()].map((r) => ({ code: r.code, players: r.match.humanCount, mode: r.match.mode, map: r.match.settings.map, lobby: r.match.lobby }));
    res.writeHead(200, { 'content-type': 'application/json', 'cache-control': 'no-store' });
    return res.end(JSON.stringify(list));
  }
  let file = path.normalize(decodeURIComponent(url.pathname)).replace(/^([/\\])+/, '');
  if (!file || file.endsWith('/')) file += 'index.html';
  const full = path.join(ROOT, file);
  if (!full.startsWith(ROOT)) { res.writeHead(403); return res.end(); }
  fs.stat(full, (err, st) => {
    if (err || !st.isFile()) { res.writeHead(404); return res.end('Not found'); }
    res.writeHead(200, { 'content-type': TYPES[path.extname(full)] || 'application/octet-stream', 'content-length': st.size });
    fs.createReadStream(full).pipe(res);
  });
});

// ---------------------------------------------------------------------------
// Minimal WebSocket (RFC 6455): text frames, ping/pong, close
// ---------------------------------------------------------------------------

class Socket {
  constructor(sock) {
    this.sock = sock;
    this.buf = Buffer.alloc(0);
    this.parts = [];
    this.open = true;
    this.onmessage = null;
    this.onclose = null;
    sock.setNoDelay(true);
    sock.on('data', (d) => this.#data(d));
    sock.on('close', () => this.#closed());
    sock.on('error', () => this.#closed());
  }

  #closed() {
    if (!this.open) return;
    this.open = false;
    this.onclose?.();
  }

  #data(chunk) {
    this.buf = Buffer.concat([this.buf, chunk]);
    if (this.buf.length > 1 << 20) return this.close();
    for (;;) {
      if (this.buf.length < 2) return;
      const b0 = this.buf[0], b1 = this.buf[1];
      const fin = (b0 & 0x80) !== 0, op = b0 & 0x0f, masked = (b1 & 0x80) !== 0;
      let len = b1 & 0x7f, off = 2;
      if (len === 126) { if (this.buf.length < 4) return; len = this.buf.readUInt16BE(2); off = 4; }
      else if (len === 127) { if (this.buf.length < 10) return; len = Number(this.buf.readBigUInt64BE(2)); off = 10; }
      if (len > 1 << 19) return this.close();
      const need = off + (masked ? 4 : 0) + len;
      if (this.buf.length < need) return;
      let payload = this.buf.subarray(off + (masked ? 4 : 0), need);
      if (masked) {
        const mask = this.buf.subarray(off, off + 4);
        payload = Buffer.from(payload);
        for (let i = 0; i < payload.length; i++) payload[i] ^= mask[i & 3];
      }
      this.buf = this.buf.subarray(need);
      if (op === 0x8) return this.close();
      if (op === 0x9) { this.#frame(0xa, payload); continue; }
      if (op === 0xa) continue;
      if (op === 0x1 || op === 0x0) {
        this.parts.push(payload);
        if (fin) {
          const text = Buffer.concat(this.parts).toString('utf8');
          this.parts = [];
          this.onmessage?.(text);
        }
      }
    }
  }

  #frame(op, payload) {
    if (!this.open) return;
    const len = payload.length;
    let head;
    if (len < 126) head = Buffer.from([0x80 | op, len]);
    else if (len < 65536) { head = Buffer.alloc(4); head[0] = 0x80 | op; head[1] = 126; head.writeUInt16BE(len, 2); }
    else { head = Buffer.alloc(10); head[0] = 0x80 | op; head[1] = 127; head.writeBigUInt64BE(BigInt(len), 2); }
    this.sock.write(Buffer.concat([head, payload]));
  }

  send(text) { this.#frame(0x1, Buffer.from(text, 'utf8')); }

  close() {
    if (this.open) { try { this.#frame(0x8, Buffer.alloc(0)); } catch { /* socket already gone */ } }
    this.sock.end();
    this.#closed();
  }
}

// ---------------------------------------------------------------------------
// Rooms
// ---------------------------------------------------------------------------

const rooms = new Map();

function makeCode() {
  const letters = 'ABCDEFGHJKLMNPQRSTUVWXYZ';
  for (;;) {
    let c = '';
    for (let i = 0; i < 4; i++) c += letters[crypto.randomInt(letters.length)];
    if (!rooms.has(c)) return c;
  }
}

function createRoom() {
  const code = makeCode();
  const room = { code, clients: new Map(), match: null, nextClient: 1 };
  room.match = new Match((to, msg) => {
    const text = JSON.stringify(msg);
    if (to === null) {
      for (const [id, s] of room.clients) if (msg.ex !== id) s.send(text);
    } else room.clients.get(to)?.send(text);
  });
  room.timer = setInterval(() => room.match.tick(TICK), TICK * 1000);
  rooms.set(code, room);
  console.log(`room ${code} created (${rooms.size} open)`);
  return room;
}

function closeRoom(room) {
  clearInterval(room.timer);
  rooms.delete(room.code);
  console.log(`room ${room.code} closed (${rooms.size} open)`);
}

server.on('upgrade', (req, sock) => {
  const url = new URL(req.url, 'http://x');
  const key = req.headers['sec-websocket-key'];
  if (url.pathname !== '/ws' || !key) return sock.destroy();

  let room;
  const code = (url.searchParams.get('room') || '').toUpperCase();
  if (code) room = rooms.get(code);
  else if (rooms.size < MAX_ROOMS) room = createRoom();

  const accept = crypto.createHash('sha1').update(key + '258EAFA5-E914-47DA-95CA-C5AB0DC85B11').digest('base64');
  sock.write('HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n' +
    `Sec-WebSocket-Accept: ${accept}\r\n\r\n`);
  const ws = new Socket(sock);
  if (!room) {
    ws.send(JSON.stringify({ t: 'error', msg: code ? `ROOM ${code} NOT FOUND` : 'SERVER FULL' }));
    return ws.close();
  }
  const id = 'p' + room.nextClient++;
  room.clients.set(id, ws);
  ws.send(JSON.stringify({ t: 'room', code: room.code }));
  const name = url.searchParams.get('name') || 'PLAYER';
  const level = Number(url.searchParams.get('level')) || 1;
  if (!room.match.addPlayer(id, name, level)) {
    ws.send(JSON.stringify({ t: 'error', msg: 'ROOM FULL' }));
    room.clients.delete(id);
    ws.close();
    if (!room.clients.size) closeRoom(room);
    return;
  }
  ws.onmessage = (text) => {
    let msg;
    try { msg = JSON.parse(text); } catch { return; }
    room.match.handle(id, msg);
  };
  ws.onclose = () => {
    room.clients.delete(id);
    room.match.removePlayer(id);
    if (!room.clients.size) closeRoom(room);
  };
});

server.listen(PORT, () => console.log(`SUNDOWN ARENA web: http://localhost:${PORT}`));
