# SUNDOWN ARENA — Roadmap

The complete feature list, as requested by Rayan. **Nothing on this list may be
forgotten.** AI collaborators: this file + `CLAUDE.md` are the source of truth;
check items off (move to Shipped) only when the user confirms they work.

## Batch order (user-chosen)

1. **Weapon add-ons / attachments** ← NEXT, already specced (see below)
2. **Feedback & polish pack**
3. Remaining batches in whatever order the user picks next

---

## 1) Weapon add-ons (NEXT — user said the packs contain the attachment models)

- [ ] Per-weapon configurable add-ons using the imported attachment prefabs:
      optic (not sniper — has one), suppressor (less recoil, quieter, **no
      radar ping on fire**), foregrip (less recoil), extended mag (+50% ammo)
- [ ] Configured in the CLASSES menu, saved permanently (PlayerPrefs)
- [ ] **Loadout toggle**: 2-weapon class mode OR full arsenal like before —
      add-ons apply in BOTH modes
- [ ] Other players see your attachments (replicate as bitmask in NetworkWeapon)
- [ ] OPEN QUESTION for user: add-ons per weapon globally (default) or per class?

## 2) Feedback & polish pack

- [ ] Hit-direction indicator on the camera when you take damage
- [ ] Heavy-breathing sound at low HP — starts after the hit, fades after a few
      seconds, NOT infinite
- [ ] Killstreak sounds at 5/10/15/20/25/30... kills in a row (+ feed line)
- [ ] No-scope sniper kills flagged in the kill feed
- [ ] Spectator: join as spectator or switch to spectator "team"
- [ ] Controller support (gameplay first; menus later if feasible)

## 3) Arsenal expansion

- [ ] Every weapon in the imported packs as a real separate weapon in game
- [ ] LMG (heavy machine gun)
- [ ] Grenade-launcher gun
- [ ] Throwables: grenades, flashbangs, sticky bombs, throwing knives
- [ ] Shotgun reloads shell-by-shell (interruptible, CoD style)
- [ ] Gun color skins chosen in the class menu (if gun materials allow tinting)

## 4) Lobby + progression

- [ ] Pre-game lobby: host configures the match while everyone in the lobby
      SEES the settings live, players prep their classes there — "exactly like
      CoD"
- [ ] Level/XP system (kills, wins → levels; shown in lobby/scoreboard)

## 5) Modes & AI

- [ ] Zombie survival mode on a NEW DARK map (wave-based)
- [ ] Bots (fill matches with AI players)

## 6) New maps

- [ ] Western-vibes map
- [ ] Underwater-vibes map
- [ ] Map inside a giant flying plane
- [ ] (older wish) Crawl/prone state using the imported crawl animation —
      requires re-importing the Generic-rig FBXs as Humanoid

## Shipped (0.5.0) — pending user confirmation in multiplayer

Killcam replay (skippable) · classes menu ·
IK weapon-hold + aim pitch + slide lean replication · spawn protection 3s · click-speed pistol · Nuketown = editable custom prefab with
injected spawn markers · Steam fixes (own SteamManager, transport switch,
visible errors) · death-display cleanup.

## Platform / tooling notes

- Steam: friend must have `steam_appid.txt` (480) next to the exe — if his
  Steam doesn't show "Spacewar", his Steam API never initialized (that was the
  join blocker). Distribution deep-dive: `docs/STEAM_DISTRIBUTION.md`.
- itch.io page exists: https://nanofr.itch.io/nouveau-dossier — updates via
  `butler push "Build/" nanofr/nouveau-dossier:windows --userversion X.Y.Z`;
  wire `GameSettings.UpdateCheckUrl` to a raw version.txt for the in-game
  update notice.
- Unity MCP: registered user-level (`UnityMCP`, http://127.0.0.1:8080/mcp).
  Requires Unity open; sessions started before registration can't see it —
  restart the CLI session. First action when connected: read the Console,
  screenshot the Game view, then tune weapon/attachment fit visually.
- Blender MCP: explained to user, not attached yet (addon + `uvx blender-mcp`).
