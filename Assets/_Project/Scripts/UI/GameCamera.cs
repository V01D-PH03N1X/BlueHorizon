using BlueHorizon.Abilities;
using BlueHorizon.Attachments;
using BlueHorizon.Core;
using BlueHorizon.Player;
using BlueHorizon.Weapons;
using UnityEngine;

namespace BlueHorizon.UI
{
    /// <summary>
    /// Local camera: first-person view with the weapon viewmodel while alive; spectates a living teammate
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
        WeaponModel _weaponModel;
        Transform _muzzle;
        Transform _mount;
        Transform _laserEmitter;
        byte _shownSlot = 255;
        PlayerLoadout _shownLoadout;
        WeaponDefinition _shownWeapon;
        float _sightHeight;
        float _kick;
        float _spectateSwitchTime;
        int _spectateIndex;

        public Camera Camera => _camera;
        public Vector3 MuzzlePosition => _muzzle != null && _viewmodel.gameObject.activeInHierarchy ? _muzzle.position : transform.position;
        /// <summary>Where the local laser beam starts (falls back to below the eye while scoped).</summary>
        public Vector3 LaserOrigin => _laserEmitter != null && _viewmodel.gameObject.activeInHierarchy
            ? _laserEmitter.position
            : transform.position - transform.up * 0.05f;

        /// <summary>Smoothed 0..1 aim-down-sights blend of the local player (presentation).</summary>
        public float AdsBlend { get; private set; }
        /// <summary>Looking through a magnified scope: HUD draws the scope overlay, the viewmodel is hidden.</summary>
        public bool Scoped { get; private set; }
        public WeaponStats ActiveStats { get; private set; }

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
            _viewmodel.gameObject.SetActive(false);
        }

        static readonly Vector3 RestPosition = new(0.17f, -0.16f, 0.34f);

        /// <summary>Instantiates the weapon's model and adds fitted attachments from their data at the model's anchors.</summary>
        void RebuildModel(WeaponDefinition def, int slot)
        {
            if (_model != null) Destroy(_model.gameObject);
            _model = new GameObject("Model").transform;
            _model.SetParent(_viewmodel, false);
            _laserEmitter = null;
            _sightHeight = 0f;
            _weaponModel = null;
            _muzzle = null;
            if (def == null || def.modelPrefab == null) return;

            WeaponModel model = Instantiate(def.modelPrefab, _model, false);
            _weaponModel = model;
            _mount = model.mount;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            Vector3 sight = model.sight.localPosition;
            Vector3 muzzle = model.muzzle.localPosition;
            AttachmentDefinition optic = _local != null ? _local.StatsForSlot(slot).Optic : def.fixedOptic;
            _sightHeight = sight.y;

            for (int a = 0; a < PlayerLoadout.AttachmentSlots; a++)
            {
                AttachmentDefinition att = a == (int)AttachmentSlot.Optic ? optic : _local != null ? _local.AttachmentIn(slot, (AttachmentSlot)a) : null;
                if (att == null) continue;
                Vector3 size = att.viewmodelSize;
                Color color = att.viewmodelColor;
                switch (att.slot)
                {
                    case AttachmentSlot.Muzzle:
                        Transform sup = Part(PrimitiveType.Cylinder, color, muzzle + new Vector3(0f, 0f, size.z * 0.5f), new Vector3(size.x, size.z * 0.5f, size.y));
                        sup.localRotation = Quaternion.Euler(90f, 0f, 0f);
                        muzzle.z += size.z;
                        break;
                    case AttachmentSlot.Underbarrel:
                        Vector3 pos = model.underbarrel.localPosition + new Vector3(att.viewmodelOffset.x * 0.5f, 0f, 0f);
                        Part(PrimitiveType.Cube, color, pos, size);
                        if (att.visibleLaser)
                        {
                            Transform lens = Part(PrimitiveType.Cube, att.laserColor, pos + new Vector3(0f, 0f, size.z * 0.5f), new Vector3(size.x * 0.6f, size.y * 0.6f, 0.004f));
                            _laserEmitter = lens;
                        }
                        break;
                    case AttachmentSlot.Optic:
                        if (model.builtInOptic) break;
                        float height = sight.y + size.y * 0.5f;
                        if (att.magnified)
                        {
                            Transform tube = Part(PrimitiveType.Cylinder, color, new Vector3(0f, height + 0.005f, sight.z), new Vector3(size.x, size.z * 0.5f, size.y));
                            tube.localRotation = Quaternion.Euler(90f, 0f, 0f);
                            Part(PrimitiveType.Cube, color, new Vector3(0f, sight.y + 0.004f, sight.z), new Vector3(0.02f, 0.012f, size.z * 0.5f));
                        }
                        else
                        {
                            // Open frame: base and hood; the reticle is drawn by the HUD at screen centre.
                            Part(PrimitiveType.Cube, color, new Vector3(0f, sight.y + 0.006f, sight.z), new Vector3(size.x, 0.012f, size.z));
                            Part(PrimitiveType.Cube, color, new Vector3(0f, height + size.y * 0.35f, sight.z + size.z * 0.3f), new Vector3(size.x, 0.008f, size.z * 0.3f));
                            Part(PrimitiveType.Cube, color, new Vector3(-size.x * 0.45f, height, sight.z + size.z * 0.3f), new Vector3(0.006f, size.y * 0.8f, size.z * 0.3f));
                            Part(PrimitiveType.Cube, color, new Vector3(size.x * 0.45f, height, sight.z + size.z * 0.3f), new Vector3(0.006f, size.y * 0.8f, size.z * 0.3f));
                        }
                        _sightHeight = height;
                        break;
                }
            }
            _muzzle = new GameObject("MuzzleEnd").transform;
            _muzzle.SetParent(_mount, false);
            _muzzle.localPosition = muzzle;
        }

        Transform Part(PrimitiveType type, Color color, Vector3 position, Vector3 scale)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(_mount, false);
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
            if (weapon == null) return;
            if (_weaponModel != null) _weaponModel.PlayFire(weapon);
            float kick = weapon.category switch
            {
                WeaponCategory.Shotgun or WeaponCategory.SniperRifle => 1f,
                WeaponCategory.Revolver => 0.8f,
                WeaponCategory.PrecisionRifle => 0.55f,
                WeaponCategory.Pistol => 0.6f,
                _ => 0.35f,
            };
            _kick = Mathf.Min(1f, _kick + kick * (1f - AdsBlend * 0.6f));
        }

        void LateUpdate()
        {
            float baseFov = FieldOfView;
            float fov = baseFov;
            AdsBlend = 0f;
            Scoped = false;

            if (_local != null && _local.IsSpawned && _local.IsAlive)
            {
                PlayerSimulation sim = _local.Simulation;
                WeaponSimState w = sim.Weapons;
                ActiveStats = _local.StatsForSlot(w.activeSlot);
                AdsBlend = Mathf.SmoothStep(0f, 1f, w.adsProgress);
                fov = Mathf.Lerp(baseFov, baseFov * ActiveStats.AdsFovMultiplier, AdsBlend);
                Scoped = ActiveStats.Magnified && w.adsProgress > 0.9f;
                transform.SetPositionAndRotation(sim.RenderEyePosition, WeaponSimulation.ViewRotation(sim.ViewYaw, sim.ViewPitch));
                UpdateViewmodel(sim);
            }
            else
            {
                ActiveStats = default;
                _viewmodel.gameObject.SetActive(false);
                Spectate();
            }

            _camera.fieldOfView = fov;
            // Keep the on-screen turn rate proportional while zoomed.
            InputPreferences.ZoomSensitivityScale = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(baseFov * 0.5f * Mathf.Deg2Rad);
        }

        void UpdateViewmodel(PlayerSimulation sim)
        {
            _viewmodel.gameObject.SetActive(!Scoped);
            WeaponSimState w = sim.Weapons;
            PlayerLoadout loadout = _local.LoadoutValue.Value;
            WeaponDefinition current = _local.WeaponInSlot(w.activeSlot);
            if (w.activeSlot != _shownSlot || !loadout.Equals(_shownLoadout) || current != _shownWeapon || _model == null)
            {
                _shownSlot = w.activeSlot;
                _shownLoadout = loadout;
                _shownWeapon = current;
                RebuildModel(current, w.activeSlot);
                if (_weaponModel != null && sim.SimTime < w.equipEndTime) _weaponModel.PlayEquip(current);
            }
            if (_weaponModel != null) _weaponModel.SetReloading(w.IsReloading, current);

            // Reload and equip motion come from the weapon clips; the camera adds recoil kick and walking bob.
            _kick = Mathf.MoveTowards(_kick, 0f, Time.deltaTime * 6f);
            Vector3 v = sim.Velocity;
            float bob = sim.Grounded ? Mathf.Sin(Time.time * 9f) * Mathf.Clamp01(new Vector2(v.x, v.z).magnitude / 5f) * 0.008f : 0f;
            // Aiming centres the sight line on the camera axis.
            Vector3 aimPosition = new(0f, -_sightHeight - 0.012f, RestPosition.z - 0.04f);
            Vector3 rest = Vector3.Lerp(RestPosition, aimPosition, AdsBlend);
            _viewmodel.localPosition = rest + new Vector3(0f, bob * (1f - AdsBlend * 0.8f), -_kick * 0.04f);
            _viewmodel.localRotation = Quaternion.Euler(-_kick * 6f * (1f - AdsBlend * 0.5f), 0f, 0f);
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
