using System;
using BlueHorizon.Weapons;
using UnityEngine;

namespace BlueHorizon.Combat
{
    public enum HitZone : byte
    {
        Torso = 0,
        Head = 1,
        Legs = 2,
    }

    /// <summary>
    /// Pure damage calculation, shared by the server and tests.
    ///
    /// Stages (in order):
    ///   1. Zone base damage from the weapon definition (head / torso / legs).
    ///   2. × attachment damage multiplier (e.g. suppressor 0.8).
    ///   3. × range multiplier (falloff curve; 1.0 for the vertical-slice weapons).
    ///   4. Round half up to an integer, minimum 1 for any hit.
    /// Shields and health are applied afterwards by HealthModel.
    /// </summary>
    public static class DamageResolver
    {
        public static int BaseZoneDamage(WeaponDefinition weapon, HitZone zone) => zone switch
        {
            HitZone.Head => weapon.damage.head,
            HitZone.Legs => weapon.damage.legs,
            _ => weapon.damage.torso,
        };

        public static float RangeMultiplier(in RangeFalloff falloff, float distance)
        {
            if (falloff.minMultiplier >= 1f || distance <= falloff.falloffStart)
                return 1f;
            if (distance >= falloff.falloffEnd || falloff.falloffEnd <= falloff.falloffStart)
                return Mathf.Clamp01(falloff.minMultiplier);
            float t = (distance - falloff.falloffStart) / (falloff.falloffEnd - falloff.falloffStart);
            return Mathf.Lerp(1f, falloff.minMultiplier, t);
        }

        public static int Compute(in WeaponStats stats, HitZone zone, float distance)
        {
            WeaponDefinition weapon = stats.Definition;
            float damage = BaseZoneDamage(weapon, zone);
            damage *= stats.DamageMultiplier;
            damage *= RangeMultiplier(weapon.falloff, distance);
            int rounded = (int)Math.Floor(damage + 0.5f);
            return Math.Max(1, rounded);
        }
    }
}
