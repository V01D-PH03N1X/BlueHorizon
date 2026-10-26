using BlueHorizon.Abilities;
using BlueHorizon.Core;
using BlueHorizon.Health;
using NUnit.Framework;
using UnityEngine;

namespace BlueHorizon.Tests
{
    public sealed class HealthModelTests
    {
        static HealthModel Striker() => new(new HealthProfile
        {
            maxHealth = 125, maxArmor = 25, maxBarrier = 25, barrierRegenDelay = 4f, barrierRegenPerSecond = 10f,
        });

        [Test]
        public void Damage_IsAbsorbed_BarrierThenArmorThenHealth()
        {
            HealthModel h = Striker();
            DamageOutcome o = h.ApplyDamage(35, 0);
            Assert.AreEqual(25, o.absorbedByBarrier);
            Assert.AreEqual(10, o.absorbedByArmor);
            Assert.AreEqual(0, o.healthDamage);
            Assert.AreEqual((0, 15, 125), (h.Barrier, h.Armor, h.Health));

            o = h.ApplyDamage(35, 1);
            Assert.AreEqual(15, o.absorbedByArmor);
            Assert.AreEqual(20, o.healthDamage);
            Assert.AreEqual(105, h.Health);
        }

        [Test]
        public void Overflow_CarriesThroughAllLayersInOneHit()
        {
            HealthModel h = Striker();
            DamageOutcome o = h.ApplyDamage(165, 0); // Rifle headshot from full: 175 effective HP.
            Assert.AreEqual(25 + 25 + 115, o.TotalApplied);
            Assert.AreEqual(10, h.Health);
            Assert.IsFalse(o.killed);
        }

        [Test]
        public void Death_OnZeroHealth_AndNoFurtherDamage()
        {
            HealthModel h = Striker();
            DamageOutcome o = h.ApplyDamage(500, 0);
            Assert.IsTrue(o.killed);
            Assert.AreEqual(0, h.Health);
            Assert.AreEqual(175, o.TotalApplied, "Only the remaining pool is applied");
            Assert.AreEqual(0, h.ApplyDamage(50, 1).TotalApplied);
        }

        [Test]
        public void Heal_RespectsMaximum_AndIsRefusedWhenDead()
        {
            HealthModel h = Striker();
            h.ApplyDamage(50 + 30, 0); // 30 health damage
            Assert.AreEqual(30, h.Heal(50));
            Assert.AreEqual(125, h.Health);
            Assert.AreEqual(0, h.Heal(50));

            h.ApplyDamage(1000, 1);
            Assert.AreEqual(0, h.Heal(50));
            Assert.IsFalse(h.IsAlive);
        }

        [Test]
        public void Barrier_RegeneratesAfterDelay_UpToMaximum_AndDamageInterrupts()
        {
            HealthModel h = Striker();
            h.ApplyDamage(20, 0);
            Assert.AreEqual(5, h.Barrier);

            Assert.IsFalse(h.Tick(3.9, 1f / 60f), "No regen before the delay");
            for (int i = 0; i < 60; i++) h.Tick(4.0 + i / 60.0, 1f / 60f); // 1 s at 10/s
            Assert.That(h.Barrier, Is.InRange(14, 15));

            h.ApplyDamage(3, 5.0);
            int afterHit = h.Barrier;
            for (int i = 0; i < 60; i++) h.Tick(5.0 + i / 60.0, 1f / 60f);
            Assert.AreEqual(afterHit, h.Barrier, "Damage restarted the delay");

            for (int i = 0; i < 600; i++) h.Tick(9.0 + i / 60.0, 1f / 60f);
            Assert.AreEqual(25, h.Barrier);
            Assert.AreEqual(25, h.Armor, "Armor never regenerates");
        }

        [Test]
        public void Armor_DoesNotRegenerate()
        {
            HealthModel h = Striker();
            h.ApplyDamage(40, 0);
            for (int i = 0; i < 1200; i++) h.Tick(10 + i / 60.0, 1f / 60f);
            Assert.AreEqual(10, h.Armor);
        }
    }

    public sealed class AbilityRulesTests
    {
        sealed class TestAbility : AbilityDefinition
        {
            public override AbilityRejectReason Execute(in AbilityContext context) => AbilityRejectReason.None;
        }

        static TestAbility Make(int charges, float cooldown)
        {
            var a = ScriptableObject.CreateInstance<TestAbility>();
            a.maxCharges = charges;
            a.cooldown = cooldown;
            return a;
        }

        [Test]
        public void Charges_AreConsumed_AndExhausted()
        {
            TestAbility smoke = Make(2, 1f);
            AbilitySlotState s = AbilityRules.Fresh(smoke);
            Assert.AreEqual(AbilityRejectReason.None, AbilityRules.CanActivate(smoke, s, 0, true, true));
            AbilityRules.Consume(smoke, ref s, 0);
            Assert.AreEqual(AbilityRejectReason.OnCooldown, AbilityRules.CanActivate(smoke, s, 0.5, true, true));
            AbilityRules.Consume(smoke, ref s, 2);
            Assert.AreEqual(AbilityRejectReason.NoCharges, AbilityRules.CanActivate(smoke, s, 10, true, true));
        }

        [Test]
        public void Signature_HasUnlimitedCharges_GatedByCooldown()
        {
            TestAbility heal = Make(0, 30f);
            AbilitySlotState s = AbilityRules.Fresh(heal);
            AbilityRules.Consume(heal, ref s, 100);
            Assert.AreEqual(AbilityRejectReason.OnCooldown, AbilityRules.CanActivate(heal, s, 129.9, true, true));
            Assert.AreEqual(AbilityRejectReason.None, AbilityRules.CanActivate(heal, s, 130.0, true, true));
        }

        [Test]
        public void Activation_IsRejectedWhileDead_OrOutsideAllowedPhase()
        {
            TestAbility a = Make(1, 0f);
            AbilitySlotState s = AbilityRules.Fresh(a);
            Assert.AreEqual(AbilityRejectReason.Dead, AbilityRules.CanActivate(a, s, 0, false, true));
            Assert.AreEqual(AbilityRejectReason.NotAllowedNow, AbilityRules.CanActivate(a, s, 0, true, false));
            Assert.AreEqual(AbilityRejectReason.Missing, AbilityRules.CanActivate(null, s, 0, true, true));
        }
    }

    /// <summary>Wayblocker placement uses real physics queries against edit-mode colliders.</summary>
    public sealed class WayblockerPlacementTests
    {
        GameObject _floor, _wall, _player;
        WayblockerDefinition _def;

        [SetUp]
        public void SetUp()
        {
            _floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _floor.transform.position = new Vector3(0f, -0.5f, 0f);
            _floor.transform.localScale = new Vector3(40f, 1f, 40f);
            _def = ScriptableObject.CreateInstance<WayblockerDefinition>();
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_floor);
            if (_wall != null) Object.DestroyImmediate(_wall);
            if (_player != null) Object.DestroyImmediate(_player);
        }

        static Vector3 Aim(float pitchDown) => Quaternion.Euler(pitchDown, 0f, 0f) * Vector3.forward;

        [Test]
        public void Placement_OnOpenGround_IsAccepted_InFrontOfPlayer()
        {
            Assert.IsTrue(_def.TryFindPlacement(new Vector3(0f, 1.6f, 0f), Aim(25f), out Vector3 center, out Quaternion rot));
            Assert.That(center.z, Is.InRange(1f, 6f));
            Assert.AreEqual(_def.size.y * 0.5f, center.y, 0.05f);
            Assert.AreEqual(0f, rot.eulerAngles.y, 0.5f);
        }

        [Test]
        public void Placement_OverlappingGeometry_IsRejected()
        {
            // A pillar inside the wall's footprint but off the aim ray.
            _wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _wall.transform.position = new Vector3(1.2f, 1f, 3.43f);
            _wall.transform.localScale = new Vector3(0.4f, 2f, 0.4f);
            Physics.SyncTransforms();
            Assert.IsFalse(_def.TryFindPlacement(new Vector3(0f, 1.6f, 0f), Aim(25f), out _, out _));
        }

        [Test]
        public void Placement_InsideAPlayer_IsRejected()
        {
            Assert.IsTrue(_def.TryFindPlacement(new Vector3(0f, 1.6f, 0f), Aim(25f), out Vector3 center, out _));
            _player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _player.layer = GameLayers.Player;
            _player.transform.position = new Vector3(0f, 1f, center.z);
            Physics.SyncTransforms();
            Assert.IsFalse(_def.TryFindPlacement(new Vector3(0f, 1.6f, 0f), Aim(25f), out _, out _));
        }

        [Test]
        public void Placement_BeyondRange_IsRejected()
        {
            // Looking at the horizon: the probe point is at max range; move the floor away so nothing is below it.
            _floor.transform.localScale = new Vector3(4f, 1f, 4f);
            Physics.SyncTransforms();
            Assert.IsFalse(_def.TryFindPlacement(new Vector3(0f, 1.6f, 0f), Aim(0f), out _, out _));
        }
    }
}
