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
            gameObject.AddComponent<LaserSight>().Initialize(_avatar, _rig);

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

        /// <summary>Shot sound by weapon category; pitch and loudness come from the weapon data.</summary>
        static Sfx ShotSound(WeaponDefinition weapon, bool suppressed) =>
            suppressed ? Sfx.SuppressedShot
            : weapon != null && weapon.category is WeaponCategory.Pistol or WeaponCategory.Revolver ? Sfx.PistolShot
            : Sfx.RifleShot;

        void OnLocalShot(ShotEvent shot)
        {
            WeaponStats stats = _avatar.StatsForSlot(shot.slot);
            WeaponDefinition weapon = stats.Definition;
            GameAudio.Play2D(ShotSound(weapon, stats.Suppressed), (stats.Suppressed ? 0.55f : 0.8f) * weapon.fireSoundVolume, weapon.fireSoundPitch);

            GameCamera cam = GameCamera.Instance;
            Vector3 muzzle = cam != null ? cam.MuzzlePosition : _sim.RenderEyePosition;
            cam?.Kick(weapon);

            // Visual-only raycasts (one per pellet, same deterministic directions as the server); the server decides hits.
            Vector3 origin = _sim.EyePosition;
            int pellets = Mathf.Max(1, (int)shot.pellets);
            for (int i = 0; i < pellets; i++)
            {
                Vector3 dir = WeaponSimulation.PelletDirection(shot, i);
                Vector3 end = origin + dir * weapon.maxRange;
                if (Physics.Raycast(origin, dir, out RaycastHit hit, weapon.maxRange, GameLayers.ShotMask & ~(1 << GameLayers.Hitbox), QueryTriggerInteraction.Ignore))
                {
                    end = hit.point;
                    Effects.Instance?.Impact(end, false);
                }
                Effects.Instance?.Tracer(muzzle, end, stats.Suppressed || pellets > 1);
            }
            Effects.Instance?.MuzzleFlash(muzzle, stats.Suppressed);
        }

        void OnRemoteShot(Vector3 origin, Vector3 end, byte slot, bool suppressed, bool hitPlayer)
        {
            Vector3 muzzle = _rig.WeaponVisual != null ? _rig.WeaponVisual.position + _rig.WeaponVisual.forward * 0.3f : origin;
            WeaponStats stats = _avatar.StatsForSlot(slot);
            WeaponDefinition weapon = stats.Definition;
            float volume = (suppressed ? 0.35f : 1f) * (weapon != null ? weapon.fireSoundVolume : 1f);
            GameAudio.PlayAt(ShotSound(weapon, suppressed), origin, volume, weapon != null ? weapon.fireSoundPitch : 1f, suppressed ? 20f : 90f);
            Effects.Instance?.Tracer(muzzle, end, suppressed);
            if (weapon != null && weapon.pellets > 1)
            {
                // Only the first pellet's end point is replicated; the others are drawn as an approximate cone.
                Vector3 dir = (end - origin).normalized;
                float length = Vector3.Distance(origin, end);
                for (int i = 1; i < weapon.pellets; i++)
                {
                    Vector3 spread = Quaternion.Euler(Random.Range(-weapon.pelletSpread, weapon.pelletSpread), Random.Range(-weapon.pelletSpread, weapon.pelletSpread), 0f)
                                     * Vector3.forward;
                    Effects.Instance?.Tracer(muzzle, origin + Quaternion.LookRotation(dir) * spread * length, true);
                }
            }
            Effects.Instance?.MuzzleFlash(muzzle, suppressed);
            Effects.Instance?.Impact(end, hitPlayer);
        }

        void OnAbilityActivated(AbilitySlot slot)
        {
            Sfx sfx = _avatar.Abilities.Definition(slot) switch
            {
                ThrownGrenadeDefinition => Sfx.Throw,
                WayblockerDefinition => Sfx.WallDeploy,
                EnemyAlertDefinition => Sfx.ScanPulse,
                RevivalDefinition => Sfx.Revive,
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
