using System.Collections.Generic;
using BlueHorizon.Core;
using UnityEngine;

namespace BlueHorizon.UI
{
    /// <summary>
    /// Pooled, presentation-only visual effects: bullet tracers, impact sparks, muzzle flashes, explosions.
    /// Never instantiated on headless processes.
    /// </summary>
    public sealed class Effects : MonoBehaviour
    {
        const int TracerPool = 64;
        const int FlashPool = 12;
        const int ImpactPool = 24;

        static Effects _instance;

        struct Timed
        {
            public GameObject go;
            public float until;
            public float duration;
        }

        readonly List<LineRenderer> _tracers = new();
        readonly List<Light> _flashes = new();
        readonly List<Transform> _impacts = new();
        readonly List<Timed> _active = new();
        Transform _explosion;
        Light _explosionLight;
        float _explosionUntil;
        // Flash bursts and scan pulses: one expanding sphere + light each.
        const int BurstPool = 4;
        readonly List<(Transform t, Light light, Material material, float start, float duration, float size, Color color)> _bursts = new();
        int _nextBurst;
        int _nextTracer, _nextFlash, _nextImpact;

        public static Effects Instance
        {
            get
            {
                if (_instance == null && !Player.PlayerRig.IsHeadless)
                {
                    var go = new GameObject("Effects");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<Effects>();
                }
                return _instance;
            }
        }

        void Awake()
        {
            GameContent content = GameContent.Instance;
            Material unlit = content != null ? content.unlitMaterial : null;
            Material emissive = content != null ? content.emissiveMaterial : null;

            for (int i = 0; i < TracerPool; i++)
            {
                var go = new GameObject("Tracer");
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.positionCount = 2;
                lr.widthMultiplier = 0.02f;
                lr.numCapVertices = 0;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (unlit != null) lr.sharedMaterial = new Material(unlit) { color = new Color(1f, 0.85f, 0.5f) };
                go.SetActive(false);
                _tracers.Add(lr);
            }

            for (int i = 0; i < FlashPool; i++)
            {
                var go = new GameObject("MuzzleFlash");
                go.transform.SetParent(transform, false);
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 5f;
                light.intensity = 3f;
                light.color = new Color(1f, 0.75f, 0.4f);
                light.shadows = LightShadows.None;
                go.SetActive(false);
                _flashes.Add(light);
            }

            for (int i = 0; i < ImpactPool; i++)
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                DestroyImmediate(go.GetComponent<Collider>());
                go.name = "Impact";
                go.transform.SetParent(transform, false);
                go.transform.localScale = Vector3.one * 0.06f;
                var r = go.GetComponent<Renderer>();
                if (emissive != null) r.sharedMaterial = new Material(emissive) { color = new Color(1f, 0.8f, 0.4f) };
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                go.SetActive(false);
                _impacts.Add(go.transform);
            }

            GameObject boom = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            DestroyImmediate(boom.GetComponent<Collider>());
            boom.name = "Explosion";
            boom.transform.SetParent(transform, false);
            if (content != null && content.transparentMaterial != null)
                boom.GetComponent<Renderer>().sharedMaterial = new Material(content.transparentMaterial) { color = new Color(1f, 0.55f, 0.2f, 0.7f) };
            _explosion = boom.transform;
            _explosionLight = boom.AddComponent<Light>();
            _explosionLight.range = 30f;
            _explosionLight.intensity = 8f;
            _explosionLight.color = new Color(1f, 0.6f, 0.3f);
            boom.SetActive(false);

            for (int i = 0; i < BurstPool; i++)
            {
                GameObject b = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                DestroyImmediate(b.GetComponent<Collider>());
                b.name = "Burst";
                b.transform.SetParent(transform, false);
                var r = b.GetComponent<Renderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Material m = content != null && content.transparentMaterial != null ? new Material(content.transparentMaterial) : null;
                if (m != null) r.sharedMaterial = m;
                Light l = b.AddComponent<Light>();
                l.shadows = LightShadows.None;
                b.SetActive(false);
                _bursts.Add((b.transform, l, m, 0f, 0f, 0f, Color.white));
            }
        }

        /// <summary>Flash grenade burst: a very bright, short white light.</summary>
        public void FlashBurst(Vector3 position) => StartBurst(position, 0.35f, 3f, new Color(1f, 1f, 0.92f), 40f, 25f);

        /// <summary>EnemyAlert scan: a translucent sphere expanding to the scan radius.</summary>
        public void Pulse(Vector3 position, float radius) => StartBurst(position + Vector3.up, 0.9f, radius * 2f, new Color(1f, 0.3f, 0.25f), 2f, 12f);

        void StartBurst(Vector3 position, float duration, float size, Color color, float intensity, float range)
        {
            int index = _nextBurst;
            _nextBurst = (_nextBurst + 1) % _bursts.Count;
            var b = _bursts[index];
            b.t.position = position;
            b.light.color = color;
            b.light.intensity = intensity;
            b.light.range = range;
            b.start = Time.time;
            b.duration = duration;
            b.size = size;
            b.color = color;
            b.t.gameObject.SetActive(true);
            _bursts[index] = b;
        }

        public void Tracer(Vector3 from, Vector3 to, bool suppressed)
        {
            LineRenderer lr = _tracers[_nextTracer];
            _nextTracer = (_nextTracer + 1) % _tracers.Count;
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
            lr.widthMultiplier = suppressed ? 0.012f : 0.022f;
            Show(lr.gameObject, 0.05f);
        }

        public void MuzzleFlash(Vector3 position, bool suppressed)
        {
            if (suppressed) return;
            Light light = _flashes[_nextFlash];
            _nextFlash = (_nextFlash + 1) % _flashes.Count;
            light.transform.position = position;
            Show(light.gameObject, 0.05f);
        }

        public void Impact(Vector3 position, bool flesh)
        {
            Transform t = _impacts[_nextImpact];
            _nextImpact = (_nextImpact + 1) % _impacts.Count;
            t.position = position;
            t.rotation = Random.rotation;
            var r = t.GetComponent<Renderer>();
            if (r.sharedMaterial != null) r.sharedMaterial.color = flesh ? new Color(0.9f, 0.1f, 0.1f) : new Color(1f, 0.8f, 0.4f);
            Show(t.gameObject, 0.25f);
        }

        public void Explosion(Vector3 position)
        {
            _explosion.position = position + Vector3.up;
            _explosion.gameObject.SetActive(true);
            _explosionUntil = Time.time + 1.2f;
        }

        void Show(GameObject go, float duration)
        {
            go.SetActive(true);
            _active.Add(new Timed { go = go, until = Time.time + duration, duration = duration });
        }

        void Update()
        {
            float now = Time.time;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (now >= _active[i].until)
                {
                    _active[i].go.SetActive(false);
                    _active.RemoveAt(i);
                }
            }

            if (_explosion.gameObject.activeSelf)
            {
                float t = 1f - Mathf.Clamp01((_explosionUntil - now) / 1.2f);
                _explosion.localScale = Vector3.one * Mathf.Lerp(1f, 24f, Mathf.Sqrt(t));
                _explosionLight.intensity = Mathf.Lerp(8f, 0f, t);
                var r = _explosion.GetComponent<Renderer>();
                if (r.sharedMaterial != null) r.sharedMaterial.color = new Color(1f, 0.55f, 0.2f, 0.7f * (1f - t));
                if (now >= _explosionUntil) _explosion.gameObject.SetActive(false);
            }

            for (int i = 0; i < _bursts.Count; i++)
            {
                var b = _bursts[i];
                if (!b.t.gameObject.activeSelf) continue;
                float t = Mathf.Clamp01((now - b.start) / Mathf.Max(0.01f, b.duration));
                b.t.localScale = Vector3.one * Mathf.Lerp(0.3f, b.size, Mathf.Sqrt(t));
                b.light.intensity *= 1f - t * 0.25f;
                if (b.material != null) b.material.color = new Color(b.color.r, b.color.g, b.color.b, 0.35f * (1f - t));
                if (t >= 1f) b.t.gameObject.SetActive(false);
            }
        }
    }
}
