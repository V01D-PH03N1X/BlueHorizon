using UnityEngine;

namespace BlueHorizon.Core
{
    /// <summary>
    /// Thin logging wrapper so server events share one greppable format:
    /// <c>[BH][Category] message @HH:mm:ss.fff</c>. Dedicated servers write these to the -logFile.
    /// </summary>
    public static class BHLog
    {
        static string Stamp => System.DateTime.Now.ToString("HH:mm:ss.fff");

        public static void Info(string category, string message) =>
            Debug.Log($"[BH][{category}] {message} @{Stamp}");

        public static void Warn(string category, string message) =>
            Debug.LogWarning($"[BH][{category}] {message} @{Stamp}");

        public static void Error(string category, string message) =>
            Debug.LogError($"[BH][{category}] {message} @{Stamp}");
    }
}
