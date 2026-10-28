using UnityEngine;

namespace BlueHorizon.Attachments
{
    public enum AttachmentSlot : byte
    {
        Muzzle = 0,
        Underbarrel = 1,
        Optic = 2,
    }

    public enum ReticleStyle : byte
    {
        None,
        Dot,
        Holographic,
        Scope,
    }

    /// <summary>
    /// A weapon modification. Effects are expressed as data so new attachments (laser, red-dot,
    /// holographic, magnified optics, custom reticles) plug in without changing the firing code:
    /// combat-relevant modifiers are folded into <see cref="BlueHorizon.Weapons.WeaponStats"/>,
    /// presentation-only fields are read by the viewmodel/HUD.
    /// </summary>
    [CreateAssetMenu(menuName = "Blue Horizon/Attachment Definition", fileName = "Attachment")]
    public sealed class AttachmentDefinition : ScriptableObject
    {
        public string attachmentId = "attachment";
        public string displayName = "Attachment";
        public AttachmentSlot slot = AttachmentSlot.Muzzle;

        [Header("Combat modifiers (multiplicative)")]
        [Tooltip("Applied to the weapon's zone damage before range falloff. 0.8 = 20% reduction.")]
        public float damageMultiplier = 1f;
        public float spreadMultiplier = 1f;
        [Tooltip("Spread multiplier applied only when firing from the hip (laser).")]
        public float hipSpreadMultiplier = 1f;
        [Tooltip("Spread multiplier applied only when fully aimed.")]
        public float adsSpreadMultiplier = 1f;
        public float recoilMultiplier = 1f;
        [Tooltip("Multiplier on the weapon's aim-down-sights time.")]
        public float adsTimeMultiplier = 1f;

        [Header("Signature")]
        [Tooltip("Other players hear a quieter, muffled shot and see no muzzle flash at range.")]
        public bool suppressesFireSound;
        [Tooltip("The shooter is not revealed on the minimap when firing (when a minimap exists).")]
        public bool hidesMinimapFireIndicator;
        [Range(0f, 1f)] public float remoteFireVolumeMultiplier = 1f;

        [Header("Optics (future: red-dot, holographic, magnified)")]
        [Tooltip("Field-of-view multiplier while aiming. 1 = no zoom.")]
        public float aimFovMultiplier = 1f;
        public ReticleStyle reticle = ReticleStyle.None;
        [Tooltip("Magnified scope: full-screen scope overlay, viewmodel hidden when fully aimed.")]
        public bool magnified;
        [Tooltip("Default reticle colour (players can override it in settings).")]
        public Color reticleColor = Color.white;

        [Header("Laser")]
        [Tooltip("Projects a visible laser beam (other players can see it).")]
        public bool visibleLaser;
        public Color laserColor = new(1f, 0.15f, 0.1f);

        [Header("Presentation")]
        public Vector3 viewmodelOffset = new(0f, 0f, 0.28f);
        public Vector3 viewmodelSize = new(0.05f, 0.05f, 0.16f);
        public Color viewmodelColor = new(0.1f, 0.1f, 0.1f);
    }
}
