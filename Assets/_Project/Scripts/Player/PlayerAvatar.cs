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
        /// <summary>Weapons and attachments in each slot, chosen by the player and the game mode.</summary>
        public readonly NetworkVariable<PlayerLoadout> LoadoutValue = new(PlayerLoadout.Default);

        PlayerSimulation _simulation;
        PlayerHealth _health;
        AbilityController _abilities;
        WeaponStats _primary, _secondary;
        readonly List<AttachmentDefinition> _attachmentScratch = new(PlayerLoadout.AttachmentSlots);

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
            LoadoutValue.OnValueChanged += OnLoadoutChanged;
            AliveValue.OnValueChanged += OnAliveChanged;
            RecomputeStats();
            ApplyAliveState(AliveValue.Value);

            if (IsOwner)
            {
                Local = this;
                LocalPlayerSpawned?.Invoke(this);
                LocalLoadout.SendInitial(this);
            }
            AnySpawned?.Invoke(this);
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            LoadoutValue.OnValueChanged -= OnLoadoutChanged;
            AliveValue.OnValueChanged -= OnAliveChanged;
            if (Local == this) Local = null;
            AnyDespawned?.Invoke(this);
        }

        void OnLoadoutChanged(PlayerLoadout previous, PlayerLoadout current)
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
            _primary = ComputeStats(WeaponInSlot(0), 0, content);
            _secondary = ComputeStats(WeaponInSlot(1), 1, content);
        }

        /// <summary>Weapon carried in a slot, or null if the slot is empty in this mode.</summary>
        public WeaponDefinition WeaponInSlot(int slot)
        {
            GameContent content = GameContent.Instance;
            return content != null ? content.WeaponByIndex(LoadoutValue.Value.Slot(slot)) : null;
        }

        WeaponStats ComputeStats(WeaponDefinition weapon, int slot, GameContent content)
        {
            _attachmentScratch.Clear();
            PlayerLoadout loadout = LoadoutValue.Value;
            for (int a = 0; a < PlayerLoadout.AttachmentSlots; a++)
            {
                AttachmentDefinition att = content.AttachmentByIndex(loadout.Attachment(slot, (AttachmentSlot)a));
                if (att != null) _attachmentScratch.Add(att);
            }
            return WeaponStats.Compute(weapon, _attachmentScratch);
        }

        public WeaponStats PrimaryStats => _primary;
        public WeaponStats SecondaryStats => _secondary;
        public WeaponStats StatsForSlot(int slot) => slot == 0 ? _primary : _secondary;
        public bool HasSuppressor(int slot) => StatsForSlot(slot).Suppressed;

        /// <summary>Attachment fitted to a weapon slot (null if none).</summary>
        public AttachmentDefinition AttachmentIn(int weaponSlot, AttachmentSlot slot)
        {
            GameContent content = GameContent.Instance;
            return content != null ? content.AttachmentByIndex(LoadoutValue.Value.Attachment(weaponSlot, slot)) : null;
        }

        /// <summary>Server: the loadout the player asked for; modes apply it at spawn unless they dictate weapons.</summary>
        public PlayerLoadout PreferredLoadout { get; private set; } = PlayerLoadout.Default;

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

        public void ServerSetObjectiveScore(int value) => ObjectiveScore.Value = value;

        /// <summary>Server: set the carried weapons and attachments. Weapons are reset by the caller.</summary>
        public void ServerSetLoadout(PlayerLoadout loadout)
        {
            LoadoutValue.Value = loadout;
            RecomputeStats();
        }

        /// <summary>Server: restore scoreboard and loadout choice after a reconnect.</summary>
        public void ServerRestoreStats(int kills, int deaths, int objectiveScore, in PlayerLoadout preferred)
        {
            Kills.Value = kills;
            Deaths.Value = deaths;
            ObjectiveScore.Value = objectiveScore;
            PreferredLoadout = preferred;
        }

        /// <summary>Server: fit or remove the suppressor on a weapon slot, if that weapon accepts it.</summary>
        public bool ServerToggleSuppressor(int slot)
        {
            GameContent content = GameContent.Instance;
            if (content == null || content.suppressor == null) return false;
            if (!LoadoutRules.Allows(WeaponInSlot(slot), content.suppressor, AttachmentSlot.Muzzle)) return false;
            byte index = content.IndexOfAttachment(content.suppressor);
            PlayerLoadout current = LoadoutValue.Value;
            byte next = current.Attachment(slot, AttachmentSlot.Muzzle) == index ? PlayerLoadout.None : index;
            current.SetAttachment(slot, AttachmentSlot.Muzzle, next);
            ServerSetLoadout(current);
            PlayerLoadout preferred = PreferredLoadout;
            if (preferred.Slot(slot) == current.Slot(slot))
            {
                preferred.SetAttachment(slot, AttachmentSlot.Muzzle, next);
                PreferredLoadout = preferred;
            }
            return true;
        }

        float _lastLoadoutRequest = -10f;

        /// <summary>
        /// Client to server: choose weapons and attachments. The server validates the request, remembers it for the
        /// next spawn and applies it immediately while loadout changes are allowed (warmup / pre-round).
        /// </summary>
        [Rpc(SendTo.Server)]
        public void RequestLoadoutRpc(PlayerLoadout requested, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
            float now = Time.realtimeSinceStartup;
            if (now - _lastLoadoutRequest < 0.2f)
            {
                BHLog.Warn("Security", $"Loadout request from client {OwnerClientId} rate-limited");
                return;
            }
            _lastLoadoutRequest = now;

            GameContent content = GameContent.Instance;
            if (content == null) return;
            PlayerLoadout valid = LoadoutRules.Sanitize(requested, content.weapons, content.attachments);
            if (!valid.Equals(requested))
                BHLog.Warn("Loadout", $"{PlayerName}: invalid loadout request {requested} corrected to {valid}");
            PreferredLoadout = valid;

            Match.MatchManager match = Match.MatchManager.Instance;
            bool applyNow = match == null || match.LoadoutChangesAllowed;
            if (applyNow && match != null) match.RefreshLoadout(this);
            else if (applyNow) ServerSetLoadout(valid);
            BHLog.Info("Loadout", $"{PlayerName} loadout {valid} ({(applyNow ? "applied" : "from next spawn")})");
        }

        internal void ServerOnKilled(in DamageInfo info)
        {
            if (!IsServer || !AliveValue.Value) return;
            AliveValue.Value = false;
            ApplyAliveState(false);
            DeathTime = NetworkManager.ServerTime.Time;
            Deaths.Value++;

            bool freeForAll = Match.MatchManager.Instance != null && Match.MatchManager.Instance.FreeForAll;
            if (info.attackerClientId != OwnerClientId && TryGet(info.attackerClientId, out PlayerAvatar killer)
                && GameModes.TeamRules.AreEnemies(killer.Team, Team, freeForAll))
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
