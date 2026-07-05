# SUNDOWN ARENA — 3D FPS Base (Unity)

An asset-free online first-person shooter base: 4 game modes on 3 maps with a
7-weapon arsenal, kill cam, scoreboard, crouch/slide/bunny-hop movement and a
stylized golden-hour look with custom post-processing — everything generated
from code. Rename the game in `GameSettings.GameTitle`.

> The original minimal version lives in **`3D game base v1 (simple)`**.

## Features

- **Modes:** 1v1 Duel · Team Deathmatch · Free-For-All · **Gun Game** (race
  through all 7 weapons, knife kill wins) — plus a **Snipers Only** toggle for
  any mode
- **Maps:** Arena · **Nuketown-style** (two houses, street, bus) ·
  **Backrooms** (yellow maze, humming light panels) — picked when hosting,
  synced to everyone
- **Weapons (1–7):** Knife · Pistol · SMG · Shotgun (8 pellets) · Rifle ·
  Sniper (one-shot, scope) · **RPG** (real rocket, splash damage, rocket
  jumps) — **aim-down-sights on right-click** for all, one-shot **headshots**,
  reload animation + bar, fresh ammo every respawn
- **Movement:** sprint, crouch (Ctrl), **slide** (sprint+Ctrl), **bunny hop**
  (hold Space), synced to other players
- **Kill cam:** on death you spectate your killer; at match end everyone
  watches the final killer (live spectate, BO2-style presentation)
- **Scoreboard (hold Tab):** K/D, team colors, Gun Game levels
- **Team switching** from the pause menu (server keeps teams balanced)
- **HUD:** big panel-based ammo/health blocks, low-HP vignette, kill feed
  top-right, hit markers (red = headshot)
- **Full-body hitboxes:** head (lethal), neck, torso, arms, hands, legs, feet
- Layered synthesized gunshots per weapon, explosions, join-wait window with
  cancel/timeout, floating nametags, humanoid characters with walk animation

## Setup (after pulling)

1. Open the project, let it compile.
2. **Re-run `Tools > FPS Base > Setup Multiplayer (Scene + Player Prefab)`** —
   rebuilds the player prefab and menu scene with the new components.
3. Play the Multiplayer scene (or Build And Run) → PLAY → pick map/mode →
   HOST GAME. Second player joins via IP (LAN) or `127.0.0.1` (same PC).

## Controls

| Input | Action |
|---|---|
| WASD / Shift / Space (hold) | Move / sprint / jump & bunny hop |
| Left Ctrl (or C) | Crouch — while sprinting: **slide** |
| Left Mouse / Right Mouse | Shoot / aim (sniper scopes) |
| 1–7 or scroll | Weapons |
| R | Reload |
| Tab (hold) | Scoreboard |
| Escape | Pause (resume · switch team · settings · leave) |

## Playing over Steam (no port forwarding)

The menu's LOCAL/LAN section works out of the box. Steam play is scaffolded
in `SteamLobbyService.cs` behind the `FPSBASE_STEAM` define:

1. Package Manager → **Add package from git URL**:
   `https://github.com/rlabrecque/Steamworks.NET.git?path=/com.rlabrecque.steamworks.net`
2. Add the community **Steam transport for Netcode**:
   `https://github.com/Unity-Technologies/multiplayer-community-contributions.git?path=/Transports/com.community.netcode.transport.steamworks`
3. `Project Settings > Player > Scripting Define Symbols` → add `FPSBASE_STEAM`.
4. In the Multiplayer scene, add the `SteamManager` component (ships with
   Steamworks.NET) and a `SteamNetworkingSocketsTransport` next to the
   NetworkManager. A `steam_appid.txt` containing `480` (Valve's test app)
   in the project root lets you test while Steam is running.
5. The menu's STEAM section activates: hosting creates a friends-only lobby;
   friends join via **Steam overlay → Join Game**. Steam relays the traffic —
   no IPs or router setup.

For non-Steam internet play, Unity **Relay + Lobby** is the alternative
(swap `SetConnectionData` in `MainMenu` for a Relay allocation).

## Shipping updates (not full rebuilds)

You always produce a build, but players should only **download the diff**:

- **Steam (recommended, pairs with the above):** upload each build as a depot
  via SteamPipe — Steam computes binary deltas and auto-updates players.
  Typical code-only update ≈ a few MB, not the whole game.
- **itch.io:** `butler push <build-folder> you/sundown-arena:windows` —
  butler uploads only changed blocks; the itch app auto-patches players.
- **Self-hosted:** keep builds in GitHub Releases and point
  `GameSettings.UpdateCheckUrl` at a raw `version.txt`; the main menu then
  shows "update available" (players re-download; simplest, no delta).

Also note: Unity builds are deterministic-ish per platform — keeping
`Library` intact between builds makes rebuilds fast; only ship the Build
folder output.

## Project structure (key files)

```
Assets/Scripts/
  Core/      GameSettings (title/version/update URL) · EnvironmentBuilder (maps)
             HumanoidBuilder (body + hitboxes) · PlayerFactory · PostFx · UpdateChecker
  Player/    PlayerMovement (crouch/slide/bhop) · MouseLook · LimbAnimator · DeathCam
  Weapons/   WeaponDefinition (balance!) · WeaponModelBuilder · WeaponController
             RocketProjectile
  Combat/    Health · NetworkHealth · Hitbox · Effects · TargetDummy
  Audio/     SfxSynth (all sounds, synthesized)
  UI/        HudOverlay · MenuWidgets · Nametag · OfflineMenu
  Network/   GameModeManager (modes/scores/maps) · NetworkPlayer · NetworkWeapon
             MainMenu · NetworkGameHud · MultiplayerBootstrap · SteamLobbyService
```

## Extending

- **Balance:** everything lives in `WeaponDefinition.CreateDefaultLoadout()`.
- **New mode:** extend `GameMode`, add scoring in `GameModeManager.ReportKill`,
  a button in `MainMenu`.
- **New map:** add a builder + name in `EnvironmentBuilder` (`MapNames`,
  `BuildMap`, `GetSpawnPoint`).
- **Real art:** swap the outputs of `HumanoidBuilder` / `WeaponModelBuilder` /
  `SfxSynth` for imported assets (free sources: Mixamo, Kenney, Synty,
  polyhaven.com, poly.pizza — all game-ready).
- Hit detection is client-side (standard indie approach); for competitive play
  move the raycast into a ServerRpc.
