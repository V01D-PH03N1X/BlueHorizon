using System;
using UnityEngine;

namespace BlueHorizon.Combat
{
    /// <summary>
    /// Ring buffer of a character's authoritative poses (server time, feet position, crouch), recorded every server
    /// tick. Sampling interpolates between ticks, so the server can reconstruct where a target was at any moment of
    /// the last ~1 s.
    /// </summary>
    public sealed class PoseHistory
    {
        struct Sample
        {
            public double time;
            public Vector3 position;
            public float crouch;
        }

        readonly Sample[] _ring;
        int _head;   // index of the newest sample
        int _count;

        public PoseHistory(int capacity = 72) => _ring = new Sample[Mathf.Max(2, capacity)];

        public int Count => _count;

        public void Clear() => _count = 0;

        public void Record(double time, Vector3 position, float crouch)
        {
            if (_count > 0 && time <= _ring[_head].time)
            {
                // Same tick recorded twice (or clock hiccup): keep the latest pose.
                _ring[_head] = new Sample { time = _ring[_head].time, position = position, crouch = crouch };
                return;
            }
            _head = (_head + 1) % _ring.Length;
            _ring[_head] = new Sample { time = time, position = position, crouch = crouch };
            if (_count < _ring.Length) _count++;
        }

        /// <summary>Pose at <paramref name="time"/>, clamped to the recorded range. False when empty.</summary>
        public bool TrySample(double time, out Vector3 position, out float crouch)
        {
            position = default;
            crouch = 0f;
            if (_count == 0) return false;

            Sample newest = _ring[_head];
            if (time >= newest.time)
            {
                position = newest.position;
                crouch = newest.crouch;
                return true;
            }

            // Walk backwards to find the bracketing pair.
            Sample later = newest;
            for (int n = 1; n < _count; n++)
            {
                Sample earlier = _ring[(_head - n + _ring.Length) % _ring.Length];
                if (time >= earlier.time)
                {
                    float t = (float)((time - earlier.time) / Math.Max(1e-6, later.time - earlier.time));
                    position = Vector3.Lerp(earlier.position, later.position, t);
                    crouch = Mathf.Lerp(earlier.crouch, later.crouch, t);
                    return true;
                }
                later = earlier;
            }

            position = later.position; // Older than history: oldest sample.
            crouch = later.crouch;
            return true;
        }
    }

    /// <summary>
    /// Server-side rules for how far a shot may be rewound. The client reports the server time it was rendering other
    /// players at (its interpolation time); the server never trusts it blindly:
    ///   * never later than now (no shooting into the future),
    ///   * never older than <c>maxRewind</c> (bounds the advantage of high or faked latency),
    ///   * never more than <see cref="MaxBacktrack"/> older than the previous accepted view time of the same player
    ///     (a client cannot cherry-pick older snapshots shot by shot).
    /// </summary>
    public static class LagCompensation
    {
        public const double MaxBacktrack = 0.05;

        public static double ClampViewTime(double reported, double now, double maxRewind, double previousAccepted)
        {
            if (double.IsNaN(reported) || double.IsInfinity(reported) || reported <= 0) return now;
            double earliest = now - Math.Max(0, maxRewind);
            if (previousAccepted > 0) earliest = Math.Max(earliest, Math.Min(now, previousAccepted - MaxBacktrack));
            return Math.Clamp(reported, earliest, now);
        }
    }
}
