using System.Collections.Generic;
using System;
using BlueHorizon.Core;
using BlueHorizon.Player;
using BlueHorizon.Weapons;
using UnityEngine;

namespace BlueHorizon.Combat
{
    /// <summary>
    /// Server-side hitscan resolution. The shot origin is the shooter's authoritative eye position and the
    /// direction comes from the server's own weapon simulation of the client's view input — clients never
    /// report targets, hit zones or damage.
    ///
    /// Lag compensation: before the raycast every other character's hitboxes are moved to where they were at the
    /// shooter's (clamped) view time, then restored. World geometry and deployables are not rewound.
    /// </summary>
    public static class CombatSystem
    {
        static readonly RaycastHit[] Hits = new RaycastHit[16];

        public static bool FriendlyFire { get; set; }
        public static bool LagCompensationEnabled { get; set; } = true;

        // Diagnostics (server): cumulative shots, character hits and rewind amount.
        public static int ShotsResolved { get; private set; }
        public static int CharacterHits { get; private set; }
        public static double RewindSecondsTotal { get; private set; }

        /// <summary>Server: raised for every resolved shot (shooter, target or null, damage, killed).</summary>
        public static event Action<PlayerAvatar, IDamageable, int, bool> ServerShotResolved;

        struct PelletHit
        {
            public IDamageable target;
            public HitboxCollider hitbox;
            public float distance;
            public Vector3 point;
        }

        static readonly List<PelletHit> PelletHits = new(32);
        static readonly List<IDamageable> Targets = new(8);

        public static void ServerResolveShot(PlayerAvatar shooter, Vector3 origin, in ShotEvent shot, double viewTime, double now)
        {
            WeaponStats stats = shooter.StatsForSlot(shot.slot);
            WeaponDefinition weapon = stats.Definition;
            if (weapon == null) return;

            // One rewind for all pellets of the shot.
            bool rewind = LagCompensationEnabled && viewTime < now;
            if (rewind) RewindOthers(shooter, viewTime);
            else Physics.SyncTransforms();

            PelletHits.Clear();
            Vector3 firstEnd = origin + shot.direction * weapon.maxRange;
            int pellets = Mathf.Max(1, shot.pellets);
            for (int p = 0; p < pellets; p++)
            {
                Vector3 dir = WeaponSimulation.PelletDirection(shot, p);
                Vector3 end = TracePellet(shooter, origin, dir, weapon.maxRange, out PelletHit hit);
                if (p == 0) firstEnd = end;
                if (hit.target != null) PelletHits.Add(hit);
            }
            if (rewind) RestoreOthers(shooter);
            ShotsResolved++;
            RewindSecondsTotal += now - viewTime;

            // Aggregate pellets per target: one damage event per target per shot.
            Targets.Clear();
            foreach (PelletHit h in PelletHits)
                if (!Targets.Contains(h.target)) Targets.Add(h.target);

            IDamageable lastTarget = null;
            int lastApplied = 0;
            bool lastKilled = false, hitCharacter = false;
            foreach (IDamageable target in Targets)
            {
                int total = 0;
                HitZone zone = HitZone.Legs;
                Vector3 point = default;
                foreach (PelletHit h in PelletHits)
                {
                    if (h.target != target) continue;
                    total += DamageResolver.Compute(stats, h.hitbox.zone, h.distance);
                    if (ZonePriority(h.hitbox.zone) >= ZonePriority(zone)) { zone = h.hitbox.zone; point = h.point; }
                }
                if (!target.CanTakeDamage) continue;

                bool character = target is Health.PlayerHealth;
                var info = new DamageInfo
                {
                    attackerClientId = shooter.OwnerClientId,
                    attackerTeam = shooter.Team,
                    amount = total,
                    zone = zone,
                    source = DamageSource.Weapon,
                    weaponSlot = shot.slot,
                    origin = origin,
                    point = point,
                };
                int applied = target.ServerApplyDamage(info, out bool killed);
                if (applied > 0) shooter.HitConfirmRpc(zone, applied, killed, !character);
                hitCharacter |= character;
                lastTarget = target;
                lastApplied = applied;
                lastKilled = killed;
            }

            if (hitCharacter)
            {
                CharacterHits++;
                shooter.ServerCharacterHits++;
            }
            shooter.ServerShotsFired++;
            shooter.Simulation.ServerBroadcastShot(origin, firstEnd, shot.slot, stats.Suppressed, hitCharacter);
            ServerShotResolved?.Invoke(shooter, lastTarget, lastApplied, lastKilled);
        }

        static int ZonePriority(HitZone zone) => zone switch { HitZone.Head => 2, HitZone.Torso => 1, _ => 0 };

        /// <summary>Returns where the pellet stopped; <paramref name="hit"/> holds the damageable it hit, if any.</summary>
        static Vector3 TracePellet(PlayerAvatar shooter, Vector3 origin, Vector3 direction, float range, out PelletHit hit)
        {
            hit = default;
            int count = Physics.RaycastNonAlloc(origin, direction, Hits, range, GameLayers.ShotMask, QueryTriggerInteraction.Collide);
            Array.Sort(Hits, 0, count, Comparer.Instance);
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = Hits[i];
                var hitbox = h.collider.GetComponent<HitboxCollider>();
                if (hitbox == null)
                {
                    if (h.collider.isTrigger) continue; // Non-hitbox triggers never stop bullets.
                    return h.point;
                }

                IDamageable candidate = hitbox.Target;
                if (candidate == null) continue;
                if (ReferenceEquals(candidate, shooter.Health)) continue; // Own hitboxes.
                if (!candidate.CanTakeDamage) continue;
                if (!FriendlyFire && candidate.DamageTeam == shooter.Team && candidate.DamageTeam != Team.None)
                    return h.point; // Teammates stop the bullet but take no damage.

                hit = new PelletHit { target = candidate, hitbox = hitbox, distance = h.distance, point = h.point };
                return h.point;
            }
            return origin + direction * range;
        }

        static void RewindOthers(PlayerAvatar shooter, double viewTime)
        {
            foreach (PlayerAvatar other in PlayerAvatar.All)
            {
                if (other == shooter || !other.IsAlive) continue;
                other.Simulation.ServerApplyHistoricalHitboxes(viewTime);
            }
            Physics.SyncTransforms();
        }

        static void RestoreOthers(PlayerAvatar shooter)
        {
            foreach (PlayerAvatar other in PlayerAvatar.All)
            {
                if (other == shooter) continue;
                other.Simulation.ServerRestoreHitboxes();
            }
            Physics.SyncTransforms();
        }

        sealed class Comparer : System.Collections.Generic.IComparer<RaycastHit>
        {
            public static readonly Comparer Instance = new();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }
    }
}
