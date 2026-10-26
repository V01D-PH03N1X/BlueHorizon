using BlueHorizon.Abilities;
using BlueHorizon.Health;
using BlueHorizon.Movement;
using UnityEngine;

namespace BlueHorizon.Agents
{
    /// <summary>
    /// Everything that distinguishes one agent from another. The player controller, combat and ability systems
    /// read from this, so new agents are new assets rather than new controllers.
    /// </summary>
    [CreateAssetMenu(menuName = "Blue Horizon/Agent Definition", fileName = "Agent")]
    public sealed class AgentDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string agentId = "agent";
        public string codename = "";
        public string callsign = "";
        public string realName = "";
        public string origin = "";
        [TextArea(3, 8)] public string biography = "";
        [Tooltip("Locked agents can be unlocked by progression later; the vertical slice ships one unlocked agent.")]
        public bool unlockedByDefault = true;

        [Header("Survivability")]
        public HealthProfile health = new();

        [Header("Movement")]
        public MovementSettings movement = new();

        [Header("Abilities")]
        public AbilityDefinition utility1;
        public AbilityDefinition utility2;
        public AbilityDefinition signature;

        [Header("Presentation")]
        public Color accentColor = new(0.95f, 0.65f, 0.2f);
        [Tooltip("Optional voice line / audio set. Not used yet.")]
        public AudioClip selectVoiceLine;

        public string DisplayName => string.IsNullOrEmpty(callsign) ? agentId : callsign;

        public AbilityDefinition GetAbility(AbilitySlot slot) => slot switch
        {
            AbilitySlot.Utility1 => utility1,
            AbilitySlot.Utility2 => utility2,
            _ => signature,
        };
    }
}
