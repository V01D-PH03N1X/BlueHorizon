using BlueHorizon.Core;
using BlueHorizon.Player;
using Unity.Netcode;
using UnityEngine;

namespace BlueHorizon.Abilities
{
    public struct GrenadeLaunch : INetworkSerializable, System.IEquatable<GrenadeLaunch>
    {
        public Vector3 origin;
        public Vector3 velocity;
        public double launchTime;
        public float fuse;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref origin);
            serializer.SerializeValue(ref velocity);
            serializer.SerializeValue(ref launchTime);
            serializer.SerializeValue(ref fuse);
        }

        public bool Equals(GrenadeLaunch o) => origin == o.origin && velocity == o.velocity && launchTime.Equals(o.launchTime) && fuse.Equals(o.fuse);
    }

    /// <summary>
    /// Smoke grenade in flight. Server and clients integrate the same ballistic path from the replicated launch
    /// parameters (gravity + bounces against world geometry), so no per-frame transform sync is needed.
    /// The server alone decides detonation and spawns the <see cref="SmokeCloud"/>.
    /// </summary>
    public sealed class SmokeGrenadeProjectile : NetworkBehaviour
    {
        const float Gravity = 18f;
        const float Radius = 0.08f;
        const float Restitution = 0.35f;
        const float Friction = 0.6f;
        const float Step = 1f / 120f;

        public readonly NetworkVariable<GrenadeLaunch> Launch = new();

        SmokeGrenadeDefinition _definition;
        Team _team;
        Vector3 _position, _velocity;
        double _simulatedUntil;
        bool _detonated;

        GrenadeLaunch _pendingLaunch;

        /// <summary>Server, before Spawn(). Values are published in OnNetworkSpawn so they ship with the spawn message.</summary>
        public void Configure(SmokeGrenadeDefinition definition, Vector3 origin, Vector3 velocity, double now, Team team)
        {
            _definition = definition;
            _team = team;
            _pendingLaunch = new GrenadeLaunch { origin = origin, velocity = velocity, launchTime = now, fuse = definition.fuseTime };
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) Launch.Value = _pendingLaunch;
            GrenadeLaunch l = Launch.Value;
            _position = l.origin;
            _velocity = l.velocity;
            _simulatedUntil = l.launchTime;
            transform.position = _position;
            if (!PlayerRig.IsHeadless) BuildVisual();
        }

        void BuildVisual()
        {
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(transform, false);
            body.transform.localScale = new Vector3(0.09f, 0.07f, 0.09f);
            GameContent content = GameContent.Instance;
            if (content != null && content.emissiveMaterial != null)
            {
                var r = body.GetComponent<Renderer>();
                r.sharedMaterial = new Material(content.emissiveMaterial);
                r.sharedMaterial.color = new Color(0.8f, 0.85f, 0.9f);
            }
        }

        void Update()
        {
            if (!IsSpawned || _detonated) return;
            double now = NetworkManager.ServerTime.Time;
            GrenadeLaunch l = Launch.Value;
            double target = System.Math.Min(now, l.launchTime + l.fuse);

            while (_simulatedUntil + Step <= target)
            {
                Integrate(Step);
                _simulatedUntil += Step;
            }
            transform.position = _position;
            transform.Rotate(Vector3.right, 720f * Time.deltaTime, Space.Self);

            if (IsServer && now >= l.launchTime + l.fuse)
                ServerDetonate();
        }

        void Integrate(float dt)
        {
            _velocity.y -= Gravity * dt;
            Vector3 delta = _velocity * dt;
            float dist = delta.magnitude;
            if (dist < 1e-6f) return;

            if (Physics.SphereCast(_position, Radius, delta / dist, out RaycastHit hit, dist, GameLayers.WorldMask, QueryTriggerInteraction.Ignore))
            {
                _position += delta / dist * Mathf.Max(0f, hit.distance - 0.01f);
                Vector3 reflected = Vector3.Reflect(_velocity, hit.normal);
                Vector3 normalPart = Vector3.Project(reflected, hit.normal) * Restitution;
                Vector3 tangentPart = Vector3.ProjectOnPlane(reflected, hit.normal) * Friction;
                _velocity = normalPart + tangentPart;
                if (_velocity.sqrMagnitude < 0.25f) _velocity = Vector3.zero;
            }
            else
            {
                _position += delta;
            }
        }

        void ServerDetonate()
        {
            _detonated = true;
            GameContent content = GameContent.Instance;
            SmokeGrenadeDefinition def = _definition;
            if (content != null && content.smokeCloudPrefab != null && def != null)
            {
                GameObject go = Instantiate(content.smokeCloudPrefab, _position, Quaternion.identity);
                go.GetComponent<SmokeCloud>().Configure(def.smokeRadius, def.smokeDuration, def.smokeGrowTime, NetworkManager.ServerTime.Time);
                go.GetComponent<NetworkObject>().Spawn(destroyWithScene: true);
                BHLog.Info("Ability", $"Smoke detonated at {_position} ({_team} team), radius {def.smokeRadius}, {def.smokeDuration}s");
            }
            NetworkObject.Despawn(true);
        }
    }
}
