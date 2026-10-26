using System;
using System.Collections.Generic;
using BlueHorizon.Abilities;
using BlueHorizon.Combat;
using BlueHorizon.Core;
using BlueHorizon.GameModes;
using BlueHorizon.Networking;
using BlueHorizon.Objectives;
using BlueHorizon.Player;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace BlueHorizon.Match
{
    public enum MatchPhase : byte
    {
        /// <summary>Warmup: free respawns, no bomb, abilities and loadout allowed.</summary>
        WaitingForPlayers = 0,
        /// <summary>Enough players joined; match starts when the countdown ends.</summary>
        Countdown = 1,
        /// <summary>Freeze time: look and change loadout only.</summary>
        PreRound = 2,
        Live = 3,
        PostRound = 4,
        MatchOver = 5,
    }

    public struct BombNetState : INetworkSerializable, IEquatable<BombNetState>
    {
        public BombState state;
        public ulong actorId;
        public int siteIndex;
        public Vector3 position;
        public double actionStart;
        public double actionEnd;
        public double detonationTime;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref state);
            serializer.SerializeValue(ref actorId);
            serializer.SerializeValue(ref siteIndex);
            serializer.SerializeValue(ref position);
            serializer.SerializeValue(ref actionStart);
            serializer.SerializeValue(ref actionEnd);
            serializer.SerializeValue(ref detonationTime);
        }

        public bool Equals(BombNetState o) =>
            state == o.state && actorId == o.actorId && siteIndex == o.siteIndex && position == o.position
            && actionStart.Equals(o.actionStart) && actionEnd.Equals(o.actionEnd) && detonationTime.Equals(o.detonationTime);
    }

    public struct KillFeedEntry
    {
        public string killer;
        public Team killerTeam;
        public string victim;
        public Team victimTeam;
        public string weapon;
        public bool headshot;
    }

    /// <summary>
    /// Server-authoritative HeavyBomb match: players, teams, spawns, round flow, bomb objective and score.
    /// Spawned by the server when the map loads; clients observe its replicated state.
    /// Gameplay rules live in <see cref="HeavyBombRules"/>, <see cref="BombStateMachine"/> and <see cref="MatchRules"/>.
    /// </summary>
    public sealed class MatchManager : NetworkBehaviour
    {
        const float WarmupRespawnDelay = 3f;
        const float SpawnClearance = 1.2f;
        const float ExplosionRadius = 12f;

        public static MatchManager Instance { get; private set; }

        public readonly NetworkVariable<MatchPhase> Phase = new();
        public readonly NetworkVariable<double> PhaseEndTime = new();
        public readonly NetworkVariable<int> RoundNumber = new();
        public readonly NetworkVariable<int> ScoreAlpha = new();
        public readonly NetworkVariable<int> ScoreBravo = new();
        public readonly NetworkVariable<Team> AttackingTeam = new(Team.Alpha);
        public readonly NetworkVariable<Team> LastRoundWinner = new();
        public readonly NetworkVariable<RoundEndReason> LastRoundReason = new();
        public readonly NetworkVariable<Team> MatchWinner = new();
        public readonly NetworkVariable<BombNetState> Bomb = new();
        public readonly NetworkVariable<int> RoundsToWin = new();
        public readonly NetworkVariable<float> RoundTimeLimit = new();

        /// <summary>Clients: kill feed entries as they happen.</summary>
        public static event Action<KillFeedEntry> KillFeed;
        /// <summary>Clients: the bomb exploded at a position.</summary>
        public static event Action<Vector3> BombExploded;

        HeavyBombSettings _settings;
        BombStateMachine _bomb;
        readonly List<SpawnPoint> _attackSpawns = new();
        readonly List<SpawnPoint> _defenseSpawns = new();
        readonly List<Vector3> _occupied = new();
        readonly List<Vector3> _candidates = new();
        readonly System.Random _random = new();
        int _attackersAtStart, _defendersAtStart;
        int _spawnCursor;

        public HeavyBombSettings Settings => _settings;

        public bool CanMove => Phase.Value is not (MatchPhase.PreRound or MatchPhase.MatchOver);
        public bool CanFight => Phase.Value is MatchPhase.WaitingForPlayers or MatchPhase.Countdown or MatchPhase.Live or MatchPhase.PostRound;
        public bool AbilitiesAllowed => Phase.Value is MatchPhase.WaitingForPlayers or MatchPhase.Countdown or MatchPhase.Live;
        public bool LoadoutChangesAllowed => Phase.Value is MatchPhase.WaitingForPlayers or MatchPhase.Countdown or MatchPhase.PreRound;
        public bool IsWarmup => Phase.Value is MatchPhase.WaitingForPlayers or MatchPhase.Countdown;

        public float PhaseTimeRemaining => NetworkManager == null ? 0f : Mathf.Max(0f, (float)(PhaseEndTime.Value - NetworkManager.ServerTime.Time));

        public Side SideOf(Team team)
        {
            if (team == Team.None) return Side.None;
            return team == AttackingTeam.Value ? Side.Attack : Side.Defense;
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (!IsServer) return;

            _settings = GameContent.Instance.heavyBomb.WithOverrides(LaunchConfig.Current);
            CombatSystem.FriendlyFire = _settings.friendlyFire;
            _bomb = new BombStateMachine(_settings.plantDuration, _settings.defuseDuration, _settings.fuseDuration);
            RoundsToWin.Value = _settings.roundsToWin;
            RoundTimeLimit.Value = _settings.roundTime;

            foreach (SpawnPoint sp in FindObjectsByType<SpawnPoint>(FindObjectsSortMode.InstanceID))
                (sp.side == Side.Attack ? _attackSpawns : _defenseSpawns).Add(sp);
            BHLog.Info("Match", $"HeavyBomb ready: {_attackSpawns.Count} attack / {_defenseSpawns.Count} defense spawns, {BombSite.All.Count} sites. " +
                                $"Round {_settings.roundTime}s, plant {_settings.plantDuration}s, defuse {_settings.defuseDuration}s, fuse {_settings.fuseDuration}s, first to {_settings.roundsToWin}.");

            NetworkManager.SceneManager.OnSynchronizeComplete += OnClientSynchronized;
            PlayerAvatar.ServerKilled += OnPlayerKilled;
            PlayerAvatar.AnyDespawned += OnPlayerDespawned;

            SetPhase(MatchPhase.WaitingForPlayers, 0);

            // Clients that finished synchronizing before we spawned (and the host's own client).
            foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
                ServerSpawnPlayer(clientId);
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            if (!IsServer) return;
            if (NetworkManager != null && NetworkManager.SceneManager != null)
                NetworkManager.SceneManager.OnSynchronizeComplete -= OnClientSynchronized;
            PlayerAvatar.ServerKilled -= OnPlayerKilled;
            PlayerAvatar.AnyDespawned -= OnPlayerDespawned;
        }

        // ───────────── Players ─────────────

        void OnClientSynchronized(ulong clientId) => ServerSpawnPlayer(clientId);

        void ServerSpawnPlayer(ulong clientId)
        {
            if (PlayerAvatar.TryGet(clientId, out _)) return;
            if (NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) && client.PlayerObject != null) return;

            CountTeams(out int alpha, out int bravo, aliveOnly: false);
            Team team = TeamAssignment.ChooseTeam(alpha, bravo, _settings.teamSize);
            if (team == Team.None)
            {
                BHLog.Warn("Match", $"No team slot for client {clientId}; disconnecting");
                NetworkManager.DisconnectClient(clientId, "Both teams are full.");
                return;
            }

            string playerName = ConnectionRegistry.NameOf(clientId);
            GameObject go = Instantiate(GameContent.Instance.playerPrefab, new Vector3(0f, -100f, 0f), Quaternion.identity);
            var avatar = go.GetComponent<PlayerAvatar>();
            avatar.ServerInitialize(playerName, team);
            go.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, destroyWithScene: true);
            BHLog.Info("Match", $"Player '{playerName}' (client {clientId}) joined team {team}");

            if (IsWarmup)
                SpawnAtSideSpawn(avatar);
            else
                avatar.ServerSetSpectating(); // Joins play at the next round start.
        }

        void OnPlayerDespawned(PlayerAvatar avatar)
        {
            if (!IsServer || _bomb == null) return;
            ulong id = avatar.OwnerClientId;
            if (_bomb.State is BombState.Carried or BombState.Planting && _bomb.CarrierId == id)
            {
                _bomb.Drop(avatar.Simulation.Position);
                BHLog.Info("Bomb", $"Carrier '{avatar.PlayerName}' left; bomb dropped");
            }
            else if (_bomb.State == BombState.Defusing && _bomb.ActorId == id)
            {
                _bomb.CancelDefuse();
                BHLog.Info("Bomb", $"Defuser '{avatar.PlayerName}' left; defuse cancelled");
            }
            PublishBomb();
        }

        void CountTeams(out int alpha, out int bravo, bool aliveOnly)
        {
            alpha = bravo = 0;
            foreach (PlayerAvatar a in PlayerAvatar.All)
            {
                if (!a.IsSpawned || (aliveOnly && !a.IsAlive)) continue;
                if (a.Team == Team.Alpha) alpha++;
                else if (a.Team == Team.Bravo) bravo++;
            }
        }

        int CountSide(Side side, bool aliveOnly)
        {
            int n = 0;
            foreach (PlayerAvatar a in PlayerAvatar.All)
            {
                if (!a.IsSpawned || (aliveOnly && !a.IsAlive)) continue;
                if (SideOf(a.Team) == side) n++;
            }
            return n;
        }

        void SpawnAtSideSpawn(PlayerAvatar avatar)
        {
            Side side = SideOf(avatar.Team);
            List<SpawnPoint> spawns = side == Side.Attack ? _attackSpawns : _defenseSpawns;
            if (spawns.Count == 0) spawns = _attackSpawns.Count > 0 ? _attackSpawns : _defenseSpawns;
            if (spawns.Count == 0)
            {
                BHLog.Error("Match", "Map has no spawn points");
                return;
            }

            _candidates.Clear();
            foreach (SpawnPoint sp in spawns) _candidates.Add(sp.transform.position);
            _occupied.Clear();
            foreach (PlayerAvatar other in PlayerAvatar.All)
            {
                if (other != avatar && other.IsAlive) _occupied.Add(other.Simulation.Position);
            }

            int index = SpawnSelector.Select(_candidates, _occupied, _spawnCursor++, SpawnClearance);
            Transform t = spawns[index].transform;
            avatar.ServerSpawnAt(t.position + Vector3.up * 0.05f, t.eulerAngles.y);
        }

        // ───────────── Flow ─────────────

        void SetPhase(MatchPhase phase, float duration)
        {
            Phase.Value = phase;
            PhaseEndTime.Value = NetworkManager.ServerTime.Time + duration;
        }

        bool EnoughPlayers()
        {
            CountTeams(out int alpha, out int bravo, aliveOnly: false);
            if (LaunchConfig.Current.devSoloStart && alpha + bravo >= 1) return true;
            int min = Mathf.Max(1, _settings.minPlayersPerTeam);
            return alpha >= min && bravo >= min;
        }

        void Update()
        {
            if (!IsServer || !IsSpawned) return;
            double now = NetworkManager.ServerTime.Time;
            bool phaseOver = now >= PhaseEndTime.Value;

            switch (Phase.Value)
            {
                case MatchPhase.WaitingForPlayers:
                    WarmupRespawns(now);
                    if (EnoughPlayers())
                    {
                        BHLog.Info("Match", $"Enough players; match starts in {_settings.matchStartCountdown}s");
                        SetPhase(MatchPhase.Countdown, _settings.matchStartCountdown);
                    }
                    break;

                case MatchPhase.Countdown:
                    WarmupRespawns(now);
                    if (!EnoughPlayers())
                    {
                        BHLog.Info("Match", "Not enough players; back to warmup");
                        SetPhase(MatchPhase.WaitingForPlayers, 0);
                    }
                    else if (phaseOver)
                    {
                        StartMatch();
                    }
                    break;

                case MatchPhase.PreRound:
                    if (phaseOver) GoLive();
                    break;

                case MatchPhase.Live:
                    UpdateBomb(now);
                    EvaluateRound(now);
                    break;

                case MatchPhase.PostRound:
                    if (phaseOver)
                    {
                        if (MatchWinner.Value != Team.None) EndMatch();
                        else if (!EnoughPlayers())
                        {
                            BHLog.Warn("Match", "Not enough players to continue; returning to warmup");
                            ResetToWarmup();
                        }
                        else StartRound();
                    }
                    break;

                case MatchPhase.MatchOver:
                    if (phaseOver) ResetToWarmup();
                    break;
            }
        }

        void WarmupRespawns(double now)
        {
            foreach (PlayerAvatar a in PlayerAvatar.All)
            {
                if (a.IsSpawned && !a.IsAlive && now - a.DeathTime >= WarmupRespawnDelay)
                    SpawnAtSideSpawn(a);
            }
        }

        void StartMatch()
        {
            ScoreAlpha.Value = 0;
            ScoreBravo.Value = 0;
            MatchWinner.Value = Team.None;
            LastRoundWinner.Value = Team.None;
            RoundNumber.Value = 0;
            foreach (PlayerAvatar a in PlayerAvatar.All) a.ServerResetStats();
            BHLog.Info("Match", $"Match started with {PlayerAvatar.All.Count} player(s)");
            StartRound();
        }

        void StartRound()
        {
            RoundNumber.Value++;
            AttackingTeam.Value = MatchRules.AttackingTeamForRound(RoundNumber.Value, _settings.roundsPerHalf);
            CleanupRoundObjects();

            var attackers = new List<PlayerAvatar>();
            foreach (PlayerAvatar a in PlayerAvatar.All)
            {
                if (!a.IsSpawned || a.Team == Team.None) continue;
                SpawnAtSideSpawn(a);
                if (SideOf(a.Team) == Side.Attack) attackers.Add(a);
            }

            if (attackers.Count > 0)
            {
                PlayerAvatar carrier = attackers[_random.Next(attackers.Count)];
                _bomb.AssignTo(carrier.OwnerClientId);
                BHLog.Info("Bomb", $"'{carrier.PlayerName}' carries the bomb");
            }
            else
            {
                _bomb.Deactivate();
            }
            PublishBomb();

            BHLog.Info("Match", $"Round {RoundNumber.Value} starting: {AttackingTeam.Value} attacks, {AttackingTeam.Value.Other()} defends");
            SetPhase(MatchPhase.PreRound, _settings.preRoundTime);
        }

        void GoLive()
        {
            _attackersAtStart = CountSide(Side.Attack, aliveOnly: true);
            _defendersAtStart = CountSide(Side.Defense, aliveOnly: true);
            SetPhase(MatchPhase.Live, _settings.roundTime);
            BHLog.Info("Match", $"Round {RoundNumber.Value} live ({_attackersAtStart} attackers vs {_defendersAtStart} defenders)");
        }

        void EvaluateRound(double now)
        {
            var snapshot = new RoundSnapshot
            {
                attackersAlive = CountSide(Side.Attack, aliveOnly: true),
                defendersAlive = CountSide(Side.Defense, aliveOnly: true),
                attackersAtStart = _attackersAtStart,
                defendersAtStart = _defendersAtStart,
                bomb = _bomb.State,
                roundTimeExpired = now >= PhaseEndTime.Value,
            };

            if (CountSide(Side.Attack, false) + CountSide(Side.Defense, false) == 0)
            {
                BHLog.Warn("Match", "All players left mid-round; returning to warmup");
                ResetToWarmup();
                return;
            }

            RoundResult result = HeavyBombRules.Evaluate(snapshot);
            if (result.ended) EndRound(result);
        }

        void EndRound(RoundResult result)
        {
            Team winner = result.winner == Side.Attack ? AttackingTeam.Value : AttackingTeam.Value.Other();
            if (winner == Team.Alpha) ScoreAlpha.Value++;
            else ScoreBravo.Value++;

            LastRoundWinner.Value = winner;
            LastRoundReason.Value = result.reason;
            MatchWinner.Value = MatchRules.MatchWinner(ScoreAlpha.Value, ScoreBravo.Value, _settings.roundsToWin);

            if (!_bomb.IsResolved)
            {
                // Freeze the bomb where it is; it no longer ticks after the round ends.
                if (_bomb.State == BombState.Planting) _bomb.CancelPlant();
                if (_bomb.State == BombState.Defusing) _bomb.CancelDefuse();
            }
            PublishBomb();

            BHLog.Info("Match", $"Round {RoundNumber.Value} ended: {winner} ({result.winner.DisplayName()}) win — {HeavyBombRules.Describe(result.reason)}. Score Alpha {ScoreAlpha.Value} - {ScoreBravo.Value} Bravo");
            SetPhase(MatchPhase.PostRound, _settings.postRoundTime);
        }

        void EndMatch()
        {
            BHLog.Info("Match", $"Match over: {MatchWinner.Value} wins {Mathf.Max(ScoreAlpha.Value, ScoreBravo.Value)}-{Mathf.Min(ScoreAlpha.Value, ScoreBravo.Value)}");
            foreach (PlayerAvatar a in PlayerAvatar.All)
                BHLog.Info("Match", $"  {a.PlayerName} [{a.Team}] K {a.Kills.Value} / D {a.Deaths.Value} / Obj {a.ObjectiveScore.Value}");
            _bomb.Deactivate();
            PublishBomb();
            SetPhase(MatchPhase.MatchOver, _settings.matchOverTime);
            NetworkBootstrap.Instance?.NotifyMatchCompleted();
        }

        void ResetToWarmup()
        {
            ScoreAlpha.Value = 0;
            ScoreBravo.Value = 0;
            RoundNumber.Value = 0;
            MatchWinner.Value = Team.None;
            AttackingTeam.Value = Team.Alpha;
            _bomb.Deactivate();
            PublishBomb();
            CleanupRoundObjects();
            SetPhase(MatchPhase.WaitingForPlayers, 0);
            foreach (PlayerAvatar a in PlayerAvatar.All)
            {
                if (a.IsSpawned) SpawnAtSideSpawn(a);
            }
            BHLog.Info("Match", "Returned to warmup");
        }

        void CleanupRoundObjects()
        {
            foreach (Wayblocker w in FindObjectsByType<Wayblocker>(FindObjectsSortMode.None))
                if (w.IsSpawned) w.NetworkObject.Despawn(true);
            foreach (SmokeCloud s in FindObjectsByType<SmokeCloud>(FindObjectsSortMode.None))
                if (s.IsSpawned) s.NetworkObject.Despawn(true);
            foreach (SmokeGrenadeProjectile g in FindObjectsByType<SmokeGrenadeProjectile>(FindObjectsSortMode.None))
                if (g.IsSpawned) g.NetworkObject.Despawn(true);
        }

        // ───────────── Bomb ─────────────

        void UpdateBomb(double now)
        {
            switch (_bomb.State)
            {
                case BombState.Dropped:
                    foreach (PlayerAvatar a in PlayerAvatar.All)
                    {
                        if (!a.IsAlive || SideOf(a.Team) != Side.Attack) continue;
                        if ((a.Simulation.Position - _bomb.Position).sqrMagnitude <= _settings.pickupRange * _settings.pickupRange
                            && _bomb.TryPickUp(a.OwnerClientId, true, true) == BombRejectReason.None)
                        {
                            BHLog.Info("Bomb", $"'{a.PlayerName}' picked up the bomb");
                            break;
                        }
                    }
                    break;

                case BombState.Carried:
                    if (PlayerAvatar.TryGet(_bomb.CarrierId, out PlayerAvatar carrier) && carrier.Simulation.InteractHeld && IsSteady(carrier))
                    {
                        int site = BombSite.SiteAt(carrier.Simulation.Position);
                        BombRejectReason r = _bomb.TryStartPlant(carrier.OwnerClientId, SideOf(carrier.Team) == Side.Attack, carrier.IsAlive,
                            site, carrier.Simulation.Position, now);
                        if (r == BombRejectReason.None)
                            BHLog.Info("Bomb", $"'{carrier.PlayerName}' started planting at site {BombSite.ByIndex(site)?.label}");
                    }
                    break;

                case BombState.Planting:
                    if (!PlayerAvatar.TryGet(_bomb.ActorId, out PlayerAvatar planter) || !planter.IsAlive || !planter.Simulation.InteractHeld
                        || !IsSteady(planter) || BombSite.SiteAt(planter.Simulation.Position) != _bomb.SiteIndex)
                    {
                        _bomb.CancelPlant();
                        BHLog.Info("Bomb", "Plant interrupted");
                    }
                    break;

                case BombState.Planted:
                    TryStartDefuse(now);
                    break;

                case BombState.Defusing:
                    if (!PlayerAvatar.TryGet(_bomb.ActorId, out PlayerAvatar defuser) || !defuser.IsAlive || !defuser.Simulation.InteractHeld
                        || !IsSteady(defuser) || Vector3.Distance(defuser.Simulation.Position, _bomb.Position) > _settings.defuseRange)
                    {
                        _bomb.CancelDefuse();
                        BHLog.Info("Bomb", "Defuse interrupted");
                    }
                    break;
            }

            BombEvent evt = _bomb.Tick(now);
            switch (evt)
            {
                case BombEvent.Planted:
                    BHLog.Info("Bomb", $"Bomb planted at site {BombSite.ByIndex(_bomb.SiteIndex)?.label}; detonation in {_settings.fuseDuration}s");
                    CreditObjective(_bomb.Position, Side.Attack);
                    break;
                case BombEvent.Defused:
                    BHLog.Info("Bomb", "Bomb defused");
                    if (PlayerAvatar.TryGet(_bomb.ActorId, out PlayerAvatar d)) d.ServerAddObjectiveScore(1);
                    break;
                case BombEvent.Exploded:
                    BHLog.Info("Bomb", $"Bomb exploded at {_bomb.Position}");
                    ExplodeAt(_bomb.Position);
                    break;
            }
            PublishBomb();
        }

        void TryStartDefuse(double now)
        {
            PlayerAvatar best = null;
            float bestDist = float.MaxValue;
            foreach (PlayerAvatar a in PlayerAvatar.All)
            {
                if (!a.IsAlive || SideOf(a.Team) != Side.Defense || !a.Simulation.InteractHeld || !IsSteady(a)) continue;
                float dist = Vector3.Distance(a.Simulation.Position, _bomb.Position);
                if (dist < bestDist)
                {
                    best = a;
                    bestDist = dist;
                }
            }
            if (best == null) return;
            if (_bomb.TryStartDefuse(best.OwnerClientId, true, best.IsAlive, bestDist, _settings.defuseRange, now) == BombRejectReason.None)
                BHLog.Info("Bomb", $"'{best.PlayerName}' started defusing");
        }

        bool IsSteady(PlayerAvatar a)
        {
            Vector3 v = a.Simulation.Velocity;
            return a.Simulation.Grounded && new Vector2(v.x, v.z).magnitude <= _settings.maxInteractSpeed;
        }

        void CreditObjective(Vector3 position, Side side)
        {
            PlayerAvatar closest = null;
            float best = float.MaxValue;
            foreach (PlayerAvatar a in PlayerAvatar.All)
            {
                if (SideOf(a.Team) != side) continue;
                float d = (a.Simulation.Position - position).sqrMagnitude;
                if (d < best) { best = d; closest = a; }
            }
            closest?.ServerAddObjectiveScore(1);
        }

        void ExplodeAt(Vector3 position)
        {
            ExplosionRpc(position);
            foreach (PlayerAvatar a in PlayerAvatar.All.ToArray())
            {
                if (!a.IsAlive) continue;
                if (Vector3.Distance(a.Simulation.Position, position) <= ExplosionRadius)
                {
                    a.Health.ServerKill(new DamageInfo
                    {
                        attackerClientId = ulong.MaxValue,
                        amount = 9999,
                        source = DamageSource.Explosion,
                        weaponSlot = 255,
                        origin = position,
                        point = a.Simulation.Position,
                    });
                }
            }
        }

        void PublishBomb()
        {
            var state = new BombNetState
            {
                state = _bomb.State,
                actorId = _bomb.State is BombState.Carried ? _bomb.CarrierId : _bomb.ActorId,
                siteIndex = _bomb.SiteIndex,
                position = _bomb.Position,
                actionStart = _bomb.ActionStartTime,
                actionEnd = _bomb.ActionEndTime,
                detonationTime = _bomb.DetonationTime,
            };
            if (!state.Equals(Bomb.Value)) Bomb.Value = state;
        }

        // ───────────── Kills ─────────────

        void OnPlayerKilled(PlayerAvatar victim, DamageInfo info)
        {
            ulong id = victim.OwnerClientId;
            if (_bomb.State is BombState.Carried or BombState.Planting && _bomb.CarrierId == id)
            {
                _bomb.Drop(victim.Simulation.Position);
                BHLog.Info("Bomb", $"Carrier '{victim.PlayerName}' died; bomb dropped");
            }
            else if (_bomb.State == BombState.Defusing && _bomb.ActorId == id)
            {
                _bomb.CancelDefuse();
                BHLog.Info("Bomb", "Defuser died; defuse cancelled");
            }
            PublishBomb();

            PlayerAvatar.TryGet(info.attackerClientId, out PlayerAvatar killer);
            string weapon = info.source switch
            {
                DamageSource.Explosion => "Bomb",
                DamageSource.World => "World",
                _ => GameContent.Instance.WeaponForSlot(info.weaponSlot)?.displayName ?? "Weapon",
            };
            bool headshot = info.source == DamageSource.Weapon && info.zone == HitZone.Head;
            string killerName = killer != null ? killer.PlayerName : "";
            string killerLabel = killer != null ? $"{killerName} [{killer.Team}]" : "<world>";
            BHLog.Info("Kill", $"{killerLabel} killed {victim.PlayerName} [{victim.Team}] with {weapon}{(headshot ? " (headshot)" : "")}");

            KillFeedRpc(new FixedString32Bytes(killerName), killer != null ? killer.Team : Team.None,
                new FixedString32Bytes(victim.PlayerName), victim.Team, new FixedString32Bytes(weapon), headshot);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void KillFeedRpc(FixedString32Bytes killer, Team killerTeam, FixedString32Bytes victim, Team victimTeam, FixedString32Bytes weapon, bool headshot)
        {
            KillFeed?.Invoke(new KillFeedEntry
            {
                killer = killer.ToString(),
                killerTeam = killerTeam,
                victim = victim.ToString(),
                victimTeam = victimTeam,
                weapon = weapon.ToString(),
                headshot = headshot,
            });
        }

        [Rpc(SendTo.ClientsAndHost)]
        void ExplosionRpc(Vector3 position) => BombExploded?.Invoke(position);
    }
}
