using BlueHorizon.Combat;
using BlueHorizon.Core;
using BlueHorizon.Match;
using BlueHorizon.Objectives;
using BlueHorizon.Player;

namespace BlueHorizon.GameModes
{
    /// <summary>Duel: 1v1 rounds. Elimination wins the round; a timeout goes to the player with more health left.</summary>
    public sealed class DuelMode : GameModeRules
    {
        readonly DuelSettings _settings;
        int _alphaAtStart, _bravoAtStart;

        public DuelMode(DuelSettings settings) : base(settings) => _settings = settings;

        public override string Summary => $"Duel: round {_settings.timeLimit}s, first to {_settings.scoreLimit}";

        public override void OnRoundStarting()
        {
            // Spawn ends alternate every round so neither player keeps the better side.
            bool alphaSouth = !_settings.alternateSpawns || Host.RoundNumber % 2 == 1;
            Host.AttackingTeam = alphaSouth ? Team.Alpha : Team.Bravo;
        }

        public override void OnLive()
        {
            _alphaAtStart = CountAlive(Team.Alpha);
            _bravoAtStart = CountAlive(Team.Bravo);
        }

        public override ModeOutcome TickLive(double now, float deltaTime, bool timeExpired)
        {
            bool alphaOut = _alphaAtStart > 0 && CountAlive(Team.Alpha) == 0;
            bool bravoOut = _bravoAtStart > 0 && CountAlive(Team.Bravo) == 0;
            if (alphaOut && bravoOut) return ModeOutcome.Round(Team.None, "Both eliminated · draw");
            if (alphaOut) return ModeOutcome.Round(Team.Bravo, "Opponent eliminated");
            if (bravoOut) return ModeOutcome.Round(Team.Alpha, "Opponent eliminated");
            if (!timeExpired) return ModeOutcome.Continue;

            Team winner = DuelRules.TimeoutWinner(EffectiveHealth(Team.Alpha), EffectiveHealth(Team.Bravo));
            return ModeOutcome.Round(winner, winner == Team.None ? "Time expired · draw" : "Time expired · more health remaining");
        }
    }

    /// <summary>Deathmatch: every kill scores; first to the kill limit wins, otherwise the leader when time runs out.</summary>
    public sealed class DeathmatchMode : GameModeRules
    {
        public DeathmatchMode(DeathmatchSettings settings) : base(settings) { }

        public override bool RoundBased => false;

        public override string Summary => $"Deathmatch: first to {Definition.scoreLimit} kills, {Definition.timeLimit}s, respawn {Definition.respawnDelay}s";

        public override void OnPlayerKilled(PlayerAvatar victim, PlayerAvatar killer, in DamageInfo info)
        {
            if (killer != null && killer != victim) killer.ServerAddObjectiveScore(1);
        }

        public override ModeOutcome TickLive(double now, float deltaTime, bool timeExpired)
        {
            PlayerAvatar leader = FreeForAllLeader();
            if (leader != null && leader.ObjectiveScore.Value >= Definition.scoreLimit)
                return ModeOutcome.MatchForPlayer(leader, $"{Definition.scoreLimit} kills reached");
            if (timeExpired)
                return ModeOutcome.MatchForPlayer(leader, "Time expired · most kills");
            return ModeOutcome.Continue;
        }
    }

    /// <summary>Weapon Game: each kill advances the killer one weapon on the ladder; a kill with the last weapon wins.</summary>
    public sealed class WeaponGameMode : GameModeRules
    {
        readonly WeaponGameSettings _settings;
        PlayerAvatar _winner;

        public WeaponGameMode(WeaponGameSettings settings) : base(settings) => _settings = settings;

        public override bool RoundBased => false;

        public int LadderLength => _settings.ladder.Length;

        public override string Summary => $"Weapon Game: {LadderLength} levels, {Definition.timeLimit}s, respawn {Definition.respawnDelay}s";

        public override PlayerLoadout LoadoutFor(PlayerAvatar avatar)
        {
            if (LadderLength == 0) return PlayerLoadout.Default;
            int level = UnityEngine.Mathf.Clamp(avatar.ObjectiveScore.Value, 0, LadderLength - 1);
            WeaponGameLevel step = _settings.ladder[level];
            return new PlayerLoadout
            {
                primary = (byte)step.weaponIndex,
                secondary = PlayerLoadout.None,
                suppressorMask = (byte)(step.suppressed ? 1 : 0),
            };
        }

        public override void OnMatchStart() => _winner = null;

        public override void OnReset() => _winner = null;

        public override void OnPlayerKilled(PlayerAvatar victim, PlayerAvatar killer, in DamageInfo info)
        {
            if (killer == null || killer == victim || info.source != DamageSource.Weapon || _winner != null) return;
            int level = killer.ObjectiveScore.Value;
            int next = WeaponGameRules.Advance(level, LadderLength, out bool won);
            if (won)
            {
                _winner = killer;
                return;
            }
            killer.ServerSetObjectiveScore(next);
            Host.RefreshLoadout(killer);
            BHLog.Info("Match", $"'{killer.PlayerName}' advanced to level {next + 1}/{LadderLength}");
        }

        public override ModeOutcome TickLive(double now, float deltaTime, bool timeExpired)
        {
            if (_winner != null) return ModeOutcome.MatchForPlayer(_winner, "Final weapon kill");
            if (timeExpired) return ModeOutcome.MatchForPlayer(FreeForAllLeader(), "Time expired · highest level");
            return ModeOutcome.Continue;
        }
    }

    /// <summary>Capture the Area: hold the zone to score; first team to the point limit wins.</summary>
    public sealed class CaptureAreaMode : GameModeRules
    {
        readonly CaptureAreaSettings _settings;
        readonly ZoneModel _zone;

        public CaptureAreaMode(CaptureAreaSettings settings) : base(settings)
        {
            _settings = settings;
            _zone = new ZoneModel(settings.Zone);
        }

        public override bool RoundBased => false;

        public override string Summary => $"Capture the Area: first to {_settings.scoreLimit} points, {_settings.timeLimit}s, capture {_settings.captureTime}s";

        public override void OnRoundStarting() => Host.AttackingTeam = Team.Alpha;

        public override void OnMatchStart() => Reset();

        public override void OnReset() => Reset();

        void Reset()
        {
            _zone.Reset();
            Publish();
        }

        public override ModeOutcome TickLive(double now, float deltaTime, bool timeExpired)
        {
            CaptureZone zone = CaptureZone.Main;
            int alpha = 0, bravo = 0;
            if (zone != null)
            {
                foreach (PlayerAvatar a in Host.Players)
                {
                    if (!a.IsSpawned || !a.IsAlive || !zone.Contains(a.Simulation.Position)) continue;
                    if (a.Team == Team.Alpha) alpha++;
                    else if (a.Team == Team.Bravo) bravo++;
                }
            }

            Team ownerBefore = _zone.Owner;
            int points = _zone.Step(alpha, bravo, deltaTime, out bool ownerChanged);
            if (ownerChanged)
            {
                BHLog.Info("Match", $"Area captured by {_zone.Owner} (was {ownerBefore})");
                foreach (PlayerAvatar a in Host.Players)
                    if (a.IsAlive && a.Team == _zone.Owner && zone != null && zone.Contains(a.Simulation.Position)) a.ServerAddObjectiveScore(1);
            }
            if (points > 0) Host.AddTeamScore(_zone.Owner, points);
            Publish();

            int alphaScore = Host.TeamScore(Team.Alpha), bravoScore = Host.TeamScore(Team.Bravo);
            if (alphaScore >= _settings.scoreLimit) return ModeOutcome.MatchForTeam(Team.Alpha, $"{_settings.scoreLimit} points reached");
            if (bravoScore >= _settings.scoreLimit) return ModeOutcome.MatchForTeam(Team.Bravo, $"{_settings.scoreLimit} points reached");
            if (!timeExpired) return ModeOutcome.Continue;
            Team leader = alphaScore > bravoScore ? Team.Alpha : bravoScore > alphaScore ? Team.Bravo : Team.None;
            return ModeOutcome.MatchForTeam(leader, leader == Team.None ? "Time expired · draw" : "Time expired · more points");
        }

        void Publish()
        {
            Host.PublishModeStatus(new ModeStatus
            {
                owner = _zone.Owner,
                capturing = _zone.Capturing,
                progress = _zone.Progress,
                contested = _zone.Contested,
            });
        }
    }
}
