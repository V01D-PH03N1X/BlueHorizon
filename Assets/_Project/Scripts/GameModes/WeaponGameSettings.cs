using System;
using UnityEngine;

namespace BlueHorizon.GameModes
{
    [Serializable]
    public struct WeaponGameLevel
    {
        [Tooltip("Index into GameContent.weapons.")]
        public int weaponIndex;
        public bool suppressed;
    }

    /// <summary>
    /// Weapon Game: free-for-all ladder. Every kill moves the killer to the next weapon; a kill with the last weapon
    /// wins. Abilities are disabled. New weapons from Milestone 4 extend the ladder as data.
    /// </summary>
    [CreateAssetMenu(menuName = "Blue Horizon/Game Modes/Weapon Game", fileName = "WeaponGame")]
    public sealed class WeaponGameSettings : GameModeDefinition
    {
        public WeaponGameLevel[] ladder =
        {
            new() { weaponIndex = 0 },
            new() { weaponIndex = 0, suppressed = true },
            new() { weaponIndex = 1 },
            new() { weaponIndex = 1, suppressed = true },
        };

        public override GameModeRules CreateRules() => new WeaponGameMode(this);
    }
}
