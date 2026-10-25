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
  · `EnvironmentBuilder` (code-built maps: Arena, Backrooms, SKYFREIGHT plane
  interior, + Nuketown builder used by the bake tool) · `SkyfreightFlight`
  (on the plane's map root: scrolling clouds, turbulence via
  `MouseLook.MapShake`, owner reports falling out → server suicide, fog
  restored on unload) · `PlayerFactory`/`PlayerRigRefs` (player rig assembly
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
  fallback; knife and grenade launcher always procedural) · `RocketProjectile`
  (flies a mini launcher as the gag; shooter-simulated, others see a predicted
  explosion) · `GrenadeProjectile` (grenade launcher, `projectileGravity > 0`:
  NetworkWeapon has the SERVER simulate the damaging round, every client flies
  a visual replica). `WeaponDefinition.mobility`/`adsSpeed` make heavy weapons
  (LMG) slower to move and aim. Shotgun reloads are shell by shell
  (`WeaponController.AdvanceShellReload`; `ReloadCue` events → NetworkWeapon
  replays reload sounds positionally for other players).
- **Network/**: `NetworkPlayer` (owner setup, team colors, death/respawn +
  3s spawn protection, killcam ring buffer, replicated `Pitch`/`Sliding`/
  `Crouched`) · `NetworkWeapon` (third-person weapon + shot replication +
  `LastShotTime` for radar) · `NetworkHealth` (server-authoritative HP;
  headshots lethal; sends the victim a hit-direction source: attacker, blast
  point via `TakeDamageFrom`, or bot/zombie → `HudOverlay.NotifyDamage`) · `GameModeManager` (modes, scores, spawn picking,
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
  · `PreviewRenderer` (Tools > FPS Base > Render Preview Sheets: PNG sheets of
  every weapon bare/with all add-ons, first-person hip + ADS views on the
  Arena, 3 shots per map, an effects sheet (impacts, decals, explosion,
  muzzle flash), plus `bounds.txt` with every part's bounds).
- **Rendering**: built-in pipeline, **linear colour space**. `Core/PostFx`
  (`SundownPostEffect` + `Shaders/SundownPost.shader`) does AO from the depth
  texture (Medium quality and up), two-level bloom, ACES + split-tone grade,
  vignette and FXAA (only when MSAA is off). Code-built maps use
  `Sundown/WorldSurface`. `Combat/Effects` draws impacts/muzzle flashes/
  explosions from shared world-space particle systems (`Sundown/Particle`,
  always-included shader) and keeps a ring of 96 decals parented to `MapRoot`;
  remote shots get their impact from `WeaponController.SpawnRemoteImpact`.
- **Visual checks without Unity**: push to any `render/**` branch →
  `.github/workflows/render-preview.yml` runs PreviewRenderer in batch mode and
  force-pushes the PNGs (or `FAILED.txt` + `compile-errors.txt`) to the
  `render-output` branch. Clone that branch and look at the images before
  calling visual work done. Edit-mode renders skip runtime-only scripts
  (NIGHTFALL darkness, SKYFREIGHT clouds).

## Key invariants / gotchas

- **Hitboxes never change with visuals**: imported character skins are overlaid
  and primitives hidden — their colliders (incl. the lethal head trigger) stay.
  The skin is parented to the player root, NOT inside `Body`: crouch/prone pose
  the `Body` (`PlayerMovement.ApplyCrouchVisual`: squash for crouch, lying
  full-length for prone) and the capsule height, for the owner AND on remote
  proxies (NetworkPlayer, from `Crouched`/`Prone`) — the shooter's client hits
  the proxy. Torso/hips triggers are added at runtime
  (`HumanoidBuilder.EnsureTorsoHitboxes`) for the prone torso past the capsule.
- **Weapon indexes are global**: knife 0, pistol 1, smg 2, shotgun 3, rifle 4,
  sniper 5, rpg 6, lmg 7, grenade launcher 8, then 9–30 = the pack arsenal
  (one weapon per unused pack model, `WeaponDefinition.modelName` +
  `WeaponModelBuilder.FitForModel`). Classes restrict *switching*, never
  reorder. CLASSES option lists are derived from weapon types, so appended
  weapons appear automatically.
- **Code-generated materials can't be saved in prefabs/scenes** — play-mode
  copies go pink. `BakeMapTool` saves material assets properly; `MapCatalog`
  gray-fallbacks missing ones at runtime.
- **Zombie survival** is networked: `ZombieDirector` (server-only, on the
  GameModeManager object) spawns `Assets/Prefabs/NetworkZombie.prefab`
  (NetworkObject + `ZombieNetworkTransform` + `ZombieAgent`, registered in
  `DefaultNetworkPrefabs.asset`) in waves at `MapCatalog.ZombieSpawnCandidates`
  far from survivors. `ZombieAgent`: server movement/attacks/HP, shots reach it
  through `IDamageable` → `DamageRpc`; visuals (tinted Survivalist skin, arms
  out, red eyes) built on every peer. Co-op (`AreSameTeam` true), the fallen
  stay down until the next wave, everyone down = `ServerZombieGameOver`.
  Wave/remaining/countdown live in GameModeManager NetworkVariables, points in
  `NetworkPlayer.Score`. The mode forces NIGHTFALL (`BuildNightfall`, lighting
  by `NightfallAtmosphere`, restored when the map unloads).
- **Bots** are networked: `BotDirector` (server-only, on the GameModeManager
  object) keeps `GameModeManager.BotCount` bots (host lobby setting 0–7,
  PlayerPrefs `botCount`, capped by free player slots; none in the lobby,
  Zombie Survival or P Story) by spawning `Assets/Prefabs/NetworkBot.prefab`
  (NetworkObject + `BotNetworkTransform` + `BotAgent`, in
  `DefaultNetworkPrefabs.asset`), and balances team modes. `BotAgent`: body
  from `Core/BotRig` (player capsule + humanoid hitboxes + skin) built on every
  peer, server-only AI (LOS, reaction time, aim error that settles while
  tracking, bursts, hitscan resolved on the server, straight delayed splash for
  RPG/GL), paths on a NavMesh baked at runtime per map from the map colliders
  (`BotNavigation`, fallback steering). Bots stand in for client ids with
  `BotAgent.BotId` (`IdBase + NetworkObjectId`): `GameModeManager.TeamOf`/
  `NameOf`/`BotOf`/`CombatantTransform`, `ReportKill` → `ReportBotKill`,
  `NetworkHealth.ServerBotDamage`. Bots get no XP; a bot can win FFA/Gun Game.
- **Custom maps** live in `Assets/Resources/Maps/` (prefab name → menu name,
  needs `SpawnPoint` children or falls back to a spawn ring). Nuketown is one
  of these now (`Nuketown.prefab`), not a built-in.
- **Spawns**: server snaps candidates to ground from 1.5m up (higher raycasts
  land on roofs/ceilings), picks farthest-from-enemies, respawns everyone on
  map change, and gives 3s spawn protection that ends on the victim's first shot.
- **XP / levels**: the server decides (kill 100, headshot 140 in `ReportKill`;
  win 500 / match complete 150 in `EndMatch`, which returns early if the match
  already ended) and sends `NetworkPlayer.AwardXpClientRpc` to the owner only.
  The owner saves it (`GameSettings.AwardExperience`, PlayerPrefs `experience`)
  and writes the owner-permission `CareerLevel` NetworkVariable that the lobby
  and scoreboard read. Curve: level n at 100 × (n-1)² XP.
- **Add-on models are fitted, not hand-placed**: `WeaponModelBuilder.AddAttachments`
  measures each model's mesh (weapon FBXs have Read/Write on for this): optic
  on the receiver top over the trigger with iron sights (`*_sight`, `ironsight`,
  `_Optic_`) folded, suppressor on the measured barrel tip/bore, foregrip under
  the handguard (skipped when there is none), extended mag = the gun's own
  magazine stretched (none on tube shotguns/launcher). Per-model tweaks:
  `Nudge`. With an optic, ADS aims through the red dot
  (`WeaponModelInstance.opticSight`, `WeaponController.AdsPosition`).
- **Add-ons are per class**: `GameSettings.ClassAttachments[class][weapon]`
  (prefs `class{c}Att{i}`, migrated from the old global `weaponAtt{i}`).
  Full-arsenal mode uses the selected class's add-ons. `WeaponController`
  snapshots them on spawn (`RefreshAttachments`); `NetworkWeapon` publishes
  `CurrentAttachmentMask`, never re-reads GameSettings, so remotes see exactly
  what the owner wears.
- **Legacy Input Manager**: `Fire1` also maps Left Ctrl — never use it for
  firing (crouch would shoot); read mouse buttons directly. Controller input
  goes through `Core/GamepadInput` (Xbox layout, Windows numbering): triggers
  and D-pad are axes in `InputManager.asset` (9th/10th, 6th/7th) with press
  edges tracked once per frame; right-stick look is rate-based (deg/s ×
  `GameSettings.ControllerSensitivity`, radial dead zone in code).
- **Steam**: everything behind `FPSBASE_STEAM`. Our own `SteamManager` is in
  the project (do NOT import the Steamworks.NET sample — duplicate class).
  Host AND join must switch Netcode onto `SteamNetworkingSocketsTransport`
  (`UseSteamTransport()`); `steam_appid.txt` (480 for dev) must sit next to the
  exe on EVERY machine, including friends' — if someone's Steam doesn't show
  "Spacewar" while the game runs, their Steam API never initialized.
- Imported asset packs: two low-poly weapon packs (incl. `*_PreSet` prefabs
  with attachments mounted, and separate attachment prefabs: optics, muzzles,
  grips, mags), Survivalist characters (4 skins, same mesh/size), Free Crawl
  Animation (Humanoid: the pack mesh `VoxelVision_Default_Charcter.fbx` creates
  the avatar from an explicit UE5-skeleton bone map written in its .meta; the
  clips copy that avatar). Prone uses its idle + forward loop in the
  `Prone locomotion` state of `SurvivalistLocomotion.controller` (`Prone` bool,
  MoveX/MoveY blend, backward = loop at time scale -1); the driver fades the
  upper-body layer out while prone and `CharacterAimPose` lowers the aim pivot.

## Docs

- **`ROADMAP.md` — READ FIRST: the user's complete feature list + batch order.
  Nothing on it may be dropped; only the user checks items off.**
- `README.md` — setup, controls, maps, Steam setup, update shipping.
- `docs/STEAM_DISTRIBUTION.md` — SteamPipe/depots/branches, Win+macOS builds.
