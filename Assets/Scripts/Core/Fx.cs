using UnityEngine;

namespace BIS
{
    /// <summary>
    /// Shared particle systems fed with Emit, plus helpers. Glow dots (bolts, sparks, embers) are drawn in code; smoke,
    /// fireballs, muzzle flashes, flying dirt and the scorch marks explosions leave on the ground are hand-drawn sprites
    /// from Kenney's CC0 Particle Pack (Resources/Textures/Fx, 2 x 2 sheets - each particle picks one of the four).
    /// </summary>
    public static class Fx
    {
        static ParticleSystem glow, smoke, fire, flash, debris, scorch;

        static Material SheetMat(string tex, bool additive, Texture fallback)
        {
            var t = Resources.Load<Texture2D>("Textures/Fx/" + tex);
            var m = new Material(Mats.FxShader) { name = "fx_" + tex, enableInstancing = true };
            m.SetFloat("_Src", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_Dst", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            m.SetColor("_Color", Color.white);
            m.mainTexture = t ? t : fallback;
            m.renderQueue = additive ? 3100 : 3000;
            return m;
        }

        static ParticleSystem MakeSystem(string name, Material mat, Transform parent, int max, bool sheet = false, Gradient fade = null, AnimationCurve grow = null)
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
            if (fade == null)
            {
                fade = new Gradient();
                fade.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                             new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0, 1) });
            }
            col.color = fade;
            var sz = ps.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1, grow ?? new AnimationCurve(new Keyframe(0, 0.6f), new Keyframe(0.3f, 1), new Keyframe(1, 1.3f)));
            if (sheet)
            {
                // one of the sheet's four sprites per particle, held for its whole life
                var ts = ps.textureSheetAnimation; ts.enabled = true;
                ts.mode = ParticleSystemAnimationMode.Grid;
                ts.numTilesX = 2; ts.numTilesY = 2;
                ts.animation = ParticleSystemAnimationType.WholeSheet;
                ts.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
                ts.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.999f);
                ts.cycleCount = 1;
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortingFudge = 0;
            ps.Play();
            return ps;
        }

        /// <summary>A gradient from c0 to c1 with alpha keys given as (time, alpha) pairs.</summary>
        static Gradient Keys(Color c0, Color c1, params float[] alpha)
        {
            var g = new Gradient();
            var ak = new GradientAlphaKey[alpha.Length / 2];
            for (int i = 0; i < ak.Length; i++) ak[i] = new GradientAlphaKey(alpha[i * 2 + 1], alpha[i * 2]);
            g.SetKeys(new[] { new GradientColorKey(c0, 0), new GradientColorKey(c1, 1) }, ak);
            return g;
        }

        public static void Init(Transform parent)
        {
            glow = MakeSystem("fx_glow", Mats.Additive(Color.white), parent, 6000);
            smoke = MakeSystem("fx_smoke", SheetMat("fx_smoke", false, Mats.Soft), parent, 3000, true,
                Keys(Color.white, Color.white, 0, 0, 0.12f, 1, 0.55f, 0.75f, 1, 0),
                new AnimationCurve(new Keyframe(0, 0.45f), new Keyframe(0.35f, 1), new Keyframe(1, 1.6f)));
            var sr = smoke.rotationOverLifetime; sr.enabled = true; sr.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
            // fireballs cool from yellow-white to a dull red as they fade
            fire = MakeSystem("fx_fire", SheetMat("fx_fire", true, Mats.Glow), parent, 2000, true,
                Keys(Color.white, new Color(0.75f, 0.28f, 0.12f), 0, 1, 0.5f, 0.75f, 1, 0),
                new AnimationCurve(new Keyframe(0, 0.5f), new Keyframe(0.25f, 1), new Keyframe(1, 1.25f)));
            var fr = fire.rotationOverLifetime; fr.enabled = true; fr.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);
            flash = MakeSystem("fx_flash", SheetMat("fx_flash", true, Mats.Glow), parent, 600, true,
                Keys(Color.white, Color.white, 0, 1, 0.6f, 0.7f, 1, 0),
                new AnimationCurve(new Keyframe(0, 0.7f), new Keyframe(1, 1.15f)));
            debris = MakeSystem("fx_debris", SheetMat("fx_debris", false, Mats.Soft), parent, 800, true,
                Keys(Color.white, Color.white, 0, 1, 0.75f, 1, 1, 0),
                new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0.8f)));
            var dm = debris.main; dm.gravityModifier = 1.6f;
            var dr = debris.rotationOverLifetime; dr.enabled = true; dr.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
            // scorch marks lie flat on the ground and fade out slowly
            scorch = MakeSystem("fx_scorch", SheetMat("fx_scorch", false, Mats.Soft), parent, 120, true,
                Keys(Color.white, Color.white, 0, 0, 0.02f, 1, 0.8f, 1, 1, 0),
                new AnimationCurve(new Keyframe(0, 0.8f), new Keyframe(0.05f, 1), new Keyframe(1, 1)));
            var scr = scorch.GetComponent<ParticleSystemRenderer>();
            scr.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            scr.sortingFudge = 40;   // drawn before the smoke above it
        }

        static void EmitTo(ParticleSystem ps, Vector3 pos, Vector3 vel, Color col, float size, float life)
        {
            if (!ps) return;
            var ep = new ParticleSystem.EmitParams
            {
                position = pos, velocity = vel, startColor = col, startSize = size, startLifetime = life,
                rotation = Random.value * 360, applyShapeToPosition = false
            };
            ps.Emit(ep, 1);
        }

        public static void Emit(Vector3 pos, Vector3 vel, Color col, float size, float life, bool isSmoke = false)
        {
            EmitTo(isSmoke ? smoke : glow, pos, vel, col, size, life);
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
            // a small puff of dust where it struck
            EmitTo(smoke, pos + normal * 0.15f, normal * 0.8f + Vector3.up * 0.4f, new Color(0.55f, 0.52f, 0.48f, 0.35f), Random.Range(0.5f, 0.8f), Random.Range(0.5f, 0.8f));
        }

        public static void Explosion(Vector3 pos, float scale = 1, bool big = false)
        {
            if (!netMute) Net.NetHost.OnExplosion(pos, scale, big);
            Color fireC = new Color(1f, 0.62f, 0.25f, 1), core = new Color(1f, 0.9f, 0.6f, 1);
            EmitTo(flash, pos, Vector3.zero, core, 12 * scale, 0.14f);
            Emit(pos, Vector3.zero, core, 4 * scale, 0.25f);
            // the fireball: a cluster of billowing flame sprites
            for (int i = 0; i < 8 * scale + 5; i++)
                EmitTo(fire, pos + Random.insideUnitSphere * scale * 0.9f, Random.insideUnitSphere * 5 * scale + Vector3.up * 1.2f * scale,
                       fireC * Random.Range(0.85f, 1.1f), Random.Range(2.2f, 3.8f) * scale, Random.Range(0.35f, 0.8f));
            // embers
            for (int i = 0; i < 10 * scale + 4; i++)
                Emit(pos + Random.insideUnitSphere * scale, Random.onUnitSphere * Random.Range(8, 20) * scale, core, 0.25f, Random.Range(0.4f, 1f));
            // a column of dark smoke that rises and spreads
            for (int i = 0; i < 7 * scale + 4; i++)
                EmitTo(smoke, pos + Random.insideUnitSphere * scale * 1.3f, Random.insideUnitSphere * 1.6f * scale + Vector3.up * Random.Range(1.5f, 3.5f),
                       new Color(0.16f, 0.15f, 0.14f, Random.Range(0.55f, 0.8f)), Random.Range(3f, 5f) * scale, Random.Range(1.8f, 3.4f));
            // near the ground: dirt thrown up and a scorch mark left behind
            if (Physics.Raycast(pos + Vector3.up * 0.5f, Vector3.down, out var hit, 2.5f + scale * 1.5f, Layers.SolidMask, QueryTriggerInteraction.Ignore) && hit.normal.y > 0.6f)
            {
                Color dirt = new Color(0.42f, 0.36f, 0.28f, 1);
                for (int i = 0; i < 5 * scale + 4; i++)
                {
                    Vector3 v = (Vector3.up * 1.4f + Random.insideUnitSphere).normalized * Random.Range(6f, 13f) * Mathf.Sqrt(scale);
                    EmitTo(debris, hit.point + Vector3.up * 0.3f, v, dirt * Random.Range(0.7f, 1.1f), Random.Range(0.5f, 1.1f) * scale, Random.Range(0.9f, 1.5f));
                }
                EmitTo(scorch, hit.point + hit.normal * 0.07f, Vector3.zero, new Color(0.05f, 0.045f, 0.04f, 0.85f), Random.Range(4f, 5.5f) * scale, big ? 30 : 18);
            }
            Sfx.Play(big ? "explode_big" : "explode", pos, big ? 1f : 0.8f, Random.Range(0.85f, 1.1f), big ? 600 : 220);
        }

        public static void Smoke(Vector3 pos, float size = 1.5f, float life = 2f)
        {
            Emit(pos, Vector3.up * 1.5f + Random.insideUnitSphere * 0.6f, new Color(0.18f, 0.17f, 0.16f, 0.6f), size, life, true);
        }

        /// <summary>A muzzle flash: a star-shaped flash in the bolt's colour round a white-hot core.</summary>
        public static void Muzzle(Vector3 pos, Color col)
        {
            Color c = col; c.a = 1;
            EmitTo(flash, pos, Vector3.zero, c, Random.Range(1.6f, 2.2f), 0.06f);
            Emit(pos, Vector3.zero, Color.Lerp(col, Color.white, 0.6f), 0.35f, 0.04f);
        }
    }
}
