using System;

namespace BlueHorizon.Health
{
    /// <summary>
    /// Health and shield configuration for an agent.
    ///
    /// Damage absorption order (outermost first):
    ///   1. Barrier (shield layer 2) — regenerates after <see cref="barrierRegenDelay"/> seconds without damage.
    ///   2. Armor   (shield layer 1) — never regenerates during a round; restored on spawn.
    ///   3. Health.
    /// Each layer is depleted independently; overflow damage carries into the next layer in the same hit.
    /// Taking any damage interrupts and restarts the barrier regeneration delay.
    /// </summary>
    [Serializable]
    public sealed class HealthProfile
    {
        public int maxHealth = 125;
        public int maxArmor = 25;
        public int maxBarrier = 25;
        public float barrierRegenDelay = 4f;
        public float barrierRegenPerSecond = 10f;
    }
}
