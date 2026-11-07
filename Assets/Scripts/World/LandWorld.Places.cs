using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    /// <summary>
    /// Map pieces added for the city, the small town and the floating isles:
    /// - City: a street grid (real roads, so they colour, clear and guide vehicles) round blocks that hold a tower,
    ///   a cluster of buildings (some you can walk into), a park, a rubble lot or an open plaza (where posts stand).
    /// - Isles (floating maps): flat-topped islands at their own heights over a void. The ground under them is
    ///   never drawn or collided; falling below KillY kills. Undersides are rock cones, with clouds far below.
    /// - Spans: walkways between islands; a flight of stairs in the middle takes them up or down (slope StairSlope),
    ///   with rails. They are part of Top(), so bots path over them like bridges.
    /// - Walls (hedges, dry-stone walls, barriers with gaps) and landmark towers.
    /// </summary>
    public partial class LandWorld
    {
        public const float VoidY = -60f;
        /// <summary>Below this on a floating map you are gone.</summary>
        public float KillY => VoidY + 35f;
        public bool Floating => def != null && def.Floating;
        /// <summary>Nothing to stand on here (floating maps only).</summary>
        public bool OverVoid(Vector3 p) => Floating && Top(p.x, p.z) < VoidY + 5;
        /// <summary>Floating maps: stepping from `from` toward `to` goes over an edge (the ground there is far below the feet).</summary>
        public bool Ledge(Vector3 from, Vector3 to) => Floating && Top(to.x, to.z) < from.y - 3f;

        class Isle { public Vector2 c; public float r, level, seed; }
        readonly List<Isle> isles = new List<Isle>();

        class Span
        {
            public Vector2 a, u; public float len, w, y0, y1, s0, s1;
            public float DeckY(float s) => s <= s0 ? y0 : s >= s1 ? y1 : Mathf.Lerp(y0, y1, (s - s0) / Mathf.Max(0.01f, s1 - s0));
            public bool Inside(Vector2 q, out float y)
            {
                Vector2 d = q - a;
                float s = Vector2.Dot(d, u), t = Vector2.Dot(d, new Vector2(-u.y, u.x));
                y = 0;
                if (s < 0 || s > len || Mathf.Abs(t) > w / 2) return false;
                y = DeckY(s);
                return true;
            }
        }
        readonly List<Span> spans = new List<Span>();
        const float StairSlope = 0.62f, IsleCliff = 12f;

        class Block { public Vector2 c; public float size; }
        readonly List<Block> blocks = new List<Block>();

        // ── layout ───────────────────────────────────────────────────────
        void PrepareIsle(MapFeature f) => isles.Add(new Isle { c = f.pts[0] * NS, r = f.width * NS, level = f.height, seed = isles.Count * 7.31f + 3 });

        /// <summary>Spans need the islands first: each end takes the height of the island it stands on.</summary>
        void PrepareSpans()
        {
            if (def.features == null) return;
            foreach (var f in def.features)
            {
                if (f.kind != FeatureKind.Span) continue;
                Vector2 a = f.pts[0] * NS, b = f.pts[1] * NS;
                var sp = new Span { a = a, u = (b - a).normalized, len = Vector2.Distance(a, b), w = f.width };
                sp.y0 = IsleLevelAt(a); sp.y1 = IsleLevelAt(b);
                float rise = Mathf.Abs(sp.y1 - sp.y0), stair = rise / StairSlope;
                if (stair > sp.len - 2) { sp.s0 = 1; sp.s1 = sp.len - 1; Debug.LogWarning($"{def.name}: span {spans.Count} is steep ({rise:0} m up over {sp.len:0} m)"); }
                else { sp.s0 = (sp.len - stair) / 2; sp.s1 = sp.s0 + stair; }
                spans.Add(sp);
            }
        }

        /// <summary>Level of the island a point stands on (the nearest one if none).</summary>
        float IsleLevelAt(Vector2 q)
        {
            float best = float.MinValue, near = float.MaxValue, nearLevel = 0;
            foreach (var i in isles)
            {
                float d = Vector2.Distance(q, i.c);
                if (d < i.r * 0.88f) best = Mathf.Max(best, i.level);
                if (d - i.r < near) { near = d - i.r; nearLevel = i.level; }
            }
            return best > float.MinValue ? best : nearLevel;
        }

        float IsleEdge(Isle i, float x, float z)
        {
            float a = Mathf.Atan2(z - i.c.y, x - i.c.x);
            return i.r * (0.9f + 0.2f * Mathf.PerlinNoise(Mathf.Cos(a) * 1.7f + i.seed, Mathf.Sin(a) * 1.7f + i.seed * 0.7f));
        }

        /// <summary>Island tops (flat, a little uneven) and a one-cell cliff rim round each; everything else stays at y.</summary>
        float IsleHeight(float x, float z, float y, ref byte tg)
        {
            var q = new Vector2(x, z);
            foreach (var i in isles)
            {
                float d = Vector2.Distance(q, i.c);
                if (d > i.r * 1.15f + cell * 1.2f) continue;
                float re = IsleEdge(i, x, z);
                if (d < re)
                {
                    float bump = (Mathf.PerlinNoise(x * 0.045f + i.seed, z * 0.045f) - 0.5f) * 1.3f * Mathf.SmoothStep(0, 1, (re - d) / 6f);
                    y = Mathf.Max(y, i.level + bump);
                }
                else if (d < re + cell * 1.1f) { y = Mathf.Max(y, i.level - IsleCliff); tg |= TagRidge; }
            }
            foreach (var sp in spans) if (sp.Inside(q, out _)) tg |= TagClear;
            return y;
        }

        void PrepareCity(MapFeature f)
        {
            Vector2 c = f.pts[0] * NS;
            float hx = f.width * NS, hz = f.height * NS, pitch = Mathf.Max(30, f.density), street = 12;
            int nx = Mathf.Max(1, Mathf.RoundToInt(hx * 2 / pitch)), nz = Mathf.Max(1, Mathf.RoundToInt(hz * 2 / pitch));
            float x0 = c.x - nx * pitch / 2, z0 = c.y - nz * pitch / 2;
            // streets are ordinary roads, in map units
            for (int i = 0; i <= nx; i++)
            {
                float x = (x0 + i * pitch) / NS;
                roads.Add(RoadLine(MapFeature.Road(street, x, z0 / NS, x, (z0 + nz * pitch) / NS)));
            }
            for (int j = 0; j <= nz; j++)
            {
                float z = (z0 + j * pitch) / NS;
                roads.Add(RoadLine(MapFeature.Road(street, x0 / NS, z, (x0 + nx * pitch) / NS, z)));
            }
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                    blocks.Add(new Block { c = new Vector2(x0 + (i + 0.5f) * pitch, z0 + (j + 0.5f) * pitch), size = pitch - street - 3 });
        }

        Line RoadLine(MapFeature f) { var l = MakeLine(f); l.sh = Profile(l, 20); return l; }

        // ── props ────────────────────────────────────────────────────────
        void BuildPlaces(System.Func<float, float, MeshKit> kitAt, List<(Vector2 c, float r)> placed)
        {
            if (isles.Count > 0) BuildIsles();
            foreach (var sp in spans) BuildSpan(sp);
            foreach (var b in blocks) CityBlock(b, kitAt, placed);
            if (blocks.Count > 0) { StreetFurniture(kitAt); CityStreets(); }
            if (def.features == null) return;
            foreach (var f in def.features) if (f.kind == FeatureKind.Tower) Tower(f.pts[0] * NS, f.height, kitAt, placed);
            foreach (var f in def.features)
                switch (f.kind)
                {
                    case FeatureKind.Vault: BuildVault(f.pts[0] * NS, f.height, kitAt, placed); break;
                    case FeatureKind.Wreck: WarshipWreck(f.pts[0] * NS, f.height, kitAt, placed); break;
                    case FeatureKind.Compound: Compound(f.pts[0] * NS, f.width * NS, Mathf.RoundToInt(f.density), Mathf.RoundToInt(f.height), kitAt, placed); break;
                }
        }

        /// <summary>After the towns: walls keep clear of every building.</summary>
        void BuildWalls(System.Func<float, float, MeshKit> kitAt, List<(Vector2 c, float r)> placed)
        {
            if (def.features == null) return;
            foreach (var f in def.features) if (f.kind == FeatureKind.Wall) Wall(f, kitAt, placed);
        }

        Color Shade(Color c, float k) { c *= k; c.a = 1; return c; }

        // ── floating isles ───────────────────────────────────────────────
        void BuildIsles()
        {
            var k = new MeshKit();
            Color rock = th.steep, dark = Shade(th.steep, 0.72f), soil = new Color(0.42f, 0.33f, 0.24f);
            foreach (var i in isles)
            {
                float top = i.level - IsleCliff + 0.6f;
                float h = i.r * 1.15f + 12;
                // the underside: a jagged cone of rock hanging under the island, a soil band where it meets the grass
                k.Cyl(new Vector3(i.c.x, top - h / 2, i.c.y), 1.2f, h, Quaternion.Euler(0, i.seed * 40, 0), rock, 11, i.r * 1.04f, false);
                k.Cyl(new Vector3(i.c.x, top + 0.2f, i.c.y), i.r * 1.04f, 1.2f, Quaternion.Euler(0, i.seed * 40, 0), soil, 11, i.r * 1.08f, false);
                int n = 5 + Mathf.RoundToInt(i.r / 6);
                for (int b = 0; b < n; b++)
                {
                    float a = R(0, Mathf.PI * 2), d = R(0.25f, 0.85f) * i.r;
                    float y = top - R(2, h * 0.55f) * (1 - d / i.r * 0.6f);
                    float s = R(3, 2 + i.r * 0.35f);
                    k.Rock(new Vector3(i.c.x + Mathf.Cos(a) * d, y, i.c.y + Mathf.Sin(a) * d), new Vector3(s * 1.3f, s * 1.6f, s * 1.2f), Quaternion.Euler(R(-20, 20), R(0, 360), R(-20, 20)), R() < 0.5f ? rock : dark, rng.Next(), 0.3f);
                }
                // roots and a few crystals hanging off the rim
                for (int b = 0; b < 4; b++)
                {
                    float a = R(0, Mathf.PI * 2);
                    float re = IsleEdge(i, i.c.x + Mathf.Cos(a) * i.r, i.c.y + Mathf.Sin(a) * i.r);
                    Vector3 p = new Vector3(i.c.x + Mathf.Cos(a) * re * 0.98f, i.level - IsleCliff - R(1, 4), i.c.y + Mathf.Sin(a) * re * 0.98f);
                    if (R() < 0.5f) k.Cone(p - Vector3.up * 2.5f, 0.8f, 5f, Quaternion.Euler(180, 0, 0), MeshKit.Glow(new Color(0.5f, 0.85f, 1f) * 0.7f), 5);
                    else k.Cyl(p - Vector3.up * 3, 0.25f, 6, Quaternion.Euler(R(-10, 10), 0, R(-10, 10)), new Color(0.3f, 0.24f, 0.17f), 4, 0.05f);
                }
            }
            // little rocks drifting round the edges and big islands far off, for the view (no colliders)
            for (int n = 0; n < 40; n++)
            {
                var i = isles[rng.Next(isles.Count)];
                float a = R(0, Mathf.PI * 2), d = i.r + R(8, 30), s = R(1.5f, 5);
                Vector3 p = new Vector3(i.c.x + Mathf.Cos(a) * d, i.level + R(-25, 10), i.c.y + Mathf.Sin(a) * d);
                k.Rock(p, new Vector3(s * 1.4f, s, s * 1.2f), Quaternion.Euler(R(0, 360), R(0, 360), 0), R() < 0.6f ? rock : dark, rng.Next(), 0.35f);
                k.Rock(p + Vector3.up * s * 0.45f, new Vector3(s * 1.2f, s * 0.25f, s), Quaternion.Euler(0, R(0, 360), 0), th.low, rng.Next(), 0.15f);
            }
            k.Make("isleUndersides", transform, true);
            var far = new MeshKit();
            for (int n = 0; n < 16; n++)
            {
                float a = n / 16f * Mathf.PI * 2 + R(-0.15f, 0.15f), d = R(330, 620), r = R(25, 70), y = R(-10, 70);
                Vector3 c = new Vector3(Mathf.Cos(a) * d, y, Mathf.Sin(a) * d);
                far.Cyl(c, r, 3, Quaternion.identity, th.mid, 10, r * 1.05f);
                far.Cyl(c - Vector3.up * (r * 0.6f + 1.5f), 2, r * 1.2f, Quaternion.Euler(0, R(0, 90), 0), rock, 9, r * 1.05f, false);
                for (int t = 0; t < 4; t++) { Vector3 tp = c + new Vector3(R(-r, r) * 0.6f, 1.5f, R(-r, r) * 0.6f); float th2 = R(8, 16); far.Cone(tp + Vector3.up * th2 / 2, th2 * 0.3f, th2, Quaternion.identity, new Color(0.2f, 0.4f, 0.2f), 6); }
            }
            far.Make("farIsles", transform, false);
        }

        /// <summary>A sea of cloud far below the islands, two layers so it has some depth.</summary>
        void BuildClouds()
        {
            for (int l = 0; l < 2; l++)
            {
                var k = new MeshKit();
                float y = VoidY + 22 - l * 9, s = 2200;
                Color c = l == 0 ? new Color(1, 1, 1, 0.55f) : new Color(0.9f, 0.93f, 1f, 0.9f);
                k.Quad(new Vector3(-s, y, -s), new Vector3(-s, y, s), new Vector3(s, y, s), new Vector3(s, y, -s), Color.white);
                var go = k.Make("clouds" + l, transform, false, Mats.Alpha(c));
                go.layer = Layers.Default;
            }
            // puffs drifting just under the islands
            var p = new MeshKit();
            for (int n = 0; n < 70; n++)
            {
                float x = R(-half, half), z = R(-half, half), s = R(18, 45);
                p.Sphere(new Vector3(x, VoidY + R(24, 40), z), new Vector3(s * 1.6f, s * 0.45f, s), Quaternion.Euler(0, R(0, 360), 0), Color.white, 8, 4);
            }
            p.Make("cloudPuffs", transform, false, Mats.Alpha(new Color(1, 1, 1, 0.45f))).layer = Layers.Default;
        }

        // ── spans (walkways with stairs) ─────────────────────────────────
        void BuildSpan(Span sp)
        {
            var k = new MeshKit();
            var col = new MeshKit();
            Vector2 n2 = new Vector2(-sp.u.y, sp.u.x);
            Vector3 U = new Vector3(sp.u.x, 0, sp.u.y), V = new Vector3(n2.x, 0, n2.y);
            Vector3 A = new Vector3(sp.a.x, 0, sp.a.y);
            Color stone = WallColor, dark = Shade(WallColor, 0.62f), trim = Shade(WallColor, 0.82f);
            const float thick = 0.8f;
            float hw = sp.w / 2;
            Vector3 P(float s, float side) => A + U * s + V * side + Vector3.up * sp.DeckY(s);
            // the walking surface (collider), three parts: flat, stair ramp, flat
            float[] cuts = { 0, sp.s0, sp.s1, sp.len };
            for (int i = 0; i < 3; i++)
            {
                float a0 = cuts[i], a1 = cuts[i + 1];
                if (a1 - a0 < 0.05f) continue;
                Vector3 l0 = P(a0, -hw), r0 = P(a0, hw), l1 = P(a1, -hw), r1 = P(a1, hw), dn = Vector3.down * thick;
                col.Quad(r0, r1, l1, l0, stone);
                col.Quad(l0 + dn, l1 + dn, r1 + dn, r0 + dn, stone);
                col.Quad(r0 + dn, r1 + dn, r1, r0, stone);
                col.Quad(l0, l1, l1 + dn, l0 + dn, stone);
                if (i != 1)
                {
                    // flat parts are drawn as they are
                    k.Quad(r0, r1, l1, l0, Color.Lerp(stone, th.path, 0.4f));
                    k.Quad(l0 + dn, l1 + dn, r1 + dn, r0 + dn, dark);
                    k.Quad(r0 + dn, r1 + dn, r1, r0, trim);
                    k.Quad(l0, l1, l1 + dn, l0 + dn, trim);
                }
            }
            // the stairs: steps on a sloping slab
            float rise = sp.y1 - sp.y0, run = sp.s1 - sp.s0;
            if (run > 0.5f && Mathf.Abs(rise) > 0.2f)
            {
                int steps = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(rise) / 0.3f));
                float tread = run / steps;
                var q = Quaternion.LookRotation(U);
                for (int s = 0; s < steps; s++)
                {
                    float sm = sp.s0 + (s + 0.5f) * tread;
                    // each step's top is the ramp height at its higher edge, so feet never sink below the tread
                    float yTop = Mathf.Max(sp.DeckY(sp.s0 + s * tread), sp.DeckY(sp.s0 + (s + 1) * tread));
                    float hgt = Mathf.Abs(rise) / steps + thick;
                    k.Box(A + U * sm + Vector3.up * (yTop - hgt / 2), new Vector3(sp.w, hgt, tread + 0.02f), q, s % 2 == 0 ? stone : trim);
                }
                Vector3 m0 = P(sp.s0, 0) + Vector3.down * (thick + 0.25f), m1 = P(sp.s1, 0) + Vector3.down * (thick + 0.25f);
                k.Box((m0 + m1) / 2, new Vector3(sp.w * 0.8f, 0.5f, (m1 - m0).magnitude), Quaternion.LookRotation(m1 - m0), dark);
            }
            // rails: posts and a top rail along both sides (the rails stop you falling, but bots path round them freely)
            for (int e = -1; e <= 1; e += 2)
            {
                for (int i = 0; i < 3; i++)
                {
                    float a0 = cuts[i], a1 = cuts[i + 1];
                    if (a1 - a0 < 0.05f) continue;
                    Vector3 b0 = P(a0, e * (hw - 0.15f)), b1 = P(a1, e * (hw - 0.15f));
                    Vector3 mid = (b0 + b1) / 2 + Vector3.up * 0.55f, dir = b1 - b0;
                    var rq = Quaternion.LookRotation(dir);
                    k.Box(mid + Vector3.up * 0.45f, new Vector3(0.22f, 0.16f, dir.magnitude), rq, trim);
                    k.Box(mid - Vector3.up * 0.1f, new Vector3(0.12f, 0.08f, dir.magnitude), rq, dark);
                    var rc = new GameObject("rail"); rc.transform.SetParent(colRoot, false); rc.transform.SetPositionAndRotation(mid, rq);
                    rc.AddComponent<BoxCollider>().size = new Vector3(0.3f, 1.1f, dir.magnitude); rc.layer = Layers.Ground;
                }
                for (float s = 0; s <= sp.len + 0.01f; s += 2.8f)
                {
                    Vector3 pp = P(Mathf.Min(s, sp.len), e * (hw - 0.15f));
                    k.Box(pp + Vector3.up * 0.5f, new Vector3(0.22f, 1.0f, 0.22f), Quaternion.LookRotation(U), dark);
                }
                // lamps at both ends
                for (int end = 0; end < 2; end++)
                {
                    Vector3 lp = P(end == 0 ? 0.6f : sp.len - 0.6f, e * (hw - 0.15f));
                    k.Box(lp + Vector3.up * 1.2f, new Vector3(0.3f, 2.4f, 0.3f), Quaternion.identity, dark);
                    k.Sphere(lp + Vector3.up * 2.55f, Vector3.one * 0.55f, Quaternion.identity, MeshKit.Glow(new Color(1f, 0.85f, 0.5f) * 0.9f), 6, 4);
                }
            }
            k.Make("span", transform, true);
            var go = new GameObject("spanFloor");
            go.transform.SetParent(transform, false);
            go.layer = Layers.Ground;
            go.AddComponent<MeshCollider>().sharedMesh = col.Build("spanFloor");
        }

        // ── city ─────────────────────────────────────────────────────────
        void CityBlock(Block b, System.Func<float, float, MeshKit> kitAt, List<(Vector2 c, float r)> placed)
        {
            if (NearPost(b.c.x, b.c.y, b.size * 0.5f + 20)) { Plaza(b, kitAt); return; }
            float roll = R();
            if (def.royale && roll < 0.42f && R() < 0.6f) OpenTower(b.c, Mathf.Clamp(b.size * 0.6f, 14, 18), 3 + rng.Next(3), kitAt, placed);
            else if (roll < 0.3f) CityTower(b.c, b.size * R(0.55f, 0.8f), b.size * R(0.55f, 0.8f), R(26, 62), kitAt, placed);
            else if (roll < 0.58f)
            {
                // a cluster of four buildings, some of them enterable
                float q = b.size / 4;
                for (int i = 0; i < 4; i++)
                {
                    Vector2 c = b.c + new Vector2(i % 2 == 0 ? -q : q, i < 2 ? -q : q);
                    float w = b.size / 2 - R(3, 6), d = b.size / 2 - R(3, 6);
                    if (R() < (def.royale ? 0.72f : 0.45f)) PlaceHouse(c, w * 0.75f, d * 0.75f, true, kitAt, placed);
                    else CityTower(c, w, d, R(9, 20), kitAt, placed);
                }
            }
            else if (roll < 0.72f) Park(b, kitAt);
            else if (roll < 0.87f) Rubble(b, kitAt);
            else
            {
                float q = b.size / 4;
                PlaceHouse(b.c + new Vector2(-q, 0), b.size / 2 - 5, b.size * 0.42f, true, kitAt, placed);
                PlaceHouse(b.c + new Vector2(q, 0), b.size / 2 - 5, b.size * 0.42f, true, kitAt, placed);
            }
        }

        void PlaceHouse(Vector2 c, float w, float d, bool shell, System.Func<float, float, MeshKit> kitAt, List<(Vector2 c, float r)> placed)
        {
            w = Mathf.Clamp(w, 7, 14); d = Mathf.Clamp(d, 6, 12);
            float hi = float.MinValue, lo = float.MaxValue;
            for (int i = 0; i < 4; i++) { float hh = Height(c.x + (i % 2 == 0 ? -w : w) / 2, c.y + (i < 2 ? -d : d) / 2); hi = Mathf.Max(hi, hh); lo = Mathf.Min(lo, hh); }
            if (!PropSpot(c.x, c.y, 26)) return;
            Vector2 rd = NearestRoadDir(c, 40, out _);
            var rot = Quaternion.Euler(0, rd != Vector2.zero ? Mathf.Atan2(rd.x, rd.y) * Mathf.Rad2Deg : 0, 0);
            int door = 0;
            {
                Vector2 toRoad = Vector2.zero; float best = float.MaxValue;
                foreach (var l in roads) { float dd = l.Dist(c, out float s); if (dd < best) { best = dd; toRoad = l.At(s) - c; } }
                Vector3 local = Quaternion.Inverse(rot) * new Vector3(toRoad.x, 0, toRoad.y);
                door = Mathf.Abs(local.x) > Mathf.Abs(local.z) ? (local.x > 0 ? 1 : 3) : (local.z > 0 ? 0 : 2);
            }
            placed.Add((c, Mathf.Sqrt(w * w + d * d) / 2));
            var k = kitAt(c.x, c.y);
            if (shell && def.royale && w >= 9.5f && R() < 0.4f) TwoStory(k, new Vector3(c.x, hi, c.y), rot, w, d, lo, door);
            else if (shell) HouseShell(k, new Vector3(c.x, hi, c.y), rot, w, d, lo, door);
            else HouseSolid(k, new Vector3(c.x, hi, c.y), rot, w, d, lo);
        }

        void CityTower(Vector2 c, float w, float d, float hgt, System.Func<float, float, MeshKit> kitAt, List<(Vector2 c, float r)> placed)
        {
            if (!PropSpot(c.x, c.y, 26)) return;
            float lo = float.MaxValue;
            for (int i = 0; i < 4; i++) lo = Mathf.Min(lo, Height(c.x + (i % 2 == 0 ? -w : w) / 2, c.y + (i < 2 ? -d : d) / 2));
            placed.Add((c, Mathf.Sqrt(w * w + d * d) / 2));
            var k = kitAt(c.x, c.y);
            Vector3 b = new Vector3(c.x, lo - 1, c.y);
            var rot = Quaternion.Euler(0, R() < 0.5f ? 0 : 90, 0);
            Color wall = Color.Lerp(new Color(0.55f, 0.55f, 0.58f), R() < 0.5f ? new Color(0.62f, 0.5f, 0.42f) : new Color(0.38f, 0.44f, 0.52f), R(0.2f, 0.7f));
            Color glass = Color.Lerp(new Color(0.12f, 0.16f, 0.22f), new Color(0.2f, 0.3f, 0.4f), R());
            Color lit = MeshKit.Glow(new Color(1f, 0.82f, 0.5f) * 0.55f);
            k.Box(b + Vector3.up * (hgt / 2 + 0.5f), new Vector3(w, hgt + 1, d), rot, wall);
            Col(b + Vector3.up * (hgt / 2 + 0.5f), new Vector3(w, hgt + 1, d), rot);
            // window bands on every floor, a few lit
            int floors = Mathf.FloorToInt(hgt / 3.4f);
            for (int f = 1; f < floors; f++)
            {
                float y = b.y + 1 + f * 3.4f - 1.3f;
                for (int s = 0; s < 4; s++)
                {
                    bool alongX = s % 2 == 0; float len = (alongX ? w : d) - 1.2f, off = (alongX ? d : w) / 2 + 0.04f;
                    Vector3 nrm = s == 0 ? Vector3.forward : s == 1 ? Vector3.right : s == 2 ? Vector3.back : Vector3.left;
                    Vector3 pc = b + rot * (nrm * off); pc.y = y;
                    var wq = rot * Quaternion.LookRotation(nrm);
                    k.Box(pc, new Vector3(len, 1.5f, 0.08f), wq, R() < 0.12f ? lit : glass);
                }
            }
            // ground-floor band, parapet, rooftop plant and an antenna on the tall ones
            Vector3 top = b + Vector3.up * (hgt + 1);
            k.Box(b + Vector3.up * 1.6f, new Vector3(w + 0.3f, 3, d + 0.3f), rot, Shade(wall, 0.7f));
            k.Box(top + Vector3.up * 0.5f, new Vector3(w + 0.2f, 1f, d + 0.2f), rot, Shade(wall, 0.8f));
            k.Box(top + Vector3.up * 0.6f, new Vector3(w - 1.2f, 1.1f, d - 1.2f), rot, Shade(wall, 0.9f));
            k.Box(top + rot * new Vector3(w * 0.2f, 1.6f, d * 0.15f), new Vector3(3, 2.2f, 2.4f), rot, new Color(0.45f, 0.46f, 0.48f));
            if (hgt > 35)
            {
                k.Cyl(top + Vector3.up * 7, 0.18f, 14, Quaternion.identity, new Color(0.3f, 0.3f, 0.32f), 5);
                k.Sphere(top + Vector3.up * 14.2f, Vector3.one * 0.6f, Quaternion.identity, MeshKit.Glow(new Color(1f, 0.2f, 0.15f)), 6, 4);
            }
        }

        void Plaza(Block b, System.Func<float, float, MeshKit> kitAt)
        {
            // planters and benches round the edge; the middle stays open for the command post
            var k = kitAt(b.c.x, b.c.y);
            float e = b.size / 2 - 2;
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2 + 0.39f;
                Vector2 q = b.c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * e * 1.05f;
                if (NearPost(q.x, q.y, 17) || !Clear(q.x, q.y)) continue;
                Vector3 p = new Vector3(q.x, Height(q.x, q.y), q.y);
                var rq = Quaternion.Euler(0, -a * Mathf.Rad2Deg, 0);
                k.Box(p + Vector3.up * 0.45f, new Vector3(3.2f, 0.9f, 1.6f), rq, new Color(0.6f, 0.58f, 0.55f));
                k.Sphere(p + Vector3.up * 1.4f, new Vector3(2.6f, 1.4f, 1.3f), rq, new Color(0.25f, 0.42f, 0.2f), 6, 4);
                Col(p + Vector3.up * 0.45f, new Vector3(3.2f, 0.9f, 1.6f), rq);
            }
        }

        /// <summary>
        /// Asphalt laid over the city streets (the ground mesh is too coarse to paint them: its 2.5 m triangles gave
        /// the kerbs saw teeth), with a dashed centre line. A street that crosses one already laid stops at it, so
        /// crossings are one surface and never flicker.
        /// </summary>
        void CityStreets()
        {
            var k = new MeshKit();
            Color asphalt = new Color(0.2f, 0.2f, 0.22f), kerb = new Color(0.62f, 0.6f, 0.57f), paint = new Color(0.85f, 0.82f, 0.62f);
            var laid = new List<Line>();
            foreach (var l in roads)
            {
                if (l.w < 11) continue;
                float hw = l.w / 2;
                bool Crossing(Vector2 q) { foreach (var o in laid) if (o.Dist(q, out _) < o.w / 2 + 2f) return true; return false; }
                bool AnyCrossing(Vector2 q) { foreach (var o in roads) if (o != l && o.w >= 11 && o.Dist(q, out _) < o.w / 2 + 1.6f) return true; return false; }
                const float step = 3f;
                for (float s = 0; s < l.len - 0.01f; s += step)
                {
                    float s1 = Mathf.Min(l.len, s + step);
                    Vector2 a = l.At(s), b = l.At(s1), d = l.DirAt(s), n = new Vector2(-d.y, d.x);
                    if (Crossing((a + b) / 2)) continue;
                    Vector3 P(Vector2 q, float side, float lift) { Vector2 p = q + n * side; return new Vector3(p.x, Height(p.x, p.y) + lift, p.y); }
                    k.Quad(P(a, hw, 0.06f), P(b, hw, 0.06f), P(b, -hw, 0.06f), P(a, -hw, 0.06f), asphalt);
                    // kerbs: a raised strip each side, open where another street crosses
                    for (int e = -1; e <= 1; e += 2)
                    {
                        if (AnyCrossing((a + b) / 2 + n * e * (hw + 0.7f))) continue;
                        float o0 = e * hw, o1 = e * (hw + 1.4f);
                        k.Quad(P(a, e > 0 ? o1 : o0, 0.18f), P(b, e > 0 ? o1 : o0, 0.18f), P(b, e > 0 ? o0 : o1, 0.18f), P(a, e > 0 ? o0 : o1, 0.18f), kerb);
                        k.Quad(P(a, o0, 0.06f), P(b, o0, 0.06f), P(b, o0, 0.18f), P(a, o0, 0.18f), Shade(kerb, 0.7f));
                        k.Quad(P(a, o0, 0.18f), P(b, o0, 0.18f), P(b, o0, 0.06f), P(a, o0, 0.06f), Shade(kerb, 0.7f));
                    }
                    // dashed centre line
                    if (((int)(s / step)) % 2 == 0)
                    {
                        Vector2 a2 = l.At(s + 0.3f), b2 = l.At(s1 - 0.3f);
                        k.Quad(P(a2, 0.15f, 0.08f), P(b2, 0.15f, 0.08f), P(b2, -0.15f, 0.08f), P(a2, -0.15f, 0.08f), paint);
                    }
                }
                laid.Add(l);
            }
            k.Make("streets", transform, false).GetComponent<MeshRenderer>().receiveShadows = true;
        }

        void Park(Block b, System.Func<float, float, MeshKit> kitAt)
        {
            // a lawn over the concrete
            {
                var g = new MeshKit();
                float le = b.size / 2 - 0.5f, st = 4;
                for (float x = -le; x < le - 0.01f; x += st)
                    for (float z = -le; z < le - 0.01f; z += st)
                    {
                        float x1 = Mathf.Min(le, x + st), z1 = Mathf.Min(le, z + st);
                        Vector3 Q(float px, float pz) { float wx = b.c.x + px, wz = b.c.y + pz; return new Vector3(wx, Height(wx, wz) + 0.05f, wz); }
                        g.Quad(Q(x, z), Q(x, z1), Q(x1, z1), Q(x1, z), Color.Lerp(new Color(0.3f, 0.45f, 0.22f), new Color(0.36f, 0.5f, 0.25f), Mathf.PerlinNoise((b.c.x + x) * 0.1f, (b.c.y + z) * 0.1f)));
                    }
                g.Make("lawn", transform, false).GetComponent<MeshRenderer>().receiveShadows = true;
            }
            int trees = 7 + rng.Next(5);
            for (int i = 0; i < trees; i++)
            {
                float x = b.c.x + R(-0.42f, 0.42f) * b.size, z = b.c.y + R(-0.42f, 0.42f) * b.size;
                if (!PropSpot(x, z, 26)) continue;
                var k = kitAt(x, z);
                Vector3 p = new Vector3(x, Height(x, z), z);
                float hh = R(7, 12);
                k.Cyl(p + Vector3.up * hh * 0.3f, 0.35f, hh * 0.6f, Quaternion.identity, new Color(0.34f, 0.25f, 0.18f), 6);
                k.Sphere(p + Vector3.up * hh * 0.75f, new Vector3(hh * 0.55f, hh * 0.5f, hh * 0.55f), Quaternion.Euler(0, R(0, 90), 0), Color.Lerp(new Color(0.2f, 0.4f, 0.18f), new Color(0.35f, 0.5f, 0.22f), R()), 7, 4);
                ColCap(p, 0.4f, hh * 0.6f);
            }
            // hedges round it, with gaps at the middle of each side
            var hk = kitAt(b.c.x, b.c.y);
            float e = b.size / 2;
            for (int s = 0; s < 4; s++)
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 nrm = s == 0 ? Vector3.forward : s == 1 ? Vector3.right : s == 2 ? Vector3.back : Vector3.left;
                    Vector3 along = new Vector3(nrm.z, 0, -nrm.x);
                    Vector3 c = new Vector3(b.c.x, 0, b.c.y) + nrm * e + along * side * e * 0.55f;
                    c.y = Height(c.x, c.z);
                    var q = Quaternion.LookRotation(nrm);
                    hk.Box(c + Vector3.up * 0.6f, new Vector3(e * 0.8f, 1.2f, 1.1f), q, new Color(0.22f, 0.38f, 0.18f));
                    Col(c + Vector3.up * 0.6f, new Vector3(e * 0.8f, 1.2f, 1.1f), q);
                }
        }

        void Rubble(Block b, System.Func<float, float, MeshKit> kitAt)
        {
            var k = kitAt(b.c.x, b.c.y);
            Color con = new Color(0.55f, 0.54f, 0.52f), dark = new Color(0.36f, 0.35f, 0.34f);
            // a gutted building: broken walls of uneven height
            float w = b.size * 0.55f;
            for (int s = 0; s < 4; s++)
            {
                Vector3 nrm = s == 0 ? Vector3.forward : s == 1 ? Vector3.right : s == 2 ? Vector3.back : Vector3.left;
                Vector3 along = new Vector3(nrm.z, 0, -nrm.x);
                for (int piece = -1; piece <= 1; piece++)
                {
                    if (R() < 0.35f) continue;
                    Vector3 c = new Vector3(b.c.x, 0, b.c.y) + nrm * w / 2 + along * piece * w / 3;
                    c.y = Height(c.x, c.z);
                    float hh = R(1.2f, 6.5f);
                    var q = Quaternion.LookRotation(nrm);
                    k.Box(c + Vector3.up * hh / 2, new Vector3(w / 3 - 0.6f, hh, 0.6f), q, R() < 0.5f ? con : dark);
                    Col(c + Vector3.up * hh / 2, new Vector3(w / 3 - 0.6f, hh, 0.6f), q);
                }
            }
            for (int i = 0; i < 9; i++)
            {
                float x = b.c.x + R(-0.45f, 0.45f) * b.size, z = b.c.y + R(-0.45f, 0.45f) * b.size;
                if (!PropSpot(x, z, 24)) continue;
                Vector3 p = new Vector3(x, Height(x, z), z);
                if (R() < 0.5f) { float s = R(1.2f, 3f); k.Rock(p + Vector3.up * s * 0.25f, new Vector3(s * 1.5f, s * 0.7f, s * 1.2f), Quaternion.Euler(0, R(0, 360), 0), R() < 0.5f ? con : dark, rng.Next(), 0.35f); ColSphere(p + Vector3.up * s * 0.2f, s * 0.5f); }
                else { var q = Quaternion.Euler(R(-25, 25), R(0, 360), R(-10, 10)); k.Box(p + Vector3.up * 0.6f, new Vector3(R(3, 5), 0.4f, R(2, 3)), q, con); Col(p + Vector3.up * 0.6f, new Vector3(4, 0.5f, 2.5f), q); }
            }
        }

        /// <summary>Lamps along the city streets, burnt-out cars and concrete barriers for cover.</summary>
        void StreetFurniture(System.Func<float, float, MeshKit> kitAt)
        {
            foreach (var l in roads)
            {
                if (l.w < 11) continue;
                for (float s = 9; s < l.len - 9; s += 18)
                {
                    Vector2 c = l.At(s), dir = l.DirAt(s), side = new Vector2(-dir.y, dir.x);
                    if (NearPost(c.x, c.y, 24)) continue;
                    var k = kitAt(c.x, c.y);
                    var q = Quaternion.LookRotation(new Vector3(dir.x, 0, dir.y));
                    // lamp on the kerb (no collider: it would block a lane for the bots)
                    int e = (int)(s / 18) % 2 == 0 ? 1 : -1;
                    Vector2 lp = c + side * e * (l.w / 2 + 0.6f);
                    Vector3 lp3 = new Vector3(lp.x, Height(lp.x, lp.y), lp.y);
                    k.Cyl(lp3 + Vector3.up * 3.5f, 0.12f, 7, Quaternion.identity, new Color(0.25f, 0.26f, 0.28f), 5);
                    k.Box(lp3 + Vector3.up * 7 - new Vector3(side.x, 0, side.y) * e * 0.9f, new Vector3(0.5f, 0.2f, 0.5f), q, MeshKit.Glow(new Color(1f, 0.85f, 0.55f) * 0.8f));
                    float roll = R();
                    if (roll < 0.13f)
                    {
                        // a burnt-out car against one kerb
                        Vector2 cp = c + side * (R() < 0.5f ? -1 : 1) * (l.w / 2 - 2.2f) + dir * R(-4, 4);
                        Vector3 p = new Vector3(cp.x, Height(cp.x, cp.y), cp.y);
                        var cq = q * Quaternion.Euler(0, R(-20, 20), 0);
                        Color body = Color.Lerp(new Color(0.3f, 0.2f, 0.16f), new Color(0.25f, 0.27f, 0.3f), R());
                        k.Box(p + Vector3.up * 0.7f, new Vector3(1.9f, 0.9f, 4.3f), cq, body);
                        k.Box(p + Vector3.up * 1.4f + cq * new Vector3(0, 0, -0.3f), new Vector3(1.7f, 0.65f, 2.2f), cq, Shade(body, 0.6f));
                        Col(p + Vector3.up * 0.9f, new Vector3(1.9f, 1.8f, 4.3f), cq);
                    }
                    else if (roll < 0.22f)
                    {
                        // a barrier across part of the street (always a lane open)
                        Vector2 bp = c + side * (R() < 0.5f ? -1 : 1) * (l.w / 4);
                        Vector3 p = new Vector3(bp.x, Height(bp.x, bp.y), bp.y);
                        var bq = Quaternion.LookRotation(new Vector3(side.x, 0, side.y));
                        k.Taper(p + Vector3.up * 0.5f, new Vector2(4f, 0.8f), new Vector2(4f, 0.3f), 1f, bq, new Color(0.7f, 0.68f, 0.64f));
                        Col(p + Vector3.up * 0.5f, new Vector3(4f, 1f, 0.7f), bq);
                    }
                }
            }
        }

        // ── walls and towers ─────────────────────────────────────────────
        void Wall(MapFeature f, System.Func<float, float, MeshKit> kitAt, List<(Vector2 c, float r)> placed)
        {
            var l = MakeLine(f);
            float h = f.height;
            At(l.p[0].x, l.p[0].y);
            bool hedge = here == Theme.Forest;
            for (float s = 0; s < l.len;)
            {
                float len = R(4, 8), gap = R(1.8f, 3.2f);
                float sm = s + len / 2;
                s += len + gap;
                if (sm > l.len) break;
                Vector2 c = l.At(sm), d = l.DirAt(sm);
                if (!PropSpot(c.x, c.y, 24) || !PropSpot(c.x + d.x * len / 2, c.y + d.y * len / 2, 24) || !PropSpot(c.x - d.x * len / 2, c.y - d.y * len / 2, 24)) continue;
                bool hit = false;
                foreach (var b in placed) if (Vector2.Distance(b.c, c) < b.r + len / 2 + 1.5f) { hit = true; break; }
                if (hit) continue;
                float y = Mathf.Min(Height(c.x - d.x * len / 2, c.y - d.y * len / 2), Height(c.x + d.x * len / 2, c.y + d.y * len / 2));
                Vector3 p = new Vector3(c.x, y, c.y);
                var q = Quaternion.LookRotation(new Vector3(d.x, 0, d.y));
                var k = kitAt(c.x, c.y);
                bool green = hedge && R() < 0.5f;
                Color col = green ? Color.Lerp(new Color(0.18f, 0.34f, 0.15f), new Color(0.28f, 0.42f, 0.2f), R())
                          : here == Theme.Urban ? new Color(0.68f, 0.66f, 0.62f) : here == Theme.Sky ? Shade(WallColor, 1.05f) : Color.Lerp(new Color(0.52f, 0.5f, 0.46f), new Color(0.6f, 0.56f, 0.5f), R());
                float hh = green ? h + 0.3f : h;
                if (green) k.Rock(p + Vector3.up * hh / 2, new Vector3(1.3f, hh + 0.4f, len), q, col, rng.Next(), 0.12f);
                else { k.Box(p + Vector3.up * (hh / 2 - 0.3f), new Vector3(0.7f, hh + 0.6f, len), q, col); k.Box(p + Vector3.up * (hh + 0.05f), new Vector3(0.85f, 0.15f, len + 0.1f), q, Shade(col, 0.8f)); }
                Col(p + Vector3.up * (hh / 2 - 0.3f), new Vector3(0.8f, hh + 0.6f, len), q);
            }
        }

        void Tower(Vector2 c, float hgt, System.Func<float, float, MeshKit> kitAt, List<(Vector2 c, float r)> placed)
        {
            At(c.x, c.y);
            float w = here == Theme.Sky ? 4 : 7;
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < 4; i++) { float hh = Height(c.x + (i % 2 == 0 ? -w : w) / 2, c.y + (i < 2 ? -w : w) / 2); lo = Mathf.Min(lo, hh); hi = Mathf.Max(hi, hh); }
            placed.Add((c, w));
            var k = kitAt(c.x, c.y);
            Vector3 b = new Vector3(c.x, lo - 1, c.y);
            float H = hgt + (hi - lo) + 1;
            Color wall = WallColor, dark = Shade(WallColor, 0.65f);
            var q = Quaternion.Euler(0, R(0, 90), 0);
            switch (here)
            {
                case Theme.Sky:
                    // a white spire with a crystal floating above it
                    k.Taper(b + Vector3.up * H / 2, new Vector2(w, w), new Vector2(w * 0.45f, w * 0.45f), H, q, wall);
                    k.Cone(b + Vector3.up * (H + 2), w * 0.3f, 4, q, dark, 4);
                    k.Cone(b + Vector3.up * (H + 6.5f), 1.4f, 2.2f, q, MeshKit.Glow(new Color(0.5f, 0.9f, 1f)), 6);
                    k.Cone(b + Vector3.up * (H + 4.6f), 1.4f, 1.6f, q * Quaternion.Euler(180, 0, 0), MeshKit.Glow(new Color(0.5f, 0.9f, 1f)), 6);
                    Col(b + Vector3.up * H / 2, new Vector3(w * 0.8f, H, w * 0.8f), q);
                    break;
                case Theme.Urban:
                    k.Box(b + Vector3.up * H / 2, new Vector3(w, H, w), q, new Color(0.5f, 0.5f, 0.52f));
                    k.Cyl(b + Vector3.up * (H + 9), 0.3f, 18, Quaternion.identity, new Color(0.3f, 0.3f, 0.32f), 5);
                    k.Sphere(b + Vector3.up * (H + 18.2f), Vector3.one * 0.9f, Quaternion.identity, MeshKit.Glow(new Color(1f, 0.2f, 0.15f)), 6, 4);
                    Col(b + Vector3.up * H / 2, new Vector3(w, H, w), q);
                    break;
                default:
                    // a stone clock tower with a pointed roof
                    k.Box(b + Vector3.up * H / 2, new Vector3(w, H, w), q, wall);
                    k.Box(b + Vector3.up * (H - 3), new Vector3(w + 0.6f, 4, w + 0.6f), q, Shade(wall, 0.9f));
                    for (int s = 0; s < 4; s++)
                    {
                        var fq = q * Quaternion.Euler(0, s * 90, 0);
                        k.Cyl(b + Vector3.up * (H - 3) + fq * new Vector3(0, 0, w / 2 + 0.35f), 1.3f, 0.12f, fq * Quaternion.Euler(90, 0, 0), new Color(0.95f, 0.92f, 0.8f), 12);
                        k.Box(b + Vector3.up * (H - 2.6f) + fq * new Vector3(0, 0, w / 2 + 0.43f), new Vector3(0.12f, 0.9f, 0.05f), fq, new Color(0.1f, 0.1f, 0.1f));
                    }
                    k.Taper(b + Vector3.up * (H + 3.5f), new Vector2(w + 1, w + 1), new Vector2(0.3f, 0.3f), 7, q, here == Theme.Snow ? new Color(0.9f, 0.92f, 0.96f) : new Color(0.35f, 0.2f, 0.15f));
                    k.Box(b + Vector3.up * 1.4f + q * new Vector3(0, 0, w / 2 + 0.05f), new Vector3(1.6f, 2.8f, 0.12f), q, dark);
                    Col(b + Vector3.up * H / 2, new Vector3(w, H, w), q);
                    break;
            }
        }
    }
}
