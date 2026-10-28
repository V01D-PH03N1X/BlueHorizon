using BlueHorizon.Attachments;
using BlueHorizon.Combat;
using BlueHorizon.Core;
using BlueHorizon.Player;
using BlueHorizon.Weapons;
using UnityEngine;

namespace BlueHorizon.UI
{
    /// <summary>
    /// Draws the visible beam and dot of a laser attachment. Runs on every client for every player, so a laser gives
    /// its user away to opponents (that is its trade-off for the tighter hip-fire spread). Presentation only.
    /// </summary>
    public sealed class LaserSight : MonoBehaviour
    {
        static readonly RaycastHit[] Hits = new RaycastHit[12];
        const float MaxLength = 80f;

        PlayerAvatar _avatar;
        PlayerRig _rig;
        LineRenderer _beam;
        Transform _dot;
        Material _material;
        Color _shownColor = Color.clear;

        public void Initialize(PlayerAvatar avatar, PlayerRig rig)
        {
            _avatar = avatar;
            _rig = rig;

            var beamGo = new GameObject("LaserBeam");
            beamGo.transform.SetParent(transform, false);
            _beam = beamGo.AddComponent<LineRenderer>();
            _beam.positionCount = 2;
            _beam.useWorldSpace = true;
            _beam.widthMultiplier = 0.006f;
            _beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _beam.receiveShadows = false;
            GameContent content = GameContent.Instance;
            _material = content != null && content.unlitMaterial != null ? new Material(content.unlitMaterial) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            _beam.sharedMaterial = _material;

            GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(dot.GetComponent<Collider>());
            dot.name = "LaserDot";
            dot.transform.SetParent(transform, false);
            dot.transform.localScale = Vector3.one * 0.035f;
            var r = dot.GetComponent<Renderer>();
            r.sharedMaterial = _material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _dot = dot.transform;
            SetVisible(false);
        }

        void LateUpdate()
        {
            if (_avatar == null || !_avatar.IsSpawned) return;
            PlayerSimulation sim = _avatar.Simulation;
            WeaponStats stats = _avatar.StatsForSlot(sim.Weapons.activeSlot);
            AttachmentDefinition laser = stats.Laser;
            // The beam is off while reloading (the weapon points at the floor).
            bool on = laser != null && _avatar.IsAlive && !sim.Weapons.IsReloading;
            SetVisible(on);
            if (!on) return;

            if (laser.laserColor != _shownColor)
            {
                _shownColor = laser.laserColor;
                _material.color = _shownColor;
                if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", _shownColor);
            }

            Quaternion view = WeaponSimulation.ViewRotation(sim.ViewYaw, sim.ViewPitch);
            Vector3 aim = view * Vector3.forward;
            Vector3 eye = _avatar.IsOwner ? sim.RenderEyePosition : _rig.HeadVisual.position;
            Vector3 origin;
            if (_avatar.IsOwner && GameCamera.Instance != null) origin = GameCamera.Instance.LaserOrigin;
            else origin = _rig.WeaponVisual != null ? _rig.WeaponVisual.position + _rig.WeaponVisual.forward * 0.25f : eye;

            // The beam converges on what the player aims at.
            Vector3 end = Trace(eye, aim, out bool hit);
            _beam.SetPosition(0, origin);
            _beam.SetPosition(1, end);
            _dot.gameObject.SetActive(hit);
            _dot.position = end - aim * 0.01f;
        }

        Vector3 Trace(Vector3 origin, Vector3 direction, out bool hit)
        {
            int count = Physics.RaycastNonAlloc(origin, direction, Hits, MaxLength, GameLayers.ShotMask, QueryTriggerInteraction.Collide);
            float best = MaxLength;
            hit = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = Hits[i];
                if (h.distance >= best) continue;
                var hitbox = h.collider.GetComponent<HitboxCollider>();
                if (hitbox != null)
                {
                    if (ReferenceEquals(hitbox.Target, _avatar.Health)) continue;
                }
                else if (h.collider.isTrigger) continue;
                best = h.distance;
                hit = true;
            }
            return origin + direction * best;
        }

        void SetVisible(bool visible)
        {
            if (_beam.enabled != visible) _beam.enabled = visible;
            if (!visible && _dot.gameObject.activeSelf) _dot.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }
}
