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
30 s transport timeout.

### Movement model — client prediction + server reconciliation

- `PlayerMotor` is a shared deterministic-enough step on a `CharacterController` (acceleration, gravity, jump,
  crouch with headroom check, wall-velocity clipping).
- **Owner:** simulates each command immediately (prediction), records the result in a 256-entry history, renders
  the camera interpolated between predicted ticks.
- **Server:** simulates the same commands authoritatively and sends the owner its full state for the last processed
  sequence (position, velocity, crouch, weapon state) every tick (unreliable).
- **Reconciliation:** if the predicted state for that sequence differs (position > 2 cm, vertical velocity,
  ammo/slot/reload), the owner rewinds to the server state and replays newer commands. Corrections are counted and
  logged (`corrections` in the perf line). Server teleports (spawns, round start) use a reliable RPC with a
  teleport id; stale unreliable states are ignored.
- **Other players:** snapshots at 30 Hz, rendered **100 ms in the past** with interpolation.
- Clients can never set positions; the server only moves characters by simulating commands or by its own teleports.

Known limitations: no extrapolation when snapshots run out; small corrections snap instead of blending; remote
characters' colliders are at interpolated (past) positions on clients, so player-vs-player bumping mispredicts
slightly (visible as corrections in crowded load tests).

### Weapons and hit registration

- `WeaponSimulation` (ammo, cadence, reload, switching, spread, recoil) runs inside the same command step on the
  owner (prediction for instant feedback) and on the server (authority). Weapon state is part of reconciliation.
- When the server's simulation fires, `CombatSystem.ServerResolveShot` raycasts from the server's eye position along
  the server-computed direction (view input + deterministic spread + recoil). Clients never send targets, hit zones
  or damage. The first valid hitbox stops the shot; own hitboxes are skipped; teammates block without damage.
- The shooter gets `HitConfirmRpc` (hit marker / headshot / kill); other clients get `ShotFxRpc` (tracer, sound,
  impact). The owner's own tracer is drawn from a visual-only raycast.
- **No lag compensation.** Targets are tested at their current server positions, so a shooter with latency must lead
  moving targets by roughly RTT/2 + 100 ms interpolation. This is not competitive-grade hit registration yet; a
  rewind of hitboxes to the shooter's view time is the first item of Milestone 2.

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
