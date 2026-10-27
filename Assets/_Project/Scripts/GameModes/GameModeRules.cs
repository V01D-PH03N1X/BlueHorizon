using System.Collections.Generic;
using BlueHorizon.Combat;
using BlueHorizon.Core;
using BlueHorizon.Match;
using BlueHorizon.Player;
using UnityEngine;

namespace BlueHorizon.GameModes
{
    public enum OutcomeKind : byte
    {
        None,
        /// <summary>The round is decided (round-based modes); the match continues unless the score limit is reached.</summary>
        RoundOver,
        /// <summary>The match is decided directly (continuous modes).</summary>
        MatchOver,
    }

    public struct ModeOutcome
    {
        public OutcomeKind kind;
        /// <summary>Winning team (team modes); None = draw.</summary>
        public Team winnerTeam;
        /// <summary>Winning player (free-for-all); null = draw.</summary>
        public PlayerAvatar winnerPlayer;
        public string reason;

        public static ModeOutcome Continue => default;

        public static ModeOutcome Round(Team winner, string reason) =>
            new() { kind = OutcomeKind.RoundOver, winnerTeam = winner, reason = reason };

        public static ModeOutcome MatchForTeam(Team winner, string reason) =>
            new() { kind = OutcomeKind.MatchOver, winnerTeam = winner, reason = reason };

        public static ModeOutcome MatchForPlayer(PlayerAvatar winner, string reason) =>
            new() { kind = OutcomeKind.MatchOver, winnerPlayer = winner, reason = reason };
    }

    /// <summary>What a mode may ask of the match (implemented by <see cref="MatchManager"/>, server only).</summary>
    public interface IMatchHost
    {
        double Now { get; }
        int RoundNumber { get; }
        Team AttackingTeam { get; set; }
        IReadOnlyList<PlayerAvatar> Players { get; }
        void AddTeamScore(Team team, int points);
        int TeamScore(Team team);
        void PublishBomb(in BombNetState state);
        void PublishModeStatus(in ModeStatus status);
        void BroadcastExplosion(Vector3 position);
        /// <summary>Re-applies <see cref="GameModeRules.LoadoutFor"/> to a player (resets their weapons).</summary>
        void RefreshLoadout(PlayerAvatar avatar);
    }

    /// <summary>
    /// Server-side rules of one game mode. The <see cref="MatchManager"/> owns the generic flow (joins, teams,
    /// spawns, phases, respawns, scores, results) and calls these hooks; a mode only decides what is specific to it.
    /// </summary>
    public abstract class GameModeRules
    {
        protected IMatchHost Host { get; private set; }
        public GameModeDefinition Definition { get; }

        protected GameModeRules(GameModeDefinition definition) => Definition = definition;

        public void Attach(IMatchHost host) => Host = host;

        /// <summary>Round-based modes (HeavyBomb, Duel) score rounds; continuous modes run one live phase per match.</summary>
        public virtual bool RoundBased => true;

        public bool FreeForAll => Definition.freeForAll;

        public virtual PlayerLoadout LoadoutFor(PlayerAvatar avatar) => PlayerLoadout.Default;

        public virtual void OnMatchStart() { }

        /// <summary>Before players are spawned for a new round (set sides here).</summary>
        public virtual void OnRoundStarting() { }

        /// <summary>After everyone has been spawned for the round.</summary>
        public virtual void OnRoundStarted() { }

        /// <summary>Freeze time is over.</summary>
        public virtual void OnLive() { }

        /// <summary>Called every server frame while live. <paramref name="timeExpired"/>: the live phase's time limit passed.</summary>
        public abstract ModeOutcome TickLive(double now, float deltaTime, bool timeExpired);

        public virtual void OnRoundEnded() { }

        /// <summary>The match is over or returned to warmup: clear all mode state.</summary>
        public virtual void OnReset() { }

        public virtual void OnPlayerKilled(PlayerAvatar victim, PlayerAvatar killer, in DamageInfo info) { }

        public virtual void OnPlayerLeft(PlayerAvatar avatar) { }

        public virtual string Summary => Definition.displayName;

        // ───────────── Helpers ─────────────

        protected int CountAlive(Team team)
        {
            int n = 0;
            foreach (PlayerAvatar a in Host.Players)
                if (a.IsSpawned && a.IsAlive && a.Team == team) n++;
            return n;
        }

        protected int EffectiveHealth(Team team)
        {
            int total = 0;
            foreach (PlayerAvatar a in Host.Players)
            {
                if (!a.IsSpawned || !a.IsAlive || a.Team != team) continue;
                total += a.Health.HealthValue.Value + a.Health.ArmorValue.Value + a.Health.BarrierValue.Value;
            }
            return total;
        }

        readonly List<FfaStanding> _standings = new();
        readonly List<PlayerAvatar> _standingPlayers = new();

        protected PlayerAvatar FreeForAllLeader()
        {
            _standings.Clear();
            _standingPlayers.Clear();
            foreach (PlayerAvatar a in Host.Players)
            {
                if (!a.IsSpawned) continue;
                _standings.Add(new FfaStanding { clientId = a.OwnerClientId, score = a.ObjectiveScore.Value, deaths = a.Deaths.Value });
                _standingPlayers.Add(a);
            }
            int index = FreeForAllRules.Leader(_standings);
            return index >= 0 ? _standingPlayers[index] : null;
        }
    }
}
