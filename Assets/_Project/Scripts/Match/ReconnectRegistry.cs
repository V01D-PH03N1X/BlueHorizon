using System.Collections.Generic;
using BlueHorizon.Core;

namespace BlueHorizon.Match
{
    /// <summary>What a player keeps when they drop out of a running match and come back.</summary>
    public struct ReconnectRecord
    {
        public Team team;
        public int kills;
        public int deaths;
        public int objectiveScore;
        public Player.PlayerLoadout preferredLoadout;
        public int matchId;
        public double disconnectTime;
    }

    /// <summary>
    /// Server-side memory of players who disconnected during a match, keyed by display name.
    /// There are no accounts yet, so the name is the identity (see RISKS.md L-16); records expire after a grace window
    /// and are discarded when the match ends.
    /// </summary>
    public sealed class ReconnectRegistry
    {
        readonly Dictionary<string, ReconnectRecord> _records = new();

        public int Count => _records.Count;

        public void Store(string playerName, ReconnectRecord record) => _records[playerName] = record;

        public bool TryTake(string playerName, int matchId, double now, double graceSeconds, out ReconnectRecord record)
        {
            if (_records.TryGetValue(playerName, out record))
            {
                _records.Remove(playerName);
                if (record.matchId == matchId && now - record.disconnectTime <= graceSeconds) return true;
            }
            record = default;
            return false;
        }

        public void Clear() => _records.Clear();
    }
}
