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
        /// <summary>Warmup: free respawns, abilities and loadout allowed, nothing counts.</summary>
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

    /// <summary>Mode-specific objective state for the HUD (Capture the Area zone).</summary>
    public struct ModeStatus : INetworkSerializable, IEquatable<ModeStatus>
    {
        public Team owner;
        public Team capturing;
        public float progress;
        public bool contested;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref owner);
            serializer.SerializeValue(ref capturing);
            serializer.SerializeValue(ref progress);
            serializer.SerializeValue(ref contested);
        }

        public bool Equals(ModeStatus o) =>
            owner == o.owner && capturing == o.capturing && Mathf.Abs(progress - o.progress) < 0.005f && contested == o.contested;
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
    /// Server-authoritative match flow shared by every game mode: joins and team assignment, spawns and respawns,
    /// warmup/countdown/freeze/live/post-round/match-over phases, team and player scores, results and the kill feed.
    /// Everything mode-specific lives in the <see cref="GameModeRules"/> selected with -mode.
    /// </summary>
    public sealed class MatchManager : NetworkBehaviour, IMatchHost
    {
        const float WarmupRespawnDelay = 3f;
        const float SpawnClearance = 1.2f;
        const double ReconnectGraceSeconds = 180;

        public static MatchManager Instance { get; private set; }

        public readonly NetworkVariable<byte> ModeIndex = new();
        public readonly NetworkVariable<MatchPhase> Phase = new();
        public readonly NetworkVariable<double> PhaseEndTime = new();
        public readonly NetworkVariable<int> RoundNumber = new();
        public readonly NetworkVariable<int> ScoreAlpha = new();
        public readonly NetworkVariable<int> ScoreBravo = new();
        public readonly NetworkVariable<Team> AttackingTeam = new(Team.Alpha);
        public readonly NetworkVariable<Team> LastRoundWinner = new();
        public readonly NetworkVariable<FixedString64Bytes> LastRoundReason = new();
        public readonly NetworkVariable<Team> MatchWinner = new();
        public readonly NetworkVariable<FixedString32Bytes> MatchWinnerName = new();
        public readonly NetworkVariable<BombNetState> Bomb = new();
        public readonly NetworkVariable<ModeStatus> Status = new();
        public readonly NetworkVariable<int> ScoreLimit = new();
        public readonly NetworkVariable<float> TimeLimit = new();
        /// <summary>Agents players may pick (bit i = GameContent.agents[i]); see <see cref="Agents.AgentRules"/>.</summary>
        public readonly NetworkVariable<uint> AgentUnlockMask = new(1u);
        public readonly NetworkVariable<uint> AgentBanMask = new();

        /// <summary>Clients: kill feed entries as they happen.</summary>
        public static event Action<KillFeedEntry> KillFeed;
        /// <summary>Clients: the bomb exploded at a position.</summary>
        public static event Action<Vector3> BombExploded;

        GameModeDefinition _settings;
        GameModeRules _rules;
        readonly List<SpawnPoint> _attackSpawns = new();
        readonly List<SpawnPoint> _defenseSpawns = new();
        readonly List<SpawnPoint> _freeSpawns = new();
        readonly List<Vector3> _occupied = new();
        readonly List<Vector3> _candidates = new();
        readonly ReconnectRegistry _reconnects = new();
        int _matchId;
        int _spawnCursor;
        bool _matchDecided;

        /// <summary>The mode's data (valid on every peer).</summary>
        public GameModeDefinition Mode => _settings != null ? _settings : GameContent.Instance?.ModeByIndex(ModeIndex.Value);
        public HeavyBombSettings HeavyBomb => Mode as HeavyBombSettings;
        public bool FreeForAll => Mode != null && Mode.freeForAll;
        public bool RoundBased => Mode is HeavyBombSettings or DuelSettings;

        public bool CanMove => Phase.Value is not (MatchPhase.PreRound or MatchPhase.MatchOver);
        public bool CanFight => Phase.Value is MatchPhase.WaitingForPlayers or MatchPhase.Countdown or MatchPhase.Live or MatchPhase.PostRound;
        public bool AbilitiesAllowed => (Mode == null || Mode.abilitiesEnabled)
                                        && Phase.Value is MatchPhase.WaitingForPlayers or MatchPhase.Countdown or MatchPhase.Live;
        public bool LoadoutChangesAllowed => (Mode == null || Mode.loadoutChangesEnabled)
                                             && Phase.Value is MatchPhase.WaitingForPlayers or MatchPhase.Countdown or MatchPhase.PreRound;
        public bool IsWarmup => Phase.Value is MatchPhase.WaitingForPlayers or MatchPhase.Countdown;

        public float PhaseTimeRemaining => NetworkManager == null ? 0f : Mathf.Max(0f, (float)(PhaseEndTime.Value - NetworkManager.ServerTime.Time));

        /// <summary>Objective role of a team this round. Free-for-all: none.</summary>
        public Side SideOf(Team team)
        {
            if (team == Team.None || FreeForAll) return Side.None;
            return team == AttackingTeam.Value ? Side.Attack : Side.Defense;
        }

        public bool AreEnemies(PlayerAvatar a, PlayerAvatar b) => a != b && TeamRules.AreEnemies(a.Team, b.Team, FreeForAll);

        // ───────────── IMatchHost ─────────────

        double IMatchHost.Now => NetworkManager.ServerTime.Time;
        int IMatchHost.RoundNumber => RoundNumber.Value;
        Team IMatchHost.AttackingTeam { get => AttackingTeam.Value; set => AttackingTeam.Value = value; }
        IReadOnlyList<PlayerAvatar> IMatchHost.Players => PlayerAvatar.All;

        public void AddTeamScore(Team team, int points)
        {
            if (team == Team.Alpha) ScoreAlpha.Value += points;
            else if (team == Team.Bravo) ScoreBravo.Value += points;
        }

        public int TeamScore(Team team) => team == Team.Alpha ? ScoreAlpha.Value : team == Team.Bravo ? ScoreBravo.Value : 0;

        public void PublishBomb(in BombNetState state)
        {
            if (!state.Equals(Bomb.Value)) Bomb.Value = state;
        }

        public void PublishModeStatus(in ModeStatus status)
        {
            if (!status.Equals(Status.Value)) Status.Value = status;
        }

        public void BroadcastExplosion(Vector3 position) => ExplosionRpc(position);

        public Agents.AgentRejectReason CheckAgent(int index) =>
            Agents.AgentRules.Check(index, GameContent.Instance.agents, AgentUnlockMask.Value, AgentBanMask.Value);

        /// <summary>Server: the agent a player plays this life (their choice if still available).</summary>
        public byte AgentFor(PlayerAvatar avatar) =>
            Agents.AgentRules.Resolve(avatar.PreferredAgent, GameContent.Instance.agents, AgentUnlockMask.Value, AgentBanMask.Value);

        public void RefreshLoadout(PlayerAvatar avatar)
        {
            avatar.ServerSetLoadout(_rules.LoadoutFor(avatar));
            if (avatar.IsAlive) avatar.Simulation.ServerResetWeapons();
        }

        // ───────────── Lifecycle ─────────────

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (!IsServer) return;

            GameContent content = GameContent.Instance;
            int modeIndex = content.FindModeIndex(LaunchConfig.Current.mode);
            ModeIndex.Value = (byte)modeIndex;
            _settings = content.ModeByIndex(modeIndex).WithOverrides(LaunchConfig.Current);
            _rules = _settings.CreateRules();
            _rules.Attach(this);

            CombatSystem.FriendlyFire = _settings.friendlyFire;
            CombatSystem.LagCompensationEnabled = LaunchConfig.Current.lagCompensation;
            BHLog.Info("Match", LaunchConfig.Current.lagCompensation
                ? $"Lag compensation ON (max rewind {LaunchConfig.Current.maxRewindMs} ms)"
                : "Lag compensation OFF");
            ScoreLimit.Value = _settings.scoreLimit;
            TimeLimit.Value = _settings.timeLimit;
            AgentUnlockMask.Value = Agents.AgentRules.UnlockMask(content.agents, LaunchConfig.Current.unlockAgents);
            AgentBanMask.Value = Agents.AgentRules.BanMask(content.agents, LaunchConfig.Current.banAgents);
            LogAgents(content);

            foreach (SpawnPoint sp in FindObjectsByType<SpawnPoint>(FindObjectsSortMode.InstanceID))
                (sp.side == Side.Attack ? _attackSpawns : sp.side == Side.Defense ? _defenseSpawns : _freeSpawns).Add(sp);
            BHLog.Info("Match", $"Mode '{_settings.modeId}' ready — {_rules.Summary}. Spawns: {_attackSpawns.Count} attack / {_defenseSpawns.Count} defense / {_freeSpawns.Count} free, " +
                                $"{BombSite.All.Count} sites, {(CaptureZone.Main != null ? "capture zone" : "no capture zone")}, max {_settings.maxPlayers} players.");

            NetworkManager.SceneManager.OnSynchronizeComplete += OnClientSynchronized;
            PlayerAvatar.ServerKilled += OnPlayerKilled;
            PlayerAvatar.AnyDespawned += OnPlayerDespawned;

            _rules.OnReset();
            SetPhase(MatchPhase.WaitingForPlayers, 0);

            // Clients that finished synchronizing before we spawned (and the host's own client).
            foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
                ServerSpawnPlayer(clientId);
        }

        void LogAgents(GameContent content)
        {
            var available = new List<string>();
            var locked = new List<string>();
            var banned = new List<string>();
            for (int i = 0; content.agents != null && i < content.agents.Length; i++)
            {
                if (content.agents[i] == null) continue;
                string name = content.agents[i].DisplayName;
                switch (CheckAgent(i))
                {
                    case Agents.AgentRejectReason.None: available.Add(name); break;
                    case Agents.AgentRejectReason.Banned: banned.Add(name); break;
                    default: locked.Add(name); break;
                }
            }
            BHLog.Info("Match", $"Agents available: {string.Join(", ", available)}; locked: {(locked.Count > 0 ? string.Join(", ", locked) : "none")}; " +
                                $"banned: {(banned.Count > 0 ? string.Join(", ", banned) : "none")}. Downed state {(_settings.downedState && !_settings.freeForAll ? "on" : "off")}.");
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

        /// <summary>Effective player limit (config and mode). Used by connection approval.</summary>
        public static int MaxPlayersFor(GameModeDefinition mode, LaunchConfig config) => Mathf.Min(config.maxPlayers, mode != null ? mode.maxPlayers : 10);

        // ───────────── Players ─────────────

        void OnClientSynchronized(ulong clientId) => ServerSpawnPlayer(clientId);

        bool IsParticipant(PlayerAvatar a) => a.IsSpawned && (FreeForAll || a.Team != Team.None);

        void ServerSpawnPlayer(ulong clientId)
        {
            if (PlayerAvatar.TryGet(clientId, out _)) return;
            if (NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) && client.PlayerObject != null) return;

            string playerName = ConnectionRegistry.NameOf(clientId);
            bool nameInUse = false;
            foreach (PlayerAvatar a in PlayerAvatar.All) nameInUse |= a.PlayerName == playerName;
            ReconnectRecord previous = default;
            bool rejoining = !nameInUse && _reconnects.TryTake(playerName, _matchId, NetworkManager.ServerTime.Time, ReconnectGraceSeconds, out previous);

            Team team = Team.None;
            if (!FreeForAll)
            {
                CountTeams(out int alpha, out int bravo);
                team = TeamAssignment.ChooseTeam(alpha, bravo, _settings.teamSize);
                if (rejoining && (previous.team == Team.Alpha ? alpha : bravo) < _settings.teamSize)
                    team = previous.team;
                if (team == Team.None)
                {
                    BHLog.Warn("Match", $"No team slot for client {clientId}; disconnecting");
                    NetworkManager.DisconnectClient(clientId, "Both teams are full.");
                    return;
                }
            }

            GameObject go = Instantiate(GameContent.Instance.playerPrefab, new Vector3(0f, -100f, 0f), Quaternion.identity);
            var avatar = go.GetComponent<PlayerAvatar>();
            avatar.ServerInitialize(playerName, team);
            go.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, destroyWithScene: true);
            BHLog.Info("Match", FreeForAll
                ? $"Player '{playerName}' (client {clientId}) joined (free-for-all)"
                : $"Player '{playerName}' (client {clientId}) joined team {team}");
            if (rejoining)
            {
                avatar.ServerRestoreStats(previous.kills, previous.deaths, previous.objectiveScore, previous.preferredLoadout, previous.preferredAgent);
                BHLog.Info("Match", $"Player '{playerName}' reconnected to match {_matchId}; team and stats restored");
            }

            avatar.ServerSetLoadout(_rules.LoadoutFor(avatar));
            if (IsWarmup || (!_rules.RoundBased && Phase.Value is MatchPhase.Live or MatchPhase.PreRound))
                SpawnPlayer(avatar);
            else
                avatar.ServerSetSpectating(); // Joins play at the next round start.
        }

        void OnPlayerDespawned(PlayerAvatar avatar)
        {
            if (!IsServer || _rules == null) return;
            if (!IsWarmup && Phase.Value != MatchPhase.MatchOver)
            {
                _reconnects.Store(avatar.PlayerName, new ReconnectRecord
                {
                    team = avatar.Team,
                    kills = avatar.Kills.Value,
                    deaths = avatar.Deaths.Value,
                    objectiveScore = avatar.ObjectiveScore.Value,
                    preferredLoadout = avatar.PreferredLoadout,
                    preferredAgent = avatar.PreferredAgent,
                    matchId = _matchId,
                    disconnectTime = NetworkManager.ServerTime.Time,
                });
            }
            _rules.OnPlayerLeft(avatar);
        }

        void CountTeams(out int alpha, out int bravo)
        {
            alpha = bravo = 0;
            foreach (PlayerAvatar a in PlayerAvatar.All)
            {
                if (!a.IsSpawned) continue;
                if (a.Team == Team.Alpha) alpha++;
                else if (a.Team == Team.Bravo) bravo++;
            }
        }

        int ParticipantCount()
        {
            int n = 0;
            foreach (PlayerAvatar a in PlayerAvatar.All)
                if (IsParticipant(a)) n++;
            return n;
        }

        void SpawnPlayer(PlayerAvatar avatar)
        {
            Side side = SideOf(avatar.Team);
            List<SpawnPoint> spawns = side == Side.Attack ? _attackSpawns : side == Side.Defense ? _defenseSpawns : _freeSpawns;
            if (spawns.Count == 0) spawns = _freeSpawns.Count > 0 ? _freeSpawns : _attackSpawns.Count > 0 ? _attackSpawns : _defenseSpawns;
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
                if (other == avatar || !other.IsAlive) continue;
                // Free-for-all spawns avoid every living player; team spawns only need room.
                if (side == Side.None || !AreEnemies(avatar, other)) _occupied.Add(other.Simulation.Position);
            }

            int index = side == Side.None
                ? SpawnSelector.SelectFarthest(_candidates, _occupied, _spawnCursor++)
                : SpawnSelector.Select(_candidates, _occupied, _spawnCursor++, SpawnClearance);
            Transform t = spawns[index].transform;
            avatar.ServerApplyAgent(AgentFor(avatar));
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
            if (FreeForAll)
            {
                int total = ParticipantCount();
                return total >= (LaunchConfig.Current.devSoloStart ? 1 : Mathf.Max(2, _settings.minPlayersPerTeam));
            }
            CountTeams(out int alpha, out int bravo);
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
                    Respawns(now, WarmupRespawnDelay);
                    if (EnoughPlayers())
                    {
                        BHLog.Info("Match", $"Enough players; match starts in {_settings.matchStartCountdown}s");
                        SetPhase(MatchPhase.Countdown, _settings.matchStartCountdown);
                    }
                    break;

                case MatchPhase.Countdown:
                    Respawns(now, WarmupRespawnDelay);
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
                    if (ParticipantCount() == 0)
                    {
                        BHLog.Warn("Match", "All players left mid-match; returning to warmup");
                        ResetToWarmup();
                        break;
                    }
                    if (_settings.respawnDelay > 0f) Respawns(now, _settings.respawnDelay);
                    FinishStrandedDowned();
                    HandleOutcome(_rules.TickLive(now, Time.deltaTime, phaseOver));
                    break;

                case MatchPhase.PostRound:
                    if (phaseOver)
                    {
                        if (_matchDecided) EndMatch();
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

        readonly List<PlayerAvatar> _stranded = new();

        /// <summary>A team whose remaining players are all downed cannot revive anyone: they die (kills go to their downers).</summary>
        void FinishStrandedDowned()
        {
            if (FreeForAll || !_settings.downedState) return;
            for (int t = 0; t < 2; t++)
            {
                Team team = t == 0 ? Team.Alpha : Team.Bravo;
                bool standing = false;
                _stranded.Clear();
                foreach (PlayerAvatar a in PlayerAvatar.All)
                {
                    if (!a.IsSpawned || !a.IsAlive || a.Team != team) continue;
                    if (a.Health.IsDowned) _stranded.Add(a);
                    else standing = true;
                }
                if (standing) continue;
                foreach (PlayerAvatar a in _stranded) a.Health.ServerFinishDowned("bled out (no teammate left standing)");
            }
        }

        void Respawns(double now, float delay)
        {
            foreach (PlayerAvatar a in PlayerAvatar.All)
            {
                if (IsParticipant(a) && !a.IsAlive && now - a.DeathTime >= delay)
                {
                    a.ServerSetLoadout(_rules.LoadoutFor(a));
                    SpawnPlayer(a);
                }
            }
        }

        void StartMatch()
        {
            _matchId++;
            _reconnects.Clear();
            _matchDecided = false;
            ScoreAlpha.Value = 0;
            ScoreBravo.Value = 0;
            MatchWinner.Value = Team.None;
            MatchWinnerName.Value = default;
            LastRoundWinner.Value = Team.None;
            RoundNumber.Value = 0;
            foreach (PlayerAvatar a in PlayerAvatar.All) a.ServerResetStats();
            _rules.OnMatchStart();
            BHLog.Info("Match", $"Match started with {PlayerAvatar.All.Count} player(s)");
            StartRound();
        }

        void StartRound()
        {
            RoundNumber.Value++;
            _rules.OnRoundStarting();
            CleanupRoundObjects();
            foreach (PlayerAvatar a in PlayerAvatar.All)
            {
                if (!IsParticipant(a)) continue;
                a.ServerSetLoadout(_rules.LoadoutFor(a));
                SpawnPlayer(a);
            }
            _rules.OnRoundStarted();

            BHLog.Info("Match", FreeForAll
                ? $"Round {RoundNumber.Value} starting: free-for-all"
                : $"Round {RoundNumber.Value} starting: {AttackingTeam.Value} attacks, {AttackingTeam.Value.Other()} defends");
            SetPhase(MatchPhase.PreRound, _settings.preRoundTime);
        }

        void GoLive()
        {
            _rules.OnLive();
            SetPhase(MatchPhase.Live, _settings.timeLimit);
            int alpha = 0, bravo = 0, total = 0;
            foreach (PlayerAvatar a in PlayerAvatar.All)
            {
                if (!a.IsAlive) continue;
                total++;
                if (SideOf(a.Team) == Side.Attack) alpha++;
                else if (SideOf(a.Team) == Side.Defense) bravo++;
            }
            BHLog.Info("Match", FreeForAll
                ? $"Round {RoundNumber.Value} live ({total} players)"
                : $"Round {RoundNumber.Value} live ({alpha} attackers vs {bravo} defenders)");
        }

        void HandleOutcome(in ModeOutcome outcome)
        {
            switch (outcome.kind)
            {
                case OutcomeKind.RoundOver:
                {
                    Team winner = outcome.winnerTeam;
                    AddTeamScore(winner, 1);
                    LastRoundWinner.Value = winner;
                    LastRoundReason.Value = new FixedString64Bytes(Truncate(outcome.reason, 60));
                    Team matchWinner = MatchRules.MatchWinner(ScoreAlpha.Value, ScoreBravo.Value, _settings.scoreLimit);
                    if (matchWinner != Team.None)
                    {
                        _matchDecided = true;
                        MatchWinner.Value = matchWinner;
                        MatchWinnerName.Value = new FixedString32Bytes(matchWinner.DisplayName());
                    }
                    _rules.OnRoundEnded();
                    BHLog.Info("Match", $"Round {RoundNumber.Value} ended: {(winner == Team.None ? "draw" : $"{winner} win")} - {outcome.reason}. Score Alpha {ScoreAlpha.Value} - {ScoreBravo.Value} Bravo");
                    SetPhase(MatchPhase.PostRound, _settings.postRoundTime);
                    break;
                }

                case OutcomeKind.MatchOver:
                {
                    _matchDecided = true;
                    LastRoundReason.Value = new FixedString64Bytes(Truncate(outcome.reason, 60));
                    if (outcome.winnerPlayer != null)
                    {
                        MatchWinner.Value = outcome.winnerPlayer.Team;
                        MatchWinnerName.Value = new FixedString32Bytes(outcome.winnerPlayer.PlayerName);
                    }
                    else
                    {
                        MatchWinner.Value = outcome.winnerTeam;
                        MatchWinnerName.Value = new FixedString32Bytes(outcome.winnerTeam == Team.None ? "Nobody" : outcome.winnerTeam.DisplayName());
                    }
                    LastRoundWinner.Value = MatchWinner.Value;
                    _rules.OnRoundEnded();
                    BHLog.Info("Match", $"Round {RoundNumber.Value} ended: {MatchWinnerName.Value} - {outcome.reason}");
                    EndMatch();
                    break;
                }
            }
        }

        static string Truncate(string s, int max) => string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s.Substring(0, max);

        void EndMatch()
        {
            string result = FreeForAll || !_rules.RoundBased && _rules is not CaptureAreaMode
                ? $"{MatchWinnerName.Value} wins"
                : MatchWinner.Value == Team.None
                    ? $"draw {ScoreAlpha.Value}-{ScoreBravo.Value}"
                    : $"{MatchWinner.Value} wins {Mathf.Max(ScoreAlpha.Value, ScoreBravo.Value)}-{Mathf.Min(ScoreAlpha.Value, ScoreBravo.Value)}";
            BHLog.Info("Match", $"Match over: {result}");
            foreach (PlayerAvatar a in PlayerAvatar.All)
                BHLog.Info("Match", $"  {a.PlayerName} [{a.Team}] K {a.Kills.Value} / D {a.Deaths.Value} / Score {a.ObjectiveScore.Value} / shots {a.ServerShotsFired} hits {a.ServerCharacterHits}");
            _rules.OnReset();
            SetPhase(MatchPhase.MatchOver, _settings.matchOverTime);
            NetworkBootstrap.Instance?.NotifyMatchCompleted();
        }

        void ResetToWarmup()
        {
            _reconnects.Clear();
            _matchDecided = false;
            ScoreAlpha.Value = 0;
            ScoreBravo.Value = 0;
            RoundNumber.Value = 0;
            MatchWinner.Value = Team.None;
            MatchWinnerName.Value = default;
            AttackingTeam.Value = Team.Alpha;
            _rules.OnReset();
            CleanupRoundObjects();
            SetPhase(MatchPhase.WaitingForPlayers, 0);
            foreach (PlayerAvatar a in PlayerAvatar.All)
            {
                if (!IsParticipant(a)) continue;
                a.ServerResetStats();
                a.ServerSetLoadout(_rules.LoadoutFor(a));
                SpawnPlayer(a);
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

        // ───────────── Kills ─────────────

        void OnPlayerKilled(PlayerAvatar victim, DamageInfo info)
        {
            PlayerAvatar.TryGet(info.attackerClientId, out PlayerAvatar killer);
            string weapon = info.source switch
            {
                DamageSource.Explosion => "Bomb",
                DamageSource.World => "World",
                _ => killer != null ? killer.WeaponInSlot(info.weaponSlot)?.displayName ?? "Weapon" : "Weapon",
            };

            // Score/ladder changes happen before the feed so the log shows the cause first.
            if (Phase.Value == MatchPhase.Live) _rules.OnPlayerKilled(victim, killer, info);

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
