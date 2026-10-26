using System;
using System.Collections.Generic;
using BlueHorizon.Abilities;
using BlueHorizon.Agents;
using BlueHorizon.Attachments;
using BlueHorizon.Combat;
using BlueHorizon.Core;
using BlueHorizon.Health;
using BlueHorizon.Weapons;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace BlueHorizon.Player
{
    /// <summary>
    /// Identity, team, life state, scoreboard stats and loadout of a connected player.
    /// The server writes every value; clients read them for presentation.
    /// </summary>
    [RequireComponent(typeof(PlayerSimulation), typeof(PlayerHealth), typeof(AbilityController))]
    public sealed class PlayerAvatar : NetworkBehaviour
    {
        public static readonly List<PlayerAvatar> All = new();
        public static PlayerAvatar Local { get; private set; }

        public static event Action<PlayerAvatar> LocalPlayerSpawned;
        public static event Action<PlayerAvatar> AnySpawned;
        public static event Action<PlayerAvatar> AnyDespawned;
        /// <summary>Server: (victim, damage that killed).</summary>
        public static event Action<PlayerAvatar, DamageInfo> ServerKilled;

        public readonly NetworkVariable<FixedString32Bytes> NameValue = new();
        public readonly NetworkVariable<Team> TeamValue = new();
        public readonly NetworkVariable<bool> AliveValue = new();
        public readonly NetworkVariable<int> Kills = new();
        public readonly NetworkVariable<int> Deaths = new();
        public readonly NetworkVariable<int> ObjectiveScore = new();
        /// <summary>Bit per weapon slot: suppressor fitted.</summary>
        public readonly NetworkVariable<byte> SuppressorMask = new();

        PlayerSimulation _simulation;
        PlayerHealth _health;
        AbilityController _abilities;
        WeaponStats _primary, _secondary;
        readonly List<AttachmentDefinition> _attachmentScratch = new(2);

        public PlayerSimulation Simulation => _simulation;
        public PlayerHealth Health => _health;
        public AbilityController Abilities => _abilities;

        public string PlayerName => NameValue.Value.ToString();
        public Team Team => TeamValue.Value;
        public bool IsAlive => AliveValue.Value;
        public AgentDefinition Agent => GameContent.Instance != null ? GameContent.Instance.defaultAgent : null;

        /// <summary>Server diagnostics: shots resolved and shots that hit a character.</summary>
        public int ServerShotsFired { get; set; }
        public int ServerCharacterHits { get; set; }

        /// <summary>Server: wall-clock time of death, for warmup respawns.</summary>
        public double DeathTime { get; private set; }

        public event Action LoadoutChanged;

        void Awake()
        {
            _simulation = GetComponent<PlayerSimulation>();
            _health = GetComponent<PlayerHealth>();
            _abilities = GetComponent<AbilityController>();
            PlayerRig.Build(this);
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer && _pendingInit)
            {
                NameValue.Value = new FixedString32Bytes(_pendingName);
                TeamValue.Value = _pendingTeam;
                AliveValue.Value = false;
                _pendingInit = false;
            }
            All.Add(this);
            SuppressorMask.OnValueChanged += OnSuppressorChanged;
            AliveValue.OnValueChanged += OnAliveChanged;
            RecomputeStats();
            ApplyAliveState(AliveValue.Value);

            if (IsOwner)
            {
                Local = this;
                LocalPlayerSpawned?.Invoke(this);
            }
            AnySpawned?.Invoke(this);
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            SuppressorMask.OnValueChanged -= OnSuppressorChanged;
            AliveValue.OnValueChanged -= OnAliveChanged;
            if (Local == this) Local = null;
            AnyDespawned?.Invoke(this);
        }

        void OnSuppressorChanged(byte previous, byte current)
        {
            RecomputeStats();
            LoadoutChanged?.Invoke();
        }

        void OnAliveChanged(bool previous, bool current) => ApplyAliveState(current);

        void ApplyAliveState(bool alive)
        {
            // Dead players neither collide nor absorb shots.
            var cc = GetComponent<CharacterController>();
            if (cc != null) cc.enabled = alive;
            foreach (HitboxCollider hb in GetComponentsInChildren<HitboxCollider>(true))
                hb.GetComponent<Collider>().enabled = alive;
        }

        void RecomputeStats()
        {
            GameContent content = GameContent.Instance;
            if (content == null) return;
            _primary = ComputeStats(content.primaryWeapon, 0, content);
            _secondary = ComputeStats(content.secondaryWeapon, 1, content);
        }

        WeaponStats ComputeStats(WeaponDefinition weapon, int slot, GameContent content)
        {
            _attachmentScratch.Clear();
            if ((SuppressorMask.Value & (1 << slot)) != 0 && content.suppressor != null)
                _attachmentScratch.Add(content.suppressor);
            return WeaponStats.Compute(weapon, _attachmentScratch);
        }

        public WeaponStats PrimaryStats => _primary;
        public WeaponStats SecondaryStats => _secondary;
        public WeaponStats StatsForSlot(int slot) => slot == 0 ? _primary : _secondary;
        public bool HasSuppressor(int slot) => (SuppressorMask.Value & (1 << slot)) != 0;

        // ───────────── Server API ─────────────

        string _pendingName;
        Team _pendingTeam;
        bool _pendingInit;

        /// <summary>Server, before Spawn(): identity is published in OnNetworkSpawn so it ships with the spawn message.</summary>
        public void ServerInitialize(string playerName, Team team)
        {
            _pendingName = playerName;
            _pendingTeam = team;
            _pendingInit = true;
        }

        public void ServerSetTeam(Team team) => TeamValue.Value = team;

        public void ServerSpawnAt(Vector3 position, float yaw)
        {
            _health.ServerResetToFull();
            _abilities.ServerResetForRound();
            AliveValue.Value = true;
            ApplyAliveState(true);
            _simulation.ServerTeleport(position, yaw, resetWeapons: true);
        }

        /// <summary>Moves the player out of play without counting a death (late join, side swap).</summary>
        public void ServerSetSpectating()
        {
            AliveValue.Value = false;
            ApplyAliveState(false);
        }

        public void ServerResetStats()
        {
            Kills.Value = 0;
            Deaths.Value = 0;
            ObjectiveScore.Value = 0;
        }

        public void ServerAddObjectiveScore(int amount) => ObjectiveScore.Value += amount;

        /// <summary>Server: restore scoreboard and loadout after a reconnect.</summary>
        public void ServerRestoreStats(int kills, int deaths, int objectiveScore, byte suppressorMask)
        {
            Kills.Value = kills;
            Deaths.Value = deaths;
            ObjectiveScore.Value = objectiveScore;
            SuppressorMask.Value = suppressorMask;
            RecomputeStats();
        }

        public void ServerToggleSuppressor(int slot)
        {
            SuppressorMask.Value ^= (byte)(1 << slot);
            RecomputeStats();
        }

        internal void ServerOnKilled(in DamageInfo info)
        {
            if (!IsServer || !AliveValue.Value) return;
            AliveValue.Value = false;
            ApplyAliveState(false);
            DeathTime = NetworkManager.ServerTime.Time;
            Deaths.Value++;

            if (info.attackerClientId != OwnerClientId && TryGet(info.attackerClientId, out PlayerAvatar killer) && killer.Team != Team)
                killer.Kills.Value++;

            ServerKilled?.Invoke(this, info);
        }

        public static bool TryGet(ulong clientId, out PlayerAvatar avatar)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i].OwnerClientId == clientId)
                {
                    avatar = All[i];
                    return true;
                }
            }
            avatar = null;
            return false;
        }

        /// <summary>Owner-only hit confirmation for hit markers and kill feedback.</summary>
        [Rpc(SendTo.Owner)]
        public void HitConfirmRpc(HitZone zone, int damage, bool killed, bool structure)
        {
            HitConfirmed?.Invoke(zone, damage, killed, structure);
        }

        public event Action<HitZone, int, bool, bool> HitConfirmed;
    }
}
