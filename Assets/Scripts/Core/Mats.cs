using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    /// <summary>All materials are built in code from the three shaders in Resources/Shaders.</summary>
    public static class Mats
    {
        static Shader litS, fxS, skyS;
        static Material lit, litNoShadowHull;
        static readonly Dictionary<int, Material> add = new Dictionary<int, Material>();
        static readonly Dictionary<int, Material> alpha = new Dictionary<int, Material>();
        static Texture2D glow, soft;

        public static Shader LitShader => litS ? litS : (litS = Resources.Load<Shader>("Shaders/BIS_Lit"));
        public static Shader FxShader => fxS ? fxS : (fxS = Resources.Load<Shader>("Shaders/BIS_Fx"));
        public static Shader SkyShader => skyS ? skyS : (skyS = Resources.Load<Shader>("Shaders/BIS_Sky"));

        /// <summary>The one shared vertex-colour material for every solid thing.</summary>
        public static Material Lit
        {
            get
            {
                if (!lit) { lit = new Material(LitShader) { name = "BIS_Lit", enableInstancing = true }; }
                return lit;
            }
        }

        /// <summary>Glowing round dot (particles, bolt heads).</summary>
        public static Texture2D Glow
        {
            get
            {
                if (glow) return glow;
                const int n = 64;
                glow = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "glow" };
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(1 - d);
                        a = a * a * (0.6f + 0.4f * a);
                        glow.SetPixel(x, y, new Color(1, 1, 1, a));
                    }
                glow.Apply(true);
                return glow;
            }
        }

        /// <summary>Soft cloudy blob (smoke).</summary>
        public static Texture2D Soft
        {
            get
            {
                if (soft) return soft;
                const int n = 64;
                soft = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "soft" };
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float nz = Mathf.PerlinNoise(x * 0.11f, y * 0.11f) * 0.5f + 0.5f;
                        float a = Mathf.Clamp01(1 - d) * nz;
                        soft.SetPixel(x, y, new Color(1, 1, 1, a * a * 1.4f));
                    }
                soft.Apply(true);
                return soft;
            }
        }

        static int Key(Color c, int extra) =>
            (Mathf.RoundToInt(c.r * 63) | (Mathf.RoundToInt(c.g * 63) << 6) | (Mathf.RoundToInt(c.b * 63) << 12) | (Mathf.RoundToInt(c.a * 63) << 18)) ^ (extra << 25);

        /// <summary>Additive glow material of a colour (bolts, engines, particles).</summary>
        public static Material Additive(Color c, bool tex = true)
        {
            int k = Key(c, tex ? 1 : 2);
            if (add.TryGetValue(k, out var m) && m) return m;
            m = new Material(FxShader) { name = "add", enableInstancing = true };
            m.SetFloat("_Src", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_Dst", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetColor("_Color", c);
            m.mainTexture = tex ? Glow : Texture2D.whiteTexture;
            m.renderQueue = 3100;
            add[k] = m;
            return m;
        }

        /// <summary>Alpha-blended material (holograms, shields, smoke, rings).</summary>
        public static Material Alpha(Color c, bool tex = false, bool smoke = false)
        {
            int k = Key(c, smoke ? 3 : tex ? 4 : 5);
            if (alpha.TryGetValue(k, out var m) && m) return m;
            m = new Material(FxShader) { name = "alpha", enableInstancing = true };
            m.SetFloat("_Src", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_Dst", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetColor("_Color", c);
            m.mainTexture = smoke ? Soft : tex ? Glow : Texture2D.whiteTexture;
            m.renderQueue = 3000;
            alpha[k] = m;
            return m;
        }

        public static Material Sky(bool space)
        {
            var m = new Material(SkyShader) { name = space ? "space_sky" : "land_sky" };
            m.SetFloat("_Space", space ? 1 : 0);
            return m;
        }
    }
}
