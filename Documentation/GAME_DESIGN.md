# Game Design — Vertical Slice

Blue Horizon is a competitive tactical FPS: precise gunplay, short lethal fights, team objectives, agents with
tactical utility. The vertical slice proved the core loop with one mode, one map, one agent and two weapons; Milestones 3–5 added
modes, weapons/attachments and agents (Striker, Vigil, Lifeline — see [AGENT_DESIGN.md](AGENT_DESIGN.md)).
The long-term mode list (Duel, Deathmatch, Capture the Area, Weapon Game, Training Grounds) is in [ROADMAP.md](ROADMAP.md).

## Game modes

Selected on the dedicated server with `-mode <id>` (default `heavybomb`). All values live in
`ScriptableObjects/GameModes/*.asset`; time limit, score limit and respawn delay can be overridden per server
(`-timeLimit`/`-roundTime`, `-scoreLimit`/`-roundsToWin`, `-respawnDelay`).

| Mode (`-mode`) | Teams | Players | Structure | Win condition | Respawn | Abilities | Downed state |
|---|---|---|---|---|---|---|---|
| HeavyBomb (`heavybomb`) | 2 | 10 | rounds (100 s) | first to 7 rounds | next round | yes | yes |
| Duel (`duel`) | 1v1 | 2 | rounds (60 s) | first to 5 rounds | next round | yes | no |
| Deathmatch (`deathmatch`) | free-for-all | 15 | one live phase (10 min) | first to 25 kills, else most kills | 3 s | yes | no |
| Capture the Area (`capture`) | 2 | 10 | one live phase (8 min) | first to 100 points, else more points | 5 s | yes | yes |
| Weapon Game (`weapongame`) | free-for-all | 12 | one live phase (10 min) | kill with the last ladder weapon, else highest level | 2 s | no | no |

Agents (B panel, warmup/pre-round, otherwise next spawn) and the downed state / revives: [AGENT_DESIGN.md](AGENT_DESIGN.md).

Common flow for all modes: warmup (free respawns) → countdown when enough players are present (team modes: one per
team; free-for-all: two) → freeze time → live → result. Round modes repeat freeze/live/post-round until the score
limit; continuous modes have one live phase. Players joining a continuous mode spawn immediately; in round modes they
spectate until the next round.

### Duel
- One player per team; spawn ends alternate every round.
- Elimination wins the round; both dead at once = draw (no point).
- Timeout: the player with more remaining health + armor + barrier wins; equal = draw.

### Deathmatch
- Every player is hostile to every other (no teams, no friendly fire question).
- A kill scores 1. Spawns use 17 free-for-all points; the server picks the one farthest from all living players.
- Tie-break for "most kills" when time expires: fewer deaths, then earlier join.

### Capture the Area
- One zone between the mid pillar and the centre building (16 × 4.5 m).
- Alone in the zone, a team captures it in 6 s (+50 % speed per extra player, max ×2). A team that finds partial
  enemy progress first reverts it. Both teams inside = contested (nothing changes).
- The controlling team scores 1 point/s while not contested. Players inside at the moment of capture get +1 score.
- Alpha spawns south, Bravo north; both routes to the zone are ~52–59 m.

### Weapon Game
- Ladder (data in the asset, 8 levels): Assault Rifle → Shotgun → Precision Rifle → Sniper Rifle →
  Assault Rifle + suppressor → Revolver → Pistol → Pistol + suppressor.
- Each kill with a weapon moves the killer to the next level immediately (weapons reset, position and view kept).
  A kill on the last level wins. Abilities, the loadout panel and the suppressor toggle are disabled.

## HeavyBomb

Two teams of up to five (Alpha, Bravo). Each round one team attacks and one defends; sides swap after
`roundsPerHalf` rounds (default 6). First team to `roundsToWin` (default 7) wins the match; with a swap after 6 the
maximum is 13 rounds, so a match can never end in a draw.

All values live in `ScriptableObjects/GameModes/HeavyBomb.asset` and can be overridden per server via command line.

| Setting | Default |
|---|---|
| Players per team / max players | 5 / 10 |
| Minimum players per team to start | 1 (dev: `-devSoloStart` allows 1 player total) |
| Match start countdown | 5 s |
| Pre-round (freeze / buy time) | 6 s |
| Round time | 100 s |
| Plant duration | 4 s |
| Fuse (plant → detonation) | 40 s |
| Defuse duration | 7 s |
| Defuse range | 1.9 m |
| Bomb pickup range | 1.5 m |
| Max speed while planting/defusing | 0.6 m/s |
| Post-round | 6 s |
| Match-over screen | 12 s, then back to warmup |
| Friendly fire | off (teammates stop bullets but take no damage) |

### Match phases (server state, replicated)

1. **WaitingForPlayers (warmup)** — players spawn at their side's spawn, can fight, use abilities and change loadout;
   dead players respawn after 3 s. No bomb.
2. **Countdown** — enough players are present; the match starts when it ends (falls back to warmup if players leave).
3. **PreRound** — everyone respawns at full health/ammo/charges; movement and firing are frozen, looking and the
   suppressor toggle are allowed. A random attacker receives the bomb.
4. **Live** — the round is played; the server evaluates the end conditions every frame.
5. **PostRound** — result banner; movement and firing allowed, abilities not.
6. **MatchOver** — victory/defeat screen with the scoreboard, then warmup.

Players who join mid-round spectate until the next round. If fewer than the required players remain after a round,
the server returns to warmup. If every player leaves during a round, the server returns to warmup immediately.

### Bomb states

`Inactive → Carried ⇄ Planting → Planted ⇄ Defusing → Defused | Exploded`, plus `Carried ⇄ Dropped`.

- **Carried** — the carrier must be alive, an attacker, inside a site, on the ground and nearly still, and hold F.
- **Planting** — cancelled (bomb stays with the carrier) if the carrier releases F, moves, leaves the site or dies.
  A carrier who dies or disconnects drops the bomb (planting is cancelled first). Any living attacker walking within
  1.5 m picks it up.
- **Planted** — the fuse runs; the round timer no longer applies.
- **Defusing** — one defender at a time, within 1.9 m, nearly still, holding F. Cancelling loses all progress;
  the fuse keeps running.
- Every transition is validated on the server by `BombStateMachine`; invalid, duplicate, out-of-range,
  wrong-site, dead-player and after-round requests are rejected without side effects.

### Round end conditions (first match wins)

| # | Condition | Winner | Reason |
|---|---|---|---|
| 1 | Bomb exploded | Attackers | BombExploded |
| 2 | Bomb defused | Defenders | BombDefused |
| 3 | Bomb planted and all defenders dead | Attackers | DefendersEliminated |
| — | Bomb planted otherwise | round continues; the timer is ignored | |
| 4 | Not planted, all attackers dead (also: both sides dead at once) | Defenders | AttackersEliminated |
| 5 | Not planted, all defenders dead | Attackers | DefendersEliminated |
| 6 | Not planted and time expired (an unfinished plant does not count) | Defenders | TimeExpired |

Tie-breaking and edge cases:
- **Simultaneous elimination before the plant** → defenders win (the attackers failed to plant).
- **Simultaneous elimination after the plant** → attackers win (nobody can defuse).
- **Defuse vs. detonation** → a defuse that completes at or before the detonation instant wins.
- **Timeout during planting** → defenders win.
- **Death during planting/defusing** → the action is cancelled the same server frame.
- **Disconnects** count as deaths for elimination; the bomb is dropped or the defuse cancelled.
- A side that had **zero players when the round went live** cannot be "eliminated" (development matches with
  reduced team sizes); the round then ends by bomb or timer.
- The detonation kills every living player within 12 m (visual drama; it happens after the round is already decided).

## Weapons, attachments and loadout (Milestone 4)

- **Weapons:** Assault Rifle, Shotgun, Precision Rifle, Sniper Rifle (primary); Pistol, Revolver (secondary).
  Values and attachment effects: WEAPON_BALANCING.md.
- **Aim down sights (right mouse):** tighter spread, zoom (field of view), slower movement, no sprinting.
  Mouse sensitivity scales with the zoom so the on-screen turn rate stays the same.
  Iron sights for weapons without an optic; red dot / holographic reticles are drawn at screen centre;
  magnified scopes (4×, built-in 8× on the sniper) show a full-screen scope and hide the weapon model.
- **Laser sight:** tighter hip-fire spread, but its beam and dot are visible to every player.
- **Loadout panel (B):** pick both weapons and one attachment per slot (muzzle, underbarrel, optic) from what the
  weapon allows. The choice is saved on the client and sent on every join. It applies immediately in warmup and the
  buy phase, otherwise on the next spawn. T still toggles the suppressor during the buy phase.
- **Reticle settings (pause menu):** colour (optic default, red, green, cyan, yellow, white) and shape
  (optic default, dot, ring + dot, cross).

## Map — Horizon Depot (graybox)

Additions for Milestone 3: 17 free-for-all spawn points (lanes, junction, sites, both spawn areas, corridors),
the capture zone outline in the junction, and test-bot routes to the zone.

72 × 98 m, fully enclosed. Generated by `MapBuilder.cs`.

```
                N (defender spawn, z≈44)
   ┌───────────────────────────────────────┐
   │  [DEF SPAWN]   shields   [DEF SPAWN]  │
   │████ corridor ███████████ corridor ████│  z 30..36
   │  SITE A   ┌────────────┐    SITE B    │
   │  (cover)  │  CENTRE    │   (cover)    │  z 8..30
   │           └────────────┘              │
   │ west lane │  junction / mid pillar │ east lane │  z 2..12
   │           ███ mid lane ███            │  z -30..2
   │  [ATK SPAWN]  shield   [ATK SPAWN]    │
   └───────────────────────────────────────┘
                S (attacker spawn, z≈-44)
```

- **Two sites** (A north-west, B north-east) with painted outlines, a 2.6 m central cover block and crates;
  giant block letters "A"/"B" on the north walls and smaller ones at the lane entrances.
- **Three attacker routes** (west lane → A, east lane → B, mid lane → junction → either site) and two defender
  corridors, so every site has at least two approaches.
- **Cover heights**: 1.0 m (crouch), 1.6 m (standing), 2.6 m+ (full).
- **No spawn-to-spawn sightlines**: the lane blocks, the centre building and the site cover blocks were checked
  against straight lines between the spawns (see `MapBuilder` layout notes); spawn shields sit in front of exits.
- All gameplay geometry has box colliders; boundary walls are 5.5 m.
- Test-bot routes (`BotRoute`) define spawn → site paths used by automated tests.

## Health and shields (Striker; other agents in AGENT_DESIGN)

| Layer | Max | Behaviour |
|---|---|---|
| Barrier (shield layer 2) | 25 | absorbs first; regenerates 10/s after 4 s without damage; any damage restarts the delay |
| Armor (shield layer 1) | 25 | absorbs second; never regenerates during a round |
| Health | 125 | absorbs last; 0 = death |

Each layer is depleted independently and overflow carries into the next layer within the same hit
(a 165 rifle headshot from full leaves 10 health). Healing never exceeds max health and is impossible while dead
or downed. In modes with the downed state, health 0 from a weapon downs the player instead (AGENT_DESIGN).
Everything is restored at every spawn. In HeavyBomb rounds there is no respawn; in warmup players respawn after 3 s.

## Movement

Run 5.2 m/s, sprint 6.8 m/s (forward only, blocks firing), crouch 2.4 m/s (height 1.8 → 1.2 m), jump 1.05 m.
Movement is frozen in pre-round and match-over, and for dead players.
