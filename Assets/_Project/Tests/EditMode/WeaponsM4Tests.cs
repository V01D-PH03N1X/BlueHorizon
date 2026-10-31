using System.Collections.Generic;
using BlueHorizon.Attachments;
using BlueHorizon.Combat;
using BlueHorizon.Core;
using BlueHorizon.Player;
using BlueHorizon.Weapons;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BlueHorizon.Tests
{
    /// <summary>Milestone 4: pellets, aim down sights, optics, lasers and loadout validation.</summary>
    public sealed class WeaponsM4Tests
    {
        const float Dt = 1f / 60f;

        static WeaponDefinition Shotgun()
        {
            WeaponDefinition w = TestData.Rifle();
            w.weaponId = "shotgun";
            w.category = WeaponCategory.Shotgun;
            w.fireMode = FireMode.SemiAutomatic;
            w.fireRate = 1.1f;
            w.pellets = 8;
            w.pelletSpread = 4.5f;
            w.damage = new ZoneDamage { head = 30, torso = 20, legs = 12 };
            w.falloff = new RangeFalloff { falloffStart = 8f, falloffEnd = 25f, minMultiplier = 0.25f };
            return w;
        }

        static WeaponDefinition Sniper(AttachmentDefinition scope)
        {
            WeaponDefinition w = TestData.Rifle();
            w.weaponId = "sniper";
            w.category = WeaponCategory.SniperRifle;
            w.fireMode = FireMode.SemiAutomatic;
            w.fireRate = 0.75f;
            w.magazineSize = 5;
            w.damage = new ZoneDamage { head = 300, torso = 150, legs = 90 };
            w.spread = new SpreadSettings { baseSpread = 6f, movingSpread = 20f, airborneSpread = 30f, crouchMultiplier = 1f };
            w.adsTime = 0.35f;
            w.adsSpreadMultiplier = 0.015f;
            w.adsMoveSpeedMultiplier = 0.5f;
            w.fixedOptic = scope;
            return w;
        }

        static AttachmentDefinition Optic(string id, float fov, bool magnified, ReticleStyle reticle, float adsTime = 1f)
        {
            var a = ScriptableObject.CreateInstance<AttachmentDefinition>();
            a.attachmentId = id;
            a.slot = AttachmentSlot.Optic;
            a.aimFovMultiplier = fov;
            a.magnified = magnified;
            a.reticle = reticle;
            a.adsTimeMultiplier = adsTime;
            return a;
        }

        static AttachmentDefinition Laser()
        {
            var a = ScriptableObject.CreateInstance<AttachmentDefinition>();
            a.attachmentId = "laser";
            a.slot = AttachmentSlot.Underbarrel;
            a.hipSpreadMultiplier = 0.7f;
            a.visibleLaser = true;
            return a;
        }

        static PlayerInputCommand Cmd(uint seq, InputButtons buttons) => new() { sequence = seq, buttons = buttons };

        static WeaponContext Ready => new() { canFire = true, grounded = true };

        // ───────────── Pellets ─────────────

        [Test]
        public void Shotgun_FiresEightPellets_InsideTheCone_Deterministically()
        {
            WeaponStats stats = WeaponStats.Compute(Shotgun(), null);
            WeaponStats pistol = WeaponStats.Compute(TestData.Pistol(), null);
            WeaponSimState s = WeaponSimulation.Fresh(stats.Definition, pistol.Definition);
            WeaponSimulation.Step(ref s, Cmd(42, InputButtons.Fire), 1f, Dt, stats, pistol, Ready, out ShotEvent shot);

            Assert.IsTrue(shot.fired);
            Assert.AreEqual(8, shot.pellets);
            Assert.AreEqual(shot.direction, WeaponSimulation.PelletDirection(shot, 0), "pellet 0 is the aimed direction");
            var seen = new HashSet<Vector3>();
            for (int i = 0; i < shot.pellets; i++)
            {
                Vector3 d = WeaponSimulation.PelletDirection(shot, i);
                Assert.LessOrEqual(Vector3.Angle(shot.direction, d), shot.pelletSpread + 0.01f);
                Assert.AreEqual(d, WeaponSimulation.PelletDirection(shot, i), "same seed → same pellet");
                seen.Add(d);
            }
            Assert.AreEqual(8, seen.Count, "pellets scatter");
        }

        [Test]
        public void Shotgun_PelletDamage_SumsAndFallsOffWithRange()
        {
            WeaponStats stats = WeaponStats.Compute(Shotgun(), null);
            int close = 0, far = 0;
            for (int i = 0; i < 8; i++)
            {
                close += DamageResolver.Compute(stats, HitZone.Torso, 5f);
                far += DamageResolver.Compute(stats, HitZone.Torso, 30f);
            }
            Assert.AreEqual(160, close, "8 × 20 inside 8 m");
            Assert.AreEqual(40, far, "8 × 5 beyond 25 m");
        }

        // ───────────── Aim down sights ─────────────

        [Test]
        public void Ads_ReachesFullProgressAfterAdsTime_AndReturnsWhenReleased()
        {
            WeaponStats rifle = WeaponStats.Compute(TestData.Rifle(), null);
            WeaponStats pistol = WeaponStats.Compute(TestData.Pistol(), null);
            WeaponSimState s = WeaponSimulation.Fresh(rifle.Definition, pistol.Definition);
            float t = 1f;
            uint seq = 0;
            int ticks = Mathf.CeilToInt(rifle.AdsTime / Dt);
            for (int i = 0; i < ticks - 2; i++) WeaponSimulation.Step(ref s, Cmd(++seq, InputButtons.Aim), t += Dt, Dt, rifle, pistol, Ready, out _);
            Assert.Less(s.adsProgress, 1f);
            for (int i = 0; i < 3; i++) WeaponSimulation.Step(ref s, Cmd(++seq, InputButtons.Aim), t += Dt, Dt, rifle, pistol, Ready, out _);
            Assert.AreEqual(1f, s.adsProgress, 1e-4f);
            for (int i = 0; i < ticks; i++) WeaponSimulation.Step(ref s, Cmd(++seq, InputButtons.None), t += Dt, Dt, rifle, pistol, Ready, out _);
            Assert.AreEqual(0f, s.adsProgress, 1e-4f);
        }

        [Test]
        public void Ads_IsBlockedWhileSprintingOrReloading()
        {
            WeaponStats rifle = WeaponStats.Compute(TestData.Rifle(), null);
            WeaponStats pistol = WeaponStats.Compute(TestData.Pistol(), null);
            WeaponSimState s = WeaponSimulation.Fresh(rifle.Definition, pistol.Definition);
            var sprinting = new WeaponContext { canFire = true, grounded = true, sprinting = true };
            for (int i = 0; i < 30; i++) WeaponSimulation.Step(ref s, Cmd((uint)i, InputButtons.Aim), 1f + i * Dt, Dt, rifle, pistol, sprinting, out _);
            Assert.AreEqual(0f, s.adsProgress);
        }

        [Test]
        public void Sniper_IsInaccurateFromTheHip_AndPreciseWhenScoped()
        {
            AttachmentDefinition scope = Optic("scope8", 0.14f, true, ReticleStyle.Scope);
            WeaponStats sniper = WeaponStats.Compute(Sniper(scope), null);
            float hip = WeaponSimulation.SpreadAngle(sniper.Definition, sniper, 0, Ready, 0f);
            float scoped = WeaponSimulation.SpreadAngle(sniper.Definition, sniper, 0, Ready, 1f);
            Assert.AreEqual(6f, hip, 1e-4f);
            Assert.Less(scoped, 0.1f);
            Assert.IsTrue(sniper.Magnified);
            Assert.AreEqual(0.14f, sniper.AdsFovMultiplier, 1e-5f);
            Assert.AreEqual(0.5f, sniper.AdsMoveSpeedMultiplier, 1e-5f);
        }

        [Test]
        public void Sniper_BoltCadence_LimitsToOneShotPer1Point33Seconds()
        {
            WeaponStats sniper = WeaponStats.Compute(Sniper(null), null);
            WeaponStats pistol = WeaponStats.Compute(TestData.Pistol(), null);
            WeaponSimState s = WeaponSimulation.Fresh(sniper.Definition, pistol.Definition);
            int shots = 0;
            float t = 1f;
            var times = new List<float>();
            // Mash the trigger every other tick for 4 seconds.
            for (int i = 0; i < 240; i++)
            {
                t += Dt;
                WeaponSimulation.Step(ref s, Cmd((uint)i, (i & 1) == 0 ? InputButtons.Fire : InputButtons.None), t, Dt, sniper, pistol, Ready, out ShotEvent shot);
                if (shot.fired)
                {
                    shots++;
                    times.Add(t);
                }
            }
            Assert.AreEqual(3, shots, "0.75 shots/s over 4 s (first shot immediate)");
            for (int i = 1; i < times.Count; i++) Assert.GreaterOrEqual(times[i] - times[i - 1], 1f / 0.75f - Dt - 1e-3f, "cadence carry-over may absorb at most one tick");
        }

        // ───────────── Attachments ─────────────

        [Test]
        public void FixedOptic_WinsOverAFittedOptic()
        {
            AttachmentDefinition scope = Optic("scope8", 0.14f, true, ReticleStyle.Scope);
            AttachmentDefinition redDot = Optic("reddot", 0.85f, false, ReticleStyle.Dot);
            WeaponStats stats = WeaponStats.Compute(Sniper(scope), new List<AttachmentDefinition> { redDot });
            Assert.AreSame(scope, stats.Optic);
            Assert.AreEqual(ReticleStyle.Scope, stats.Reticle);
        }

        [Test]
        public void RedDot_SetsReticleAndZoom_ScopeSlowsAds()
        {
            WeaponDefinition rifle = TestData.Rifle();
            WeaponStats dot = WeaponStats.Compute(rifle, new List<AttachmentDefinition> { Optic("reddot", 0.85f, false, ReticleStyle.Dot, 0.95f) });
            WeaponStats scope = WeaponStats.Compute(rifle, new List<AttachmentDefinition> { Optic("scope4", 0.27f, true, ReticleStyle.Scope, 1.6f) });
            Assert.AreEqual(ReticleStyle.Dot, dot.Reticle);
            Assert.IsFalse(dot.Magnified);
            Assert.AreEqual(0.85f, dot.AdsFovMultiplier, 1e-5f);
            Assert.AreEqual(rifle.adsTime * 0.95f, dot.AdsTime, 1e-5f);
            Assert.IsTrue(scope.Magnified);
            Assert.AreEqual(rifle.adsTime * 1.6f, scope.AdsTime, 1e-5f);
        }

        [Test]
        public void Laser_TightensHipFireOnly()
        {
            WeaponDefinition rifle = TestData.Rifle();
            WeaponStats plain = WeaponStats.Compute(rifle, null);
            WeaponStats laser = WeaponStats.Compute(rifle, new List<AttachmentDefinition> { Laser() });
            Assert.IsNotNull(laser.Laser);
            float hipPlain = WeaponSimulation.SpreadAngle(rifle, plain, 0, Ready, 0f);
            float hipLaser = WeaponSimulation.SpreadAngle(rifle, laser, 0, Ready, 0f);
            Assert.AreEqual(hipPlain * 0.7f, hipLaser, 1e-5f);
            Assert.AreEqual(WeaponSimulation.SpreadAngle(rifle, plain, 0, Ready, 1f), WeaponSimulation.SpreadAngle(rifle, laser, 0, Ready, 1f), 1e-5f);
        }

        // ───────────── Loadouts ─────────────

        sealed class Catalog
        {
            public readonly WeaponDefinition[] Weapons;
            public readonly AttachmentDefinition[] Attachments;
            public readonly AttachmentDefinition Suppressor, Laser, RedDot, Scope;

            public Catalog()
            {
                Suppressor = TestData.Suppressor();
                Suppressor.slot = AttachmentSlot.Muzzle;
                Laser = WeaponsM4Tests.Laser();
                RedDot = Optic("reddot", 0.85f, false, ReticleStyle.Dot);
                Scope = Optic("scope8", 0.14f, true, ReticleStyle.Scope);
                WeaponDefinition rifle = TestData.Rifle();
                rifle.allowedAttachments = new[] { Suppressor, Laser, RedDot };
                WeaponDefinition pistol = TestData.Pistol();
                pistol.allowedAttachments = new[] { Suppressor, RedDot };
                WeaponDefinition sniper = Sniper(Scope);
                sniper.allowedAttachments = new[] { Suppressor, RedDot };
                Weapons = new[] { rifle, pistol, sniper };
                Attachments = new[] { Suppressor, Laser, RedDot, Scope };
            }
        }

        [Test]
        public void LoadoutRules_KeepsAValidLoadout()
        {
            var c = new Catalog();
            PlayerLoadout l = PlayerLoadout.Weapons(0, 1);
            l.primaryMuzzle = 0;
            l.primaryUnderbarrel = 1;
            l.primaryOptic = 2;
            l.secondaryOptic = 2;
            Assert.IsTrue(LoadoutRules.IsValid(l, c.Weapons, c.Attachments));
        }

        [Test]
        public void LoadoutRules_RejectsWrongSlotsUnknownIndicesAndDisallowedAttachments()
        {
            var c = new Catalog();
            PlayerLoadout l = PlayerLoadout.Weapons(1, 0); // pistol as primary, rifle as secondary
            PlayerLoadout fixedSlots = LoadoutRules.Sanitize(l, c.Weapons, c.Attachments);
            Assert.AreEqual(0, fixedSlots.primary);
            Assert.AreEqual(1, fixedSlots.secondary);

            l = PlayerLoadout.Weapons(200, 1);
            Assert.AreEqual(0, LoadoutRules.Sanitize(l, c.Weapons, c.Attachments).primary);

            l = PlayerLoadout.Weapons(0, 1);
            l.primaryMuzzle = 2;       // red dot in the muzzle slot
            l.secondaryUnderbarrel = 1; // laser not allowed on this pistol
            l.primaryOptic = 3;         // scope not in the rifle's list
            l.secondaryMuzzle = 77;     // unknown index
            PlayerLoadout s = LoadoutRules.Sanitize(l, c.Weapons, c.Attachments);
            Assert.AreEqual(PlayerLoadout.None, s.primaryMuzzle);
            Assert.AreEqual(PlayerLoadout.None, s.secondaryUnderbarrel);
            Assert.AreEqual(PlayerLoadout.None, s.primaryOptic);
            Assert.AreEqual(PlayerLoadout.None, s.secondaryMuzzle);
        }

        [Test]
        public void LoadoutRules_BuiltInScopeBlocksOtherOptics()
        {
            var c = new Catalog();
            PlayerLoadout l = PlayerLoadout.Weapons(2, 1);
            l.primaryOptic = 2; // red dot on the sniper
            l.primaryMuzzle = 0;
            PlayerLoadout s = LoadoutRules.Sanitize(l, c.Weapons, c.Attachments);
            Assert.AreEqual(PlayerLoadout.None, s.primaryOptic);
            Assert.AreEqual(0, s.primaryMuzzle);
        }

        [Test]
        public void Loadout_TextRoundTrip()
        {
            PlayerLoadout l = PlayerLoadout.Weapons(4, 2);
            l.primaryOptic = 3;
            l.secondaryUnderbarrel = 1;
            Assert.IsTrue(PlayerLoadout.TryParse(l.ToString(), out PlayerLoadout back));
            Assert.AreEqual(l, back);
            Assert.IsFalse(PlayerLoadout.TryParse("1,2,x", out _));
            Assert.IsFalse(PlayerLoadout.TryParse("1,2,3,4,5,6,7,999", out _));
        }

        [Test]
        public void RandomBotLoadout_IsAlwaysValid()
        {
            var c = new Catalog();
            var content = ScriptableObject.CreateInstance<GameContent>();
            content.weapons = c.Weapons;
            content.attachments = c.Attachments;
            var rng = new System.Random(7);
            for (int i = 0; i < 200; i++)
            {
                PlayerLoadout l = LocalLoadout.Random(content, rng);
                Assert.IsTrue(LoadoutRules.IsValid(l, c.Weapons, c.Attachments), l.ToString());
            }
        }

        // ───────────── Generated content ─────────────

        const string WeaponsPath = "Assets/_Project/ScriptableObjects/Weapons/";

        static WeaponDefinition LoadWeapon(string name)
        {
            var w = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(WeaponsPath + name + ".asset");
            if (w == null) Assert.Ignore($"{name}.asset not generated yet (run Tools/build.ps1 -Generate)");
            return w;
        }

        [TestCase("Revolver", 140, 60, 40, 1.1f, 6, WeaponSlot.Secondary)]
        [TestCase("Shotgun", 30, 20, 12, 1.1f, 6, WeaponSlot.Primary)]
        [TestCase("PrecisionRifle", 180, 75, 50, 2.5f, 12, WeaponSlot.Primary)]
        [TestCase("SniperRifle", 300, 150, 90, 0.75f, 5, WeaponSlot.Primary)]
        public void GeneratedWeapons_MatchTheBalanceProposal(string asset, int head, int torso, int legs, float rate, int magazine, WeaponSlot slot)
        {
            WeaponDefinition w = LoadWeapon(asset);
            Assert.AreEqual(head, w.damage.head);
            Assert.AreEqual(torso, w.damage.torso);
            Assert.AreEqual(legs, w.damage.legs);
            Assert.AreEqual(rate, w.fireRate, 1e-4f);
            Assert.AreEqual(magazine, w.magazineSize);
            Assert.AreEqual(slot, w.slot);
            Assert.AreEqual(FireMode.SemiAutomatic, w.fireMode);
        }

        [Test]
        public void GeneratedContent_KeepsWeaponIndicesStable_AndSniperHasItsScope()
        {
            var content = AssetDatabase.LoadAssetAtPath<GameContent>("Assets/_Project/Settings/GameContent.asset");
            if (content == null || content.weapons == null || content.weapons.Length < 6) Assert.Ignore("Content not generated yet");
            Assert.AreEqual("assault_rifle", content.weapons[0].weaponId);
            Assert.AreEqual("pistol", content.weapons[1].weaponId);
            WeaponDefinition sniper = content.weapons[5];
            Assert.AreEqual(WeaponCategory.SniperRifle, sniper.category);
            Assert.IsNotNull(sniper.fixedOptic);
            Assert.IsTrue(sniper.fixedOptic.magnified);
            Assert.AreEqual(8, content.weapons[3].pellets);
            foreach (WeaponDefinition w in content.weapons)
            {
                Assert.IsTrue(LoadoutRules.IsValid(PlayerLoadout.Weapons((byte)(w.slot == WeaponSlot.Primary ? System.Array.IndexOf(content.weapons, w) : 0),
                    (byte)(w.slot == WeaponSlot.Secondary ? System.Array.IndexOf(content.weapons, w) : 1)), content.weapons, content.attachments), w.weaponId);
            }
        }
    }
}
