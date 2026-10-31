using UnityEngine;

namespace BlueHorizon.GameModes
{
    /// <summary>Duel: 1v1 elimination rounds. A timeout goes to the player with more remaining health and shields.</summary>
    [CreateAssetMenu(menuName = "Blue Horizon/Game Modes/Duel", fileName = "Duel")]
    public sealed class DuelSettings : GameModeDefinition
    {
        [Tooltip("Swap spawn ends every round for fairness.")]
        public bool alternateSpawns = true;

        public override GameModeRules CreateRules() => new DuelMode(this);
    }
}
