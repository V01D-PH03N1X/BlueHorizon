using BlueHorizon.Agents;
using BlueHorizon.Attachments;
using BlueHorizon.GameModes;
using BlueHorizon.Weapons;
using UnityEngine;

namespace BlueHorizon.Core
{
    /// <summary>
    /// Root registry of game data and network prefabs, referenced by the bootstrap scene.
    /// Systems read content through <see cref="Instance"/> instead of hard references so data can be swapped per build.
    /// </summary>
    [CreateAssetMenu(menuName = "Blue Horizon/Game Content", fileName = "GameContent")]
    public sealed class GameContent : ScriptableObject
    {
        public static GameContent Instance { get; private set; }

        [Header("Rules")]
        public HeavyBombSettings heavyBomb;
        [Tooltip("All playable modes; selected on the server with -mode <modeId>. The first entry is the default.")]
        public GameModeDefinition[] modes;
        public AgentDefinition defaultAgent;

        [Header("Loadout")]
        public WeaponDefinition primaryWeapon;
        public WeaponDefinition secondaryWeapon;
        [Tooltip("Muzzle attachment players can toggle during the pre-round phase.")]
        public AttachmentDefinition suppressor;
        [Tooltip("Every weapon in the game; loadouts and the Weapon Game ladder refer to these indices.")]
        public WeaponDefinition[] weapons;

        [Header("Network prefabs")]
        public GameObject playerPrefab;
        public GameObject matchManagerPrefab;
        public GameObject smokeGrenadePrefab;
        public GameObject smokeCloudPrefab;
        public GameObject wayblockerPrefab;

        [Header("Materials")]
        public Material litMaterial;
        public Material transparentMaterial;
        public Material unlitMaterial;
        public Material emissiveMaterial;

        [Header("Scenes")]
        public string mapScene = "VerticalSlice";

        public void MakeCurrent() => Instance = this;

        public WeaponDefinition WeaponByIndex(int index)
        {
            if (weapons != null && weapons.Length > 0)
                return index >= 0 && index < weapons.Length ? weapons[index] : null;
            return index == 0 ? primaryWeapon : index == 1 ? secondaryWeapon : null; // Before the weapon list existed.
        }

        public GameModeDefinition ModeByIndex(int index) =>
            modes != null && index >= 0 && index < modes.Length ? modes[index] : heavyBomb;

        /// <summary>Index of the mode with this id (case-insensitive), or 0 (the default mode).</summary>
        public int FindModeIndex(string modeId)
        {
            if (modes == null) return 0;
            for (int i = 0; i < modes.Length; i++)
            {
                if (modes[i] != null && string.Equals(modes[i].modeId, modeId, System.StringComparison.OrdinalIgnoreCase)) return i;
            }
            return 0;
        }

        public GameObject[] NetworkPrefabs => new[] { playerPrefab, matchManagerPrefab, smokeGrenadePrefab, smokeCloudPrefab, wayblockerPrefab };
    }
}
