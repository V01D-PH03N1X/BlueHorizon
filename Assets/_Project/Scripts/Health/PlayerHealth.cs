using System;
using BlueHorizon.Combat;
using BlueHorizon.Core;
using BlueHorizon.GameModes;
using BlueHorizon.Match;
using BlueHorizon.Player;
using Unity.Netcode;
using UnityEngine;

namespace BlueHorizon.Health
{
    /// <summary>
    /// Networked wrapper around <see cref="HealthModel"/>. The server owns the model; clients only see the
    /// replicated integers. All mutation methods are server-only. Also runs the downed state (see
    /// <see cref="DownedRules"/>): bleedout, finishing damage and teammate revives.
    /// </summary>
    [RequireComponent(typeof(PlayerAvatar))]
    public sealed class PlayerHealth : NetworkBehaviour, IDamageable
    {
        public readonly NetworkVariable<int> HealthValue = new();
        public readonly NetworkVariable<int> ArmorValue = new();
        public readonly NetworkVariable<int> BarrierValue = new();
        public readonly NetworkVariable<DownedNetState> DownedValue = new(new DownedNetState { reviverId = DownedNetState.NoReviver });

        /// <summary>Server: raised after damage is applied (victim, info, outcome).</summary>
        public static event Action<PlayerHealth, DamageInfo, DamageOutcome> ServerDamaged;
        /// <summary>Server: (downed player, info of the damage that downed them).</summary>
        public static event Action<PlayerAvatar, DamageInfo> ServerDowned;
        /// <summary>Server: (revived player, reviver, via the Revival ability).</summary>
        public static event Action<PlayerAvatar, PlayerAvatar, bool> ServerRevived;

        /// <summary>Client (owner): raised when this local player takes damage, with the world-space source.</summary>
        public event Action<Vector3, int> LocalDamageTaken;

        PlayerAvatar _avatar;
        HealthModel _model;
        DamageInfo _downedBy;

        public PlayerAvatar Avatar => _avatar;
        public HealthProfile Profile => _avatar.Agent != null ? _avatar.Agent.health : null;
        public int MaxHealth => Profile?.maxHealth ?? 100;
        public int MaxArmor => Profile?.maxArmor ?? 0;
        public int MaxBarrier => Profile?.maxBarrier ?? 0;

        public Team DamageTeam => _avatar.Team;
        public bool CanTakeDamage => _avatar.IsAlive;

        /// <summary>Alive but down: can crawl, cannot fight, waits for a revive (all peers).</summary>
        public bool IsDowned => DownedValue.Value.downed;
        /// <summary>Alive and not downed.</summary>
        public bool IsStanding => _avatar.IsAlive && !DownedValue.Value.downed;

        void Awake()
        {
            _avatar = GetComponent<PlayerAvatar>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                _model = new HealthModel(Profile ?? new HealthProfile());
                Publish();
            }
        }

        void Publish()
        {
            HealthValue.Value = _model.Health;
            ArmorValue.Value = _model.Armor;
            BarrierValue.Value = _model.Barrier;
        }

        double Now => NetworkManager.ServerTime.Time;

        static GameModeDefinition Mode => MatchManager.Instance != null ? MatchManager.Instance.Mode : null;

        public void ServerResetToFull()
        {
            if (!IsServer) return;
            ClearDowned();
            _model.ResetToFull();
            Publish();
        }

        /// <summary>Server: the agent (and so the health profile) changed; start over with full values.</summary>
        public void ServerRebuildForAgent()
        {
            if (!IsServer) return;
            ClearDowned();
            _model = new HealthModel(Profile ?? new HealthProfile());
            Publish();
        }

        public int ServerApplyDamage(in DamageInfo info, out bool killed)
        {
            killed = false;
            if (!IsServer || !_avatar.IsAlive || info.amount <= 0) return 0;

            if (IsDowned)
            {
                GameModeDefinition mode = Mode;
                DownedNetState d = DownedValue.Value;
                d.bleedoutEnd = DownedRules.ApplyDamage(d.bleedoutEnd, info.amount, mode != null ? mode.bleedoutTime : 15f, mode != null ? mode.downedHealth : 100);
                DownedValue.Value = d;
                DamageTakenRpc(info.origin, info.amount);
                if (d.bleedoutEnd <= Now)
                {
                    killed = true;
                    BHLog.Info("Match", $"{_avatar.PlayerName} was finished while downed");
                    Finish();
                }
                return info.amount;
            }

            DamageOutcome outcome = _model.ApplyDamage(info.amount, Now);
            Publish();
            killed = outcome.killed;

            ServerDamaged?.Invoke(this, info, outcome);
            if (outcome.TotalApplied > 0)
                DamageTakenRpc(info.origin, outcome.TotalApplied);
            if (killed)
            {
                GameModeDefinition mode = Mode;
                MatchManager match = MatchManager.Instance;
                bool allowed = mode != null && mode.downedState && !mode.freeForAll && match != null && match.Phase.Value == MatchPhase.Live;
                if (DownedRules.ShouldGoDown(allowed, info.source, TeammateStanding()))
                {
                    killed = false;
                    GoDown(info, mode);
                }
                else
                {
                    _avatar.ServerOnKilled(info);
                }
            }
            return outcome.TotalApplied;
        }

        bool TeammateStanding()
        {
            if (_avatar.Team == Team.None) return false;
            foreach (PlayerAvatar a in PlayerAvatar.All)
                if (a != _avatar && a.IsSpawned && a.Team == _avatar.Team && a.Health.IsStanding) return true;
            return false;
        }

        void GoDown(in DamageInfo info, GameModeDefinition mode)
        {
            _downedBy = info;
            double now = Now;
            DownedValue.Value = new DownedNetState
            {
                downed = true,
                downedAt = now,
                bleedoutEnd = now + mode.bleedoutTime,
                reviverId = DownedNetState.NoReviver,
            };
            string by = PlayerAvatar.TryGet(info.attackerClientId, out PlayerAvatar attacker) ? attacker.PlayerName : "<world>";
            BHLog.Info("Match", $"{_avatar.PlayerName} [{_avatar.Team}] downed by {by} (bleeds out in {mode.bleedoutTime:0}s)");
            ServerDowned?.Invoke(_avatar, info);
        }

        /// <summary>Server: the downed player dies (bleedout, finishing damage, no teammate left). The downer gets the kill.</summary>
        public void ServerFinishDowned(string reason)
        {
            if (!IsServer || !IsDowned) return;
            BHLog.Info("Match", $"{_avatar.PlayerName} {reason}");
            Finish();
        }

        void Finish()
        {
            ClearDowned();
            _avatar.ServerOnKilled(_downedBy);
        }

        void ClearDowned()
        {
            if (DownedValue.Value.downed || DownedValue.Value.reviverId != DownedNetState.NoReviver)
                DownedValue.Value = new DownedNetState { reviverId = DownedNetState.NoReviver };
        }

        /// <summary>Server: bring a downed player back up.</summary>
        public bool ServerRevive(int health, PlayerAvatar reviver, bool viaAbility)
        {
            if (!IsServer || !IsDowned || !_avatar.IsAlive) return false;
            ClearDowned();
            _model.Revive(health, Now);
            Publish();
            BHLog.Info("Match", $"{_avatar.PlayerName} revived by {(reviver != null ? reviver.PlayerName : "?")} " +
                                $"({(viaAbility ? "Revival ability" : "hold interact")}, {health} health)");
            ServerRevived?.Invoke(_avatar, reviver, viaAbility);
            return true;
        }

        public int ServerHeal(int amount)
        {
            if (!IsServer || IsDowned) return 0;
            int healed = _model.Heal(amount);
            if (healed > 0) Publish();
            return healed;
        }

        public void ServerKill(in DamageInfo info)
        {
            if (!IsServer) return;
            if (IsDowned)
            {
                _downedBy = info;
                Finish();
                return;
            }
            if (!_model.Kill()) return;
            Publish();
            _avatar.ServerOnKilled(info);
        }

        void Update()
        {
            if (!IsServer || _model == null) return;
            if (IsDowned)
            {
                ServerTickDowned();
                return;
            }
            if (_model.Tick(Now, Time.deltaTime))
                Publish();
        }

        void ServerTickDowned()
        {
            double now = Now;
            DownedNetState d = DownedValue.Value;
            if (now >= d.bleedoutEnd)
            {
                ServerFinishDowned("bled out");
                return;
            }

            GameModeDefinition mode = Mode;
            float range = mode != null ? mode.reviveRange : 1.8f;
            Vector3 position = _avatar.Simulation.Position;

            // Keep the current reviver while they hold on; otherwise the closest teammate holding interact takes over.
            PlayerAvatar reviver = null;
            if (d.reviverId != DownedNetState.NoReviver && PlayerAvatar.TryGet(d.reviverId, out PlayerAvatar current) && IsReviving(current, position, range))
                reviver = current;
            if (reviver == null)
            {
                float best = float.MaxValue;
                foreach (PlayerAvatar a in PlayerAvatar.All)
                {
                    if (a == _avatar || !IsReviving(a, position, range)) continue;
                    float dist = Vector3.Distance(a.Simulation.Position, position);
                    if (dist < best)
                    {
                        best = dist;
                        reviver = a;
                    }
                }
            }

            if (reviver == null)
            {
                if (d.reviverId != DownedNetState.NoReviver)
                {
                    d.reviverId = DownedNetState.NoReviver;
                    DownedValue.Value = d;
                }
                return;
            }
            if (d.reviverId != reviver.OwnerClientId)
            {
                d.reviverId = reviver.OwnerClientId;
                d.reviveStart = now;
                d.reviveEnd = now + (mode != null ? mode.reviveTime : 4f);
                DownedValue.Value = d;
                return;
            }
            if (now >= d.reviveEnd) ServerRevive(mode != null ? mode.reviveHealth : 30, reviver, viaAbility: false);
        }

        bool IsReviving(PlayerAvatar a, Vector3 position, float range) =>
            a.IsSpawned && DownedRules.CanRevive(a.Health.IsStanding, a.Team == _avatar.Team && a.Team != Team.None,
                Vector3.Distance(a.Simulation.Position, position), range, a.Simulation.InteractHeld);

        [Rpc(SendTo.Owner)]
        void DamageTakenRpc(Vector3 sourcePosition, int amount)
        {
            LocalDamageTaken?.Invoke(sourcePosition, amount);
        }
    }
}
