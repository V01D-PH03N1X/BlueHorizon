using BlueHorizon.Core;
using Unity.Netcode;
using UnityEngine;

namespace BlueHorizon.Abilities
{
    /// <summary>Utility: thrown grenade that bursts into an opaque smoke volume after a fuse.</summary>
    [CreateAssetMenu(menuName = "Blue Horizon/Abilities/Smoke Grenade", fileName = "SmokeGrenade")]
    public sealed class SmokeGrenadeDefinition : AbilityDefinition
    {
        public float throwSpeed = 17f;
        public float upwardSpeed = 2.5f;
        public float fuseTime = 1.6f;
        public float smokeDuration = 14f;
        public float smokeRadius = 4.5f;
        public float smokeGrowTime = 0.8f;

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
            grenade.Configure(this, start, velocity, context.Now, context.Owner.Team);
            go.GetComponent<NetworkObject>().Spawn(destroyWithScene: true);
            return AbilityRejectReason.None;
        }
    }
}
