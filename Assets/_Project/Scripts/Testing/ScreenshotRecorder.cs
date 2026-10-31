using System.IO;
using BlueHorizon.Core;
using UnityEngine;

namespace BlueHorizon.Testing
{
    /// <summary>Captures numbered screenshots at a fixed interval (-screenshotInterval N -screenshotDir path) for visual review.</summary>
    public sealed class ScreenshotRecorder : MonoBehaviour
    {
        float _interval;
        string _dir;
        float _next;
        int _index;

        public static void Install(float interval, string dir)
        {
            if (interval <= 0f || string.IsNullOrEmpty(dir) || Player.PlayerRig.IsHeadless) return;
            Directory.CreateDirectory(dir);
            var go = new GameObject("ScreenshotRecorder");
            DontDestroyOnLoad(go);
            var r = go.AddComponent<ScreenshotRecorder>();
            r._interval = interval;
            r._dir = dir;
            r._next = Time.unscaledTime + interval;
        }

        void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next += _interval;
            string path = Path.Combine(_dir, $"shot_{_index++:000}.png");
            ScreenCapture.CaptureScreenshot(path);
            BHLog.Info("Screenshot", path);
        }
    }
}
