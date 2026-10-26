using System.Collections.Generic;
using BlueHorizon.Core;
using BlueHorizon.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Profiling;

namespace BlueHorizon.Testing
{
    /// <summary>
    /// Periodic one-line performance report: frame time (avg / p99 / max), GC collections, managed heap,
    /// connected players and, on clients, prediction corrections. Used to back performance claims with data.
    /// Enabled by default on dedicated servers (30 s), or with -perfLogInterval N anywhere.
    /// </summary>
    public sealed class PerformanceMonitor : MonoBehaviour
    {
        float _interval;
        float _windowStart;
        readonly List<float> _frames = new(4096);
        int _gcAtWindowStart;
        int _correctionsAtWindowStart;
        readonly Dictionary<ulong, int> _lastReceived = new();
        int _shotsAtWindowStart, _hitsAtWindowStart;
        double _rewindAtWindowStart;

        public static void Install(float interval)
        {
            if (interval <= 0f) return;
            var go = new GameObject("PerformanceMonitor");
            DontDestroyOnLoad(go);
            go.AddComponent<PerformanceMonitor>()._interval = interval;
        }

        void Start()
        {
            _windowStart = Time.unscaledTime;
            _gcAtWindowStart = System.GC.CollectionCount(0);
        }

        void Update()
        {
            _frames.Add(Time.unscaledDeltaTime * 1000f);
            if (Time.unscaledTime - _windowStart < _interval) return;

            _frames.Sort();
            float sum = 0f;
            foreach (float f in _frames) sum += f;
            float avg = sum / _frames.Count;
            float p99 = _frames[Mathf.Clamp((int)(_frames.Count * 0.99f), 0, _frames.Count - 1)];
            float max = _frames[^1];
            int gc = System.GC.CollectionCount(0) - _gcAtWindowStart;
            long heapMb = Profiler.GetMonoUsedSizeLong() / (1024 * 1024);

            NetworkManager nm = NetworkManager.Singleton;
            string net = "";
            if (nm != null && nm.IsServer)
            {
                var sb = new System.Text.StringBuilder($" players {nm.ConnectedClientsIds.Count} inputs[");
                foreach (PlayerAvatar a in PlayerAvatar.All)
                {
                    int total = a.Simulation.MessagesReceived;
                    _lastReceived.TryGetValue(a.OwnerClientId, out int previous);
                    _lastReceived[a.OwnerClientId] = total;
                    sb.Append($" {a.OwnerClientId}:{total - previous}");
                }
                sb.Append(" ] rtt[");
                foreach (PlayerAvatar a in PlayerAvatar.All)
                {
                    if (a.OwnerClientId == NetworkManager.ServerClientId) continue;
                    sb.Append($" {a.OwnerClientId}:{nm.NetworkConfig.NetworkTransport.GetCurrentRtt(a.OwnerClientId)}");
                }
                int shots = Combat.CombatSystem.ShotsResolved - _shotsAtWindowStart;
                double rewind = Combat.CombatSystem.RewindSecondsTotal - _rewindAtWindowStart;
                sb.Append($" ] shots {shots} hits {Combat.CombatSystem.CharacterHits - _hitsAtWindowStart}");
                if (shots > 0) sb.Append($" rewind avg {rewind / shots * 1000.0:0}ms");
                _shotsAtWindowStart = Combat.CombatSystem.ShotsResolved;
                _hitsAtWindowStart = Combat.CombatSystem.CharacterHits;
                _rewindAtWindowStart = Combat.CombatSystem.RewindSecondsTotal;
                net = sb.ToString();
            }
            if (nm != null && nm.IsClient && !nm.IsServer && PlayerAvatar.Local != null)
            {
                int corrections = PlayerAvatar.Local.Simulation.CorrectionCount;
                int states = PlayerAvatar.Local.Simulation.MessagesReceived;
                _lastReceived.TryGetValue(0, out int previousStates);
                _lastReceived[0] = states;
                net = $" rtt {nm.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId)}ms corrections {corrections - _correctionsAtWindowStart} states {states - previousStates}";
                _correctionsAtWindowStart = corrections;
            }

            BHLog.Info("Perf", $"frames {_frames.Count} avg {avg:0.00}ms ({1000f / avg:0} fps) p99 {p99:0.00}ms max {max:0.00}ms gc0 {gc} heap {heapMb}MB{net}");

            _frames.Clear();
            _windowStart = Time.unscaledTime;
            _gcAtWindowStart = System.GC.CollectionCount(0);
        }
    }
}
