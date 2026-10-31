using BlueHorizon.Core;
using Unity.Netcode;
using UnityEngine;

namespace BlueHorizon.Abilities
{
    /// <summary>Signature: instant self-heal, capped at max health. Refused at full health so the cooldown is not wasted.</summary>
    [CreateAssetMenu(menuName = "Blue Horizon/Abilities/Heal", fileName = "Heal")]
    public sealed class HealAbilityDefinition : AbilityDefinition
    {
        public int healAmount = 50;

        public override AbilityRejectReason Execute(in AbilityContext context)
        {
            int healed = context.Owner.Health.ServerHeal(healAmount);
            if (healed <= 0) return AbilityRejectReason.AlreadyFull;
            BHLog.Info("Ability", $"{context.Owner.PlayerName} healed {healed}");
            return AbilityRejectReason.None;
        }
    }
}
