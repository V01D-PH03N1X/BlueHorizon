using System.Collections.Generic;
using BlueHorizon.Core;
using BlueHorizon.GameModes;
using BlueHorizon.Match;
using NUnit.Framework;
using UnityEngine;

namespace BlueHorizon.Tests
{
    public sealed class TeamRulesTests
    {
        [Test]
        public void TeamModes_OnlyOtherTeamIsHostile()
        {
            Assert.IsTrue(TeamRules.AreEnemies(Team.Alpha, Team.Bravo, false));
            Assert.IsFalse(TeamRules.AreEnemies(Team.Alpha, Team.Alpha, false));
            Assert.IsFalse(TeamRules.AreEnemies(Team.None, Team.Bravo, false), "Spectators are nobody's enemy");
        }

        [Test]
        public void FreeForAll_EveryoneIsHostile() => Assert.IsTrue(TeamRules.AreEnemies(Team.None, Team.None, true));
    }

    public sealed class DuelRulesTests
    {
        [Test]
        public void Timeout_GoesToMoreEffectiveHealth_TieIsDraw()
        {
            Assert.AreEqual(Team.Alpha, DuelRules.TimeoutWinner(120, 80));
            Assert.AreEqual(Team.Bravo, DuelRules.TimeoutWinner(10, 175));
            Assert.AreEqual(Team.None, DuelRules.TimeoutWinner(100, 100));
        }
    }

    public sealed class FreeForAllRulesTests
    {
        [Test]
        public void Leader_ByScore_ThenFewerDeaths_ThenLowerId()
        {
            var s = new List<FfaStanding>
            {
                new() { clientId = 5, score = 10, deaths = 4 },
                new() { clientId = 2, score = 12, deaths = 9 },
                new() { clientId = 3, score = 12, deaths = 3 },
                new() { clientId = 1, score = 12, deaths = 3 },
            };
            Assert.AreEqual(3, FreeForAllRules.Leader(s));
            Assert.AreEqual(-1, FreeForAllRules.Leader(new List<FfaStanding>()));
        }
    }

    public sealed class WeaponGameRulesTests
    {
        [Test]
        public void Advance_MovesUpTheLadder_AndWinsOnLastLevel()
        {
            Assert.AreEqual(1, WeaponGameRules.Advance(0, 4, out bool won0));
            Assert.IsFalse(won0);
            Assert.AreEqual(3, WeaponGameRules.Advance(2, 4, out bool won2));
            Assert.IsFalse(won2);
            Assert.AreEqual(3, WeaponGameRules.Advance(3, 4, out bool won3));
            Assert.IsTrue(won3, "A kill with the last weapon wins");
        }
    }

    public sealed class ZoneModelTests
    {
        static ZoneModel Zone() => new(new ZoneSettings { captureTime = 4f, capturePerExtraPlayer = 0.5f, maxCaptureMultiplier = 2f, pointsPerSecond = 1f });

        static (int points, bool changed) Run(ZoneModel z, int alpha, int bravo, float seconds)
        {
            int points = 0;
            bool changed = false;
            for (int i = 0; i < Mathf.RoundToInt(seconds * 60f); i++)
            {
                points += z.Step(alpha, bravo, 1f / 60f, out bool c);
                changed |= c;
            }
            return (points, changed);
        }

        [Test]
        public void OnePlayer_CapturesAfterCaptureTime_ThenScores()
        {
            ZoneModel z = Zone();
            Run(z, 1, 0, 3.9f);
            Assert.AreEqual(Team.None, z.Owner);
            Assert.AreEqual(Team.Alpha, z.Capturing);
            (int _, bool changed) = Run(z, 1, 0, 0.2f);
            Assert.IsTrue(changed);
            Assert.AreEqual(Team.Alpha, z.Owner);
            (int points, bool _) = Run(z, 0, 0, 10f);
            Assert.That(points, Is.InRange(9, 10), "Owner keeps scoring while nobody contests");
        }

        [Test]
        public void MorePlayers_CaptureFaster_UpToCap()
        {
            ZoneModel two = Zone();
            Run(two, 2, 0, 2.75f); // 1.5x speed → 2.67 s
            Assert.AreEqual(Team.Alpha, two.Owner);

            ZoneModel five = Zone();
            Run(five, 5, 0, 1.9f); // capped at 2x → 2 s
            Assert.AreEqual(Team.None, five.Owner);
            Run(five, 5, 0, 0.2f);
            Assert.AreEqual(Team.Alpha, five.Owner);
        }

        [Test]
        public void Contested_StopsProgressAndScoring()
        {
            ZoneModel z = Zone();
            Run(z, 1, 0, 4.1f);
            (int points, bool _) = Run(z, 2, 1, 5f);
            Assert.IsTrue(z.Contested);
            Assert.AreEqual(0, points);
            Assert.AreEqual(Team.Alpha, z.Owner);
        }

        [Test]
        public void EnemyMustRevertPartialProgress_BeforeCapturing()
        {
            ZoneModel z = Zone();
            Run(z, 1, 0, 2f); // Alpha 50 %
            Run(z, 0, 1, 1f); // Bravo reverts 25 %
            Assert.AreEqual(Team.Alpha, z.Capturing);
            Assert.AreEqual(0.25f, z.Progress, 0.02f);
            Run(z, 0, 1, 1.1f); // reverted to 0, Bravo now capturing
            Assert.AreEqual(Team.Bravo, z.Capturing);
        }

        [Test]
        public void EmptyZone_DecaysProgress()
        {
            ZoneModel z = Zone();
            Run(z, 1, 0, 2f);
            Run(z, 0, 0, 3f);
            Assert.AreEqual(0f, z.Progress, 1e-4f);
            Assert.AreEqual(Team.None, z.Capturing);
        }
    }

    public sealed class FfaSpawnTests
    {
        [Test]
        public void SelectFarthest_PicksSpawnFarthestFromThreats()
        {
            var candidates = new List<Vector3> { new(0, 0, 0), new(10, 0, 0), new(50, 0, 0) };
            Assert.AreEqual(2, SpawnSelector.SelectFarthest(candidates, new List<Vector3> { new(1, 0, 0) }, 0));
            Assert.AreEqual(0, SpawnSelector.SelectFarthest(candidates, new List<Vector3> { new(49, 0, 0) }, 0));
            Assert.AreEqual(1, SpawnSelector.SelectFarthest(candidates, new List<Vector3>(), 1), "No threats: rotate");
            Assert.AreEqual(-1, SpawnSelector.SelectFarthest(new List<Vector3>(), new List<Vector3>(), 0));
        }
    }

    public sealed class ModeConfigTests
    {
        [Test]
        public void ModeAndAliases_AreParsed()
        {
            LaunchConfig c = LaunchConfig.Resolve(new CommandLineArgs(new[] { "x.exe", "-mode", "Deathmatch", "-timeLimit", "90", "-scoreLimit", "5", "-respawnDelay", "1.5", "-maxPlayers", "15" }));
            Assert.AreEqual("deathmatch", c.mode);
            Assert.AreEqual(90f, c.roundTime);
            Assert.AreEqual(5, c.roundsToWin);
            Assert.AreEqual(1.5f, c.respawnDelay);
            Assert.AreEqual(15, c.maxPlayers);
        }
    }
}
