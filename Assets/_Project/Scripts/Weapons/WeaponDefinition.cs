using System;
using BlueHorizon.Attachments;
using UnityEngine;

namespace BlueHorizon.Weapons
{
    public enum FireMode : byte
    {
        SemiAutomatic,
        Automatic,
    }

    public enum WeaponSlot : byte
    {
        Primary = 0,
        Secondary = 1,
    }

    public enum WeaponCategory : byte
    {
        AssaultRifle,
        Pistol,
        Revolver,
        Shotgun,
        PrecisionRifle,
        SniperRifle,
    }

    [Serializable]
    public struct ZoneDamage
    {
        public int head;
        public int torso;
        public int legs;
    }

    /// <summary>
    /// Range falloff: full damage until <see cref="falloffStart"/>, then linear down to
    /// <see cref="minMultiplier"/> at <see cref="falloffEnd"/>. Set minMultiplier = 1 to disable.
    /// </summary>
    [Serializable]
    public struct RangeFalloff
    {
        public float falloffStart;
        public float falloffEnd;
        [Range(0f, 1f)] public float minMultiplier;
    }

    [Serializable]
    public struct SpreadSettings
    {
        [Tooltip("Cone half-angle in degrees while standing still on the ground.")]
        public float baseSpread;
        public float movingSpread;
        public float airborneSpread;
        [Tooltip("Added per consecutive shot.")]
        public float bloomPerShot;
        public float maxBloom;
        [Tooltip("Multiplier applied to the total while crouched.")]
        public float crouchMultiplier;
    }

    [Serializable]
    public struct RecoilSettings
    {
        [Tooltip("Vertical aim kick per consecutive shot, degrees.")]
        public float verticalPerShot;
        public float maxVertical;
        [Tooltip("Peak horizontal sway, degrees. Follows a deterministic pattern.")]
        public float horizontalAmplitude;
        [Tooltip("Seconds without firing before the spray counter resets.")]
        public float resetTime;
        [Tooltip("Degrees per second of recoil recovery when not firing.")]
        public float recoveryRate;
    }

    /// <summary>
    /// Data-only weapon description. Firing logic lives in WeaponSimulation, damage in DamageResolver,
    /// presentation in WeaponPresenter. Values are the design specification, not real-world figures.
    /// </summary>
    [CreateAssetMenu(menuName = "Blue Horizon/Weapon Definition", fileName = "Weapon")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        public string weaponId = "weapon";
        public string displayName = "Weapon";
        public WeaponSlot slot = WeaponSlot.Primary;
        public WeaponCategory category = WeaponCategory.AssaultRifle;
        public FireMode fireMode = FireMode.SemiAutomatic;

        [Header("Cadence & Ammo")]
        [Tooltip("Shots per second.")]
        public float fireRate = 2f;
        public int magazineSize = 12;
        public int reserveAmmo = 36;
        public float reloadTime = 1.8f;
        public float equipTime = 0.5f;

        [Header("Damage")]
        public ZoneDamage damage = new() { head = 100, torso = 40, legs = 20 };
        public RangeFalloff falloff = new() { falloffStart = 1000f, falloffEnd = 1000f, minMultiplier = 1f };
        public float maxRange = 150f;
        [Tooltip("Projectiles per shot (shotgun). Damage values are per pellet.")]
        public int pellets = 1;
        [Tooltip("Cone half-angle, degrees, in which pellets scatter around the shot direction.")]
        public float pelletSpread;

        [Header("Accuracy")]
        public SpreadSettings spread = new()
        {
            baseSpread = 0.2f, movingSpread = 2f, airborneSpread = 5f,
            bloomPerShot = 0.4f, maxBloom = 3f, crouchMultiplier = 0.75f,
        };
        public RecoilSettings recoil = new()
        {
            verticalPerShot = 0.6f, maxVertical = 6f, horizontalAmplitude = 0.6f,
            resetTime = 0.35f, recoveryRate = 18f,
        };
        [Tooltip("Firing is blocked while sprinting.")]
        public bool blockFireWhileSprinting = true;

        [Header("Aim down sights")]
        public bool canAim = true;
        [Tooltip("Seconds from hip to fully aimed.")]
        public float adsTime = 0.2f;
        [Tooltip("Spread multiplier when fully aimed (hip = 1).")]
        public float adsSpreadMultiplier = 0.5f;
        [Tooltip("Movement speed multiplier while aiming.")]
        public float adsMoveSpeedMultiplier = 0.75f;
        [Tooltip("Field-of-view multiplier with iron sights (optics override it).")]
        public float adsFovMultiplier = 0.9f;

        [Header("Attachments")]
        [Tooltip("Attachments a player may fit to this weapon.")]
        public AttachmentDefinition[] allowedAttachments = System.Array.Empty<AttachmentDefinition>();
        [Tooltip("Built-in optic that is always fitted (e.g. sniper scope).")]
        public AttachmentDefinition fixedOptic;

        [Header("Presentation")]
        public Color viewmodelColor = new(0.2f, 0.22f, 0.25f);
        public Vector3 viewmodelSize = new(0.06f, 0.1f, 0.35f);
        public float fireSoundPitch = 1f;
        public float fireSoundVolume = 1f;

        public float FireInterval => fireRate > 0f ? 1f / fireRate : float.MaxValue;
    }
}
