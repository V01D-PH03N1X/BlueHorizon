# Testing

Only results that were actually run are marked as passed. Environment for every result below:
AMD Ryzen 9 5900XT (16 cores), NVIDIA GeForce RTX 3080 Ti, 64 GB RAM, Windows 11 Pro, Unity 6000.3.13f1,
all processes on one machine (localhost).

## How to run

| What | Command | Output |
|---|---|---|
| EditMode unit tests | `Tools\run-tests.ps1` | `Logs\editmode-results.xml` |
| Build client + server | `Tools\build.ps1` (`-Generate` to regenerate content) | `Builds\` |
| One multiplayer scenario | `Tools\autotest-match.ps1 -Scenario fight\|passive\|explode\|idle\|disconnect` | `Logs\autotest-<scenario>\` |
| All multiplayer scenarios | `Tools\run-all-autotests.ps1` | pass/fail table |
| 10-player load test | `Tools\loadtest.ps1 -Bots 10 -DurationSeconds 150` | `Logs\loadtest\` |
| Manual local match | `Tools\run-local-match.ps1 -Clients 2` | `Logs\local\` |

The multiplayer scenarios run the **real builds over UDP**: one headless dedicated server and headless clients driven
by `TestBotInputSource`, which only produces input commands — every action passes the same server validation as a
human player. The script checks the server log for the expected events.

## Results — 2026-10-26

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

A windowed client (1280×720) driven by the test bot captured screenshots during real matches against the dedicated
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
