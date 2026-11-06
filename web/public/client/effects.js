// Port of Combat/Effects.cs: one shared world-space particle system (sparks,
// dust, blood puffs, smoke, fire), tracers, a ring of 96 bullet-hole decals,
// explosions (fireball + light + smoke + debris + scorch) and muzzle flashes.

import * as THREE from 'three';

const MAX_PARTICLES = 3000;
const MAX_DECALS = 96;

export class Effects {
  constructor(scene) {
    this.scene = scene;
    // ---- Particles ----
    this.pos = new Float32Array(MAX_PARTICLES * 3);
    this.col = new Float32Array(MAX_PARTICLES * 4);
    this.size = new Float32Array(MAX_PARTICLES);
    this.vel = new Float32Array(MAX_PARTICLES * 3);
    this.life = new Float32Array(MAX_PARTICLES);
    this.maxLife = new Float32Array(MAX_PARTICLES);
    this.grav = new Float32Array(MAX_PARTICLES);
    this.grow = new Float32Array(MAX_PARTICLES);
    this.baseA = new Float32Array(MAX_PARTICLES);
    this.cursor = 0;
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.BufferAttribute(this.pos, 3).setUsage(THREE.DynamicDrawUsage));
    geo.setAttribute('color', new THREE.BufferAttribute(this.col, 4).setUsage(THREE.DynamicDrawUsage));
    geo.setAttribute('size', new THREE.BufferAttribute(this.size, 1).setUsage(THREE.DynamicDrawUsage));
    this.geo = geo;
    const mat = new THREE.ShaderMaterial({
      transparent: true, depthWrite: false, blending: THREE.NormalBlending,
      uniforms: { scale: { value: 600 } },
      vertexShader: `attribute float size; attribute vec4 color; varying vec4 vColor; uniform float scale;
        void main(){ vColor = color; vec4 mv = modelViewMatrix * vec4(position,1.0); gl_PointSize = size * scale / max(0.1, -mv.z); gl_Position = projectionMatrix * mv; }`,
      fragmentShader: `varying vec4 vColor; void main(){ vec2 d = gl_PointCoord - 0.5; float r = dot(d,d); if (r > 0.25) discard; gl_FragColor = vec4(vColor.rgb, vColor.a * (1.0 - r * 4.0)); }`,
    });
    this.points = new THREE.Points(geo, mat);
    this.points.frustumCulled = false;
    this.points.renderOrder = 5;
    scene.add(this.points);
    this.mat = mat;

    // ---- Decals ----
    this.decals = [];
    this.decalIndex = 0;
    this.decalGeo = new THREE.PlaneGeometry(1, 1);
    const holeTex = makeHoleTexture();
    this.holeMat = new THREE.MeshBasicMaterial({ map: holeTex, transparent: true, depthWrite: false, polygonOffset: true, polygonOffsetFactor: -4 });
    this.scorchMat = new THREE.MeshBasicMaterial({ map: holeTex, transparent: true, opacity: 0.8, depthWrite: false, polygonOffset: true, polygonOffsetFactor: -4, color: 0x222222 });

    // ---- Tracers / flashes / lights ----
    this.tracers = [];
    this.lights = [];
    this.fireballs = [];
    this.tracerMat = new THREE.LineBasicMaterial({ color: 0xffe0a0, transparent: true, opacity: 0.7 });
    this.flashTex = makeFlashTexture();
  }

  setScale(height, fovDeg) {
    this.mat.uniforms.scale.value = height / (2 * Math.tan((fovDeg * Math.PI) / 360));
  }

  emit(p, v, color, alpha, size, life, gravity = 0, grow = 0) {
    const i = this.cursor;
    this.cursor = (this.cursor + 1) % MAX_PARTICLES;
    this.pos.set(p, i * 3);
    this.vel.set(v, i * 3);
    this.col[i * 4] = color[0]; this.col[i * 4 + 1] = color[1]; this.col[i * 4 + 2] = color[2]; this.col[i * 4 + 3] = alpha;
    this.baseA[i] = alpha;
    this.size[i] = size;
    this.life[i] = life;
    this.maxLife[i] = life;
    this.grav[i] = gravity;
    this.grow[i] = grow;
  }

  /** Bullet impact: sparks + surface-coloured dust, or a red puff on characters. */
  impact(point, normal, flesh, surfaceColor = [0.6, 0.58, 0.55]) {
    const n = normal || [0, 1, 0];
    if (flesh) {
      for (let k = 0; k < 10; k++) this.emit(point, jitter(n, 1.6, 1.5), [0.55, 0.03, 0.02], 0.9, 0.09 + Math.random() * 0.08, 0.35 + Math.random() * 0.2, 6, 0.25);
      return;
    }
    for (let k = 0; k < 7; k++) this.emit(point, jitter(n, 5, 3), [1, 0.8, 0.45], 1, 0.025, 0.12 + Math.random() * 0.12, 12);
    for (let k = 0; k < 6; k++) this.emit(point, jitter(n, 1.2, 0.6), surfaceColor, 0.55, 0.12 + Math.random() * 0.1, 0.6 + Math.random() * 0.4, 1.5, 0.5);
    this.decal(point, n, 0.09 + Math.random() * 0.03, this.holeMat);
  }

  decal(point, normal, size, mat) {
    let mesh = this.decals[this.decalIndex];
    if (!mesh) {
      mesh = new THREE.Mesh(this.decalGeo, mat);
      this.decals[this.decalIndex] = mesh;
      this.scene.add(mesh);
    }
    mesh.material = mat;
    this.decalIndex = (this.decalIndex + 1) % MAX_DECALS;
    const nv = new THREE.Vector3(...normal);
    mesh.position.set(point[0] + normal[0] * 0.01, point[1] + normal[1] * 0.01, point[2] + normal[2] * 0.01);
    mesh.quaternion.setFromUnitVectors(new THREE.Vector3(0, 0, 1), nv);
    mesh.rotateZ(Math.random() * Math.PI * 2);
    mesh.scale.setScalar(size);
    mesh.visible = true;
  }

  clearDecals() {
    for (const d of this.decals) if (d) d.visible = false;
  }

  tracer(from, to) {
    const geo = new THREE.BufferGeometry().setFromPoints([new THREE.Vector3(...from), new THREE.Vector3(...to)]);
    const line = new THREE.Line(geo, this.tracerMat.clone());
    this.scene.add(line);
    this.tracers.push({ line, life: 0.07 });
  }

  muzzleFlash(pos, dir, small = false) {
    const s = new THREE.Sprite(new THREE.SpriteMaterial({ map: this.flashTex, color: 0xffd9a0, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending }));
    s.position.set(...pos);
    s.scale.setScalar(small ? 0.18 : 0.35);
    s.material.rotation = Math.random() * Math.PI;
    this.scene.add(s);
    this.fireballs.push({ obj: s, life: 0.05, max: 0.05, kind: 'flash' });
    this.light(pos, 0xffb060, 3, 6, 0.06);
  }

  light(pos, color, intensity, distance, life) {
    if (this.lights.length > 6) return;
    const l = new THREE.PointLight(color, intensity, distance, 1.5);
    l.position.set(...pos);
    this.scene.add(l);
    this.lights.push({ l, life, max: life, base: intensity });
  }

  explosion(p, radius = 4, small = false) {
    const s = small ? 0.3 : Math.max(0.6, radius / 4.5);
    this.light(p, 0xff8a3a, small ? 6 : 40, radius * 4, small ? 0.15 : 0.5);
    const ball = new THREE.Mesh(new THREE.SphereGeometry(1, 16, 12), new THREE.MeshBasicMaterial({ color: 0xffa040, transparent: true, opacity: 0.95, depthWrite: false, blending: THREE.AdditiveBlending }));
    ball.position.set(...p);
    ball.scale.setScalar(0.3 * s);
    this.scene.add(ball);
    this.fireballs.push({ obj: ball, life: 0.35, max: 0.35, kind: 'ball', to: 2.2 * s });
    const n = small ? 8 : 40;
    for (let k = 0; k < n; k++) this.emit(p, jitter([0, 1, 0], 7 * s, 7 * s), [1, 0.55 + Math.random() * 0.3, 0.2], 1, 0.35 * s, 0.25 + Math.random() * 0.3, 2, 1.5);
    for (let k = 0; k < n; k++) this.emit(p, jitter([0, 1.5, 0], 2.5 * s, 2 * s), [0.42, 0.4, 0.38], 0.55, 0.8 * s, 1.6 + Math.random() * 1.2, -0.6, 1.4);
    for (let k = 0; k < n / 2; k++) this.emit(p, jitter([0, 1, 0], 9, 6), [1, 0.85, 0.5], 1, 0.03, 0.4 + Math.random() * 0.4, 14);
    for (let k = 0; k < n / 3; k++) this.emit(p, jitter([0, 1, 0], 6, 4), [0.12, 0.1, 0.09], 1, 0.07, 0.8 + Math.random() * 0.5, 14);
    if (!small) this.decal([p[0], p[1] < 0.6 ? 0.01 : p[1], p[2]], [0, 1, 0], radius * 0.7, this.scorchMat);
  }

  deathBurst(p, color) {
    for (let k = 0; k < 16; k++) this.emit(p, jitter([0, 1, 0], 3, 2), color, 0.9, 0.12, 0.6 + Math.random() * 0.4, 8, 0.2);
  }

  update(dt) {
    for (let i = 0; i < MAX_PARTICLES; i++) {
      if (this.life[i] <= 0) { this.col[i * 4 + 3] = 0; continue; }
      this.life[i] -= dt;
      this.vel[i * 3 + 1] -= this.grav[i] * dt;
      const drag = 1 - Math.min(1, 2.2 * dt);
      this.vel[i * 3] *= drag; this.vel[i * 3 + 2] *= drag;
      this.pos[i * 3] += this.vel[i * 3] * dt;
      this.pos[i * 3 + 1] += this.vel[i * 3 + 1] * dt;
      this.pos[i * 3 + 2] += this.vel[i * 3 + 2] * dt;
      this.size[i] += this.grow[i] * dt;
      this.col[i * 4 + 3] = this.baseA[i] * Math.max(0, this.life[i] / this.maxLife[i]);
    }
    this.geo.attributes.position.needsUpdate = true;
    this.geo.attributes.color.needsUpdate = true;
    this.geo.attributes.size.needsUpdate = true;

    for (let k = this.tracers.length - 1; k >= 0; k--) {
      const t = this.tracers[k];
      t.life -= dt;
      t.line.material.opacity = Math.max(0, t.life / 0.07) * 0.7;
      if (t.life <= 0) { this.scene.remove(t.line); t.line.geometry.dispose(); t.line.material.dispose(); this.tracers.splice(k, 1); }
    }
    for (let k = this.lights.length - 1; k >= 0; k--) {
      const l = this.lights[k];
      l.life -= dt;
      l.l.intensity = l.base * Math.max(0, l.life / l.max);
      if (l.life <= 0) { this.scene.remove(l.l); this.lights.splice(k, 1); }
    }
    for (let k = this.fireballs.length - 1; k >= 0; k--) {
      const f = this.fireballs[k];
      f.life -= dt;
      const u = 1 - Math.max(0, f.life / f.max);
      if (f.kind === 'ball') { f.obj.scale.setScalar(f.obj.scale.x + (f.to - f.obj.scale.x) * Math.min(1, dt * 14)); f.obj.material.opacity = 0.95 * (1 - u); }
      else f.obj.material.opacity = 1 - u;
      if (f.life <= 0) { this.scene.remove(f.obj); f.obj.material.dispose(); if (f.obj.geometry && f.kind === 'ball') f.obj.geometry.dispose(); this.fireballs.splice(k, 1); }
    }
  }
}

function jitter(n, speed, spread) {
  return [n[0] * speed * Math.random() + (Math.random() - 0.5) * spread,
    n[1] * speed * Math.random() + (Math.random() - 0.5) * spread,
    n[2] * speed * Math.random() + (Math.random() - 0.5) * spread];
}

function makeHoleTexture() {
  const c = document.createElement('canvas');
  c.width = c.height = 64;
  const g = c.getContext('2d');
  const grad = g.createRadialGradient(32, 32, 2, 32, 32, 30);
  grad.addColorStop(0, 'rgba(10,10,10,1)');
  grad.addColorStop(0.3, 'rgba(25,22,20,0.9)');
  grad.addColorStop(1, 'rgba(40,36,32,0)');
  g.fillStyle = grad;
  g.fillRect(0, 0, 64, 64);
  const t = new THREE.CanvasTexture(c);
  t.colorSpace = THREE.SRGBColorSpace;
  return t;
}

function makeFlashTexture() {
  const c = document.createElement('canvas');
  c.width = c.height = 64;
  const g = c.getContext('2d');
  const grad = g.createRadialGradient(32, 32, 0, 32, 32, 32);
  grad.addColorStop(0, 'rgba(255,255,230,1)');
  grad.addColorStop(0.25, 'rgba(255,190,90,0.9)');
  grad.addColorStop(1, 'rgba(255,120,30,0)');
  g.fillStyle = grad;
  g.beginPath();
  for (let i = 0; i < 10; i++) {
    const a = (i / 10) * Math.PI * 2, r = i % 2 ? 12 : 32;
    g.lineTo(32 + Math.cos(a) * r, 32 + Math.sin(a) * r);
  }
  g.fill();
  const t = new THREE.CanvasTexture(c);
  t.colorSpace = THREE.SRGBColorSpace;
  return t;
}
