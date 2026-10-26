# Roadmap

The long-term vision stays intact; features are added incrementally on the slice's shared systems
(input commands → server simulation, data-driven weapons/agents/abilities, match/team/spawn managers,
game-mode rules classes).

## Milestone 1 — Vertical slice (this delivery)

Dedicated-server foundation, prediction/reconciliation movement, two weapons + suppressor, health & two shield layers,
Striker with Smoke Grenade / Wayblocker / Field Heal, HeavyBomb with full round/match flow, graybox map, HUD and menus,
builds, automated tests. See TESTING.md for what was verified and RISKS.md for what is not done.

## Milestone 2 — Stability and multiplayer

- Lag compensation (hitbox history, rewind to shooter view time, max rewind window) — L-01.
- Correction smoothing, snapshot extrapolation, starvation handling — L-02, L-04.
- Latency/packet-loss/jitter test matrix (network simulator), dashboards from `[BH][Perf]` lines — L-11.
- Harden request validation further (per-client RPC budgets, anomaly logging).
- Validate 10-player performance on the target server hardware; Linux dedicated server build — L-12.
- Reconnect to a running match — L-15.

## Milestone 3 — Additional core modes

Duel (1v1), Deathmatch (up to 15 players — requires re-validating server limits), Capture the Area, Weapon Game.
Each mode = a `GameModeDefinition` asset + a rules class; shared configurable match/scoring.

## Milestone 4 — Weapons and attachments

Revolver, shotgun (multi-pellet resolution in `CombatSystem`), precision rifle, sniper rifle (ADS + magnified optic),
laser attachment, red-dot/holographic sights, customizable reticles, expanded balancing, purchase/economy if desired.

## Milestone 5 — Agent expansion

EnemyAlert, Flash Grenade, Revival (downed state), additional unlockable agents, agent selection and loadout rules,
optional agent bans.

## Milestone 6 — Training Grounds

Target range, damage testing using `DamageResolver`, weapon tuning, training scenarios, bots (beyond the test driver),
hidden collectibles and jump challenges.

## Milestone 7 — Production readiness

Cloud server allocation and session management behind `IServerHostingAdapter` / `ISessionConnector`, matchmaking,
player persistence and progression, additional maps, audio and animation polish, operational monitoring, anti-cheat
evaluation.

## Milestone 8 — Experimental content

Dynamic time and weather, environmental entities and alerts, magical equipment and spells, voice chat,
agent/team synergies, seasonal and limited-time events, map and agent selection/bans.
