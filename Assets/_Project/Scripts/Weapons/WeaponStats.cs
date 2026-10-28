using System.Collections.Generic;
using BlueHorizon.Attachments;
using UnityEngine;

namespace BlueHorizon.Weapons
{
    /// <summary>
    /// The effective, attachment-adjusted values of one equipped weapon.
    /// Computed once when the loadout changes, never per shot.
    /// </summary>
    public readonly struct WeaponStats
    {
        public readonly WeaponDefinition Definition;
        public readonly float DamageMultiplier;
        public readonly float SpreadMultiplier;
        /// <summary>Extra spread multiplier from the hip (blended out while aiming).</summary>
        public readonly float HipSpreadMultiplier;
        /// <summary>Spread multiplier when fully aimed (weapon × attachments).</summary>
        public readonly float AdsSpreadMultiplier;
        public readonly float RecoilMultiplier;
        public readonly float AdsTime;
        public readonly float AdsFovMultiplier;
        public readonly float AdsMoveSpeedMultiplier;
        public readonly bool Suppressed;
        public readonly bool HidesMinimapFireIndicator;
        public readonly float RemoteFireVolume;
        public readonly AttachmentDefinition Optic;
        public readonly AttachmentDefinition Laser;

        public bool Magnified => Optic != null && Optic.magnified;
        public ReticleStyle Reticle => Optic != null ? Optic.reticle : ReticleStyle.None;

        WeaponStats(WeaponDefinition definition, float damage, float spread, float hipSpread, float adsSpread, float recoil,
            float adsTime, float adsFov, float adsMove, bool suppressed, bool hidesMinimap, float remoteVolume,
            AttachmentDefinition optic, AttachmentDefinition laser)
        {
            Definition = definition;
            DamageMultiplier = damage;
            SpreadMultiplier = spread;
            HipSpreadMultiplier = hipSpread;
            AdsSpreadMultiplier = adsSpread;
            RecoilMultiplier = recoil;
            AdsTime = adsTime;
            AdsFovMultiplier = adsFov;
            AdsMoveSpeedMultiplier = adsMove;
            Suppressed = suppressed;
            HidesMinimapFireIndicator = hidesMinimap;
            RemoteFireVolume = remoteVolume;
            Optic = optic;
            Laser = laser;
        }

        /// <param name="attachments">Fitted attachments; the weapon's fixed optic is added automatically.</param>
        public static WeaponStats Compute(WeaponDefinition definition, IReadOnlyList<AttachmentDefinition> attachments)
        {
            float damage = 1f, spread = 1f, hip = 1f, recoil = 1f, remoteVolume = 1f, adsTimeMul = 1f, adsSpreadMul = 1f;
            bool suppressed = false, hidesMinimap = false;
            AttachmentDefinition optic = definition != null ? definition.fixedOptic : null, laser = null;

            void Apply(AttachmentDefinition a)
            {
                damage *= a.damageMultiplier;
                spread *= a.spreadMultiplier;
                hip *= a.hipSpreadMultiplier;
                adsSpreadMul *= a.adsSpreadMultiplier;
                recoil *= a.recoilMultiplier;
                adsTimeMul *= a.adsTimeMultiplier;
                remoteVolume *= a.remoteFireVolumeMultiplier;
                suppressed |= a.suppressesFireSound;
                hidesMinimap |= a.hidesMinimapFireIndicator;
                if (a.visibleLaser) laser = a;
            }

            if (optic != null) Apply(optic);
            if (attachments != null)
            {
                for (int i = 0; i < attachments.Count; i++)
                {
                    AttachmentDefinition a = attachments[i];
                    if (a == null || a == optic) continue;
                    if (a.slot == AttachmentSlot.Optic)
                    {
                        if (definition != null && definition.fixedOptic != null) continue; // Built-in optic wins.
                        optic = a;
                    }
                    Apply(a);
                }
            }

            float adsTime = definition != null ? definition.adsTime * adsTimeMul : 0.2f;
            float adsSpread = (definition != null ? definition.adsSpreadMultiplier : 1f) * adsSpreadMul;
            float adsFov = optic != null && optic.aimFovMultiplier < 1f ? optic.aimFovMultiplier : definition != null ? definition.adsFovMultiplier : 1f;
            float adsMove = definition != null ? definition.adsMoveSpeedMultiplier : 1f;
            return new WeaponStats(definition, damage, spread, hip, adsSpread, recoil, Mathf.Max(0.01f, adsTime), adsFov, adsMove,
                suppressed, hidesMinimap, remoteVolume, optic, laser);
        }
    }
}
