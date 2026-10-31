using BlueHorizon.Combat;
using BlueHorizon.Core;
using BlueHorizon.Player;
using Unity.Netcode;
using UnityEngine;

namespace BlueHorizon.Abilities
{
    /// <summary>
    /// Deployable destructible wall. Blocks movement (Deployable layer collides with characters) and bullets
    /// (it is in the shot mask and takes damage through its own hitbox). The server owns health and lifetime.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class Wayblocker : NetworkBehaviour, IDamageable
    {
        public readonly NetworkVariable<int> HealthValue = new();
        public readonly NetworkVariable<int> MaxHealth = new();
        public readonly NetworkVariable<Vector3> Size = new();
        public readonly NetworkVariable<Team> TeamValue = new();
        public readonly NetworkVariable<double> ExpireTime = new();

        Renderer _panel;
        Material _panelMaterial;

        public Team DamageTeam => TeamValue.Value;
        public bool CanTakeDamage => IsSpawned && HealthValue.Value > 0;

        WayblockerDefinition _definition;
        Team _pendingTeam;
        double _pendingExpire;

        /// <summary>Server, before Spawn().</summary>
        public void Configure(WayblockerDefinition definition, Team team, double now)
        {
            _definition = definition;
            _pendingTeam = team;
            _pendingExpire = now + definition.lifetime;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer && _definition != null)
            {
                HealthValue.Value = _definition.maxHealth;
                MaxHealth.Value = _definition.maxHealth;
                Size.Value = _definition.size;
                TeamValue.Value = _pendingTeam;
                ExpireTime.Value = _pendingExpire;
            }
            gameObject.layer = GameLayers.Deployable;
            var box = GetComponent<BoxCollider>();
            box.size = Size.Value;
            var hitbox = GetComponent<HitboxCollider>();
            if (hitbox == null) hitbox = gameObject.AddComponent<HitboxCollider>();
            hitbox.zone = HitZone.Torso;
            hitbox.owner = this;
            // Character controllers on clients must collide with the wall immediately for prediction.
            Physics.SyncTransforms();

            if (!PlayerRig.IsHeadless) BuildVisual();
            HealthValue.OnValueChanged += OnHealthChanged;
        }

        public override void OnNetworkDespawn()
        {
            HealthValue.OnValueChanged -= OnHealthChanged;
            if (_panelMaterial != null) Destroy(_panelMaterial);
        }

        void BuildVisual()
        {
            GameContent content = GameContent.Instance;
            GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            DestroyImmediate(panel.GetComponent<Collider>());
            panel.transform.SetParent(transform, false);
            panel.transform.localScale = Size.Value;
            _panel = panel.GetComponent<Renderer>();
            if (content != null && content.emissiveMaterial != null)
            {
                _panelMaterial = new Material(content.emissiveMaterial);
                _panel.sharedMaterial = _panelMaterial;
            }
            UpdateColor();

            // Frame posts for readability.
            for (int side = -1; side <= 1; side += 2)
            {
                GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cube);
                DestroyImmediate(post.GetComponent<Collider>());
                post.transform.SetParent(transform, false);
                post.transform.localPosition = new Vector3(side * Size.Value.x * 0.5f, 0f, 0f);
                post.transform.localScale = new Vector3(0.12f, Size.Value.y + 0.05f, Size.Value.z + 0.1f);
                if (content != null && content.litMaterial != null)
                {
                    var r = post.GetComponent<Renderer>();
                    r.sharedMaterial = new Material(content.litMaterial) { color = new Color(0.15f, 0.16f, 0.18f) };
                }
            }
        }

        void OnHealthChanged(int previous, int current) => UpdateColor();

        void UpdateColor()
        {
            if (_panelMaterial == null) return;
            float t = MaxHealth.Value > 0 ? Mathf.Clamp01(HealthValue.Value / (float)MaxHealth.Value) : 1f;
            Color healthy = TeamValue.Value == Team.Alpha ? new Color(0.25f, 0.6f, 1f) : new Color(1f, 0.45f, 0.25f);
            _panelMaterial.color = Color.Lerp(new Color(0.9f, 0.15f, 0.1f), healthy, t);
            if (_panelMaterial.HasProperty("_EmissionColor"))
                _panelMaterial.SetColor("_EmissionColor", _panelMaterial.color * 0.6f);
        }

        public int ServerApplyDamage(in DamageInfo info, out bool killed)
        {
            killed = false;
            if (!IsServer || HealthValue.Value <= 0) return 0;
            int applied = Mathf.Min(info.amount, HealthValue.Value);
            HealthValue.Value -= applied;
            if (HealthValue.Value <= 0)
            {
                killed = true;
                BHLog.Info("Ability", $"Wayblocker destroyed at {transform.position}");
                NetworkObject.Despawn(true);
            }
            return applied;
        }

        void Update()
        {
            if (IsServer && IsSpawned && NetworkManager.ServerTime.Time >= ExpireTime.Value)
            {
                BHLog.Info("Ability", $"Wayblocker expired at {transform.position}");
                NetworkObject.Despawn(true);
            }
        }
    }
}
