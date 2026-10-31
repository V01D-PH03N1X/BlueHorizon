using System;
using BlueHorizon.Combat;
using Unity.Netcode;
using UnityEngine;

namespace BlueHorizon.Health
{
    /// <summary>Replicated downed state of a player. Times are server times; clients derive progress bars from them.</summary>
    public struct DownedNetState : INetworkSerializable, IEquatable<DownedNetState>
    {
        public bool downed;
        public double downedAt;
        public double bleedoutEnd;
        /// <summary>Client id of the teammate currently reviving, or <see cref="NoReviver"/>.</summary>
        public ulong reviverId;
        public double reviveStart;
        public double reviveEnd;

        public const ulong NoReviver = ulong.MaxValue;

        public bool BeingRevived => downed && reviverId != NoReviver;

        public float BleedoutRemaining(double now) => downed ? Mathf.Max(0f, (float)(bleedoutEnd - now)) : 0f;

        public float ReviveProgress(double now) =>
            BeingRevived ? Mathf.Clamp01((float)((now - reviveStart) / Math.Max(0.01, reviveEnd - reviveStart))) : 0f;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref downed);
            serializer.SerializeValue(ref downedAt);
            serializer.SerializeValue(ref bleedoutEnd);
            serializer.SerializeValue(ref reviverId);
            serializer.SerializeValue(ref reviveStart);
            serializer.SerializeValue(ref reviveEnd);
        }

        public bool Equals(DownedNetState o) =>
            downed == o.downed && downedAt.Equals(o.downedAt) && bleedoutEnd.Equals(o.bleedoutEnd) && reviverId == o.reviverId
            && reviveStart.Equals(o.reviveStart) && reviveEnd.Equals(o.reviveEnd);
    }

    /// <summary>
    /// Rules of the downed state (Revival). Pure so they can be unit-tested.
    ///   * Only lethal <b>weapon</b> damage downs a player, only in modes that enable it and only while a teammate is
    ///     still standing; explosions and world damage kill outright.
    ///   * A downed player bleeds out after the mode's bleedout time. Damage shortens it: the mode's downed health
    ///     worth of damage removes the full bleedout time.
    ///   * The kill is credited to whoever downed the player when they bleed out or are finished.
    /// </summary>
    public static class DownedRules
    {
        public static bool ShouldGoDown(bool modeAllows, DamageSource source, bool teammateStanding) =>
            modeAllows && teammateStanding && source == DamageSource.Weapon;

        /// <summary>New bleedout end after <paramref name="amount"/> damage to a downed player.</summary>
        public static double ApplyDamage(double bleedoutEnd, int amount, float bleedoutTime, int downedHealth)
        {
            if (amount <= 0) return bleedoutEnd;
            double secondsPerPoint = bleedoutTime / Math.Max(1, downedHealth);
            return bleedoutEnd - amount * secondsPerPoint;
        }

        public static bool CanRevive(bool reviverStanding, bool sameTeam, float distance, float range, bool holdingInteract) =>
            reviverStanding && sameTeam && holdingInteract && distance <= range;
    }
}
