# Risks, Known Issues and Limitations

Status as of 2026-10-26 (vertical slice, milestone 1).

## Known limitations (by design for the slice)

| ID | Limitation | Impact | Plan |
|---|---|---|---|
| L-01 | **No lag compensation.** Shots are tested against current server positions. | Shooters with latency must lead moving targets by ~RTT/2 + 100 ms. Not competitive-grade. | Milestone 2: hitbox history + rewind to the shooter's view time, bounded rewind window. |
| L-02 | No snapshot extrapolation; corrections snap instead of blending. | Visible pops under packet loss / heavy mispredictions. | Milestone 2: error smoothing, extrapolation cap. |
| L-03 | Remote players' colliders are at interpolated positions on clients. | Player-vs-player contact mispredicts (load test: up to ~40 corrections per 30 s for some bots in crowded spawns). | Exclude remote characters from owner prediction collisions or predict them. |
| L-04 | Server does not simulate a player whose input stops (no gravity during input starvation). | A lagging player hangs in place until input resumes or the 5 s timeout drops them. | Simulate empty commands after N starved ticks. |
| L-05 | Smoke is a client-side visual obstruction; no server visibility culling. | A modified client can see through smoke; bullets pass through smoke (intended). | Server-side relevancy/visibility for competitive integrity. |
| L-06 | View angles are trusted input. | Aimbots are possible; this is an anti-cheat problem, not addressed. | Anti-cheat evaluation (Milestone 7). |
| L-07 | Placeholder art, animation and audio (synthesized at runtime). | Presentation is graybox-level. | Art/audio pass (Milestone 7). |
| L-08 | No minimap, so the suppressor's "no minimap indicator" effect has no visible consequence yet. | — | Implement with the minimap. |
| L-09 | Single agent; no agent selection UI; no weapon purchase/economy. | — | Milestones 4–5. |
| L-10 | Matchmaking, cloud allocation and player persistence are interfaces only (`ISessionConnector`, `IServerHostingAdapter`). | Players connect by IP. | Milestone 7. |
| L-11 | Latency/packet-loss behaviour has not been measured (tests run on localhost). | Unknown feel on real networks. | Milestone 2: run with the Multiplayer Tools network simulator or an OS-level shaper. |
| L-12 | Linux dedicated server build target is wired up but not built or run. | — | Milestone 2. |
| L-13 | The managed ICMP-safe socket allocates per packet (~1 gen-0 GC/s at 10 players). | Negligible at 10 players (heap ~3 MB). | Pool endpoints / use `SocketAddress` APIs before scaling up. |
| L-14 | Scenes are generated; hand edits are overwritten by the generator. | Level designers must edit `MapBuilder.cs` (or stop regenerating the map). | Convert the map to an authored scene when art production starts. |
| L-15 | The pause menu does not pause the match (online game); there is no reconnect-to-match flow. | — | Reconnect support in Milestone 2. |

## Issues found and fixed during the slice

| ID | Issue | Root cause | Fix / verification |
|---|---|---|---|
| R-07 | Killing one client process caused **all** other clients to be dropped ~2–10 s later. | Windows: after the client's port closes, every server packet to it triggers ICMP "port unreachable"; with the stock Unity Transport socket the server then stopped receiving from everyone. Suspending (not killing) the client did not reproduce it, which isolated ICMP as the trigger. | `IcmpSafeUdpInterface` (managed socket with `SIO_UDP_CONNRESET` disabled) on Windows servers + 5 s input-silence timeout. Verified: after a kill the remaining clients keep sending 60 inputs/s and the match completes (disconnect scenario). |
| R-08 | Rounds kept running with zero players after everyone disconnected. | End-of-round only checked elimination. | Return to warmup when nobody is connected or too few players remain after a round. Verified in the disconnect repro. |
| R-09 | Netcode warnings "NetworkVariable is written to … before spawned". | Spawn parameters written before `Spawn()`. | Values are now written in `OnNetworkSpawn` on the server. |
| R-10 | Netcode prefab hashes were 0 for prefabs created by script. | `NetworkObject.OnValidate` does not run for prefab assets created in the same editor session. | Generator invokes it explicitly and fails loudly if a hash is still 0. |
| R-11 | Several MonoBehaviours/ScriptableObjects in one file → "missing script" on assets. | Unity requires one serialized class per file with a matching name. | Split into separate files. |
| R-12 | Scoreboard rebuilt every frame (GC spikes in perf logs). | UI rebuilt per frame. | Throttled to 4 Hz. |

## Project risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| Netcode for GameObjects movement/hit-reg model hits limits for competitive play | Medium | Custom prediction already in place; lag compensation next; evaluate Netcode for Entities only with measurements. |
| Hosting provider choice delays production | Medium | Hosting/session code is behind interfaces; local dedicated workflow works today. |
| Windows-only transport workaround | Low | Isolated in one class, opt-out flag `-defaultSocket`; Linux servers unaffected by design. |
| Generated content vs. authored content workflow | Medium | Decide before art production (L-14). |
| Scope creep from the long-term vision | High | Roadmap ordering; acceptance criteria per milestone. |
