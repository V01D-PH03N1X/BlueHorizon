using UnityEngine;

namespace BlueHorizon.Combat
{
    /// <summary>
    /// A hitbox collider. Raycasts resolve the <see cref="IDamageable"/> and zone from this component,
    /// so each shot damages exactly one zone of one target.
    /// </summary>
    public sealed class HitboxCollider : MonoBehaviour
    {
        public HitZone zone = HitZone.Torso;
        public MonoBehaviour owner;

        public IDamageable Target => owner as IDamageable;
    }
}
