using BlueHorizon.Abilities;
using BlueHorizon.Agents;
using BlueHorizon.Combat;
using BlueHorizon.Core;
using BlueHorizon.GameModes;
using BlueHorizon.Health;
using BlueHorizon.Networking;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BlueHorizon.Tests
{
    /// <summary>Milestone 5: flash rules, EnemyAlert range, downed state and revives, agent availability, generated agents.</summary>
    public sealed class AgentsM5Tests
    {
        // ───────────── Flash Grenade ─────────────

        [Test]
        public void Flash_LookingStraightAtCloseFlash_IsNearlyFull()
        {
            float i = FlashRules.Intensity(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, 2f), 22f, lineOfSight: true);
            Assert.Greater(i, 0.9f);
            Assert.AreEqual(3.2f * i, FlashRules.Duration(i, 3.2f), 1e-4f);
        }

        [Test]
        public void Flash_WithoutLineOfSight_DoesNothing()
        {
            Assert.AreEqual(0f, FlashRules.Intensity(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, 2f), 22f, lineOfSight: false));
        }

        [Test]
        public void Flash_BeyondRadius_DoesNothing()
        {
            Assert.AreEqual(0f, FlashRules.Intensity(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, 23f), 22f, true));
        }

        [Test]
        public void Flash_LookingAway_IsMuchWeaker()
        {
            Vector3 flash = new(0f, 0f, 5f);
            float facing = FlashRules.Intensity(Vector3.zero, Vector3.forward, flash, 22f, true);
            float side = FlashRules.Intensity(Vector3.zero, Vector3.right, flash, 22f, true);
            float behind = FlashRules.Intensity(Vector3.zero, Vector3.back, flash, 22f, true);
            Assert.Greater(facing, side);
            Assert.Greater(side, behind);
            Assert.Less(behind, 0.25f);
        }

        [Test]
        public void Flash_FarAndTurnedAway_IsIgnored()
        {
            // Behind (0.2) at the edge (~0.3): below the minimum intensity.
            Assert.AreEqual(0f, FlashRules.Intensity(Vector3.zero, Vector3.back, new Vector3(0f, 0f, 21f), 22f, true));
        }

        [Test]
        public void Flash_Whiteness_HoldsThenFades()
        {
            Assert.AreEqual(1f, FlashRules.Whiteness(1f, 3f, 0.5f), 1e-4f);
            float mid = FlashRules.Whiteness(1f, 3f, 2f);
            Assert.Greater(mid, 0f);
            Assert.Less(mid, 1f);
            Assert.AreEqual(0f, FlashRules.Whiteness(1f, 3f, 3.1f));
        }

        // ───────────── EnemyAlert ─────────────

        [Test]
        public void EnemyAlert_RangeIsSpherical()
        {
            Assert.IsTrue(AlertRules.InRange(Vector3.zero, new Vector3(20f, 0f, 22f), 30f));
            Assert.IsFalse(AlertRules.InRange(Vector3.zero, new Vector3(25f, 0f, 20f), 30f));
        }

        // ───────────── Downed state ─────────────

        [Test]
        public void Downed_OnlyWeaponDamageWithTeammateStanding()
        {
            Assert.IsTrue(DownedRules.ShouldGoDown(true, DamageSource.Weapon, teammateStanding: true));
            Assert.IsFalse(DownedRules.ShouldGoDown(true, DamageSource.Weapon, teammateStanding: false), "last player standing dies");
            Assert.IsFalse(DownedRules.ShouldGoDown(true, DamageSource.Explosion, true), "the bomb kills outright");
            Assert.IsFalse(DownedRules.ShouldGoDown(true, DamageSource.World, true));
            Assert.IsFalse(DownedRules.ShouldGoDown(false, DamageSource.Weapon, true), "mode without downed state");
        }

        [Test]
        public void Downed_DamageShortensBleedout()
        {
            // 15 s bleedout, 100 downed health: 30 damage removes 4.5 s; 100 damage removes all of it.
            Assert.AreEqual(15.5, DownedRules.ApplyDamage(20.0, 30, 15f, 100), 1e-6);
            Assert.LessOrEqual(DownedRules.ApplyDamage(20.0, 100, 15f, 100), 5.0 + 1e-6);
            Assert.AreEqual(20.0, DownedRules.ApplyDamage(20.0, 0, 15f, 100));
        }

        [Test]
        public void Downed_ReviveNeedsStandingTeammateInRangeHolding()
        {
            Assert.IsTrue(DownedRules.CanRevive(true, true, 1.5f, 1.8f, true));
            Assert.IsFalse(DownedRules.CanRevive(false, true, 1.5f, 1.8f, true), "downed reviver");
            Assert.IsFalse(DownedRules.CanRevive(true, false, 1.5f, 1.8f, true), "enemy");
            Assert.IsFalse(DownedRules.CanRevive(true, true, 2.5f, 1.8f, true), "too far");
            Assert.IsFalse(DownedRules.CanRevive(true, true, 1.5f, 1.8f, false), "not holding interact");
        }

        [Test]
        public void Downed_NetStateProgress()
        {
            var d = new DownedNetState { downed = true, bleedoutEnd = 20.0, reviverId = 3, reviveStart = 10.0, reviveEnd = 14.0 };
            Assert.AreEqual(0.5f, d.ReviveProgress(12.0), 1e-4f);
            Assert.AreEqual(8f, d.BleedoutRemaining(12.0), 1e-4f);
            d.reviverId = DownedNetState.NoReviver;
            Assert.IsFalse(d.BeingRevived);
            Assert.AreEqual(0f, d.ReviveProgress(12.0));
        }

        [Test]
        public void HealthModel_ReviveOnlyFromZero()
        {
            var model = new HealthModel(new HealthProfile { maxHealth = 125, maxArmor = 25, maxBarrier = 25 });
            Assert.IsFalse(model.Revive(30, 0), "alive players cannot be revived");
            model.ApplyDamage(500, 1.0);
            Assert.IsFalse(model.IsAlive);
            Assert.AreEqual(0, model.Heal(50), "downed players cannot heal");
            Assert.IsTrue(model.Revive(30, 2.0));
            Assert.AreEqual(30, model.Health);
            Assert.AreEqual(0, model.Armor);
            Assert.IsFalse(model.Revive(30, 2.0), "already up");

            var small = new HealthModel(new HealthProfile { maxHealth = 50, maxArmor = 0, maxBarrier = 0 });
            small.ApplyDamage(999, 0);
            Assert.IsTrue(small.Revive(500, 1));
            Assert.AreEqual(50, small.Health, "revive is capped at max health");
        }

        // ───────────── Agent availability ─────────────

        static AgentDefinition[] Agents()
        {
            AgentDefinition Make(string id, string callsign, bool unlocked)
            {
                var a = ScriptableObject.CreateInstance<AgentDefinition>();
                a.agentId = id;
                a.callsign = callsign;
                a.unlockedByDefault = unlocked;
                return a;
            }
            return new[] { Make("watchdog", "Striker", true), Make("vigil", "Vigil", true), Make("lifeline", "Lifeline", false) };
        }

        [Test]
        public void Agents_LockedUntilUnlocked()
        {
            AgentDefinition[] agents = Agents();
            uint unlocked = AgentRules.UnlockMask(agents, "");
            Assert.AreEqual(0b011u, unlocked);
            Assert.AreEqual(AgentRejectReason.Locked, AgentRules.Check(2, agents, unlocked, 0));
            Assert.AreEqual(0b111u, AgentRules.UnlockMask(agents, "all"));
            Assert.AreEqual(0b111u, AgentRules.UnlockMask(agents, "Lifeline"), "ids and callsigns both work, case-insensitive");
        }

        [Test]
        public void Agents_BanWinsOverUnlock()
        {
            AgentDefinition[] agents = Agents();
            uint unlocked = AgentRules.UnlockMask(agents, "all");
            uint banned = AgentRules.BanMask(agents, " vigil , nobody ");
            Assert.AreEqual(0b010u, banned);
            Assert.AreEqual(AgentRejectReason.Banned, AgentRules.Check(1, agents, unlocked, banned));
            Assert.AreEqual(AgentRejectReason.None, AgentRules.Check(0, agents, unlocked, banned));
        }

        [Test]
        public void Agents_UnknownIndexRejected()
        {
            AgentDefinition[] agents = Agents();
            Assert.AreEqual(AgentRejectReason.Unknown, AgentRules.Check(3, agents, ~0u, 0));
            Assert.AreEqual(AgentRejectReason.Unknown, AgentRules.Check(-1, agents, ~0u, 0));
            Assert.AreEqual(AgentRejectReason.Unknown, AgentRules.Check(0, null, ~0u, 0));
        }

        [Test]
        public void Agents_ResolveFallsBackToFirstAvailable()
        {
            AgentDefinition[] agents = Agents();
            uint unlocked = AgentRules.UnlockMask(agents, "");
            Assert.AreEqual(1, AgentRules.Resolve(1, agents, unlocked, 0));
            Assert.AreEqual(0, AgentRules.Resolve(2, agents, unlocked, 0), "locked choice falls back");
            Assert.AreEqual(1, AgentRules.Resolve(0, agents, unlocked, 0b001u), "banned Striker falls back to Vigil");
            Assert.AreEqual(0, AgentRules.Resolve(1, agents, unlocked, 0b111u), "nothing available: index 0");
        }

        // ───────────── Protocol and generated content ─────────────

        [Test]
        public void Protocol_Milestone4ClientsRejected()
        {
            Assert.GreaterOrEqual(ConnectionPayload.CurrentProtocol, 5);
            Assert.IsNotNull(ConnectionRegistry.Validate(new ConnectionPayload { protocol = 4 }, true, 0, 10));
        }

        [Test]
        public void GeneratedContent_HasThreeAgentsWithAbilities()
        {
            var content = AssetDatabase.LoadAssetAtPath<GameContent>("Assets/_Project/Settings/GameContent.asset");
            Assert.IsNotNull(content);
            Assert.IsNotNull(content.agents);
            Assert.AreEqual(3, content.agents.Length);
            Assert.AreSame(content.defaultAgent, content.agents[0], "Striker stays index 0");
            Assert.AreEqual("vigil", content.agents[1].agentId);
            Assert.AreEqual("lifeline", content.agents[2].agentId);
            Assert.IsTrue(content.agents[1].unlockedByDefault);
            Assert.IsFalse(content.agents[2].unlockedByDefault, "Lifeline is the unlockable agent");
            Assert.IsInstanceOf<FlashGrenadeDefinition>(content.agents[1].utility1);
            Assert.IsInstanceOf<EnemyAlertDefinition>(content.agents[1].signature);
            Assert.IsInstanceOf<RevivalDefinition>(content.agents[2].signature);
            Assert.IsInstanceOf<FlashGrenadeDefinition>(content.agents[2].utility2);
            foreach (AgentDefinition a in content.agents)
            {
                Assert.IsNotNull(a.utility1, a.agentId);
                Assert.IsNotNull(a.utility2, a.agentId);
                Assert.IsNotNull(a.signature, a.agentId);
            }
        }

        [Test]
        public void GeneratedContent_TeamObjectiveModesUseDownedState()
        {
            var content = AssetDatabase.LoadAssetAtPath<GameContent>("Assets/_Project/Settings/GameContent.asset");
            foreach (GameModeDefinition mode in content.modes)
            {
                bool expected = mode is HeavyBombSettings or CaptureAreaSettings;
                Assert.AreEqual(expected, mode.downedState, mode.modeId);
            }
        }

        [Test]
        public void SmokeGrenade_KeepsThrowSettingsAfterBaseClassMove()
        {
            var smoke = AssetDatabase.LoadAssetAtPath<SmokeGrenadeDefinition>("Assets/_Project/ScriptableObjects/Abilities/SmokeGrenade.asset");
            Assert.IsNotNull(smoke);
            Assert.Greater(smoke.throwSpeed, 0f);
            Assert.Greater(smoke.fuseTime, 0f);
            Assert.AreEqual(GrenadeKind.Smoke, smoke.Kind);
        }
    }
}
