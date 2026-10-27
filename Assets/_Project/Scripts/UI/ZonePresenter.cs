using BlueHorizon.Core;
using BlueHorizon.GameModes;
using BlueHorizon.Match;
using BlueHorizon.Objectives;
using UnityEngine;

namespace BlueHorizon.UI
{
    /// <summary>
    /// Capture-the-Area visuals on clients: a glowing floor plate tinted by the controlling team (pulsing while
    /// contested) and four corner posts whose height shows capture progress. Thin geometry only, so standing inside
    /// the zone does not tint the whole view. Driven entirely by the replicated <see cref="ModeStatus"/>.
    /// </summary>
    public sealed class ZonePresenter : MonoBehaviour
    {
        const float MaxPostHeight = 3f;

        Material _plateMaterial;
        Material _postMaterial;
        readonly Transform[] _posts = new Transform[4];

        public static void EnsureFor(MatchManager match)
        {
            if (match == null || match.Mode is not CaptureAreaSettings || CaptureZone.Main == null) return;
            if (CaptureZone.Main.GetComponentInChildren<ZonePresenter>() != null) return;
            var go = new GameObject("ZonePresenter");
            go.transform.SetParent(CaptureZone.Main.transform, false);
            go.AddComponent<ZonePresenter>();
        }

        void Awake()
        {
            GameContent content = GameContent.Instance;
            Vector3 size = CaptureZone.Main.size;

            Renderer plate = Part("Plate", new Vector3(0f, 0.025f, 0f), new Vector3(size.x, 0.03f, size.z));
            if (content != null && content.transparentMaterial != null)
            {
                _plateMaterial = new Material(content.transparentMaterial);
                plate.sharedMaterial = _plateMaterial;
            }

            if (content != null && content.emissiveMaterial != null) _postMaterial = new Material(content.emissiveMaterial);
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0 ? -0.5f : 0.5f) * size.x;
                float z = (i < 2 ? -0.5f : 0.5f) * size.z;
                Renderer post = Part($"Post{i}", new Vector3(x, 0f, z), new Vector3(0.15f, 0.05f, 0.15f));
                if (_postMaterial != null) post.sharedMaterial = _postMaterial;
                _posts[i] = post.transform;
            }
        }

        Renderer Part(string name, Vector3 position, Vector3 scale)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }

        void OnDestroy()
        {
            if (_plateMaterial != null) Destroy(_plateMaterial);
            if (_postMaterial != null) Destroy(_postMaterial);
        }

        void LateUpdate()
        {
            MatchManager match = MatchManager.Instance;
            if (match == null) return;
            ModeStatus z = match.Status.Value;

            Color owner = z.owner == Team.None ? new Color(0.85f, 0.9f, 1f) : PlayerPresenter.TeamColor(z.owner);
            float alpha = z.contested ? 0.25f + 0.25f * Mathf.Abs(Mathf.Sin(Time.time * 6f)) : 0.35f;
            if (_plateMaterial != null) _plateMaterial.color = new Color(owner.r, owner.g, owner.b, alpha);

            // Posts: full height in the owner's colour; while capturing they grow in the capturing team's colour.
            bool capturing = z.capturing != Team.None;
            Color postColor = capturing ? PlayerPresenter.TeamColor(z.capturing) : owner;
            float height = capturing ? Mathf.Max(0.05f, z.progress * MaxPostHeight) : z.owner != Team.None ? MaxPostHeight : 0.05f;
            if (_postMaterial != null)
            {
                _postMaterial.color = postColor;
                if (_postMaterial.HasProperty("_EmissionColor")) _postMaterial.SetColor("_EmissionColor", postColor * 2f);
            }
            foreach (Transform post in _posts)
            {
                post.localScale = new Vector3(0.15f, height, 0.15f);
                post.localPosition = new Vector3(post.localPosition.x, height * 0.5f, post.localPosition.z);
            }
        }
    }
}
