# Agent Design

## Framework

An agent is data, not code: `AgentDefinition` (ScriptableObject) holds identity, lore, `HealthProfile`,
`MovementSettings`, three ability slots and presentation references. The player controller, weapons and
`AbilityController` read the agent definition, so a new agent is a new asset; new abilities are new
`AbilityDefinition` subclasses.

| Field | Purpose |
|---|---|
| `agentId`, `codename`, `callsign`, `realName`, `origin`, `role`, `biography` | identity and lore |
| `unlockedByDefault` | locked agents need a server unlock (`-unlockAgents`) until progression exists |
| `health` | health and two shield layers (see GAME_DESIGN) |
| `movement` | speeds, acceleration, heights, look limits |
| `utility1`, `utility2`, `signature` | `AbilityDefinition` references |
| `accentColor`, `selectVoiceLine` | presentation (voice line unused yet) |

### Agent selection (Milestone 5)

- `GameContent.agents` lists every agent (append only; players pick by index, Striker stays index 0).
- `PlayerAvatar.AgentIndexValue` (replicated byte) is the agent played this life; `PlayerAvatar.Agent`, health,
  movement and abilities all read it, so changing it changes the agent without any controller code.
- Clients ask with `RequestAgentRpc(index)` (owner only, 5/s). The server refuses unknown, **locked** or **banned**
  agents with a reason (toast) and otherwise stores the choice. It applies at once while loadout changes are allowed
  (warmup, countdown, pre-round; health and abilities restart), otherwise at the next spawn. At every spawn the
  server checks the choice again (`AgentRules.Resolve`): if it became unavailable the player gets the first available agent.
- Availability is two server-set bit masks replicated by the `MatchManager`: unlocked (`unlockedByDefault` plus
  `-unlockAgents all|id,id`) and banned (`-banAgents id,id`). A ban wins over an unlock. There is no progression
  system yet, so "unlockable" means "the server decides"; a per-player unlock needs accounts (Milestone 7).
- **Agent bans** are a server/session setting. A player-driven ban phase (vote before the match) is not built.
- Selection UI: the B panel ("Agent & Loadout") shows every agent with LOCKED/BANNED states, role, health values
  and abilities. The choice is saved in PlayerPrefs (`bh.agent`, by id) and sent on every join. Bots use
  `-botAgent random|<id>`.
- The choice survives a reconnect (reconnect record) like the loadout.
- Weapons are not restricted per agent; every agent uses the same loadout rules (`LoadoutRules`).

### Ability rules (shared)

- Activation is a **button edge inside the player's input command**; the server processes it while simulating that
  command. Clients never call an "activate" RPC directly.
- `AbilityRules.CanActivate` checks, in order: ability exists, player alive, match phase allows abilities
  (warmup, countdown, live), charges, cooldown; then the controller refuses downed players. Then
  `AbilityDefinition.Execute` runs ability-specific validation.
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

## Lighthouse — "Vigil" (Mara Lindqvist, Sweden) — Milestone 5, unlocked

> A former coastal-ranger scout who mapped hostile shorelines by night. She trusts what she can see and makes
> sure the enemy cannot.

Role: recon / information. Health 115 · Armor 25 · Barrier 25. Slightly faster (run 5.4, sprint 7.1 m/s).
Lore is placeholder (the brief only defines Striker).

### Q — Flash Grenade (utility, 2 charges per round, 1 s cooldown)

- Same thrown projectile as the smoke (`ThrownGrenadeDefinition` base, shared ballistic `SmokeGrenadeProjectile`):
  19 m/s + 3 m/s up, fuse 0.9 s. The launch carries a `GrenadeKind` so clients draw a yellow grenade.
- On detonation the **server** checks every standing player: line of sight from their eye to the flash (world
  geometry and smoke block it), distance ≤ 22 m and the angle between their view and the flash.
  `FlashRules.Intensity` = facing factor (1 within 50°, falling to 0.2 at 150°+) × distance factor (1 → 0.3 at the
  radius); below 0.15 nothing happens. Blind time = intensity × 3.2 s.
- It blinds **everyone** who looks at it — enemies, teammates and the thrower.
- The victim's client gets `FlashedRpc(intensity, duration)` and draws a white screen: full for 40 % of the time,
  then a fade (`FlashRules.Whiteness`). All clients see a bright light burst and hear a bang.
- **Limitation:** blindness is presentation on the victim's client; the server does not stop a blinded player from
  shooting. A modified client could ignore it (same class of problem as smoke, L-05).

### E — Smoke Grenade (2 charges per round)

The same ability asset as Striker's smoke.

### X — EnemyAlert (signature, unlimited, 35 s cooldown)

- Scan pulse from Vigil's position: every living enemy within 30 m (through walls) is marked for 4 s.
- Observers are Vigil's team (only Vigil in free-for-all); the server sends `RevealedRpc` to exactly those clients,
  so other clients never learn the positions. Marked players get a "Spotted by EnemyAlert" warning.
- Observers see a red diamond with the distance above each marked enemy; everyone sees a red pulse sphere.

## Kintsugi — "Lifeline" (Aiko Mori, Japan) — Milestone 5, locked by default

> A disaster-relief paramedic who learned to work under fire. Nobody on her team stays down for long.

Role: support / revives. Health 125 · Armor 15 · Barrier 40 (regenerates 12/s). Slightly slower (5.1 / 6.7 m/s).
Locked: a server must start with `-unlockAgents lifeline` (or `all`). Lore is placeholder.

### Q — Smoke Grenade, E — Flash Grenade

Shared ability assets (see above).

### X — Revival (signature, unlimited, 40 s cooldown)

- Instantly revives the downed teammate in line of sight within 9 m that is closest to the aim direction, with 75
  health. Refused without a target ("No downed teammate in range"), so the cooldown is not spent.

## Downed state and revives (all agents)

Enabled per mode (`GameModeDefinition.downedState`): **HeavyBomb** and **Capture the Area**. Off in Duel, Deathmatch
and Weapon Game (no teammates). Override with `-downed on|off`.

- Lethal **weapon** damage while the match is live downs the player instead of killing them — if at least one
  teammate is still standing. Explosions and world damage kill outright; the last player standing dies.
- Downed players stay alive for the rules (they count for round elimination), crawl (forced crouch, 35 % speed)
  and cannot shoot, aim, interact, plant/defuse or use abilities. Their hitboxes stay active.
- They bleed out after 15 s. Damage shortens it: 100 damage removes the full 15 s.
- Any standing teammate revives by holding F within 1.8 m for 4 s (comes back with 30 health, armor as it was).
  Letting go or moving away cancels; the closest teammate holding F takes over.
- If every remaining player of a team is downed, nobody can revive them: they all die at once.
- The kill (score, kill feed) goes to whoever downed the player, whether they bleed out or are finished.
- HUD: red vignette and bleedout countdown for the downed player; green diamonds with bleedout/revive progress
  above downed teammates; "Hold F to revive" prompt with a progress bar; scoreboard shows DOWN.
- **Limitation:** a downed bomb carrier keeps the bomb until death (it drops then).
