using System.Collections.Generic;
using System.Text;
using BlueHorizon.Abilities;
using BlueHorizon.Agents;
using BlueHorizon.Core;
using BlueHorizon.Health;
using BlueHorizon.Match;
using BlueHorizon.Player;
using UnityEngine;
using UnityEngine.UIElements;

namespace BlueHorizon.UI
{
    /// <summary>Milestone 5 HUD: flash blindness, EnemyAlert / downed-teammate markers, downed banner and agent selection.</summary>
    public sealed partial class HudController
    {
        const int MaxMarkers = 16;

        VisualElement _flashOverlay, _downedOverlay;
        readonly List<VisualElement> _markers = new();
        readonly List<Label> _markerLabels = new();
        readonly Label[] _abilityName = new Label[3];
        // Agent selection (inside the loadout panel)
        VisualElement _agentBody;
        int _draftAgent;
        bool _wasDowned;

        void OnAbilityBurst(AbilityBurst kind, Vector3 position)
        {
            if (kind == AbilityBurst.Flash)
            {
                Effects.Instance?.FlashBurst(position);
                Audio.GameAudio.PlayAt(Audio.Sfx.FlashBang, position, 1f, 1f, 90f);
            }
            else
            {
                Effects.Instance?.Pulse(position, 30f);
            }
        }

        void BuildAgentHud()
        {
            _flashOverlay = Box(_hud, "overlay", "flash-overlay");
            _downedOverlay = Box(_hud, "overlay", "downed-overlay");
            _flashOverlay.pickingMode = _downedOverlay.pickingMode = PickingMode.Ignore;
            // Overlays sit below the HUD widgets (inserted after the damage/dead overlays).
            _hud.Insert(_hud.IndexOf(_deadOverlay) + 1, _downedOverlay);
            _hud.Insert(_hud.IndexOf(_deadOverlay) + 1, _flashOverlay);
            Show(_downedOverlay, false);

            for (int i = 0; i < MaxMarkers; i++)
            {
                VisualElement marker = Box(_hud, "world-marker");
                marker.pickingMode = PickingMode.Ignore;
                Box(marker, "world-marker-icon");
                _markerLabels.Add(Text(marker, "", "world-marker-label"));
                Show(marker, false);
                _markers.Add(marker);
            }
        }

        void BindAgentEvents(PlayerAvatar previous, PlayerAvatar local)
        {
            if (previous != null)
            {
                previous.Abilities.Spotted -= OnSpotted;
                previous.AgentRejected -= OnAgentRejected;
            }
            if (local != null)
            {
                local.Abilities.Spotted += OnSpotted;
                local.AgentRejected += OnAgentRejected;
            }
        }

        void OnSpotted(float seconds) => ShowToast($"Spotted by EnemyAlert ({seconds:0}s)");

        void OnAgentRejected(AgentRejectReason reason) => ShowToast(AgentRules.Describe(reason));

        void UpdateAgentHud(MatchManager match)
        {
            PlayerAvatar local = PlayerAvatar.Local;

            // Flash blindness: presentation only (the server sends intensity and duration to the victim).
            float white = 0f;
            if (local != null && local.IsAlive)
            {
                AbilityController a = local.Abilities;
                white = FlashRules.Whiteness(a.FlashIntensity, a.FlashDuration, a.FlashElapsed);
            }
            _flashOverlay.style.backgroundColor = new Color(1f, 1f, 0.97f, white);

            bool downed = local != null && local.IsAlive && local.Health.IsDowned;
            Show(_downedOverlay, downed);
            if (downed != _wasDowned && local != null && local.IsAlive)
                Audio.GameAudio.Play2D(downed ? Audio.Sfx.Downed : Audio.Sfx.Revive, 0.7f);
            _wasDowned = downed;

            UpdateMarkers(match, local);
        }

        void UpdateMarkers(MatchManager match, PlayerAvatar local)
        {
            int used = 0;
            Camera cam = GameCamera.Instance != null ? GameCamera.Instance.Camera : null;
            if (cam != null && local != null && _root.panel != null)
            {
                foreach (PlayerAvatar a in PlayerAvatar.All)
                {
                    if (used >= MaxMarkers) break;
                    if (a == local || !a.IsSpawned || !a.IsAlive) continue;
                    bool enemy = match.AreEnemies(local, a);
                    string label;
                    string kind;
                    if (enemy && a.Abilities.IsRevealed)
                    {
                        kind = "world-marker-enemy";
                        label = $"{Vector3.Distance(local.Simulation.Position, a.Simulation.Position):0} m";
                    }
                    else if (!enemy && a.Health.IsDowned)
                    {
                        kind = "world-marker-downed";
                        DownedNetState d = a.Health.DownedValue.Value;
                        double now = Unity.Netcode.NetworkManager.Singleton.ServerTime.Time;
                        label = d.BeingRevived ? $"REVIVING {d.ReviveProgress(now) * 100f:0}%" : $"DOWN {d.BleedoutRemaining(now):0}s";
                    }
                    else continue;

                    Vector3 world = a.Simulation.Position + Vector3.up * (a.Health.IsDowned ? 1.3f : 2.1f);
                    if (cam.WorldToViewportPoint(world).z <= 0f) continue;
                    Vector2 p = RuntimePanelUtils.CameraTransformWorldToPanel(_root.panel, world, cam);
                    VisualElement m = _markers[used];
                    m.style.left = p.x - 40f;
                    m.style.top = p.y - 22f;
                    m.EnableInClassList("world-marker-enemy", kind == "world-marker-enemy");
                    m.EnableInClassList("world-marker-downed", kind == "world-marker-downed");
                    _markerLabels[used].text = label;
                    Show(m, true);
                    used++;
                }
            }
            for (int i = used; i < _markers.Count; i++) Show(_markers[i], false);
        }

        /// <summary>Prompt text for downed players and revivers; null when neither applies.</summary>
        string DownedPrompt(PlayerAvatar local, out float progress)
        {
            progress = -1f;
            double now = Unity.Netcode.NetworkManager.Singleton.ServerTime.Time;
            if (local.Health.IsDowned)
            {
                DownedNetState d = local.Health.DownedValue.Value;
                if (d.BeingRevived)
                {
                    progress = d.ReviveProgress(now);
                    string by = PlayerAvatar.TryGet(d.reviverId, out PlayerAvatar r) ? r.PlayerName : "A teammate";
                    return $"{by.ToUpperInvariant()} IS REVIVING YOU";
                }
                return $"DOWNED · bleeding out in {d.BleedoutRemaining(now):0}s · crawl to cover, wait for a teammate";
            }

            foreach (PlayerAvatar a in PlayerAvatar.All)
            {
                if (a == local || !a.IsAlive || !a.Health.IsDowned || a.Team != local.Team || a.Team == Team.None) continue;
                DownedNetState d = a.Health.DownedValue.Value;
                if (d.reviverId == local.OwnerClientId)
                {
                    progress = d.ReviveProgress(now);
                    return $"REVIVING {a.PlayerName.ToUpperInvariant()}…  keep holding F";
                }
                float range = MatchManager.Instance != null && MatchManager.Instance.Mode != null ? MatchManager.Instance.Mode.reviveRange : 1.8f;
                if (Vector3.Distance(a.Simulation.Position, local.Simulation.Position) <= range)
                    return $"Hold F to revive {a.PlayerName}";
            }
            return null;
        }

        void UpdateAbilityNames(PlayerAvatar local)
        {
            for (int i = 0; i < 3; i++)
            {
                if (_abilityName[i] == null) continue;
                AbilityDefinition def = local != null ? local.Abilities.Definition((AbilitySlot)i) : null;
                string text = def != null ? def.displayName.ToUpperInvariant() : "—";
                if (_abilityName[i].text != text) _abilityName[i].text = text;
            }
        }

        // ───────────── Agent selection (loadout panel) ─────────────

        void OpenAgentDraft(PlayerAvatar local)
        {
            GameContent content = GameContent.Instance;
            int saved = LocalAgent.SavedIndex(content);
            _draftAgent = saved >= 0 ? saved : local.AgentIndexValue.Value;
        }

        void RebuildAgentBody()
        {
            _agentBody.Clear();
            GameContent content = GameContent.Instance;
            MatchManager match = MatchManager.Instance;
            if (content == null || content.agents == null) return;

            VisualElement section = Box(_agentBody, "loadout-slot");
            Text(section, "AGENT", "loadout-slot-title");
            VisualElement row = Box(section, "loadout-choices");
            for (int i = 0; i < content.agents.Length; i++)
            {
                AgentDefinition agent = content.agents[i];
                if (agent == null) continue;
                AgentRejectReason reason = match != null ? match.CheckAgent(i) : AgentRejectReason.None;
                string suffix = reason == AgentRejectReason.Locked ? " · LOCKED" : reason == AgentRejectReason.Banned ? " · BANNED" : "";
                int index = i;
                Button b = MakeButton(row, agent.DisplayName.ToUpperInvariant() + suffix, () =>
                {
                    _draftAgent = index;
                    RebuildAgentBody();
                }, "loadout-choice");
                b.EnableInClassList("loadout-choice-selected", _draftAgent == i);
                b.SetEnabled(reason == AgentRejectReason.None);
            }

            AgentDefinition chosen = content.AgentByIndex(_draftAgent);
            if (chosen == null) return;
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(chosen.role)) sb.Append(chosen.role).Append("  ·  ");
            sb.Append($"{chosen.health.maxHealth} HP / {chosen.health.maxArmor} armor / {chosen.health.maxBarrier} barrier");
            Text(section, sb.ToString(), "loadout-stats");
            string[] keys = { "Q", "E", "X" };
            for (int s = 0; s < 3; s++)
            {
                AbilityDefinition ability = chosen.GetAbility((AbilitySlot)s);
                if (ability == null) continue;
                string uses = ability.maxCharges > 0 ? $"{ability.maxCharges} per round" : $"{ability.cooldown:0}s cooldown";
                Text(section, $"{keys[s]}  {ability.displayName} — {ability.description} ({uses})", "loadout-stats");
            }
        }

        void ApplyAgentDraft(PlayerAvatar local)
        {
            GameContent content = GameContent.Instance;
            if (content == null || content.agents == null || _draftAgent < 0 || _draftAgent >= content.agents.Length) return;
            LocalAgent.Save(content.agents[_draftAgent]);
            local.RequestAgentRpc((byte)_draftAgent);
        }
    }
}
