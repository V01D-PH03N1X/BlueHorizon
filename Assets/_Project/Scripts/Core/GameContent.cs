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
        [Tooltip("Every agent in the game; players pick by index. Append only. The first entry is the default.")]
        public AgentDefinition[] agents;

        [Header("Loadout")]
        public WeaponDefinition primaryWeapon;
        public WeaponDefinition secondaryWeapon;
        [Tooltip("Muzzle attachment players can toggle with the attachment key during the pre-round phase.")]
        public AttachmentDefinition suppressor;
        [Tooltip("Every weapon in the game; loadouts and the Weapon Game ladder refer to these indices.")]
        public WeaponDefinition[] weapons;
        [Tooltip("Every attachment in the game; loadouts refer to these indices.")]
        public AttachmentDefinition[] attachments;

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

        public AgentDefinition AgentByIndex(int index) =>
            agents != null && index >= 0 && index < agents.Length && agents[index] != null ? agents[index] : defaultAgent;

        /// <summary>Index of the agent with this id or callsign (case-insensitive), or -1.</summary>
        public int FindAgentIndex(string id)
        {
            if (agents == null || string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < agents.Length; i++)
            {
                if (agents[i] == null) continue;
                if (string.Equals(agents[i].agentId, id, System.StringComparison.OrdinalIgnoreCase)
                    || string.Equals(agents[i].callsign, id, System.StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        public AttachmentDefinition AttachmentByIndex(int index) =>
            attachments != null && index >= 0 && index < attachments.Length ? attachments[index] : null;

        /// <summary>Index of an attachment in <see cref="attachments"/>, or <c>PlayerLoadout.None</c> (255).</summary>
        public byte IndexOfAttachment(AttachmentDefinition attachment)
        {
            if (attachments == null || attachment == null) return 255;
            int i = System.Array.IndexOf(attachments, attachment);
            return i >= 0 && i < 255 ? (byte)i : (byte)255;
        }

        public int IndexOfWeapon(WeaponDefinition weapon) => weapons == null || weapon == null ? -1 : System.Array.IndexOf(weapons, weapon);

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
