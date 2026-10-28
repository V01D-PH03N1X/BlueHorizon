using System;
using Unity.Netcode;
using UnityEngine;

namespace BlueHorizon.Player
{
    [Flags]
    public enum InputButtons : ushort
    {
        None = 0,
        Jump = 1 << 0,
        Crouch = 1 << 1,
        Sprint = 1 << 2,
        Fire = 1 << 3,
        Reload = 1 << 4,
        Interact = 1 << 5,
        Ability1 = 1 << 6,
        Ability2 = 1 << 7,
        Signature = 1 << 8,
        Weapon1 = 1 << 9,
        Weapon2 = 1 << 10,
        ToggleAttachment = 1 << 11,
        Aim = 1 << 12,
    }

    /// <summary>
    /// One simulation tick of player intent. This is the only gameplay data a client sends for its character:
    /// the server simulates movement, firing and abilities from these commands and never accepts positions,
    /// hits or damage from clients. Movement axes are quantized so both sides simulate identical values.
    /// </summary>
    public struct PlayerInputCommand : INetworkSerializable
    {
        public uint sequence;
        public sbyte moveX;
        public sbyte moveY;
        public float yaw;
        public float pitch;
        public InputButtons buttons;
        /// <summary>
        /// Server time the client was rendering other players at when it built this command (its interpolation time).
        /// Used only for lag compensation, and always clamped by the server (see LagCompensation.ClampViewTime).
        /// </summary>
        public double viewTime;

        public Vector2 Move => new(moveX / 127f, moveY / 127f);

        public bool Has(InputButtons b) => (buttons & b) != 0;

        public void SetMove(Vector2 move)
        {
            move = Vector2.ClampMagnitude(move, 1f);
            moveX = (sbyte)Mathf.RoundToInt(move.x * 127f);
            moveY = (sbyte)Mathf.RoundToInt(move.y * 127f);
        }

        /// <summary>Clamps values a malicious client could abuse (pitch range, non-finite yaw, unknown bits).</summary>
        public void Sanitize(float minPitch, float maxPitch)
        {
            if (!float.IsFinite(yaw)) yaw = 0f;
            if (!float.IsFinite(pitch)) pitch = 0f;
            if (!double.IsFinite(viewTime)) viewTime = 0;
            yaw = Mathf.Repeat(yaw, 360f);
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            if (moveX == sbyte.MinValue) moveX = -127;
            if (moveY == sbyte.MinValue) moveY = -127;
            buttons &= (InputButtons)0x1FFF;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref sequence);
            serializer.SerializeValue(ref moveX);
            serializer.SerializeValue(ref moveY);
            serializer.SerializeValue(ref yaw);
            serializer.SerializeValue(ref pitch);
            serializer.SerializeValue(ref buttons);
            serializer.SerializeValue(ref viewTime);
        }
    }

    /// <summary>
    /// The latest few commands, sent unreliably every tick so a single lost packet does not lose input.
    /// Fixed-size to avoid per-message allocations.
    /// </summary>
    public struct InputBundle : INetworkSerializable
    {
        public const int Capacity = 4;

        public byte count;
        public PlayerInputCommand c0, c1, c2, c3;

        public PlayerInputCommand this[int index]
        {
            get => index switch { 0 => c0, 1 => c1, 2 => c2, _ => c3 };
            set
            {
                switch (index)
                {
                    case 0: c0 = value; break;
                    case 1: c1 = value; break;
                    case 2: c2 = value; break;
                    default: c3 = value; break;
                }
            }
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref count);
            if (count > Capacity) count = Capacity;
            for (int i = 0; i < count; i++)
            {
                PlayerInputCommand c = serializer.IsWriter ? this[i] : default;
                serializer.SerializeNetworkSerializable(ref c);
                if (serializer.IsReader) this[i] = c;
            }
        }
    }
}
