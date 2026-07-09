# Steam: servers, updates, and Windows + macOS builds

This is the deep-dive companion to the README's short "Playing over Steam" and
"Shipping updates" sections. Everything here is from the official Steamworks
docs and current (2026) community-verified practice; sources at the bottom.

---

## 1. What "Steam servers" actually means for this game

Steam gives you **three separate things** — be clear which one you're using:

| Thing | What it is | Do you need it? |
|---|---|---|
| **Steam Datagram Relay (SDR)** | Valve's relay network. Your traffic is routed through Valve's backbone; players never see each other's IPs and no port forwarding is needed. | **Yes — this is what `SteamNetworkingSocketsTransport` uses.** |
| **Lobbies + friend join** | Matchmaking metadata service (create/join lobby, invite via overlay, Join Game on a friend). | **Yes — this is what `SteamLobbyService.cs` implements.** |
| **Dedicated game servers** | Machines YOU rent/run that use the Steam game-server API. Valve does not host game servers for you. | **No.** Your game is host-as-server (a player hosts; Netcode runs on their machine, relayed by SDR). |

So: "using Steam servers" = your existing host-client model, but connections go
through Valve's relay and matchmaking. There's no server rental involved and
nothing extra to pay — relay usage is free with the app fee.

What that means practically:
- The host's PC is the source of truth; if the host quits, the match dies
  (same as your LAN mode today). Host migration is not built in.
- Quality depends on the host's upload bandwidth, not on Valve.
- If one day you want 24/7 servers players can join anytime, that's the
  dedicated-server API + a rented Linux box — a separate project.

Setup steps for Steam play in this project are already in the README
("Playing over Steam"): Steamworks.NET package + Steam transport package +
`FPSBASE_STEAM` define + `SteamManager`/transport components + `steam_appid.txt`
(480 "Spacewar" for testing, your real App ID for release).

---

## 2. One-time Steamworks setup (before you can upload anything)

1. **Steamworks account**: sign up at `partner.steamgames.com`.
2. **Steam Direct fee**: **$100 per app** (recoupable after $1,000 revenue).
   This buys you an **App ID**.
3. In the partner site, your app automatically gets:
   - one **package** (what customers "buy"/install),
   - one **store page** (needs review before launch),
   - one default **depot** (see below — you'll add a second for macOS).
4. Identity verification (bank/tax info) takes days — do it early.

---

## 3. Shipping updates the right way (SteamPipe)

The whole point: **you never "send builds" to players.** You upload each build
once to Steam; Steam computes a **binary delta** against what players already
have, and the Steam client patches them automatically (usually a few MB for a
code-only change, even if your game is hundreds of MB). Players don't do
anything — the game updates itself in their library.

### The pieces

- **Depot** = a folder of files Steam tracks (one per OS is the norm).
- **Build** = a snapshot of all depots at a moment in time.
- **Branch** = a named pointer to a build. `default` is what players get.
  You can add `beta` branches (optionally password-protected) for testers.

### One-time local setup

1. Download the **Steamworks SDK** (partner site → SDK). Unzip somewhere
   permanent, e.g. `C:\steamworks\`.
2. The uploader lives in `sdk\tools\ContentBuilder\`:
   - `builder\steamcmd.exe` — the upload tool
   - `content\` — put your Unity build output here
   - `scripts\` — your `.vdf` build scripts
   - `output\` — logs + the local build cache (**don't delete between uploads**
     — it's what makes later uploads fast/delta-friendly)

### The two script files (one-time)

`scripts\app_build_YOURAPPID.vdf`:

```vdf
"AppBuild"
{
    "AppID" "YOURAPPID"
    "Desc" "v1.4 - nuketown + crouch fix"   // shows in the builds list
    "Preview" "0"           // 1 = dry run, nothing uploaded
    "SetLive" ""            // "" = upload only; "beta" = auto-publish to beta branch
    "ContentRoot" "..\content\"
    "BuildOutput" "..\output\"
    "Depots"
    {
        "YOURAPPID+1" "depot_windows.vdf"
        "YOURAPPID+2" "depot_mac.vdf"
    }
}
```

(Depot IDs: convention is AppID+1, AppID+2… — create them in the partner site
under *App Admin → SteamPipe → Depots* first.)

`scripts\depot_windows.vdf`:

```vdf
"DepotBuild"
{
    "DepotID" "YOURAPPID+1"
    "FileMapping"
    {
        "LocalPath" "windows\*"   // relative to ContentRoot
        "DepotPath" "."
        "recursive" "1"
    }
}
```

`depot_mac.vdf` is identical with `"LocalPath" "mac\*"`.

### Every update after that (the actual workflow)

```
1. Unity: build Windows → ContentBuilder\content\windows\
   Unity: build macOS  → ContentBuilder\content\mac\
2. cd C:\steamworks\sdk\tools\ContentBuilder
3. builder\steamcmd.exe +login YOUR_BUILD_ACCOUNT +run_app_build ..\scripts\app_build_YOURAPPID.vdf +quit
4. Partner site → App Admin → SteamPipe → Builds
   → set the new build live on "default" (two clicks; can also be automated
     with "SetLive", but doing the default branch by hand is the recommended
     safety valve — publishing to *default* from the script requires an extra
     confirmation for exactly that reason).
5. Players' Steam clients auto-download just the diff. Done.
```

Practical tips (hard-won, from the official docs):
- Use a **dedicated build account** with only "Edit App Metadata" + "Publish
  App Changes" permissions, not your main login (steamcmd stores credentials;
  Steam Guard will prompt once per machine).
- Steam's delta works on file blocks — **don't rename/rearrange files each
  build** if you can avoid it; Unity's output layout is stable so you're fine.
- Test the pipeline **before launch**: builds can be uploaded and set live on a
  private branch even while your store page is still in review.
- `"Preview" "1"` first run = see exactly what would upload, without uploading.

### Beta branches (free QA)

Create a branch `beta` in the partner site, upload with `"SetLive" "beta"`.
Testers: game → Properties → Betas → pick `beta`. They get updates instantly;
`default` players see nothing until you promote the same build to default —
promoting is instant (no re-upload; a build can be pointed at any branch).

---

## 4. Building for Windows AND macOS from this project

### Windows (what you do today)
`File > Build Settings > Windows` → output into
`ContentBuilder\content\windows\`. Nothing changes.

### macOS — the honest picture

- **You CAN build a Mac player from Unity on Windows** — install the module
  **macOS Build Support (Mono)** via Unity Hub for your 6000.x version. Target
  "Intel 64-bit + Apple silicon" (Universal) in Player Settings so both Mac
  generations get native code.
- **The catch: IL2CPP for macOS can only be compiled ON a Mac.** From Windows
  you're limited to the **Mono** scripting backend for the Mac build. For this
  project that's fine (Mono is the default and everything here runs on Mono).
- Output goes to `ContentBuilder\content\mac\YourGame.app`.

### Signing / notarization — what's actually required (common confusion)

- Valve's official position: notarization is **suggested, not mandated** —
  macOS games ship on Steam unsigned every day and run fine. This works
  because **the Steam client doesn't set the `com.apple.quarantine` flag** on
  files it installs, and Gatekeeper's notarization check only blocks
  *quarantined* apps. (Direct downloads from a website DO get quarantined —
  that's where notarization matters.)
- So for **Steam-only** Mac distribution: you can ship unsigned. Recommended
  polish if you ever get a Mac + Apple Developer account ($99/yr): codesign
  with hardened runtime + these entitlements (Mono/Steam need them):
  `com.apple.security.cs.allow-unsigned-executable-memory`,
  `com.apple.security.cs.allow-dyld-environment-variables`,
  `com.apple.security.cs.disable-library-validation`, and **do not** sandbox
  (`com.apple.security.app-sandbox` must be absent). Then notarize with
  `notarytool`. Unity's manual has a page walking through exactly this.
- One real-world gotcha: the mac `.app` is a folder full of files — SteamPipe
  uploads it fine, but **executable permissions can be lost** if you zip it
  through Windows tools first. Point the depot at the raw `.app` folder (as in
  the vdf above), never at a zip.

### Per-OS launch options (one-time, partner site)

*App Admin → Installation → General Installation*: add two launch options —
`YourGame.exe` (OS: Windows) and `YourGame.app` (OS: macOS). Each customer's
client downloads only the depot for their OS and uses the matching launch
option automatically.

### Testing the Mac build without owning a Mac

Realistically: a friend with a Mac + a `beta` branch is the standard indie
answer. The Mono cross-build usually "just works", but the first time you
should verify: game launches, Steam overlay opens (Shift+Tab), a Steam-relay
match connects between a Windows host and the Mac client.

---

## 5. Cheat sheet

```
NEW UPDATE  = Unity build (Win + Mac) → steamcmd +run_app_build → set live
PLAYERS     = auto-patched, delta only, zero action
TEST FIRST  = SetLive "beta", testers opt in via Properties → Betas
MAC         = Mono backend from Windows, Universal, unsigned OK on Steam
NEVER       = send zips to players / rebuild their whole install
```

## Sources

- [Steamworks: Uploading to Steam (SteamPipe/ContentBuilder/vdf)](https://partner.steamgames.com/doc/sdk/uploading)
- [Steamworks: Builds & branches](https://partner.steamgames.com/doc/store/application/builds)
- [Valve announcement thread: macOS requirements — 64-bit required; notarization "suggested, not mandated"](https://steamcommunity.com/groups/steamworks/eventcomments/1742266164814599752/)
- [Unity Manual: Code sign and notarize your macOS application (entitlements list)](https://docs.unity3d.com/Manual/macos-building-notarization.html)
- [Unity Manual: Building your macOS application (cross-building, IL2CPP-needs-a-Mac)](https://docs.unity3d.com/2021.2/Documentation/Manual/macos-building.html)
- [Community guide: uploading Unity mac builds to Steam (permissions/entitlements pitfalls)](https://yemi.me/2020/02/17/en/submit-unity-macos-build-to-steam-appstore/)
- [Publishing Unity games on Steam — end-to-end walkthrough](https://medium.com/@yoonicode/publishing-unity-games-on-steam-the-ultimate-guide-5e09fc812c65)
