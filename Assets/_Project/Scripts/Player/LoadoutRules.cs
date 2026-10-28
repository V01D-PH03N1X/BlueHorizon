using System.Collections.Generic;
using BlueHorizon.Attachments;
using BlueHorizon.Weapons;

namespace BlueHorizon.Player
{
    /// <summary>
    /// Server-side validation of loadouts requested by players. Clients may send anything; the server keeps only
    /// weapons that exist and fit their slot, and attachments the weapon allows in the matching attachment slot.
    /// Mode-provided loadouts (Weapon Game) are trusted and not passed through here.
    /// </summary>
    public static class LoadoutRules
    {
        public static PlayerLoadout Sanitize(in PlayerLoadout requested, IReadOnlyList<WeaponDefinition> weapons,
            IReadOnlyList<AttachmentDefinition> attachments)
        {
            PlayerLoadout result = requested;
            PlayerLoadout fallback = PlayerLoadout.Default;
            for (int slot = 0; slot < 2; slot++)
            {
                WeaponDefinition weapon = Get(weapons, requested.Slot(slot));
                if (weapon == null || (int)weapon.slot != slot)
                {
                    result.SetSlot(slot, fallback.Slot(slot));
                    result.ClearAttachments(slot);
                    continue;
                }
                for (int a = 0; a < PlayerLoadout.AttachmentSlots; a++)
                {
                    var attachmentSlot = (AttachmentSlot)a;
                    byte index = requested.Attachment(slot, attachmentSlot);
                    if (index == PlayerLoadout.None) continue;
                    if (!Allows(weapon, Get(attachments, index), attachmentSlot))
                        result.SetAttachment(slot, attachmentSlot, PlayerLoadout.None);
                }
            }
            return result;
        }

        public static bool IsValid(in PlayerLoadout loadout, IReadOnlyList<WeaponDefinition> weapons,
            IReadOnlyList<AttachmentDefinition> attachments) =>
            Sanitize(loadout, weapons, attachments).Equals(loadout);

        /// <summary>The weapon accepts this attachment in this slot (a built-in optic blocks other optics).</summary>
        public static bool Allows(WeaponDefinition weapon, AttachmentDefinition attachment, AttachmentSlot slot)
        {
            if (weapon == null || attachment == null || attachment.slot != slot) return false;
            if (slot == AttachmentSlot.Optic && weapon.fixedOptic != null) return false;
            if (weapon.allowedAttachments == null) return false;
            foreach (AttachmentDefinition allowed in weapon.allowedAttachments)
            {
                if (allowed == attachment) return true;
            }
            return false;
        }

        static T Get<T>(IReadOnlyList<T> list, byte index) where T : class =>
            list != null && index < list.Count ? list[index] : null;
    }
}
