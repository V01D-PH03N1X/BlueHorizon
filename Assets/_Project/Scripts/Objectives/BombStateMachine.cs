using UnityEngine;

namespace BlueHorizon.Objectives
{
    public enum BombState : byte
    {
        /// <summary>No bomb in play (between rounds, warmup, or before assignment).</summary>
        Inactive = 0,
        Carried = 1,
        /// <summary>On the ground after the carrier died or disconnected; any living attacker can pick it up.</summary>
        Dropped = 2,
        Planting = 3,
        Planted = 4,
        Defusing = 5,
        Defused = 6,
        Exploded = 7,
    }

    public enum BombRejectReason : byte
    {
        None = 0,
        WrongState,
        NotCarrier,
        WrongSide,
        Dead,
        NotInSite,
        OutOfRange,
        Busy,
    }

    public enum BombEvent : byte
    {
        None = 0,
        Planted,
        Defused,
        Exploded,
    }

    /// <summary>
    /// Authoritative HeavyBomb objective state. Engine-agnostic so every transition is unit tested.
    /// Every method validates its preconditions and rejects (without side effects) when they fail.
    ///
    /// Timing rules:
    ///   * Planting completes after <see cref="PlantDuration"/> seconds of uninterrupted interaction.
    ///   * The fuse starts when planting completes; detonation at plant time + <see cref="FuseDuration"/>.
    ///   * Defusing completes after <see cref="DefuseDuration"/>; cancelling resets defuse progress.
    ///   * Tie-break: a defuse that completes at or before the detonation instant wins.
    /// </summary>
    public sealed class BombStateMachine
    {
        public float PlantDuration { get; set; }
        public float DefuseDuration { get; set; }
        public float FuseDuration { get; set; }

        public BombState State { get; private set; } = BombState.Inactive;
        /// <summary>Client id of the carrier (Carried/Planting) or defuser (Defusing).</summary>
        public ulong ActorId { get; private set; } = ulong.MaxValue;
        /// <summary>Carrier at the time the bomb was last held; used to resume Carried after a cancelled plant.</summary>
        public ulong CarrierId { get; private set; } = ulong.MaxValue;
        public int SiteIndex { get; private set; } = -1;
        public Vector3 Position { get; private set; }
        public double ActionStartTime { get; private set; }
        public double ActionEndTime { get; private set; }
        public double DetonationTime { get; private set; }

        public bool IsPlanted => State is BombState.Planted or BombState.Defusing;
        public bool IsResolved => State is BombState.Defused or BombState.Exploded;

        public BombStateMachine(float plantDuration, float defuseDuration, float fuseDuration)
        {
            PlantDuration = plantDuration;
            DefuseDuration = defuseDuration;
            FuseDuration = fuseDuration;
        }

        public void Deactivate()
        {
            State = BombState.Inactive;
            ActorId = ulong.MaxValue;
            CarrierId = ulong.MaxValue;
            SiteIndex = -1;
            ActionStartTime = ActionEndTime = DetonationTime = 0;
        }

        /// <summary>Gives the bomb to an attacker at round start.</summary>
        public void AssignTo(ulong carrierId)
        {
            Deactivate();
            State = BombState.Carried;
            ActorId = CarrierId = carrierId;
        }

        /// <summary>Places the bomb on the ground at round start (no attacker available yet).</summary>
        public void PlaceDropped(Vector3 position)
        {
            Deactivate();
            State = BombState.Dropped;
            Position = position;
        }

        public BombRejectReason TryStartPlant(ulong playerId, bool isAttacker, bool isAlive, int siteIndex, Vector3 position, double now)
        {
            if (State != BombState.Carried) return BombRejectReason.WrongState;
            if (playerId != CarrierId) return BombRejectReason.NotCarrier;
            if (!isAttacker) return BombRejectReason.WrongSide;
            if (!isAlive) return BombRejectReason.Dead;
            if (siteIndex < 0) return BombRejectReason.NotInSite;

            State = BombState.Planting;
            ActorId = playerId;
            SiteIndex = siteIndex;
            Position = position;
            ActionStartTime = now;
            ActionEndTime = now + PlantDuration;
            return BombRejectReason.None;
        }

        /// <summary>Interrupts planting (key released, moved off-site, died). The carrier keeps the bomb.</summary>
        public bool CancelPlant()
        {
            if (State != BombState.Planting) return false;
            State = BombState.Carried;
            ActorId = CarrierId;
            SiteIndex = -1;
            return true;
        }

        public BombRejectReason TryStartDefuse(ulong playerId, bool isDefender, bool isAlive, float distance, float maxDistance, double now)
        {
            if (State == BombState.Defusing) return BombRejectReason.Busy;
            if (State != BombState.Planted) return BombRejectReason.WrongState;
            if (!isDefender) return BombRejectReason.WrongSide;
            if (!isAlive) return BombRejectReason.Dead;
            if (distance > maxDistance) return BombRejectReason.OutOfRange;

            State = BombState.Defusing;
            ActorId = playerId;
            ActionStartTime = now;
            ActionEndTime = now + DefuseDuration;
            return BombRejectReason.None;
        }

        /// <summary>Interrupts defusing. Progress is lost; the fuse keeps running.</summary>
        public bool CancelDefuse()
        {
            if (State != BombState.Defusing) return false;
            State = BombState.Planted;
            ActorId = ulong.MaxValue;
            return true;
        }

        /// <summary>The carrier died or disconnected. Cancels an active plant first.</summary>
        public bool Drop(Vector3 position)
        {
            if (State == BombState.Planting) CancelPlant();
            if (State != BombState.Carried) return false;
            State = BombState.Dropped;
            ActorId = CarrierId = ulong.MaxValue;
            Position = position;
            return true;
        }

        public BombRejectReason TryPickUp(ulong playerId, bool isAttacker, bool isAlive)
        {
            if (State != BombState.Dropped) return BombRejectReason.WrongState;
            if (!isAttacker) return BombRejectReason.WrongSide;
            if (!isAlive) return BombRejectReason.Dead;
            State = BombState.Carried;
            ActorId = CarrierId = playerId;
            return BombRejectReason.None;
        }

        /// <summary>Advances timers. Returns the event that occurred this step, if any.</summary>
        public BombEvent Tick(double now)
        {
            switch (State)
            {
                case BombState.Planting when now >= ActionEndTime:
                    State = BombState.Planted;
                    DetonationTime = ActionEndTime + FuseDuration;
                    ActorId = ulong.MaxValue;
                    CarrierId = ulong.MaxValue;
                    return BombEvent.Planted;

                case BombState.Defusing when now >= ActionEndTime && ActionEndTime <= DetonationTime:
                    State = BombState.Defused;
                    return BombEvent.Defused;

                case BombState.Planted or BombState.Defusing when now >= DetonationTime:
                    State = BombState.Exploded;
                    ActorId = ulong.MaxValue;
                    return BombEvent.Exploded;
            }
            return BombEvent.None;
        }

        public float Progress(double now)
        {
            if (State is not (BombState.Planting or BombState.Defusing)) return 0f;
            double span = ActionEndTime - ActionStartTime;
            return span <= 0 ? 1f : Mathf.Clamp01((float)((now - ActionStartTime) / span));
        }
    }
}
