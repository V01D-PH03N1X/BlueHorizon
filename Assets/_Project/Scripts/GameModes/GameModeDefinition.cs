using BlueHorizon.Core;
using UnityEngine;
using UnityEngine.Serialization;

namespace BlueHorizon.GameModes
{
    /// <summary>
    /// Shared configuration for every game mode. A mode is an asset (this data) plus a <see cref="GameModeRules"/>
    /// class created by <see cref="CreateRules"/>; the MatchManager provides players, teams, spawns, phases,
    /// respawns and scoring, so a new mode only implements what is specific to it.
    /// </summary>
    public abstract class GameModeDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string modeId = "mode";
        public string displayName = "Mode";
        [TextArea] public string description = "";

        [Header("Players")]
        [Tooltip("Players per team (team modes). Ignored for free-for-all.")]
        public int teamSize = 5;
        public int maxPlayers = 10;
        [Tooltip("Players required on each team before a match starts (free-for-all: players in total).")]
        public int minPlayersPerTeam = 1;
        [Tooltip("Every player fights for themselves; there are no teams.")]
        public bool freeForAll;
        public bool friendlyFire;
        public bool abilitiesEnabled = true;
        [Tooltip("Players may toggle the suppressor during warmup/buy time.")]
        public bool loadoutChangesEnabled = true;

        [Header("Downed state")]
        [Tooltip("Lethal weapon damage downs a player instead of killing them while a teammate is still standing.")]
        public bool downedState;
        [Tooltip("Seconds a downed player lasts without taking damage.")]
        public float bleedoutTime = 15f;
        [Tooltip("Seconds a teammate has to hold interact next to a downed player.")]
        public float reviveTime = 4f;
        public float reviveRange = 1.8f;
        public int reviveHealth = 30;
        [Tooltip("Damage needed to finish a downed player who has just gone down.")]
        public int downedHealth = 100;

        [Header("Flow (seconds)")]
        public float matchStartCountdown = 5f;
        [Tooltip("Freeze time at the start of each round: players can look and change loadout but not move or shoot.")]
        public float preRoundTime = 6f;
        [Tooltip("Round length for round-based modes, match length for continuous modes.")]
        [FormerlySerializedAs("roundTime")] public float timeLimit = 100f;
        public float postRoundTime = 6f;
        public float matchOverTime = 12f;
        [Tooltip("Seconds until a dead player respawns while the match is live. 0 = no respawn until the next round.")]
        public float respawnDelay;

        [Header("Victory")]
        [Tooltip("Rounds (round modes), points or kills (continuous modes) needed to win.")]
        [FormerlySerializedAs("roundsToWin")] public int scoreLimit = 7;

        public abstract GameModeRules CreateRules();

        /// <summary>Returns a copy with any command-line/config overrides applied.</summary>
        public GameModeDefinition WithOverrides(LaunchConfig config)
        {
            GameModeDefinition copy = Instantiate(this);
            if (config == null) return copy;
            if (config.roundTime > 0) copy.timeLimit = config.roundTime;
            if (config.preRoundTime >= 0) copy.preRoundTime = config.preRoundTime;
            if (config.postRoundTime >= 0) copy.postRoundTime = config.postRoundTime;
            if (config.roundsToWin > 0) copy.scoreLimit = config.roundsToWin;
            if (config.minPlayersPerTeam >= 0) copy.minPlayersPerTeam = config.minPlayersPerTeam;
            if (config.respawnDelay >= 0) copy.respawnDelay = config.respawnDelay;
            if (config.downed == "on") copy.downedState = true;
            else if (config.downed == "off") copy.downedState = false;
            copy.maxPlayers = Mathf.Min(copy.maxPlayers, config.maxPlayers);
            copy.ApplyModeOverrides(config);
            return copy;
        }

        protected virtual void ApplyModeOverrides(LaunchConfig config) { }
    }
}
