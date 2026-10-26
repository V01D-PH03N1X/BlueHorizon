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
        public AgentDefinition defaultAgent;

        [Header("Loadout")]
        public WeaponDefinition primaryWeapon;
        public WeaponDefinition secondaryWeapon;
        [Tooltip("Muzzle attachment players can toggle during the pre-round phase.")]
        public AttachmentDefinition suppressor;

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

        public WeaponDefinition WeaponForSlot(int slot) => slot == 0 ? primaryWeapon : secondaryWeapon;

        public GameObject[] NetworkPrefabs => new[] { playerPrefab, matchManagerPrefab, smokeGrenadePrefab, smokeCloudPrefab, wayblockerPrefab };
    }
}
