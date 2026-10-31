using System.Collections.Generic;
using BlueHorizon.Core;
using BlueHorizon.Match;
using BlueHorizon.Player;
using UnityEngine;

namespace BlueHorizon.Abilities
{
    /// <summary>
    /// Scan pulse: every living enemy within the radius is marked for the user's team (only the user in
    /// free-for-all) for a few seconds, through walls. Marked players are told they were spotted.
    /// </summary>
    [CreateAssetMenu(menuName = "Blue Horizon/Abilities/Enemy Alert", fileName = "EnemyAlert")]
    public sealed class EnemyAlertDefinition : AbilityDefinition
    {
        public float radius = 30f;
        public float revealDuration = 4f;

        static readonly List<ulong> Observers = new();

        public override AbilityRejectReason Execute(in AbilityContext context)
        {
            PlayerAvatar owner = context.Owner;
            MatchManager match = MatchManager.Instance;
            bool freeForAll = match != null && match.FreeForAll;

            Observers.Clear();
            foreach (PlayerAvatar a in PlayerAvatar.All)
            {
                if (!a.IsSpawned) continue;
                if (a == owner || (!freeForAll && a.Team == owner.Team && a.Team != Team.None)) Observers.Add(a.OwnerClientId);
            }

            int revealed = 0;
            Vector3 origin = owner.Simulation.Position;
            foreach (PlayerAvatar enemy in PlayerAvatar.All)
            {
                if (!enemy.IsSpawned || !enemy.IsAlive || !GameModes.TeamRules.AreEnemies(owner.Team, enemy.Team, freeForAll) || enemy == owner) continue;
                if (!AlertRules.InRange(origin, enemy.Simulation.Position, radius)) continue;
                enemy.Abilities.ServerReveal(Observers, revealDuration);
                revealed++;
            }
            owner.Abilities.ServerBurst(AbilityBurst.Pulse, origin);
            BHLog.Info("Ability", $"EnemyAlert by {owner.PlayerName} revealed {revealed} enemy(s) within {radius:0} m for {revealDuration:0.#}s");
            return AbilityRejectReason.None;
        }
    }

    public static class AlertRules
    {
        /// <summary>Spherical range test; the pulse ignores walls.</summary>
        public static bool InRange(Vector3 origin, Vector3 target, float radius) => (target - origin).sqrMagnitude <= radius * radius;
    }
}
