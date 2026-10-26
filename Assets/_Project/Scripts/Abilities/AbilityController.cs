using System;
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

    /// <summary>
    /// Server-validated ability activation for one player. Activation is triggered by button edges inside the
    /// player's authoritative input stream; this component checks life state, match phase, charges and cooldowns,
    /// executes the ability definition and replicates the resulting slot state.
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
    }
}
