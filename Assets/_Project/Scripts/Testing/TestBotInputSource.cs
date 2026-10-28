using BlueHorizon.Abilities;
using BlueHorizon.Core;
using BlueHorizon.Match;
using BlueHorizon.Objectives;
using BlueHorizon.Player;
using UnityEngine;

namespace BlueHorizon.Testing
{
    /// <summary>
    /// Scripted input for automated multiplayer tests (-bot). It produces ordinary input commands, so everything
    /// it does goes through the same server validation as a human player: it follows map routes, fights visible
    /// enemies, carries/plants/defuses the bomb and uses each of Striker's abilities.
    /// It is a test driver, not game AI.
    /// </summary>
    public sealed class TestBotInputSource : IPlayerInputSource
    {
        const float VisionRange = 60f;
        const float AimTolerance = 2.5f;

        readonly PlayerSimulation _sim;
        readonly PlayerAvatar _avatar;
        float _yaw, _pitch;

        BotRoute _route;
        readonly System.Collections.Generic.List<Vector3> _path = new();
        int _pathIndex;
        int _retraceCount;
        int _routeRound = -1;
        MatchPhase _lastPhase;
        Vector3 _lastPosition;
        float _stuckTime;
        bool _jump;
        bool _usedSmoke, _usedWall;
        float _phaseTime;
        bool _toggleFire;

        public float Yaw => _yaw;
        public float Pitch => _pitch;

        public TestBotInputSource(PlayerSimulation sim, PlayerAvatar avatar)
        {
            _sim = sim;
            _avatar = avatar;
            BHLog.Info("Bot", "Test bot input active");
        }

        public void SetLook(float yaw, float pitch)
        {
            _yaw = yaw;
            _pitch = pitch;
            _route = null;
        }

        public void UpdateFrame(float deltaTime)
        {
            _phaseTime += deltaTime;
            Vector3 pos = _sim.Position;
            if ((pos - _lastPosition).sqrMagnitude < 0.0004f) _stuckTime += deltaTime;
            else _stuckTime = 0f;
            _lastPosition = pos;
        }

        public void Fill(ref PlayerInputCommand cmd)
        {
            cmd.buttons = InputButtons.None;
            cmd.SetMove(Vector2.zero);

            MatchManager match = MatchManager.Instance;
            string mode = LaunchConfig.Current.botMode;
            if (match == null || !_avatar.IsAlive || mode == "idle" || (mode == "strafe" && match.Phase.Value == MatchPhase.PreRound))
            {
                cmd.yaw = _yaw;
                cmd.pitch = _pitch;
                return;
            }
            if (mode == "ads")
            {
                // Visual review of optics: stand still and hold aim down sights.
                cmd.buttons |= InputButtons.Aim;
                cmd.yaw = _yaw;
                cmd.pitch = _pitch;
                return;
            }

            if (match.Phase.Value != _lastPhase)
            {
                _lastPhase = match.Phase.Value;
                _phaseTime = 0f;
                if (_lastPhase == MatchPhase.PreRound)
                {
                    _usedSmoke = _usedWall = false;
                    _route = null;
                }
            }

            Side side = match.SideOf(_avatar.Team);
            BombNetState bomb = match.Bomb.Value;
            Vector3 eye = _sim.EyePosition;

            // Combat first.
            if (mode == "strafe")
            {
                // Moving target for hit-registration tests: walk to its site, then strafe left/right at run speed.
                // Never shoots and ignores the bomb.
                Vector3? post = match.Phase.Value == MatchPhase.Live ? FollowRoute(side, (int)(_avatar.OwnerClientId % 2), match) : null;
                if (post.HasValue)
                {
                    Vector3 flat = post.Value - _sim.Position;
                    flat.y = 0f;
                    _yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
                    cmd.SetMove(Vector2.up);
                }
                else if (match.Phase.Value == MatchPhase.Live)
                {
                    int dir = ((int)(Time.time / 0.9f) & 1) == 0 ? 1 : -1;
                    cmd.SetMove(new Vector2(dir, 0f));
                }
                Finish(ref cmd);
                return;
            }

            PlayerAvatar enemy = mode == "passive" ? null : FindVisibleEnemy(eye);
            if (enemy != null)
            {
                Vector3 aimPoint = enemy.transform.position + Vector3.up * 1.2f;
                LookAt(eye, aimPoint);
                Vector3 view = Quaternion.Euler(-_pitch, _yaw, 0f) * Vector3.forward;
                // Weapons with an optic aim down sights; scoped ones only fire once fully zoomed.
                Weapons.WeaponStats stats = _avatar.StatsForSlot(_sim.ActiveSlot);
                bool scoped = stats.Magnified || stats.Definition?.category == Weapons.WeaponCategory.SniperRifle;
                if (scoped || stats.Optic != null) cmd.buttons |= InputButtons.Aim;
                bool steady = !scoped || _sim.Weapons.adsProgress >= 0.9f;
                if (steady && Vector3.Angle(view, aimPoint - eye) < AimTolerance)
                {
                    _toggleFire = !_toggleFire;
                    bool automatic = _avatar.WeaponInSlot(_sim.ActiveSlot)?.fireMode == Weapons.FireMode.Automatic;
                    if (automatic || _toggleFire) cmd.buttons |= InputButtons.Fire;
                }
                cmd.buttons |= InputButtons.Crouch;
                Finish(ref cmd);
                return;
            }

            if (_avatar.Health.HealthValue.Value < 80 && match.AbilitiesAllowed)
                cmd.buttons |= InputButtons.Signature;

            if (match.Phase.Value != MatchPhase.Live)
            {
                // Warmup and freeze: hold position, look along the route.
                Finish(ref cmd);
                return;
            }

            ulong me = _avatar.OwnerClientId;
            Vector3? goal = null;
            bool interact = false;

            if (match.FreeForAll)
            {
                goal = Patrol();
            }
            else if (match.Mode is GameModes.CaptureAreaSettings)
            {
                goal = FollowRoute(side, CaptureRouteIndex, match);
                CaptureZone zone = CaptureZone.Main;
                if (goal == null && zone != null && !zone.Contains(_sim.Position)) goal = zone.transform.position;
            }
            else if (side == Side.Attack)
            {
                if (!_usedSmoke && _phaseTime > 1.5f)
                {
                    cmd.buttons |= InputButtons.Ability1;
                    _usedSmoke = true;
                }

                if (bomb.state == BombState.Dropped)
                {
                    goal = bomb.position;
                }
                else if (bomb.state is BombState.Carried or BombState.Planting && bomb.actorId == me)
                {
                    int site = TargetSite(match);
                    if (BombSite.SiteAt(_sim.Position) == site)
                        interact = true;
                    else
                        goal = FollowRoute(Side.Attack, site, match);
                }
                else
                {
                    goal = FollowRoute(Side.Attack, bomb.state is BombState.Planted or BombState.Defusing ? bomb.siteIndex : TargetSite(match), match);
                }
            }
            else if (side == Side.Defense)
            {
                if (bomb.state is BombState.Planted or BombState.Defusing)
                {
                    float dist = Vector3.Distance(_sim.Position, bomb.position);
                    if (dist < 1.2f) interact = true;
                    else goal = dist < 8f ? bomb.position : FollowRoute(Side.Defense, bomb.siteIndex, match) ?? bomb.position;
                }
                else
                {
                    // Duel: both players head for the same site so they meet; HeavyBomb defenders split by id.
                    int site = match.Mode is GameModes.DuelSettings ? TargetSite(match) : (int)(me % 2);
                    goal = FollowRoute(Side.Defense, site, match);
                    if (goal == null && !_usedWall)
                    {
                        // Arrived: deploy the wall beside the site entrance (facing the outer wall keeps it clear of cover).
                        _yaw = site == 0 ? 270f : 90f;
                        _pitch = -30f;
                        cmd.buttons |= InputButtons.Ability2;
                        _usedWall = true;
                    }
                }
            }

            if (interact)
            {
                cmd.buttons |= InputButtons.Interact;
            }
            else if (goal.HasValue)
            {
                Vector3 target = goal.Value;
                Vector3 flat = target - _sim.Position;
                flat.y = 0f;
                if (flat.sqrMagnitude > 0.01f)
                {
                    _yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
                    _pitch = Mathf.MoveTowards(_pitch, -10f, 5f);
                    cmd.SetMove(Vector2.up);
                    if (_stuckTime > 0.6f)
                    {
                        _jump = !_jump;
                        if (_jump) cmd.buttons |= InputButtons.Jump;
                        cmd.SetMove(new Vector2(_stuckTime > 1.2f ? 1f : -1f, 1f));
                    }
                }
            }

            Finish(ref cmd);
        }

        void Finish(ref PlayerInputCommand cmd)
        {
            cmd.yaw = Mathf.Repeat(_yaw, 360f);
            cmd.pitch = _pitch;
        }

        /// <summary>BotRoute.siteIndex used for routes to the capture zone.</summary>
        public const int CaptureRouteIndex = 2;

        BotRoute _patrolRoute;
        int _patrolLife = -1;

        /// <summary>Free-for-all: walk the route closest to where we spawned, then back and forth along it.</summary>
        Vector3? Patrol()
        {
            int life = _avatar.Deaths.Value;
            if (_patrolRoute == null || _patrolLife != life)
            {
                _patrolLife = life;
                _patrolRoute = null;
                float best = float.MaxValue;
                int bestIndex = 0;
                foreach (BotRoute r in BotRoute.All)
                {
                    for (int i = 0; i < r.points.Length; i++)
                    {
                        float d = (r.points[i] - _sim.Position).sqrMagnitude;
                        if (d < best) { best = d; _patrolRoute = r; bestIndex = i; }
                    }
                }
                if (_patrolRoute == null) return null;
                _path.Clear();
                _path.AddRange(_patrolRoute.points);
                _pathIndex = bestIndex;
                _route = null;
            }

            while (true)
            {
                while (_pathIndex < _path.Count)
                {
                    Vector3 p = _path[_pathIndex];
                    Vector3 flat = p - _sim.Position;
                    flat.y = 0f;
                    if (flat.magnitude > 0.9f) return p;
                    _pathIndex++;
                }
                if (_path.Count < 2) return null;
                _path.Reverse();
                _pathIndex = 1;
            }
        }

        static int TargetSite(MatchManager match) =>
            LaunchConfig.Current.botSite >= 0 ? LaunchConfig.Current.botSite : match.RoundNumber.Value % 2 == 1 ? 0 : 1;

        Vector3? FollowRoute(Side side, int site, MatchManager match)
        {
            _patrolRoute = null;
            bool newRound = _routeRound != match.RoundNumber.Value;
            if (_route == null || _route.side != side || _route.siteIndex != site || newRound)
            {
                BotRoute next = null;
                foreach (BotRoute r in BotRoute.All)
                {
                    if (r.side == side && r.siteIndex == site) { next = r; break; }
                }
                if (next == null) return null;

                _path.Clear();
                if (!newRound && _route != null && _route.side == side)
                {
                    // Switching sites mid-round: retrace the current route towards spawn, then take the new one.
                    int reached = Mathf.Clamp(_pathIndex - _retraceCount, 0, _route.points.Length - 1);
                    for (int i = reached; i >= 0; i--) _path.Add(_route.points[i]);
                    _retraceCount = _path.Count;
                    _path.AddRange(next.points);
                    _pathIndex = 0;
                }
                else
                {
                    _retraceCount = 0;
                    _path.AddRange(next.points);
                    float best = float.MaxValue;
                    for (int i = 0; i < _path.Count; i++)
                    {
                        float d = (_path[i] - _sim.Position).sqrMagnitude;
                        if (d < best) { best = d; _pathIndex = i; }
                    }
                }
                _route = next;
                _routeRound = match.RoundNumber.Value;
            }

            while (_pathIndex < _path.Count)
            {
                Vector3 p = _path[_pathIndex];
                Vector3 flat = p - _sim.Position;
                flat.y = 0f;
                if (flat.magnitude > 0.9f) return p;
                _pathIndex++;
            }
            return null;
        }

        PlayerAvatar FindVisibleEnemy(Vector3 eye)
        {
            PlayerAvatar best = null;
            float bestDist = VisionRange;
            foreach (PlayerAvatar other in PlayerAvatar.All)
            {
                if (!other.IsAlive || !MatchManager.Instance.AreEnemies(_avatar, other)) continue;
                Vector3 target = other.transform.position + Vector3.up * 1.2f;
                float dist = Vector3.Distance(eye, target);
                if (dist > bestDist) continue;
                if (Physics.Linecast(eye, target, GameLayers.WorldMask, QueryTriggerInteraction.Ignore)) continue;
                if (SmokeCloud.BlocksLine(eye, target)) continue;
                best = other;
                bestDist = dist;
            }
            return best;
        }

        void LookAt(Vector3 eye, Vector3 point)
        {
            Vector3 d = point - eye;
            _yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            _pitch = Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
        }

        public void Dispose() { }
    }
}
