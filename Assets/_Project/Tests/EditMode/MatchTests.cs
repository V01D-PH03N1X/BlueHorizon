using System.Collections.Generic;
using BlueHorizon.Core;
using BlueHorizon.GameModes;
using BlueHorizon.Match;
using BlueHorizon.Networking;
using BlueHorizon.Objectives;
using NUnit.Framework;
using UnityEngine;

namespace BlueHorizon.Tests
{
    public sealed class BombStateMachineTests
    {
        const ulong Carrier = 1, OtherAttacker = 2, Defender = 3;
        BombStateMachine _bomb;

        [SetUp]
        public void SetUp()
        {
            _bomb = new BombStateMachine(plantDuration: 4f, defuseDuration: 7f, fuseDuration: 40f);
            _bomb.AssignTo(Carrier);
        }

        void Plant(double at = 10)
        {
            Assert.AreEqual(BombRejectReason.None, _bomb.TryStartPlant(Carrier, true, true, 0, Vector3.zero, at));
            Assert.AreEqual(BombEvent.Planted, _bomb.Tick(at + 4));
        }

        [Test]
        public void Plant_CompletesAfterDuration_AndStartsFuse()
        {
            Assert.AreEqual(BombRejectReason.None, _bomb.TryStartPlant(Carrier, true, true, 1, Vector3.one, 10));
            Assert.AreEqual(BombState.Planting, _bomb.State);
            Assert.AreEqual(BombEvent.None, _bomb.Tick(13.9));
            Assert.AreEqual(BombEvent.Planted, _bomb.Tick(14.0));
            Assert.AreEqual(BombState.Planted, _bomb.State);
            Assert.AreEqual(1, _bomb.SiteIndex);
            Assert.AreEqual(54.0, _bomb.DetonationTime, 1e-6);
        }

        [TestCase(OtherAttacker, true, true, 0, BombRejectReason.NotCarrier)]
        [TestCase(Carrier, false, true, 0, BombRejectReason.WrongSide)]
        [TestCase(Carrier, true, false, 0, BombRejectReason.Dead)]
        [TestCase(Carrier, true, true, -1, BombRejectReason.NotInSite)]
        public void Plant_InvalidRequests_AreRejectedWithoutSideEffects(ulong player, bool attacker, bool alive, int site, BombRejectReason expected)
        {
            Assert.AreEqual(expected, _bomb.TryStartPlant(player, attacker, alive, site, Vector3.zero, 0));
            Assert.AreEqual(BombState.Carried, _bomb.State);
        }

        [Test]
        public void Plant_DuplicateRequest_IsRejected()
        {
            _bomb.TryStartPlant(Carrier, true, true, 0, Vector3.zero, 0);
            Assert.AreEqual(BombRejectReason.WrongState, _bomb.TryStartPlant(Carrier, true, true, 0, Vector3.zero, 1));
        }

        [Test]
        public void DeathDuringPlanting_CancelsPlant_AndDropsBomb()
        {
            _bomb.TryStartPlant(Carrier, true, true, 0, Vector3.zero, 0);
            Assert.IsTrue(_bomb.Drop(new Vector3(5, 0, 5)));
            Assert.AreEqual(BombState.Dropped, _bomb.State);
            Assert.AreEqual(BombEvent.None, _bomb.Tick(100), "A cancelled plant never completes");
            Assert.AreEqual(BombRejectReason.WrongSide, _bomb.TryPickUp(Defender, false, true));
            Assert.AreEqual(BombRejectReason.None, _bomb.TryPickUp(OtherAttacker, true, true));
            Assert.AreEqual(OtherAttacker, _bomb.CarrierId);
        }

        [Test]
        public void CancelledPlant_ReturnsBombToCarrier()
        {
            _bomb.TryStartPlant(Carrier, true, true, 0, Vector3.zero, 0);
            Assert.IsTrue(_bomb.CancelPlant());
            Assert.AreEqual(BombState.Carried, _bomb.State);
            Assert.AreEqual(Carrier, _bomb.CarrierId);
        }

        [Test]
        public void Defuse_CompletesAfterDuration()
        {
            Plant();
            Assert.AreEqual(BombRejectReason.None, _bomb.TryStartDefuse(Defender, true, true, 1f, 1.9f, 20));
            Assert.AreEqual(BombEvent.None, _bomb.Tick(26.9));
            Assert.AreEqual(BombEvent.Defused, _bomb.Tick(27));
            Assert.AreEqual(BombState.Defused, _bomb.State);
        }

        [Test]
        public void Defuse_InvalidRequests_AreRejected()
        {
            Assert.AreEqual(BombRejectReason.WrongState, _bomb.TryStartDefuse(Defender, true, true, 0.5f, 1.9f, 0), "Not planted");
            Plant();
            Assert.AreEqual(BombRejectReason.WrongSide, _bomb.TryStartDefuse(OtherAttacker, false, true, 0.5f, 1.9f, 15));
            Assert.AreEqual(BombRejectReason.Dead, _bomb.TryStartDefuse(Defender, true, false, 0.5f, 1.9f, 15));
            Assert.AreEqual(BombRejectReason.OutOfRange, _bomb.TryStartDefuse(Defender, true, true, 2.5f, 1.9f, 15));
            _bomb.TryStartDefuse(Defender, true, true, 0.5f, 1.9f, 15);
            Assert.AreEqual(BombRejectReason.Busy, _bomb.TryStartDefuse(4, true, true, 0.5f, 1.9f, 15), "Second defuser");
        }

        [Test]
        public void DefuseInterrupted_LosesProgress_FuseContinues()
        {
            Plant(); // detonation at 54
            _bomb.TryStartDefuse(Defender, true, true, 1f, 1.9f, 20);
            Assert.IsTrue(_bomb.CancelDefuse());
            Assert.AreEqual(BombState.Planted, _bomb.State);
            _bomb.TryStartDefuse(Defender, true, true, 1f, 1.9f, 25);
            Assert.AreEqual(BombEvent.None, _bomb.Tick(31.9), "Restarted from zero");
            Assert.AreEqual(BombEvent.Defused, _bomb.Tick(32));
        }

        [Test]
        public void Bomb_Explodes_AtDetonationTime()
        {
            Plant(); // detonation at 54
            Assert.AreEqual(BombEvent.None, _bomb.Tick(53.99));
            Assert.AreEqual(BombEvent.Exploded, _bomb.Tick(54));
            Assert.AreEqual(BombState.Exploded, _bomb.State);
            Assert.AreEqual(BombRejectReason.WrongState, _bomb.TryStartDefuse(Defender, true, true, 0f, 2f, 55));
        }

        [Test]
        public void TieBreak_DefuseFinishingExactlyAtDetonation_Wins()
        {
            Plant(); // detonation at 54
            _bomb.TryStartDefuse(Defender, true, true, 1f, 1.9f, 47); // ends at 54
            Assert.AreEqual(BombEvent.Defused, _bomb.Tick(54));
        }

        [Test]
        public void TieBreak_DefuseThatWouldFinishLate_Explodes()
        {
            Plant(); // detonation at 54
            _bomb.TryStartDefuse(Defender, true, true, 1f, 1.9f, 50); // would end at 57
            Assert.AreEqual(BombEvent.Exploded, _bomb.Tick(54));
        }

        [Test]
        public void Inactive_RejectsEverything()
        {
            _bomb.Deactivate();
            Assert.AreEqual(BombRejectReason.WrongState, _bomb.TryStartPlant(Carrier, true, true, 0, Vector3.zero, 0));
            Assert.AreEqual(BombRejectReason.WrongState, _bomb.TryPickUp(Carrier, true, true));
            Assert.IsFalse(_bomb.Drop(Vector3.zero));
            Assert.AreEqual(BombEvent.None, _bomb.Tick(1000));
        }
    }

    public sealed class HeavyBombRulesTests
    {
        static RoundSnapshot Live(int atk, int def, BombState bomb = BombState.Carried, bool expired = false) => new()
        {
            attackersAlive = atk, defendersAlive = def, attackersAtStart = 5, defendersAtStart = 5, bomb = bomb, roundTimeExpired = expired,
        };

        static void Expect(RoundSnapshot s, Side winner, RoundEndReason reason)
        {
            RoundResult r = HeavyBombRules.Evaluate(s);
            Assert.IsTrue(r.ended);
            Assert.AreEqual(winner, r.winner);
            Assert.AreEqual(reason, r.reason);
        }

        [Test] public void Continue_WhileBothSidesAlive() => Assert.IsFalse(HeavyBombRules.Evaluate(Live(3, 2)).ended);
        [Test] public void BombExploded_AttackersWin() => Expect(Live(0, 5, BombState.Exploded), Side.Attack, RoundEndReason.BombExploded);
        [Test] public void BombDefused_DefendersWin() => Expect(Live(5, 1, BombState.Defused), Side.Defense, RoundEndReason.BombDefused);
        [Test] public void AttackersEliminatedBeforePlant_DefendersWin() => Expect(Live(0, 3), Side.Defense, RoundEndReason.AttackersEliminated);
        [Test] public void DefendersEliminated_AttackersWin() => Expect(Live(2, 0), Side.Attack, RoundEndReason.DefendersEliminated);
        [Test] public void DefendersEliminatedAfterPlant_AttackersWin() => Expect(Live(0, 0, BombState.Planted), Side.Attack, RoundEndReason.DefendersEliminated);
        [Test] public void TimeoutBeforePlant_DefendersWin() => Expect(Live(5, 5, BombState.Carried, expired: true), Side.Defense, RoundEndReason.TimeExpired);
        [Test] public void TimeoutDuringPlanting_DefendersWin() => Expect(Live(5, 5, BombState.Planting, expired: true), Side.Defense, RoundEndReason.TimeExpired);
        [Test] public void SimultaneousEliminationBeforePlant_DefendersWin() => Expect(Live(0, 0), Side.Defense, RoundEndReason.AttackersEliminated);

        [Test]
        public void AttackersEliminatedAfterPlant_RoundContinues_TimerIgnored()
        {
            Assert.IsFalse(HeavyBombRules.Evaluate(Live(0, 3, BombState.Planted, expired: true)).ended);
            Assert.IsFalse(HeavyBombRules.Evaluate(Live(0, 3, BombState.Defusing, expired: true)).ended);
        }

        [Test]
        public void ReducedDevTeams_EmptySideIsNeverEliminated()
        {
            var s = new RoundSnapshot { attackersAlive = 1, defendersAlive = 0, attackersAtStart = 1, defendersAtStart = 0, bomb = BombState.Carried };
            Assert.IsFalse(HeavyBombRules.Evaluate(s).ended);
            s.roundTimeExpired = true;
            Expect(s, Side.Defense, RoundEndReason.TimeExpired);
        }
    }

    public sealed class MatchRulesTests
    {
        [Test]
        public void Sides_SwapEveryHalf()
        {
            Assert.AreEqual(Team.Alpha, MatchRules.AttackingTeamForRound(1, 6));
            Assert.AreEqual(Team.Alpha, MatchRules.AttackingTeamForRound(6, 6));
            Assert.AreEqual(Team.Bravo, MatchRules.AttackingTeamForRound(7, 6));
            Assert.AreEqual(Team.Bravo, MatchRules.AttackingTeamForRound(12, 6));
            Assert.AreEqual(Team.Alpha, MatchRules.AttackingTeamForRound(13, 6));
        }

        [Test]
        public void MatchWinner_FirstToRoundsToWin()
        {
            Assert.AreEqual(Team.None, MatchRules.MatchWinner(6, 6, 7));
            Assert.AreEqual(Team.Alpha, MatchRules.MatchWinner(7, 5, 7));
            Assert.AreEqual(Team.Bravo, MatchRules.MatchWinner(3, 7, 7));
        }

        [Test]
        public void TeamAssignment_BalancesTeams_AndRejectsWhenFull()
        {
            Assert.AreEqual(Team.Alpha, TeamAssignment.ChooseTeam(0, 0, 5));
            Assert.AreEqual(Team.Bravo, TeamAssignment.ChooseTeam(1, 0, 5));
            Assert.AreEqual(Team.Alpha, TeamAssignment.ChooseTeam(1, 1, 5));
            Assert.AreEqual(Team.Alpha, TeamAssignment.ChooseTeam(4, 5, 5));
            Assert.AreEqual(Team.Bravo, TeamAssignment.ChooseTeam(5, 3, 5));
            Assert.AreEqual(Team.None, TeamAssignment.ChooseTeam(5, 5, 5));
        }

        [Test]
        public void SpawnSelector_SkipsOccupiedPoints_AndWraps()
        {
            var points = new List<Vector3> { new(0, 0, 0), new(4, 0, 0), new(8, 0, 0) };
            Assert.AreEqual(0, SpawnSelector.Select(points, new List<Vector3>(), 0, 1.2f));
            Assert.AreEqual(1, SpawnSelector.Select(points, new List<Vector3> { new(0.3f, 0, 0) }, 0, 1.2f));
            Assert.AreEqual(0, SpawnSelector.Select(points, new List<Vector3> { new(8, 0, 0) }, 2, 1.2f), "Wraps around");
            Assert.AreEqual(1, SpawnSelector.Select(points, points, 1, 1.2f), "All occupied: falls back to start");
            Assert.AreEqual(-1, SpawnSelector.Select(new List<Vector3>(), points, 0, 1.2f));
        }
    }

    public sealed class ConnectionTests
    {
        [Test]
        public void Payload_RoundTrips()
        {
            var p = new ConnectionPayload { protocol = ConnectionPayload.CurrentProtocol, playerName = "Striker" };
            Assert.IsTrue(ConnectionPayload.TryParse(p.ToBytes(), out ConnectionPayload parsed));
            Assert.AreEqual("Striker", parsed.playerName);
        }

        [Test]
        public void Validate_RejectsMalformed_WrongProtocol_AndFullServer()
        {
            Assert.IsFalse(ConnectionPayload.TryParse(new byte[] { 1, 2, 3 }, out _));
            Assert.IsFalse(ConnectionPayload.TryParse(new byte[1024], out _), "Oversized payload");
            Assert.IsNotNull(ConnectionRegistry.Validate(default, false, 0, 10));
            Assert.IsNotNull(ConnectionRegistry.Validate(new ConnectionPayload { protocol = 999 }, true, 0, 10));
            Assert.IsNotNull(ConnectionRegistry.Validate(new ConnectionPayload { protocol = ConnectionPayload.CurrentProtocol }, true, 10, 10));
            Assert.IsNull(ConnectionRegistry.Validate(new ConnectionPayload { protocol = ConnectionPayload.CurrentProtocol }, true, 9, 10));
        }

        [Test]
        public void Names_AreSanitizedAndBounded()
        {
            Assert.AreEqual("Player7", ConnectionRegistry.SanitizeName("  ", 7));
            Assert.AreEqual("abc", ConnectionRegistry.SanitizeName("<a>b{c}", 1));
            Assert.AreEqual(16, ConnectionRegistry.SanitizeName(new string('x', 40), 1).Length);
        }

        [Test]
        public void CommandLine_ResolvesRolesAndOverrides()
        {
            LaunchConfig c = LaunchConfig.Resolve(new CommandLineArgs(new[] { "game.exe", "-connect", "10.0.0.5", "-port", "7800", "-name", "Bot1", "-bot", "-fuseTime", "5.5", "-roundsToWin", "2" }));
            Assert.AreEqual(LaunchRole.Client, c.role);
            Assert.AreEqual("10.0.0.5", c.address);
            Assert.AreEqual(7800, c.port);
            Assert.AreEqual("Bot1", c.playerName);
            Assert.IsTrue(c.bot);
            Assert.AreEqual(5.5f, c.fuseTime);
            Assert.AreEqual(2, c.roundsToWin);

            LaunchConfig s = LaunchConfig.Resolve(new CommandLineArgs(new[] { "server.exe", "-server", "-maxPlayers", "64" }));
            Assert.AreEqual(LaunchRole.Server, s.role);
            Assert.AreEqual(16, s.maxPlayers, "Clamped to the 16-player server limit");

            // The mode caps it further: Duel allows 2, HeavyBomb 10.
            var duel = ScriptableObject.CreateInstance<BlueHorizon.GameModes.DuelSettings>();
            duel.maxPlayers = 2;
            Assert.AreEqual(2, MatchManager.MaxPlayersFor(duel, s));
            var heavyBomb = ScriptableObject.CreateInstance<BlueHorizon.GameModes.HeavyBombSettings>();
            heavyBomb.maxPlayers = 10;
            Assert.AreEqual(10, MatchManager.MaxPlayersFor(heavyBomb, s));
        }
    }
}
