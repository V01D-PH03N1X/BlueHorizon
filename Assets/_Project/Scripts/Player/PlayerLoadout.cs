using System;
using Unity.Netcode;

namespace BlueHorizon.Player
{
    /// <summary>
    /// The weapons a player carries, as indices into <c>GameContent.weapons</c>. Chosen by the game mode on the server
    /// and replicated, so every peer computes the same weapon stats.
    /// </summary>
    public struct PlayerLoadout : INetworkSerializable, IEquatable<PlayerLoadout>
    {
        public const byte None = 255;
        /// <summary>Value for <see cref="suppressorMask"/> meaning "keep the player's current choice".</summary>
        public const byte KeepSuppressors = 255;

        public byte primary;
        public byte secondary;
        public byte suppressorMask;

        public static PlayerLoadout Default => new() { primary = 0, secondary = 1, suppressorMask = KeepSuppressors };

        public byte Slot(int slot) => slot == 0 ? primary : secondary;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref primary);
            serializer.SerializeValue(ref secondary);
            serializer.SerializeValue(ref suppressorMask);
        }

        public bool Equals(PlayerLoadout other) =>
            primary == other.primary && secondary == other.secondary && suppressorMask == other.suppressorMask;
    }
}
