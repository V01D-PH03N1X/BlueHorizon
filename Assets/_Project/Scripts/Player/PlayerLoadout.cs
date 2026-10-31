using System;
using BlueHorizon.Attachments;
using Unity.Netcode;

namespace BlueHorizon.Player
{
    /// <summary>
    /// The weapons a player carries (indices into <c>GameContent.weapons</c>) and the attachments fitted to each
    /// (indices into <c>GameContent.attachments</c>, one per attachment slot). Chosen by the player and the game mode
    /// on the server and replicated, so every peer computes the same weapon stats.
    /// </summary>
    public struct PlayerLoadout : INetworkSerializable, IEquatable<PlayerLoadout>
    {
        public const byte None = 255;
        public const int AttachmentSlots = 3;

        public byte primary;
        public byte secondary;
        public byte primaryMuzzle, primaryUnderbarrel, primaryOptic;
        public byte secondaryMuzzle, secondaryUnderbarrel, secondaryOptic;

        public static PlayerLoadout Default => Weapons(0, 1);

        /// <summary>A loadout with these weapons and no attachments.</summary>
        public static PlayerLoadout Weapons(byte primary, byte secondary) => new()
        {
            primary = primary,
            secondary = secondary,
            primaryMuzzle = None, primaryUnderbarrel = None, primaryOptic = None,
            secondaryMuzzle = None, secondaryUnderbarrel = None, secondaryOptic = None,
        };

        public byte Slot(int slot) => slot == 0 ? primary : secondary;

        public void SetSlot(int slot, byte weapon)
        {
            if (slot == 0) primary = weapon; else secondary = weapon;
        }

        public byte Attachment(int weaponSlot, AttachmentSlot slot) => (weaponSlot, slot) switch
        {
            (0, AttachmentSlot.Muzzle) => primaryMuzzle,
            (0, AttachmentSlot.Underbarrel) => primaryUnderbarrel,
            (0, AttachmentSlot.Optic) => primaryOptic,
            (1, AttachmentSlot.Muzzle) => secondaryMuzzle,
            (1, AttachmentSlot.Underbarrel) => secondaryUnderbarrel,
            (1, AttachmentSlot.Optic) => secondaryOptic,
            _ => None,
        };

        public void SetAttachment(int weaponSlot, AttachmentSlot slot, byte index)
        {
            switch (weaponSlot, slot)
            {
                case (0, AttachmentSlot.Muzzle): primaryMuzzle = index; break;
                case (0, AttachmentSlot.Underbarrel): primaryUnderbarrel = index; break;
                case (0, AttachmentSlot.Optic): primaryOptic = index; break;
                case (1, AttachmentSlot.Muzzle): secondaryMuzzle = index; break;
                case (1, AttachmentSlot.Underbarrel): secondaryUnderbarrel = index; break;
                case (1, AttachmentSlot.Optic): secondaryOptic = index; break;
            }
        }

        public void ClearAttachments(int weaponSlot)
        {
            for (int a = 0; a < AttachmentSlots; a++) SetAttachment(weaponSlot, (AttachmentSlot)a, None);
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref primary);
            serializer.SerializeValue(ref secondary);
            serializer.SerializeValue(ref primaryMuzzle);
            serializer.SerializeValue(ref primaryUnderbarrel);
            serializer.SerializeValue(ref primaryOptic);
            serializer.SerializeValue(ref secondaryMuzzle);
            serializer.SerializeValue(ref secondaryUnderbarrel);
            serializer.SerializeValue(ref secondaryOptic);
        }

        public bool Equals(PlayerLoadout other) =>
            primary == other.primary && secondary == other.secondary
            && primaryMuzzle == other.primaryMuzzle && primaryUnderbarrel == other.primaryUnderbarrel && primaryOptic == other.primaryOptic
            && secondaryMuzzle == other.secondaryMuzzle && secondaryUnderbarrel == other.secondaryUnderbarrel && secondaryOptic == other.secondaryOptic;

        /// <summary>Compact text form for PlayerPrefs and logs: "p,s,pm,pu,po,sm,su,so".</summary>
        public override string ToString() =>
            $"{primary},{secondary},{primaryMuzzle},{primaryUnderbarrel},{primaryOptic},{secondaryMuzzle},{secondaryUnderbarrel},{secondaryOptic}";

        public static bool TryParse(string text, out PlayerLoadout loadout)
        {
            loadout = Default;
            if (string.IsNullOrEmpty(text)) return false;
            string[] parts = text.Split(',');
            if (parts.Length != 8) return false;
            var v = new byte[8];
            for (int i = 0; i < 8; i++)
            {
                if (!byte.TryParse(parts[i], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out v[i]))
                    return false;
            }
            loadout = new PlayerLoadout
            {
                primary = v[0], secondary = v[1],
                primaryMuzzle = v[2], primaryUnderbarrel = v[3], primaryOptic = v[4],
                secondaryMuzzle = v[5], secondaryUnderbarrel = v[6], secondaryOptic = v[7],
            };
            return true;
        }
    }
}
