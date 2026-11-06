// Builds the three.js scene for a map (EnvironmentBuilder visuals): materials,
// ground tiles, lights, golden-hour sky, fog, and the per-map atmosphere
// (SKYFREIGHT clouds + turbulence, NIGHTFALL fire flicker).

import * as THREE from 'three';

const srgb = (c) => new THREE.Color().setRGB(c[0], c[1], c[2], THREE.SRGBColorSpace);
const DEG = Math.PI / 180;

const matCache = new Map();
let grainTex = null;

/** Faint world grain (stands in for the Sundown/WorldSurface noise). */
function grain() {
  if (grainTex) return grainTex;
  const c = document.createElement('canvas');
  c.width = c.height = 128;
  const g = c.getContext('2d');
  const img = g.createImageData(128, 128);
  for (let i = 0; i < img.data.length; i += 4) {
    const v = 236 + Math.random() * 19;
    img.data[i] = img.data[i + 1] = img.data[i + 2] = v;
    img.data[i + 3] = 255;
  }
  g.putImageData(img, 0, 0);
  grainTex = new THREE.CanvasTexture(c);
  grainTex.wrapS = grainTex.wrapT = THREE.RepeatWrapping;
  grainTex.colorSpace = THREE.SRGBColorSpace;
  return grainTex;
}

function tileTexture(a, b, repeatX, repeatY) {
  const c = document.createElement('canvas');
  c.width = c.height = 64;
  const g = c.getContext('2d');
  const ca = srgb(a).getStyle(THREE.SRGBColorSpace), cb = srgb(b).getStyle(THREE.SRGBColorSpace);
  g.fillStyle = ca; g.fillRect(0, 0, 64, 64);
  g.fillStyle = cb; g.fillRect(0, 0, 32, 32); g.fillRect(32, 32, 32, 32);
  // Grout lines.
  g.fillStyle = 'rgba(0,0,0,0.12)';
  g.fillRect(0, 0, 64, 1); g.fillRect(0, 32, 64, 1); g.fillRect(0, 0, 1, 64); g.fillRect(32, 0, 1, 64);
  const tex = new THREE.CanvasTexture(c);
  tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
  tex.repeat.set(repeatX, repeatY);
  tex.colorSpace = THREE.SRGBColorSpace;
  tex.anisotropy = 8;
  tex.magFilter = THREE.NearestFilter;
  return tex;
}

export function material(m) {
  const key = JSON.stringify(m);
  if (matCache.has(key)) return matCache.get(key);
  const params = {
    color: srgb(m.color), metalness: m.metallic * 0.7, roughness: 1 - m.smoothness * 0.8,
  };
  if (m.emission > 0) {
    params.emissive = srgb(m.color);
    params.emissiveIntensity = m.emission * 0.9;
  }
  if (m.tiles) params.map = tileTexture(m.tiles[0], m.tiles[1], m.tileScale[0], m.tileScale[1]), params.color = new THREE.Color(1, 1, 1);
  else if (!m.emission) params.map = grain();
  const mat = new THREE.MeshStandardMaterial(params);
  matCache.set(key, mat);
  return mat;
}

const boxGeo = new THREE.BoxGeometry(1, 1, 1);
const cylGeo = new THREE.CylinderGeometry(0.5, 0.5, 2, 20);
const sphereGeo = new THREE.SphereGeometry(0.5, 20, 14);

// ---------------------------------------------------------------------------
// Sky
// ---------------------------------------------------------------------------

const SKIES = {
  sunset: { zenith: [0.33, 0.39, 0.62], horizon: [0.93, 0.66, 0.5], ground: [0.38, 0.32, 0.3], sun: [1, 0.85, 0.62], stars: 0 },
  flight: { zenith: [0.25, 0.45, 0.85], horizon: [0.8, 0.88, 0.98], ground: [0.85, 0.9, 0.97], sun: [1, 0.95, 0.85], stars: 0 },
  night: { zenith: [0.01, 0.012, 0.03], horizon: [0.04, 0.045, 0.07], ground: [0.02, 0.02, 0.03], sun: [0, 0, 0], stars: 1 },
  underwater: { zenith: [0.05, 0.35, 0.42], horizon: [0.02, 0.2, 0.25], ground: [0.01, 0.08, 0.1], sun: [0.3, 0.7, 0.75], stars: 0 },
  indoor: { zenith: [0.55, 0.5, 0.32], horizon: [0.55, 0.5, 0.32], ground: [0.5, 0.45, 0.28], sun: [0, 0, 0], stars: 0 },
};

function makeSky(kind, sunDir) {
  const s = SKIES[kind] || SKIES.sunset;
  const mat = new THREE.ShaderMaterial({
    side: THREE.BackSide, depthWrite: false, fog: false,
    uniforms: {
      zenith: { value: srgb(s.zenith) }, horizon: { value: srgb(s.horizon) }, groundC: { value: srgb(s.ground) },
      sunC: { value: srgb(s.sun) }, sunDir: { value: sunDir.clone().negate() }, stars: { value: s.stars },
    },
    vertexShader: `varying vec3 vDir; void main(){ vDir = normalize(position); vec4 p = projectionMatrix * modelViewMatrix * vec4(position,1.0); gl_Position = p.xyww; }`,
    fragmentShader: `
      uniform vec3 zenith, horizon, groundC, sunC, sunDir; uniform float stars; varying vec3 vDir;
      float hash(vec3 p){ p = fract(p*0.3183099+.1); p*=17.0; return fract(p.x*p.y*p.z*(p.x+p.y+p.z)); }
      void main(){
        vec3 d = normalize(vDir);
        float h = d.y;
        vec3 col = h > 0.0 ? mix(horizon, zenith, pow(clamp(h,0.0,1.0), 0.55)) : mix(horizon, groundC, clamp(-h*4.0,0.0,1.0));
        float sd = max(dot(d, normalize(sunDir)), 0.0);
        col += sunC * (pow(sd, 900.0) * 6.0 + pow(sd, 12.0) * 0.35);
        if (stars > 0.0 && h > 0.05) { vec3 q = floor(d*260.0); float s = step(0.997, hash(q)); col += vec3(s) * 0.8 * h; }
        gl_FragColor = vec4(col, 1.0);
        #include <tonemapping_fragment>
        #include <colorspace_fragment>
      }`,
  });
  const mesh = new THREE.Mesh(new THREE.SphereGeometry(900, 32, 16), mat);
  mesh.frustumCulled = false;
  mesh.renderOrder = -1;
  return mesh;
}

// ---------------------------------------------------------------------------
// Build
// ---------------------------------------------------------------------------

export function buildWorld(scene, map, quality = 'high') {
  const root = new THREE.Group();
  root.name = 'MapRoot';
  const shadows = quality !== 'low';

  // Sun: Unity Euler(28, -40, 0), converted to three's axes.
  const sunDir = new THREE.Vector3(Math.sin(-40 * DEG) * Math.cos(28 * DEG), -Math.sin(28 * DEG), -Math.cos(-40 * DEG) * Math.cos(28 * DEG)).normalize();
  const night = map.sky === 'night';
  const indoor = map.sky === 'indoor';
  const sun = new THREE.DirectionalLight(srgb([1, 0.84, 0.64]), night ? 0.08 : indoor ? 0.5 : map.sky === 'flight' ? 2.4 : 2.6);
  sun.position.copy(sunDir).multiplyScalar(-60);
  sun.target.position.set(0, 0, 0);
  if (shadows && !indoor) {
    sun.castShadow = true;
    sun.shadow.mapSize.set(quality === 'high' ? 2048 : 1024, quality === 'high' ? 2048 : 1024);
    const cam = sun.shadow.camera;
    cam.left = cam.bottom = -45; cam.right = cam.top = 45; cam.near = 1; cam.far = 160;
    sun.shadow.bias = -0.0004;
    sun.shadow.normalBias = 0.03;
  }
  root.add(sun, sun.target);
  const hemi = new THREE.HemisphereLight(srgb([0.56, 0.6, 0.72]), srgb([0.3, 0.26, 0.24]), night ? 0.12 : indoor ? 0.9 : 1.1);
  root.add(hemi);
  if (night) root.add(new THREE.AmbientLight(srgb([0.25, 0.3, 0.45]), 0.25));

  scene.fog = new THREE.FogExp2(srgb(map.fog.color), map.fog.density);
  root.add(makeSky(map.sky, sunDir));

  if (map.ground) {
    const g = map.ground;
    const mesh = new THREE.Mesh(new THREE.PlaneGeometry(g.sizeX, g.sizeZ), new THREE.MeshStandardMaterial({
      map: tileTexture(g.a, g.b, g.sizeX / 8, g.sizeZ / 8), roughness: 0.85, metalness: 0,
    }));
    mesh.rotation.x = -Math.PI / 2;
    mesh.receiveShadow = true;
    root.add(mesh);
  }

  for (const b of map.boxes) {
    const mesh = new THREE.Mesh(boxGeo, material(b.m));
    mesh.position.set(b.p[0], b.p[1], b.p[2]);
    mesh.rotation.set(b.r[0] * DEG, b.r[1] * DEG, b.r[2] * DEG, 'YXZ');
    mesh.scale.set(b.s[0], b.s[1], b.s[2]);
    mesh.castShadow = shadows && !b.m.emission;
    mesh.receiveShadow = shadows;
    mesh.matrixAutoUpdate = false;
    mesh.updateMatrix();
    root.add(mesh);
  }
  for (const c of map.cylinders) {
    const mesh = new THREE.Mesh(cylGeo, material(c.m));
    mesh.position.set(c.p[0], c.p[1], c.p[2]);
    mesh.rotation.set(c.r[0] * DEG, c.r[1] * DEG, c.r[2] * DEG, 'YXZ');
    mesh.scale.set(c.s[0], c.s[1], c.s[2]);
    mesh.castShadow = shadows && !c.m.emission;
    mesh.receiveShadow = shadows;
    root.add(mesh);
  }
  for (const s of map.spheres) {
    const mesh = new THREE.Mesh(sphereGeo, material(s.m));
    mesh.position.set(s.p[0], s.p[1], s.p[2]);
    mesh.scale.set(s.s[0], s.s[1], s.s[2]);
    root.add(mesh);
  }

  const lights = [];
  const maxLights = quality === 'low' ? 4 : quality === 'medium' ? 8 : 16;
  for (const l of map.lights.slice(0, maxLights)) {
    const pl = new THREE.PointLight(srgb(l.color), l.intensity * 9, l.range * 1.2, 1.4);
    pl.position.set(l.p[0], l.p[1], l.p[2]);
    root.add(pl);
    lights.push(pl);
  }

  const atmosphere = { update() {}, shake: 0 };
  if (map.fires) {
    const fires = map.fires.filter((i) => i < lights.length).map((i) => ({ light: lights[i], base: lights[i].intensity, seed: Math.random() * 100 }));
    atmosphere.update = (dt, t) => {
      for (const f of fires) f.light.intensity = f.base * (0.75 + 0.25 * Math.sin(t * 13 + f.seed) * Math.sin(t * 7.3 + f.seed * 2));
    };
  }
  if (map.flight) buildFlight(root, atmosphere);

  scene.add(root);
  return {
    root, atmosphere,
    dispose() { scene.remove(root); root.traverse((o) => { if (o.geometry && o.geometry !== boxGeo && o.geometry !== cylGeo && o.geometry !== sphereGeo) o.geometry.dispose(); }); },
  };
}

/** SKYFREIGHT: cloud sea below, clouds streaming past, turbulence shake. */
function buildFlight(root, atmosphere) {
  const cloudMat = new THREE.MeshStandardMaterial({ color: srgb([0.96, 0.97, 1]), emissive: srgb([0.96, 0.97, 1]), emissiveIntensity: 0.35, roughness: 1 });
  const sea = new THREE.Mesh(new THREE.PlaneGeometry(1200, 1200), new THREE.MeshStandardMaterial({ color: srgb([0.86, 0.9, 0.97]), emissive: srgb([0.86, 0.9, 0.97]), emissiveIntensity: 0.25, roughness: 1 }));
  sea.rotation.x = -Math.PI / 2;
  sea.position.y = -70;
  root.add(sea);
  // Same seeded sky on every client.
  let seed = 1337;
  const rnd = () => ((seed = (seed * 16807) % 2147483647) / 2147483647);
  const R = (a, b) => a + rnd() * (b - a);
  const clouds = new THREE.Group();
  for (let i = 0; i < 70; i++) {
    const c = i % 10 < 7
      ? new THREE.Vector3(R(-160, 160), R(-40, -18), R(-320, 320))
      : new THREE.Vector3((i % 2 === 0 ? 1 : -1) * R(55, 150), R(-12, 18), R(-320, 320));
    const w = R(14, 38), d = R(14, 38);
    const cloud = new THREE.Group();
    cloud.position.copy(c);
    const main = new THREE.Mesh(sphereGeo, cloudMat);
    main.scale.set(w, R(4, 8), d);
    const puff = new THREE.Mesh(sphereGeo, cloudMat);
    puff.position.set(R(-w, w) * 0.3, R(1, 3), R(-d, d) * 0.3);
    puff.scale.set(w * 0.6, R(5, 9), d * 0.6);
    cloud.add(main, puff);
    clouds.add(cloud);
  }
  root.add(clouds);
  atmosphere.update = (dt, t) => {
    // The plane flies toward Unity +Z = three -Z: clouds stream toward +Z.
    for (const c of clouds.children) {
      c.position.z += 55 * dt;
      if (c.position.z > 320) c.position.z -= 640;
    }
    atmosphere.shake = 0.15 + 0.85 * Math.max(0, Math.sin(t * 0.21) * Math.sin(t * 0.13 + 1));
  };
}
