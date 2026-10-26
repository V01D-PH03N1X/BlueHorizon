using BlueHorizon.Core;
using UnityEngine;

namespace BlueHorizon.GameModes
{
    /// <summary>
    /// Base data for any game mode. Future modes (Duel, Deathmatch, Capture the Area, Weapon Game)
    /// derive from this and get their own rules class, reusing the match, team and spawn systems.
    /// </summary>
    public abstract class GameModeDefinition : ScriptableObject
    {
        public string modeId = "mode";
        public string displayName = "Mode";
        public int teamSize = 5;
        public int maxPlayers = 10;
        [Tooltip("Players required on each team before a match starts.")]
        public int minPlayersPerTeam = 1;
        public bool friendlyFire;
    }

    [CreateAssetMenu(menuName = "Blue Horizon/Game Modes/HeavyBomb", fileName = "HeavyBomb")]
    public sealed class HeavyBombSettings : GameModeDefinition
    {
        [Header("Flow (seconds)")]
        public float matchStartCountdown = 5f;
        [Tooltip("Freeze time at the start of each round: players can look and change loadout but not move or shoot.")]
        public float preRoundTime = 6f;
        public float roundTime = 100f;
        public float postRoundTime = 6f;
        public float matchOverTime = 12f;

        [Header("Bomb (seconds / metres)")]
        public float plantDuration = 4f;
        public float defuseDuration = 7f;
        public float fuseDuration = 40f;
        public float defuseRange = 1.9f;
        public float pickupRange = 1.5f;
        [Tooltip("Planting/defusing are cancelled if the player moves faster than this (m/s).")]
        public float maxInteractSpeed = 0.6f;

        [Header("Match")]
        public int roundsToWin = 7;
        [Tooltip("Teams swap sides after this many rounds.")]
        public int roundsPerHalf = 6;

        /// <summary>Returns a copy with any command-line/config overrides applied.</summary>
        public HeavyBombSettings WithOverrides(LaunchConfig config)
        {
            HeavyBombSettings copy = Instantiate(this);
            if (config == null) return copy;
            if (config.roundTime > 0) copy.roundTime = config.roundTime;
            if (config.preRoundTime >= 0) copy.preRoundTime = config.preRoundTime;
            if (config.postRoundTime >= 0) copy.postRoundTime = config.postRoundTime;
            if (config.plantTime > 0) copy.plantDuration = config.plantTime;
            if (config.defuseTime > 0) copy.defuseDuration = config.defuseTime;
            if (config.fuseTime > 0) copy.fuseDuration = config.fuseTime;
            if (config.roundsToWin > 0) copy.roundsToWin = config.roundsToWin;
            if (config.minPlayersPerTeam >= 0) copy.minPlayersPerTeam = config.minPlayersPerTeam;
            copy.maxPlayers = Mathf.Min(copy.maxPlayers, config.maxPlayers);
            return copy;
        }
    }
}
