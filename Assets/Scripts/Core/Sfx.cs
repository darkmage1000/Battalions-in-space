using System;
using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    /// <summary>Every sound is synthesised at start-up (no audio files). 3D one-shots come from a pool.</summary>
    public static class Sfx
    {
        const int Rate = 44100;
        static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        static AudioSource[] pool;
        static AudioSource ui;
        static int next;
        static System.Random rng = new System.Random(7);
        static Transform root;
        public static AudioSource Music;

        static float Rnd() => (float)rng.NextDouble() * 2 - 1;

        static AudioClip Make(string name, float len, Func<float, float> f)
        {
            int n = Mathf.CeilToInt(len * Rate);
            var d = new float[n];
            for (int i = 0; i < n; i++) d[i] = Mathf.Clamp(f(i / (float)Rate), -1, 1);
            var c = AudioClip.Create(name, n, 1, Rate, false);
            c.SetData(d, 0);
            clips[name] = c;
            return c;
        }

        static float Env(float t, float a, float len) => t < a ? t / a : Mathf.Exp(-(t - a) / len);

        // simple one-pole filtered noise
        static Func<float, float> Noise(float cutoff)
        {
            float y = 0, k = Mathf.Clamp01(cutoff);
            return t => { y += (Rnd() - y) * k; return y; };
        }

        public static void Init(Transform parent)
        {
            root = parent;
            // blaster: falling pitch saw + square
            float Blaster(float t, float f0, float f1, float len, float grit)
            {
                float f = Mathf.Lerp(f0, f1, Mathf.Clamp01(t / len));
                float ph = t * f;
                float s = (ph % 1f) * 2 - 1;
                float q = Mathf.Sin(ph * Mathf.PI * 2) > 0 ? 0.6f : -0.6f;
                return (s * 0.5f + q * 0.5f + Rnd() * grit) * Env(t, 0.003f, len * 0.35f);
            }
            Make("blaster", 0.22f, t => Blaster(t, 1500, 300, 0.2f, 0.15f) * 0.55f);
            Make("blaster2", 0.24f, t => Blaster(t, 1100, 220, 0.22f, 0.1f) * 0.55f);
            Make("blaster3", 0.2f, t => Blaster(t, 1900, 500, 0.18f, 0.2f) * 0.5f);
            Make("pistol", 0.18f, t => Blaster(t, 2200, 700, 0.15f, 0.1f) * 0.45f);
            Make("repeater", 0.12f, t => Blaster(t, 1700, 600, 0.1f, 0.25f) * 0.5f);
            Make("heavy", 0.35f, t => Blaster(t, 700, 120, 0.3f, 0.3f) * 0.7f);
            Make("sniper", 0.5f, t => (Blaster(t, 2600, 180, 0.45f, 0.05f) * 0.6f + Mathf.Sin(t * 90 * 6.28f) * Env(t, 0.002f, 0.08f) * 0.6f));
            var shotN = Noise(0.5f);
            Make("shotgun", 0.4f, t => (shotN(t) * 1.6f + Blaster(t, 900, 200, 0.2f, 0) * 0.4f) * Env(t, 0.002f, 0.09f));
            Make("laser", 0.18f, t => Mathf.Sin(t * Mathf.Lerp(2400, 900, t / 0.18f) * 6.28f) * Env(t, 0.002f, 0.05f) * 0.5f + Rnd() * 0.05f * Env(t, 0, 0.03f));
            Make("cannon", 0.6f, t => (Blaster(t, 400, 60, 0.5f, 0.5f) * 0.8f) * Env(t, 0.004f, 0.18f) * 1.4f);
            var rk = Noise(0.15f);
            Make("rocket", 0.7f, t => rk(t) * 2.2f * Env(t, 0.03f, 0.25f) + Mathf.Sin(t * 180 * 6.28f) * 0.2f * Env(t, 0.01f, 0.2f));
            var ex1 = Noise(0.12f); var ex2 = Noise(0.04f);
            Make("explode", 1.3f, t => (ex1(t) * 1.8f + ex2(t) * 3f) * Env(t, 0.004f, 0.32f));
            var bx1 = Noise(0.08f); var bx2 = Noise(0.02f);
            Make("explode_big", 3f, t => (bx1(t) * 1.5f + bx2(t) * 4.5f) * Env(t, 0.01f, 0.9f) + Mathf.Sin(t * 38 * 6.28f) * 0.5f * Env(t, 0.01f, 0.5f));
            Make("hit", 0.12f, t => Rnd() * Env(t, 0.001f, 0.025f) * 0.7f + Mathf.Sin(t * 300 * 6.28f) * Env(t, 0.001f, 0.03f) * 0.4f);
            Make("hitmark", 0.06f, t => Mathf.Sin(t * 2600 * 6.28f) * Env(t, 0.001f, 0.015f) * 0.35f);
            Make("ui", 0.08f, t => Mathf.Sin(t * 1200 * 6.28f) * Env(t, 0.002f, 0.025f) * 0.3f);
            Make("ui2", 0.12f, t => Mathf.Sin(t * (t < 0.05f ? 900 : 1350) * 6.28f) * Env(t, 0.002f, 0.04f) * 0.3f);
            Make("deny", 0.25f, t => ((t * 160 % 1f) * 2 - 1) * Env(t, 0.005f, 0.08f) * 0.35f);
            Make("capture", 1.1f, t =>
            {
                float f = t < 0.18f ? 523 : t < 0.36f ? 659 : 784;
                return (Mathf.Sin(t * f * 6.28f) + Mathf.Sin(t * f * 2 * 6.28f) * 0.3f) * Env(t, 0.01f, 0.35f) * 0.35f;
            });
            Make("lost", 1.1f, t =>
            {
                float f = t < 0.18f ? 523 : t < 0.36f ? 440 : 349;
                return (Mathf.Sin(t * f * 6.28f) + ((t * f % 1f) * 2 - 1) * 0.2f) * Env(t, 0.01f, 0.35f) * 0.35f;
            });
            Make("award", 1.4f, t =>
            {
                float f = t < 0.12f ? 659 : t < 0.24f ? 784 : t < 0.36f ? 988 : 1319;
                return (Mathf.Sin(t * f * 6.28f) * 0.6f + Mathf.Sin(t * f * 1.5f * 6.28f) * 0.2f) * Env(t, 0.01f, 0.5f) * 0.4f;
            });
            Make("blade", 0.35f, t => (Mathf.Sin(t * Mathf.Lerp(160, 260, t / 0.35f) * 6.28f) * 0.6f + Rnd() * 0.15f) * Env(t, 0.02f, 0.12f) * 0.6f);
            Make("bladehum", 1f, t => (Mathf.Sin(t * 110 * 6.28f) * 0.5f + Mathf.Sin(t * 113 * 6.28f) * 0.4f) * 0.25f);
            Make("zap", 0.3f, t => (Rnd() * 0.6f + ((t * 220 % 1f) > 0.5f ? 0.4f : -0.4f)) * Env(t, 0.002f, 0.08f) * 0.6f);
            var jn = Noise(0.3f);
            Make("jet", 0.6f, t => jn(t) * 0.8f);
            var en = Noise(0.05f);
            Make("engine", 1f, t => en(t) * 1.2f + Mathf.Sin(t * 55 * 6.28f) * 0.25f + Mathf.Sin(t * 110 * 6.28f) * 0.1f);
            var fn = Noise(0.25f);
            Make("fighter", 1f, t => fn(t) * 0.6f + Mathf.Sin(t * 180 * 6.28f) * 0.12f + Mathf.Sin(t * 360 * 6.28f) * 0.06f);
            Make("lock", 0.1f, t => Mathf.Sin(t * 1800 * 6.28f) * Env(t, 0.002f, 0.03f) * 0.3f);
            Make("alarm", 0.6f, t => Mathf.Sin(t * (t % 0.3f < 0.15f ? 700 : 500) * 6.28f) * 0.25f * Env(t, 0.01f, 0.5f));
            var gn = Noise(0.4f);
            Make("thud", 0.25f, t => (gn(t) * 0.8f + Mathf.Sin(t * 70 * 6.28f)) * Env(t, 0.002f, 0.06f) * 0.6f);
            Make("repair", 0.15f, t => Mathf.Sin(t * (800 + 400 * Mathf.Sin(t * 60)) * 6.28f) * Env(t, 0.005f, 0.05f) * 0.25f);
            Make("beep", 0.1f, t => Mathf.Sin(t * 1000 * 6.28f) * Env(t, 0.002f, 0.04f) * 0.3f);

            pool = new AudioSource[40];
            for (int i = 0; i < pool.Length; i++)
            {
                var go = new GameObject("sfx" + i);
                go.transform.SetParent(root, false);
                var a = go.AddComponent<AudioSource>();
                a.playOnAwake = false; a.spatialBlend = 1; a.rolloffMode = AudioRolloffMode.Linear;
                a.minDistance = 4; a.maxDistance = 140; a.dopplerLevel = 0;
                pool[i] = a;
            }
            var u = new GameObject("ui"); u.transform.SetParent(root, false);
            ui = u.AddComponent<AudioSource>(); ui.playOnAwake = false; ui.spatialBlend = 0;
            var mu = new GameObject("music"); mu.transform.SetParent(root, false);
            Music = mu.AddComponent<AudioSource>(); Music.playOnAwake = false; Music.spatialBlend = 0; Music.loop = true;
            Music.clip = MakeMusic(); Music.volume = Settings.MusicVolume * 0.5f;
        }

        public static AudioClip Get(string id) => clips.TryGetValue(id, out var c) ? c : null;

        public static void Play(string id, Vector3 pos, float vol = 1, float pitch = 1, float range = 140)
        {
            if (pool == null || !clips.TryGetValue(id, out var c)) return;
            var cam = Game.Cam;
            if (cam && (cam.transform.position - pos).sqrMagnitude > range * range * 1.1f) return;
            var a = pool[next]; next = (next + 1) % pool.Length;
            a.transform.position = pos;
            a.clip = c; a.volume = vol; a.pitch = pitch * (1 + Rnd() * 0.04f); a.maxDistance = range;
            a.Play();
        }

        public static void Play2D(string id, float vol = 1, float pitch = 1)
        {
            if (ui == null || !clips.TryGetValue(id, out var c)) return;
            ui.pitch = pitch;
            ui.PlayOneShot(c, vol);
        }

        public static AudioSource Loop(string id, Transform parent, float vol, float range = 90)
        {
            var go = new GameObject("loop_" + id);
            go.transform.SetParent(parent, false);
            var a = go.AddComponent<AudioSource>();
            a.clip = Get(id); a.loop = true; a.spatialBlend = 1; a.volume = vol; a.rolloffMode = AudioRolloffMode.Linear;
            a.minDistance = 5; a.maxDistance = range; a.dopplerLevel = 0;
            if (a.clip) a.time = UnityEngine.Random.value * a.clip.length * 0.8f;
            a.Play();
            return a;
        }

        /// <summary>A short brass-and-drum march loop, all synthesised (32 s).</summary>
        static AudioClip MakeMusic()
        {
            const float bpm = 104; float beat = 60f / bpm; int bars = 16; float len = bars * 4 * beat;
            int n = Mathf.CeilToInt(len * 22050);
            var d = new float[n];
            int[] roots = { 0, 0, 5, 3, 0, 0, 7, 5, 8, 7, 5, 3, 0, 3, 7, 0 };
            int[] mel = { 12, 15, 19, 17, 15, 12, 10, 12, 15, 19, 22, 19, 17, 15, 14, 12 };
            float Note(int semi) => 110f * Mathf.Pow(2, semi / 12f);
            var r = new System.Random(3);
            for (int i = 0; i < n; i++)
            {
                float t = i / 22050f;
                int b = (int)(t / beat); int bar = (b / 4) % bars; float tb = t - b * beat;
                float root = Note(roots[bar] - 12);
                float s = 0;
                // low string pad
                s += (Mathf.Sin(t * root * 6.283f) + Mathf.Sin(t * root * 1.5f * 6.283f) * 0.5f + Mathf.Sin(t * root * 2.0f * 6.283f) * 0.3f) * 0.12f;
                // brass melody (every half bar)
                int mi = (b / 2) % mel.Length; float tm = t - (b / 2) * beat * 2;
                float mf = Note(mel[(mi + bar) % mel.Length] + roots[bar] - 12);
                float br = ((t * mf % 1f) * 2 - 1) * 0.6f + Mathf.Sin(t * mf * 6.283f) * 0.4f;
                s += br * Mathf.Min(1, tm * 12) * Mathf.Exp(-tm * 1.4f) * 0.13f;
                // snare on 2 & 4, march roll on bar ends
                bool snare = b % 2 == 1 || (b % 4 == 3 && tb > beat * 0.5f);
                if (snare) s += ((float)r.NextDouble() * 2 - 1) * Mathf.Exp(-tb * 28) * 0.18f;
                // timpani on 1
                if (b % 4 == 0) s += Mathf.Sin(tb * 70 * 6.283f) * Mathf.Exp(-tb * 7) * 0.35f;
                d[i] = Mathf.Clamp(s, -1, 1);
            }
            var c = AudioClip.Create("music", n, 1, 22050, false);
            c.SetData(d, 0);
            return c;
        }
    }
}
