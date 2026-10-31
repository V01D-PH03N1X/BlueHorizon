using System.Collections.Generic;
using UnityEngine;

namespace BlueHorizon.Audio
{
    public enum Sfx
    {
        RifleShot,
        PistolShot,
        SuppressedShot,
        Reload,
        Equip,
        DryFire,
        HitMarker,
        HeadshotMarker,
        KillConfirm,
        Hurt,
        BombBeep,
        BombPlanted,
        BombDefused,
        Explosion,
        SmokeBurst,
        Throw,
        WallDeploy,
        Heal,
        Denied,
        RoundStart,
        UiClick,
        FlashBang,
        ScanPulse,
        Revive,
        Downed,
    }

    /// <summary>
    /// Placeholder audio synthesized at startup (no audio assets yet) and a small pool of 3D sources.
    /// Swap clips for authored audio by assigning <see cref="Override"/> entries; call sites do not change.
    /// </summary>
    public sealed class GameAudio : MonoBehaviour
    {
        const int SampleRate = 44100;
        const int PoolSize = 24;

        static GameAudio _instance;
        readonly Dictionary<Sfx, AudioClip> _clips = new();
        readonly List<AudioSource> _pool = new();
        int _next;

        public static readonly Dictionary<Sfx, AudioClip> Override = new();

        public static float MasterVolume
        {
            get => PlayerPrefs.GetFloat("bh.volume", 0.8f);
            set
            {
                PlayerPrefs.SetFloat("bh.volume", Mathf.Clamp01(value));
                AudioListener.volume = Mathf.Clamp01(value);
            }
        }

        public static GameAudio Instance
        {
            get
            {
                if (_instance == null && !Player.PlayerRig.IsHeadless)
                {
                    var go = new GameObject("GameAudio");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<GameAudio>();
                }
                return _instance;
            }
        }

        void Awake()
        {
            AudioListener.volume = MasterVolume;
            for (int i = 0; i < PoolSize; i++)
            {
                var child = new GameObject($"Voice{i}");
                child.transform.SetParent(transform, false);
                var src = child.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.maxDistance = 70f;
                src.minDistance = 2f;
                src.dopplerLevel = 0f;
                _pool.Add(src);
            }
            Synthesize();
        }

        public static void Play2D(Sfx sfx, float volume = 1f, float pitch = 1f) => Instance?.PlayInternal(sfx, Vector3.zero, volume, pitch, spatial: false);

        public static void PlayAt(Sfx sfx, Vector3 position, float volume = 1f, float pitch = 1f, float maxDistance = 70f) =>
            Instance?.PlayInternal(sfx, position, volume, pitch, spatial: true, maxDistance);

        void PlayInternal(Sfx sfx, Vector3 position, float volume, float pitch, bool spatial, float maxDistance = 70f)
        {
            AudioClip clip = Override.TryGetValue(sfx, out AudioClip o) && o != null ? o : _clips.GetValueOrDefault(sfx);
            if (clip == null) return;
            AudioSource src = _pool[_next];
            _next = (_next + 1) % _pool.Count;
            src.transform.position = position;
            src.spatialBlend = spatial ? 1f : 0f;
            src.maxDistance = maxDistance;
            src.pitch = pitch * Random.Range(0.97f, 1.03f);
            src.volume = volume;
            src.clip = clip;
            src.Play();
        }

        // ───────────── Synthesis ─────────────

        void Synthesize()
        {
            var rng = new System.Random(1234);
            _clips[Sfx.RifleShot] = Gunshot("rifle", rng, 0.22f, 2200f, 90f, 1f);
            _clips[Sfx.PistolShot] = Gunshot("pistol", rng, 0.16f, 3200f, 130f, 0.85f);
            _clips[Sfx.SuppressedShot] = Gunshot("suppressed", rng, 0.12f, 900f, 60f, 0.35f);
            _clips[Sfx.Reload] = Clicks("reload", rng, new[] { 0f, 0.18f, 0.55f, 0.62f }, 0.75f);
            _clips[Sfx.Equip] = Clicks("equip", rng, new[] { 0f, 0.08f }, 0.25f);
            _clips[Sfx.DryFire] = Clicks("dry", rng, new[] { 0f }, 0.08f);
            _clips[Sfx.HitMarker] = Tone("hit", 0.05f, 1800f, 0.4f, 0f);
            _clips[Sfx.HeadshotMarker] = Tone("headshot", 0.12f, 2600f, 0.45f, 400f);
            _clips[Sfx.KillConfirm] = Tone("kill", 0.22f, 880f, 0.45f, 880f);
            _clips[Sfx.Hurt] = Noise("hurt", rng, 0.12f, 500f, 0.5f);
            _clips[Sfx.BombBeep] = Tone("beep", 0.08f, 2100f, 0.5f, 0f);
            _clips[Sfx.BombPlanted] = Tone("planted", 0.5f, 600f, 0.5f, -200f);
            _clips[Sfx.BombDefused] = Tone("defused", 0.5f, 500f, 0.5f, 700f);
            _clips[Sfx.Explosion] = Gunshot("explosion", rng, 1.6f, 380f, 3.2f, 1f);
            _clips[Sfx.SmokeBurst] = Noise("smoke", rng, 1.2f, 2500f, 0.5f);
            _clips[Sfx.Throw] = Noise("throw", rng, 0.18f, 1200f, 0.3f);
            _clips[Sfx.WallDeploy] = Tone("wall", 0.35f, 180f, 0.6f, 120f);
            _clips[Sfx.Heal] = Tone("heal", 0.45f, 520f, 0.4f, 520f);
            _clips[Sfx.Denied] = Tone("denied", 0.15f, 220f, 0.4f, -60f);
            _clips[Sfx.RoundStart] = Tone("roundstart", 0.6f, 440f, 0.4f, 220f);
            _clips[Sfx.UiClick] = Tone("click", 0.03f, 1400f, 0.25f, 0f);
            _clips[Sfx.FlashBang] = Gunshot("flashbang", rng, 0.9f, 4200f, 5f, 1f);
            _clips[Sfx.ScanPulse] = Tone("scan", 0.7f, 300f, 0.45f, 900f);
            _clips[Sfx.Revive] = Tone("revive", 0.5f, 660f, 0.4f, 330f);
            _clips[Sfx.Downed] = Tone("downed", 0.6f, 330f, 0.45f, -180f);
        }

        static AudioClip Make(string name, float[] data)
        {
            AudioClip clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Noise burst with a low-passed body and exponential decay.</summary>
        static AudioClip Gunshot(string name, System.Random rng, float length, float cutoff, float decay, float gain)
        {
            int n = (int)(SampleRate * length);
            var data = new float[n];
            float lp = 0f, lp2 = 0f;
            float a = Mathf.Clamp01(2f * Mathf.PI * cutoff / SampleRate);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += a * (noise - lp);
                lp2 += a * 0.3f * (lp - lp2);
                float env = Mathf.Exp(-t * decay) * (t < 0.002f ? t / 0.002f : 1f);
                float thump = Mathf.Sin(2f * Mathf.PI * 70f * t) * Mathf.Exp(-t * decay * 0.6f);
                data[i] = Mathf.Clamp((lp * 1.6f + lp2 * 1.2f + thump * 0.6f) * env * gain, -1f, 1f);
            }
            return Make(name, data);
        }

        static AudioClip Noise(string name, System.Random rng, float length, float cutoff, float gain)
        {
            int n = (int)(SampleRate * length);
            var data = new float[n];
            float lp = 0f;
            float a = Mathf.Clamp01(2f * Mathf.PI * cutoff / SampleRate);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                lp += a * ((float)(rng.NextDouble() * 2.0 - 1.0) - lp);
                float env = Mathf.Sin(Mathf.PI * Mathf.Min(1f, t * 1.2f)) * (1f - t);
                data[i] = lp * env * gain;
            }
            return Make(name, data);
        }

        static AudioClip Tone(string name, float length, float freq, float gain, float sweep)
        {
            int n = (int)(SampleRate * length);
            var data = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float f = freq + sweep * t;
                phase += 2f * Mathf.PI * f / SampleRate;
                float env = Mathf.Min(1f, t * 40f) * (1f - t);
                data[i] = Mathf.Sin(phase) * env * gain;
            }
            return Make(name, data);
        }

        static AudioClip Clicks(string name, System.Random rng, float[] times, float length)
        {
            int n = (int)(SampleRate * length);
            var data = new float[n];
            foreach (float time in times)
            {
                int start = (int)(time * SampleRate);
                for (int i = 0; i < 1400 && start + i < n; i++)
                {
                    float env = Mathf.Exp(-i / 180f);
                    data[start + i] += ((float)(rng.NextDouble() * 2.0 - 1.0) * 0.6f + Mathf.Sin(i * 0.9f) * 0.4f) * env * 0.6f;
                }
            }
            return Make(name, data);
        }
    }
}
