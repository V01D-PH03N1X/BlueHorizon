using System;
using BlueHorizon.Combat;
using BlueHorizon.Core;
using BlueHorizon.Player;
using Unity.Netcode;
using UnityEngine;

namespace BlueHorizon.Health
{
    /// <summary>
    /// Networked wrapper around <see cref="HealthModel"/>. The server owns the model; clients only see the
    /// replicated integers. All mutation methods are server-only.
    /// </summary>
    [RequireComponent(typeof(PlayerAvatar))]
    public sealed class PlayerHealth : NetworkBehaviour, IDamageable
    {
        public readonly NetworkVariable<int> HealthValue = new();
        public readonly NetworkVariable<int> ArmorValue = new();
        public readonly NetworkVariable<int> BarrierValue = new();

        /// <summary>Server: raised after damage is applied (victim, info, outcome).</summary>
        public static event Action<PlayerHealth, DamageInfo, DamageOutcome> ServerDamaged;

        /// <summary>Client (owner): raised when this local player takes damage, with the world-space source.</summary>
        public event Action<Vector3, int> LocalDamageTaken;

        PlayerAvatar _avatar;
        HealthModel _model;

        public PlayerAvatar Avatar => _avatar;
        public HealthProfile Profile => _avatar.Agent != null ? _avatar.Agent.health : null;
        public int MaxHealth => Profile?.maxHealth ?? 100;
        public int MaxArmor => Profile?.maxArmor ?? 0;
        public int MaxBarrier => Profile?.maxBarrier ?? 0;

        public Team DamageTeam => _avatar.Team;
        public bool CanTakeDamage => _avatar.IsAlive;

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

        public void ServerResetToFull()
        {
            if (!IsServer) return;
            _model.ResetToFull();
            Publish();
        }

        public int ServerApplyDamage(in DamageInfo info, out bool killed)
        {
            killed = false;
            if (!IsServer || !_avatar.IsAlive) return 0;

            DamageOutcome outcome = _model.ApplyDamage(info.amount, NetworkManager.ServerTime.Time);
            Publish();
            killed = outcome.killed;

            ServerDamaged?.Invoke(this, info, outcome);
            if (outcome.TotalApplied > 0)
                DamageTakenRpc(info.origin, outcome.TotalApplied);
            if (killed)
                _avatar.ServerOnKilled(info);
            return outcome.TotalApplied;
        }

        public int ServerHeal(int amount)
        {
            if (!IsServer) return 0;
            int healed = _model.Heal(amount);
            if (healed > 0) Publish();
            return healed;
        }

        public void ServerKill(in DamageInfo info)
        {
            if (!IsServer || !_model.Kill()) return;
            Publish();
            _avatar.ServerOnKilled(info);
        }

        void Update()
        {
            if (IsServer && _model != null && _model.Tick(NetworkManager.ServerTime.Time, Time.deltaTime))
                Publish();
        }

        [Rpc(SendTo.Owner)]
        void DamageTakenRpc(Vector3 sourcePosition, int amount)
        {
            LocalDamageTaken?.Invoke(sourcePosition, amount);
        }
    }
}
