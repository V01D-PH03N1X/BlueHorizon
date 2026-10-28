using BlueHorizon.Core;
using BlueHorizon.Player;
using Unity.Netcode;
using UnityEngine;

namespace BlueHorizon.Abilities
{
    /// <summary>Utility: thrown grenade that bursts into an opaque smoke volume after a fuse.</summary>
    [CreateAssetMenu(menuName = "Blue Horizon/Abilities/Smoke Grenade", fileName = "SmokeGrenade")]
    public sealed class SmokeGrenadeDefinition : ThrownGrenadeDefinition
    {
        public float smokeDuration = 14f;
        public float smokeRadius = 4.5f;
        public float smokeGrowTime = 0.8f;

        public override GrenadeKind Kind => GrenadeKind.Smoke;

        public override void Detonate(Vector3 position, PlayerAvatar thrower, Team throwerTeam, double now)
        {
            GameContent content = GameContent.Instance;
            if (content == null || content.smokeCloudPrefab == null) return;
            GameObject go = Instantiate(content.smokeCloudPrefab, position, Quaternion.identity);
            go.GetComponent<SmokeCloud>().Configure(smokeRadius, smokeDuration, smokeGrowTime, now);
            go.GetComponent<NetworkObject>().Spawn(destroyWithScene: true);
            BHLog.Info("Ability", $"Smoke detonated at {position} ({throwerTeam} team), radius {smokeRadius}, {smokeDuration}s");
        }
    }
}
