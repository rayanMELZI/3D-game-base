// Port of Player/PlayerMovement.cs + MouseLook: CharacterController-style
// movement (walk / sprint / crouch / slide / prone / bunny hop) on the shared
// collision world, eye height blends, and mouse / right-stick look.

import { Input, PAD } from './input.js';
import { S } from './settings.js';

const WALK = 5, SPRINT = 8.5, CROUCH = 2.6, PRONE = 1.45;
const SLIDE_SPEED = 10.5, SLIDE_DURATION = 0.85;
const JUMP_HEIGHT = 1.2, GRAVITY = -20;
const STAND_H = 1.8, CROUCH_H = 1.2, PRONE_H = 0.55;
const STAND_EYE = 1.62, CROUCH_EYE = 1.12, PRONE_EYE = 0.58, PRONE_EYE_FORWARD = 0.25;
const RADIUS = 0.4;
const PITCH_LIMIT = 88 * Math.PI / 180;

export class LocalPlayer {
  constructor() {
    this.p = [0, 0, 0];
    this.yaw = 0;
    this.pitch = 0;
    this.vy = 0;
    this.hv = [0, 0];
    this.grounded = false;
    this.crouchBlend = 0;
    this.proneBlend = 0;
    this.slideTimer = 0;
    this.slideDir = [0, 0];
    this.proneToggle = false;
    this.padSprint = false;
    this.padCrouch = false;
    this.sprinting = false;
    this.speedMultiplier = 1;
    this.stepAcc = 0;
    this.recoilPitch = 0;
    this.recoilYaw = 0;
    this.shake = 0;
  }

  get sliding() { return this.slideTimer > 0; }
  get height() { const h = STAND_H + (CROUCH_H - STAND_H) * this.crouchBlend; return h + (PRONE_H - h) * this.proneBlend; }
  get eyeHeight() { const e = STAND_EYE + (CROUCH_EYE - STAND_EYE) * this.crouchBlend; return e + (PRONE_EYE - e) * this.proneBlend; }

  teleport(p, yaw) {
    this.p = p.slice();
    this.yaw = yaw ?? this.yaw;
    this.pitch = 0;
    this.vy = 0;
    this.slideTimer = 0;
    this.proneToggle = false;
    this.crouchBlend = this.proneBlend = 0;
  }

  /** Mouse + right-stick look. sensScale < 1 while aiming down sights. */
  look(dt, sensScale = 1) {
    const k = 0.0022 * S.sensitivity * sensScale;
    this.yaw -= Input.dx * k;
    this.pitch -= Input.dy * k * (S.invertY ? -1 : 1);
    // Right stick: rate-based (deg/s), radial dead zone.
    const rx = Input.padAxis(2), ry = Input.padAxis(3);
    const mag = Math.hypot(rx, ry);
    if (mag > 0.15) {
      const scale = ((mag - 0.15) / 0.85) / mag;
      const rate = 170 * Math.PI / 180 * sensScale * (S.sensitivity / 2);
      this.yaw -= rx * scale * rate * dt;
      this.pitch -= ry * scale * rate * dt * (S.invertY ? -1 : 1);
    }
    this.pitch = Math.max(-PITCH_LIMIT, Math.min(PITCH_LIMIT, this.pitch));
  }

  /** Movement step. aiming/firing stop a controller sprint. Returns footstep flag. */
  update(dt, world, { aiming = false, firing = false } = {}) {
    const grounded = this.grounded;
    let ix = 0, iz = 0;
    if (Input.down('KeyW') || Input.down('ArrowUp')) iz += 1;
    if (Input.down('KeyS') || Input.down('ArrowDown')) iz -= 1;
    if (Input.down('KeyD') || Input.down('ArrowRight')) ix += 1;
    if (Input.down('KeyA') || Input.down('ArrowLeft')) ix -= 1;
    const lx = Input.padAxis(0), ly = -Input.padAxis(1);
    if (Math.hypot(lx, ly) > 0.18) { ix += lx; iz += ly; }

    const fwd = [-Math.sin(this.yaw), -Math.cos(this.yaw)];
    const right = [Math.cos(this.yaw), -Math.sin(this.yaw)];
    let mx = right[0] * ix + fwd[0] * iz, mz = right[1] * ix + fwd[1] * iz;
    const ml = Math.hypot(mx, mz);
    if (ml > 1) { mx /= ml; mz /= ml; }
    const moving = mx * mx + mz * mz > 0.1;

    if (Input.hit('KeyZ')) this.proneToggle = !this.proneToggle;
    const jumpHeld = Input.down('Space') || Input.padDown(PAD.A);
    if (Input.hit('Space') || Input.padHit(PAD.A)) { this.proneToggle = false; this.padCrouch = false; }

    // Controller: stick click latches sprint, B taps crouch / slides.
    let padSlide = false;
    if (Input.padHit(PAD.LS)) { this.padSprint = true; this.padCrouch = false; this.proneToggle = false; }
    if (iz <= 0.1 || aiming || firing) this.padSprint = false;
    if (Input.padHit(PAD.B)) {
      if (this.padSprint && grounded && this.slideTimer <= 0 && moving) padSlide = true;
      else this.padCrouch = !this.padCrouch;
    }

    const prone = this.proneToggle;
    const crouchKey = Input.down('ControlLeft') || Input.down('KeyC');
    const wantCrouch = !prone && (crouchKey || this.padCrouch);
    const sprintHeld = Input.down('ShiftLeft') || this.padSprint;
    this.sprinting = sprintHeld && iz > 0.1 && moving && !wantCrouch && !prone && !this.sliding;

    if (grounded && this.slideTimer <= 0 && sprintHeld && iz > 0.1 && moving &&
      (Input.hit('ControlLeft') || Input.hit('KeyC') || padSlide)) {
      this.slideTimer = SLIDE_DURATION;
      const l = Math.hypot(mx, mz) || 1;
      this.slideDir = [mx / l, mz / l];
      if (padSlide) { this.padSprint = false; this.padCrouch = true; }
    }
    if (this.slideTimer > 0) {
      this.slideTimer -= dt;
      if (!grounded) this.slideTimer = 0;
    }
    const crouching = wantCrouch || this.sliding || prone;

    let hx, hz;
    if (this.sliding) {
      const t = 1 - this.slideTimer / SLIDE_DURATION;
      const speed = SLIDE_SPEED + (CROUCH - SLIDE_SPEED) * t;
      hx = this.slideDir[0] * speed + right[0] * ix * 1.2;
      hz = this.slideDir[1] * speed + right[1] * ix * 1.2;
    } else {
      const speed = (prone ? PRONE : crouching ? CROUCH : this.sprinting ? SPRINT : WALK) * this.speedMultiplier;
      if (this.sprinting) { hx = fwd[0] * speed; hz = fwd[1] * speed; }
      else { hx = mx * speed; hz = mz * speed; }
    }
    this.hv = [hx, hz];

    // Standing up needs head room.
    const targetCrouch = crouching ? 1 : 0, targetProne = prone ? 1 : 0;
    const nextCrouch = moveTowards(this.crouchBlend, targetCrouch, 8 * dt);
    const nextProne = moveTowards(this.proneBlend, targetProne, 3.5 * dt);
    const hNext = (() => { const h = STAND_H + (CROUCH_H - STAND_H) * nextCrouch; return h + (PRONE_H - h) * nextProne; })();
    if (hNext <= this.height + 1e-4 || world.fits(this.p, RADIUS, hNext)) {
      this.crouchBlend = nextCrouch;
      this.proneBlend = nextProne;
    } else if (!prone) {
      this.proneToggle = this.proneBlend > 0.5;
    }

    if (grounded && this.vy < 0) this.vy = -2;
    if (grounded && jumpHeld && this.proneBlend < 0.1) {
      this.vy = Math.sqrt(JUMP_HEIGHT * -2 * GRAVITY);
      this.slideTimer = 0;
    }
    this.vy += GRAVITY * dt;
    const res = world.moveCharacter(this.p, [hx * dt, this.vy * dt, hz * dt], RADIUS, this.height);
    this.grounded = res.grounded;
    if (res.hitCeiling && this.vy > 0) this.vy = 0;
    if (this.grounded && this.vy < -2) this.vy = -2;

    // Footsteps.
    let step = false;
    const hs = Math.hypot(hx, hz);
    if (this.grounded && hs > 1 && !this.sliding) {
      this.stepAcc += hs * dt;
      const stride = this.sprinting ? 2.4 : 1.9;
      if (this.stepAcc > stride) { this.stepAcc = 0; step = true; }
    }
    return step;
  }

  eyePosition() {
    const fx = -Math.sin(this.yaw), fz = -Math.cos(this.yaw);
    const fwd = PRONE_EYE_FORWARD * this.proneBlend;
    return [this.p[0] + fx * fwd, this.p[1] + this.eyeHeight, this.p[2] + fz * fwd];
  }

  get speed() { return Math.hypot(this.hv[0], this.hv[1]); }
}

function moveTowards(a, b, step) {
  return a < b ? Math.min(b, a + step) : Math.max(b, a - step);
}
