# Changelog

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
