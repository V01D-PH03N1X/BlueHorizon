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

        public static void ServerResolveShot(PlayerAvatar shooter, Vector3 origin, in ShotEvent shot, double viewTime, double now)
        {
            WeaponStats stats = shooter.StatsForSlot(shot.slot);
            WeaponDefinition weapon = stats.Definition;
            if (weapon == null) return;

            bool rewind = LagCompensationEnabled && viewTime < now;
            if (rewind) RewindOthers(shooter, viewTime);
            else Physics.SyncTransforms();

            int count = Physics.RaycastNonAlloc(origin, shot.direction, Hits, weapon.maxRange, GameLayers.ShotMask, QueryTriggerInteraction.Collide);
            if (rewind) RestoreOthers(shooter);
            Array.Sort(Hits, 0, count, Comparer.Instance);
            ShotsResolved++;
            RewindSecondsTotal += now - viewTime;

            Vector3 end = origin + shot.direction * weapon.maxRange;
            IDamageable target = null;
            int applied = 0;
            bool killed = false;
            bool hitCharacter = false;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = Hits[i];
                var hitbox = hit.collider.GetComponent<HitboxCollider>();
                if (hitbox == null)
                {
                    if (hit.collider.isTrigger) continue; // Non-hitbox triggers never stop bullets.
                    end = hit.point;
                    break;
                }

                IDamageable candidate = hitbox.Target;
                if (candidate == null) continue;
                if (ReferenceEquals(candidate, shooter.Health)) continue; // Own hitboxes.
                if (!candidate.CanTakeDamage) continue;

                end = hit.point;
                if (!FriendlyFire && candidate.DamageTeam == shooter.Team && candidate.DamageTeam != Team.None)
                    break; // Teammates stop the bullet but take no damage.

                target = candidate;
                hitCharacter = candidate is Health.PlayerHealth;
                int damage = DamageResolver.Compute(stats, hitbox.zone, hit.distance);
                var info = new DamageInfo
                {
                    attackerClientId = shooter.OwnerClientId,
                    attackerTeam = shooter.Team,
                    amount = damage,
                    zone = hitbox.zone,
                    source = DamageSource.Weapon,
                    weaponSlot = shot.slot,
                    origin = origin,
                    point = hit.point,
                };
                applied = candidate.ServerApplyDamage(info, out killed);
                if (applied > 0)
                    shooter.HitConfirmRpc(hitbox.zone, applied, killed, !hitCharacter);
                break;
            }

            if (hitCharacter)
            {
                CharacterHits++;
                shooter.ServerCharacterHits++;
            }
            shooter.ServerShotsFired++;
            shooter.Simulation.ServerBroadcastShot(origin, end, shot.slot, stats.Suppressed, hitCharacter);
            ServerShotResolved?.Invoke(shooter, target, applied, killed);
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
