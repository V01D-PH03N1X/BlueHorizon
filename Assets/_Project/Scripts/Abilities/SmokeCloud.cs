using System.Collections.Generic;
using BlueHorizon.Core;
using BlueHorizon.Player;
using Unity.Netcode;
using UnityEngine;

namespace BlueHorizon.Abilities
{
    public struct SmokeParams : INetworkSerializable, System.IEquatable<SmokeParams>
    {
        public float radius;
        public float duration;
        public float growTime;
        public double startTime;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref radius);
            serializer.SerializeValue(ref duration);
            serializer.SerializeValue(ref growTime);
            serializer.SerializeValue(ref startTime);
        }

        public bool Equals(SmokeParams o) => radius.Equals(o.radius) && duration.Equals(o.duration) && growTime.Equals(o.growTime) && startTime.Equals(o.startTime);
    }

    /// <summary>
    /// Smoke volume. Visibility model (vertical slice): a purely visual obstruction made of layered, double-sided
    /// translucent shells plus a screen fog when the camera is inside. Server hit detection ignores smoke —
    /// bullets pass through, as in most tactical shooters. The server owns the lifetime and despawns it.
    /// </summary>
    public sealed class SmokeCloud : NetworkBehaviour
    {
        const float FadeOutTime = 1.5f;
        static readonly List<SmokeCloud> Active = new();

        public readonly NetworkVariable<SmokeParams> Params = new();

        readonly List<(Transform shell, Material material, float scale, float alpha)> _shells = new();

        SmokeParams _pending;

        /// <summary>Server, before Spawn().</summary>
        public void Configure(float radius, float duration, float growTime, double now)
        {
            _pending = new SmokeParams { radius = radius, duration = duration, growTime = growTime, startTime = now };
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) Params.Value = _pending;
            Active.Add(this);
            if (!PlayerRig.IsHeadless) BuildVisual();
        }

        public override void OnNetworkDespawn()
        {
            Active.Remove(this);
            foreach (var s in _shells) Destroy(s.material);
        }

        void BuildVisual()
        {
            GameContent content = GameContent.Instance;
            Material baseMat = content != null ? content.transparentMaterial : null;
            float[] scales = { 1f, 0.82f, 0.62f, 0.4f };
            float[] alphas = { 0.55f, 0.6f, 0.7f, 0.8f };
            for (int i = 0; i < scales.Length; i++)
            {
                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                DestroyImmediate(sphere.GetComponent<Collider>());
                sphere.name = $"SmokeShell{i}";
                sphere.transform.SetParent(transform, false);
                sphere.transform.localPosition = new Vector3(0f, -0.15f * i, 0f);
                var r = sphere.GetComponent<Renderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Material m = baseMat != null ? new Material(baseMat) : r.material;
                r.sharedMaterial = m;
                _shells.Add((sphere.transform, m, scales[i], alphas[i]));
            }
        }

        public float Age => NetworkManager != null ? (float)(NetworkManager.ServerTime.Time - Params.Value.startTime) : 0f;

        public float CurrentRadius
        {
            get
            {
                SmokeParams p = Params.Value;
                float grow = p.growTime > 0f ? Mathf.Clamp01(Age / p.growTime) : 1f;
                return p.radius * Mathf.SmoothStep(0.2f, 1f, grow);
            }
        }

        /// <summary>0..1 density, fading out at the end of the lifetime.</summary>
        public float Density
        {
            get
            {
                SmokeParams p = Params.Value;
                float remaining = p.duration - Age;
                return Mathf.Clamp01(remaining / FadeOutTime);
            }
        }

        void Update()
        {
            if (!IsSpawned) return;
            SmokeParams p = Params.Value;

            if (IsServer && Age >= p.duration)
            {
                NetworkObject.Despawn(true);
                return;
            }

            float radius = CurrentRadius;
            float density = Density;
            for (int i = 0; i < _shells.Count; i++)
            {
                var s = _shells[i];
                s.shell.localScale = Vector3.one * (radius * 2f * s.scale);
                s.shell.Rotate(Vector3.up, (6f + i * 3f) * Time.deltaTime);
                Color c = new Color(0.78f, 0.8f, 0.82f, s.alpha * density);
                s.material.color = c;
            }
        }

        /// <summary>Screen fog factor for a camera position: 1 deep inside a cloud, 0 outside all clouds.</summary>
        public static float FogAt(Vector3 position)
        {
            float fog = 0f;
            for (int i = 0; i < Active.Count; i++)
            {
                SmokeCloud c = Active[i];
                float r = c.CurrentRadius;
                if (r <= 0f) continue;
                float d = Vector3.Distance(position, c.transform.position);
                float inside = Mathf.Clamp01((r - d) / (r * 0.35f));
                fog = Mathf.Max(fog, inside * c.Density);
            }
            return fog;
        }

        /// <summary>True when the segment passes through a smoke volume. Used by the test bot's vision check.</summary>
        public static bool BlocksLine(Vector3 a, Vector3 b)
        {
            for (int i = 0; i < Active.Count; i++)
            {
                SmokeCloud c = Active[i];
                if (c.Density < 0.5f) continue;
                float r = c.CurrentRadius * 0.85f;
                Vector3 center = c.transform.position;
                Vector3 ab = b - a;
                float t = Mathf.Clamp01(Vector3.Dot(center - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-5f));
                if ((a + ab * t - center).sqrMagnitude < r * r) return true;
            }
            return false;
        }
    }
}
