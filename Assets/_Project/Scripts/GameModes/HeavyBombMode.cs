using System.Collections.Generic;
using BlueHorizon.Combat;
using BlueHorizon.Core;
using BlueHorizon.Match;
using BlueHorizon.Objectives;
using BlueHorizon.Player;
using UnityEngine;

namespace BlueHorizon.GameModes
{
    /// <summary>HeavyBomb rules: bomb carrier selection, plant/defuse interaction, detonation and round evaluation.</summary>
    public sealed class HeavyBombMode : GameModeRules
    {
        const float ExplosionRadius = 12f;

        readonly HeavyBombSettings _settings;
        readonly BombStateMachine _bomb;
        readonly System.Random _random = new();
        int _attackersAtStart, _defendersAtStart;

        public HeavyBombMode(HeavyBombSettings settings) : base(settings)
        {
            _settings = settings;
            _bomb = new BombStateMachine(settings.plantDuration, settings.defuseDuration, settings.fuseDuration);
        }

        public override string Summary =>
            $"HeavyBomb: round {_settings.timeLimit}s, plant {_settings.plantDuration}s, defuse {_settings.defuseDuration}s, fuse {_settings.fuseDuration}s, first to {_settings.scoreLimit}";

        Side SideOf(Team team) => team == Team.None ? Side.None : team == Host.AttackingTeam ? Side.Attack : Side.Defense;

        int CountSide(Side side, bool aliveOnly)
        {
            int n = 0;
            foreach (PlayerAvatar a in Host.Players)
            {
                if (!a.IsSpawned || (aliveOnly && !a.IsAlive)) continue;
                if (SideOf(a.Team) == side) n++;
            }
            return n;
        }

        public override void OnRoundStarting()
        {
            Host.AttackingTeam = MatchRules.AttackingTeamForRound(Host.RoundNumber, _settings.roundsPerHalf);
        }

        public override void OnRoundStarted()
        {
            var attackers = new List<PlayerAvatar>();
            foreach (PlayerAvatar a in Host.Players)
                if (a.IsSpawned && a.IsAlive && SideOf(a.Team) == Side.Attack) attackers.Add(a);

            if (attackers.Count > 0)
            {
                PlayerAvatar carrier = attackers[_random.Next(attackers.Count)];
                _bomb.AssignTo(carrier.OwnerClientId);
                BHLog.Info("Bomb", $"'{carrier.PlayerName}' carries the bomb");
            }
            else
            {
                _bomb.Deactivate();
            }
            Publish();
        }

        public override void OnLive()
        {
            _attackersAtStart = CountSide(Side.Attack, aliveOnly: true);
            _defendersAtStart = CountSide(Side.Defense, aliveOnly: true);
        }

        public override ModeOutcome TickLive(double now, float deltaTime, bool timeExpired)
        {
            UpdateBomb(now);
            var snapshot = new RoundSnapshot
            {
                attackersAlive = CountSide(Side.Attack, aliveOnly: true),
                defendersAlive = CountSide(Side.Defense, aliveOnly: true),
                attackersAtStart = _attackersAtStart,
                defendersAtStart = _defendersAtStart,
                bomb = _bomb.State,
                roundTimeExpired = timeExpired,
            };
            RoundResult result = HeavyBombRules.Evaluate(snapshot);
            if (!result.ended) return ModeOutcome.Continue;

            Team winner = result.winner == Side.Attack ? Host.AttackingTeam : Host.AttackingTeam.Other();
            return ModeOutcome.Round(winner, $"{result.winner.DisplayName()} · {HeavyBombRules.Describe(result.reason)}");
        }

        public override void OnRoundEnded()
        {
            // Freeze the bomb where it is; it no longer ticks after the round ends.
            if (_bomb.State == BombState.Planting) _bomb.CancelPlant();
            if (_bomb.State == BombState.Defusing) _bomb.CancelDefuse();
            Publish();
        }

        public override void OnReset()
        {
            _bomb.Deactivate();
            Publish();
        }

        public override void OnPlayerKilled(PlayerAvatar victim, PlayerAvatar killer, in DamageInfo info)
        {
            ulong id = victim.OwnerClientId;
            if (_bomb.State is BombState.Carried or BombState.Planting && _bomb.CarrierId == id)
            {
                _bomb.Drop(victim.Simulation.Position);
                BHLog.Info("Bomb", $"Carrier '{victim.PlayerName}' died; bomb dropped");
            }
            else if (_bomb.State == BombState.Defusing && _bomb.ActorId == id)
            {
                _bomb.CancelDefuse();
                BHLog.Info("Bomb", "Defuser died; defuse cancelled");
            }
            Publish();
        }

        public override void OnPlayerLeft(PlayerAvatar avatar)
        {
            ulong id = avatar.OwnerClientId;
            if (_bomb.State is BombState.Carried or BombState.Planting && _bomb.CarrierId == id)
            {
                _bomb.Drop(avatar.Simulation.Position);
                BHLog.Info("Bomb", $"Carrier '{avatar.PlayerName}' left; bomb dropped");
            }
            else if (_bomb.State == BombState.Defusing && _bomb.ActorId == id)
            {
                _bomb.CancelDefuse();
                BHLog.Info("Bomb", $"Defuser '{avatar.PlayerName}' left; defuse cancelled");
            }
            Publish();
        }

        // ───────────── Bomb ─────────────

        void UpdateBomb(double now)
        {
            switch (_bomb.State)
            {
                case BombState.Dropped:
                    foreach (PlayerAvatar a in Host.Players)
                    {
                        if (!a.IsAlive || SideOf(a.Team) != Side.Attack) continue;
                        if ((a.Simulation.Position - _bomb.Position).sqrMagnitude <= _settings.pickupRange * _settings.pickupRange
                            && _bomb.TryPickUp(a.OwnerClientId, true, true) == BombRejectReason.None)
                        {
                            BHLog.Info("Bomb", $"'{a.PlayerName}' picked up the bomb");
                            break;
                        }
                    }
                    break;

                case BombState.Carried:
                    if (PlayerAvatar.TryGet(_bomb.CarrierId, out PlayerAvatar carrier) && carrier.Simulation.InteractHeld && IsSteady(carrier))
                    {
                        int site = BombSite.SiteAt(carrier.Simulation.Position);
                        BombRejectReason r = _bomb.TryStartPlant(carrier.OwnerClientId, SideOf(carrier.Team) == Side.Attack, carrier.IsAlive,
                            site, carrier.Simulation.Position, now);
                        if (r == BombRejectReason.None)
                            BHLog.Info("Bomb", $"'{carrier.PlayerName}' started planting at site {BombSite.ByIndex(site)?.label}");
                    }
                    break;

                case BombState.Planting:
                    if (!PlayerAvatar.TryGet(_bomb.ActorId, out PlayerAvatar planter) || !planter.IsAlive || !planter.Simulation.InteractHeld
                        || !IsSteady(planter) || BombSite.SiteAt(planter.Simulation.Position) != _bomb.SiteIndex)
                    {
                        _bomb.CancelPlant();
                        BHLog.Info("Bomb", "Plant interrupted");
                    }
                    break;

                case BombState.Planted:
                    TryStartDefuse(now);
                    break;

                case BombState.Defusing:
                    if (!PlayerAvatar.TryGet(_bomb.ActorId, out PlayerAvatar defuser) || !defuser.IsAlive || !defuser.Simulation.InteractHeld
                        || !IsSteady(defuser) || Vector3.Distance(defuser.Simulation.Position, _bomb.Position) > _settings.defuseRange)
                    {
                        _bomb.CancelDefuse();
                        BHLog.Info("Bomb", "Defuse interrupted");
                    }
                    break;
            }

            switch (_bomb.Tick(now))
            {
                case BombEvent.Planted:
                    BHLog.Info("Bomb", $"Bomb planted at site {BombSite.ByIndex(_bomb.SiteIndex)?.label}; detonation in {_settings.fuseDuration}s");
                    CreditClosest(_bomb.Position, Side.Attack);
                    break;
                case BombEvent.Defused:
                    BHLog.Info("Bomb", "Bomb defused");
                    if (PlayerAvatar.TryGet(_bomb.ActorId, out PlayerAvatar d)) d.ServerAddObjectiveScore(1);
                    break;
                case BombEvent.Exploded:
                    BHLog.Info("Bomb", $"Bomb exploded at {_bomb.Position}");
                    Explode(_bomb.Position);
                    break;
            }
            Publish();
        }

        void TryStartDefuse(double now)
        {
            PlayerAvatar best = null;
            float bestDist = float.MaxValue;
            foreach (PlayerAvatar a in Host.Players)
            {
                if (!a.IsAlive || SideOf(a.Team) != Side.Defense || !a.Simulation.InteractHeld || !IsSteady(a)) continue;
                float dist = Vector3.Distance(a.Simulation.Position, _bomb.Position);
                if (dist < bestDist)
                {
                    best = a;
                    bestDist = dist;
                }
            }
            if (best == null) return;
            if (_bomb.TryStartDefuse(best.OwnerClientId, true, best.IsAlive, bestDist, _settings.defuseRange, now) == BombRejectReason.None)
                BHLog.Info("Bomb", $"'{best.PlayerName}' started defusing");
        }

        bool IsSteady(PlayerAvatar a)
        {
            Vector3 v = a.Simulation.Velocity;
            return a.Simulation.Grounded && new Vector2(v.x, v.z).magnitude <= _settings.maxInteractSpeed;
        }

        void CreditClosest(Vector3 position, Side side)
        {
            PlayerAvatar closest = null;
            float best = float.MaxValue;
            foreach (PlayerAvatar a in Host.Players)
            {
                if (SideOf(a.Team) != side) continue;
                float d = (a.Simulation.Position - position).sqrMagnitude;
                if (d < best) { best = d; closest = a; }
            }
            closest?.ServerAddObjectiveScore(1);
        }

        void Explode(Vector3 position)
        {
            Host.BroadcastExplosion(position);
            var victims = new List<PlayerAvatar>();
            foreach (PlayerAvatar a in Host.Players)
                if (a.IsAlive && Vector3.Distance(a.Simulation.Position, position) <= ExplosionRadius) victims.Add(a);
            foreach (PlayerAvatar a in victims)
            {
                a.Health.ServerKill(new DamageInfo
                {
                    attackerClientId = ulong.MaxValue,
                    amount = 9999,
                    source = DamageSource.Explosion,
                    weaponSlot = 255,
                    origin = position,
                    point = a.Simulation.Position,
                });
            }
        }

        void Publish()
        {
            Host.PublishBomb(new BombNetState
            {
                state = _bomb.State,
                actorId = _bomb.State is BombState.Carried ? _bomb.CarrierId : _bomb.ActorId,
                siteIndex = _bomb.SiteIndex,
                position = _bomb.Position,
                actionStart = _bomb.ActionStartTime,
                actionEnd = _bomb.ActionEndTime,
                detonationTime = _bomb.DetonationTime,
            });
        }
    }
}
