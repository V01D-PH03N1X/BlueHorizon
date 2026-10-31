using BlueHorizon.Core;
using Unity.Netcode;
using UnityEngine;

namespace BlueHorizon.Abilities
{
    /// <summary>Utility: deployable destructible wall placed on the ground in front of the player.</summary>
    [CreateAssetMenu(menuName = "Blue Horizon/Abilities/Wayblocker", fileName = "Wayblocker")]
    public sealed class WayblockerDefinition : AbilityDefinition
    {
        public float placementRange = 6f;
        public Vector3 size = new(3.2f, 2.1f, 0.35f);
        public int maxHealth = 400;
        public float lifetime = 30f;
        [Tooltip("Steepest ground the wall can stand on, degrees.")]
        public float maxSlope = 25f;

        static readonly Collider[] Overlaps = new Collider[8];

        public override AbilityRejectReason Execute(in AbilityContext context)
        {
            GameContent content = GameContent.Instance;
            if (content == null || content.wayblockerPrefab == null) return AbilityRejectReason.Missing;

            if (!TryFindPlacement(context.EyePosition, context.AimDirection, out Vector3 center, out Quaternion rotation))
                return AbilityRejectReason.InvalidTarget;

            GameObject go = Instantiate(content.wayblockerPrefab, center, rotation);
            go.GetComponent<Wayblocker>().Configure(this, context.Owner.Team, context.Now);
            go.GetComponent<NetworkObject>().Spawn(destroyWithScene: true);
            BHLog.Info("Ability", $"{context.Owner.PlayerName} placed Wayblocker at {center}");
            return AbilityRejectReason.None;
        }

        /// <summary>
        /// Finds a ground point along the aim (or below the aim's end point), facing the player, with room for the wall.
        /// Rejects steep ground, positions beyond range and any overlap with players or world geometry.
        /// </summary>
        public bool TryFindPlacement(Vector3 eye, Vector3 aim, out Vector3 center, out Quaternion rotation)
        {
            center = default;
            rotation = Quaternion.identity;

            Vector3 flat = new Vector3(aim.x, 0f, aim.z);
            if (flat.sqrMagnitude < 1e-4f) return false;
            flat.Normalize();

            float minNormalY = Mathf.Cos(maxSlope * Mathf.Deg2Rad);
            Vector3 ground;
            if (Physics.Raycast(eye, aim, out RaycastHit hit, placementRange, GameLayers.WorldMask, QueryTriggerInteraction.Ignore)
                && hit.normal.y >= minNormalY)
            {
                ground = hit.point;
            }
            else
            {
                float reach = Physics.Raycast(eye, aim, out RaycastHit wall, placementRange, GameLayers.WorldMask, QueryTriggerInteraction.Ignore)
                    ? Mathf.Max(0f, wall.distance - size.z)
                    : placementRange;
                Vector3 probe = eye + aim * reach;
                if (!Physics.Raycast(probe + Vector3.up * 0.1f, Vector3.down, out RaycastHit down, 3f, GameLayers.WorldMask, QueryTriggerInteraction.Ignore)
                    || down.normal.y < minNormalY)
                    return false;
                ground = down.point;
            }

            Vector3 horizontalOffset = ground - eye;
            horizontalOffset.y = 0f;
            if (horizontalOffset.magnitude > placementRange) return false;

            rotation = Quaternion.LookRotation(flat, Vector3.up);
            center = ground + Vector3.up * (size.y * 0.5f);

            // Clearance check slightly above the ground so the floor itself does not count.
            Vector3 half = new Vector3(size.x * 0.5f - 0.02f, size.y * 0.5f - 0.06f, size.z * 0.5f - 0.02f);
            int n = Physics.OverlapBoxNonAlloc(center + Vector3.up * 0.05f, half, Overlaps, rotation,
                GameLayers.WorldMask | GameLayers.PlayerMask, QueryTriggerInteraction.Ignore);
            return n == 0;
        }
    }
}
