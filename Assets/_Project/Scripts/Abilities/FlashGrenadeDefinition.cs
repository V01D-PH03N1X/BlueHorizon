using BlueHorizon.Core;
using BlueHorizon.Player;
using UnityEngine;

namespace BlueHorizon.Abilities
{
    /// <summary>
    /// How strongly a flash blinds a viewer. Pure so it can be unit-tested.
    /// Intensity is 0 without line of sight or beyond the radius; otherwise a facing factor (full inside
    /// <c>fullAngle</c> of the view direction, falling to <c>behindFactor</c> at 150°) times a distance factor
    /// (1 at the flash, <c>edgeFactor</c> at the radius).
    /// </summary>
    public static class FlashRules
    {
        public const float FullAngle = 50f;
        public const float BehindAngle = 150f;
        public const float BehindFactor = 0.2f;
        public const float EdgeFactor = 0.3f;
        /// <summary>Weaker flashes are ignored.</summary>
        public const float MinIntensity = 0.15f;

        public static float Intensity(Vector3 eye, Vector3 forward, Vector3 flash, float radius, bool lineOfSight)
        {
            if (!lineOfSight || radius <= 0f) return 0f;
            Vector3 to = flash - eye;
            float distance = to.magnitude;
            if (distance > radius) return 0f;
            float angle = distance < 0.01f ? 0f : Vector3.Angle(forward, to);
            float facing = angle <= FullAngle ? 1f : Mathf.Lerp(1f, BehindFactor, (angle - FullAngle) / (BehindAngle - FullAngle));
            float near = Mathf.Lerp(1f, EdgeFactor, distance / radius);
            float intensity = facing * near;
            return intensity < MinIntensity ? 0f : intensity;
        }

        /// <summary>Seconds of blindness for an intensity.</summary>
        public static float Duration(float intensity, float maxDuration) => Mathf.Max(0f, intensity) * maxDuration;

        /// <summary>Screen whiteness over time: fully white for the first 40 % of the duration, then a fade.</summary>
        public static float Whiteness(float intensity, float duration, float elapsed)
        {
            if (duration <= 0f || elapsed >= duration || elapsed < 0f) return 0f;
            float hold = duration * 0.4f;
            float fade = elapsed <= hold ? 1f : 1f - (elapsed - hold) / (duration - hold);
            return Mathf.Clamp01(Mathf.Max(intensity, 0.6f) * fade);
        }
    }

    /// <summary>Utility: thrown grenade that blinds everyone (teammates and the thrower too) who looks at it when it bursts.</summary>
    [CreateAssetMenu(menuName = "Blue Horizon/Abilities/Flash Grenade", fileName = "FlashGrenade")]
    public sealed class FlashGrenadeDefinition : ThrownGrenadeDefinition
    {
        public float flashRadius = 22f;
        [Tooltip("Blind time for a viewer looking straight at a flash right next to them.")]
        public float maxBlindDuration = 3.2f;

        public override GrenadeKind Kind => GrenadeKind.Flash;

        public override void Detonate(Vector3 position, PlayerAvatar thrower, Team throwerTeam, double now)
        {
            Vector3 point = position + Vector3.up * 0.15f;
            int blinded = 0;
            foreach (PlayerAvatar a in PlayerAvatar.All)
            {
                if (!a.IsSpawned || !a.IsAlive || a.Health.IsDowned) continue;
                Vector3 eye = a.Simulation.EyePosition;
                bool sight = !Physics.Linecast(eye, point, GameLayers.WorldMask, QueryTriggerInteraction.Ignore)
                             && !SmokeCloud.BlocksLine(eye, point);
                float intensity = FlashRules.Intensity(eye, a.Simulation.AimDirection, point, flashRadius, sight);
                if (intensity <= 0f) continue;
                float duration = FlashRules.Duration(intensity, maxBlindDuration);
                a.Abilities.ServerFlash(intensity, duration);
                blinded++;
                string relation = a == thrower ? "self" : a.Team == throwerTeam && throwerTeam != Team.None ? "ally" : "enemy";
                BHLog.Info("Ability", $"Flash blinded {a.PlayerName} ({relation}) for {duration:0.0}s (intensity {intensity:0.00})");
            }
            thrower?.Abilities.ServerBurst(AbilityBurst.Flash, position);
            BHLog.Info("Ability", $"Flash detonated at {position} ({throwerTeam} team), {blinded} player(s) blinded");
        }
    }
}
