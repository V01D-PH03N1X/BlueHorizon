using UnityEngine;

namespace BlueHorizon.GameModes
{
    /// <summary>Deathmatch: free-for-all, respawns, first to the kill limit (or most kills when time runs out).</summary>
    [CreateAssetMenu(menuName = "Blue Horizon/Game Modes/Deathmatch", fileName = "Deathmatch")]
    public sealed class DeathmatchSettings : GameModeDefinition
    {
        public override GameModeRules CreateRules() => new DeathmatchMode(this);
    }
}
