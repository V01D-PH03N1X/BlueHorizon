# Risks, Known Issues and Limitations

Status as of 2026-10-26 (vertical slice, milestone 1).

## Known limitations (by design for the slice)

| ID | Limitation | Impact | Plan |
|---|---|---|---|
| L-01 | Lag compensation is capped at 300 ms rewind. | Above ~200 ms RTT shooters are only partially compensated; victims can be hit up to 300 ms after reaching cover. | Tune the cap with real playtests; consider per-player caps. |
| L-02 | Correction blending is position-only; view angles are not blended. | Minor. | None planned. |
| L-03 | Remote players' colliders are at interpolated positions on clients. | Player-vs-player contact mispredicts (load test: up to ~40 corrections per 30 s for some bots in crowded spawns). | Exclude remote characters from owner prediction collisions or predict them. |
| L-04 | During input starvation the server only applies gravity (no horizontal extrapolation). | A lagging player stops horizontally until input resumes. | Revisit with real-network data. |
| L-05 | Smoke is a client-side visual obstruction; no server visibility culling. | A modified client can see through smoke; bullets pass through smoke (intended). | Server-side relevancy/visibility for competitive integrity. |
| L-06 | View angles are trusted input. | Aimbots are possible; this is an anti-cheat problem, not addressed. | Anti-cheat evaluation (Milestone 7). |
| L-07 | Placeholder art, animation and audio (synthesized at runtime). | Presentation is graybox-level. | Art/audio pass (Milestone 7). |
| L-08 | No minimap, so the suppressor's "no minimap indicator" effect has no visible consequence yet. | — | Implement with the minimap. |
| L-09 | Single agent; no agent selection UI; no weapon purchase/economy. | — | Milestones 4–5. |
| L-10 | Matchmaking, cloud allocation and player persistence are interfaces only (`ISessionConnector`, `IServerHostingAdapter`). | Players connect by IP. | Milestone 7. |
| L-11 | Latency/jitter/loss measured only with the built-in simulator on one machine (no WAN, no two-machine LAN test). | Real routers/Wi-Fi may behave differently (bursty loss). | Two-machine and WAN playtests. |
| L-12 | Linux dedicated server **builds** (85 MB) but has not been run (no Linux/WSL on the dev PC). It uses the stock socket (Linux does not report ICMP errors on unconnected UDP sockets). | Untested platform. | Run on a Linux VM/host before production. |
| L-13 | The managed ICMP-safe socket allocates per packet (~1 gen-0 GC/s at 10 players). | Negligible at 10 players (heap ~3 MB). | Pool endpoints / use `SocketAddress` APIs before scaling up. |
| L-14 | Scenes are generated; hand edits are overwritten by the generator. | Level designers must edit `MapBuilder.cs` (or stop regenerating the map). | Convert the map to an authored scene when art production starts. |
| L-15 | The pause menu does not pause the match (online game, by design). | — | — |
| L-17 | Weapon Game ladder has only 4 levels from 2 weapons. | Short matches. | Grows with Milestone 4 weapons (data only). |
| L-18 | One map for all modes; Capture zone is closer in route length to Bravo (~59 m) than Alpha (~52 m) by design of the existing layout, Deathmatch spawns are fixed points. | Possible side bias; spawn kills in crowded FFA. | Dedicated layouts per mode when more maps exist; measure win rates. |
| L-19 | Test bots do not step into a contested zone (they fight from outside), so the contested path is verified by unit tests only. | — | Human playtests. |
| L-16 | Reconnect identity is the display name (no accounts). | Someone joining with a disconnected player's name within 180 s inherits their team and stats. | Accounts/session tokens (Milestone 7). |

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
