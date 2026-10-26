namespace BlueHorizon.Core
{
    /// <summary>A persistent team for the whole match. Which team attacks swaps at half-time.</summary>
    public enum Team : byte
    {
        None = 0,
        Alpha = 1,
        Bravo = 2,
    }

    /// <summary>The objective role a team plays in the current HeavyBomb round.</summary>
    public enum Side : byte
    {
        None = 0,
        Attack = 1,
        Defense = 2,
    }

    public static class TeamUtility
    {
        public static Team Other(this Team team) => team switch
        {
            Team.Alpha => Team.Bravo,
            Team.Bravo => Team.Alpha,
            _ => Team.None,
        };

        public static string DisplayName(this Team team) => team switch
        {
            Team.Alpha => "Alpha",
            Team.Bravo => "Bravo",
            _ => "Spectator",
        };

        public static string DisplayName(this Side side) => side switch
        {
            Side.Attack => "Attackers",
            Side.Defense => "Defenders",
            _ => "Spectators",
        };
    }
}
