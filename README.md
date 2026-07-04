# 3D FPS Base (Unity)

A minimal, dependency-free first-person shooter base project, meant to be cloned/copied
as the starting point for future games.

**What's in the box**
- First-person player: WASD movement, sprint, jump, mouse look, cursor lock
- Simple capsule characters (blue player, red target dummies that die and respawn)
- Simple hitscan gun: automatic fire, ammo + reload, recoil, muzzle flash, tracers, hit markers
- Flat ground arena with walls and a few crates
- Generic `Health` component reusable for players, enemies, destructibles
- Minimal HUD (crosshair, ammo, health bar) with zero asset dependencies

Everything (level, player, gun, HUD) is **built from code** by one script
(`GameBootstrap`) using Unity primitives — no models, prefabs, materials or
packages required. That keeps the base portable and easy to reshape per game.

---

## Getting started

1. Install **Unity Hub**: https://unity.com/download
2. In the Hub, install any recent editor — **Unity 6 (6000.x) LTS recommended**
   (anything from 2021.3 LTS up works).
3. In the Hub: **Add → Add project from disk** → select this folder.
4. If the Hub warns the exact editor version isn't installed, just pick the one
   you installed — Unity will upgrade the project automatically.
5. Open the project (first import takes a couple of minutes), open
   `Assets/Scenes/Main.unity` if it isn't open already, and press **Play**.

> The scene looks empty in the editor — it only contains the `GameBootstrap`
> object. The whole level is generated the moment you press Play.

## Controls

| Input | Action |
|---|---|
| Mouse | Look |
| WASD | Move |
| Left Shift | Sprint |
| Space | Jump |
| Left Mouse | Shoot (hold for auto) |
| R | Reload |
| Escape | Release cursor (click to re-lock) |

## Project structure

```
Assets/
  Editor/FpsBaseMenu.cs          # Tools > FPS Base > Add Game Bootstrap (for new scenes)
  Scenes/Main.unity              # contains only the GameBootstrap object
  Scripts/
    Core/GameBootstrap.cs        # builds the whole level + player at runtime
    Player/PlayerMovement.cs     # CharacterController movement (walk/sprint/jump/gravity)
    Player/MouseLook.cs          # camera pitch + body yaw, cursor lock, recoil recovery
    Weapons/Gun.cs               # raycast shooting, ammo/reload, effects
    Combat/Health.cs             # generic damage/death component
    Combat/TargetDummy.cs        # respawning practice targets
    UI/HudOverlay.cs             # IMGUI crosshair / ammo / health (no assets needed)
```

## How to build on this base

- **Real level instead of the generated arena:** design your level in the scene,
  then delete the environment parts of `GameBootstrap` (or the whole bootstrap)
  and place the player as a prefab. The player/gun/health scripts don't care
  how the scene was made.
- **New weapons:** duplicate `Gun.cs` fields into variants (damage, fire rate,
  magazine) or turn them into a `ScriptableObject` weapon config.
- **Enemies:** put `Health` on anything; subscribe to `Health.OnDeath` for
  loot, score, ragdolls, etc. `TargetDummy` shows the pattern.
- **Real UI:** replace `HudOverlay` (IMGUI) with uGUI or UI Toolkit.

## Making it online (multiplayer)

Networking is intentionally **not** wired in yet — the code is kept
single-responsibility so it's easy to add. Recommended path with Unity's
official **Netcode for GameObjects (NGO)**:

1. `Window > Package Manager` → install **Netcode for GameObjects**
   (`com.unity.netcode.gameobjects`).
2. Turn the player into a prefab with a `NetworkObject` component, and register
   it as the *Player Prefab* on a `NetworkManager` in the scene.
3. In `PlayerMovement`, `MouseLook`, and `Gun`, inherit from `NetworkBehaviour`
   and early-out when `!IsOwner` (so you only control *your* player), and
   disable the camera/audio listener on non-owned players.
4. Send shots to the server with a `[ServerRpc]` in `Gun.Shoot()`; the server
   does the raycast and applies damage; replicate health with a
   `NetworkVariable<float>` in `Health`.
5. Add a `NetworkTransform` (or client network transform) to the player prefab
   so movement replicates.
6. Test locally: build the game, run the build as host, and connect the editor
   as client (NGO's `NetworkManager` inspector has Start Host / Start Client
   buttons for quick testing).

Alternative: **Mirror** (free, popular on the Asset Store) or **Photon Fusion**
(hosted relay, easiest way to get internet play without your own servers).
