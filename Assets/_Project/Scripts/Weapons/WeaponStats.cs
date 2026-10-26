using System.Collections.Generic;
using BlueHorizon.Attachments;

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
        public readonly float RecoilMultiplier;
        public readonly bool Suppressed;
        public readonly bool HidesMinimapFireIndicator;
        public readonly float RemoteFireVolume;

        WeaponStats(WeaponDefinition definition, float damage, float spread, float recoil,
            bool suppressed, bool hidesMinimap, float remoteVolume)
        {
            Definition = definition;
            DamageMultiplier = damage;
            SpreadMultiplier = spread;
            RecoilMultiplier = recoil;
            Suppressed = suppressed;
            HidesMinimapFireIndicator = hidesMinimap;
            RemoteFireVolume = remoteVolume;
        }

        public static WeaponStats Compute(WeaponDefinition definition, IReadOnlyList<AttachmentDefinition> attachments)
        {
            float damage = 1f, spread = 1f, recoil = 1f, remoteVolume = 1f;
            bool suppressed = false, hidesMinimap = false;
            if (attachments != null)
            {
                for (int i = 0; i < attachments.Count; i++)
                {
                    AttachmentDefinition a = attachments[i];
                    if (a == null) continue;
                    damage *= a.damageMultiplier;
                    spread *= a.spreadMultiplier;
                    recoil *= a.recoilMultiplier;
                    remoteVolume *= a.remoteFireVolumeMultiplier;
                    suppressed |= a.suppressesFireSound;
                    hidesMinimap |= a.hidesMinimapFireIndicator;
                }
            }
            return new WeaponStats(definition, damage, spread, recoil, suppressed, hidesMinimap, remoteVolume);
        }
    }
}
