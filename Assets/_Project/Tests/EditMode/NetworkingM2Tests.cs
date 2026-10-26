using BlueHorizon.Combat;
using BlueHorizon.Core;
using BlueHorizon.Match;
using NUnit.Framework;
using UnityEngine;

namespace BlueHorizon.Tests
{
    public sealed class PoseHistoryTests
    {
        [Test]
        public void Sample_InterpolatesBetweenTicks()
        {
            var h = new PoseHistory();
            h.Record(1.0, new Vector3(0, 0, 0), 0f);
            h.Record(1.1, new Vector3(1, 0, 0), 1f);
            Assert.IsTrue(h.TrySample(1.05, out Vector3 p, out float crouch));
            Assert.AreEqual(0.5f, p.x, 1e-4f);
            Assert.AreEqual(0.5f, crouch, 1e-4f);
        }

        [Test]
        public void Sample_ClampsToRecordedRange()
        {
            var h = new PoseHistory();
            h.Record(1.0, new Vector3(1, 0, 0), 0f);
            h.Record(2.0, new Vector3(2, 0, 0), 0f);
            h.TrySample(0.5, out Vector3 older, out _);
            h.TrySample(9.0, out Vector3 newer, out _);
            Assert.AreEqual(1f, older.x, 1e-5f);
            Assert.AreEqual(2f, newer.x, 1e-5f);
        }

        [Test]
        public void RingBuffer_KeepsOnlyCapacity_AndDuplicateTicksOverwrite()
        {
            var h = new PoseHistory(capacity: 4);
            for (int i = 0; i < 10; i++) h.Record(i, new Vector3(i, 0, 0), 0f);
            Assert.AreEqual(4, h.Count);
            h.TrySample(0, out Vector3 oldest, out _);
            Assert.AreEqual(6f, oldest.x, 1e-5f, "Oldest retained sample is tick 6");

            h.Record(9, new Vector3(99, 0, 0), 0f);
            h.TrySample(9, out Vector3 latest, out _);
            Assert.AreEqual(99f, latest.x, 1e-5f);
            Assert.AreEqual(4, h.Count);
        }

        [Test]
        public void Empty_OrCleared_ReturnsFalse()
        {
            var h = new PoseHistory();
            Assert.IsFalse(h.TrySample(1, out _, out _));
            h.Record(1, Vector3.one, 0f);
            h.Clear();
            Assert.IsFalse(h.TrySample(1, out _, out _));
        }
    }

    public sealed class LagCompensationTests
    {
        [Test]
        public void ViewTime_InWindow_IsAccepted()
        {
            Assert.AreEqual(9.85, LagCompensation.ClampViewTime(9.85, 10.0, 0.3, 0), 1e-9);
        }

        [Test]
        public void ViewTime_InTheFuture_IsClampedToNow()
        {
            Assert.AreEqual(10.0, LagCompensation.ClampViewTime(12.0, 10.0, 0.3, 0), 1e-9);
        }

        [Test]
        public void ViewTime_TooOld_IsClampedToMaxRewind()
        {
            Assert.AreEqual(9.7, LagCompensation.ClampViewTime(5.0, 10.0, 0.3, 0), 1e-9);
        }

        [Test]
        public void ViewTime_Invalid_MeansNoRewind()
        {
            Assert.AreEqual(10.0, LagCompensation.ClampViewTime(double.NaN, 10.0, 0.3, 0), 1e-9);
            Assert.AreEqual(10.0, LagCompensation.ClampViewTime(0, 10.0, 0.3, 0), 1e-9);
            Assert.AreEqual(10.0, LagCompensation.ClampViewTime(double.NegativeInfinity, 10.0, 0.3, 0), 1e-9);
        }

        [Test]
        public void ViewTime_CannotJumpBackBeyondBacktrackLimit()
        {
            // Previous command was accepted at 9.95; a client cannot now claim 9.75 to pick an older snapshot.
            double accepted = LagCompensation.ClampViewTime(9.75, 10.0, 0.3, previousAccepted: 9.95);
            Assert.AreEqual(9.95 - LagCompensation.MaxBacktrack, accepted, 1e-9);
        }

        [Test]
        public void MaxRewindZero_DisablesRewind()
        {
            Assert.AreEqual(10.0, LagCompensation.ClampViewTime(9.8, 10.0, 0, 0), 1e-9);
        }
    }

    public sealed class ReconnectRegistryTests
    {
        static ReconnectRecord Record(int match, double t) => new() { team = Team.Bravo, kills = 3, deaths = 1, matchId = match, disconnectTime = t };

        [Test]
        public void SameMatch_WithinGrace_IsRestoredOnce()
        {
            var r = new ReconnectRegistry();
            r.Store("Striker", Record(2, 100));
            Assert.IsTrue(r.TryTake("Striker", 2, 150, 180, out ReconnectRecord rec));
            Assert.AreEqual(Team.Bravo, rec.team);
            Assert.AreEqual(3, rec.kills);
            Assert.IsFalse(r.TryTake("Striker", 2, 150, 180, out _), "Records are single-use");
        }

        [Test]
        public void Expired_OrOtherMatch_IsNotRestored()
        {
            var r = new ReconnectRegistry();
            r.Store("A", Record(2, 100));
            r.Store("B", Record(1, 100));
            Assert.IsFalse(r.TryTake("A", 2, 400, 180, out _), "Grace window expired");
            Assert.IsFalse(r.TryTake("B", 2, 110, 180, out _), "Different match");
            Assert.AreEqual(0, r.Count);
        }
    }

    public sealed class NetworkConditionTests
    {
        [Test]
        public void CommandLine_ParsesSimulationAndLagCompFlags()
        {
            LaunchConfig c = LaunchConfig.Resolve(new CommandLineArgs(new[] { "x.exe", "-simLatency", "150", "-simJitter", "20", "-simLoss", "2.5", "-noLagComp", "-maxRewind", "5000" }));
            Assert.AreEqual(150, c.simLatencyMs);
            Assert.AreEqual(20, c.simJitterMs);
            Assert.AreEqual(2.5f, c.simLossPercent);
            Assert.IsFalse(c.lagCompensation);
            Assert.AreEqual(1000, c.maxRewindMs, "Rewind is capped at 1 s");
            Assert.IsTrue(c.NetworkConditions.Enabled);

            LaunchConfig d = LaunchConfig.Resolve(new CommandLineArgs(new[] { "x.exe" }));
            Assert.IsTrue(d.lagCompensation);
            Assert.IsFalse(d.NetworkConditions.Enabled);
        }
    }
}
