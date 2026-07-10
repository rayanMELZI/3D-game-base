# SUNDOWN ARENA — AI collaborator guide

Online FPS base in Unity 6000.x, C#, Netcode for GameObjects, built to be
reused as the foundation for multiple games. Owner: Rayan (student). This file
is the onboarding brief for AI assistants working on the repo.

## Ground rules

- **Versioning**: `GameSettings.Version` is `0.MINOR.PATCH` — bump MINOR for
  features, PATCH for fixes, on every change batch. Git tags mirror it
  (`v0.5.0`). Keep commits atomic (one logical change per commit), messages in
  `Category: description` style. Don't push unless asked.
- **Honesty about testing**: nothing here can be verified without opening
  Unity — say clearly what was not play-tested.
- **A single compile error silently freezes everything**: Unity keeps running
  the last-good assembly, so features "don't exist" until the Console is clean.
  Always check for compile errors first when something "isn't implemented".
- The user makes his own maps and runs Unity/git himself.

## Architecture (Assets/Scripts/)

- **Core/**: `GameSettings` (persisted settings + weapon classes + version) ·
  `MapCatalog` (map list: built-ins + `Resources/Maps` prefabs, synced by index)
  · `EnvironmentBuilder` (code-built maps: Arena, Backrooms, + Nuketown builder
  used by the bake tool) · `PlayerFactory`/`PlayerRigRefs` (player rig assembly
  + imported character skins) · `HumanoidBuilder` (primitive body, now hidden
  under skins but its colliders ARE the hitboxes) · `SpawnPoint` (custom map
  spawn markers).
- **Player/**: `PlayerMovement` (CharacterController: sprint/crouch/slide) ·
  `MouseLook` (`CurrentPitch` is replicated for aim) · `LimbAnimator` (legacy,
  disabled when a skin is applied) · `CharacterAnimatorDriver` (drives Speed on
  the skin's Animator from real movement) · `CharacterAimPose` (IK: hands onto
  the gun, look-at aim pitch, slide lean; remote players fed by NetworkPlayer)
  · `DeathCam` (killcam replay from buffered killer poses + orbit spectate).
- **Weapons/**: `WeaponDefinition` (data table, `CreateDefaultLoadout()` is THE
  arsenal; index order matters — Gun Game ladder + network weapon index depend
  on it) · `WeaponController` (fire/reload/ADS/switch; `classSlots` restricts
  switching to the selected class) · `WeaponModelBuilder` (imported models from
  `Resources/Weapons` with per-weapon `FitFor()` tuning table; procedural
  fallback; knife always procedural) · `RocketProjectile` (flies a mini
  launcher as the gag).
- **Network/**: `NetworkPlayer` (owner setup, team colors, death/respawn +
  3s spawn protection, killcam ring buffer, replicated `Pitch`/`Sliding`/
  `Crouched`) · `NetworkWeapon` (third-person weapon + shot replication +
  `LastShotTime` for radar) · `NetworkHealth` (server-authoritative HP;
  headshots lethal) · `GameModeManager` (modes, scores, spawn picking,
  `RadarMode`) · `MainMenu` (IMGUI menus incl. CLASSES screen) ·
  `MultiplayerBootstrap` (map build + menu camera) · `SteamLobbyService` +
  `SteamManager` (behind `FPSBASE_STEAM` define).
- **UI/**: `HudOverlay` (crosshair/ammo/health/radar; hidden during killcam) ·
  `NetworkGameHud` (scores, kill feed, scoreboard) · `MenuWidgets` (IMGUI
  styles) · `Nametag`.
- **Editor/**: `MultiplayerSetupTool` (bakes the NetworkPlayer prefab — must be
  re-run after changing the rig structure) · `NewMapTool` (starter custom map)
  · `BakeMapTool` (bake a code map into an editable prefab with real material
  assets + spawn markers).

## Key invariants / gotchas

- **Hitboxes never change with visuals**: imported character skins are overlaid
  and primitives hidden — their colliders (incl. the lethal head trigger) stay.
- **Weapon indexes are global**: knife 0, pistol 1, smg 2, shotgun 3, rifle 4,
  sniper 5, rpg 6. Classes restrict *switching*, never reorder.
- **Code-generated materials can't be saved in prefabs/scenes** — play-mode
  copies go pink. `BakeMapTool` saves material assets properly; `MapCatalog`
  gray-fallbacks missing ones at runtime.
- **Custom maps** live in `Assets/Resources/Maps/` (prefab name → menu name,
  needs `SpawnPoint` children or falls back to a spawn ring). Nuketown is one
  of these now (`Nuketown.prefab`), not a built-in.
- **Spawns**: server snaps candidates to ground from 1.5m up (higher raycasts
  land on roofs/ceilings), picks farthest-from-enemies, respawns everyone on
  map change, and gives 3s spawn protection that ends on the victim's first shot.
- **Legacy Input Manager**: `Fire1` also maps Left Ctrl — never use it for
  firing (crouch would shoot); read mouse buttons directly.
- **Steam**: everything behind `FPSBASE_STEAM`. Our own `SteamManager` is in
  the project (do NOT import the Steamworks.NET sample — duplicate class).
  Host AND join must switch Netcode onto `SteamNetworkingSocketsTransport`
  (`UseSteamTransport()`); `steam_appid.txt` (480 for dev) must sit next to the
  exe on EVERY machine, including friends' — if someone's Steam doesn't show
  "Spacewar" while the game runs, their Steam API never initialized.
- Imported asset packs: two low-poly weapon packs (incl. `*_PreSet` prefabs
  with attachments mounted, and separate attachment prefabs: optics, muzzles,
  grips, mags), Survivalist characters (4 skins, same mesh/size), Free Crawl
  Animation (Generic rig — must be re-imported as Humanoid to retarget; crawl
  feature not built yet).

## Docs

- **`ROADMAP.md` — READ FIRST: the user's complete feature list + batch order.
  Nothing on it may be dropped; only the user checks items off.**
- `README.md` — setup, controls, maps, Steam setup, update shipping.
- `docs/STEAM_DISTRIBUTION.md` — SteamPipe/depots/branches, Win+macOS builds.
