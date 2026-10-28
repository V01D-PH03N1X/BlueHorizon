using System.Collections.Generic;
using System.Text;
using BlueHorizon.Attachments;
using BlueHorizon.Core;
using BlueHorizon.Match;
using BlueHorizon.Player;
using BlueHorizon.Weapons;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace BlueHorizon.UI
{
    /// <summary>Player-chosen reticle look for red-dot and holographic sights (PlayerPrefs).</summary>
    public static class ReticlePreferences
    {
        public enum Shape { Optic, Dot, RingDot, Cross }

        public static readonly (string name, Color color)[] Colors =
        {
            ("OPTIC", Color.clear),
            ("RED", new Color(1f, 0.16f, 0.12f)),
            ("GREEN", new Color(0.3f, 1f, 0.35f)),
            ("CYAN", new Color(0.3f, 0.95f, 1f)),
            ("YELLOW", new Color(1f, 0.9f, 0.25f)),
            ("WHITE", Color.white),
        };

        public static int ColorIndex
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt("bh.reticleColor", 0), 0, Colors.Length - 1);
            set => PlayerPrefs.SetInt("bh.reticleColor", Mathf.Clamp(value, 0, Colors.Length - 1));
        }

        public static Shape ReticleShape
        {
            get => (Shape)Mathf.Clamp(PlayerPrefs.GetInt("bh.reticleShape", 0), 0, 3);
            set => PlayerPrefs.SetInt("bh.reticleShape", (int)value);
        }

        public static Color Resolve(AttachmentDefinition optic)
        {
            int i = ColorIndex;
            return i == 0 && optic != null ? optic.reticleColor : i == 0 ? Colors[1].color : Colors[i].color;
        }

        public static Shape Resolve(ReticleStyle style)
        {
            Shape s = ReticleShape;
            if (s != Shape.Optic) return s;
            return style == ReticleStyle.Holographic ? Shape.RingDot : Shape.Dot;
        }
    }

    public sealed partial class HudController
    {
        // Optics
        VisualElement _reticle, _reticleDot, _reticleRing, _reticleCross;
        VisualElement _scope, _scopeLens;
        Texture2D _scopeTexture;
        // Loadout
        VisualElement _loadout, _loadoutBody;
        Label _loadoutStatus;
        PlayerLoadout _draft;
        bool _loadoutOpen;
        bool _shownLoadoutOnce;

        bool LoadoutOpen => _loadoutOpen;

        // ───────────── Optics ─────────────

        void BuildOptics()
        {
            // Directly above the screen overlays, below every HUD widget (ammo and health stay readable while scoped).
            _scope = Box(null, "overlay", "scope-overlay");
            _hud.Insert(_hud.IndexOf(_deadOverlay) + 1, _scope);
            _scope.pickingMode = PickingMode.Ignore;
            Box(_scope, "scope-bar").pickingMode = PickingMode.Ignore;
            _scopeLens = Box(_scope, "scope-lens");
            _scopeLens.pickingMode = PickingMode.Ignore;
            _scopeTexture = BuildScopeTexture(512);
            _scopeLens.style.backgroundImage = new StyleBackground(_scopeTexture);
            Box(_scopeLens, "scope-line", "scope-line-h");
            Box(_scopeLens, "scope-line", "scope-line-v");
            Box(_scopeLens, "scope-center");
            Box(_scope, "scope-bar").pickingMode = PickingMode.Ignore;
            Show(_scope, false);

            _reticle = Box(_hud, "reticle");
            _reticle.pickingMode = PickingMode.Ignore;
            _reticleRing = Box(_reticle, "reticle-ring");
            _reticleCross = Box(_reticle, "reticle-cross");
            Box(_reticleCross, "reticle-cross-h");
            Box(_reticleCross, "reticle-cross-v");
            _reticleDot = Box(_reticle, "reticle-dot");
            Show(_reticle, false);
        }

        /// <summary>Black mask with a round, slightly vignetted lens in the middle.</summary>
        static Texture2D BuildScopeTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "ScopeMask" };
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - half) * (x + 0.5f - half) + (y + 0.5f - half) * (y + 0.5f - half)) / half;
                    float edge = Mathf.SmoothStep(0.86f, 0.97f, d);
                    float alpha = d >= 0.97f ? 1f : Mathf.Max(edge, Mathf.SmoothStep(0.5f, 0.97f, d) * 0.35f);
                    pixels[y * size + x] = new Color32(0, 0, 0, (byte)(alpha * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        void UpdateOptics()
        {
            GameCamera cam = GameCamera.Instance;
            PlayerAvatar local = PlayerAvatar.Local;
            bool alive = cam != null && local != null && local.IsAlive;
            WeaponStats stats = alive ? cam.ActiveStats : default;
            float ads = alive ? cam.AdsBlend : 0f;

            bool scoped = alive && cam.Scoped;
            Show(_scope, scoped);
            if (scoped)
            {
                float h = _root.resolvedStyle.height > 0 ? _root.resolvedStyle.height : Screen.height;
                _scopeLens.style.width = h;
                _scopeLens.style.height = h;
            }

            bool reflex = alive && stats.Optic != null && !stats.Optic.magnified && stats.Reticle != ReticleStyle.None && ads > 0.35f;
            Show(_reticle, reflex);
            if (reflex)
            {
                Color c = ReticlePreferences.Resolve(stats.Optic);
                ReticlePreferences.Shape shape = ReticlePreferences.Resolve(stats.Reticle);
                _reticle.style.opacity = Mathf.InverseLerp(0.35f, 0.9f, ads);
                _reticleDot.style.backgroundColor = c;
                _reticleRing.style.borderTopColor = _reticleRing.style.borderBottomColor =
                    _reticleRing.style.borderLeftColor = _reticleRing.style.borderRightColor = c;
                foreach (VisualElement line in _reticleCross.Children()) line.style.backgroundColor = c;
                Show(_reticleRing, shape == ReticlePreferences.Shape.RingDot);
                Show(_reticleCross, shape == ReticlePreferences.Shape.Cross);
                Show(_reticleDot, shape != ReticlePreferences.Shape.Cross);
            }
        }

        // ───────────── Reticle settings (pause menu) ─────────────

        void BuildReticleSettings(VisualElement panel)
        {
            VisualElement colorRow = Box(panel, "setting-row");
            Text(colorRow, "RETICLE COLOR", "setting-label");
            var colorField = new DropdownField(ColorNames(), ReticlePreferences.ColorIndex);
            colorField.AddToClassList("setting-control");
            colorField.RegisterValueChangedCallback(_ =>
            {
                ReticlePreferences.ColorIndex = colorField.index;
                PlayerPrefs.Save();
            });
            colorRow.Add(colorField);

            VisualElement shapeRow = Box(panel, "setting-row");
            Text(shapeRow, "RETICLE SHAPE", "setting-label");
            var shapeField = new DropdownField(new List<string> { "OPTIC DEFAULT", "DOT", "RING + DOT", "CROSS" }, (int)ReticlePreferences.ReticleShape);
            shapeField.AddToClassList("setting-control");
            shapeField.RegisterValueChangedCallback(_ =>
            {
                ReticlePreferences.ReticleShape = (ReticlePreferences.Shape)shapeField.index;
                PlayerPrefs.Save();
            });
            shapeRow.Add(shapeField);
        }

        static List<string> ColorNames()
        {
            var names = new List<string>();
            foreach (var (name, _) in ReticlePreferences.Colors) names.Add(name);
            return names;
        }

        // ───────────── Loadout panel (B) ─────────────

        void BuildLoadout()
        {
            _loadout = Box(_root, "screen", "center-screen", "modal-dim");
            VisualElement panel = Box(_loadout, "panel", "loadout");
            Text(panel, "LOADOUT", "heading");
            _loadoutBody = Box(panel);
            _loadoutStatus = Text(panel, "", "muted", "loadout-status");
            VisualElement buttons = Box(panel, "loadout-buttons");
            MakeButton(buttons, "APPLY", ApplyLoadout, "button-primary", "loadout-button");
            MakeButton(buttons, "CLOSE", () => SetLoadoutOpen(false), "button-quiet", "loadout-button");
            Show(_loadout, false);
        }

        void HandleLoadoutKey(Keyboard kb, bool inMatch)
        {
            if (!inMatch)
            {
                _loadoutOpen = false;
                return;
            }
            if (LaunchConfig.Current.showLoadout && !_shownLoadoutOnce && PlayerAvatar.Local != null && PlayerAvatar.Local.IsAlive
                && Time.timeSinceLevelLoad > 6f)
            {
                _shownLoadoutOnce = true;
                SetLoadoutOpen(true);
            }
            if (kb == null) return;
            if (kb.bKey.wasPressedThisFrame && !_paused) SetLoadoutOpen(!_loadoutOpen);
            if (_loadoutOpen && kb.escapeKey.wasPressedThisFrame) SetLoadoutOpen(false);
            if (_loadoutOpen) RefreshLoadoutStatus(); // The phase can change while the panel is open.
        }

        void SetLoadoutOpen(bool open)
        {
            PlayerAvatar local = PlayerAvatar.Local;
            if (open && local == null) return;
            _loadoutOpen = open;
            if (open)
            {
                _draft = LocalLoadout.TryGetSaved(out PlayerLoadout saved) ? saved : local.LoadoutValue.Value;
                GameContent content = GameContent.Instance;
                if (content != null) _draft = LoadoutRules.Sanitize(_draft, content.weapons, content.attachments);
                RebuildLoadoutBody();
            }
        }

        void ApplyLoadout()
        {
            PlayerAvatar local = PlayerAvatar.Local;
            if (local == null) return;
            LocalLoadout.Save(_draft);
            local.RequestLoadoutRpc(_draft);
            MatchManager match = MatchManager.Instance;
            ShowToast(match != null && match.LoadoutChangesAllowed ? "Loadout applied" : "Loadout applies on your next spawn");
            SetLoadoutOpen(false);
        }

        void RebuildLoadoutBody()
        {
            _loadoutBody.Clear();
            GameContent content = GameContent.Instance;
            MatchManager match = MatchManager.Instance;
            if (content == null) return;

            RefreshLoadoutStatus();

            for (int slot = 0; slot < 2; slot++)
            {
                VisualElement column = Box(_loadoutBody, "loadout-slot");
                Text(column, slot == 0 ? "PRIMARY" : "SECONDARY", "loadout-slot-title");
                VisualElement weaponRow = Box(column, "loadout-choices");
                for (int i = 0; i < content.weapons.Length; i++)
                {
                    WeaponDefinition w = content.weapons[i];
                    if (w == null || (int)w.slot != slot) continue;
                    int slotCopy = slot, index = i;
                    Choice(weaponRow, w.displayName.ToUpperInvariant(), _draft.Slot(slot) == i, () =>
                    {
                        if (_draft.Slot(slotCopy) == index) return;
                        _draft.SetSlot(slotCopy, (byte)index);
                        _draft = LoadoutRules.Sanitize(_draft, content.weapons, content.attachments);
                        RebuildLoadoutBody();
                    });
                }

                WeaponDefinition weapon = content.WeaponByIndex(_draft.Slot(slot));
                if (weapon == null) continue;
                Text(column, WeaponSummary(weapon), "loadout-stats");
                for (int a = 0; a < PlayerLoadout.AttachmentSlots; a++)
                {
                    var attachmentSlot = (AttachmentSlot)a;
                    VisualElement row = Box(column, "loadout-attachment-row");
                    Text(row, attachmentSlot.ToString().ToUpperInvariant(), "loadout-attachment-label");
                    VisualElement choices = Box(row, "loadout-choices");
                    if (attachmentSlot == AttachmentSlot.Optic && weapon.fixedOptic != null)
                    {
                        Text(choices, $"{weapon.fixedOptic.displayName.ToUpperInvariant()} (BUILT IN)", "muted");
                        continue;
                    }
                    int slotCopy = slot;
                    byte current = _draft.Attachment(slot, attachmentSlot);
                    bool any = false;
                    foreach (AttachmentDefinition att in weapon.allowedAttachments)
                    {
                        if (!LoadoutRules.Allows(weapon, att, attachmentSlot)) continue;
                        if (!any)
                        {
                            Choice(choices, "NONE", current == PlayerLoadout.None, () => SetDraftAttachment(slotCopy, attachmentSlot, PlayerLoadout.None));
                            any = true;
                        }
                        byte index = content.IndexOfAttachment(att);
                        Choice(choices, att.displayName.ToUpperInvariant(), current == index, () => SetDraftAttachment(slotCopy, attachmentSlot, index));
                    }
                    if (!any) Text(choices, "—", "muted");
                }
            }
        }

        void RefreshLoadoutStatus()
        {
            MatchManager match = MatchManager.Instance;
            bool fixedByMode = match != null && match.Mode != null && !match.Mode.loadoutChangesEnabled;
            string text = fixedByMode
                ? $"{match.Mode.displayName} hands out the weapons. Your choice is saved for other modes."
                : match != null && match.LoadoutChangesAllowed
                    ? "Changes apply immediately (warmup / buy phase)."
                    : "Changes apply on your next spawn.";
            if (_loadoutStatus.text != text) _loadoutStatus.text = text;
        }

        void SetDraftAttachment(int slot, AttachmentSlot attachmentSlot, byte index)
        {
            _draft.SetAttachment(slot, attachmentSlot, index);
            RebuildLoadoutBody();
        }

        static void Choice(VisualElement parent, string text, bool selected, System.Action onClick)
        {
            Button b = MakeButton(parent, text, onClick, "loadout-choice");
            b.EnableInClassList("loadout-choice-selected", selected);
        }

        static string WeaponSummary(WeaponDefinition w)
        {
            var sb = new StringBuilder();
            sb.Append(w.pellets > 1 ? $"{w.damage.torso}×{w.pellets} body" : $"{w.damage.head} head · {w.damage.torso} body · {w.damage.legs} legs");
            sb.Append($" · {w.fireRate:0.##}/s · {w.magazineSize} rds");
            if (w.fireMode == FireMode.Automatic) sb.Append(" · auto");
            return sb.ToString();
        }

        /// <summary>Weapon tags line: fitted attachments and state.</summary>
        static string WeaponTags(PlayerAvatar local, int slot, bool reloading)
        {
            var sb = new StringBuilder();
            WeaponStats stats = local.StatsForSlot(slot);
            for (int a = 0; a < PlayerLoadout.AttachmentSlots; a++)
            {
                AttachmentDefinition att = a == (int)AttachmentSlot.Optic ? stats.Optic : local.AttachmentIn(slot, (AttachmentSlot)a);
                if (att == null) continue;
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(att.displayName.ToUpperInvariant());
            }
            if (stats.Suppressed) sb.Append(" (−20% DMG)");
            if (reloading) sb.Append(sb.Length > 0 ? " · RELOADING" : "RELOADING");
            return sb.ToString();
        }
    }
}
