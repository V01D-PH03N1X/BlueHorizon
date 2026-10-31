using System;

namespace BlueHorizon.Agents
{
    public enum AgentRejectReason : byte
    {
        None = 0,
        Unknown,
        Locked,
        Banned,
    }

    /// <summary>
    /// Which agents a player may pick. Agents are indices into the append-only <c>GameContent.agents</c>; availability is
    /// two server-set bit masks (bit i = agent i) replicated by the MatchManager:
    ///   * unlocked — agents with <see cref="AgentDefinition.unlockedByDefault"/>, plus those the server unlocks with
    ///     <c>-unlockAgents</c> (there is no progression yet, so unlocks are a server setting);
    ///   * banned — agents the server bans for the session with <c>-banAgents</c>.
    /// Pure so it can be unit-tested; at most 32 agents.
    /// </summary>
    public static class AgentRules
    {
        public const int MaxAgents = 32;

        public static uint UnlockMask(AgentDefinition[] agents, string unlockList)
        {
            uint mask = ListMask(agents, unlockList);
            for (int i = 0; agents != null && i < agents.Length && i < MaxAgents; i++)
                if (agents[i] != null && agents[i].unlockedByDefault) mask |= 1u << i;
            return mask;
        }

        public static uint BanMask(AgentDefinition[] agents, string banList) => ListMask(agents, banList);

        /// <summary>Bits of the agents named in a comma-separated id list ("all" selects every agent).</summary>
        public static uint ListMask(AgentDefinition[] agents, string list)
        {
            if (agents == null || string.IsNullOrWhiteSpace(list)) return 0;
            uint mask = 0;
            foreach (string raw in list.Split(','))
            {
                string id = raw.Trim();
                if (id.Length == 0) continue;
                for (int i = 0; i < agents.Length && i < MaxAgents; i++)
                {
                    if (agents[i] == null) continue;
                    if (id == "all" || string.Equals(agents[i].agentId, id, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(agents[i].callsign, id, StringComparison.OrdinalIgnoreCase))
                        mask |= 1u << i;
                }
            }
            return mask;
        }

        public static AgentRejectReason Check(int index, AgentDefinition[] agents, uint unlocked, uint banned)
        {
            if (agents == null || index < 0 || index >= agents.Length || index >= MaxAgents || agents[index] == null) return AgentRejectReason.Unknown;
            if ((banned & (1u << index)) != 0) return AgentRejectReason.Banned;
            if ((unlocked & (1u << index)) == 0) return AgentRejectReason.Locked;
            return AgentRejectReason.None;
        }

        public static bool IsAvailable(int index, AgentDefinition[] agents, uint unlocked, uint banned) =>
            Check(index, agents, unlocked, banned) == AgentRejectReason.None;

        /// <summary>The requested agent if it is available, else the first available one, else 0.</summary>
        public static byte Resolve(int requested, AgentDefinition[] agents, uint unlocked, uint banned)
        {
            if (IsAvailable(requested, agents, unlocked, banned)) return (byte)requested;
            for (int i = 0; agents != null && i < agents.Length && i < MaxAgents; i++)
                if (IsAvailable(i, agents, unlocked, banned)) return (byte)i;
            return 0;
        }

        public static string Describe(AgentRejectReason reason) => reason switch
        {
            AgentRejectReason.Unknown => "Unknown agent",
            AgentRejectReason.Locked => "Agent is locked",
            AgentRejectReason.Banned => "Agent is banned on this server",
            _ => "",
        };
    }
}
