# Agent Design

## Framework

An agent is data, not code: `AgentDefinition` (ScriptableObject) holds identity, lore, `HealthProfile`,
`MovementSettings`, three ability slots and presentation references. The player controller, weapons and
`AbilityController` read the agent definition, so a new agent is a new asset; new abilities are new
`AbilityDefinition` subclasses.

| Field | Purpose |
|---|---|
| `agentId`, `codename`, `callsign`, `realName`, `origin`, `biography` | identity and lore |
| `unlockedByDefault` | hook for future unlocking/progression |
| `health` | health and two shield layers (see GAME_DESIGN) |
| `movement` | speeds, acceleration, heights, look limits |
| `utility1`, `utility2`, `signature` | `AbilityDefinition` references |
| `accentColor`, `selectVoiceLine` | presentation (voice line unused yet) |

Agent selection is not in the slice: `GameContent.defaultAgent` is used for every player. Adding selection means
replicating an agent index on `PlayerAvatar` — no controller changes.

### Ability rules (shared)

- Activation is a **button edge inside the player's input command**; the server processes it while simulating that
  command. Clients never call an "activate" RPC directly.
- `AbilityRules.CanActivate` checks, in order: ability exists, player alive, match phase allows abilities
  (warmup, countdown, live), charges, cooldown. Then `AbilityDefinition.Execute` runs ability-specific validation.
  A rejection consumes nothing; the owner receives a reason (shown as a toast with a "denied" sound).
- `maxCharges > 0` → limited uses per round (restored at every spawn). `maxCharges == 0` → unlimited, cooldown only.
- Attempts are rate-limited to one per 0.2 s per slot before any physics query runs.
- Slot state (charges, cooldown end in server time) is replicated; the HUD shows charges, cooldown sweep and seconds.

## WatchDog — "Striker" (Thomas Schmidt, Germany)

> A former Bundeswehr soldier and tactical specialist who led a special operations unit before a classified
> operation resulted in its dissolution.

Role: front-line controller who isolates angles and sustains through trades.
Health 125 · Armor 25 · Barrier 25 (regenerating).

### Q — Smoke Grenade (utility, 2 charges per round, 1 s cooldown)

- Thrown from the eye: 17 m/s along the aim + 2.5 m/s up + half the player's velocity; never starts inside a wall.
- Ballistic flight with gravity 18 m/s² and bounces (restitution 0.35, friction 0.6) against world geometry.
  Server and clients integrate the same path from the replicated launch parameters — no transform streaming.
- Fuse 1.6 s, then the **server** spawns a smoke cloud: radius 4.5 m, grows in over 0.8 s, lasts 14 s, fades over
  the final 1.5 s, despawned by the server.
- **Visibility model (slice):** layered double-sided translucent shells plus a screen fog when the camera is inside.
  It is a visual obstruction only — the server's hit detection ignores smoke (bullets pass through, as in most
  tactical shooters). There is no server-side visibility culling, so a modified client could remove the smoke.

### E — Wayblocker (utility, 1 charge per round)

- Server finds placement: the aim ray's ground hit within 6 m (slope ≤ 25°), or the ground below the aim point /
  in front of a hit wall. The wall faces the player.
- Rejected when out of range, on steep ground, or overlapping world geometry or any player (box check slightly above
  the floor).
- 3.2 × 2.1 × 0.35 m, 400 health, 30 s lifetime. Blocks movement (Deployable layer collides with characters) and
  bullets; destructible by gunfire (takes torso damage). Team-coloured; colour shifts to red as it loses health.
- Despawned by the server on destruction, expiry or round reset.

### X — Field Heal (signature, unlimited, 30 s cooldown)

- Instantly restores 50 health, capped at max health. Refused at full health (no cooldown spent) and while dead.

## Planned abilities (architecture-ready)

EnemyAlert (reveal), Flash Grenade (projectile + screen effect), Revival (target a downed teammate) will be new
`AbilityDefinition` subclasses using the same context (owner, eye position, aim, server time) and slot rules.
Revival will need a "downed" life state in `PlayerAvatar`.
