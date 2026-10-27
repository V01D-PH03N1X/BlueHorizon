using BlueHorizon.Abilities;
using BlueHorizon.Audio;
using BlueHorizon.Core;
using BlueHorizon.Match;
using BlueHorizon.Objectives;
using BlueHorizon.Player;
using BlueHorizon.Weapons;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlueHorizon.UI
{
    /// <summary>
    /// Client-side presentation of one player: team colours, first-person body hiding, remote aim pose,
    /// death pose, carried bomb, shot/ability feedback. Contains no gameplay rules.
    /// </summary>
    public sealed class PlayerPresenter : NetworkBehaviour
    {
        public static readonly Color AlphaColor = new(0.25f, 0.62f, 1f);
        public static readonly Color BravoColor = new(1f, 0.48f, 0.22f);

        PlayerAvatar _avatar;
        PlayerSimulation _sim;
        PlayerRig _rig;
        bool _wasReloading;
        byte _lastSlot;

        public static Color TeamColor(Team team) => team switch
        {
            Team.Alpha => AlphaColor,
            Team.Bravo => BravoColor,
            _ => new Color(0.92f, 0.32f, 0.3f), // Free-for-all: everyone else is hostile.
        };

        void Awake()
        {
            _avatar = GetComponent<PlayerAvatar>();
            _sim = GetComponent<PlayerSimulation>();
        }

        public override void OnNetworkSpawn()
        {
            _rig = GetComponent<PlayerRig>();
            if (_rig == null || !_rig.HasVisuals)
            {
                enabled = false;
                return;
            }

            _avatar.TeamValue.OnValueChanged += OnTeamChanged;
            _sim.LocalShotPredicted += OnLocalShot;
            _sim.RemoteShot += OnRemoteShot;
            _avatar.Abilities.Activated += OnAbilityActivated;
            _avatar.Abilities.Rejected += OnAbilityRejected;
            ApplyTeamColor();

            if (IsOwner)
            {
                // First person: keep the body for shadows only.
                foreach (Renderer r in _rig.BodyRenderers) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                _rig.VisorRenderer.enabled = false;
                _rig.WeaponVisual.gameObject.SetActive(false);
                GameCamera.Ensure().AttachLocal(this);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (_rig == null || !_rig.HasVisuals) return;
            _avatar.TeamValue.OnValueChanged -= OnTeamChanged;
            _sim.LocalShotPredicted -= OnLocalShot;
            _sim.RemoteShot -= OnRemoteShot;
            _avatar.Abilities.Activated -= OnAbilityActivated;
            _avatar.Abilities.Rejected -= OnAbilityRejected;
        }

        void OnTeamChanged(Team previous, Team current) => ApplyTeamColor();

        void ApplyTeamColor()
        {
            Color team = TeamColor(_avatar.Team);
            Color body = Color.Lerp(new Color(0.22f, 0.24f, 0.27f), team, 0.25f);
            foreach (Renderer r in _rig.BodyRenderers)
            {
                if (r.sharedMaterial != null) r.sharedMaterial.color = body;
            }
            if (_rig.VisorRenderer.sharedMaterial != null)
            {
                Material m = _rig.VisorRenderer.sharedMaterial;
                m.color = team;
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", team * 1.5f);
            }
        }

        void LateUpdate()
        {
            if (!IsSpawned) return;

            bool alive = _avatar.IsAlive;
            if (!IsOwner)
            {
                // Remote pose: head pitch, lying down when dead.
                _rig.HeadVisual.localRotation = Quaternion.Euler(-_sim.ViewPitch, 0f, 0f);
                _rig.VisualRoot.localRotation = alive ? Quaternion.identity : Quaternion.Euler(-85f, 0f, 0f);
                _rig.VisualRoot.localPosition = alive ? Vector3.zero : new Vector3(0f, 0.25f, 0f);
                _rig.VisualRoot.gameObject.SetActive(alive || _avatar.Team != Team.None);
            }

            MatchManager match = MatchManager.Instance;
            bool carrying = match != null && alive && match.Bomb.Value.actorId == OwnerClientId
                            && match.Bomb.Value.state is BombState.Carried or BombState.Planting;
            _rig.BombVisual.gameObject.SetActive(carrying && !IsOwner);

            if (IsOwner)
            {
                // Reload / equip audio from predicted weapon state transitions.
                WeaponSimState w = _sim.Weapons;
                if (w.IsReloading && !_wasReloading) GameAudio.Play2D(Sfx.Reload, 0.7f);
                if (w.activeSlot != _lastSlot) GameAudio.Play2D(Sfx.Equip, 0.6f);
                _wasReloading = w.IsReloading;
                _lastSlot = w.activeSlot;
            }
        }

        void OnLocalShot(ShotEvent shot)
        {
            WeaponStats stats = _avatar.StatsForSlot(shot.slot);
            Sfx sfx = stats.Suppressed ? Sfx.SuppressedShot : stats.Definition.slot == WeaponSlot.Primary ? Sfx.RifleShot : Sfx.PistolShot;
            GameAudio.Play2D(sfx, stats.Suppressed ? 0.55f : 0.8f);

            GameCamera cam = GameCamera.Instance;
            Vector3 muzzle = cam != null ? cam.MuzzlePosition : _sim.RenderEyePosition;
            cam?.Kick(stats.Definition);

            // Visual-only raycast; the server decides what was actually hit.
            Vector3 origin = _sim.EyePosition;
            Vector3 end = origin + shot.direction * stats.Definition.maxRange;
            if (Physics.Raycast(origin, shot.direction, out RaycastHit hit, stats.Definition.maxRange, GameLayers.ShotMask & ~(1 << GameLayers.Hitbox), QueryTriggerInteraction.Ignore))
            {
                end = hit.point;
                Effects.Instance?.Impact(end, false);
            }
            Effects.Instance?.Tracer(muzzle, end, stats.Suppressed);
            Effects.Instance?.MuzzleFlash(muzzle, stats.Suppressed);
        }

        void OnRemoteShot(Vector3 origin, Vector3 end, byte slot, bool suppressed, bool hitPlayer)
        {
            Vector3 muzzle = _rig.WeaponVisual != null ? _rig.WeaponVisual.position + _rig.WeaponVisual.forward * 0.3f : origin;
            bool rifle = _avatar.WeaponInSlot(slot)?.slot == WeaponSlot.Primary;
            Sfx sfx = suppressed ? Sfx.SuppressedShot : rifle ? Sfx.RifleShot : Sfx.PistolShot;
            float volume = suppressed ? 0.35f : 1f;
            GameAudio.PlayAt(sfx, origin, volume, 1f, suppressed ? 20f : 90f);
            Effects.Instance?.Tracer(muzzle, end, suppressed);
            Effects.Instance?.MuzzleFlash(muzzle, suppressed);
            Effects.Instance?.Impact(end, hitPlayer);
        }

        void OnAbilityActivated(AbilitySlot slot)
        {
            Sfx sfx = slot switch
            {
                AbilitySlot.Utility1 => Sfx.Throw,
                AbilitySlot.Utility2 => Sfx.WallDeploy,
                _ => Sfx.Heal,
            };
            if (IsOwner) GameAudio.Play2D(sfx, 0.8f);
            else GameAudio.PlayAt(sfx, transform.position + Vector3.up, 0.8f);
        }

        void OnAbilityRejected(AbilitySlot slot, AbilityRejectReason reason)
        {
            GameAudio.Play2D(Sfx.Denied, 0.6f);
            HudController.Instance?.ShowToast(AbilityRules.Describe(reason));
        }
    }
}
