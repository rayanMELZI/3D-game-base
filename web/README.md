# SUNDOWN ARENA — web version

The browser port of the game: same arsenal (31 weapons, same global indexes
and stats), same built-in maps, same modes, bots, zombie waves, classes with
add-ons, throwables, XP/levels and procedural sounds. Three.js for rendering,
a zero-dependency Node server for online rooms. Nothing to install besides
Node ≥ 18.

```bash
cd web
npm start            # http://localhost:8080  (PORT=xxxx to change)
npm run check        # headless checks: maps, nav, simulated matches, server
```

- **Play offline**: the match runs inside the page (solo vs bots, zombies).
  `public/` alone is a static site, so offline play also works from any
  static host (itch.io HTML upload, GitHub Pages…); online play needs the
  Node server.
- **Online**: *HOST A MATCH* creates a room with a 4-letter code; friends
  type it (or click it in the room list) to join the lobby. The host sets
  mode/map/bots/radar/weapons rule there, everyone sees it live, then START.
- Deep link for quick tests: `/?solo=MODE,MAP,BOTS` (e.g. `/?solo=4,5,0`
  = Zombie Survival on NIGHTFALL).

## Layout

| Path | What it is | Unity source it follows |
|---|---|---|
| `public/shared/weapons.js` | Arsenal table, add-on rules, throwables | `WeaponDefinition`, `WeaponAttachments` |
| `public/shared/maps.js` | Built-in maps as data + spawns | `EnvironmentBuilder`, `MapCatalog` |
| `public/shared/physics.js` | OBB/capsule world, character mover, raycasts, hitboxes (lethal head) | CharacterController + `HumanoidBuilder` colliders |
| `public/shared/nav.js` | Walkable grid + A* | `BotNavigation` |
| `public/shared/match.js` | Authoritative match: modes, scores, spawns, damage, projectiles, waves, XP | `GameModeManager`, `NetworkHealth`, `ZombieDirector`, `BotDirector` |
| `public/shared/ai.js` | Bot and zombie brains | `BotAgent`, `ZombieAgent` |
| `public/client/*` | Renderer, local player, weapons/viewmodel, HUD, menus, sounds, transports | `PlayerMovement`, `WeaponController`, `HudOverlay`, `MainMenu`, `SfxSynth`… |
| `server.js` | Static files + WebSocket rooms (each runs a `Match` at 30 Hz) | Netcode host |

`shared/` runs both in the browser and in Node, so offline and online play
use exactly the same rules. Movement is client-authoritative and hits are
found by the shooter's client (as in the Unity build); damage, deaths,
scores, projectiles, bots and zombies are decided by the match.

Coordinates: map data is written in Unity coordinates (copied from the C#)
and converted once (`z` negated, Euler angles `(-x, -y, z)`, order `YXZ`).

## Not in the web version (yet)

- Imported art: weapon/character packs, crawl animation and imported sounds
  are replaced by procedural models and the synthesized `SfxSynth` sounds.
- Custom map prefabs from `Assets/Resources/Maps` and the P STORY island
  mode (prefab content can't be read outside Unity).
- Killcam replay (a death cam looks at the killer instead), Steam lobbies,
  gun colour skins.
- Post effects (AO, bloom): ACES tone mapping + fog + shadows only.
