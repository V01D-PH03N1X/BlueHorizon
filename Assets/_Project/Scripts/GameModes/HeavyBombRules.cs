using BlueHorizon.Core;
using BlueHorizon.Objectives;

namespace BlueHorizon.GameModes
{
    public enum RoundEndReason : byte
    {
        None = 0,
        BombExploded,
        BombDefused,
        AttackersEliminated,
        DefendersEliminated,
        TimeExpired,
    }

    public struct RoundSnapshot
    {
        public int attackersAlive;
        public int defendersAlive;
        /// <summary>Players on each side when the round went live. Sides with zero players cannot be "eliminated"
        /// (development matches with reduced team sizes).</summary>
        public int attackersAtStart;
        public int defendersAtStart;
        public BombState bomb;
        public bool roundTimeExpired;
    }

    public struct RoundResult
    {
        public bool ended;
        public Side winner;
        public RoundEndReason reason;

        public static RoundResult Continue => default;

        public static RoundResult Win(Side winner, RoundEndReason reason) =>
            new() { ended = true, winner = winner, reason = reason };
    }

    /// <summary>
    /// HeavyBomb round-ending rules, evaluated by the server every tick while the round is live.
    /// Order of precedence (first match wins):
    ///   1. Bomb exploded                         → Attackers (BombExploded)
    ///   2. Bomb defused                          → Defenders (BombDefused)
    ///   3. Bomb planted and all defenders dead   → Attackers (DefendersEliminated)
    ///      Bomb planted otherwise                → round continues; the round timer no longer applies.
    ///   4. Not planted, all attackers dead       → Defenders (AttackersEliminated)
    ///      — this includes simultaneous elimination of both sides before a plant.
    ///   5. Not planted, all defenders dead       → Attackers (DefendersEliminated)
    ///   6. Not planted and round time expired    → Defenders (TimeExpired) — an unfinished plant does not count.
    /// </summary>
    public static class HeavyBombRules
    {
        public static RoundResult Evaluate(in RoundSnapshot s)
        {
            if (s.bomb == BombState.Exploded)
                return RoundResult.Win(Side.Attack, RoundEndReason.BombExploded);
            if (s.bomb == BombState.Defused)
                return RoundResult.Win(Side.Defense, RoundEndReason.BombDefused);

            bool attackersEliminated = s.attackersAtStart > 0 && s.attackersAlive <= 0;
            bool defendersEliminated = s.defendersAtStart > 0 && s.defendersAlive <= 0;

            if (s.bomb is BombState.Planted or BombState.Defusing)
            {
                return defendersEliminated
                    ? RoundResult.Win(Side.Attack, RoundEndReason.DefendersEliminated)
                    : RoundResult.Continue;
            }

            if (attackersEliminated)
                return RoundResult.Win(Side.Defense, RoundEndReason.AttackersEliminated);
            if (defendersEliminated)
                return RoundResult.Win(Side.Attack, RoundEndReason.DefendersEliminated);
            if (s.roundTimeExpired)
                return RoundResult.Win(Side.Defense, RoundEndReason.TimeExpired);
            return RoundResult.Continue;
        }

        public static string Describe(RoundEndReason reason) => reason switch
        {
            RoundEndReason.BombExploded => "The bomb detonated",
            RoundEndReason.BombDefused => "The bomb was defused",
            RoundEndReason.AttackersEliminated => "Attackers eliminated",
            RoundEndReason.DefendersEliminated => "Defenders eliminated",
            RoundEndReason.TimeExpired => "Time expired",
            _ => "",
        };
    }

    /// <summary>Match-level helpers: side assignment per round and victory checks.</summary>
    public static class MatchRules
    {
        /// <summary>Alpha attacks the first half, Bravo the second, alternating every <paramref name="roundsPerHalf"/> rounds.</summary>
        public static Team AttackingTeamForRound(int roundNumber, int roundsPerHalf)
        {
            if (roundsPerHalf <= 0) return Team.Alpha;
            int half = (System.Math.Max(1, roundNumber) - 1) / roundsPerHalf;
            return half % 2 == 0 ? Team.Alpha : Team.Bravo;
        }

        public static Team MatchWinner(int alphaScore, int bravoScore, int roundsToWin)
        {
            if (alphaScore >= roundsToWin) return Team.Alpha;
            if (bravoScore >= roundsToWin) return Team.Bravo;
            return Team.None;
        }
    }
}
