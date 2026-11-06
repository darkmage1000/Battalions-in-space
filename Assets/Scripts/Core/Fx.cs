using UnityEngine;

namespace BIS
{
    /// <summary>Two shared particle systems (glow and smoke) fed with Emit, plus helpers.</summary>
    public static class Fx
    {
        static ParticleSystem glow, smoke;

        static ParticleSystem MakeSystem(string name, Material mat, Transform parent, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false; main.playOnAwake = false; main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 1; main.startSpeed = 0; main.duration = 1;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var sz = ps.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, 0.6f), new Keyframe(0.3f, 1), new Keyframe(1, 1.3f)));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortingFudge = 0;
            ps.Play();
            return ps;
        }

        public static void Init(Transform parent)
        {
            glow = MakeSystem("fx_glow", Mats.Additive(Color.white), parent, 6000);
            smoke = MakeSystem("fx_smoke", Mats.Alpha(Color.white, smoke: true), parent, 3000);
            var dr = smoke.velocityOverLifetime; dr.enabled = false;
        }

        public static void Emit(Vector3 pos, Vector3 vel, Color col, float size, float life, bool isSmoke = false)
        {
            var ps = isSmoke ? smoke : glow;
            if (!ps) return;
            var ep = new ParticleSystem.EmitParams
            {
                position = pos, velocity = vel, startColor = col, startSize = size, startLifetime = life,
                rotation = Random.value * 360, applyShapeToPosition = false
            };
            ps.Emit(ep, 1);
        }

        public static bool netMute;      // set around effects a Wi-Fi guest makes for itself

        public static void Burst(Vector3 pos, Color col, int count, float speed, float size, float life)
        {
            if (!netMute) Net.NetHost.OnBurst(pos, col, count, speed, size, life);
            for (int i = 0; i < count; i++)
                Emit(pos, Random.insideUnitSphere * speed, col, size * Random.Range(0.6f, 1.3f), life * Random.Range(0.6f, 1.2f));
        }

        public static void Sparks(Vector3 pos, Vector3 normal, Color col, int count = 6)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 v = (normal + Random.insideUnitSphere * 0.9f).normalized * Random.Range(3f, 9f);
                Emit(pos, v, col, Random.Range(0.08f, 0.2f), Random.Range(0.15f, 0.4f));
            }
            Emit(pos, Vector3.zero, col, 0.7f, 0.12f);
        }

        public static void Explosion(Vector3 pos, float scale = 1, bool big = false)
        {
            if (!netMute) Net.NetHost.OnExplosion(pos, scale, big);
            Color fire = new Color(1f, 0.6f, 0.2f, 1), core = new Color(1f, 0.9f, 0.6f, 1);
            Emit(pos, Vector3.zero, core, 4 * scale, 0.25f);
            for (int i = 0; i < 14 * scale + 6; i++)
                Emit(pos + Random.insideUnitSphere * scale, Random.insideUnitSphere * 7 * scale, fire, Random.Range(1.2f, 2.6f) * scale, Random.Range(0.3f, 0.7f));
            for (int i = 0; i < 10 * scale + 4; i++)
                Emit(pos + Random.insideUnitSphere * scale, Random.onUnitSphere * Random.Range(8, 20) * scale, core, 0.25f, Random.Range(0.4f, 1f));
            for (int i = 0; i < 8 * scale + 3; i++)
                Emit(pos + Random.insideUnitSphere * scale * 1.5f, Random.insideUnitSphere * 2 * scale + Vector3.up * 2, new Color(0.15f, 0.14f, 0.13f, 0.7f),
                     Random.Range(2.5f, 4.5f) * scale, Random.Range(1.5f, 3f), true);
            Sfx.Play(big ? "explode_big" : "explode", pos, big ? 1f : 0.8f, Random.Range(0.85f, 1.1f), big ? 600 : 220);
        }

        public static void Smoke(Vector3 pos, float size = 1.5f, float life = 2f)
        {
            Emit(pos, Vector3.up * 1.5f + Random.insideUnitSphere * 0.6f, new Color(0.18f, 0.17f, 0.16f, 0.6f), size, life, true);
        }

        public static void Muzzle(Vector3 pos, Color col)
        {
            Emit(pos, Vector3.zero, col, 0.6f, 0.05f);
        }
    }
}
