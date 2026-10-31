using BlueHorizon.Agents;
using BlueHorizon.Core;
using BlueHorizon.Match;
using UnityEngine;

namespace BlueHorizon.Player
{
    /// <summary>
    /// The local player's agent choice: kept in PlayerPrefs by agent id (robust to list changes) and sent to the
    /// server when the player spawns. Test bots pick with <c>-botAgent random|&lt;id&gt;</c>. The server validates every request.
    /// </summary>
    public static class LocalAgent
    {
        const string Key = "bh.agent";

        public static int SavedIndex(GameContent content) => content != null ? content.FindAgentIndex(PlayerPrefs.GetString(Key, "")) : -1;

        public static void Save(AgentDefinition agent)
        {
            if (agent == null) return;
            PlayerPrefs.SetString(Key, agent.agentId);
            PlayerPrefs.Save();
        }

        /// <summary>Owner: called once the local avatar has spawned.</summary>
        public static void SendInitial(PlayerAvatar avatar)
        {
            GameContent content = GameContent.Instance;
            if (content == null || content.agents == null) return;
            int index;
            if (LaunchConfig.Current.bot)
            {
                // Bots never read the saved choice (shared PlayerPrefs, repeatable tests).
                string choice = LaunchConfig.Current.botAgent;
                if (string.IsNullOrEmpty(choice)) return;
                index = choice == "random"
                    ? RandomAvailable(content, new System.Random(unchecked((int)avatar.OwnerClientId * 104729 + System.Environment.TickCount)))
                    : content.FindAgentIndex(choice);
                if (index < 0)
                {
                    BHLog.Warn("Agent", $"Bot agent '{choice}' not found");
                    return;
                }
                BHLog.Info("Agent", $"Bot requests agent {content.agents[index].DisplayName}");
            }
            else
            {
                index = SavedIndex(content);
                if (index <= 0) return;
            }
            avatar.RequestAgentRpc((byte)index);
        }

        /// <summary>A random agent that the server currently allows (falls back to 0).</summary>
        public static int RandomAvailable(GameContent content, System.Random rng)
        {
            MatchManager match = MatchManager.Instance;
            uint unlocked = match != null ? match.AgentUnlockMask.Value : ~0u;
            uint banned = match != null ? match.AgentBanMask.Value : 0u;
            var options = new System.Collections.Generic.List<int>();
            for (int i = 0; i < content.agents.Length; i++)
                if (AgentRules.IsAvailable(i, content.agents, unlocked, banned)) options.Add(i);
            return options.Count == 0 ? 0 : options[rng.Next(options.Count)];
        }
    }
}
