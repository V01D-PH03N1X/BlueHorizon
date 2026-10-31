using UnityEngine;

namespace BlueHorizon.Player
{
    /// <summary>
    /// Client estimate of "the server time of the newest snapshots", derived from the snapshots themselves.
    /// Remote players are rendered <see cref="PlayerSimulation.InterpolationDelay"/> behind this clock, and the same
    /// time is reported for lag compensation. Measured: Netcode's client ServerTime trails the snapshot stream by
    /// ~90 ms on localhost, which would add that much needless display delay on top of the interpolation buffer.
    /// </summary>
    public static class SnapshotClock
    {
        const double Smoothing = 0.05;
        const double ResyncThreshold = 0.5;

        static double _offset;
        static bool _valid;

        public static bool IsValid => _valid;

        /// <summary>Feed every received snapshot's server timestamp.</summary>
        public static void Observe(double snapshotServerTime)
        {
            double sample = snapshotServerTime - Time.unscaledTimeAsDouble;
            if (!_valid || System.Math.Abs(sample - _offset) > ResyncThreshold)
            {
                _offset = sample;
                _valid = true;
                return;
            }
            _offset += (sample - _offset) * Smoothing;
        }

        public static double ServerTimeEstimate => Time.unscaledTimeAsDouble + _offset;

        public static void Reset() => _valid = false;
    }
}
