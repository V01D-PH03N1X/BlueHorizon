# Roadmap

The long-term vision stays intact; features are added incrementally on the slice's shared systems
(input commands → server simulation, data-driven weapons/agents/abilities, match/team/spawn managers,
game-mode rules classes).

## Milestone 1 — Vertical slice (this delivery)

Dedicated-server foundation, prediction/reconciliation movement, two weapons + suppressor, health & two shield layers,
Striker with Smoke Grenade / Wayblocker / Field Heal, HeavyBomb with full round/match flow, graybox map, HUD and menus,
builds, automated tests. See TESTING.md for what was verified and RISKS.md for what is not done.

## Milestone 2 — Stability and multiplayer (implemented 2026-10-26, see TESTING)

- Done: lag compensation (pose history, clamped rewind to the shooter's view time, 300 ms cap).
- Done: correction blending, snapshot extrapolation, input-starvation gravity, snapshot-derived render clock.
- Done: network simulator (latency/jitter/loss) and latency test matrix; per-client RTT, shots/hits, rewind in perf logs.
- Done: input message budget with anomaly logging.
- Done: 10-player load test re-run; Linux dedicated server builds (not yet run, L-12).
- Done: reconnect to a running match.
- Open: two-machine LAN/WAN playtest, Linux run, human playtest of hit-registration feel.

## Milestone 3 — Additional core modes (implemented 2026-10-27, see TESTING)

- Done: generic match flow in `MatchManager`, pluggable `GameModeRules`; HeavyBomb moved onto it unchanged.
- Done: Duel, Deathmatch (15 players, validated), Capture the Area, Weapon Game; per-player loadouts.
- Done: free-for-all spawns and capture zone on Horizon Depot; mode-aware HUD, scoreboard, results and test bots.
- Open: human playtests of the new modes; balance of capture timings and Weapon Game ladder (only 2 weapons until M4).

## Milestone 4 — Weapons and attachments (implemented 2026-10-27, balance not playtested)

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
