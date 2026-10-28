using System;

namespace BlueHorizon.Health
{
    public struct DamageOutcome
    {
        public int requested;
        public int absorbedByBarrier;
        public int absorbedByArmor;
        public int healthDamage;
        public bool killed;

        public int TotalApplied => absorbedByBarrier + absorbedByArmor + healthDamage;
    }

    /// <summary>
    /// Authoritative health/shield rules with no engine dependencies. The server owns one per player;
    /// PlayerHealth replicates its integer values. See <see cref="HealthProfile"/> for the layer rules.
    /// </summary>
    public sealed class HealthModel
    {
        readonly HealthProfile _profile;
        double _lastDamageTime = double.NegativeInfinity;
        float _barrierRegenRemainder;

        public int Health { get; private set; }
        public int Armor { get; private set; }
        public int Barrier { get; private set; }
        public bool IsAlive => Health > 0;
        public HealthProfile Profile => _profile;

        public HealthModel(HealthProfile profile)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            ResetToFull();
        }

        public void ResetToFull()
        {
            Health = _profile.maxHealth;
            Armor = _profile.maxArmor;
            Barrier = _profile.maxBarrier;
            _lastDamageTime = double.NegativeInfinity;
            _barrierRegenRemainder = 0f;
        }

        /// <summary>Applies damage through barrier → armor → health. Dead targets take no damage.</summary>
        public DamageOutcome ApplyDamage(int amount, double now)
        {
            var outcome = new DamageOutcome { requested = amount };
            if (amount <= 0 || !IsAlive)
                return outcome;

            int remaining = amount;

            int barrierHit = Math.Min(Barrier, remaining);
            Barrier -= barrierHit;
            remaining -= barrierHit;
            outcome.absorbedByBarrier = barrierHit;

            int armorHit = Math.Min(Armor, remaining);
            Armor -= armorHit;
            remaining -= armorHit;
            outcome.absorbedByArmor = armorHit;

            int healthHit = Math.Min(Health, remaining);
            Health -= healthHit;
            outcome.healthDamage = healthHit;
            outcome.killed = Health <= 0;

            _lastDamageTime = now;
            _barrierRegenRemainder = 0f;
            return outcome;
        }

        /// <summary>Restores health up to the maximum. Returns the amount actually healed. Dead targets cannot heal.</summary>
        public int Heal(int amount)
        {
            if (amount <= 0 || !IsAlive)
                return 0;
            int healed = Math.Min(amount, _profile.maxHealth - Health);
            Health += healed;
            return healed;
        }

        /// <summary>Advances barrier regeneration. Returns true when any value changed.</summary>
        public bool Tick(double now, float deltaTime)
        {
            if (!IsAlive || Barrier >= _profile.maxBarrier || deltaTime <= 0f)
                return false;
            if (now - _lastDamageTime < _profile.barrierRegenDelay)
                return false;

            _barrierRegenRemainder += _profile.barrierRegenPerSecond * deltaTime;
            int whole = (int)_barrierRegenRemainder;
            if (whole <= 0)
                return false;

            _barrierRegenRemainder -= whole;
            Barrier = Math.Min(_profile.maxBarrier, Barrier + whole);
            return true;
        }

        /// <summary>
        /// Brings a downed player (health 0) back with <paramref name="health"/> points. Armor stays as it was;
        /// the barrier starts its regeneration delay. Returns false if the player was not at 0 health.
        /// </summary>
        public bool Revive(int health, double now)
        {
            if (IsAlive || health <= 0) return false;
            Health = Math.Min(health, _profile.maxHealth);
            _lastDamageTime = now;
            _barrierRegenRemainder = 0f;
            return true;
        }

        /// <summary>Immediately kills (out-of-bounds, admin). Returns false if already dead.</summary>
        public bool Kill()
        {
            if (!IsAlive) return false;
            Health = 0;
            return true;
        }
    }
}
