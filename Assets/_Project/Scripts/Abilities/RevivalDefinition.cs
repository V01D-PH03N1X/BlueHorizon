using BlueHorizon.Core;
using BlueHorizon.Player;
using UnityEngine;

namespace BlueHorizon.Abilities
{
    /// <summary>
    /// Signature: instantly revive a downed teammate in sight within range (the one closest to the aim
    /// direction). Refused without a valid target so the cooldown is not wasted. Any teammate can also revive by
    /// holding the interact key next to a downed player; this ability is the fast, ranged version.
    /// </summary>
    [CreateAssetMenu(menuName = "Blue Horizon/Abilities/Revival", fileName = "Revival")]
    public sealed class RevivalDefinition : AbilityDefinition
    {
        public float range = 9f;
        public int reviveHealth = 75;

        public override AbilityRejectReason Execute(in AbilityContext context)
        {
            PlayerAvatar owner = context.Owner;
            PlayerAvatar best = null;
            float bestAngle = float.MaxValue;
            foreach (PlayerAvatar a in PlayerAvatar.All)
            {
                if (a == owner || !a.IsSpawned || !a.IsAlive || !a.Health.IsDowned || a.Team != owner.Team || a.Team == Team.None) continue;
                Vector3 target = a.Simulation.Position + Vector3.up * 0.5f;
                Vector3 to = target - context.EyePosition;
                if (to.magnitude > range) continue;
                if (Physics.Linecast(context.EyePosition, target, GameLayers.WorldMask, QueryTriggerInteraction.Ignore)) continue;
                float angle = Vector3.Angle(context.AimDirection, to);
                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    best = a;
                }
            }
            if (best == null) return AbilityRejectReason.NoTarget;
            best.Health.ServerRevive(reviveHealth, owner, viaAbility: true);
            return AbilityRejectReason.None;
        }
    }
}
