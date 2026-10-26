# Blue Horizon — Vertical Slice

Competitive tactical first-person shooter by **Sebastian Schindler**.
This repository contains the playable vertical slice: one HeavyBomb match on the graybox map *Horizon Depot*,
played on a **dedicated, server-authoritative** server.

| Component | Version |
|---|---|
| Unity Editor | **6000.3.13f1** (Unity 6.3 LTS) |
| Render pipeline | URP 17.3.0 |
| Networking | Netcode for GameObjects **2.11.0**, Unity Transport **2.7.2** |
| Input | Input System 1.19.0 |
| UI | UI Toolkit (runtime) |
| Tests | Unity Test Framework 1.6.0 (EditMode) |
| Platform | Windows 64-bit client + Windows dedicated server (Linux server build target available, untested) |

The package versions are the ones bundled with the 6000.3.13f1 editor; do not upgrade them without a reason.

## Contents

- [GAME_DESIGN.md](GAME_DESIGN.md) — HeavyBomb rules, round flow, map, health/shields
- [AGENT_DESIGN.md](AGENT_DESIGN.md) — Striker and the ability framework
- [WEAPON_BALANCING.md](WEAPON_BALANCING.md) — weapon values, damage pipeline, attachments
- [TECHNICAL_ARCHITECTURE.md](TECHNICAL_ARCHITECTURE.md) — networking model, security boundary, modules
- [TESTING.md](TESTING.md) — how to test, and the latest **actual** results
- [RISKS.md](RISKS.md) — known issues, limitations and risks
- [ROADMAP.md](ROADMAP.md) — milestones after the vertical slice
- [CHANGELOG.md](CHANGELOG.md)

## Quick start (fresh machine)

1. Install **Unity 6000.3.13f1** with the modules *Windows Build Support (Mono)* and *Windows Dedicated Server Build Support*
   (optional: *Linux Dedicated Server Build Support*).
2. Clone the repository and open it once in the editor *or* run the build script (it imports the project headless).
   Close the editor before running any `Tools\*.ps1` script — batch mode cannot open a project that is already open.
3. Build the client and the dedicated server:

   ```powershell
   powershell -ExecutionPolicy Bypass -File Tools\build.ps1
   ```

   Outputs: `Builds\Client\BlueHorizon.exe`, `Builds\Server\BlueHorizonServer.exe` (+ `server.json`).
   Add `-Generate` to regenerate scenes/prefabs/materials from code first (see *Generated content* below).

4. Start a local server and two clients:

   ```powershell
   powershell -ExecutionPolicy Bypass -File Tools\run-local-match.ps1 -Clients 2
   ```

   With only one human player, add `-DevSolo` (match starts with one player) or `-Bots 1` (adds a headless test bot).
   Stop everything with `Tools\stop-local-match.ps1`.

The tools find Unity through `$env:UNITY_EDITOR`, the Unity Hub secondary install path, or common install folders.

## Running the dedicated server

```powershell
Builds\Server\BlueHorizonServer.exe -port 7777 -logFile Logs\server.log
```

Server builds always start as a server (`UNITY_SERVER`), headless, capped at 120 Hz, listening on UDP `0.0.0.0:<port>`.
Configuration precedence: defaults → `server.json` next to the executable (or `-config <path>`) → command line.

| Argument | Meaning |
|---|---|
| `-port N` | UDP port (default 7777) |
| `-listen ADDR` | listen address (default 0.0.0.0) |
| `-maxPlayers N` | connection limit, 1–10 |
| `-devSoloStart` | allow a match to start with one player (development) |
| `-minPlayersPerTeam N` | players required per team (default 1) |
| `-roundTime S`, `-preRoundTime S`, `-postRoundTime S` | round timing overrides |
| `-plantTime S`, `-defuseTime S`, `-fuseTime S` | bomb timing overrides |
| `-roundsToWin N` | match length (default 7) |
| `-quitAfter S`, `-quitAfterMatches N` | automated-test lifetime |
| `-perfLogInterval S` | performance log period (server default 30 s) |
| `-defaultSocket` | use the stock Unity Transport socket instead of the ICMP-safe one (diagnostics) |

Shut the server down with Ctrl+C / closing the window, or `Stop-Process`. It logs startup, joins, team assignment,
match start, every kill, ability use, bomb event, round result, match result, disconnects and periodic performance in
the `[BH][Category] message @time` format.

## Connecting a client

- **Menu:** start `BlueHorizon.exe`, enter callsign, server address and port, press **Connect**.
- **Command line:** `BlueHorizon.exe -connect 127.0.0.1 -port 7777 -name Striker`.
- Window size: `-screen-width 1280 -screen-height 720 -screen-fullscreen 0`.
- Several clients can run on one PC (Run In Background is enabled).

*Host Local Match (Dev)* in the menu starts a listen server for quick solo checks. It is a development convenience
only; the dedicated server build is the reference.

## Controls

| Action | Key |
|---|---|
| Move / sprint / crouch / jump | WASD / Shift / Ctrl or C / Space |
| Fire / reload | Left mouse / R |
| Assault rifle / pistol | 1 / 2 |
| Toggle suppressor on current weapon (warmup and buy time only) | T |
| Smoke Grenade / Wayblocker / Field Heal | Q / E / X |
| Plant (attacker with bomb, inside a site) / defuse (defender at the bomb) | hold F |
| Scoreboard / menu & settings | Tab / Esc |
| Switch spectated teammate (while dead) | Space |

Settings (sensitivity, FOV, volume, invert look) persist in PlayerPrefs.

## Generated content

Scenes (`Bootstrap`, `MainMenu`, `VerticalSlice`), prefabs, materials, the grid texture, map meshes, the panel settings
and the network prefab list are generated by **Blue Horizon ▸ Generate Project Content** (or `Tools\build.ps1 -Generate`).
The generator is the source of truth for the map layout (`Assets/_Project/Editor/MapBuilder.cs`).
Data assets (weapons, agent, abilities, game mode) are created only when missing, so balance edits made in the
inspector survive regeneration. Hand edits to generated scenes are overwritten — change the generator instead.

## Project layout

```
Assets/_Project/
  Scripts/            runtime code (one assembly: BlueHorizon.Runtime)
    Core/ Networking/ Player/ Movement/ Weapons/ Attachments/ Combat/ Health/
    Agents/ Abilities/ Objectives/ GameModes/ Match/ UI/ Audio/ Testing/
  Editor/             content generator + build tools (BlueHorizon.Editor)
  Tests/EditMode/     NUnit tests (BlueHorizon.Tests.EditMode)
  ScriptableObjects/  weapons, attachments, agent, abilities, game mode
  Prefabs/ Scenes/ Materials/ Settings/ UI/ Art/Generated/
Documentation/        this folder
Tools/                build, test, launch and automated multiplayer test scripts
```
