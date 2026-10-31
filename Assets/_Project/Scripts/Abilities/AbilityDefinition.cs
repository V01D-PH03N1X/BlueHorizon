using UnityEngine;

namespace BlueHorizon.Abilities
{
    public enum AbilitySlot : byte
    {
        Utility1 = 0,
        Utility2 = 1,
        Signature = 2,
    }

    public enum AbilityRejectReason : byte
    {
        None = 0,
        Dead,
        NotAllowedNow,
        NoCharges,
        OnCooldown,
        InvalidTarget,
        AlreadyFull,
        Missing,
        Downed,
        NoTarget,
    }

    /// <summary>
    /// Base for every tactical ability. Concrete abilities override <see cref="Execute"/> (server only).
    /// Charges and cooldowns are generic and handled by <see cref="AbilityRules"/>:
    ///   * maxCharges &gt; 0 — limited uses per round, restored at round start.
    ///   * maxCharges == 0 — unlimited uses gated only by the cooldown (signature abilities).
    /// New abilities (Flash Grenade, EnemyAlert, Revival in Milestone 5) add a subclass and an asset; no controller changes.
    /// </summary>
    public abstract class AbilityDefinition : ScriptableObject
    {
        public string abilityId = "ability";
        public string displayName = "Ability";
        [TextArea] public string description = "";
        [Tooltip("Seconds after use before the ability can be used again.")]
        public float cooldown = 1f;
        public int maxCharges = 1;
        public Color hudColor = new(0.35f, 0.75f, 1f);

        /// <summary>Server-side effect. Return a reject reason to refuse without consuming a charge.</summary>
        public abstract AbilityRejectReason Execute(in AbilityContext context);
    }

    public struct AbilitySlotState
    {
        public byte charges;
        public double cooldownEndTime;
    }

    public static class AbilityRules
    {
        public static AbilitySlotState Fresh(AbilityDefinition definition) => new()
        {
            charges = (byte)Mathf.Clamp(definition != null ? definition.maxCharges : 0, 0, 255),
            cooldownEndTime = 0,
        };

        public static AbilityRejectReason CanActivate(AbilityDefinition definition, in AbilitySlotState state, double now, bool isAlive, bool phaseAllowsAbilities)
        {
            if (definition == null) return AbilityRejectReason.Missing;
            if (!isAlive) return AbilityRejectReason.Dead;
            if (!phaseAllowsAbilities) return AbilityRejectReason.NotAllowedNow;
            if (definition.maxCharges > 0 && state.charges == 0) return AbilityRejectReason.NoCharges;
            if (now < state.cooldownEndTime) return AbilityRejectReason.OnCooldown;
            return AbilityRejectReason.None;
        }

        public static void Consume(AbilityDefinition definition, ref AbilitySlotState state, double now)
        {
            if (definition.maxCharges > 0 && state.charges > 0)
                state.charges--;
            state.cooldownEndTime = now + definition.cooldown;
        }

        public static string Describe(AbilityRejectReason reason) => reason switch
        {
            AbilityRejectReason.Dead => "You are eliminated",
            AbilityRejectReason.NotAllowedNow => "Not available right now",
            AbilityRejectReason.NoCharges => "No charges left",
            AbilityRejectReason.OnCooldown => "On cooldown",
            AbilityRejectReason.InvalidTarget => "Invalid placement",
            AbilityRejectReason.AlreadyFull => "Already at full health",
            AbilityRejectReason.Missing => "No ability equipped",
            AbilityRejectReason.Downed => "You are downed",
            AbilityRejectReason.NoTarget => "No downed teammate in range",
            _ => "",
        };
    }
}
