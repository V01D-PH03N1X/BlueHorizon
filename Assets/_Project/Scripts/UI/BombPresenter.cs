using BlueHorizon.Core;
using BlueHorizon.Match;
using BlueHorizon.Objectives;
using UnityEngine;

namespace BlueHorizon.UI
{
    /// <summary>World-space bomb visual on clients, driven entirely by the replicated <see cref="BombNetState"/>.</summary>
    public sealed class BombPresenter : MonoBehaviour
    {
        Transform _body;
        Renderer _light;
        Material _lightMaterial;

        public static BombPresenter Ensure()
        {
            var existing = FindAnyObjectByType<BombPresenter>();
            if (existing != null) return existing;
            return new GameObject("BombPresenter").AddComponent<BombPresenter>();
        }

        void Awake()
        {
            GameContent content = GameContent.Instance;
            _body = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            DestroyImmediate(_body.GetComponent<Collider>());
            _body.SetParent(transform, false);
            _body.localScale = new Vector3(0.45f, 0.22f, 0.32f);
            _body.localPosition = new Vector3(0f, 0.11f, 0f);
            if (content != null && content.litMaterial != null)
                _body.GetComponent<Renderer>().sharedMaterial = new Material(content.litMaterial) { color = new Color(0.18f, 0.2f, 0.16f) };

            GameObject lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            DestroyImmediate(lamp.GetComponent<Collider>());
            lamp.transform.SetParent(transform, false);
            lamp.transform.localScale = Vector3.one * 0.08f;
            lamp.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            _light = lamp.GetComponent<Renderer>();
            if (content != null && content.emissiveMaterial != null)
            {
                _lightMaterial = new Material(content.emissiveMaterial);
                _light.sharedMaterial = _lightMaterial;
            }
            gameObject.SetActive(true);
        }

        void LateUpdate()
        {
            MatchManager match = MatchManager.Instance;
            bool visible = match != null && match.Bomb.Value.state is BombState.Dropped or BombState.Planting or BombState.Planted
                or BombState.Defusing or BombState.Defused;
            _body.gameObject.SetActive(visible);
            _light.gameObject.SetActive(visible);
            if (!visible) return;

            BombNetState bomb = match.Bomb.Value;
            transform.position = bomb.position;
            if (_lightMaterial != null)
            {
                bool armed = bomb.state is BombState.Planted or BombState.Defusing;
                bool on = armed ? Mathf.Repeat(Time.time * 2f, 1f) < 0.5f : bomb.state != BombState.Defused;
                Color c = bomb.state == BombState.Defused ? new Color(0.3f, 1f, 0.5f) : armed ? new Color(1f, 0.15f, 0.1f) : new Color(1f, 0.7f, 0.2f);
                _lightMaterial.color = on ? c : c * 0.2f;
                if (_lightMaterial.HasProperty("_EmissionColor")) _lightMaterial.SetColor("_EmissionColor", on ? c * 3f : Color.black);
            }
        }
    }
}
