using BlueHorizon.Core;
using UnityEngine;

namespace BlueHorizon.Combat
{
    public enum DamageSource : byte
    {
        Weapon,
        Explosion,
        World,
    }

    public struct DamageInfo
    {
        public ulong attackerClientId;
        public Team attackerTeam;
        public int amount;
        public HitZone zone;
        public DamageSource source;
        /// <summary>Weapon slot (0/1) for weapon damage, otherwise 255.</summary>
        public byte weaponSlot;
        public Vector3 origin;
        public Vector3 point;
    }

    /// <summary>Anything the server can damage: players, deployables.</summary>
    public interface IDamageable
    {
        Team DamageTeam { get; }
        bool CanTakeDamage { get; }
        /// <summary>Server only. Returns the damage actually applied.</summary>
        int ServerApplyDamage(in DamageInfo info, out bool killed);
    }
}
