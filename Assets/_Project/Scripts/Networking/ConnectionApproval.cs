using System;
using System.Collections.Generic;
using System.Text;
using BlueHorizon.Core;
using UnityEngine;

namespace BlueHorizon.Networking
{
    /// <summary>Data a client sends when connecting. Contains no credentials.</summary>
    [Serializable]
    public struct ConnectionPayload
    {
        /// <summary>
        /// Bumped whenever client/server gameplay messages change incompatibly (input commands, RPCs, NetworkVariable
        /// types, network prefabs). 2: lag-comp view time (M2). 3: game modes (M3). 4: loadouts, ADS, pellets (M4).
        /// 5: agents, flash/alert/revive RPCs, downed state (M5).
        /// </summary>
        public const int CurrentProtocol = 5;

        public int protocol;
        public string playerName;

        public byte[] ToBytes() => Encoding.UTF8.GetBytes(JsonUtility.ToJson(this));

        public static bool TryParse(byte[] bytes, out ConnectionPayload payload)
        {
            payload = default;
            if (bytes == null || bytes.Length == 0 || bytes.Length > 512) return false;
            try
            {
                payload = JsonUtility.FromJson<ConnectionPayload>(Encoding.UTF8.GetString(bytes));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>Server-side record of approved connections (client id → display name).</summary>
    public static class ConnectionRegistry
    {
        static readonly Dictionary<ulong, string> Names = new();

        public static void Register(ulong clientId, string name) => Names[clientId] = name;
        public static void Unregister(ulong clientId) => Names.Remove(clientId);
        public static void Clear() => Names.Clear();

        public static string NameOf(ulong clientId) =>
            Names.TryGetValue(clientId, out string n) ? n : $"Player{clientId}";

        public static int Count => Names.Count;

        /// <summary>Validates the payload and returns a reject reason, or null when approved.</summary>
        public static string Validate(in ConnectionPayload payload, bool parsed, int connectedPlayers, int maxPlayers)
        {
            if (!parsed) return "Malformed connection request.";
            if (payload.protocol != ConnectionPayload.CurrentProtocol)
                return $"Version mismatch (client {payload.protocol}, server {ConnectionPayload.CurrentProtocol}). Please update.";
            if (connectedPlayers >= maxPlayers) return "Server is full.";
            return null;
        }

        public static string SanitizeName(string raw, ulong clientId)
        {
            if (string.IsNullOrWhiteSpace(raw)) return $"Player{clientId}";
            var sb = new StringBuilder(16);
            foreach (char c in raw.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ' ') sb.Append(c);
                if (sb.Length >= 16) break;
            }
            return sb.Length == 0 ? $"Player{clientId}" : sb.ToString();
        }
    }
}
