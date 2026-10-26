using UnityEngine;

namespace BlueHorizon.Attachments
{
    public enum AttachmentSlot : byte
    {
        Muzzle = 0,
        Underbarrel = 1,
        Optic = 2,
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
        public float recoilMultiplier = 1f;

        [Header("Signature")]
        [Tooltip("Other players hear a quieter, muffled shot and see no muzzle flash at range.")]
        public bool suppressesFireSound;
        [Tooltip("The shooter is not revealed on the minimap when firing (when a minimap exists).")]
        public bool hidesMinimapFireIndicator;
        [Range(0f, 1f)] public float remoteFireVolumeMultiplier = 1f;

        [Header("Optics (future: red-dot, holographic, magnified)")]
        [Tooltip("Field-of-view multiplier while aiming. 1 = no zoom.")]
        public float aimFovMultiplier = 1f;
        public Color reticleColor = Color.white;

        [Header("Presentation")]
        public Vector3 viewmodelOffset = new(0f, 0f, 0.28f);
        public Vector3 viewmodelSize = new(0.05f, 0.05f, 0.16f);
        public Color viewmodelColor = new(0.1f, 0.1f, 0.1f);
    }
}
