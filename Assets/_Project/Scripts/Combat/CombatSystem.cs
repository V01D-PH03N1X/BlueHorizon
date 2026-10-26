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
    /// Known limitation (documented in TECHNICAL_ARCHITECTURE.md): there is no lag compensation yet; targets are
    /// tested at their current server positions, so high-latency shooters must lead moving targets.
    /// </summary>
    public static class CombatSystem
    {
        static readonly RaycastHit[] Hits = new RaycastHit[16];

        public static bool FriendlyFire { get; set; }

        /// <summary>Server: raised for every resolved shot (shooter, target or null, damage, killed).</summary>
        public static event Action<PlayerAvatar, IDamageable, int, bool> ServerShotResolved;

        public static void ServerResolveShot(PlayerAvatar shooter, Vector3 origin, in ShotEvent shot)
        {
            WeaponStats stats = shooter.StatsForSlot(shot.slot);
            WeaponDefinition weapon = stats.Definition;
            if (weapon == null) return;

            Physics.SyncTransforms();
            int count = Physics.RaycastNonAlloc(origin, shot.direction, Hits, weapon.maxRange, GameLayers.ShotMask, QueryTriggerInteraction.Collide);
            Array.Sort(Hits, 0, count, Comparer.Instance);

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

            shooter.Simulation.ServerBroadcastShot(origin, end, shot.slot, stats.Suppressed, hitCharacter);
            ServerShotResolved?.Invoke(shooter, target, applied, killed);
        }

        sealed class Comparer : System.Collections.Generic.IComparer<RaycastHit>
        {
            public static readonly Comparer Instance = new();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }
    }
}
