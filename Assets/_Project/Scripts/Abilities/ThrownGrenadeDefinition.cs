using BlueHorizon.Core;
using BlueHorizon.Player;
using Unity.Netcode;
using UnityEngine;

namespace BlueHorizon.Abilities
{
    /// <summary>Visual style of a grenade in flight (replicated with the launch).</summary>
    public enum GrenadeKind : byte
    {
        Smoke = 0,
        Flash = 1,
    }

    /// <summary>
    /// Base for thrown utilities: a validated server-side throw spawns a <see cref="SmokeGrenadeProjectile"/>
    /// (the shared ballistic projectile) that calls <see cref="Detonate"/> on the server when its fuse ends.
    /// </summary>
    public abstract class ThrownGrenadeDefinition : AbilityDefinition
    {
        public float throwSpeed = 17f;
        public float upwardSpeed = 2.5f;
        public float fuseTime = 1.6f;

        public abstract GrenadeKind Kind { get; }

        public override AbilityRejectReason Execute(in AbilityContext context)
        {
            GameContent content = GameContent.Instance;
            if (content == null || content.smokeGrenadePrefab == null) return AbilityRejectReason.Missing;

            // Start slightly in front of the eye but never inside a wall.
            Vector3 dir = context.AimDirection.normalized;
            Vector3 start = context.EyePosition + dir * 0.5f;
            if (Physics.Raycast(context.EyePosition, dir, out RaycastHit hit, 0.6f, GameLayers.WorldMask, QueryTriggerInteraction.Ignore))
                start = hit.point - dir * 0.15f;

            Vector3 velocity = dir * throwSpeed + Vector3.up * upwardSpeed + context.Owner.Simulation.Velocity * 0.5f;
            GameObject go = Instantiate(content.smokeGrenadePrefab, start, Quaternion.identity);
            var grenade = go.GetComponent<SmokeGrenadeProjectile>();
            grenade.Configure(this, start, velocity, context.Now, context.Owner);
            go.GetComponent<NetworkObject>().Spawn(destroyWithScene: true);
            return AbilityRejectReason.None;
        }

        /// <summary>Server: the fuse has ended at <paramref name="position"/>. The thrower may have left (null).</summary>
        public abstract void Detonate(Vector3 position, PlayerAvatar thrower, Team throwerTeam, double now);
    }
}
