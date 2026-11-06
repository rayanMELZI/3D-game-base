// Keyboard + mouse state with per-frame press edges, pointer lock, and a
// gamepad layer (Xbox layout through the standard Gamepad API mapping).

export const Input = {
  keys: new Set(),
  pressed: new Set(),
  released: new Set(),
  mouse: [false, false, false],
  mousePressed: [false, false, false],
  dx: 0, dy: 0, wheel: 0,
  locked: false,
  pad: null,
  padPrev: [],

  init(canvas) {
    addEventListener('keydown', (e) => {
      if (e.repeat) return;
      if (['Tab', 'Space', 'ControlLeft', 'KeyC', 'KeyZ'].includes(e.code) && this.locked) e.preventDefault();
      if (e.code === 'Tab') e.preventDefault();
      this.keys.add(e.code);
      this.pressed.add(e.code);
    });
    addEventListener('keyup', (e) => { this.keys.delete(e.code); this.released.add(e.code); });
    addEventListener('blur', () => { this.keys.clear(); this.mouse = [false, false, false]; });
    canvas.addEventListener('mousedown', (e) => {
      if (!this.locked) return;
      this.mouse[e.button] = true;
      this.mousePressed[e.button] = true;
    });
    addEventListener('mouseup', (e) => { this.mouse[e.button] = false; });
    addEventListener('mousemove', (e) => {
      if (!this.locked) return;
      this.dx += e.movementX;
      this.dy += e.movementY;
    });
    addEventListener('wheel', (e) => { if (this.locked) this.wheel += Math.sign(e.deltaY); }, { passive: true });
    addEventListener('contextmenu', (e) => e.preventDefault());
    document.addEventListener('pointerlockchange', () => {
      this.locked = document.pointerLockElement === canvas;
      if (!this.locked) { this.mouse = [false, false, false]; this.keys.clear(); }
    });
  },

  lock(canvas) {
    const p = canvas.requestPointerLock?.({ unadjustedMovement: true });
    if (p && p.catch) p.catch(() => canvas.requestPointerLock());
  },
  unlock() { if (document.pointerLockElement) document.exitPointerLock(); },

  down(code) { return this.keys.has(code); },
  hit(code) { return this.pressed.has(code); },

  // ---- Gamepad (standard mapping) ----
  pollPad() {
    const pads = navigator.getGamepads ? navigator.getGamepads() : [];
    this.pad = [...pads].find((p) => p && p.connected) || null;
  },
  padDown(i) { return !!(this.pad && this.pad.buttons[i] && this.pad.buttons[i].value > 0.4); },
  padHit(i) { return this.padDown(i) && !this.padPrev[i]; },
  padAxis(i) { return this.pad ? this.pad.axes[i] || 0 : 0; },

  endFrame() {
    this.pressed.clear();
    this.released.clear();
    this.mousePressed = [false, false, false];
    this.dx = this.dy = this.wheel = 0;
    this.padPrev = this.pad ? this.pad.buttons.map((b) => b.value > 0.4) : [];
  },
};

export const PAD = { A: 0, B: 1, X: 2, Y: 3, LB: 4, RB: 5, LT: 6, RT: 7, View: 8, Menu: 9, LS: 10, RS: 11, Up: 12, Down: 13, Left: 14, Right: 15 };
