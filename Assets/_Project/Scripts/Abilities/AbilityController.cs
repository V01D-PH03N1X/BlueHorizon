using System;
using System.Collections.Generic;
using BlueHorizon.Agents;
using BlueHorizon.Core;
using BlueHorizon.Match;
using BlueHorizon.Player;
using Unity.Netcode;
using UnityEngine;

namespace BlueHorizon.Abilities
{
    public readonly struct AbilityContext
    {
        public readonly PlayerAvatar Owner;
        public readonly Vector3 EyePosition;
        public readonly Vector3 AimDirection;
        public readonly double Now;

        public AbilityContext(PlayerAvatar owner, Vector3 eye, Vector3 aim, double now)
        {
            Owner = owner;
            EyePosition = eye;
            AimDirection = aim;
            Now = now;
        }
    }

    public struct AbilityNetState : INetworkSerializable, IEquatable<AbilityNetState>
    {
        public byte charges;
        public double cooldownEnd;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref charges);
            serializer.SerializeValue(ref cooldownEnd);
        }

        public bool Equals(AbilityNetState other) => charges == other.charges && cooldownEnd.Equals(other.cooldownEnd);
    }

    /// <summary>Visual-only burst effects other clients see (flash light, scan pulse ring).</summary>
    public enum AbilityBurst : byte
    {
        Flash = 0,
        Pulse = 1,
    }

    /// <summary>
    /// Server-validated ability activation for one player. Activation is triggered by button edges inside the
    /// player's authoritative input stream; this component checks life state, match phase, charges and cooldowns,
    /// executes the ability definition and replicates the resulting slot state. It also delivers the status effects
    /// abilities put on this player: blindness from flashes (owner only) and EnemyAlert marks (to the observers).
    /// </summary>
    public sealed class AbilityController : NetworkBehaviour
    {
        const float MinAttemptInterval = 0.2f;

        // NGO discovers NetworkVariables only as individual fields, not array elements.
        readonly NetworkVariable<AbilityNetState> _utility1 = new();
        readonly NetworkVariable<AbilityNetState> _utility2 = new();
        readonly NetworkVariable<AbilityNetState> _signature = new();
        NetworkVariable<AbilityNetState>[] _slots;

        readonly double[] _lastAttempt = new double[3];
        PlayerAvatar _avatar;

        /// <summary>All peers: an ability was used (slot). For audio/visual feedback.</summary>
        public event Action<AbilitySlot> Activated;
        /// <summary>Owner: the server refused an activation.</summary>
        public event Action<AbilitySlot, AbilityRejectReason> Rejected;
        /// <summary>All peers: a flash burst or scan pulse at a position (visual/audio only).</summary>
        public static event Action<AbilityBurst, Vector3> Burst;
        /// <summary>Owner: this player was spotted by an EnemyAlert pulse (seconds).</summary>
        public event Action<float> Spotted;

        /// <summary>Owner: current flash blindness (presentation and test bots; the server does not enforce it).</summary>
        public float FlashIntensity { get; private set; }
        public float FlashDuration { get; private set; }
        public float FlashStartTime { get; private set; } = -100f;
        public float FlashElapsed => Time.time - FlashStartTime;
        public bool IsBlinded => FlashElapsed < FlashDuration * 0.7f;
        /// <summary>Observers: <see cref="Time.time"/> until which this (enemy) player is marked by EnemyAlert.</summary>
        public float RevealedUntil { get; private set; } = -100f;
        public bool IsRevealed => Time.time < RevealedUntil;

        void Awake()
        {
            _avatar = GetComponent<PlayerAvatar>();
            _slots = new[] { _utility1, _utility2, _signature };
        }

        AgentDefinition Agent => _avatar.Agent;

        public AbilityDefinition Definition(AbilitySlot slot) => Agent != null ? Agent.GetAbility(slot) : null;

        public AbilityNetState State(AbilitySlot slot) => _slots[(int)slot].Value;

        public float CooldownRemaining(AbilitySlot slot)
        {
            if (NetworkManager == null) return 0f;
            return Mathf.Max(0f, (float)(_slots[(int)slot].Value.cooldownEnd - NetworkManager.ServerTime.Time));
        }

        public void ServerResetForRound()
        {
            if (!IsServer) return;
            for (int i = 0; i < _slots.Length; i++)
            {
                AbilitySlotState fresh = AbilityRules.Fresh(Definition((AbilitySlot)i));
                _slots[i].Value = new AbilityNetState { charges = fresh.charges, cooldownEnd = 0 };
            }
        }

        public void ServerTryActivate(AbilitySlot slot, Vector3 eye, Vector3 aim)
        {
            if (!IsServer) return;
            double now = NetworkManager.ServerTime.Time;
            int index = (int)slot;

            // Cheap rate limit before any physics queries a placement might run.
            if (now - _lastAttempt[index] < MinAttemptInterval) return;
            _lastAttempt[index] = now;

            AbilityDefinition definition = Definition(slot);
            AbilityNetState net = _slots[index].Value;
            var state = new AbilitySlotState { charges = net.charges, cooldownEndTime = net.cooldownEnd };

            MatchManager match = MatchManager.Instance;
            bool phaseAllows = match == null || match.AbilitiesAllowed;
            AbilityRejectReason reason = AbilityRules.CanActivate(definition, state, now, _avatar.IsAlive, phaseAllows);
            if (reason == AbilityRejectReason.None && _avatar.Health.IsDowned) reason = AbilityRejectReason.Downed;
            if (reason == AbilityRejectReason.None)
                reason = definition.Execute(new AbilityContext(_avatar, eye, aim, now));

            if (reason != AbilityRejectReason.None)
            {
                RejectedRpc(slot, reason);
                return;
            }

            AbilityRules.Consume(definition, ref state, now);
            _slots[index].Value = new AbilityNetState { charges = state.charges, cooldownEnd = state.cooldownEndTime };
            BHLog.Info("Ability", $"{_avatar.PlayerName} used {definition.displayName} (charges {state.charges}, cooldown {definition.cooldown:0.#}s)");
            ActivatedRpc(slot);
        }

        [Rpc(SendTo.Owner)]
        void RejectedRpc(AbilitySlot slot, AbilityRejectReason reason) => Rejected?.Invoke(slot, reason);

        [Rpc(SendTo.ClientsAndHost)]
        void ActivatedRpc(AbilitySlot slot) => Activated?.Invoke(slot);

        // ───────────── Status effects ─────────────

        /// <summary>Server: blind this player (sent to its owner only).</summary>
        public void ServerFlash(float intensity, float duration)
        {
            if (IsServer) FlashedRpc(intensity, duration);
        }

        /// <summary>Server: mark this player for the given observers (EnemyAlert) and warn its owner.</summary>
        public void ServerReveal(List<ulong> observers, float duration)
        {
            if (!IsServer) return;
            if (observers.Count > 0) RevealedRpc(duration, RpcTarget.Group(observers, RpcTargetUse.Temp));
            SpottedRpc(duration);
        }

        public void ServerBurst(AbilityBurst kind, Vector3 position)
        {
            if (IsServer) BurstRpc(kind, position);
        }

        [Rpc(SendTo.Owner)]
        void FlashedRpc(float intensity, float duration)
        {
            // A stronger flash replaces a weaker one that is still fading.
            float remaining = FlashRules.Whiteness(FlashIntensity, FlashDuration, FlashElapsed);
            if (remaining > FlashRules.Whiteness(intensity, duration, 0f)) return;
            FlashIntensity = intensity;
            FlashDuration = duration;
            FlashStartTime = Time.time;
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void RevealedRpc(float duration, RpcParams rpcParams = default) => RevealedUntil = Mathf.Max(RevealedUntil, Time.time + duration);

        [Rpc(SendTo.Owner)]
        void SpottedRpc(float duration) => Spotted?.Invoke(duration);

        [Rpc(SendTo.ClientsAndHost)]
        void BurstRpc(AbilityBurst kind, Vector3 position) => Burst?.Invoke(kind, position);
    }
}
