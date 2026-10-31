using BlueHorizon.Core;
using UnityEngine;

namespace BlueHorizon.GameModes
{
    /// <summary>HeavyBomb: attackers plant, defenders defuse; round based, sides swap at half-time.</summary>
    [CreateAssetMenu(menuName = "Blue Horizon/Game Modes/HeavyBomb", fileName = "HeavyBomb")]
    public sealed class HeavyBombSettings : GameModeDefinition
    {
        [Header("Bomb (seconds / metres)")]
        public float plantDuration = 4f;
        public float defuseDuration = 7f;
        public float fuseDuration = 40f;
        public float defuseRange = 1.9f;
        public float pickupRange = 1.5f;
        [Tooltip("Planting/defusing are cancelled if the player moves faster than this (m/s).")]
        public float maxInteractSpeed = 0.6f;

        [Header("Sides")]
        [Tooltip("Teams swap sides after this many rounds.")]
        public int roundsPerHalf = 6;

        public override GameModeRules CreateRules() => new HeavyBombMode(this);

        protected override void ApplyModeOverrides(LaunchConfig config)
        {
            if (config.plantTime > 0) plantDuration = config.plantTime;
            if (config.defuseTime > 0) defuseDuration = config.defuseTime;
            if (config.fuseTime > 0) fuseDuration = config.fuseTime;
        }
    }
}
