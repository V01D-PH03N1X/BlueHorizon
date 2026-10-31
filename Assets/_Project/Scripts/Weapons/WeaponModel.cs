using UnityEngine;

namespace BlueHorizon.Weapons
{
    /// <summary>
    /// Root of a weapon model prefab. The origin sits at the grip, +Z runs down the barrel and +Y is up;
    /// <see cref="mount"/> rides on the Root bone and lines up with the prefab root at rest, so everything parented
    /// to it (the anchors, fitted attachments) follows the clips while local positions stay in model space.
    /// Clips are stretched to the durations in the weapon data, which stay the gameplay truth.
    /// </summary>
    public sealed class WeaponModel : MonoBehaviour
    {
        public const string IdleClip = "Idle";
        public const string FireClip = "Fire";
        public const string ReloadClip = "Reload";
        public const string EquipClip = "Equip";

        public Transform mount;
        public Transform muzzle;
        public Transform sight;
        public Transform underbarrel;
        [Tooltip("The mesh already contains the optic, so no optic is drawn on top of it.")]
        public bool builtInOptic;
        public Animation clips;

        bool _reloading;

        void Update()
        {
            if (clips != null && !clips.isPlaying) clips.CrossFade(IdleClip, 0.1f);
        }

        public void PlayEquip(WeaponDefinition weapon) => Play(EquipClip, weapon.equipTime);

        /// <summary>The clip never outlasts the fire interval, so automatic fire restarts it cleanly.</summary>
        public void PlayFire(WeaponDefinition weapon)
        {
            AnimationState state = clips != null ? clips[FireClip] : null;
            if (state != null) Play(FireClip, Mathf.Min(state.length, weapon.FireInterval));
        }

        public void SetReloading(bool reloading, WeaponDefinition weapon)
        {
            if (reloading && !_reloading) Play(ReloadClip, weapon.reloadTime);
            _reloading = reloading;
        }

        void Play(string clip, float duration)
        {
            AnimationState state = clips != null ? clips[clip] : null;
            if (state == null) return;
            state.speed = state.length / Mathf.Max(0.01f, duration);
            state.time = 0f;
            clips.Play(clip, PlayMode.StopAll);
        }
    }
}
