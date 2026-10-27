# Technical Architecture

## Stack decision

- **Netcode for GameObjects 2.11.0 + Unity Transport 2.7.2** (the versions bundled with Unity 6000.3.13f1).
  GameObject-based, modest player count (10), fits the team's iteration speed. Netcode for Entities was not chosen:
  no measured requirement justifies its complexity (the 10-player server idles at its 120 Hz cap, see TESTING).
- **Client–server topology, dedicated authoritative server.** The listen-server *host* mode exists for quick local
  checks only.
- Tick rate **60 Hz** (`NetworkConfig.TickRate`). Dedicated servers cap the frame rate at 120 Hz.

## Process roles

`NetworkBootstrap` (on the NetworkManager in the `Bootstrap` scene) resolves `LaunchConfig`
(defaults → `server.json` → command line). Server builds are compiled with `UNITY_SERVER` and always run as server.

```
Bootstrap scene (DontDestroyOnLoad: NetworkManager+NetworkBootstrap, UI, EventSystem)
 ├─ Server: StartServer → NGO loads "VerticalSlice" for everyone → spawn MatchManager
 ├─ Client: StartClient → NGO synchronizes the map scene → server spawns our PlayerAvatar
 └─ Menu:   load "MainMenu" backdrop; UI offers Connect / Host (dev) / Quit
```

Connection approval checks protocol version (`ConnectionPayload.CurrentProtocol`), payload size/format and the
player limit, and sanitizes the display name. No credentials exist in client or server builds.

## Server-authoritative simulation

### Input commands

The only gameplay data a client sends for its character is `PlayerInputCommand`: sequence number, quantized move
axes, view yaw/pitch, button bits. Commands are sent every tick unreliably, bundled with the previous three for
loss tolerance (`InputBundle`).

On the server (`PlayerSimulation.SubmitInputRpc`) commands are accepted only from the owning client, only with a
higher sequence number, and only up to a 24-command queue. Each tick the server processes at most 3 commands and
spends one token per command from a budget refilled by one token per tick (max 6). **A client therefore cannot
simulate faster than real time** (no speed hacks), and floods are bounded and cheap. Commands are sanitized
(pitch clamp, non-finite values, unknown button bits) before use.

If a client sends nothing for **5 s** the server disconnects it (input-silence timeout) instead of waiting for the
30 s transport timeout. More than 180 input messages per second (clients send ~60) are dropped and logged as a
`[BH][Security]` anomaly (at most once per 10 s).

**Input starvation:** when no command arrives for more than 6 ticks and the character is airborne, the server keeps
applying gravity (empty command, no sequence consumed) so a lagging player cannot hang in the air. The owner is
corrected when its input resumes.

### Movement model — client prediction + server reconciliation

- `PlayerMotor` is a shared deterministic-enough step on a `CharacterController` (acceleration, gravity, jump,
  crouch with headroom check, wall-velocity clipping).
- **Owner:** simulates each command immediately (prediction), records the result in a 256-entry history, renders
  the camera interpolated between predicted ticks.
- **Server:** simulates the same commands authoritatively and sends the owner its full state for the last processed
  sequence (position, velocity, crouch, weapon state) every tick (unreliable).
- **Reconciliation:** if the predicted state for that sequence differs (position > 2 cm, vertical velocity,
  ammo/slot/reload), the owner rewinds to the server state and replays newer commands. Corrections are counted and
  logged (`corrections` in the perf line). Errors under 2 m are **blended out visually** (camera offset decaying at
  rate 15/s); larger ones snap. Server teleports (spawns, round start) use a reliable RPC with a teleport id; stale
  unreliable states are ignored.
- **Other players:** snapshots at 30 Hz, rendered **100 ms behind the newest snapshot** with interpolation. The
  render clock (`SnapshotClock`) is derived from the snapshot timestamps themselves (exponentially smoothed offset to
  the local clock); Netcode's client `ServerTime` was measured to trail the snapshot stream by ~90 ms, which would have
  added that much display delay. If snapshots run out (loss), positions are extrapolated for at most 100 ms.
- Clients can never set positions; the server only moves characters by simulating commands or by its own teleports.

Known limitation: remote characters' colliders are at interpolated (past) positions on clients, so player-vs-player
bumping mispredicts slightly (visible as corrections in crowded load tests and under latency).

### Weapons and hit registration

- `WeaponSimulation` (ammo, cadence, reload, switching, spread, recoil) runs inside the same command step on the
  owner (prediction for instant feedback) and on the server (authority). Weapon state is part of reconciliation.
- When the server's simulation fires, `CombatSystem.ServerResolveShot` raycasts from the server's eye position along
  the server-computed direction (view input + deterministic spread + recoil). Clients never send targets, hit zones
  or damage. The first valid hitbox stops the shot; own hitboxes are skipped; teammates block without damage.
- The shooter gets `HitConfirmRpc` (hit marker / headshot / kill); other clients get `ShotFxRpc` (tracer, sound,
  impact). The owner's own tracer is drawn from a visual-only raycast.
- **Lag compensation (hitbox rewind).** Every server tick each character records its pose (server time, position,
  crouch) in a 72-entry ring buffer (`PoseHistory`, ~1.2 s). Each input command carries the server time the client was
  rendering others at (`viewTime`). When a shot resolves, the server clamps that time
  (`LagCompensation.ClampViewTime`: not in the future, not older than **300 ms** (`-maxRewind`), not more than 50 ms
  older than the player's previous accepted view time), moves every other living character's hitboxes to their
  interpolated pose at that time, raycasts, and restores them. World geometry and deployables are not rewound.
  Measured average rewind: 123 ms at 0 ms RTT, 167/227/266 ms at 50/100/150 ms RTT (see TESTING). Beyond ~200 ms RTT the
  300 ms cap compensates only partially, by design, to bound "shot behind cover" effects for the victim.
  `-noLagComp` disables it for comparison.

### Game modes

`MatchManager` owns everything that is the same in every mode: joins and team assignment (or free-for-all),
spawn selection (team spawns by side; free-for-all spawns farthest from living players), warmup / countdown /
freeze / live / post-round / match-over phases, respawn timers, team and player scores, results, reconnect records
and the kill feed. A mode is a `GameModeDefinition` asset plus a server-only `GameModeRules` object it creates:

| Hook | Used by |
|---|---|
| `RoundBased` | HeavyBomb, Duel (true); others run one live phase |
| `OnRoundStarting` (set sides) / `OnRoundStarted` / `OnLive` | HeavyBomb bomb carrier, Duel spawn alternation, alive counts |
| `TickLive(now, dt, timeExpired)` → `ModeOutcome` (continue / round over / match over) | every mode |
| `OnPlayerKilled` / `OnPlayerLeft` | kill scoring, weapon ladder, bomb drop |
| `LoadoutFor(player)` | Weapon Game ladder |

Modes talk back through `IMatchHost` (team score, bomb/zone state publishing, loadout refresh). Pure rule helpers
(`HeavyBombRules`, `DuelRules`, `FreeForAllRules`, `ZoneModel`, `WeaponGameRules`, `TeamRules`, `SpawnSelector`) are
unit-tested. Hostility is decided by `TeamRules.AreEnemies` (free-for-all: everyone; team modes: the other team) and is
used for kill credit, bot targeting and spawn safety; friendly fire uses the existing team check (free-for-all players
have no team, so all damage applies).

Loadouts are per player: `PlayerAvatar.LoadoutValue` (weapon indices into `GameContent.weapons`) plus the suppressor
mask, replicated so every peer computes identical weapon stats. A mid-life loadout change resets weapons through the
reliable teleport path without moving the player or changing their view.

Replicated match state: mode index, phase and end time, round number, team scores, attacking team, last round
winner and reason text, match winner (team and name), bomb state, zone state (`ModeStatus`), score and time limits.

### Health, abilities, objective, match

| Module | Authority | Replication |
|---|---|---|
| `PlayerHealth` + `HealthModel` | server model | NetworkVariables health/armor/barrier; owner damage-direction RPC |
| `AbilityController` + `AbilityDefinition`s | server, from command button edges | NetworkVariable per slot (charges, cooldown end) |
| `SmokeGrenadeProjectile` / `SmokeCloud` / `Wayblocker` | server spawns/despawns | spawn parameters as NetworkVariables set in `OnNetworkSpawn` |
| `MatchManager` | server phase machine, teams, spawns, score | NetworkVariables (phase, end time, scores, sides, bomb state); kill feed & explosion RPCs |
| `BombStateMachine` | server, pure C# | `BombNetState` NetworkVariable |
| `HeavyBombRules`, `MatchRules`, `TeamAssignment`, `SpawnSelector` | pure functions | — |

Pure rule classes have no engine/network dependencies and are covered by EditMode tests.

## Security boundary

| Client may | Client may not |
|---|---|
| send input commands (bounded, sanitized, rate-limited) | set position, velocity or rotation |
| predict movement/ammo for responsiveness | decide hits, hit zones, damage or kills |
| render effects and play sounds | change health, shields, charges, cooldowns, ammo |
| request loadout toggle via a button (accepted only in warmup/buy time) | change bomb state, score, round or match result |
| choose its display name (sanitized, 16 chars) | exceed 10 players, connect with another protocol version |

Remaining trust: view angles are input (aim assistance/aimbots are a client-integrity problem, not solved here);
smoke and other visibility are client-side (wallhack-style information is available to a modified client).

## Network simulation

`IcmpSafeUdpInterface` can add latency, jitter and loss in both directions (`-simLatency ms` = added RTT,
`-simJitter ms`, `-simLoss %`). Any process with these flags uses the managed socket; `Tools\latency-matrix.ps1`
applies them to the bot clients to measure hit registration under latency.

## Reconnect

When a player disconnects during a running match, the server keeps their team, kills/deaths/objective score and
loadout for 180 s (`ReconnectRegistry`, keyed by display name; there are no accounts yet). Rejoining with the same name
in the same match restores them; the player spectates until the next round. After an unexpected disconnect the client
menu shows **Reconnect to <server>**.

## Network transport on Windows (`IcmpSafeUdpInterface`)

On Windows servers the Unity Transport driver is created with a managed UDP socket that disables
`SIO_UDP_CONNRESET` (`IcmpSafeDriverConstructor`, Netcode's default pipelines are kept). Reason: with the stock
socket, killing one client process caused ICMP "port unreachable" replies that stalled the server's reception from
**all** clients within 2–10 s (reproduced; RISKS.md R-07). The managed socket runs synchronously on the main thread
and allocates per packet (~14 gen-0 GCs per 15 s with 10 players, heap 3 MB) — fine for 10 players, to be optimized
before larger servers. Linux servers would use the stock socket (Linux does not report ICMP errors on unconnected
UDP sockets). `-defaultSocket` switches back for comparison.

## Sessions, matchmaking and hosting

Kept behind two small interfaces so a provider can be added without touching gameplay:

- `ISessionConnector` (client): "find a match" → `ServerEndpoint`. The slice ships `DirectSessionConnector`
  (typed address). A production connector would: create/join a player session or matchmaker ticket → wait for the
  match assignment → wait for a dedicated server allocation → return its IP/port → `NetworkBootstrap.StartClient`
  (unchanged) → `LeaveAsync` on disconnect. A lobby by itself does **not** allocate a dedicated server.
- `IServerHostingAdapter` (server): `OnServerStarted`, `OnReadyForPlayers`, `OnPlayerCountChanged`,
  `OnMatchCompleted`, `OnShutdown`. The slice ships `LocalServerHostingAdapter` (logging). A provider adapter maps these
  to its SDK (ready-for-players, allocation payload → match config, deallocation on completion).

Candidates evaluated for Milestone 7: Unity Multiplayer Services (sessions/matchmaker, `com.unity.services.multiplayer`
2.1.3 is bundled with the editor) for player sessions and matchmaking, combined with a dedicated game-server
fleet provider for allocation (Unity has announced changes to its own Multiplay Game Server Hosting, so its
availability and alternatives such as Edgegap, AWS GameLift or Agones-based self-hosting must be re-verified when the
integration starts — this was not evaluated hands-on in the slice). No credentials are stored in this repository.

## Assemblies and folders

- `BlueHorizon.Runtime` — all runtime code (namespaces per module). Unsafe code is enabled only for the socket shim.
- `BlueHorizon.Editor` — `ProjectSetup` (layers, physics matrix, player settings), `ContentFactory` (materials, data
  assets, network prefabs, panel settings), `MapBuilder` (scenes and map), `BuildTools` (builds).
- `BlueHorizon.Tests.EditMode` — NUnit tests.

Network prefabs (player, match manager, smoke grenade, smoke cloud, wayblocker) are registered through
`Settings/NetworkPrefabs.asset`. Prefabs are thin: hitboxes and graybox visuals are built in code at runtime
(`PlayerRig`); visuals are skipped on headless processes.

## Presentation (client only)

`HudController` (UI Toolkit, built in code + `BlueHorizon.uss`), `GameCamera` (first person, placeholder viewmodel,
spectator), `PlayerPresenter` (team colours, remote pose, effects), `BombPresenter`, `Effects` (pooled tracers,
flashes, impacts, explosion), `GameAudio` (placeholder clips synthesized at startup, pooled 3D sources).
None of these hold gameplay state.

## Diagnostics

- `[BH][Category] message @HH:mm:ss.fff` log lines.
- `PerformanceMonitor`: frame time avg/p99/max, GC count, heap; server: players and inputs received per client;
  client: RTT, prediction corrections, authoritative states received.
- `ScreenshotRecorder` (`-screenshotInterval`, `-screenshotDir`) for visual review of graphical runs.
- `TestBotInputSource` (`-bot`, `-botMode fight|passive|idle`): scripted *input* driver for automated tests; it goes
  through the same server validation as a human.
