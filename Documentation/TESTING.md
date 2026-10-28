# Testing

Only results that were actually run are marked as passed. Environment for every result below:
AMD Ryzen 9 5900XT (16 cores), NVIDIA GeForce RTX 3080 Ti, 64 GB RAM, Windows 11 Pro, Unity 6000.3.13f1,
all processes on one machine (localhost).

## How to run

| What | Command | Output |
|---|---|---|
| EditMode unit tests | `Tools\run-tests.ps1` | `Logs\editmode-results.xml` |
| Build client + server | `Tools\build.ps1` (`-Generate` to regenerate content) | `Builds\` |
| One multiplayer scenario | `Tools\autotest-match.ps1 -Scenario fight\|passive\|explode\|idle\|disconnect\|reconnect\|duel\|deathmatch\|capture\|weapongame\|weapons\|agents\|agentban` | `Logs\autotest-<scenario>\` |
| All multiplayer scenarios | `Tools\run-all-autotests.ps1` | pass/fail table |
| 10-player load test | `Tools\loadtest.ps1 -Bots 10 -DurationSeconds 150` | `Logs\loadtest\` |
| Hit registration under latency | `Tools\latency-matrix.ps1 [-Latencies 0,50,100,150] [-JitterMs n] [-LossPercent p]` | `Logs\latency\results.json` |
| Manual local match | `Tools\run-local-match.ps1 -Clients 2` | `Logs\local\` |

The multiplayer scenarios run the **real builds over UDP**: one headless dedicated server and headless clients driven
by `TestBotInputSource`, which only produces input commands — every action passes the same server validation as a
human player. The script checks the server log for the expected events.

Bot options for manual or visual runs: `-botLoadout random` (or an explicit `p,s,pm,pu,po,sm,su,so` index list),
`-botMode ads` (stand still aiming down sights, for optics screenshots), `-showLoadout` (opens the loadout panel after
spawning), `-screenshotInterval N -screenshotDir path`.

Agent options: `-botAgent random|<id>` (bots), server `-unlockAgents all|<ids>`, `-banAgents <ids>`, `-downed on|off`.

## Results — Milestone 5 (2026-10-28)

### EditMode — 136 / 136 passed
New (`AgentsM5Tests`, 20): flash intensity (facing, behind, out of range, no line of sight, minimum), whiteness
curve, EnemyAlert range, downed rules (weapon only, last player standing dies, explosions kill), bleedout damage,
revive conditions, downed net-state progress, `HealthModel.Revive` (only from 0, capped, no healing while down),
agent unlock/ban/unknown/fallback rules, protocol 4 rejected, generated agents and abilities, downed state only in
HeavyBomb/Capture, smoke throw settings survived the move to `ThrownGrenadeDefinition`.

### Automated multiplayer scenarios — 13 / 13 passed (M5 build)
All eleven earlier scenarios pass with the downed state active in HeavyBomb and Capture, plus:
- **agents** (HeavyBomb 3v3, Vigil + Lifeline + Striker per team, `-unlockAgents all`): all six agents applied;
  flashes blinded the thrower, allies and enemies (log shows relation, duration, intensity); EnemyAlert revealed
  3 enemies; players downed, revived by hold-F (30 HP) and by Revival (75 HP), finished while downed, and a team
  with nobody standing bled out; bots logged "Blinded by a flash; holding fire". No exceptions in any log.
- **agentban** (Deathmatch, `-banAgents vigil`, Lifeline locked): availability logged, Vigil requests refused as
  banned, Lifeline as locked, bots played as Striker. (First run failed: only 2 bots never met in 120 s; the
  scenario now uses 4 bots and passes.)

### Graphical check
Windowed bot client with screenshots: Agent & Loadout panel (agents, role, health values, abilities), ability bar
following the agent, flash white-out with the HUD on top, "REVIVING …  keep holding F" prompt with progress and the
green downed-teammate marker. Not capture|weapongame|weapons|agents|agentban` | `Logsd on screen: the red EnemyAlert enemy marker (no reveal hit enemies while
the windowed bot was recording) and the downed vignette (the windowed bot was finished within a second).

### Not verified in M5
Human play and balance of flash, EnemyAlert, downed/revive timings and the new agents; EnemyAlert marker and downed
vignette on screen; Linux server run.

## Results — Milestone 4 (2026-10-28)

### EditMode — 116 / 116 passed
New (`WeaponsM4Tests`): shotgun pellets (8, inside the cone, deterministic, distinct), summed pellet damage with
falloff (160 close / 40 beyond 25 m), ADS progress timing and release, ADS blocked while sprinting, sniper hip vs.
scoped spread, bolt cadence (0.75/s), built-in optic beats a fitted optic, red dot / 4× scope stats, laser affects hip
fire only, loadout validation (wrong slot, unknown index, disallowed attachment, built-in scope), loadout text
round-trip, random bot loadouts always valid, generated weapon assets match the balance proposal.

### Automated multiplayer scenarios — 11 / 11 passed (M4 build)
All ten earlier scenarios still pass, plus **weapons**: Deathmatch with four `-botLoadout random` bots — all four
loadouts accepted without correction, kills with assault rifle and sniper rifle, "5 kills reached". The Weapon Game
run climbed all 8 ladder levels (shotgun, precision rifle, sniper and revolver kills) to "Final weapon kill".

### Graphical check
Windowed bot clients with screenshots: loadout panel, laser beam from the underbarrel to the aim point, red dot
reticle, holographic ring, 8× scope overlay. Found and fixed: the scope's black bars covered the health and ammo
panels (overlay moved below the HUD widgets); the red dot was too small at 720p; the `-showLoadout` preview opened
before the server had applied the loadout; the panel's "applies now / next spawn" line went stale when the phase changed.

### Not verified in M4
Human play and balance of the new weapons; ADS feel with a real mouse; laser/scope readability for opponents at range.

## Results — Milestone 3 (2026-10-27)

### EditMode — 97 / 97 passed
New: hostility rules (team / free-for-all), duel timeout winner, free-for-all leader and tie-breaks, weapon ladder
advance and win, capture|weapongame|weapons|agents|agentban` | `Logs zone (capture time, multi-player speed and cap, contested, reverting enemy progress, decay,
scoring), farthest spawn selection, mode command-line flags, mode player caps.

### Automated multiplayer scenarios — 10 / 10 passed (M3 build)
The six HeavyBomb scenarios still pass after the `MatchManager` refactor, plus:

| Scenario | Setup | Result |
|---|---|---|
| duel | 2 bots, first to 3, 45 s rounds | 5 decisive rounds, Alpha wins 3-2 |
| deathmatch | 4 bots, first to 5 kills | "5 kills reached", 11 kills, server limit 15 |
| capture|weapongame|weapons|agents|agentban` | `Logs | 2v2, first to 25 points | area captured, 25 points reached (a separate graphical run saw 4 ownership changes) |
| capture|weapongame|weapons|agents|agentban` | `Logs | 4 bots | several players advanced through the ladder; "Final weapon kill" ended the match |

### Load test — 15 players, Deathmatch, 150 s
Server avg 8.34 ms (120 Hz cap), p99 ≤ 8.6 ms, every client 900 inputs per 15 s, 344 kills, 56 ability uses,
0 exceptions, client corrections 0–11 per 30 s. GC ~25 gen-0 per 15 s (heap 3 MB).

### Graphical check
Capture: team points, "BRAVO HOLDS THE AREA" / "BRAVO CAPTURING 36%", "IN THE AREA · HOLDING" prompt. Deathmatch:
own score vs. leader, "FREE FOR ALL" banner, red hostile kill-feed names. Found and fixed: the zone volume tinted the
whole screen when standing in it (replaced by floor plate + posts).

### Not verified in M3
Human play of the new modes; win-rate balance; Weapon Game with more than two weapons.

## Results — Milestone 2 (2026-10-26)

### EditMode — 85 / 85 passed
The 72 milestone-1 tests plus 13 new ones: pose history interpolation/clamping/ring buffer, view-time clamping
(future, too old, invalid, backtracking, rewind disabled), reconnect registry (restore once, expiry, other match),
simulation/lag-comp command-line flags.

### Hit registration under latency — `Tools\latency-matrix.ps1`
A shooting bot against a bot strafing left/right at run speed (5.2 m/s, direction change every 0.9 s), both bots with
simulated latency in their sockets, 180 s per run, server rewind cap 300 ms:

| RTT | Lag comp | Shots | Hits | Hit % | Avg rewind | Kills |
|---|---|---|---|---|---|---|
| 0 ms | on | 30 | 24 | **80.0** | 123 ms | 6 |
| 0 ms | off | 59 | 22 | 37.3 | — | 6 |
| 50 ms | on | 34 | 24 | **70.6** | 167 ms | 6 |
| 50 ms | off | 119 | 17 | 14.3 | — | 4 |
| 100 ms | on | 27 | 20 | **74.1** | 227 ms | 6 |
| 100 ms | off | 167 | 16 | 9.6 | — | 3 |
| 150 ms | on | 27 | 22 | **81.5** | 266 ms | 6 |
| 150 ms | off | 227 | 11 | 4.8 | — | 2 |
| 100 ms ±20 ms jitter, 3 % loss | on | 28 | 22 | **78.6** | 214 ms | 6 |
| 100 ms ±20 ms jitter, 3 % loss | off | 194 | 13 | 6.7 | — | 3 |

Without compensation the bot (which aims where it *sees* the target) misses a moving target more the higher the
latency; with compensation the hit rate stays roughly constant. Even at 0 ms the interpolation delay makes
uncompensated shots trail a strafing target. Samples are small (one engagement per round); they show the effect
clearly but are not a precise accuracy benchmark. The bot's aim is perfect — this measures hit *registration*, not
human feel. Prediction corrections for the shooter rose with latency (2 → 64 per 180 s), mostly from player contact
(RISKS L-03).

An earlier run (before `SnapshotClock`) showed 188 ms of rewind at 0 ms RTT: Netcode's client `ServerTime` trailed
the snapshot stream by ~90 ms, so remote players were displayed later than necessary. Fixed and re-measured above.

### Automated multiplayer scenarios — 6 / 6 passed
`fight`, `passive`, `explode`, `idle`, `disconnect` (all re-run on the M2 build) and the new **`reconnect`**:
Bot1 is killed mid-round, dropped after 5 s of input silence, restarted with the same name, rejoins Alpha with its
stats restored ("reconnected to match 1; team and stats restored"), and the match completes.

### Load test — 10 players, 150 s (M2 build)
Server avg 8.34 ms (120 Hz cap), p99 ≤ 8.5 ms, every client 900 inputs per 15 s, per-client RTT 7–18 ms on localhost,
average rewind 113–118 ms, 24 kills, 46 ability uses, 0 exceptions. GC unchanged (~14 gen-0 per 15 s, heap 3 MB).

### Linux dedicated server
Builds (`Tools\build.ps1 -Target LinuxServer`, 85 MB). **Not run** — no Linux/WSL on the dev PC.

### Not verified in M2
Human feel of lag compensation and correction blending; two-machine LAN/WAN; Linux run; jitter above ±20 ms and loss
above 3 %.

## Results — Milestone 1 (2026-10-26)

### EditMode — 72 / 72 passed

| Area | Tests |
|---|---|
| Damage | pistol & rifle damage per zone (spec values), suppressor ×0.8 for both weapons, falloff applied after attachments, minimum 1 damage |
| Weapon simulation | rifle 4.5/s cadence, exactly one magazine (31) then auto-reload, ammo conservation, reload duration and refill, no fire while reloading, semi-auto single shot, pistol 1.6/s under click spam, no fire when not allowed or sprinting, equip delay after switching, deterministic spread |
| Input | command sanitization (NaN, pitch clamp, unknown bits), move clamping |
| Health | barrier→armor→health order, overflow through layers, death at 0 and no further damage, heal cap and no heal when dead, barrier regen delay/rate/cap, damage restarts regen, armor never regenerates |
| Abilities | charge consumption/exhaustion, cooldown gating, unlimited-charge signature, rejection while dead / wrong phase / missing |
| Wayblocker placement (real physics) | accepted on open ground facing the player, rejected overlapping geometry, rejected inside a player, rejected beyond range |
| Bomb state machine | plant timing and fuse start, invalid plant requests (not carrier, wrong side, dead, outside site), duplicate plant, death during plant drops bomb and pickup rules, cancelled plant, defuse timing, invalid defuse (not planted, wrong side, dead, out of range, second defuser), interrupted defuse loses progress, detonation timing, tie-break both ways, inactive bomb rejects everything |
| Round rules | every end condition, simultaneous elimination, timeout during planting, timer ignored after plant, reduced dev teams |
| Match | side swap per half, match winner, team balancing and full teams, spawn selection |
| Connection | payload round trip, malformed/oversized/wrong-protocol/full-server rejection, name sanitization, command-line role and override parsing |

### Builds — passed

Windows client (100 MB, ~50 s incremental) and Windows dedicated server (91 MB, ~45 s) build headless via
`Tools\build.ps1` without errors. Linux server: **not built** (target wired, untested).

### Automated multiplayer scenarios — 5 / 5 passed

Each: dedicated server + headless bots over UDP, short timings (round 75 s, plant 3 s), first to 2.

| Scenario | Bots | What happened (server log) | Result |
|---|---|---|---|
| fight | 2 | joins & team assignment, smoke/wayblocker/heal used, kills incl. headshots, elimination round ends, a plant followed by a defuse, match over | PASS |
| passive | 2 | carrier plants at A, defender defuses; round 2 at B — defender re-routes from A and defuses; match over 0-2 | PASS |
| explode | 2 | fuse (4 s) shorter than defuse (6 s): defuse started but bomb detonates (tie-break), 12 m blast kills; attackers win 2-0 | PASS |
| idle | 2 | nobody acts: both rounds end on the timer, defenders win | PASS |
| disconnect | 3 | Bot1's process is killed mid-round; server drops it after 5 s of input silence; remaining attacker and defender keep playing (plants + defuses); match completes | PASS |

### Load test — 10 players, 150 s

Dedicated server + 10 headless fighting bots. Server samples every 15 s:

| Metric | Value |
|---|---|
| Server frame time | avg 8.34 ms (= its 120 Hz cap), p99 ≤ 8.5 ms, max 11.4 ms after warm-up (first window 224 ms: scene/player spawn) |
| Inputs received | 900 per client per 15 s for all 10 clients (= 60 Hz, nothing dropped) |
| GC | ~14 gen-0 collections per 15 s, managed heap 3 MB (from the managed socket, see RISKS L-13) |
| Activity | 22 kills, 4 rounds, 45 ability uses, **0 exceptions** |
| Client prediction | 1–43 corrections per 30 s per bot (player-vs-player contact, RISKS L-03) |

### Graphical client (visual verification)

A windowed client (1280×720) driven by the test bot capture|weapongame|weapons|agents|agentban` | `Logsd screenshots during real matches against the dedicated
server: main menu, HUD (vitals, ammo, abilities, timer/score), defuse prompt with progress bar, red bomb timer,
kill feed, round-result banner, spectator overview after death, victory screen with scoreboard, team-coloured
Wayblocker, remote player model. Frame time at 1280×720: avg 2.5–5 ms (200–400 fps), p99 8–15 ms.

### Other manual checks

| Check | Result |
|---|---|
| Server ↔ client connection, scene sync, player spawn | passed (all runs) |
| Dev host mode (`-host`) with a guest | passed: host plays, guest joins mid-round as spectator |
| Mid-round join | passed: joiner spectates until the next round |
| Server returns to warmup when players leave | passed (zero-player and not-enough-player cases) |
| Kill → ICMP stall root cause | reproduced with stock socket, not reproduced when the client is suspended, fixed with the ICMP-safe socket |

## Not verified (honest gaps)

- **Keyboard/mouse gameplay by a human.** Every gameplay run used the scripted bot input; the
  `KeyboardMouseInputSource` path (mouse look, key bindings) compiles and is wired but was not exercised by a person
  in this session. The menu was rendered and inspected, but its buttons were not clicked (clients connected via
  `-connect`). **First manual action for the team: play a 2-client match with `Tools\run-local-match.ps1`.**
- Latency, jitter and packet loss behaviour (all tests on localhost) — RISKS L-11.
- Two-machine LAN play and firewall configuration.
- Linux dedicated server.
- Audio quality and mix (placeholder synthesized clips; only presence verified).
- PlayMode tests: none; integration behaviour is covered by the build-level scenarios above.

## Acceptance checklist (vertical slice)

| Criterion | Status |
|---|---|
| A. Project opens with 6000.3.13f1, packages resolve, no compile errors | verified headless |
| B. Dedicated server, ≥2 clients, authoritative state, spawning/movement sync, consistent damage/score/bomb, disconnects don't stall | verified (scenarios, load test) |
| C. Both weapons, spec values, fire/reload/ammo, hit zones, death/respawn, no client-decided damage | verified (unit tests + scenarios); human gunfeel not verified |
| D. Striker, health & shields, Smoke, Wayblocker, Heal, synced cooldowns | verified (unit tests + scenarios + screenshots) |
| E. HeavyBomb: teams, both sites, plant/defuse rules, detonation, every round end, server results, valid state after rounds | verified (sites A and B, all five end reasons observed) |
| F. Readable HUD, feedback, no dead buttons, playable map, cohesive look | verified visually from screenshots; human playtest pending |
| G. Reproducible builds, documented commands, test results, limitations | this folder |
