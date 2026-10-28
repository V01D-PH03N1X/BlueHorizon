# Changelog

## 0.4.0 — 2026-10-27 — Milestone 4: weapons and attachments

### Added
- Revolver, Shotgun (8 pellets, server-side per-pellet hitscan with one rewind), Precision Rifle, Sniper Rifle (built-in 8× scope).
- Laser sight (visible beam for all players), red dot, holographic sight, 4× scope; reticle colour/shape settings.
- Aim down sights (right mouse): predicted ADS progress, spread/FOV/move-speed changes, zoom-scaled sensitivity, scope overlay.
- Full loadouts with per-slot attachments, server-validated `RequestLoadoutRpc`, loadout panel (B), choice saved in PlayerPrefs.
- Bots: `-botLoadout random|<indices>`, ADS with optics, `-botMode ads`; `-showLoadout`; autotest scenario `weapons`.
- Weapon Game ladder extended to 8 levels.

### Changed
- `SuppressorMask` replaced by attachment indices in `PlayerLoadout`; reconnect keeps the preferred loadout.

## 0.3.0 — 2026-10-27 — Milestone 3: additional core modes

### Added
- Pluggable game modes: generic `MatchManager` flow + `GameModeRules` per mode; selected with `-mode`.
- Duel (1v1 rounds, timeout by remaining health), Deathmatch (free-for-all, 15 players), Capture the Area
  (single zone, capture/contest/score), Weapon Game (kill-to-advance weapon ladder).
- Per-player loadouts (replicated weapon indices), mid-life weapon reset; `GameContent.weapons` and `modes`.
- 17 free-for-all spawns with farthest-from-enemies selection; capture zone with floor plate and progress posts.
- Mode-aware HUD (scores, phase text, prompts, announcements), free-for-all scoreboard and results.
- Test bots for all modes; `duel`, `deathmatch`, `capture`, `weapongame` automated scenarios; `loadtest.ps1 -Mode`.
- 12 new EditMode tests (97 total).

### Changed
- Server player limit raised to 16 (the mode caps it); `server.json` default updated.
- Mode timing fields moved to the shared `GameModeDefinition` (`roundTime` → `timeLimit`, `roundsToWin` → `scoreLimit`,
  existing asset values migrated with `FormerlySerializedAs`).

## 0.2.0 — 2026-10-26 — Milestone 2: stability and multiplayer

### Added
- Server lag compensation: per-character pose history, clamped client view time, hitbox rewind for every shot
  (`-noLagComp`, `-maxRewind ms`).
- Snapshot-derived render clock for remote players (removes ~90 ms of extra display delay), up to 100 ms of
  extrapolation when snapshots run out, visual blending of prediction corrections.
- Server applies gravity to input-starved airborne players; input message budget (180/s) with `[BH][Security]` logs.
- Network simulator in the managed socket (`-simLatency`, `-simJitter`, `-simLoss`); `Tools\latency-matrix.ps1`.
- Reconnect: team, stats and loadout restored for 180 s after a disconnect; Reconnect button in the menu;
  `reconnect` automated scenario.
- Perf log: per-client RTT, shots/hits, average rewind; per-player shots/hits in the match summary.
- Linux dedicated server target builds.
- 13 new EditMode tests (85 total).

## 0.1.0 — 2026-10-26 — Vertical slice (milestone 1)

### Added
- Netcode for GameObjects 2.11.0 / Unity Transport 2.7.2 dedicated-server architecture; Windows client and
  dedicated server builds; `server.json` + command-line configuration; connection approval (protocol, capacity, names).
- Server-authoritative player simulation with client prediction, reconciliation and remote interpolation; bounded
  input processing (no speed hacks); 5 s input-silence timeout.
- First-person controller: run, sprint, crouch (with headroom), jump, sensitivity/FOV/invert settings.
- Data-driven weapons (Assault Rifle, Pistol) with spec damage values, cadence, magazines, reload, switching,
  deterministic spread and recoil; server hitscan with head/torso/legs hitboxes; hit confirmation, kill feed.
- Attachment framework with the Suppressor (×0.8 damage, quiet remote audio, no muzzle flash, minimap flag).
- Health with two shield layers (barrier regenerates, armor does not).
- Agent framework; Striker with Smoke Grenade, Wayblocker and Field Heal; generic charges/cooldowns.
- HeavyBomb: warmup, countdown, freeze time, live rounds, bomb carry/drop/pickup/plant/defuse/detonation,
  all round-end rules, side swap, match victory, return to warmup.
- Graybox map *Horizon Depot* with two sites, three lanes, signage and spawn protection; generated from code.
- UI Toolkit HUD (vitals, ammo, abilities, timer/score, bomb state, prompts, kill feed, hit markers, damage and smoke
  overlays), main menu/connection screen, scoreboard, pause/settings, round and match result screens.
- Placeholder synthesized audio and pooled visual effects.
- Editor content generator, build tools, PowerShell tooling (build, tests, local match, automated multiplayer
  scenarios, load test), performance monitor, screenshot recorder, scripted test bot.
- 72 EditMode tests.

### Fixed during development
- Windows ICMP "port unreachable" stall dropping all clients after one client crashed (ICMP-safe socket).
- Zero-player rounds; pre-spawn NetworkVariable writes; prefab hash generation; one-class-per-file serialization.
