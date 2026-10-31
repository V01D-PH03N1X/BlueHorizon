using BlueHorizon.Core;
using BlueHorizon.Weapons;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BlueHorizon.Tests
{
    public class WeaponModelTests
    {
        [Test]
        public void EveryWeaponHasAModelFacingDownTheBarrel()
        {
            var content = AssetDatabase.LoadAssetAtPath<GameContent>("Assets/_Project/Settings/GameContent.asset");
            Assert.IsNotNull(content);

            foreach (WeaponDefinition weapon in content.weapons)
            {
                WeaponModel model = weapon.modelPrefab;
                Assert.IsNotNull(model, weapon.weaponId);
                Assert.IsNotEmpty(model.GetComponentsInChildren<Renderer>(), weapon.weaponId);
                Assert.AreEqual("Root", model.mount.parent.name, weapon.weaponId);
                Assert.AreEqual(model.mount, model.muzzle.parent, weapon.weaponId);
                Assert.Less(Vector3.Distance(model.mount.position, model.transform.position), 0.001f, weapon.weaponId);
                Vector3 muzzle = model.muzzle.localPosition;
                Assert.Greater(muzzle.z, 0.1f, weapon.weaponId);
                Assert.Less(muzzle.z, 1.2f, weapon.weaponId);
                Assert.Greater(model.sight.localPosition.y, muzzle.y, weapon.weaponId);
            }
        }

        [Test]
        public void EveryModelHasTheClipsThePresentationPlays()
        {
            var content = AssetDatabase.LoadAssetAtPath<GameContent>("Assets/_Project/Settings/GameContent.asset");
            foreach (WeaponDefinition weapon in content.weapons)
            {
                Animation clips = weapon.modelPrefab.clips;
                Assert.IsNotNull(clips, weapon.weaponId);
                foreach (string clip in new[] { WeaponModel.IdleClip, WeaponModel.FireClip, WeaponModel.ReloadClip, WeaponModel.EquipClip })
                    Assert.IsNotNull(clips.GetClip(clip), $"{weapon.weaponId} {clip}");
            }
        }

        [Test]
        public void OnlyTheFixedOpticWeaponHasABuiltInOptic()
        {
            var content = AssetDatabase.LoadAssetAtPath<GameContent>("Assets/_Project/Settings/GameContent.asset");
            foreach (WeaponDefinition weapon in content.weapons)
                Assert.AreEqual(weapon.fixedOptic != null, weapon.modelPrefab.builtInOptic, weapon.weaponId);
        }
    }
}
