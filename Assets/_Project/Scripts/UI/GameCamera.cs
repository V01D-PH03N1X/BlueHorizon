using BlueHorizon.Abilities;
using BlueHorizon.Attachments;
using BlueHorizon.Core;
using BlueHorizon.Player;
using BlueHorizon.Weapons;
using UnityEngine;

namespace BlueHorizon.UI
{
    /// <summary>
    /// Local camera: first-person view with a placeholder viewmodel while alive; spectates a living teammate
    /// (or the map overview) while dead. Owns no gameplay state.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class GameCamera : MonoBehaviour
    {
        public static GameCamera Instance { get; private set; }

        public static float FieldOfView
        {
            get => PlayerPrefs.GetFloat("bh.fov", 74f);
            set => PlayerPrefs.SetFloat("bh.fov", Mathf.Clamp(value, 60f, 100f));
        }

        Camera _camera;
        PlayerAvatar _local;
        Vector3 _overviewPosition;
        Quaternion _overviewRotation;

        Transform _viewmodel;
        Transform _model;
        Transform _muzzle;
        byte _shownSlot = 255;
        bool _shownSuppressor;
        WeaponDefinition _shownWeapon;
        float _kick;
        float _spectateSwitchTime;
        int _spectateIndex;

        public Camera Camera => _camera;
        public Vector3 MuzzlePosition => _muzzle != null && _viewmodel.gameObject.activeInHierarchy ? _muzzle.position : transform.position;

        public static GameCamera Ensure()
        {
            if (Instance != null) return Instance;
            Camera cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            var existing = cam.GetComponent<GameCamera>();
            return existing != null ? existing : cam.gameObject.AddComponent<GameCamera>();
        }

        void Awake()
        {
            Instance = this;
            _camera = GetComponent<Camera>();
            _camera.nearClipPlane = 0.03f;
            _overviewPosition = transform.position;
            _overviewRotation = transform.rotation;
            BuildViewmodel();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void AttachLocal(PlayerPresenter presenter) => _local = presenter.GetComponent<PlayerAvatar>();

        void BuildViewmodel()
        {
            _viewmodel = new GameObject("Viewmodel").transform;
            _viewmodel.SetParent(transform, false);
            _viewmodel.localPosition = RestPosition;
            _muzzle = new GameObject("Muzzle").transform;
            _muzzle.SetParent(_viewmodel, false);
            _viewmodel.gameObject.SetActive(false);
        }

        static readonly Vector3 RestPosition = new(0.17f, -0.16f, 0.34f);

        /// <summary>Placeholder weapon model assembled from boxes: receiver, barrel, grip/magazine, stock, sight, suppressor.</summary>
        void RebuildModel(WeaponDefinition def, bool suppressed)
        {
            if (_model != null) Destroy(_model.gameObject);
            _model = new GameObject("Model").transform;
            _model.SetParent(_viewmodel, false);

            Vector3 s = def != null ? def.viewmodelSize : new Vector3(0.05f, 0.09f, 0.3f);
            Color body = def != null ? def.viewmodelColor : new Color(0.2f, 0.2f, 0.22f);
            Color dark = Color.Lerp(body, Color.black, 0.45f);
            bool primary = def != null && def.slot == WeaponSlot.Primary;
            float barrelLength = primary ? s.z * 0.45f : s.z * 0.15f;

            Part(PrimitiveType.Cube, body, new Vector3(0f, 0f, 0f), new Vector3(s.x, s.y * 0.5f, s.z));
            Part(PrimitiveType.Cube, dark, new Vector3(0f, s.y * 0.08f, s.z * 0.5f + barrelLength * 0.5f), new Vector3(s.x * 0.32f, s.x * 0.32f, barrelLength));
            Part(PrimitiveType.Cube, dark, new Vector3(0f, -s.y * 0.42f, primary ? s.z * 0.12f : -s.z * 0.18f), new Vector3(s.x * 0.75f, s.y * 0.75f, s.x * 1.2f));
            if (primary)
            {
                Part(PrimitiveType.Cube, dark, new Vector3(0f, -s.y * 0.38f, -s.z * 0.15f), new Vector3(s.x * 0.7f, s.y * 0.55f, s.x * 0.9f));
                Part(PrimitiveType.Cube, body, new Vector3(0f, -s.y * 0.05f, -s.z * 0.62f), new Vector3(s.x * 0.85f, s.y * 0.55f, s.z * 0.3f));
                Part(PrimitiveType.Cube, dark, new Vector3(0f, s.y * 0.35f, s.z * 0.05f), new Vector3(s.x * 0.5f, s.y * 0.25f, s.z * 0.2f));
            }

            float muzzleZ = s.z * 0.5f + barrelLength;
            if (suppressed)
            {
                AttachmentDefinition att = GameContent.Instance != null ? GameContent.Instance.suppressor : null;
                Vector3 a = att != null ? att.viewmodelSize : new Vector3(0.05f, 0.05f, 0.16f);
                Transform sup = Part(PrimitiveType.Cylinder, att != null ? att.viewmodelColor : Color.black,
                    new Vector3(0f, s.y * 0.08f, muzzleZ + a.z * 0.5f), new Vector3(a.x, a.z * 0.5f, a.y));
                sup.localRotation = Quaternion.Euler(90f, 0f, 0f);
                muzzleZ += a.z;
            }
            _muzzle.localPosition = new Vector3(0f, s.y * 0.08f, muzzleZ);
        }

        Transform Part(PrimitiveType type, Color color, Vector3 position, Vector3 scale)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(_model, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            GameContent content = GameContent.Instance;
            if (content != null && content.litMaterial != null)
                r.sharedMaterial = new Material(content.litMaterial) { color = color };
            return go.transform;
        }

        public void Kick(WeaponDefinition weapon)
        {
            if (weapon != null) _kick = Mathf.Min(1f, _kick + (weapon.slot == WeaponSlot.Primary ? 0.35f : 0.6f));
        }

        void LateUpdate()
        {
            _camera.fieldOfView = FieldOfView;

            if (_local != null && _local.IsSpawned && _local.IsAlive)
            {
                PlayerSimulation sim = _local.Simulation;
                transform.SetPositionAndRotation(sim.RenderEyePosition, WeaponSimulation.ViewRotation(sim.ViewYaw, sim.ViewPitch));
                UpdateViewmodel(sim);
                return;
            }

            _viewmodel.gameObject.SetActive(false);
            Spectate();
        }

        void UpdateViewmodel(PlayerSimulation sim)
        {
            _viewmodel.gameObject.SetActive(true);
            WeaponSimState w = sim.Weapons;
            bool suppressed = _local.HasSuppressor(w.activeSlot);
            WeaponDefinition current = _local.WeaponInSlot(w.activeSlot);
            if (w.activeSlot != _shownSlot || suppressed != _shownSuppressor || current != _shownWeapon)
            {
                _shownSlot = w.activeSlot;
                _shownSuppressor = suppressed;
                _shownWeapon = current;
                WeaponDefinition def = current;
                RebuildModel(def, suppressed);
            }

            // Placeholder animation: recoil kick, reload dip, equip raise, sprint lower.
            _kick = Mathf.MoveTowards(_kick, 0f, Time.deltaTime * 6f);
            float reload = w.IsReloading ? 1f : 0f;
            float equip = Mathf.Clamp01((w.equipEndTime - sim.SimTime) / 0.4f);
            Vector3 v = sim.Velocity;
            float bob = sim.Grounded ? Mathf.Sin(Time.time * 9f) * Mathf.Clamp01(new Vector2(v.x, v.z).magnitude / 5f) * 0.008f : 0f;
            _viewmodel.localPosition = RestPosition + new Vector3(0f, -reload * 0.1f - equip * 0.18f + bob, -_kick * 0.04f);
            _viewmodel.localRotation = Quaternion.Euler(-_kick * 6f + reload * 25f, 0f, reload * -15f);
        }

        void Spectate()
        {
            PlayerAvatar target = null;
            if (_local != null)
            {
                if (Time.time >= _spectateSwitchTime)
                {
                    _spectateSwitchTime = Time.time + 1f;
                    var alive = new System.Collections.Generic.List<PlayerAvatar>();
                    foreach (PlayerAvatar a in PlayerAvatar.All)
                        if (a != _local && a.IsAlive && a.Team == _local.Team) alive.Add(a);
                    target = alive.Count > 0 ? alive[_spectateIndex % alive.Count] : null;
                    _spectateTarget = target;
                }
                target = _spectateTarget != null && _spectateTarget.IsAlive ? _spectateTarget : null;
            }

            if (target != null)
            {
                Quaternion rot = Quaternion.Euler(15f, target.Simulation.ViewYaw, 0f);
                Vector3 pivot = target.transform.position + Vector3.up * 1.7f;
                Vector3 desired = pivot - rot * Vector3.forward * 3.5f;
                if (Physics.Linecast(pivot, desired, out RaycastHit hit, GameLayers.WorldMask, QueryTriggerInteraction.Ignore))
                    desired = hit.point + hit.normal * 0.2f;
                transform.SetPositionAndRotation(Vector3.Lerp(transform.position, desired, 10f * Time.deltaTime), rot);
            }
            else
            {
                transform.SetPositionAndRotation(
                    Vector3.Lerp(transform.position, _overviewPosition, 3f * Time.deltaTime),
                    Quaternion.Slerp(transform.rotation, _overviewRotation, 3f * Time.deltaTime));
            }
        }

        PlayerAvatar _spectateTarget;

        public void CycleSpectate() => _spectateIndex++;

        /// <summary>0..1 screen fog when the camera is inside smoke.</summary>
        public float SmokeFog => SmokeCloud.FogAt(transform.position);
    }
}
