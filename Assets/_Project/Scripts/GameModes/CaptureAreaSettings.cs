using UnityEngine;

namespace BlueHorizon.GameModes
{
    /// <summary>Capture the Area: teams fight over one zone; the controlling team scores points over time.</summary>
    [CreateAssetMenu(menuName = "Blue Horizon/Game Modes/Capture the Area", fileName = "CaptureArea")]
    public sealed class CaptureAreaSettings : GameModeDefinition
    {
        [Tooltip("Seconds for one player to capture the area from neutral (or to neutralize the enemy's control).")]
        public float captureTime = 6f;
        [Tooltip("Extra capture speed per additional player inside (0.5 = +50 % each), capped at maxCaptureMultiplier.")]
        public float capturePerExtraPlayer = 0.5f;
        public float maxCaptureMultiplier = 2f;
        [Tooltip("Points per second for the controlling team while the area is not contested.")]
        public float pointsPerSecond = 1f;

        public override GameModeRules CreateRules() => new CaptureAreaMode(this);

        public ZoneSettings Zone => new()
        {
            captureTime = captureTime,
            capturePerExtraPlayer = capturePerExtraPlayer,
            maxCaptureMultiplier = maxCaptureMultiplier,
            pointsPerSecond = pointsPerSecond,
        };
    }
}
