using System.Collections.Generic;
using BlueHorizon.Attachments;
using BlueHorizon.Combat;
using BlueHorizon.Player;
using BlueHorizon.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace BlueHorizon.Tests
{
    public static class TestData
    {
        public static WeaponDefinition Pistol()
        {
            var w = ScriptableObject.CreateInstance<WeaponDefinition>();
            w.weaponId = "pistol";
            w.slot = WeaponSlot.Secondary;
            w.fireMode = FireMode.SemiAutomatic;
            w.fireRate = 1.6f;
            w.magazineSize = 12;
            w.reserveAmmo = 36;
            w.reloadTime = 1.5f;
            w.equipTime = 0.35f;
            w.damage = new ZoneDamage { head = 85, torso = 35, legs = 15 };
            w.falloff = new RangeFalloff { falloffStart = 1000, falloffEnd = 1000, minMultiplier = 1 };
            return w;
        }

        public static WeaponDefinition Rifle()
        {
            var w = ScriptableObject.CreateInstance<WeaponDefinition>();
            w.weaponId = "rifle";
            w.slot = WeaponSlot.Primary;
            w.fireMode = FireMode.Automatic;
            w.fireRate = 4.5f;
            w.magazineSize = 31;
            w.reserveAmmo = 93;
            w.reloadTime = 2.3f;
            w.equipTime = 0.6f;
            w.damage = new ZoneDamage { head = 165, torso = 55, legs = 35 };
            w.falloff = new RangeFalloff { falloffStart = 1000, falloffEnd = 1000, minMultiplier = 1 };
            return w;
        }

        public static AttachmentDefinition Suppressor()
        {
            var a = ScriptableObject.CreateInstance<AttachmentDefinition>();
            a.damageMultiplier = 0.8f;
            a.suppressesFireSound = true;
            a.hidesMinimapFireIndicator = true;
            return a;
        }
    }

    public sealed class DamageResolverTests
    {
        [TestCase(HitZone.Head, 85)]
        [TestCase(HitZone.Torso, 35)]
        [TestCase(HitZone.Legs, 15)]
        public void Pistol_DamageByZone_MatchesSpecification(HitZone zone, int expected)
        {
            var stats = WeaponStats.Compute(TestData.Pistol(), null);
            Assert.AreEqual(expected, DamageResolver.Compute(stats, zone, 10f));
        }

        [TestCase(HitZone.Head, 165)]
        [TestCase(HitZone.Torso, 55)]
        [TestCase(HitZone.Legs, 35)]
        public void AssaultRifle_DamageByZone_MatchesSpecification(HitZone zone, int expected)
        {
            var stats = WeaponStats.Compute(TestData.Rifle(), null);
            Assert.AreEqual(expected, DamageResolver.Compute(stats, zone, 10f));
        }

        [TestCase(HitZone.Head, 132)]
        [TestCase(HitZone.Torso, 44)]
        [TestCase(HitZone.Legs, 28)]
        public void Suppressor_ReducesRifleBaseDamageBy20Percent(HitZone zone, int expected)
        {
            var stats = WeaponStats.Compute(TestData.Rifle(), new List<AttachmentDefinition> { TestData.Suppressor() });
            Assert.AreEqual(expected, DamageResolver.Compute(stats, zone, 10f));
            Assert.IsTrue(stats.Suppressed);
            Assert.IsTrue(stats.HidesMinimapFireIndicator);
        }

        [TestCase(HitZone.Head, 68)]
        [TestCase(HitZone.Torso, 28)]
        [TestCase(HitZone.Legs, 12)]
        public void Suppressor_ReducesPistolBaseDamageBy20Percent(HitZone zone, int expected)
        {
            var stats = WeaponStats.Compute(TestData.Pistol(), new List<AttachmentDefinition> { TestData.Suppressor() });
            Assert.AreEqual(expected, DamageResolver.Compute(stats, zone, 10f));
        }

        [Test]
        public void RangeFalloff_IsAppliedAfterAttachmentModifier()
        {
            WeaponDefinition rifle = TestData.Rifle();
            rifle.falloff = new RangeFalloff { falloffStart = 20f, falloffEnd = 40f, minMultiplier = 0.5f };
            var stats = WeaponStats.Compute(rifle, new List<AttachmentDefinition> { TestData.Suppressor() });
            Assert.AreEqual(44, DamageResolver.Compute(stats, HitZone.Torso, 10f));
            Assert.AreEqual(33, DamageResolver.Compute(stats, HitZone.Torso, 30f)); // 55 × 0.8 × 0.75
            Assert.AreEqual(22, DamageResolver.Compute(stats, HitZone.Torso, 80f)); // 55 × 0.8 × 0.5
        }

        [Test]
        public void AnyHit_DealsAtLeastOneDamage()
        {
            WeaponDefinition w = TestData.Pistol();
            w.damage = new ZoneDamage { head = 1, torso = 1, legs = 1 };
            w.falloff = new RangeFalloff { falloffStart = 0f, falloffEnd = 1f, minMultiplier = 0.01f };
            Assert.AreEqual(1, DamageResolver.Compute(WeaponStats.Compute(w, null), HitZone.Legs, 100f));
        }
    }

    public sealed class WeaponSimulationTests
    {
        const float Dt = 1f / 60f;
        WeaponStats _rifle, _pistol;

        [SetUp]
        public void SetUp()
        {
            _rifle = WeaponStats.Compute(TestData.Rifle(), null);
            _pistol = WeaponStats.Compute(TestData.Pistol(), null);
        }

        static WeaponContext Ready => new() { canFire = true, grounded = true };

        static PlayerInputCommand Cmd(uint seq, InputButtons buttons) => new() { sequence = seq, buttons = buttons };

        int Run(ref WeaponSimState s, ref float time, ref uint seq, int ticks, System.Func<int, InputButtons> buttons, WeaponContext ctx)
        {
            int shots = 0;
            for (int i = 0; i < ticks; i++)
            {
                time += Dt;
                WeaponSimulation.Step(ref s, Cmd(++seq, buttons(i)), time, Dt, _rifle, _pistol, ctx, out ShotEvent shot);
                if (shot.fired) shots++;
            }
            return shots;
        }

        [Test]
        public void Rifle_FireRate_IsEnforcedAt4Point5PerSecond()
        {
            WeaponSimState s = WeaponSimulation.Fresh(_rifle.Definition, _pistol.Definition);
            float t = 0f;
            uint seq = 0;
            int shots = Run(ref s, ref t, ref seq, 120, _ => InputButtons.Fire, Ready); // 2 seconds
            Assert.That(shots, Is.InRange(9, 10), "4.5 shots/s over 2 s");
        }

        [Test]
        public void Rifle_CannotFireMoreThanMagazine_ThenReloadsFromReserve()
        {
            WeaponSimState s = WeaponSimulation.Fresh(_rifle.Definition, _pistol.Definition);
            float t = 0f;
            uint seq = 0;
            // 31 rounds at 4.5/s take ~6.7 s; holding the trigger for 7 s must fire exactly one magazine.
            int shots = Run(ref s, ref t, ref seq, 60 * 7, _ => InputButtons.Fire, Ready);
            Assert.AreEqual(31, shots);
            Assert.AreEqual(0, s.Magazine(0));
            Assert.IsTrue(s.IsReloading, "Empty magazine starts a reload automatically");

            Run(ref s, ref t, ref seq, 60 * 3, _ => InputButtons.None, Ready);
            Assert.AreEqual(31, s.Magazine(0));
            Assert.AreEqual(62, s.Reserve(0));
            Assert.AreEqual(93 + 31, s.Magazine(0) + s.Reserve(0) + shots, "Ammo is conserved");
        }

        [Test]
        public void Reload_TakesConfiguredTime_AndRefillsMagazine()
        {
            WeaponSimState s = WeaponSimulation.Fresh(_rifle.Definition, _pistol.Definition);
            float t = 0f;
            uint seq = 0;
            Run(ref s, ref t, ref seq, 30, _ => InputButtons.Fire, Ready);
            short magBefore = s.Magazine(0);
            Assert.Less(magBefore, 31);

            Run(ref s, ref t, ref seq, 1, _ => InputButtons.Reload, Ready);
            Assert.IsTrue(s.IsReloading);
            Run(ref s, ref t, ref seq, (int)(2.0f / Dt), _ => InputButtons.None, Ready);
            Assert.IsTrue(s.IsReloading, "Still reloading before 2.3 s");
            Run(ref s, ref t, ref seq, (int)(0.4f / Dt), _ => InputButtons.None, Ready);
            Assert.IsFalse(s.IsReloading);
            Assert.AreEqual(31, s.Magazine(0));
            Assert.AreEqual(93 - (31 - magBefore), s.Reserve(0));
        }

        [Test]
        public void Reload_CannotFireWhileReloading()
        {
            WeaponSimState s = WeaponSimulation.Fresh(_rifle.Definition, _pistol.Definition);
            float t = 0f;
            uint seq = 0;
            Run(ref s, ref t, ref seq, 10, _ => InputButtons.Fire, Ready);
            Run(ref s, ref t, ref seq, 1, _ => InputButtons.Reload, Ready);
            int shots = Run(ref s, ref t, ref seq, 60, _ => InputButtons.Fire, Ready);
            Assert.AreEqual(0, shots);
        }

        [Test]
        public void Pistol_SemiAutomatic_HoldingTriggerFiresOnce()
        {
            WeaponSimState s = WeaponSimulation.Fresh(_rifle.Definition, _pistol.Definition);
            float t = 0f;
            uint seq = 0;
            Run(ref s, ref t, ref seq, 60, _ => InputButtons.Weapon2, Ready); // switch + equip
            int shots = Run(ref s, ref t, ref seq, 120, _ => InputButtons.Fire, Ready);
            Assert.AreEqual(1, shots);
        }

        [Test]
        public void Pistol_FireRate_IsEnforcedAt1Point6PerSecond_EvenWhenSpammed()
        {
            WeaponSimState s = WeaponSimulation.Fresh(_rifle.Definition, _pistol.Definition);
            float t = 0f;
            uint seq = 0;
            Run(ref s, ref t, ref seq, 60, _ => InputButtons.Weapon2, Ready);
            // Click every other tick for 3 seconds (30 clicks/s).
            int shots = Run(ref s, ref t, ref seq, 180, i => i % 2 == 0 ? InputButtons.Fire : InputButtons.None, Ready);
            Assert.That(shots, Is.InRange(4, 5), "1.6 shots/s over 3 s");
        }

        [Test]
        public void Firing_IsRejected_WhenNotAllowed_OrSprinting()
        {
            WeaponSimState s = WeaponSimulation.Fresh(_rifle.Definition, _pistol.Definition);
            float t = 0f;
            uint seq = 0;
            Assert.AreEqual(0, Run(ref s, ref t, ref seq, 60, _ => InputButtons.Fire, new WeaponContext { canFire = false, grounded = true }));
            Assert.AreEqual(0, Run(ref s, ref t, ref seq, 60, _ => InputButtons.Fire, new WeaponContext { canFire = true, sprinting = true, grounded = true }));
            Assert.AreEqual(31, s.Magazine(0));
        }

        [Test]
        public void WeaponSwitch_BlocksFireDuringEquip()
        {
            WeaponSimState s = WeaponSimulation.Fresh(_rifle.Definition, _pistol.Definition);
            float t = 0f;
            uint seq = 0;
            Run(ref s, ref t, ref seq, 1, _ => InputButtons.Weapon2, Ready);
            Assert.AreEqual(1, s.activeSlot);
            int during = Run(ref s, ref t, ref seq, 15, _ => InputButtons.Fire, Ready); // 0.25 s < 0.35 s equip
            Assert.AreEqual(0, during);
        }

        [Test]
        public void Spread_IsDeterministicPerSequence()
        {
            Quaternion view = WeaponSimulation.ViewRotation(30f, 5f);
            Vector3 a = WeaponSimulation.ApplySpread(view, 3f, 1234);
            Vector3 b = WeaponSimulation.ApplySpread(view, 3f, 1234);
            Vector3 c = WeaponSimulation.ApplySpread(view, 3f, 1235);
            Assert.AreEqual(a, b);
            Assert.AreNotEqual(a, c);
            Assert.LessOrEqual(Vector3.Angle(view * Vector3.forward, a), 3.01f);
        }
    }

    public sealed class InputCommandTests
    {
        [Test]
        public void Sanitize_ClampsPitch_RejectsNonFinite_AndUnknownButtons()
        {
            var cmd = new PlayerInputCommand { yaw = float.NaN, pitch = 500f, moveX = sbyte.MinValue, buttons = (InputButtons)0xFFFF };
            cmd.Sanitize(-85f, 85f);
            Assert.AreEqual(0f, cmd.yaw);
            Assert.AreEqual(85f, cmd.pitch);
            Assert.AreEqual(-127, cmd.moveX);
            Assert.AreEqual((InputButtons)0x1FFF, cmd.buttons);
        }

        [Test]
        public void SetMove_ClampsToUnitLength()
        {
            var cmd = new PlayerInputCommand();
            cmd.SetMove(new Vector2(5f, 5f));
            Assert.LessOrEqual(cmd.Move.magnitude, 1.01f);
        }
    }
}
