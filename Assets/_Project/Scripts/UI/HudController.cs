using System.Collections.Generic;
using System.Globalization;
using BlueHorizon.Abilities;
using BlueHorizon.Agents;
using BlueHorizon.Audio;
using BlueHorizon.Combat;
using BlueHorizon.Core;
using BlueHorizon.GameModes;
using BlueHorizon.Match;
using BlueHorizon.Networking;
using BlueHorizon.Objectives;
using BlueHorizon.Player;
using BlueHorizon.Weapons;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace BlueHorizon.UI
{
    /// <summary>
    /// All runtime UI (UI Toolkit): main menu / connection screen, HUD, scoreboard, pause &amp; settings,
    /// round and match results. Reads replicated state only; the only actions it performs are local
    /// (connect, disconnect, settings, quit).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class HudController : MonoBehaviour
    {
        const int MaxKillFeed = 5;
        const float KillFeedLifetime = 6f;

        [SerializeField] StyleSheet styleSheet;

        public static HudController Instance { get; private set; }

        VisualElement _root;
        // Screens
        VisualElement _menu, _connecting, _hud, _scoreboard, _pause, _victory;
        Label _menuStatus, _connectingLabel;
        TextField _nameField, _addressField, _portField;
        Button _reconnectButton;
        // HUD
        Label _scoreAlpha, _scoreBravo, _timer, _phaseLabel, _sideBanner;
        Label _healthValue, _armorLabel, _barrierLabel;
        VisualElement _healthFill, _armorFill, _barrierFill;
        Label _ammoMag, _ammoReserve, _weaponName, _weaponTags, _weaponHint;
        readonly VisualElement[] _abilitySlots = new VisualElement[3];
        readonly Label[] _abilityInfo = new Label[3];
        readonly VisualElement[] _abilityFill = new VisualElement[3];
        VisualElement _crosshair, _crossTop, _crossBottom, _crossLeft, _crossRight;
        VisualElement _hitmarker;
        VisualElement _prompt, _progress, _progressFill;
        Label _promptText;
        VisualElement _announcement;
        Label _announceTitle, _announceSub;
        Label _toast;
        VisualElement _killfeed;
        VisualElement _damageOverlay, _smokeOverlay, _deadOverlay;
        Label _deadBanner;
        // Scoreboard / victory
        VisualElement _scoreboardBody, _victoryBoard;
        float _nextScoreboardRefresh;
        Label _victoryTitle, _victoryScore, _victorySub;
        // Settings
        Slider _sensitivity, _fov, _volume;
        Toggle _invert;

        bool _paused;
        float _hitmarkerUntil;
        float _damageFlash;
        float _toastUntil;
        float _announceUntil;
        readonly List<(VisualElement element, float until)> _feed = new();
        PlayerAvatar _boundLocal;
        MatchPhase _lastPhase = (MatchPhase)255;
        int _lastRound;
        BombState _lastBombState;
        float _nextBeep;
        BombPresenter _bombPresenter;

        void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void Start()
        {
            if (LaunchConfig.Current.role == LaunchRole.Server || PlayerRig.IsHeadless)
            {
                // Dedicated servers and headless test clients have no UI.
                gameObject.SetActive(false);
                return;
            }

            var doc = GetComponent<UIDocument>();
            _root = doc.rootVisualElement;
            if (styleSheet != null) _root.styleSheets.Add(styleSheet);
            _root.AddToClassList("root");

            BuildMenu();
            BuildConnecting();
            BuildHud();
            BuildScoreboard();
            BuildPause();
            BuildVictory();

            MatchManager.KillFeed += OnKillFeed;
            MatchManager.BombExploded += OnBombExploded;
            if (NetworkBootstrap.Instance != null) NetworkBootstrap.Instance.StateChanged += RefreshMenuStatus;
            RefreshMenuStatus();
        }

        void OnDestroy()
        {
            MatchManager.KillFeed -= OnKillFeed;
            MatchManager.BombExploded -= OnBombExploded;
            if (NetworkBootstrap.Instance != null) NetworkBootstrap.Instance.StateChanged -= RefreshMenuStatus;
        }

        // ───────────── Element helpers ─────────────

        static VisualElement Box(VisualElement parent, params string[] classes)
        {
            var e = new VisualElement();
            foreach (string c in classes) e.AddToClassList(c);
            parent?.Add(e);
            return e;
        }

        static Label Text(VisualElement parent, string text, params string[] classes)
        {
            var l = new Label(text);
            foreach (string c in classes) l.AddToClassList(c);
            parent?.Add(l);
            return l;
        }

        static Button MakeButton(VisualElement parent, string text, System.Action onClick, params string[] classes)
        {
            var b = new Button(() =>
            {
                GameAudio.Play2D(Sfx.UiClick, 0.5f);
                onClick();
            }) { text = text };
            foreach (string c in classes) b.AddToClassList(c);
            parent.Add(b);
            return b;
        }

        static void Show(VisualElement e, bool visible) => e.EnableInClassList("hidden", !visible);

        // ───────────── Menu ─────────────

        void BuildMenu()
        {
            _menu = Box(_root, "screen", "menu-screen");

            VisualElement left = Box(_menu, "menu-column");
            Text(left, "BLUE HORIZON", "title");
            Text(left, "TACTICAL OPERATIONS · VERTICAL SLICE", "subtitle");

            VisualElement form = Box(left, "panel");
            Text(form, "CONNECT TO DEDICATED SERVER", "heading");
            Text(form, "CALLSIGN", "field-label");
            _nameField = new TextField { value = PlayerPrefs.GetString("bh.name", "Operator" + Random.Range(100, 999)) , maxLength = 16 };
            form.Add(_nameField);
            Text(form, "SERVER ADDRESS", "field-label");
            _addressField = new TextField { value = PlayerPrefs.GetString("bh.address", LaunchConfig.Current.address) };
            form.Add(_addressField);
            Text(form, "PORT", "field-label");
            _portField = new TextField { value = PlayerPrefs.GetInt("bh.port", LaunchConfig.Current.port).ToString(CultureInfo.InvariantCulture), maxLength = 5 };
            form.Add(_portField);

            MakeButton(form, "CONNECT", OnConnectClicked, "button-primary");
            _reconnectButton = MakeButton(form, "RECONNECT TO LAST MATCH", OnReconnectClicked, "button-primary");
            Show(_reconnectButton, false);
            MakeButton(form, "HOST LOCAL MATCH (DEV)", OnHostClicked, "button-quiet");
            MakeButton(form, "QUIT", () => NetworkBootstrap.Instance?.Quit(0), "button-quiet");
            _menuStatus = Text(form, "", "status");

            VisualElement right = Box(_menu, "menu-column", "menu-column-right");
            AgentDefinition agent = GameContent.Instance != null ? GameContent.Instance.defaultAgent : null;
            if (agent != null)
            {
                VisualElement card = Box(right, "panel", "agent-card");
                Text(card, "AGENT", "heading");
                Text(card, agent.callsign.ToUpperInvariant(), "agent-name");
                Text(card, $"Codename {agent.codename} · {agent.realName} · {agent.origin}", "agent-meta");
                Text(card, agent.biography, "agent-bio");
                AddAbilityRow(card, "Q", agent.utility1);
                AddAbilityRow(card, "E", agent.utility2);
                AddAbilityRow(card, "X", agent.signature);

                VisualElement controls = Box(right, "panel");
                controls.style.marginTop = 12;
                Text(controls, "CONTROLS", "heading");
                Text(controls, "WASD move · Shift sprint · Ctrl/C crouch · Space jump\nLMB fire · R reload · 1/2 weapons · T suppressor (pre-round)\nF plant / defuse · Tab scoreboard · Esc menu", "muted");
            }
        }

        static void AddAbilityRow(VisualElement parent, string key, AbilityDefinition ability)
        {
            if (ability == null) return;
            VisualElement row = Box(parent, "ability-row");
            Text(row, key, "ability-key");
            string charges = ability.maxCharges > 0 ? $"{ability.maxCharges} per round" : $"{ability.cooldown:0}s cooldown";
            Text(row, $"<b>{ability.displayName}</b> — {ability.description} ({charges})", "ability-desc");
        }

        void OnConnectClicked()
        {
            if (!ushort.TryParse(_portField.value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort port) || port == 0)
            {
                _menuStatus.text = "Enter a valid port (1-65535).";
                return;
            }
            string address = _addressField.value.Trim();
            if (string.IsNullOrEmpty(address))
            {
                _menuStatus.text = "Enter a server address.";
                return;
            }
            SaveMenuFields(port);
            LaunchConfig.Current.role = LaunchRole.Client;
            NetworkBootstrap.Instance.StartClient(new ServerEndpoint(address, port), _nameField.value);
        }

        void OnReconnectClicked()
        {
            NetworkBootstrap nb = NetworkBootstrap.Instance;
            if (nb == null || nb.LastEndpoint.Address == null) return;
            LaunchConfig.Current.role = LaunchRole.Client;
            nb.StartClient(nb.LastEndpoint, _nameField.value);
        }

        void OnHostClicked()
        {
            ushort.TryParse(_portField.value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort port);
            if (port == 0) port = LaunchConfig.DefaultPort;
            SaveMenuFields(port);
            LaunchConfig.Current.port = port;
            LaunchConfig.Current.devSoloStart = true;
            NetworkBootstrap.Instance.StartHost(_nameField.value);
        }

        void SaveMenuFields(ushort port)
        {
            PlayerPrefs.SetString("bh.name", _nameField.value);
            PlayerPrefs.SetString("bh.address", _addressField.value.Trim());
            PlayerPrefs.SetInt("bh.port", port);
            PlayerPrefs.Save();
        }

        void RefreshMenuStatus()
        {
            if (_menuStatus == null || NetworkBootstrap.Instance == null) return;
            NetworkBootstrap nb = NetworkBootstrap.Instance;
            _menuStatus.text = nb.ClientState switch
            {
                ClientConnectionState.Disconnected => nb.LastDisconnectReason,
                ClientConnectionState.Connecting => "Connecting…",
                _ => "",
            };
            _menuStatus.EnableInClassList("danger", nb.ClientState == ClientConnectionState.Disconnected);
            if (_reconnectButton != null)
            {
                bool canReconnect = nb.ClientState == ClientConnectionState.Disconnected && nb.LastEndpoint.Address != null;
                Show(_reconnectButton, canReconnect);
                if (canReconnect) _reconnectButton.text = $"RECONNECT TO {nb.LastEndpoint}";
            }
            if (_connectingLabel != null)
                _connectingLabel.text = nb.LastEndpoint.Address != null ? $"CONNECTING TO {nb.LastEndpoint}" : "CONNECTING";
        }

        void BuildConnecting()
        {
            _connecting = Box(_root, "screen", "center-screen", "modal-dim");
            VisualElement panel = Box(_connecting, "panel");
            _connectingLabel = Text(panel, "CONNECTING", "connecting-text");
            Text(panel, "Loading the match…", "muted").style.unityTextAlign = TextAnchor.MiddleCenter;
            MakeButton(panel, "CANCEL", () => NetworkBootstrap.Instance.Disconnect(), "button-quiet");
        }

        // ───────────── HUD ─────────────

        void BuildHud()
        {
            _hud = Box(_root, "screen");
            _smokeOverlay = Box(_hud, "overlay", "smoke-overlay");
            _damageOverlay = Box(_hud, "overlay", "damage-overlay");
            _deadOverlay = Box(_hud, "overlay", "dead-overlay");
            _smokeOverlay.pickingMode = _damageOverlay.pickingMode = _deadOverlay.pickingMode = PickingMode.Ignore;

            VisualElement scorebar = Box(_hud, "scorebar");
            _scoreAlpha = Text(scorebar, "0", "score-team", "score-team-alpha");
            VisualElement center = Box(scorebar, "score-center");
            _timer = Text(center, "0:00", "round-timer");
            _phaseLabel = Text(center, "WARMUP", "phase-label");
            _scoreBravo = Text(scorebar, "0", "score-team", "score-team-bravo");
            _sideBanner = Text(_hud, "", "side-banner");

            _killfeed = Box(_hud, "killfeed");

            VisualElement vitals = Box(_hud, "vitals", "panel");
            VisualElement healthRow = Box(vitals, "health-row");
            _healthValue = Text(healthRow, "125", "health-value");
            Text(healthRow, "HEALTH", "health-label");
            _healthFill = Bar(vitals, "bar-health");
            VisualElement labels = Box(vitals, "bar-label-row");
            _barrierLabel = Text(labels, "BARRIER 25", "bar-label");
            _armorLabel = Text(labels, "ARMOR 25", "bar-label");
            VisualElement shieldRow = Box(vitals);
            shieldRow.style.flexDirection = FlexDirection.Row;
            VisualElement barrierHolder = Box(shieldRow);
            barrierHolder.style.flexGrow = 1;
            barrierHolder.style.marginRight = 4;
            _barrierFill = Bar(barrierHolder, "bar-barrier");
            VisualElement armorHolder = Box(shieldRow);
            armorHolder.style.flexGrow = 1;
            _armorFill = Bar(armorHolder, "bar-armor");

            VisualElement abilities = Box(_hud, "abilities");
            string[] keys = { "Q", "E", "X" };
            for (int i = 0; i < 3; i++)
            {
                VisualElement slot = Box(abilities, "ability");
                _abilityFill[i] = Box(slot, "ability-cooldown-fill");
                Text(slot, keys[i], "ability-slot-key");
                AbilityDefinition def = GameContent.Instance?.defaultAgent?.GetAbility((AbilitySlot)i);
                Text(slot, def != null ? def.displayName.ToUpperInvariant() : "—", "ability-slot-name");
                _abilityInfo[i] = Text(slot, "", "ability-slot-info");
                _abilitySlots[i] = slot;
            }

            VisualElement weapon = Box(_hud, "weapon-block", "panel");
            VisualElement ammo = Box(weapon, "ammo");
            _ammoMag = Text(ammo, "31", "ammo-mag");
            _ammoReserve = Text(ammo, "/ 93", "ammo-reserve");
            _weaponName = Text(weapon, "ASSAULT RIFLE", "weapon-name");
            _weaponTags = Text(weapon, "", "weapon-tags");
            _weaponHint = Text(weapon, "", "weapon-hint");

            _crosshair = Box(_hud, "crosshair");
            _crossTop = Box(_crosshair, "cross-line", "cross-v");
            _crossBottom = Box(_crosshair, "cross-line", "cross-v");
            _crossLeft = Box(_crosshair, "cross-line", "cross-h");
            _crossRight = Box(_crosshair, "cross-line", "cross-h");
            Box(_crosshair, "cross-dot");

            _hitmarker = Box(_hud, "hitmarker");
            for (int i = 0; i < 4; i++)
            {
                VisualElement line = Box(_hitmarker, "hit-line");
                float angle = 45f + 90f * i;
                float rad = angle * Mathf.Deg2Rad;
                line.style.left = Mathf.Cos(rad) * 10f - 6f;
                line.style.top = Mathf.Sin(rad) * 10f - 1f;
                line.style.rotate = new Rotate(new Angle(angle));
            }
            Show(_hitmarker, false);

            _prompt = Box(_hud, "prompt");
            _promptText = Text(_prompt, "", "prompt-text");
            _progress = Box(_prompt, "progress");
            _progressFill = Box(_progress, "progress-fill");

            _announcement = Box(_hud, "announcement");
            _announceTitle = Text(_announcement, "", "announcement-title");
            _announceSub = Text(_announcement, "", "announcement-sub");
            Show(_announcement, false);

            _toast = Text(_hud, "", "toast");
            _deadBanner = Text(_hud, "", "dead-banner");
        }

        static VisualElement Bar(VisualElement parent, string fillClass)
        {
            VisualElement bar = Box(parent, "bar");
            return Box(bar, "bar-fill", fillClass);
        }

        void BuildScoreboard()
        {
            _scoreboard = Box(_root, "screen", "center-screen");
            VisualElement panel = Box(_scoreboard, "panel", "scoreboard");
            Text(panel, "SCOREBOARD", "heading");
            _scoreboardBody = Box(panel);
        }

        void BuildPause()
        {
            _pause = Box(_root, "screen", "center-screen", "modal-dim");
            VisualElement panel = Box(_pause, "panel", "pause");
            Text(panel, "PAUSED", "heading");
            Text(panel, "The match continues while this menu is open.", "muted");

            _sensitivity = AddSlider(panel, "SENSITIVITY", 0.1f, 4f, InputPreferences.Sensitivity, v => InputPreferences.Sensitivity = v, "0.00");
            _fov = AddSlider(panel, "FIELD OF VIEW", 60f, 100f, GameCamera.FieldOfView, v => GameCamera.FieldOfView = v, "0");
            _volume = AddSlider(panel, "VOLUME", 0f, 1f, GameAudio.MasterVolume, v => GameAudio.MasterVolume = v, "0%");

            VisualElement invertRow = Box(panel, "setting-row");
            Text(invertRow, "INVERT LOOK", "setting-label");
            _invert = new Toggle { value = InputPreferences.InvertY };
            _invert.RegisterValueChangedCallback(e => InputPreferences.InvertY = e.newValue);
            invertRow.Add(_invert);

            MakeButton(panel, "RESUME", () => SetPaused(false), "button-primary");
            MakeButton(panel, "DISCONNECT", () =>
            {
                SetPaused(false);
                NetworkBootstrap.Instance.Disconnect();
            }, "button-quiet");
            MakeButton(panel, "QUIT GAME", () => NetworkBootstrap.Instance?.Quit(0), "button-quiet");
        }

        static Slider AddSlider(VisualElement parent, string label, float min, float max, float value, System.Action<float> onChange, string format)
        {
            VisualElement row = Box(parent, "setting-row");
            Text(row, label, "setting-label");
            var slider = new Slider(min, max) { value = value };
            slider.AddToClassList("setting-control");
            row.Add(slider);
            Label valueLabel = Text(row, value.ToString(format, CultureInfo.InvariantCulture), "setting-value");
            slider.RegisterValueChangedCallback(e =>
            {
                onChange(e.newValue);
                valueLabel.text = e.newValue.ToString(format, CultureInfo.InvariantCulture);
                PlayerPrefs.Save();
            });
            return slider;
        }

        void BuildVictory()
        {
            _victory = Box(_root, "screen", "center-screen", "modal-dim", "victory-screen");
            VisualElement panel = Box(_victory);
            panel.style.alignItems = Align.Center;
            _victoryTitle = Text(panel, "", "victory-title");
            _victoryScore = Text(panel, "", "victory-score");
            _victorySub = Text(panel, "Returning to warmup shortly…", "victory-sub");
            VisualElement board = Box(panel, "panel", "scoreboard");
            board.style.marginTop = 24;
            _victoryBoard = Box(board);
        }

        // ───────────── Per-frame ─────────────

        void Update()
        {
            if (_root == null) return;

            NetworkBootstrap nb = NetworkBootstrap.Instance;
            bool inMatch = nb != null && nb.ClientState == ClientConnectionState.Connected && MatchManager.Instance != null;
            bool connecting = nb != null && (nb.ClientState == ClientConnectionState.Connecting
                                             || (nb.ClientState == ClientConnectionState.Connected && MatchManager.Instance == null));
            bool inMenu = !inMatch && !connecting;

            Keyboard kb = Keyboard.current;
            if (inMatch && kb != null && kb.escapeKey.wasPressedThisFrame) SetPaused(!_paused);
            if (!inMatch) _paused = false;

            Show(_menu, inMenu);
            Show(_connecting, connecting);
            Show(_hud, inMatch);
            Show(_pause, inMatch && _paused);

            bool showScoreboard = inMatch && !_paused && kb != null && kb.tabKey.isPressed;
            MatchManager match = MatchManager.Instance;
            bool matchOver = inMatch && match.Phase.Value == MatchPhase.MatchOver;
            Show(_victory, matchOver && !_paused);
            Show(_scoreboard, showScoreboard && !matchOver && !_paused);

            bool cursorFree = !inMatch || _paused;
            InputPreferences.GameplayBlocked = cursorFree;
            UnityEngine.Cursor.lockState = cursorFree ? CursorLockMode.None : CursorLockMode.Locked;
            UnityEngine.Cursor.visible = cursorFree;

            if (!inMatch) return;

            if (_bombPresenter == null) _bombPresenter = BombPresenter.Ensure();
            ZonePresenter.EnsureFor(match);
            BindLocal();
            UpdateScorebar(match);
            UpdatePhaseEvents(match);
            UpdateVitals();
            UpdateWeapon();
            UpdateAbilities();
            UpdateCrosshair();
            UpdatePrompt(match);
            UpdateKillFeed();
            UpdateOverlays();
            if ((showScoreboard || matchOver) && Time.unscaledTime >= _nextScoreboardRefresh)
            {
                // Rebuilt a few times per second, not every frame, to avoid UI allocations.
                _nextScoreboardRefresh = Time.unscaledTime + 0.25f;
                UpdateScoreboard(match, matchOver ? _victoryBoard : _scoreboardBody);
            }
            if (matchOver)
            {
                UpdateVictory(match);
                Show(_announcement, false);
            }
            if (kb != null && kb.spaceKey.wasPressedThisFrame && PlayerAvatar.Local != null && !PlayerAvatar.Local.IsAlive)
                GameCamera.Instance?.CycleSpectate();
        }

        void BindLocal()
        {
            PlayerAvatar local = PlayerAvatar.Local;
            if (local == _boundLocal) return;
            if (_boundLocal != null)
            {
                _boundLocal.HitConfirmed -= OnHitConfirmed;
                _boundLocal.Health.LocalDamageTaken -= OnDamageTaken;
            }
            _boundLocal = local;
            if (local != null)
            {
                local.HitConfirmed += OnHitConfirmed;
                local.Health.LocalDamageTaken += OnDamageTaken;
            }
        }

        static string FormatTime(float seconds)
        {
            int s = Mathf.CeilToInt(Mathf.Max(0f, seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        void UpdateScorebar(MatchManager match)
        {
            GameModeDefinition mode = match.Mode;
            PlayerAvatar local = PlayerAvatar.Local;
            bool ffa = match.FreeForAll;

            if (ffa)
            {
                // Free-for-all: own score on the left, leader on the right.
                PlayerAvatar leader = Leader();
                _scoreAlpha.text = local != null ? local.ObjectiveScore.Value.ToString(CultureInfo.InvariantCulture) : "0";
                _scoreBravo.text = leader != null ? leader.ObjectiveScore.Value.ToString(CultureInfo.InvariantCulture) : "0";
            }
            else
            {
                _scoreAlpha.text = match.ScoreAlpha.Value.ToString(CultureInfo.InvariantCulture);
                _scoreBravo.text = match.ScoreBravo.Value.ToString(CultureInfo.InvariantCulture);
            }

            BombNetState bomb = match.Bomb.Value;
            bool bombLive = match.Phase.Value == MatchPhase.Live && bomb.state is BombState.Planted or BombState.Defusing;
            double now = Unity.Netcode.NetworkManager.Singleton.ServerTime.Time;
            _timer.EnableInClassList("round-timer-bomb", bombLive);
            _timer.text = bombLive ? FormatTime((float)(bomb.detonationTime - now)) : FormatTime(match.PhaseTimeRemaining);
            if (match.Phase.Value == MatchPhase.WaitingForPlayers) _timer.text = "--:--";

            string modeName = mode != null ? mode.displayName.ToUpperInvariant() : "";
            _phaseLabel.text = match.Phase.Value switch
            {
                MatchPhase.WaitingForPlayers => $"{modeName} · WARMUP",
                MatchPhase.Countdown => "MATCH STARTING",
                MatchPhase.PreRound => match.RoundBased ? $"ROUND {match.RoundNumber.Value} · BUY TIME" : $"{modeName} · GET READY",
                MatchPhase.Live => LivePhaseLabel(match, bombLive, bomb),
                MatchPhase.PostRound => "ROUND OVER",
                _ => "MATCH OVER",
            };

            if (local != null)
            {
                string role;
                if (match.IsWarmup) role = "WARMUP";
                else if (mode is HeavyBombSettings) role = match.SideOf(local.Team) == Side.Attack ? "ATTACKING" : "DEFENDING";
                else role = modeName;
                string bombNote = bomb.actorId == local.OwnerClientId && bomb.state == BombState.Carried ? " · YOU CARRY THE BOMB" : "";
                string team = ffa ? "FREE FOR ALL" : $"TEAM {local.Team.DisplayName().ToUpperInvariant()}";
                _sideBanner.text = $"{local.PlayerName.ToUpperInvariant()} · {team} · {role} · {local.Agent?.callsign?.ToUpperInvariant()}{bombNote}";
                _sideBanner.style.color = ffa ? new Color(0.85f, 0.88f, 0.95f) : PlayerPresenter.TeamColor(local.Team);
            }
        }

        static string LivePhaseLabel(MatchManager match, bool bombLive, in BombNetState bomb)
        {
            switch (match.Mode)
            {
                case HeavyBombSettings:
                    return bombLive ? $"BOMB PLANTED · SITE {BombSite.ByIndex(bomb.siteIndex)?.label}" : $"ROUND {match.RoundNumber.Value} · FIRST TO {match.ScoreLimit.Value}";
                case DuelSettings:
                    return $"DUEL · ROUND {match.RoundNumber.Value} · FIRST TO {match.ScoreLimit.Value}";
                case CaptureAreaSettings:
                {
                    ModeStatus z = match.Status.Value;
                    if (z.contested) return "AREA CONTESTED";
                    if (z.capturing != Team.None) return $"{z.capturing.DisplayName().ToUpperInvariant()} CAPTURING {z.progress * 100f:0}%";
                    return z.owner == Team.None ? $"AREA NEUTRAL · FIRST TO {match.ScoreLimit.Value}" : $"{z.owner.DisplayName().ToUpperInvariant()} HOLDS THE AREA";
                }
                case WeaponGameSettings wg:
                {
                    PlayerAvatar local = PlayerAvatar.Local;
                    int level = local != null ? local.ObjectiveScore.Value + 1 : 1;
                    return $"WEAPON GAME · LEVEL {level}/{wg.ladder.Length}";
                }
                default:
                    return $"{match.Mode?.displayName.ToUpperInvariant()} · FIRST TO {match.ScoreLimit.Value}";
            }
        }

        static PlayerAvatar Leader()
        {
            PlayerAvatar best = null;
            foreach (PlayerAvatar a in PlayerAvatar.All)
            {
                if (best == null || a.ObjectiveScore.Value > best.ObjectiveScore.Value
                    || (a.ObjectiveScore.Value == best.ObjectiveScore.Value && a.Deaths.Value < best.Deaths.Value))
                    best = a;
            }
            return best;
        }


        void UpdatePhaseEvents(MatchManager match)
        {
            MatchPhase phase = match.Phase.Value;
            PlayerAvatar local = PlayerAvatar.Local;
            if (phase != _lastPhase || match.RoundNumber.Value != _lastRound)
            {
                Side side = local != null ? match.SideOf(local.Team) : Side.None;
                switch (phase)
                {
                    case MatchPhase.WaitingForPlayers:
                        Announce("WARMUP", "Waiting for players on both teams", new Color(0.7f, 0.85f, 1f), 3f);
                        break;
                    case MatchPhase.Countdown:
                        Announce("MATCH STARTING", "Get ready", new Color(0.7f, 0.85f, 1f), 3f);
                        break;
                    case MatchPhase.PreRound:
                        GameAudio.Play2D(Sfx.RoundStart, 0.6f);
                        if (match.Mode is HeavyBombSettings)
                            Announce($"ROUND {match.RoundNumber.Value}",
                                side == Side.Attack ? "ATTACK · Plant the bomb at site A or B" : "DEFEND · Stop the plant or defuse the bomb",
                                side == Side.Attack ? PlayerPresenter.BravoColor : PlayerPresenter.AlphaColor, 4f);
                        else if (match.RoundBased)
                            Announce($"ROUND {match.RoundNumber.Value}", match.Mode?.description ?? "", new Color(0.7f, 0.85f, 1f), 3f);
                        else
                            Announce(match.Mode?.displayName.ToUpperInvariant() ?? "", match.Mode?.description ?? "", new Color(0.7f, 0.85f, 1f), 4f);
                        break;
                    case MatchPhase.Live:
                        if (_lastPhase == MatchPhase.PreRound) Announce("GO", "", Color.white, 1f);
                        break;
                    case MatchPhase.PostRound:
                        Team winner = match.LastRoundWinner.Value;
                        bool won = local != null && winner == local.Team;
                        bool draw = winner == Team.None;
                        Announce(draw ? "ROUND DRAWN" : won ? "ROUND WON" : "ROUND LOST",
                            $"{(draw ? "NO POINT" : winner.DisplayName().ToUpperInvariant())} · {match.LastRoundReason.Value}",
                            draw ? new Color(0.8f, 0.85f, 0.95f) : won ? new Color(0.45f, 1f, 0.7f) : new Color(1f, 0.45f, 0.4f), 5f);
                        break;
                }
                _lastPhase = phase;
                _lastRound = match.RoundNumber.Value;
            }

            BombState bomb = match.Bomb.Value.state;
            if (bomb != _lastBombState)
            {
                if (bomb == BombState.Planted && _lastBombState == BombState.Planting)
                {
                    GameAudio.Play2D(Sfx.BombPlanted, 0.7f);
                    Announce("BOMB PLANTED", $"Site {BombSite.ByIndex(match.Bomb.Value.siteIndex)?.label}", new Color(1f, 0.4f, 0.3f), 2.5f);
                }
                else if (bomb == BombState.Defused)
                {
                    GameAudio.Play2D(Sfx.BombDefused, 0.7f);
                }
                _lastBombState = bomb;
            }

            if (match.Phase.Value == MatchPhase.Live && bomb is BombState.Planted or BombState.Defusing)
            {
                double now = Unity.Netcode.NetworkManager.Singleton.ServerTime.Time;
                float remaining = (float)(match.Bomb.Value.detonationTime - now);
                if (Time.time >= _nextBeep)
                {
                    GameAudio.PlayAt(Sfx.BombBeep, match.Bomb.Value.position + Vector3.up * 0.3f, 0.8f);
                    _nextBeep = Time.time + Mathf.Lerp(0.12f, 1f, Mathf.Clamp01(remaining / 30f));
                }
            }
        }

        void Announce(string title, string sub, Color color, float duration)
        {
            _announceTitle.text = title;
            _announceTitle.style.color = color;
            _announceSub.text = sub;
            _announceUntil = Time.time + duration;
            Show(_announcement, true);
        }

        public void ShowToast(string message)
        {
            if (_toast == null || string.IsNullOrEmpty(message)) return;
            _toast.text = message;
            _toastUntil = Time.time + 1.6f;
        }

        void UpdateVitals()
        {
            PlayerAvatar local = PlayerAvatar.Local;
            if (local == null) return;
            PlayerHealthView(local.Health);
        }

        void PlayerHealthView(Health.PlayerHealth h)
        {
            int hp = h.HealthValue.Value;
            _healthValue.text = hp.ToString(CultureInfo.InvariantCulture);
            _healthFill.style.width = Length.Percent(100f * hp / Mathf.Max(1, h.MaxHealth));
            _healthFill.EnableInClassList("bar-health-low", hp <= h.MaxHealth * 0.3f);
            _armorFill.style.width = Length.Percent(100f * h.ArmorValue.Value / Mathf.Max(1, h.MaxArmor));
            _barrierFill.style.width = Length.Percent(100f * h.BarrierValue.Value / Mathf.Max(1, h.MaxBarrier));
            _armorLabel.text = $"ARMOR {h.ArmorValue.Value}";
            _barrierLabel.text = $"BARRIER {h.BarrierValue.Value}";
        }

        void UpdateWeapon()
        {
            PlayerAvatar local = PlayerAvatar.Local;
            if (local == null) return;
            WeaponSimState w = local.Simulation.Weapons;
            WeaponDefinition def = local.WeaponInSlot(w.activeSlot);
            short mag = w.Magazine(w.activeSlot);
            _ammoMag.text = w.IsReloading ? "··" : mag.ToString(CultureInfo.InvariantCulture);
            _ammoMag.EnableInClassList("ammo-low", def != null && mag <= def.magazineSize / 4);
            _ammoReserve.text = $"/ {w.Reserve(w.activeSlot)}";
            _weaponName.text = def != null ? def.displayName.ToUpperInvariant() : "";
            bool suppressed = local.HasSuppressor(w.activeSlot);
            _weaponTags.text = (suppressed ? "SUPPRESSED · −20% DMG" : "") + (w.IsReloading ? (suppressed ? " · " : "") + "RELOADING" : "");
            MatchManager match = MatchManager.Instance;
            _weaponHint.text = match != null && match.LoadoutChangesAllowed ? "T · toggle suppressor" : "";
        }

        void UpdateAbilities()
        {
            PlayerAvatar local = PlayerAvatar.Local;
            if (local == null) return;
            MatchManager match = MatchManager.Instance;
            for (int i = 0; i < 3; i++)
            {
                var slot = (AbilitySlot)i;
                AbilityDefinition def = local.Abilities.Definition(slot);
                if (def == null) continue;
                AbilityNetState state = local.Abilities.State(slot);
                float cd = local.Abilities.CooldownRemaining(slot);
                bool hasCharge = def.maxCharges == 0 || state.charges > 0;
                bool available = local.IsAlive && hasCharge && cd <= 0f && (match == null || match.AbilitiesAllowed);
                _abilitySlots[i].EnableInClassList("ability-unavailable", !available);
                _abilityFill[i].style.height = Length.Percent(def.cooldown > 0 ? 100f * Mathf.Clamp01(cd / def.cooldown) : 0f);
                _abilityInfo[i].text = cd > 0f ? $"{cd:0}s" : def.maxCharges > 0 ? $"×{state.charges}" : "READY";
            }
        }

        void UpdateCrosshair()
        {
            PlayerAvatar local = PlayerAvatar.Local;
            bool visible = local != null && local.IsAlive;
            Show(_crosshair, visible);
            if (!visible) return;

            PlayerSimulation sim = local.Simulation;
            WeaponSimState w = sim.Weapons;
            WeaponStats stats = local.StatsForSlot(w.activeSlot);
            if (stats.Definition == null) return;
            Vector3 v = sim.Velocity;
            var ctx = new WeaponContext
            {
                grounded = sim.Grounded,
                crouched = sim.Crouch > 0.5f,
                moveFraction = new Vector2(v.x, v.z).magnitude / Mathf.Max(0.01f, sim.Movement.runSpeed),
            };
            float spread = WeaponSimulation.SpreadAngle(stats.Definition, stats, w.sprayCount, ctx);
            float fov = GameCamera.Instance != null ? GameCamera.Instance.Camera.fieldOfView : 74f;
            float panelHeight = _root.resolvedStyle.height > 0 ? _root.resolvedStyle.height : Screen.height;
            float gap = 4f + Mathf.Tan(spread * Mathf.Deg2Rad) / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * panelHeight * 0.5f;
            _crossTop.style.top = -gap - 9f;
            _crossBottom.style.top = gap;
            _crossLeft.style.left = -gap - 9f;
            _crossRight.style.left = gap;

            if (Time.time > _hitmarkerUntil) Show(_hitmarker, false);
        }

        void UpdatePrompt(MatchManager match)
        {
            PlayerAvatar local = PlayerAvatar.Local;
            BombNetState bomb = match.Bomb.Value;
            string text = "";
            float progress = -1f;
            double now = Unity.Netcode.NetworkManager.Singleton.ServerTime.Time;

            if (local != null && local.IsAlive && match.Phase.Value == MatchPhase.Live)
            {
                Side side = match.SideOf(local.Team);
                Vector3 pos = local.Simulation.Position;
                if ((bomb.state is BombState.Planting or BombState.Defusing) && bomb.actorId == local.OwnerClientId)
                {
                    text = bomb.state == BombState.Planting ? "PLANTING…  keep holding F" : "DEFUSING…  keep holding F";
                    progress = (float)((now - bomb.actionStart) / Mathf.Max(0.01f, (float)(bomb.actionEnd - bomb.actionStart)));
                }
                else if (side == Side.Attack && bomb.state == BombState.Carried && bomb.actorId == local.OwnerClientId)
                {
                    int site = BombSite.SiteAt(pos);
                    text = site >= 0 ? $"Hold F to plant at site {BombSite.ByIndex(site)?.label}" : "You carry the bomb · reach site A or B";
                }
                else if (side == Side.Attack && bomb.state == BombState.Dropped)
                {
                    text = "The bomb is down · walk over it to pick it up";
                }
                else if (side == Side.Defense && bomb.state == BombState.Planted)
                {
                    float d = Vector3.Distance(pos, bomb.position);
                    text = d <= (match.HeavyBomb != null ? match.HeavyBomb.defuseRange : 1.9f) ? "Hold F to defuse" : "Bomb planted · find and defuse it";
                }
                else if (bomb.state == BombState.Defusing && side == Side.Attack)
                {
                    text = "THE BOMB IS BEING DEFUSED";
                }
                else if (match.Mode is CaptureAreaSettings && CaptureZone.Main != null && CaptureZone.Main.Contains(pos))
                {
                    ModeStatus z = match.Status.Value;
                    text = z.contested ? "IN THE AREA · CONTESTED" : z.owner == local.Team ? "IN THE AREA · HOLDING" : "IN THE AREA · CAPTURING";
                    if (!z.contested && z.capturing == local.Team) progress = z.progress;
                }
            }

            Show(_prompt, !string.IsNullOrEmpty(text));
            _promptText.text = text;
            Show(_progress, progress >= 0f);
            if (progress >= 0f) _progressFill.style.width = Length.Percent(100f * Mathf.Clamp01(progress));

            if (Time.time > _announceUntil) Show(_announcement, false);
            _toast.text = Time.time < _toastUntil ? _toast.text : "";
        }

        void UpdateOverlays()
        {
            _damageFlash = Mathf.MoveTowards(_damageFlash, 0f, Time.deltaTime * 1.5f);
            _damageOverlay.style.backgroundColor = new Color(1f, 0.12f, 0.08f, _damageFlash * 0.35f);
            float fog = GameCamera.Instance != null ? GameCamera.Instance.SmokeFog : 0f;
            _smokeOverlay.style.backgroundColor = new Color(0.75f, 0.77f, 0.8f, fog * 0.97f);

            PlayerAvatar local = PlayerAvatar.Local;
            bool dead = local != null && !local.IsAlive;
            Show(_deadOverlay, dead);
            MatchManager match = MatchManager.Instance;
            bool respawning = match != null && (match.IsWarmup || (match.Mode != null && match.Mode.respawnDelay > 0f && match.Phase.Value == MatchPhase.Live));
            _deadBanner.text = !dead ? "" : respawning ? "ELIMINATED · RESPAWNING" : "ELIMINATED · SPECTATING (SPACE TO SWITCH)";
        }

        void UpdateKillFeed()
        {
            for (int i = _feed.Count - 1; i >= 0; i--)
            {
                if (Time.time > _feed[i].until)
                {
                    _feed[i].element.RemoveFromHierarchy();
                    _feed.RemoveAt(i);
                }
            }
        }

        void OnKillFeed(KillFeedEntry entry)
        {
            if (_killfeed == null) return;
            VisualElement row = Box(_killfeed, "killfeed-entry");
            string localName = PlayerAvatar.Local != null ? PlayerAvatar.Local.PlayerName : null;
            if (entry.killer == localName || entry.victim == localName) row.AddToClassList("killfeed-entry-local");
            if (!string.IsNullOrEmpty(entry.killer))
                Text(row, entry.killer, entry.killerTeam == Team.Alpha ? "alpha" : "bravo");
            Text(row, $"[{entry.weapon}{(entry.headshot ? " · HS" : "")}]", "killfeed-weapon");
            Text(row, entry.victim, entry.victimTeam == Team.Alpha ? "alpha" : "bravo");
            _feed.Add((row, Time.time + KillFeedLifetime));
            while (_feed.Count > MaxKillFeed)
            {
                _feed[0].element.RemoveFromHierarchy();
                _feed.RemoveAt(0);
            }
        }

        void OnBombExploded(Vector3 position)
        {
            Effects.Instance?.Explosion(position);
            GameAudio.PlayAt(Sfx.Explosion, position, 1f, 1f, 200f);
        }

        void OnHitConfirmed(HitZone zone, int damage, bool killed, bool structure)
        {
            Show(_hitmarker, true);
            _hitmarker.EnableInClassList("hitmarker-head", zone == HitZone.Head && !structure);
            _hitmarker.EnableInClassList("hitmarker-kill", killed && !structure);
            _hitmarkerUntil = Time.time + (killed ? 0.4f : 0.15f);
            GameAudio.Play2D(killed && !structure ? Sfx.KillConfirm : zone == HitZone.Head ? Sfx.HeadshotMarker : Sfx.HitMarker, 0.7f);
        }

        void OnDamageTaken(Vector3 source, int amount)
        {
            _damageFlash = Mathf.Clamp01(_damageFlash + amount / 60f);
            GameAudio.Play2D(Sfx.Hurt, 0.6f);
        }

        static readonly Team[] ScoreboardTeams = { Team.Alpha, Team.Bravo };

        void UpdateScoreboard(MatchManager match, VisualElement target)
        {
            target.Clear();
            if (match.FreeForAll)
            {
                VisualElement block = Box(target, "scoreboard-team");
                Label title = Text(block, $"{match.Mode?.displayName.ToUpperInvariant()} · FIRST TO {match.ScoreLimit.Value}", "heading");
                ScoreboardHeader(block, match.Mode is WeaponGameSettings ? "LEVEL" : "SCORE");
                var players = new List<PlayerAvatar>(PlayerAvatar.All);
                players.Sort((a, b) => b.ObjectiveScore.Value != a.ObjectiveScore.Value
                    ? b.ObjectiveScore.Value.CompareTo(a.ObjectiveScore.Value)
                    : a.Deaths.Value.CompareTo(b.Deaths.Value));
                bool ladder = match.Mode is WeaponGameSettings;
                foreach (PlayerAvatar a in players) ScoreboardRow(block, a, ladder ? a.ObjectiveScore.Value + 1 : a.ObjectiveScore.Value, "");
                return;
            }

            foreach (Team team in ScoreboardTeams)
            {
                VisualElement block = Box(target, "scoreboard-team");
                int score = team == Team.Alpha ? match.ScoreAlpha.Value : match.ScoreBravo.Value;
                string role = match.IsWarmup ? "WARMUP" : match.Mode is HeavyBombSettings ? match.SideOf(team).DisplayName().ToUpperInvariant() : match.Mode?.displayName.ToUpperInvariant();
                Label title = Text(block, $"TEAM {team.DisplayName().ToUpperInvariant()} · {score} · {role}", "heading");
                title.style.color = PlayerPresenter.TeamColor(team);
                ScoreboardHeader(block, "OBJ");

                foreach (PlayerAvatar a in PlayerAvatar.All)
                {
                    if (a.Team != team) continue;
                    bool carrier = match.Bomb.Value.actorId == a.OwnerClientId && match.Bomb.Value.state == BombState.Carried
                                   && PlayerAvatar.Local != null && PlayerAvatar.Local.Team == a.Team;
                    ScoreboardRow(block, a, a.ObjectiveScore.Value, carrier ? "BOMB" : "");
                }
            }
        }

        static void ScoreboardHeader(VisualElement block, string scoreColumn)
        {
            VisualElement header = Box(block, "scoreboard-header");
            Text(header, "PLAYER", "col-name");
            Text(header, "KILLS", "col-num");
            Text(header, "DEATHS", "col-num");
            Text(header, scoreColumn, "col-num");
            Text(header, "", "col-status");
        }

        static void ScoreboardRow(VisualElement block, PlayerAvatar a, int score, string status)
        {
            VisualElement row = Box(block, "scoreboard-row");
            if (a == PlayerAvatar.Local) row.AddToClassList("scoreboard-row-local");
            if (!a.IsAlive) row.AddToClassList("scoreboard-row-dead");
            Text(row, a.PlayerName, "col-name");
            Text(row, a.Kills.Value.ToString(CultureInfo.InvariantCulture), "col-num");
            Text(row, a.Deaths.Value.ToString(CultureInfo.InvariantCulture), "col-num");
            Text(row, score.ToString(CultureInfo.InvariantCulture), "col-num");
            Text(row, !a.IsAlive ? "DEAD" : status, "col-status");
        }


        void UpdateVictory(MatchManager match)
        {
            PlayerAvatar local = PlayerAvatar.Local;
            string winnerName = match.MatchWinnerName.Value.ToString();
            bool won, draw;
            if (match.FreeForAll)
            {
                won = local != null && winnerName == local.PlayerName;
                draw = string.IsNullOrEmpty(winnerName) || winnerName == "Nobody";
                _victoryScore.text = $"{winnerName.ToUpperInvariant()} WINS";
            }
            else
            {
                Team winner = match.MatchWinner.Value;
                won = local != null && winner == local.Team;
                draw = winner == Team.None;
                _victoryScore.text = $"ALPHA {match.ScoreAlpha.Value} — {match.ScoreBravo.Value} BRAVO";
            }
            _victoryTitle.text = draw ? "DRAW" : won ? "VICTORY" : "DEFEAT";
            _victoryTitle.style.color = draw ? new Color(0.85f, 0.88f, 0.95f) : won ? new Color(0.45f, 1f, 0.7f) : new Color(1f, 0.45f, 0.4f);
            _victorySub.text = $"{match.LastRoundReason.Value} · next match in {Mathf.CeilToInt(match.PhaseTimeRemaining)}s";
        }


        void SetPaused(bool paused)
        {
            _paused = paused;
        }
    }
}
