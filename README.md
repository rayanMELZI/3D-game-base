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

## Making your own map (no code)

The maps are now designer-friendly — build them by dragging objects in the
Unity editor:

1. **Tools > FPS Base > New Map** → name it → it creates a prefab in
   `Assets/Resources/Maps/` and opens it in **Prefab Mode** (an isolated
   editing view).
2. **Build the level.** Add objects with `GameObject > 3D Object > Cube`
   (etc.) — they must have **colliders**, and Unity primitives already do.
   You can drag in imported models/props too; just make sure they have a
   collider (add a Mesh/Box Collider component).
3. **Place spawns.** The starter map has colored `SpawnPoint` markers —
   blue = team 0, orange = team 1, green = anyone (FFA/Gun Game). Move them
   where players should appear. Duplicate them for more spawns. (Their gizmo
   arrow shows the facing, but the game overrides facing to look at the map
   center anyway.)
4. **Press Ctrl+S.** Done — the map now appears in the in-game **MAP** button
   automatically (maps are sorted by name, so all players see the same list).

Notes:
- The map's root object is instantiated at the world origin, so build around
  `(0,0,0)`. Team 0 conventionally spawns toward −Z, team 1 toward +Z.
- If you place no SpawnPoints, the game falls back to a safe default ring.
- The two built-in maps (Arena, Backrooms) are still generated from code in
  `EnvironmentBuilder` as examples.

## Playing over Steam (no port forwarding)

The menu's LOCAL/LAN section works out of the box. Steam play is scaffolded in
`SteamLobbyService.cs`, disabled until you add the packages and the
`FPSBASE_STEAM` define. Full steps:

1. **Steamworks.NET** (Steam API wrapper) — Package Manager → **+** → *Add
   package from git URL*:
   ```
   https://github.com/rlabrecque/Steamworks.NET.git?path=/com.rlabrecque.steamworks.net
   ```
   *(You already did this step.)*
2. **The Steam transport for Netcode** — same *Add package from git URL*. The
   correct package (the one that uses Steamworks.NET) is
   `steamnetworkingsockets` — the earlier `...transport.steamworks` name was
   wrong and 404s. Use exactly:
   ```
   https://github.com/Unity-Technologies/multiplayer-community-contributions.git?path=/Transports/com.community.netcode.transport.steamnetworkingsockets
   ```
   If Package Manager rejects the URL, the alternative is to download that repo
   and copy the `com.community.netcode.transport.steamnetworkingsockets` folder
   into your project's `Packages/` folder (embedded package) — same result.
3. **Enable the code** — `Project Settings > Player > Scripting Define Symbols`
   → add `FPSBASE_STEAM` (semicolon-separated if there are others) → Apply.
   This is what turns on everything inside `SteamLobbyService.cs` and the
   STEAM button in the menu.
4. **Scene setup** — in `Assets/Scenes/Multiplayer.unity`, select the
   `NetworkManager` object and:
   - Add the **`SteamManager`** component. **It's included in this project**
     (`Assets/Scripts/Network/SteamManager.cs`, active once the define from
     step 3 is set) — do NOT import the "SteamManager" sample from the
     Steamworks.NET package; it isn't needed and would be a duplicate.
   - Add a **`SteamNetworkingSocketsTransport`** component (from the package in
     step 2). Leave the NetworkManager's *Network Transport* field on
     UnityTransport — LAN keeps working, and the code switches to the Steam
     transport automatically when hosting or joining via Steam.

   **Can't find the components in Add Component?** They only appear once the
   project compiles with **zero errors** (check the Console). Any compile error
   hides ALL new components, including the ones from the packages. Fix the
   errors (and make sure step 3's define is applied), let Unity finish
   compiling, then search again.
5. **App ID for testing** — create a text file `steam_appid.txt` containing just
   `480` in the project root (and next to the built `.exe`). 480 is Valve's
   public test app "Spacewar". Steam must be **running and logged in**. Ship
   with your own App ID once you have one from Steamworks.
6. **Play** — the menu's STEAM section now works: **HOST STEAM GAME** creates a
   friends-only lobby; a friend opens the **Steam overlay (Shift+Tab) → your
   name → Join Game**. Steam's relay handles NAT/firewall, so no IPs or port
   forwarding. (This is peer-to-peer with the host as server — "Steam servers"
   meaning Steam's relay, not a dedicated server you rent.)

If any Steam type names differ in your package version, the two things the code
needs are: the included `SteamManager` (just checks `SteamAPI.Init`) and a
transport component with a `ConnectToSteamID` field — the latter is what the
community transport package provides.

For non-Steam internet play, Unity **Relay + Lobby** is the alternative (swap
`SetConnectionData` in `MainMenu` for a Relay allocation).

## Shipping updates (so players don't redownload the whole game)

You always produce a full build in Unity (`File > Build Settings > Build`), but
the **distribution platform** is what patches players with only the changed
bytes. You don't do the diffing yourself. Pick one:

**Steam (best if you're already using Steam play):**
1. Get a Steamworks account + App ID (one-time, ~$100 to publish an app).
2. Download the **Steamworks SDK**, which includes **SteamPipe** / `steamcmd`.
3. Write a small app-build + depot-build script (`.vdf` files — templates are
   in the SDK) pointing at your Unity `Build/` folder.
4. Run `steamcmd +run_app_build yourapp.vdf`. Steam uploads, and for each
   later build it computes a **binary delta** — players auto-update, usually
   downloading only a few MB for a code change.

**itch.io (simplest, free):**
1. Install **butler** (itch's CLI): `itch.io/docs/butler`.
2. `butler push "Build/" yourname/your-game:windows` after each build.
3. butler uploads only changed blocks; the itch.io app auto-patches players.
   No app ID, no cost.

**Self-hosted / GitHub Releases (no delta, simplest to set up):**
- Upload each build as a GitHub Release zip. Set
  `GameSettings.UpdateCheckUrl` to a raw text file holding the latest version
  (e.g. `https://raw.githubusercontent.com/you/repo/main/version.txt`). The
  main menu already reads it and shows "update available" when it differs from
  `GameSettings.Version`. Players download the new zip manually (full download,
  no patching).

Rule of thumb: **Steam or itch = automatic delta patching; GitHub = manual full
download with an in-game "update available" notice.** In all cases you only
distribute the contents of the Unity `Build/` output folder, never the whole
Unity project.

**Deep dive:** [docs/STEAM_DISTRIBUTION.md](docs/STEAM_DISTRIBUTION.md) — what
"Steam servers" really are (relay, not rented servers), the full SteamPipe
update workflow with example `.vdf` scripts, beta branches, and how to build
for **Windows + macOS** (including the signing/notarization truth for Steam).

## Project structure (key files)

```
Assets/Scripts/
  Core/      GameSettings (title/version/update URL) · MapCatalog (map list)
             EnvironmentBuilder (built-in maps) · SpawnPoint · HumanoidBuilder
             PlayerFactory · PostFx · UpdateChecker
  Player/    PlayerMovement (crouch/slide/bhop) · MouseLook · LimbAnimator · DeathCam
  Weapons/   WeaponDefinition (balance!) · WeaponModelBuilder · WeaponController
             RocketProjectile
  Combat/    Health · NetworkHealth · Hitbox · Effects · TargetDummy
  Audio/     SfxSynth (all sounds, synthesized)
  UI/        HudOverlay · MenuWidgets · Nametag · OfflineMenu
  Network/   GameModeManager (modes/scores/maps/spawns) · NetworkPlayer · NetworkWeapon
             MainMenu · NetworkGameHud · MultiplayerBootstrap · SteamLobbyService
Assets/Editor/  MultiplayerSetupTool · NewMapTool (Tools > FPS Base menu)
Assets/Resources/Maps/  your custom map prefabs (auto-listed in-game)
```

## Extending

- **Balance:** everything lives in `WeaponDefinition.CreateDefaultLoadout()`.
- **New mode:** extend `GameMode`, add scoring in `GameModeManager.ReportKill`,
  a button in `MainMenu`.
- **New map:** just use **Tools > FPS Base > New Map** (see "Making your own
  map" above). Built-in maps are code in `EnvironmentBuilder`; the list is
  assembled in `MapCatalog`.
- **Real art:** swap the outputs of `HumanoidBuilder` / `WeaponModelBuilder` /
  `SfxSynth` for imported assets (free sources: Mixamo, Kenney, Synty,
  polyhaven.com, poly.pizza — all game-ready).
- Hit detection is client-side (standard indie approach); for competitive play
  move the raycast into a ServerRpc.
