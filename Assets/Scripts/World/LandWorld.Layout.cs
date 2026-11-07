using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    /// <summary>
    /// The designed part of a land map (LandMapDef.features): mountain ridges and crater rims that can't be
    /// climbed, roads that cut passes through them, rivers / lava / dry gulches with bridges where roads cross,
    /// frozen lakes, dense forests, towns of buildings and fields of big boulders.
    /// Heights are added on top of the noise terrain in LayoutHeight(); props are built in BuildLayoutProps().
    /// </summary>
    public partial class LandWorld
    {
        class Line
        {
            public FeatureKind kind;
            public Vector2[] p;
            public float[] cum;          // arc length at each point
            public float w, hgt, len;
            public Rect box;
            public float[] sh;           // per-sample height (roads: road surface, channels: liquid top / gulch rim)
            public const float Step = 3f;
            public float SampleH(float s) { float f = Mathf.Clamp(s / Step, 0, sh.Length - 1); int i = Mathf.Min((int)f, sh.Length - 2); return Mathf.Lerp(sh[i], sh[i + 1], f - i); }
            public Vector2 At(float s)
            {
                s = Mathf.Clamp(s, 0, len);
                for (int i = 0; i < p.Length - 1; i++) if (s <= cum[i + 1] || i == p.Length - 2) { float t = (s - cum[i]) / Mathf.Max(0.001f, cum[i + 1] - cum[i]); return Vector2.Lerp(p[i], p[i + 1], Mathf.Clamp01(t)); }
                return p[p.Length - 1];
            }
            public Vector2 DirAt(float s)
            {
                for (int i = 0; i < p.Length - 1; i++) if (s <= cum[i + 1] || i == p.Length - 2) return (p[i + 1] - p[i]).normalized;
                return Vector2.up;
            }
            /// <summary>Distance from q to the polyline, and the arc length of the nearest point.</summary>
            public float Dist(Vector2 q, out float s)
            {
                float best = float.MaxValue; s = 0;
                for (int i = 0; i < p.Length - 1; i++)
                {
                    Vector2 a = p[i], d = p[i + 1] - p[i];
                    float L2 = d.sqrMagnitude;
                    float t = L2 > 0 ? Mathf.Clamp01(Vector2.Dot(q - a, d) / L2) : 0;
                    float dd = (a + d * t - q).sqrMagnitude;
                    if (dd < best) { best = dd; s = cum[i] + t * Mathf.Sqrt(L2); }
                }
                return Mathf.Sqrt(best);
            }
        }

        class Bridge
        {
            public Vector2 c, u;          // centre, unit direction along the deck
            public float hl, hw, yMid, yEnd0, yEnd1, ramp;
            public bool Inside(Vector2 q, out float y)
            {
                Vector2 d = q - c;
                float a = Vector2.Dot(d, u), b = Vector2.Dot(d, new Vector2(-u.y, u.x));
                y = 0;
                if (Mathf.Abs(a) > hl || Mathf.Abs(b) > hw) return false;
                y = DeckY(a);
                return true;
            }
            public float DeckY(float a)
            {
                float m = Mathf.Abs(a);
                if (m <= hl - ramp) return yMid;
                float e = a < 0 ? yEnd0 : yEnd1;
                return Mathf.Lerp(yMid, e, (m - (hl - ramp)) / ramp);
            }
        }

        /// <summary>A building you can walk into: its doorway (outside / middle / inside points), centre and footprint.</summary>
        public class House { public Vector3 doorOut, door, doorIn, center; public Quaternion rot; public float w, d; public bool vault; }
        public readonly List<House> houses = new List<House>();
        readonly List<Line> ridges = new List<Line>(), roads = new List<Line>(), channels = new List<Line>();
        readonly List<Bridge> bridges = new List<Bridge>();
        readonly List<(Vector2 c, float r, float level)> lakes = new List<(Vector2, float, float)>();
        readonly List<(Vector2 c, float r, float hgt)> craters = new List<(Vector2, float, float)>();
        readonly List<(Vector2 c, float r)> fords = new List<(Vector2, float)>();
        // per terrain vertex: what is there (for colours, prop placement and liquids)
        byte[] vtag;                       // TagRoad | TagWet | TagLake | TagClear | TagRidge
        float[] liquidTop;                // NaN where there is no water or lava
        bool[] liquidLava;
        const byte TagRoad = 1, TagWet = 2, TagLake = 4, TagClear = 8, TagRidge = 16;
        float NS => half * 0.72f;         // map space -> metres (same as posts)

        Line MakeLine(MapFeature f)
        {
            var l = new Line { kind = f.kind, w = f.width, hgt = f.height, p = new Vector2[f.pts.Length] };
            for (int i = 0; i < f.pts.Length; i++) l.p[i] = f.pts[i] * NS;
            l.cum = new float[l.p.Length];
            for (int i = 1; i < l.p.Length; i++) l.cum[i] = l.cum[i - 1] + Vector2.Distance(l.p[i - 1], l.p[i]);
            l.len = l.cum[l.p.Length - 1];
            float pad = l.w + 30;
            float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
            foreach (var q in l.p) { x0 = Mathf.Min(x0, q.x); z0 = Mathf.Min(z0, q.y); x1 = Mathf.Max(x1, q.x); z1 = Mathf.Max(z1, q.y); }
            l.box = Rect.MinMaxRect(x0 - pad, z0 - pad, x1 + pad, z1 + pad);
            return l;
        }

        /// <summary>Smoothed noise-terrain height along a line, one sample every Line.Step metres.</summary>
        float[] Profile(Line l, float window)
        {
            int n = Mathf.Max(2, Mathf.CeilToInt(l.len / Line.Step) + 1);
            var raw = new float[n];
            for (int i = 0; i < n; i++) { var q = l.At(i * Line.Step); raw[i] = RawHeight(q.x, q.y); }
            var sm = new float[n];
            int k = Mathf.Max(1, Mathf.RoundToInt(window / Line.Step));
            for (int i = 0; i < n; i++)
            {
                float s = 0; int c = 0;
                for (int j = Mathf.Max(0, i - k); j <= Mathf.Min(n - 1, i + k); j++) { s += raw[j]; c++; }
                sm[i] = s / c;
            }
            return sm;
        }

        void PrepareLayout()
        {
            if (def.features == null) return;
            foreach (var f in def.features)
            {
                switch (f.kind)
                {
                    case FeatureKind.Ridge: ridges.Add(MakeLine(f)); break;
                    case FeatureKind.Road: { var l = MakeLine(f); l.sh = Profile(l, 20); roads.Add(l); break; }
                    case FeatureKind.River:
                    case FeatureKind.Lava:
                    case FeatureKind.Wadi:
                        {
                            var l = MakeLine(f);
                            l.sh = Profile(l, 30);
                            float drop = f.kind == FeatureKind.Wadi ? 0 : f.kind == FeatureKind.Lava ? 1.4f : 1.0f;
                            for (int i = 0; i < l.sh.Length; i++) l.sh[i] -= drop;
                            channels.Add(l);
                            break;
                        }
                    case FeatureKind.Lake:
                        {
                            Vector2 c = f.pts[0] * NS; float r = f.width * NS;
                            float lo = float.MaxValue;
                            for (int a = 0; a < 16; a++) for (int d = 0; d < 3; d++) { var q = c + new Vector2(Mathf.Cos(a * 0.3927f), Mathf.Sin(a * 0.3927f)) * r * (0.2f + d * 0.3f); lo = Mathf.Min(lo, RawHeight(q.x, q.y)); }
                            lakes.Add((c, r, lo - 0.4f));
                            break;
                        }
                    case FeatureKind.Crater: craters.Add((f.pts[0] * NS, f.width * NS, f.height)); break;
                    case FeatureKind.Ford: fords.Add((f.pts[0] * NS, f.width * NS)); break;
                    case FeatureKind.City: PrepareCity(f); break;
                    case FeatureKind.Isle: PrepareIsle(f); break;
                }
            }
            PrepareSpans();
            PreparePads();
        }

        /// <summary>Bridges where roads cross water, lava or a gulch (after road heights meet the posts).</summary>
        void MakeBridges()
        {
            foreach (var r in roads)
                foreach (var ch in channels)
                    for (int i = 0; i < r.p.Length - 1; i++)
                        for (int j = 0; j < ch.p.Length - 1; j++)
                        {
                            if (!SegHit(r.p[i], r.p[i + 1], ch.p[j], ch.p[j + 1], out float t)) continue;
                            float s = r.cum[i] + t * (r.cum[i + 1] - r.cum[i]);
                            Vector2 c = r.At(s), u = (r.p[i + 1] - r.p[i]).normalized;
                            Vector2 cd = (ch.p[j + 1] - ch.p[j]).normalized;
                            float sinA = Mathf.Max(0.35f, Mathf.Abs(u.x * cd.y - u.y * cd.x));
                            float bank = ch.kind == FeatureKind.Wadi ? 5 : 7;
                            float hl = (ch.w / 2 + bank) / sinA + 4;
                            ch.Dist(c, out float cs);
                            float top = ch.SampleH(cs);
                            var br = new Bridge { c = c, u = u, hl = hl, hw = r.w / 2 + 0.6f };
                            br.yEnd0 = r.SampleH(s - hl); br.yEnd1 = r.SampleH(s + hl);
                            br.yMid = Mathf.Max(r.SampleH(s) + 0.3f, top + (ch.kind == FeatureKind.Lava ? 2.2f : 1.4f), Mathf.Max(br.yEnd0, br.yEnd1) + 0.2f);
                            br.ramp = Mathf.Clamp((br.yMid - Mathf.Min(br.yEnd0, br.yEnd1)) / 0.3f, 4, hl * 0.45f);
                            bridges.Add(br);
                        }
        }

        /// <summary>Road heights blend into the post plateaus so roads arrive level with them.</summary>
        void BlendRoadsToPosts()
        {
            foreach (var r in roads)
                for (int i = 0; i < r.sh.Length; i++)
                {
                    var q = r.At(i * Line.Step);
                    for (int k = 0; k < postXZ.Count; k++)
                    {
                        float d = Vector2.Distance(q, postXZ[k]);
                        if (d < 70) r.sh[i] = Mathf.Lerp(postH[k], r.sh[i], Mathf.SmoothStep(0, 1, Mathf.InverseLerp(28, 70, d)));
                    }
                }
        }

        static bool SegHit(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out float t)
        {
            Vector2 r = b - a, s = d - c;
            float den = r.x * s.y - r.y * s.x;
            t = 0;
            if (Mathf.Abs(den) < 1e-5f) return false;
            Vector2 ca = c - a;
            t = (ca.x * s.y - ca.y * s.x) / den;
            float u = (ca.x * r.y - ca.y * r.x) / den;
            return t >= 0 && t <= 1 && u >= 0 && u <= 1;
        }

        /// <summary>Noise terrain plus everything designed (before posts are flattened in).</summary>
        float LayoutHeight(float x, float z, out byte tg, out float liq, out bool lava)
        {
            tg = 0; liq = float.NaN; lava = false;
            float y = RawHeight(x, z);
            if (isles.Count > 0) y = IsleHeight(x, z, y, ref tg);
            Vector2 q = new Vector2(x, z);
            // ridges and crater rims: steep walls with a jagged top
            float add = 0;
            foreach (var l in ridges)
            {
                if (!l.box.Contains(q)) continue;
                float d = l.Dist(q, out float s);
                float hw = l.w / 2, core = l.w * 0.22f;
                if (d >= hw) continue;
                float prof = d <= core ? 1 : 1 - Mathf.SmoothStep(0, 1, (d - core) / (hw - core));
                // taper the ends so a ridge doesn't stop in a flat wall
                float end = Mathf.Min(s, l.len - s);
                prof *= Mathf.SmoothStep(0.25f, 1, Mathf.Clamp01(end / Mathf.Max(8, l.w * 0.6f)) * 0.75f + 0.25f);
                float jag = 0.72f + 0.55f * Mathf.PerlinNoise(x * 0.025f + 11, z * 0.025f + 5) + 0.12f * Mathf.PerlinNoise(x * 0.2f, z * 0.2f);
                add = Mathf.Max(add, l.hgt * prof * jag);
            }
            foreach (var cr in craters)
            {
                float d = Mathf.Abs(Vector2.Distance(q, cr.c) - cr.r);
                float hw = cr.hgt * 1.25f;
                if (d >= hw) continue;
                float prof = 1 - Mathf.SmoothStep(0, 1, d / hw);
                float jag = 0.75f + 0.5f * Mathf.PerlinNoise(x * 0.04f + 3, z * 0.04f + 8);
                add = Mathf.Max(add, cr.hgt * prof * jag);
            }
            if (add > 6) tg |= TagRidge;
            y += add;
            foreach (var lk in lakes)
            {
                float d = Vector2.Distance(q, lk.c);
                if (d < lk.r) { y = Mathf.Lerp(lk.level, y, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(lk.r * 0.75f, lk.r, d))); if (d < lk.r * 0.85f) tg |= TagLake | TagClear; }
            }
            if (pads.Count > 0) y = PadHeight(q, y, ref tg);
            // roads level the ground and cut passes through ridges
            foreach (var l in roads)
            {
                if (!l.box.Contains(q)) continue;
                float d = l.Dist(q, out float s);
                float hw = l.w / 2, bank = 9;
                if (d >= hw + bank) continue;
                float rh = l.SampleH(s);
                y = Mathf.Lerp(rh, y, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(hw, hw + bank, d)));
                if (d < hw) tg |= TagRoad;
                if (d < hw + 5) tg |= TagClear;
            }
            // rivers, lava and gulches carve channels
            foreach (var l in channels)
            {
                if (!l.box.Contains(q)) continue;
                float d = l.Dist(q, out float s);
                float hw = l.w / 2, bank = l.kind == FeatureKind.Wadi ? 5 : 7;
                if (d >= hw + bank) continue;
                float top = l.SampleH(s);
                float depth = l.kind == FeatureKind.Wadi ? l.hgt : l.kind == FeatureKind.Lava ? 3f : l.hgt;
                float bed = top - depth;
                foreach (var fd in fords) { float fdd = Vector2.Distance(q, fd.c); if (fdd < fd.r) bed = Mathf.Max(bed, Mathf.Lerp(top - 0.75f, bed, fdd / fd.r)); }
                float rim = l.kind == FeatureKind.Wadi ? top : top - 0.3f;
                float want = d < hw ? Mathf.Lerp(bed, rim, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(hw * 0.45f, hw, d))) : Mathf.Lerp(rim, y, Mathf.SmoothStep(0, 1, (d - hw) / bank));
                y = Mathf.Min(y, want);
                tg |= TagClear;
                if (d < hw + 1) tg |= TagWet;
                if (l.kind != FeatureKind.Wadi && d < hw)
                {
                    liq = float.IsNaN(liq) ? top : Mathf.Max(liq, top);
                    lava |= l.kind == FeatureKind.Lava;
                }
            }
            foreach (var br in bridges) if (br.Inside(q, out _)) tg |= TagClear;
            return y;
        }

        int VIndex(float x, float z)
        {
            int i = Mathf.Clamp(Mathf.RoundToInt((x + half) / cell), 0, res), j = Mathf.Clamp(Mathf.RoundToInt((z + half) / cell), 0, res);
            return j * (res + 1) + i;
        }
        byte TagAt(float x, float z) => vtag != null ? vtag[VIndex(x, z)] : (byte)0;

        /// <summary>Top of water or lava at this point (NaN if none).</summary>
        public bool LiquidAt(float x, float z, out float top, out bool lava)
        {
            top = float.NaN; lava = false;
            if (liquidTop == null) return false;
            int k = VIndex(x, z);
            top = liquidTop[k]; lava = liquidLava[k];
            return !float.IsNaN(top);
        }

        /// <summary>Standing height: the terrain, or a bridge deck over it.</summary>
        public float Top(float x, float z)
        {
            float y = Height(x, z);
            if (bridges.Count == 0 && spans.Count == 0) return y;
            var q = new Vector2(x, z);
            foreach (var br in bridges) if (br.Inside(q, out float by) && by > y) y = by;
            foreach (var sp in spans) if (sp.Inside(q, out float sy) && sy > y) y = sy;
            return y;
        }

        public bool Clear(float x, float z) => (TagAt(x, z) & TagClear) == 0;
        public bool OnRoad(float x, float z) => (TagAt(x, z) & TagRoad) != 0;

        Vector2 NearestRoadDir(Vector2 q, float within, out float dist)
        {
            dist = float.MaxValue; Vector2 dir = Vector2.zero;
            foreach (var l in roads)
            {
                float d = l.Dist(q, out float s);
                if (d < dist) { dist = d; dir = l.DirAt(s); }
            }
            return dist < within ? dir : Vector2.zero;
        }

        // ── props ────────────────────────────────────────────────────────
        Color WallColor => here == Theme.Snow ? new Color(0.72f, 0.75f, 0.8f) : here == Theme.Volcanic ? new Color(0.3f, 0.28f, 0.28f) : here == Theme.Forest ? new Color(0.5f, 0.42f, 0.32f)
                         : here == Theme.Urban ? new Color(0.6f, 0.58f, 0.55f) : here == Theme.Sky ? new Color(0.88f, 0.86f, 0.8f) : new Color(0.8f, 0.68f, 0.5f);

        void BuildLayoutProps(System.Func<float, float, MeshKit> kitAt)
        {
            var buildings = new List<(Vector2 c, float r)>();
            foreach (var br in bridges) BuildBridge(br);
            foreach (var l in channels) if (l.kind != FeatureKind.Wadi) BuildLiquid(l);
            BuildPlaces(kitAt, buildings);
            if (def.features == null) return;
            foreach (var f in def.features)
            {
                Vector2 c = f.pts[0] * NS;
                float r = f.width * NS;
                switch (f.kind)
                {
                    case FeatureKind.Forest: Forest(c, r, f.density, kitAt); break;
                    case FeatureKind.Boulders: BoulderField(c, r, Mathf.RoundToInt(f.density), kitAt); break;
                    case FeatureKind.Town: Town(c, r, Mathf.RoundToInt(f.density), kitAt, buildings, f.height > 0 ? f.height : def.royale ? 0.8f : 0.55f); break;
                }
            }
            BuildWalls(kitAt, buildings);
            AtBase();
        }

        bool PropSpot(float x, float z, float postClear)
        {
            if (!Clear(x, z)) return false;
            if (NearPost(x, z, postClear)) return false;
            float lim = half * 0.86f;
            return Mathf.Abs(x) < lim && Mathf.Abs(z) < lim;
        }

        void Forest(Vector2 c, float r, float density, System.Func<float, float, MeshKit> kitAt)
        {
            At(c.x, c.y);
            float spacing = 8.5f / Mathf.Sqrt(Mathf.Max(0.2f, density));
            for (float gx = -r; gx <= r; gx += spacing)
                for (float gz = -r; gz <= r; gz += spacing)
                {
                    float x = c.x + gx + R(-0.45f, 0.45f) * spacing, z = c.y + gz + R(-0.45f, 0.45f) * spacing;
                    float d = Vector2.Distance(new Vector2(x, z), c);
                    float edge = r * (0.75f + 0.35f * Mathf.PerlinNoise(x * 0.03f + 7, z * 0.03f + 1));
                    if (d > edge) continue;
                    if (!PropSpot(x, z, 28)) continue;
                    var nrm = Normal(x, z);
                    if (nrm.y < 0.8f) continue;
                    Vector3 p = new Vector3(x, Height(x, z), z);
                    var k = kitAt(x, z);
                    float roll = R();
                    switch (here)
                    {
                        case Theme.Forest:
                            if (roll < 0.55f) BigTree(k, p); else if (roll < 0.88f) Pine(k, p, false);
                            else { float s = R(1.2f, 2.4f); k.Sphere(p + Vector3.up * s * 0.4f, new Vector3(s * 1.5f, s, s * 1.5f), Quaternion.identity, new Color(0.18f, 0.36f, 0.14f), 6, 4); }
                            break;
                        case Theme.Snow: Pine(k, p, true); break;
                        case Theme.Volcanic: DeadTree(k, p); break;
                        case Theme.Urban:
                        case Theme.Sky:
                            if (roll < 0.6f) BigTree(k, p); else Pine(k, p, false);
                            break;
                        default: { float s = R(2, 5); k.Rock(p + Vector3.up * s * 0.25f, new Vector3(s * 1.3f, s, s * 1.1f), Quaternion.Euler(0, R(0, 360), 0), Color.Lerp(th.steep, th.high, R()), rng.Next(), 0.3f); ColSphere(p + Vector3.up * s * 0.2f, s * 0.5f); break; }
                    }
                }
        }

        void DeadTree(MeshKit k, Vector3 p)
        {
            float hh = R(6, 12);
            Color bark = new Color(0.09f, 0.08f, 0.08f);
            k.Cyl(p + Vector3.up * hh / 2, 0.45f, hh, Quaternion.Euler(R(-6, 6), 0, R(-6, 6)), bark, 6, 0.15f);
            for (int i = 0; i < 3; i++)
            {
                float a = R(0, 360), y = hh * R(0.45f, 0.85f);
                var q = Quaternion.Euler(0, a, 0) * Quaternion.Euler(55, 0, 0);
                k.Cyl(p + Vector3.up * y + Quaternion.Euler(0, a, 0) * Vector3.forward * 0.8f, 0.18f, R(2, 3.5f), q, bark, 5, 0.05f);
            }
            if (R() < 0.3f) k.Cyl(p + Vector3.up * 0.2f, 0.7f, 0.25f, Quaternion.identity, MeshKit.Glow(new Color(1f, 0.4f, 0.1f) * 0.8f), 6);
            ColCap(p, 0.5f, hh * 0.8f);
        }

        void BoulderField(Vector2 c, float r, int count, System.Func<float, float, MeshKit> kitAt)
        {
            At(c.x, c.y);
            for (int n = 0, tries = 0; n < count && tries < count * 8; tries++)
            {
                float a = R(0, Mathf.PI * 2), d = Mathf.Sqrt(R()) * r;
                float x = c.x + Mathf.Cos(a) * d, z = c.y + Mathf.Sin(a) * d;
                if (!PropSpot(x, z, 30)) continue;
                n++;
                float s = R(4, 10);
                Vector3 p = new Vector3(x, Height(x, z), z);
                Color col = here == Theme.Snow ? Color.Lerp(th.steep, new Color(0.62f, 0.66f, 0.72f), R() * 0.6f)
                          : here == Theme.Volcanic ? Color.Lerp(new Color(0.08f, 0.07f, 0.09f), th.high, R() * 0.4f)
                          : here == Theme.Forest ? Color.Lerp(new Color(0.42f, 0.42f, 0.38f), new Color(0.3f, 0.4f, 0.24f), R() * 0.5f)
                          : here == Theme.Urban ? Color.Lerp(new Color(0.5f, 0.49f, 0.47f), new Color(0.36f, 0.34f, 0.33f), R())
                          : here == Theme.Sky ? Color.Lerp(th.steep, new Color(0.78f, 0.76f, 0.7f), R() * 0.6f)
                          : Color.Lerp(th.steep, th.high, R());
                var q = Quaternion.Euler(R(-10, 10), R(0, 360), R(-10, 10));
                var k = kitAt(x, z);
                k.Rock(p + Vector3.up * s * 0.3f, new Vector3(s * R(1.1f, 1.6f), s * R(0.8f, 1.2f), s), q, col, rng.Next(), 0.28f);
                if (here == Theme.Snow) k.Rock(p + Vector3.up * s * 0.75f, new Vector3(s * 1.1f, s * 0.3f, s * 0.85f), q, th.high, rng.Next(), 0.12f);
                ColSphere(p + Vector3.up * s * 0.3f, s * 0.52f);
            }
        }

        // ── towns ────────────────────────────────────────────────────────
        void Town(Vector2 c, float r, int count, System.Func<float, float, MeshKit> kitAt, List<(Vector2 c, float r)> placed, float shellShare = 0.55f)
        {
            for (int n = 0, tries = 0; n < count && tries < count * 25; tries++)
            {
                float a = R(0, Mathf.PI * 2), d = Mathf.Sqrt(R()) * r;
                Vector2 q = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                float w = R(7, 12), dp = R(6, 10), rad = Mathf.Sqrt(w * w + dp * dp) / 2;
                // keep clear of roads, water, posts and each other
                bool ok = true;
                for (int k = 0; k < 8 && ok; k++) { float an = k * Mathf.PI / 4; if (!PropSpot(q.x + Mathf.Cos(an) * rad, q.y + Mathf.Sin(an) * rad, 32)) ok = false; }
                if (!ok || !PropSpot(q.x, q.y, 32)) continue;
                foreach (var b in placed) if (Vector2.Distance(b.c, q) < b.r + rad + 3.5f) { ok = false; break; }
                if (!ok) continue;
                Vector2 rd = NearestRoadDir(q, 35, out _);
                float yaw = rd != Vector2.zero ? Mathf.Atan2(rd.x, rd.y) * Mathf.Rad2Deg + (R() < 0.5f ? 0 : 90) : R(0, 360);
                var rot = Quaternion.Euler(0, yaw, 0);
                float lo = float.MaxValue, hi = float.MinValue;
                for (int k = 0; k < 4; k++)
                {
                    Vector3 corner = rot * new Vector3((k % 2 == 0 ? -0.5f : 0.5f) * w, 0, (k < 2 ? -0.5f : 0.5f) * dp);
                    float hh = Height(q.x + corner.x, q.y + corner.z);
                    lo = Mathf.Min(lo, hh); hi = Mathf.Max(hi, hh);
                }
                if (hi - lo > 3.5f) continue;
                placed.Add((q, rad));
                n++;
                var kit = kitAt(q.x, q.y);
                bool shell = hi - lo < 1.6f && R() < shellShare;
                // the doorway faces the nearest road, if any
                int door = 0;
                if (rd != Vector2.zero)
                {
                    Vector2 toRoad = Vector2.zero; float best = float.MaxValue;
                    foreach (var l in roads) { float dd = l.Dist(q, out float s); if (dd < best) { best = dd; toRoad = l.At(s) - q; } }
                    Vector3 local = Quaternion.Inverse(rot) * new Vector3(toRoad.x, 0, toRoad.y);
                    door = Mathf.Abs(local.x) > Mathf.Abs(local.z) ? (local.x > 0 ? 1 : 3) : (local.z > 0 ? 0 : 2);
                }
                if (shell) HouseShell(kit, new Vector3(q.x, hi, q.y), rot, w, dp, lo, door);
                else HouseSolid(kit, new Vector3(q.x, hi, q.y), rot, w, dp, lo);
            }
        }

        float BuildingHeight => here == Theme.Volcanic ? R(6, 11) : here == Theme.Snow ? R(4, 6) : here == Theme.Urban ? R(9, 18) : R(4.5f, 8);

        /// <summary>A solid block building: walls, a themed roof and some details. `floor` = highest ground under it.</summary>
        void HouseSolid(MeshKit k, Vector3 p, Quaternion rot, float w, float dp, float lowGround)
        {
            float hh = BuildingHeight;
            float foot = p.y - lowGround + 1;
            Color wall = WallColor * R(0.88f, 1.08f); wall.a = 1;
            Color dark = wall * 0.6f; dark.a = 1;
            Vector3 c = p + Vector3.up * (hh / 2 - foot / 2);
            k.Box(c, new Vector3(w, hh + foot, dp), rot, wall);
            Col(c, new Vector3(w, hh + foot, dp), rot);
            Vector3 top = p + Vector3.up * hh;
            switch (here)
            {
                case Theme.Desert:
                    k.Box(top + Vector3.up * 0.4f + rot * new Vector3(0, 0, dp / 2 - 0.2f), new Vector3(w, 0.8f, 0.4f), rot, dark);
                    k.Box(top + Vector3.up * 0.4f + rot * new Vector3(0, 0, -dp / 2 + 0.2f), new Vector3(w, 0.8f, 0.4f), rot, dark);
                    if (R() < 0.4f) k.Sphere(top + Vector3.up * 0.2f, new Vector3(dp * 0.7f, dp * 0.55f, dp * 0.7f), rot, Color.Lerp(wall, Color.white, 0.25f), 10, 5);
                    break;
                case Theme.Forest:
                    k.Wedge(top + Vector3.up * 1.6f, new Vector3(w + 1, 3.2f, dp + 1), rot, new Color(0.35f, 0.2f, 0.14f));
                    k.Box(p + Vector3.up * 1.2f + rot * new Vector3(0, 0, dp / 2 + 0.05f), new Vector3(1.3f, 2.4f, 0.12f), rot, new Color(0.22f, 0.14f, 0.1f));
                    break;
                case Theme.Snow:
                    k.Sphere(top, new Vector3(w * 0.9f, 2.6f, dp * 0.9f), rot, new Color(0.85f, 0.88f, 0.92f), 10, 4);
                    k.Cyl(top + rot * new Vector3(w * 0.3f, 2.6f, 0), 0.08f, 4, rot, new Color(0.3f, 0.3f, 0.32f), 4);
                    break;
                case Theme.Urban:
                    k.Box(top + Vector3.up * 0.45f, new Vector3(w + 0.2f, 0.9f, dp + 0.2f), rot, dark);
                    k.Box(top + Vector3.up * 0.55f, new Vector3(w - 1f, 1f, dp - 1f), rot, wall);
                    k.Box(top + rot * new Vector3(w * 0.2f, 1.4f, 0), new Vector3(2.2f, 1.6f, 1.8f), rot, new Color(0.45f, 0.46f, 0.48f));
                    break;
                case Theme.Sky:
                    k.Sphere(top + Vector3.up * 0.2f, new Vector3(Mathf.Min(w, dp) * 0.8f, 3f, Mathf.Min(w, dp) * 0.8f), rot, new Color(0.35f, 0.55f, 0.75f), 10, 5);
                    break;
                default:
                    k.Cyl(top + rot * new Vector3(w * 0.25f, 2.5f, dp * 0.2f), 0.9f, 5, rot, new Color(0.14f, 0.12f, 0.12f), 8);
                    k.Cyl(top + rot * new Vector3(w * 0.25f, 5.05f, dp * 0.2f), 0.75f, 0.12f, rot, MeshKit.Glow(new Color(1f, 0.45f, 0.1f)), 8);
                    lavaSpots.Add(top + rot * new Vector3(w * 0.25f, 5.2f, dp * 0.2f));
                    k.Box(p + Vector3.up * 1.5f + rot * new Vector3(0, 0, dp / 2 + 0.06f), new Vector3(w * 0.6f, 0.3f, 0.1f), rot, MeshKit.Glow(new Color(1f, 0.5f, 0.15f) * 0.6f));
                    break;
            }
            // windows
            for (int s = 0; s < 2; s++)
                for (float x = -w / 2 + 1.6f; x < w / 2 - 1; x += 2.6f)
                    k.Box(p + Vector3.up * (hh * 0.6f) + rot * new Vector3(x, 0, (s == 0 ? 1 : -1) * (dp / 2 + 0.03f)), new Vector3(1.1f, 1f, 0.08f), rot, new Color(0.12f, 0.13f, 0.15f));
        }

        /// <summary>A building you can walk into: four walls with a doorway, window gaps and a flat roof.</summary>
        House HouseShell(MeshKit k, Vector3 p, Quaternion rot, float w, float dp, float lowGround, int door, float doorW = 2.2f, float height = 0, bool windows = true, Color? wallCol = null)
        {
            float hh = height > 0 ? height : Mathf.Min(BuildingHeight, 5.5f), t = 0.45f;
            Color wall = (wallCol ?? WallColor) * R(0.85f, 1.05f); wall.a = 1;
            House made = null;
            Color dark = wall * 0.6f; dark.a = 1;
            float foot = p.y - lowGround + 0.4f;
            // floor slab (counts as ground)
            Vector3 fc = p + Vector3.up * (0.1f - foot / 2);
            k.Box(fc, new Vector3(w, foot + 0.2f, dp), rot, dark);
            var fgo = new GameObject("floor"); fgo.transform.SetParent(colRoot, false); fgo.transform.position = fc; fgo.transform.rotation = rot; fgo.AddComponent<BoxCollider>().size = new Vector3(w, foot + 0.2f, dp); fgo.layer = Layers.Ground;
            Vector3 b = p + Vector3.up * 0.2f;
            // walls: side 0 = +z, 1 = +x, 2 = -z, 3 = -x
            for (int s = 0; s < 4; s++)
            {
                bool alongX = s % 2 == 0;
                float len = alongX ? w : dp;
                Vector3 n = s == 0 ? Vector3.forward : s == 1 ? Vector3.right : s == 2 ? Vector3.back : Vector3.left;
                Vector3 centre = b + rot * (n * ((alongX ? dp : w) / 2 - t / 2));
                Vector3 along = rot * (alongX ? Vector3.right : Vector3.forward);
                var wq = rot * Quaternion.LookRotation(n);
                float gap = s == door ? doorW : 1.4f, gapH = s == door ? Mathf.Min(hh - 0.6f, doorW > 3 ? 4.6f : 2.7f) : 1.2f, gapY = s == door ? 0 : 1.2f;
                if (s != door && !windows) { Vector3 pc0 = centre + Vector3.up * hh / 2; k.Box(pc0, new Vector3(len, hh, t), wq, wall); Col(pc0, new Vector3(len, hh, t), wq); continue; }
                float side = (len - gap) / 2;
                // two wall pieces either side of the opening, then above and below it
                for (int e = -1; e <= 1; e += 2)
                {
                    Vector3 pc = centre + along * e * (gap / 2 + side / 2) + Vector3.up * hh / 2;
                    k.Box(pc, new Vector3(side, hh, t), wq, wall); Col(pc, new Vector3(side, hh, t), wq);
                }
                Vector3 above = centre + Vector3.up * ((gapY + gapH + hh) / 2);
                k.Box(above, new Vector3(gap, hh - gapY - gapH, t), wq, wall); Col(above, new Vector3(gap, hh - gapY - gapH, t), wq);
                if (gapY > 0) { Vector3 below = centre + Vector3.up * (gapY / 2); k.Box(below, new Vector3(gap, gapY, t), wq, wall); Col(below, new Vector3(gap, gapY, t), wq); }
            }
            {
                Vector3 n = door == 0 ? Vector3.forward : door == 1 ? Vector3.right : door == 2 ? Vector3.back : Vector3.left;
                float half = door % 2 == 0 ? dp / 2 : w / 2;
                Vector3 dn = rot * n;
                Vector3 dm = new Vector3(p.x, 0, p.z) + dn * (half - t / 2);
                var hs = new House { door = dm, doorOut = dm + dn * 2.6f, doorIn = dm - dn * 1.6f, center = new Vector3(p.x, 0, p.z), rot = rot, w = w, d = dp };
                hs.door.y = p.y + 0.2f; hs.doorIn.y = p.y + 0.2f; hs.center.y = p.y + 0.2f; hs.doorOut.y = Height(hs.doorOut.x, hs.doorOut.z);
                houses.Add(hs);
                made = hs;
                // steps up to a doorway that stands above the ground outside it
                float floorTop = p.y + 0.2f;
                Vector3 o1 = dm + dn * 1.2f;
                float diff = floorTop - Height(o1.x, o1.z);
                if (diff > 0.3f)
                {
                    int ns = Mathf.CeilToInt(diff / 0.35f);
                    var dq = Quaternion.LookRotation(dn);
                    for (int i = 0; i < ns; i++)
                    {
                        float topY = floorTop - diff * (i + 1) / (ns + 1f);
                        Vector3 sc = dm + dn * (0.55f + i * 0.6f);
                        float gy = Height(sc.x, sc.z) - 0.3f;
                        float hgt = Mathf.Max(0.1f, topY - gy);
                        Vector3 c0 = new Vector3(sc.x, gy + hgt / 2, sc.z);
                        k.Box(c0, new Vector3(2.4f, hgt, 0.62f), dq, dark);
                        var sg = new GameObject("step"); sg.transform.SetParent(colRoot, false); sg.transform.SetPositionAndRotation(c0, dq);
                        sg.AddComponent<BoxCollider>().size = new Vector3(2.4f, hgt, 0.62f); sg.layer = Layers.Ground;
                    }
                    hs.doorOut = dm + dn * (0.9f + ns * 0.6f); hs.doorOut.y = Height(hs.doorOut.x, hs.doorOut.z);
                }
            }
            Vector3 roof = b + Vector3.up * (hh + 0.2f);
            k.Box(roof, new Vector3(w + 0.4f, 0.4f, dp + 0.4f), rot, dark);
            Col(roof, new Vector3(w + 0.4f, 0.4f, dp + 0.4f), rot);
            if (here == Theme.Forest) k.Wedge(roof + Vector3.up * 1.4f, new Vector3(w + 0.8f, 2.6f, dp + 0.8f), rot, new Color(0.35f, 0.2f, 0.14f));
            if (here == Theme.Snow) k.Box(roof + Vector3.up * 0.3f, new Vector3(w + 0.2f, 0.25f, dp + 0.2f), rot, new Color(0.93f, 0.95f, 1f));
            return made;
        }

        // ── bridges and liquids ──────────────────────────────────────────
        void BuildBridge(Bridge br)
        {
            At(br.c.x, br.c.y);
            var k = new MeshKit();
            Vector3 U = new Vector3(br.u.x, 0, br.u.y), V = new Vector3(-br.u.y, 0, br.u.x);
            Vector3 C = new Vector3(br.c.x, 0, br.c.y);
            Color deck = here == Theme.Forest ? new Color(0.45f, 0.32f, 0.22f) : here == Theme.Volcanic ? new Color(0.25f, 0.24f, 0.26f) : here == Theme.Snow ? new Color(0.55f, 0.58f, 0.62f) : new Color(0.62f, 0.5f, 0.36f);
            Color dark = deck * 0.6f; dark.a = 1;
            float[] us = { -br.hl, -br.hl + br.ramp, br.hl - br.ramp, br.hl };
            const float thick = 0.6f;
            for (int i = 0; i < 3; i++)
            {
                float a0 = us[i], a1 = us[i + 1];
                Vector3 p0 = C + U * a0 + Vector3.up * br.DeckY(a0), p1 = C + U * a1 + Vector3.up * br.DeckY(a1);
                Vector3 l0 = p0 - V * br.hw, r0 = p0 + V * br.hw, l1 = p1 - V * br.hw, r1 = p1 + V * br.hw;
                k.Quad(r0, r1, l1, l0, deck);
                Vector3 dn = Vector3.down * thick;
                k.Quad(l0 + dn, l1 + dn, r1 + dn, r0 + dn, dark);
                k.Quad(r0 + dn, r1 + dn, r1, r0, dark);
                k.Quad(l0, l1, l1 + dn, l0 + dn, dark);
            }
            var go = k.Make("bridge", transform, true);
            go.layer = Layers.Ground;
            go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
            // rails along the level middle part, pillars down into the channel
            var k2 = new MeshKit();
            float midLen = (br.hl - br.ramp) * 2;
            var q = Quaternion.LookRotation(U);
            for (int e = -1; e <= 1; e += 2)
            {
                Vector3 rc = C + V * e * (br.hw - 0.15f) + Vector3.up * (br.yMid + 0.55f);
                k2.Box(rc, new Vector3(0.3f, 1.1f, midLen), q, dark);
                Col(rc, new Vector3(0.3f, 1.1f, midLen), q);
                for (float a = -midLen / 2; a <= midLen / 2 + 0.1f; a += Mathf.Max(6, midLen / 3))
                {
                    Vector3 pc = C + U * a + V * e * (br.hw - 0.4f);
                    float gy = Height(pc.x, pc.z);
                    float hgt = br.yMid - thick - gy + 1;
                    if (hgt > 0.5f) k2.Cyl(pc + Vector3.up * (gy - 1 + hgt / 2), 0.45f, hgt, Quaternion.identity, dark, 6);
                }
            }
            k2.Make("bridgeRails", transform, true);
        }

        void BuildLiquid(Line l)
        {
            At(l.p[0].x, l.p[0].y);
            var k = new MeshKit();
            bool lava = l.kind == FeatureKind.Lava;
            Color col = lava ? MeshKit.Glow(new Color(1f, 0.42f, 0.08f)) : Color.white;
            float step = 4;
            int n = Mathf.CeilToInt(l.len / step);
            for (int i = 0; i < n; i++)
            {
                float s0 = i * step, s1 = Mathf.Min(l.len, s0 + step);
                Vector2 a = l.At(s0), b = l.At(s1);
                Vector2 da = l.DirAt(s0), db = l.DirAt(Mathf.Max(0, s1 - 0.01f));
                Vector2 na = new Vector2(-da.y, da.x) * (l.w / 2 + 1), nb = new Vector2(-db.y, db.x) * (l.w / 2 + 1);
                float ya = l.SampleH(s0) - 0.05f, yb = l.SampleH(s1) - 0.05f;
                Vector3 A0 = new Vector3(a.x - na.x, ya, a.y - na.y), A1 = new Vector3(a.x + na.x, ya, a.y + na.y);
                Vector3 B0 = new Vector3(b.x - nb.x, yb, b.y - nb.y), B1 = new Vector3(b.x + nb.x, yb, b.y + nb.y);
                k.Quad(A1, B1, B0, A0, col);
                if (lava && i % 5 == 0) lavaSpots.Add(new Vector3(a.x, ya + 0.3f, a.y));
            }
            var go = k.Make(lava ? "lava" : "water", transform, false, lava ? null : Mats.Alpha(here == Theme.Snow ? new Color(0.55f, 0.75f, 0.85f, 0.75f) : new Color(0.22f, 0.42f, 0.5f, 0.72f)));
            go.layer = Layers.Default;
        }
    }
}
