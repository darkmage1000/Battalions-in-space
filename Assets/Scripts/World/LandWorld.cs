using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    /// <summary>
    /// Builds a land battlefield from a LandMapDef: flat-shaded height-field terrain in chunks with
    /// mesh colliders, themed props (instanced visuals per chunk + primitive colliders), outposts and
    /// command posts with vehicle pads, sky, fog and a minimap texture.
    /// </summary>
    public class LandWorld : MonoBehaviour
    {
        public LandMapDef def;
        public ThemeInfo th;
        public float size, half;
        public Texture2D minimap;
        int res; float cell; float[] h;
        float ox, oz;
        readonly List<Vector2> postXZ = new List<Vector2>();
        readonly List<float> postH = new List<float>();
        Transform colRoot;
        System.Random rng;
        public readonly List<Vector3> lavaSpots = new List<Vector3>();

        public float PlayHalf => half * 0.8f;

        // ── height field ─────────────────────────────────────────────────
        float Noise(float x, float z) => Mathf.PerlinNoise(x + ox, z + oz);
        float Fbm(float x, float z, int oct = 4)
        {
            float s = 0, a = 0.5f, f = 1;
            for (int i = 0; i < oct; i++) { s += a * Noise(x * f, z * f); f *= 2.03f; a *= 0.5f; }
            return s;
        }

        float RawHeight(float x, float z)
        {
            float nx = x / size, nz = z / size;
            float b = (Fbm(nx * 3.2f, nz * 3.2f) - 0.47f) * 2f;
            float y = b * 16f * def.hills;
            switch (def.theme)
            {
                case Theme.Desert:
                    float warp = Fbm(nx * 2 + 9, nz * 2 + 9, 2) * 6;
                    y += (Mathf.Sin((x * 0.8f + z * 0.35f) / 17f + warp) * 0.5f + 0.5f) * 5f * def.hills;
                    y += Mathf.Max(0, Fbm(nx * 6 + 3, nz * 6 + 1, 3) - 0.62f) * 60f;   // rocky outcrops
                    break;
                case Theme.Forest:
                    y += (Fbm(nx * 7 + 4, nz * 7, 3) - 0.5f) * 8f;
                    break;
                case Theme.Snow:
                    float ridge = 1 - Mathf.Abs(Fbm(nx * 2.5f + 2, nz * 2.5f + 7, 4) * 2 - 1);
                    y += Mathf.Pow(ridge, 3) * 34f * def.hills;
                    break;
                case Theme.Volcanic:
                    float c = Fbm(nx * 4 + 5, nz * 4 + 5, 3);
                    y += Mathf.Pow(Mathf.Abs(c - 0.5f) * 2, 2) * 22f - 4;
                    break;
            }
            // mountains round the edge
            float d = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) / half;
            if (d > 0.76f) { float e = (d - 0.76f) / 0.24f; y += e * e * 75f + Fbm(nx * 9, nz * 9, 3) * e * 25f; }
            return y;
        }

        float BuiltHeight(float x, float z)
        {
            float y = RawHeight(x, z);
            for (int i = 0; i < postXZ.Count; i++)
            {
                float r = Vector2.Distance(new Vector2(x, z), postXZ[i]);
                if (r < 34)
                {
                    float w = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(16, 34, r));
                    y = Mathf.Lerp(postH[i], y, w);
                }
            }
            return y;
        }

        float H(int i, int j) => h[Mathf.Clamp(j, 0, res) * (res + 1) + Mathf.Clamp(i, 0, res)];

        /// <summary>Exact height of the terrain surface (matches the mesh triangles).</summary>
        public float Height(float x, float z)
        {
            float gx = (x + half) / cell, gz = (z + half) / cell;
            int i = Mathf.Clamp(Mathf.FloorToInt(gx), 0, res - 1), j = Mathf.Clamp(Mathf.FloorToInt(gz), 0, res - 1);
            float fx = Mathf.Clamp01(gx - i), fz = Mathf.Clamp01(gz - j);
            float h00 = H(i, j), h10 = H(i + 1, j), h01 = H(i, j + 1), h11 = H(i + 1, j + 1);
            if (fz > fx) return h00 + (h11 - h01) * fx + (h01 - h00) * fz;
            return h00 + (h10 - h00) * fx + (h11 - h10) * fz;
        }

        public Vector3 Ground(Vector3 p) => new Vector3(p.x, Height(p.x, p.z), p.z);

        public Vector3 Normal(float x, float z)
        {
            float e = 1.5f;
            return new Vector3(Height(x - e, z) - Height(x + e, z), 2 * e, Height(x, z - e) - Height(x, z + e)).normalized;
        }

        public bool InBounds(Vector3 p) => Mathf.Abs(p.x) < PlayHalf && Mathf.Abs(p.z) < PlayHalf;

        // ── build ────────────────────────────────────────────────────────
        public void Build(LandMapDef d, Battle battle)
        {
            def = d;
            th = ThemeInfo.Of(d.theme);
            size = d.size; half = size / 2;
            rng = new System.Random(d.seed);
            ox = (float)rng.NextDouble() * 500; oz = (float)rng.NextDouble() * 500;
            cell = 2.5f;
            res = Mathf.RoundToInt(size / cell);

            float postScale = half * 0.72f;
            foreach (var p in d.posts)
            {
                var xz = p.pos * postScale;
                postXZ.Add(xz);
                float avg = 0; int n = 0;
                for (int a = 0; a < 8; a++) { float an = a * Mathf.PI / 4; avg += RawHeight(xz.x + Mathf.Cos(an) * 12, xz.y + Mathf.Sin(an) * 12); n++; }
                postH.Add(Mathf.Max(avg / n, RawHeight(xz.x, xz.y) - 2));
            }

            h = new float[(res + 1) * (res + 1)];
            for (int j = 0; j <= res; j++)
                for (int i = 0; i <= res; i++)
                    h[j * (res + 1) + i] = BuiltHeight(-half + i * cell, -half + j * cell);

            colRoot = new GameObject("colliders").transform;
            colRoot.SetParent(transform, false);

            BuildTerrain();
            BuildMinimap();
            var props = new List<PropKit>();
            BuildProps(props);
            BuildPosts(battle);
            SetupAtmosphere();
        }

        Color GroundColor(float x, float z, float y, float slope)
        {
            float n = Mathf.PerlinNoise(x * 0.045f + 33, z * 0.045f + 71);
            float n2 = Mathf.PerlinNoise(x * 0.17f + 3, z * 0.17f + 9);
            float hn = Mathf.InverseLerp(-15, 35, y);
            Color c = Color.Lerp(th.low, th.mid, Mathf.SmoothStep(0, 1, n));
            c = Color.Lerp(c, th.high, Mathf.SmoothStep(0.45f, 0.95f, hn));
            if (n2 > 0.66f) c = Color.Lerp(c, th.patch, (n2 - 0.66f) * 2.5f);
            if (def.theme == Theme.Snow) c = Color.Lerp(c, th.steep, Mathf.SmoothStep(0.5f, 0.75f, slope));
            else c = Color.Lerp(c, th.steep, Mathf.SmoothStep(0.45f, 0.7f, slope));
            if (def.theme == Theme.Volcanic && y < -5 && n2 < 0.32f) { c = new Color(1f, 0.35f, 0.08f, 0); }
            c *= 0.94f + 0.12f * n2;
            c.a = c.a < 0.5f ? 0 : 1;
            return c;
        }

        void BuildTerrain()
        {
            const int chunkCells = 32;
            int chunks = res / chunkCells;
            var root = new GameObject("terrain").transform;
            root.SetParent(transform, false);
            var k = new MeshKit();
            for (int cz = 0; cz < chunks; cz++)
                for (int cx = 0; cx < chunks; cx++)
                {
                    k.Clear();
                    for (int j = cz * chunkCells; j < (cz + 1) * chunkCells; j++)
                        for (int i = cx * chunkCells; i < (cx + 1) * chunkCells; i++)
                        {
                            float x0 = -half + i * cell, z0 = -half + j * cell, x1 = x0 + cell, z1 = z0 + cell;
                            Vector3 p00 = new Vector3(x0, H(i, j), z0), p10 = new Vector3(x1, H(i + 1, j), z0);
                            Vector3 p01 = new Vector3(x0, H(i, j + 1), z1), p11 = new Vector3(x1, H(i + 1, j + 1), z1);
                            Vector3 n1 = Vector3.Cross(p01 - p00, p11 - p00).normalized;
                            Vector3 n2 = Vector3.Cross(p11 - p00, p10 - p00).normalized;
                            Vector3 c1 = (p00 + p01 + p11) / 3, c2 = (p00 + p11 + p10) / 3;
                            k.Tri(p00, p01, p11, GroundColor(c1.x, c1.z, c1.y, 1 - n1.y));
                            k.Tri(p00, p11, p10, GroundColor(c2.x, c2.z, c2.y, 1 - n2.y));
                        }
                    var go = k.Make($"chunk_{cx}_{cz}", root, false);
                    go.layer = Layers.Ground;
                    go.GetComponent<MeshRenderer>().receiveShadows = true;
                    var mc = go.AddComponent<MeshCollider>();
                    mc.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
                }
        }

        void BuildMinimap()
        {
            const int n = 256;
            minimap = new Texture2D(n, n, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp, name = "minimap" };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float wx = -half + (x + 0.5f) / n * size, wz = -half + (y + 0.5f) / n * size;
                    float hy = Height(wx, wz);
                    var nn = Normal(wx, wz);
                    Color c = GroundColor(wx, wz, hy, 1 - nn.y);
                    float shade = Mathf.Clamp01(0.6f + Vector3.Dot(nn, new Vector3(-0.5f, 0.8f, 0.3f).normalized) * 0.5f);
                    c = c * shade; c.a = 1;
                    float edge = Mathf.Max(Mathf.Abs(wx), Mathf.Abs(wz));
                    if (edge > PlayHalf) c *= 0.45f;
                    minimap.SetPixel(x, y, c);
                }
            minimap.Apply();
        }

        // ── props ────────────────────────────────────────────────────────
        class PropKit { public MeshKit kit = new MeshKit(); }

        float R() => (float)rng.NextDouble();
        float R(float a, float b) => a + (b - a) * (float)rng.NextDouble();

        bool NearPost(float x, float z, float r)
        {
            foreach (var p in postXZ) if ((new Vector2(x, z) - p).sqrMagnitude < r * r) return true;
            return false;
        }

        void Col(Vector3 pos, Vector3 size, Quaternion rot)
        {
            var go = new GameObject("c");
            go.transform.SetParent(colRoot, false);
            go.transform.position = pos; go.transform.rotation = rot;
            go.AddComponent<BoxCollider>().size = size;
        }
        void ColCap(Vector3 pos, float r, float hgt)
        {
            var go = new GameObject("c");
            go.transform.SetParent(colRoot, false);
            go.transform.position = pos;
            var c = go.AddComponent<CapsuleCollider>(); c.radius = r; c.height = hgt; c.center = Vector3.up * hgt / 2;
        }
        void ColSphere(Vector3 pos, float r)
        {
            var go = new GameObject("c");
            go.transform.SetParent(colRoot, false);
            go.transform.position = pos;
            go.AddComponent<SphereCollider>().radius = r;
        }

        void BuildProps(List<PropKit> unused)
        {
            const int grid = 8;
            float gs = size / grid;
            var kits = new MeshKit[grid * grid];
            for (int i = 0; i < kits.Length; i++) kits[i] = new MeshKit();
            MeshKit KitAt(float x, float z)
            {
                int gx = Mathf.Clamp((int)((x + half) / gs), 0, grid - 1), gz = Mathf.Clamp((int)((z + half) / gs), 0, grid - 1);
                return kits[gz * grid + gx];
            }

            int count = Mathf.RoundToInt(520 * def.props);
            float lim = half * 0.86f;
            for (int n = 0; n < count; n++)
            {
                float x = R(-lim, lim), z = R(-lim, lim);
                if (NearPost(x, z, 26)) continue;
                float y = Height(x, z);
                var nrm = Normal(x, z);
                float roll = R();
                var k = KitAt(x, z);
                Vector3 p = new Vector3(x, y, z);
                Quaternion yawQ = Quaternion.Euler(0, R(0, 360), 0);
                switch (def.theme)
                {
                    case Theme.Desert:
                        if (roll < 0.55f) { float s = R(1.5f, 5f); k.Rock(p + Vector3.up * s * 0.25f, new Vector3(s * 1.4f, s * 0.9f, s * 1.1f), yawQ, Color.Lerp(th.steep, th.high, R()), rng.Next(), 0.3f); ColSphere(p + Vector3.up * s * 0.2f, s * 0.5f); }
                        else if (roll < 0.7f && nrm.y > 0.9f) { float hh = R(10, 24), w = R(4, 8); k.Taper(p + Vector3.up * hh / 2, new Vector2(w, w * 0.8f), new Vector2(w * 0.7f, w * 0.55f), hh, yawQ, Color.Lerp(th.steep, th.mid, R() * 0.4f)); k.Box(p + Vector3.up * (hh + 0.3f), new Vector3(w * 0.75f, 0.6f, w * 0.6f), yawQ, th.high); Col(p + Vector3.up * hh / 2, new Vector3(w * 0.85f, hh, w * 0.7f), yawQ); }
                        else if (roll < 0.76f && nrm.y > 0.93f) Arch(k, p, yawQ);
                        else if (roll < 0.82f) Wreck(k, p, yawQ, nrm);
                        else { float s = R(0.4f, 0.8f); k.Cone(p + Vector3.up * s * 0.4f, s * 0.5f, s * 0.9f, yawQ, new Color(0.6f, 0.58f, 0.36f), 5); }
                        break;
                    case Theme.Forest:
                        if (roll < 0.42f && nrm.y > 0.8f) BigTree(k, p);
                        else if (roll < 0.62f && nrm.y > 0.8f) Pine(k, p, false);
                        else if (roll < 0.78f) { float s = R(1.2f, 3.6f); k.Rock(p + Vector3.up * s * 0.2f, new Vector3(s * 1.3f, s * 0.8f, s), yawQ, Color.Lerp(new Color(0.4f, 0.42f, 0.38f), th.patch, R() * 0.4f), rng.Next()); ColSphere(p + Vector3.up * s * 0.15f, s * 0.45f); }
                        else if (roll < 0.86f) { float l = R(5, 9); var q = Quaternion.Euler(0, R(0, 360), 90); k.Cyl(p + Vector3.up * 0.5f, 0.5f, l, q, new Color(0.38f, 0.28f, 0.2f), 7); Col(p + Vector3.up * 0.5f, new Vector3(1, l, 1), q); }
                        else { float s = R(0.8f, 1.8f); k.Sphere(p + Vector3.up * s * 0.4f, new Vector3(s * 1.4f, s, s * 1.4f), yawQ, Color.Lerp(th.low, new Color(0.2f, 0.4f, 0.15f), R()), 6, 4); }
                        break;
                    case Theme.Snow:
                        if (roll < 0.35f && nrm.y > 0.82f) Pine(k, p, true);
                        else if (roll < 0.7f) { float s = R(1.5f, 5f); k.Rock(p + Vector3.up * s * 0.2f, new Vector3(s * 1.3f, s * 0.9f, s), yawQ, Color.Lerp(th.steep, new Color(0.6f, 0.66f, 0.74f), R() * 0.5f), rng.Next()); k.Rock(p + Vector3.up * s * 0.42f, new Vector3(s * 1.1f, s * 0.35f, s * 0.85f), yawQ, th.high, rng.Next(), 0.15f); ColSphere(p + Vector3.up * s * 0.2f, s * 0.48f); }
                        else if (roll < 0.85f) { float hh = R(3, 9); k.Cone(p + Vector3.up * hh / 2, hh * 0.22f, hh, Quaternion.Euler(R(-12, 12), R(0, 360), R(-12, 12)), MeshKit.Glow(new Color(0.55f, 0.8f, 0.95f) * 0.55f), 5); ColCap(p, hh * 0.15f, hh * 0.8f); }
                        else Wreck(k, p, yawQ, nrm);
                        break;
                    case Theme.Volcanic:
                        if (roll < 0.45f) { float s = R(1.5f, 5.5f); k.Rock(p + Vector3.up * s * 0.2f, new Vector3(s * 1.3f, s, s * 1.1f), yawQ, Color.Lerp(new Color(0.1f, 0.09f, 0.1f), th.high, R() * 0.5f), rng.Next(), 0.35f); ColSphere(p + Vector3.up * s * 0.2f, s * 0.5f); }
                        else if (roll < 0.65f) { float hh = R(6, 20); k.Cone(p + Vector3.up * hh / 2, hh * 0.18f, hh, Quaternion.Euler(R(-8, 8), R(0, 360), R(-8, 8)), new Color(0.08f, 0.07f, 0.1f), 6); ColCap(p, hh * 0.14f, hh * 0.85f); }
                        else if (roll < 0.78f && y < 2) { float s = R(4, 9); k.Cyl(p + Vector3.up * 0.06f, s, 0.12f, Quaternion.identity, MeshKit.Glow(new Color(1f, 0.38f, 0.06f)), 10); lavaSpots.Add(p); }
                        else if (roll < 0.85f) { k.Rock(p, new Vector3(4, 2, 4), yawQ, new Color(0.15f, 0.12f, 0.12f), rng.Next()); k.Cyl(p + Vector3.up * 0.9f, 0.6f, 0.3f, Quaternion.identity, MeshKit.Glow(new Color(1f, 0.5f, 0.1f)), 8); ColSphere(p + Vector3.up * 0.4f, 1.8f); lavaSpots.Add(p + Vector3.up * 1.2f); }
                        else Wreck(k, p, yawQ, nrm);
                        break;
                }
            }
            var root = new GameObject("props").transform;
            root.SetParent(transform, false);
            for (int i = 0; i < kits.Length; i++) if (kits[i].Count > 0) kits[i].Make("props" + i, root, true);
        }

        void Arch(MeshKit k, Vector3 p, Quaternion q)
        {
            float span = R(10, 16), hh = R(9, 14), w = R(2.5f, 3.5f);
            Color c = Color.Lerp(th.steep, th.mid, 0.3f);
            Vector3 right = q * Vector3.right;
            Vector3 a = p + right * span / 2, b = p - right * span / 2;
            a.y = Height(a.x, a.z) - 1; b.y = Height(b.x, b.z) - 1;
            k.Taper(a + Vector3.up * (hh / 2), new Vector2(w * 1.3f, w), new Vector2(w, w * 0.8f), hh, q, c);
            k.Taper(b + Vector3.up * (hh / 2), new Vector2(w * 1.3f, w), new Vector2(w, w * 0.8f), hh, q, c);
            Vector3 top = (a + b) / 2 + Vector3.up * hh;
            k.Box(top, new Vector3(span + w, w * 0.8f, w * 0.9f), q, th.high);
            Col(a + Vector3.up * hh / 2, new Vector3(w * 1.2f, hh, w), q);
            Col(b + Vector3.up * hh / 2, new Vector3(w * 1.2f, hh, w), q);
            Col(top, new Vector3(span + w, w * 0.8f, w * 0.9f), q);
        }

        void Wreck(MeshKit k, Vector3 p, Quaternion q, Vector3 nrm)
        {
            Color m = new Color(0.36f, 0.36f, 0.38f), m2 = new Color(0.26f, 0.24f, 0.22f);
            var tilt = Quaternion.FromToRotation(Vector3.up, nrm) * q * Quaternion.Euler(R(-15, 15), 0, R(-25, 25));
            k.Box(p + Vector3.up * 1.0f, new Vector3(4.5f, 2f, 8f), tilt, m);
            k.Nose(p + Vector3.up * 1.0f + tilt * new Vector3(0, 0, 5.5f), new Vector3(4f, 1.8f, 3f), tilt, m2);
            k.Box(p + Vector3.up * 1.6f + tilt * new Vector3(3, 0, -1), new Vector3(3f, 0.3f, 2.5f), tilt * Quaternion.Euler(0, 0, -20), m2);
            k.Cyl(p + Vector3.up * 1.2f + tilt * new Vector3(0, 0, -4.4f), 0.8f, 1.2f, tilt * Quaternion.Euler(90, 0, 0), new Color(0.15f, 0.14f, 0.13f), 7);
            Col(p + Vector3.up * 1.0f, new Vector3(4.5f, 2f, 9f), tilt);
        }

        void BigTree(MeshKit k, Vector3 p)
        {
            float hh = R(16, 28), r = R(0.9f, 1.6f);
            Color bark = Color.Lerp(new Color(0.34f, 0.24f, 0.17f), new Color(0.45f, 0.36f, 0.26f), R());
            k.Cyl(p + Vector3.up * (hh / 2 - 1), r, hh, Quaternion.identity, bark, 8, r * 0.6f);
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90 + R(0, 40);
                k.Box(p + Quaternion.Euler(0, a, 0) * new Vector3(0, 0.3f, r * 1.2f), new Vector3(r * 0.5f, 1.4f, r * 1.4f), Quaternion.Euler(0, a, 0) * Quaternion.Euler(30, 0, 0), bark);
            }
            Color leaf = Color.Lerp(new Color(0.16f, 0.36f, 0.14f), new Color(0.32f, 0.5f, 0.2f), R());
            int blobs = 4 + rng.Next(3);
            for (int i = 0; i < blobs; i++)
            {
                Vector3 o = new Vector3(R(-4, 4), hh * R(0.75f, 1.05f), R(-4, 4));
                float s = R(6, 10);
                k.Sphere(p + o, new Vector3(s, s * 0.7f, s), Quaternion.Euler(0, R(0, 90), 0), leaf * R(0.85f, 1.1f), 7, 4);
            }
            ColCap(p - Vector3.up, r * 0.95f, hh);
        }

        void Pine(MeshKit k, Vector3 p, bool snow)
        {
            float hh = R(9, 17), r = hh * 0.22f;
            k.Cyl(p + Vector3.up * hh * 0.2f, 0.4f, hh * 0.4f, Quaternion.identity, new Color(0.3f, 0.22f, 0.16f), 6);
            Color c = snow ? new Color(0.2f, 0.32f, 0.26f) : new Color(0.14f, 0.3f, 0.18f);
            for (int i = 0; i < 3; i++)
            {
                float y = hh * (0.3f + i * 0.22f), cr = r * (1 - i * 0.25f);
                k.Cone(p + Vector3.up * (y + hh * 0.2f), cr, hh * 0.42f, Quaternion.Euler(0, i * 30, 0), c, 7);
                if (snow) k.Cone(p + Vector3.up * (y + hh * 0.28f), cr * 0.55f, hh * 0.27f, Quaternion.Euler(0, i * 30, 0), new Color(0.92f, 0.95f, 1f), 7);
            }
            ColCap(p, 0.5f, hh * 0.6f);
        }

        // ── posts ────────────────────────────────────────────────────────
        void BuildPosts(Battle battle)
        {
            for (int i = 0; i < def.posts.Length; i++)
            {
                var pd = def.posts[i];
                Vector3 p = new Vector3(postXZ[i].x, postH[i], postXZ[i].y);
                var go = new GameObject("post_" + pd.name);
                go.transform.SetParent(transform, false);
                go.transform.position = p;
                var cp = go.AddComponent<CommandPost>();
                cp.Init(battle, i, pd.name, pd.owner, p);
                bool baseCamp = pd.vehicles.Length >= 3;
                Outpost(go.transform, p, baseCamp, i);
                // vehicle pads round the post
                for (int v = 0; v < pd.vehicles.Length; v++)
                {
                    float ang = (i * 53 + v * 120 + 30) * Mathf.Deg2Rad;
                    float rad = pd.vehicles[v] == VKind.Air ? 24 : 20;
                    Vector3 pp = p + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * rad;
                    pp.y = Height(pp.x, pp.z);
                    Vector3 toward = -new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang));
                    // face outwards toward the map centre
                    Vector3 fwd = (-p).normalized; fwd.y = 0; if (fwd.sqrMagnitude < 0.1f) fwd = toward;
                    cp.AddPad(pd.vehicles[v], pp, Quaternion.LookRotation(fwd.normalized));
                    var k = new MeshKit();
                    k.Cyl(pp + Vector3.up * 0.05f, pd.vehicles[v] == VKind.Air ? 6 : 5, 0.2f, Quaternion.identity, new Color(0.3f, 0.3f, 0.32f), 12);
                    k.Ring(pp + Vector3.up * 0.17f, (pd.vehicles[v] == VKind.Air ? 5.4f : 4.4f), (pd.vehicles[v] == VKind.Air ? 5.8f : 4.8f), MeshKit.Glow(new Color(0.9f, 0.75f, 0.3f) * 0.6f), 24);
                    k.Make("pad", go.transform, false).layer = Layers.Default;
                }
                battle.posts.Add(cp);
            }
        }

        void Outpost(Transform parent, Vector3 p, bool big, int idx)
        {
            var k = new MeshKit();
            var r = new System.Random(def.seed + idx * 31);
            float Rn(float a, float b) => a + (b - a) * (float)r.NextDouble();
            Color wall = def.theme == Theme.Snow ? new Color(0.72f, 0.75f, 0.8f) : def.theme == Theme.Volcanic ? new Color(0.3f, 0.28f, 0.28f) : def.theme == Theme.Forest ? new Color(0.5f, 0.46f, 0.38f) : new Color(0.78f, 0.68f, 0.52f);
            Color dark = wall * 0.6f; dark.a = 1;
            // central platform
            k.Cyl(p + Vector3.up * 0.15f, 4.2f, 0.5f, Quaternion.identity, dark, 6);
            // low cover walls in a broken ring
            int segs = big ? 10 : 7;
            float rad = big ? 13 : 10;
            for (int s = 0; s < segs; s++)
            {
                if (r.NextDouble() < 0.3) continue;
                float a = (s + 0.5f) / segs * 360f + idx * 17;
                var q = Quaternion.Euler(0, a, 0);
                Vector3 wp = p + q * new Vector3(0, 0, rad);
                wp.y = Height(wp.x, wp.z);
                float len = Rn(3.5f, 5.5f), hh = Rn(1.1f, 1.5f);
                k.Box(wp + Vector3.up * (hh / 2 - 0.2f), new Vector3(len, hh, 0.7f), q, wall);
                k.Box(wp + Vector3.up * (hh - 0.15f), new Vector3(len + 0.2f, 0.15f, 0.85f), q, dark);
                Col(wp + Vector3.up * (hh / 2 - 0.2f), new Vector3(len, hh, 0.7f), q);
            }
            // crates
            int crates = big ? 8 : 4;
            for (int c = 0; c < crates; c++)
            {
                float a = Rn(0, 360); float d = Rn(5, rad - 2);
                Vector3 cpz = p + Quaternion.Euler(0, a, 0) * new Vector3(0, 0, d); cpz.y = Height(cpz.x, cpz.z);
                float s = Rn(0.9f, 1.4f);
                var q = Quaternion.Euler(0, Rn(0, 90), 0);
                k.Box(cpz + Vector3.up * s / 2, Vector3.one * s, q, new Color(0.42f, 0.38f, 0.3f));
                k.Box(cpz + Vector3.up * s / 2, new Vector3(s * 1.02f, s * 0.2f, s * 1.02f), q, new Color(0.3f, 0.3f, 0.32f));
                Col(cpz + Vector3.up * s / 2, Vector3.one * s, q);
            }
            if (big)
            {
                // a bunker with a doorway and a watch tower
                float a = idx * 47 + 200;
                var q = Quaternion.Euler(0, a, 0);
                Vector3 bp = p + q * new Vector3(0, 0, -7); bp.y = Height(bp.x, bp.z);
                k.Box(bp + q * new Vector3(-2.6f, 1.4f, 0), new Vector3(2, 3, 6), q, wall);
                k.Box(bp + q * new Vector3(2.6f, 1.4f, 0), new Vector3(2, 3, 6), q, wall);
                k.Box(bp + q * new Vector3(0, 2.6f, 0), new Vector3(7.4f, 0.8f, 6.4f), q, dark);
                Col(bp + q * new Vector3(-2.6f, 1.4f, 0), new Vector3(2, 3, 6), q);
                Col(bp + q * new Vector3(2.6f, 1.4f, 0), new Vector3(2, 3, 6), q);
                Col(bp + q * new Vector3(0, 2.6f, 0), new Vector3(7.4f, 0.8f, 6.4f), q);
                Vector3 tp = p + q * new Vector3(8, 0, 6); tp.y = Height(tp.x, tp.z);
                for (int l = 0; l < 4; l++)
                {
                    Vector3 o = new Vector3(l % 2 == 0 ? -1.2f : 1.2f, 3.5f, l < 2 ? -1.2f : 1.2f);
                    k.Box(tp + q * o, new Vector3(0.3f, 7, 0.3f), q, dark);
                }
                k.Box(tp + q * new Vector3(0, 7.1f, 0), new Vector3(3.4f, 0.3f, 3.4f), q, wall);
                k.Box(tp + q * new Vector3(0, 7.8f, 1.6f), new Vector3(3.4f, 1.1f, 0.2f), q, wall);
                k.Box(tp + q * new Vector3(0, 7.8f, -1.6f), new Vector3(3.4f, 1.1f, 0.2f), q, wall);
                Col(tp + q * new Vector3(0, 7.1f, 0), new Vector3(3.4f, 0.3f, 3.4f), q);
                Col(tp + q * new Vector3(0, 3.5f, 0), new Vector3(2.6f, 7, 2.6f), q);
            }
            k.Make("outpost", parent, true);
        }

        // ── atmosphere ───────────────────────────────────────────────────
        void SetupAtmosphere()
        {
            var sky = Mats.Sky(false);
            sky.SetColor("_Top", th.skyTop); sky.SetColor("_Horizon", th.skyHorizon); sky.SetColor("_Bottom", th.skyBottom);
            sky.SetColor("_SunCol", th.sun);
            var sunRot = Quaternion.Euler(th.sunPitch, th.sunYaw, 0);
            Vector3 sunDir = -(sunRot * Vector3.forward);
            sky.SetVector("_SunDir", sunDir);
            sky.SetFloat("_Seed", def.seed % 97);
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = th.ambientSky;
            RenderSettings.ambientEquatorColor = Color.Lerp(th.ambientSky, th.ambientGround, 0.5f);
            RenderSettings.ambientGroundColor = th.ambientGround;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = th.fog;
            RenderSettings.fogDensity = th.fogDensity;
            Game.I.sun.transform.rotation = sunRot;
            Game.I.sun.color = th.sun;
            Game.I.sun.intensity = def.theme == Theme.Volcanic ? 0.9f : 1.15f;
            Game.I.sun.shadows = LightShadows.Soft;
            Game.Cam.clearFlags = CameraClearFlags.Skybox;
            Game.Cam.farClipPlane = 1400;
        }

        void Update()
        {
            // smoke and embers over lava
            if (lavaSpots.Count > 0 && Random.value < 0.35f)
            {
                var s = lavaSpots[Random.Range(0, lavaSpots.Count)];
                if (Game.Cam && (Game.Cam.transform.position - s).sqrMagnitude < 200 * 200)
                {
                    Fx.Smoke(s + Random.insideUnitSphere * 2 + Vector3.up, 3f, 4f);
                    Fx.Emit(s + Random.insideUnitSphere * 2, Vector3.up * Random.Range(2, 5), new Color(1f, 0.5f, 0.15f, 1), 0.15f, 1.5f);
                }
            }
        }
    }
}
