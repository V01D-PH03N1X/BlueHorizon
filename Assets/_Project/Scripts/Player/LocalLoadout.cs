using BlueHorizon.Attachments;
using BlueHorizon.Core;
using BlueHorizon.Weapons;
using UnityEngine;

namespace BlueHorizon.Player
{
    /// <summary>
    /// The local player's loadout choice: kept in PlayerPrefs and sent to the server when the player spawns.
    /// Test bots pick a random valid loadout with <c>-botLoadout random</c>. The server validates every request.
    /// </summary>
    public static class LocalLoadout
    {
        const string Key = "bh.loadout";

        public static bool TryGetSaved(out PlayerLoadout loadout) => PlayerLoadout.TryParse(PlayerPrefs.GetString(Key, ""), out loadout);

        public static void Save(in PlayerLoadout loadout)
        {
            PlayerPrefs.SetString(Key, loadout.ToString());
            PlayerPrefs.Save();
        }

        /// <summary>Owner: called once the local avatar has spawned.</summary>
        public static void SendInitial(PlayerAvatar avatar)
        {
            string botLoadout = LaunchConfig.Current.botLoadout;
            if (LaunchConfig.Current.bot && !string.IsNullOrEmpty(botLoadout))
            {
                GameContent content = GameContent.Instance;
                if (content == null) return;
                PlayerLoadout chosen = botLoadout == "random"
                    ? Random(content, new System.Random(unchecked((int)avatar.OwnerClientId * 7919 + System.Environment.TickCount)))
                    : PlayerLoadout.TryParse(botLoadout, out PlayerLoadout parsed) ? parsed : PlayerLoadout.Default;
                BHLog.Info("Loadout", $"Bot requests loadout {chosen}");
                avatar.RequestLoadoutRpc(chosen);
                return;
            }
            if (TryGetSaved(out PlayerLoadout saved) && !saved.Equals(PlayerLoadout.Default))
                avatar.RequestLoadoutRpc(saved);
        }

        /// <summary>A random loadout that passes <see cref="LoadoutRules"/>: any fitting weapons, each allowed attachment at 50 %.</summary>
        public static PlayerLoadout Random(GameContent content, System.Random rng)
        {
            PlayerLoadout loadout = PlayerLoadout.Default;
            for (int slot = 0; slot < 2; slot++)
            {
                var candidates = new System.Collections.Generic.List<int>();
                for (int i = 0; i < content.weapons.Length; i++)
                {
                    if (content.weapons[i] != null && (int)content.weapons[i].slot == slot) candidates.Add(i);
                }
                if (candidates.Count == 0) continue;
                int index = candidates[rng.Next(candidates.Count)];
                loadout.SetSlot(slot, (byte)index);
                WeaponDefinition weapon = content.weapons[index];
                for (int a = 0; a < PlayerLoadout.AttachmentSlots; a++)
                {
                    var attachmentSlot = (AttachmentSlot)a;
                    var options = new System.Collections.Generic.List<AttachmentDefinition>();
                    foreach (AttachmentDefinition att in weapon.allowedAttachments)
                    {
                        if (LoadoutRules.Allows(weapon, att, attachmentSlot)) options.Add(att);
                    }
                    if (options.Count == 0 || rng.Next(2) == 0) continue;
                    loadout.SetAttachment(slot, attachmentSlot, content.IndexOfAttachment(options[rng.Next(options.Count)]));
                }
            }
            return loadout;
        }
    }
}
