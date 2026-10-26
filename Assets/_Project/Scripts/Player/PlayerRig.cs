using BlueHorizon.Combat;
using BlueHorizon.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlueHorizon.Player
{
    /// <summary>
    /// Builds the character's hitboxes (every peer, including the dedicated server) and its graybox visuals
    /// (only on processes with a graphics device). Built in code so the prefab stays a thin list of components.
    /// </summary>
    public sealed class PlayerRig : MonoBehaviour
    {
        public const float StandingHeight = 1.8f;

        public Transform HitboxRoot { get; private set; }
        public Transform VisualRoot { get; private set; }
        public Transform HeadVisual { get; private set; }
        public Transform WeaponVisual { get; private set; }
        public Transform BombVisual { get; private set; }
        public Renderer[] BodyRenderers { get; private set; }
        public Renderer VisorRenderer { get; private set; }
        public bool HasVisuals => VisualRoot != null;

        public static bool IsHeadless => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;

        public static PlayerRig Build(PlayerAvatar avatar)
        {
            var rig = avatar.gameObject.GetComponent<PlayerRig>();
            if (rig == null) rig = avatar.gameObject.AddComponent<PlayerRig>();
            rig.BuildHitboxes(avatar);
            if (!IsHeadless) rig.BuildVisuals();
            return rig;
        }

        void BuildHitboxes(PlayerAvatar avatar)
        {
            var root = new GameObject("Hitboxes");
            root.transform.SetParent(transform, false);
            root.layer = GameLayers.Hitbox;
            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            HitboxRoot = root.transform;

            var health = avatar.GetComponent<Health.PlayerHealth>();
            AddSphere(root.transform, "Head", HitZone.Head, new Vector3(0f, 1.62f, 0.02f), 0.16f, health);
            AddBox(root.transform, "Torso", HitZone.Torso, new Vector3(0f, 1.16f, 0f), new Vector3(0.52f, 0.64f, 0.34f), health);
            AddBox(root.transform, "Legs", HitZone.Legs, new Vector3(0f, 0.42f, 0f), new Vector3(0.44f, 0.84f, 0.3f), health);
        }

        static void AddSphere(Transform parent, string name, HitZone zone, Vector3 center, float radius, MonoBehaviour owner)
        {
            var go = new GameObject(name) { layer = GameLayers.Hitbox };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            var col = go.AddComponent<SphereCollider>();
            col.radius = radius;
            col.isTrigger = true;
            var hb = go.AddComponent<HitboxCollider>();
            hb.zone = zone;
            hb.owner = owner;
        }

        static void AddBox(Transform parent, string name, HitZone zone, Vector3 center, Vector3 size, MonoBehaviour owner)
        {
            var go = new GameObject(name) { layer = GameLayers.Hitbox };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            var col = go.AddComponent<BoxCollider>();
            col.size = size;
            col.isTrigger = true;
            var hb = go.AddComponent<HitboxCollider>();
            hb.zone = zone;
            hb.owner = owner;
        }

        void BuildVisuals()
        {
            GameContent content = GameContent.Instance;
            Material lit = content != null ? content.litMaterial : null;
            Material emissive = content != null ? content.emissiveMaterial : null;

            var root = new GameObject("Visual");
            root.transform.SetParent(transform, false);
            VisualRoot = root.transform;

            Renderer torso = Primitive(PrimitiveType.Capsule, "Body", VisualRoot, new Vector3(0f, 0.95f, 0f), new Vector3(0.62f, 0.62f, 0.5f), lit);
            Renderer legs = Primitive(PrimitiveType.Cube, "Legs", VisualRoot, new Vector3(0f, 0.4f, 0f), new Vector3(0.42f, 0.8f, 0.28f), lit);
            Renderer pack = Primitive(PrimitiveType.Cube, "Pack", VisualRoot, new Vector3(0f, 1.2f, -0.24f), new Vector3(0.38f, 0.42f, 0.16f), lit);

            var head = new GameObject("HeadPivot").transform;
            head.SetParent(VisualRoot, false);
            head.localPosition = new Vector3(0f, 1.6f, 0f);
            HeadVisual = head;
            Renderer helmet = Primitive(PrimitiveType.Sphere, "Helmet", head, Vector3.zero, new Vector3(0.32f, 0.32f, 0.34f), lit);
            VisorRenderer = Primitive(PrimitiveType.Cube, "Visor", head, new Vector3(0f, 0.01f, 0.14f), new Vector3(0.26f, 0.08f, 0.08f), emissive);

            var weapon = new GameObject("Weapon").transform;
            weapon.SetParent(head, false);
            weapon.localPosition = new Vector3(0.22f, -0.28f, 0.3f);
            WeaponVisual = weapon;
            Primitive(PrimitiveType.Cube, "Gun", weapon, Vector3.zero, new Vector3(0.07f, 0.11f, 0.5f), lit).sharedMaterial.color = new Color(0.12f, 0.13f, 0.15f);

            BombVisual = Primitive(PrimitiveType.Cube, "Bomb", VisualRoot, new Vector3(0f, 1.05f, -0.36f), new Vector3(0.3f, 0.36f, 0.14f), emissive).transform;
            BombVisual.gameObject.SetActive(false);

            BodyRenderers = new[] { torso, legs, pack, helmet };
        }

        static Renderer Primitive(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            if (material != null) r.sharedMaterial = new Material(material);
            r.shadowCastingMode = ShadowCastingMode.On;
            return r;
        }

        /// <summary>Scales hitboxes and visuals for crouching (1 = standing).</summary>
        public void SetHeightScale(float heightScale)
        {
            if (HitboxRoot != null) HitboxRoot.localScale = new Vector3(1f, heightScale, 1f);
            if (VisualRoot != null) VisualRoot.localScale = new Vector3(1f, heightScale, 1f);
        }
    }
}
