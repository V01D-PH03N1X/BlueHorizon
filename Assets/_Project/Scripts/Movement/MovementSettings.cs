using System;
using UnityEngine;

namespace BlueHorizon.Movement
{
    /// <summary>Tunable first-person movement parameters. Owned by an agent definition.</summary>
    [Serializable]
    public sealed class MovementSettings
    {
        [Header("Speeds (m/s)")]
        public float runSpeed = 5.2f;
        public float sprintSpeed = 6.8f;
        public float crouchSpeed = 2.4f;

        [Header("Acceleration (m/s²)")]
        public float groundAcceleration = 55f;
        public float groundDeceleration = 45f;
        public float airAcceleration = 8f;

        [Header("Vertical")]
        public float jumpHeight = 1.05f;
        public float gravity = 22f;
        public float maxFallSpeed = 30f;

        [Header("Body")]
        public float standingHeight = 1.8f;
        public float crouchHeight = 1.2f;
        public float radius = 0.35f;
        public float standingEyeHeight = 1.62f;
        public float crouchEyeHeight = 1.05f;
        /// <summary>Crouch transition speed in "full transitions per second".</summary>
        public float crouchTransitionRate = 7f;

        [Header("Look")]
        public float minPitch = -85f;
        public float maxPitch = 85f;
    }
}
