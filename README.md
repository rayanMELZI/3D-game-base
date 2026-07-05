# SUNDOWN ARENA — 3D FPS Base (Unity)

An asset-free online first-person shooter base: 1v1 Duel and Team Deathmatch,
three weapons, humanoid characters with one-shot headshots, a full main menu,
procedural sound effects and a stylized golden-hour look — everything generated
from code, no models/textures/audio files.

> The original minimal version lives in the sibling folder
> **`3D game base v1 (simple)`** — keep that as the clean starting point for
> other games. Rename the game in `GameSettings.GameTitle`.

**Features**
- **Main menu** (builds boot into it): Play Online (host 1v1 / host TDM / join by
  IP, player name), Practice Range (offline dummies), Settings (sensitivity,
  FOV, volume, fullscreen, quality), Quit — plus an in-match pause menu (Esc)
- **3 weapons**: semi-auto pistol, automatic rifle, one-shot **sniper** with scope;
  switch with 1/2/3 or scroll
- **Headshots are instant kills** — heads have real hitboxes above the body capsule
- **Humanoid characters**: torso, helmet + glowing visor, swinging arms/legs
  (procedural walk animation), team-colored suits with glowing chest stripes,
  floating nametags, cube-burst death effect
- **Online (Netcode for GameObjects)**: team balancing, server-checked scores and
  friendly fire, kill feed with names and HEADSHOT tags, respawns, win banner +
  jingle, automatic match restart
- **Charm**: golden-hour sun + warm fog + procedural skybox, synthesized SFX
  (gunshots per weapon, hit/headshot dings, reload, death, UI clicks, win
  jingle), orbiting menu camera, tracers, hit markers, sprint FOV

## Setup (after pulling these changes)

1. Open the project and let it compile.
2. **Re-run `Tools > FPS Base > Setup Multiplayer (Scene + Player Prefab)`** —
   this rebuilds the player prefab (new humanoid body + head hitbox), rebuilds
   the Multiplayer scene (new menu), and **fixes the build scene order** so
   builds boot into the main menu. Do this again whenever these generated
   assets need to pick up code changes.
3. Press Play in the Multiplayer scene (or `File > Build And Run`) → main menu.

### Playing online
- **Same PC test:** host in a standalone build, press Play in the editor → Play
  Online → Join `127.0.0.1`.
- **LAN:** friends join the host's local IPv4 (`ipconfig`). Allow the game / UDP
  port **7777** through the host's Windows firewall.
- **Internet:** port-forward 7777, or add Unity **Relay + Lobby** (see notes below).

## Controls

| Input | Action |
|---|---|
| Mouse / WASD / Shift / Space | Look / move / sprint / jump |
| Left Mouse | Shoot (rifle auto, pistol & sniper per-click) |
| Right Mouse (hold) | Sniper scope |
| 1 / 2 / 3 or scroll | Pistol / Rifle / Sniper |
| R | Reload |
| Escape | Pause menu (resume, settings, leave) |

## Weapon balance (edit `WeaponDefinition.CreateDefaultLoadout`)

| | Damage | Fire rate | Mag | Special |
|---|---|---|---|---|
| Pistol | 20 | 5/s semi | 12 | fast reload |
| Rifle | 22 | 9/s auto | 30 | starting weapon |
| Sniper | **100 (one-shot)** | 0.9/s | 5 | 16° scope |
| Any weapon | — | — | — | **headshot = instant kill** |

## Project structure

```
Assets/
  Editor/                      # Add Game Bootstrap · Setup Multiplayer (bakes prefab+scene, fixes scene order)
  Scenes/                      # Multiplayer.unity (menu + online, scene 0) · Main.unity (practice)
  Scripts/
    Core/
      GameSettings.cs          # game title, saved player settings (PlayerPrefs)
      GameBootstrap.cs         # practice range: arena + humanoid dummies + player
      EnvironmentBuilder.cs    # golden-hour lighting/sky/fog + deterministic arena
      HumanoidBuilder.cs       # primitive humanoid body + head hitbox + materials
      PlayerFactory.cs         # player rig (controller, body, camera, weapons)
      PlayerRigRefs.cs         # reference hub, runtime materials, team color
    Player/                    # PlayerMovement · MouseLook (zoom/settings) · LimbAnimator
    Weapons/                   # WeaponDefinition · WeaponModelBuilder · WeaponController
    Combat/                    # IDamageable/IHealthSource · Health · Hitbox · TargetDummy · Effects
    Audio/SfxSynth.cs          # procedurally synthesized sound effects
    UI/                        # HudOverlay · MenuWidgets · OfflineMenu · Nametag
    Network/                   # MainMenu · GameModeManager · NetworkPlayer/Health/Weapon
                               # · NetworkGameHud · MultiplayerBootstrap · ClientAuthNetworkTransform
```

## Graphics

The game uses the built-in render pipeline with a **self-contained post stack**
(no packages): bloom + ACES filmic tonemapping + warm grading + vignette,
implemented in `Assets/Shaders/SundownPost.shader` and attached to every camera
by `PostFx.cs`. Re-run the setup tool once so the shader is force-included in
builds. Your own body renders **shadows-only** in first person (no clipping,
but your full shadow — including the held weapon — stays visible).

### Making it truly beautiful: importing other people's work

Everything visual is generated from primitives behind three small builder
classes, so real assets drop in cleanly:

| What | Great free sources | Where to plug it in |
|---|---|---|
| Characters (rigged + animated) | **Mixamo** (free, auto-rigged + animations), Kenney "Blocky Characters", Synty POLYGON free samples | Replace the body built in `HumanoidBuilder.Build` with your model prefab; keep the head `Hitbox` |
| Weapons | Kenney **"Blaster Kit"** / **"Weapon Pack"** (CC0), Asset Store free weapon packs | Return your prefab from `WeaponModelBuilder.Build` (keep the `Muzzle` child + flash light) |
| Environment / props | Kenney "Prototype Kit", Synty POLYGON packs, Unity Asset Store free section, **poly.pizza** (CC0 models) | Build a real scene, or spawn prefabs in `EnvironmentBuilder.BuildArena` |
| Skyboxes | Free HDRIs from **polyhaven.com** (CC0) | Assign in `EnvironmentBuilder.SetupLightingAndSky` instead of the procedural skybox |
| Audio | **freesound.org**, Kenney audio packs (CC0) | Return real clips from the `SfxSynth` methods |

Import via `Assets > Import Package` / dragging files into `Assets/`, or the
Package Manager's "My Assets" tab for Asset Store items. Check each pack's
license (CC0 = use freely; most Asset Store free packs allow game use, no
redistribution of the raw assets).

## Architecture notes

- **Offline and online share gameplay code** via `IDamageable`: offline `Health`
  applies damage directly, online `NetworkHealth` routes it to the server (which
  checks friendly fire / match state). Headshots are decided by `Hitbox` triggers.
- **Head hitboxes**: the CharacterController capsule stops at the shoulders;
  the head sphere has a *trigger* collider (never blocks movement) marked with
  `Hitbox.isHead`. Weapon raycasts include triggers and check the marker.
- **Hit detection is client-side** (simple, standard for indie MP). For
  competitive play, move the raycast into a ServerRpc taking origin+direction.
- **Add a game mode:** extend `GameMode`, its limits in `GameModeManager`, and a
  button in `MainMenu`.
- **Add a weapon:** a `WeaponDefinition` entry + optionally a model in
  `WeaponModelBuilder`.
- **Internet matchmaking:** install `com.unity.services.relay`, create a UGS
  project, swap `SetConnectionData` in `MainMenu` for a Relay allocation.
- **Real art later:** all visuals/audio are generated behind small builder/synth
  classes — swap `HumanoidBuilder`/`WeaponModelBuilder`/`SfxSynth` outputs for
  real assets without touching gameplay logic.
