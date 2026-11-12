using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    /// <summary>
    /// Hand-made trees, bushes, ferns and rocks (Quaternius's CC0 Stylized Nature MegaKit, Resources/Models/Nature) in
    /// place of the cone-and-ball trees and lumpy-ball rocks. Nothing becomes its own GameObject: a model is written into
    /// the map's chunk meshes like every other prop.
    ///   - trunks, branches and rocks go into the ordinary vertex-colour kit: their texture is read once per vertex and
    ///     kept as light-and-dark variation of the tint the map asks for (so a desert rock is still desert coloured);
    ///   - leaves go into a second kit drawn with BIS/LitCutout from one leaf atlas (Textures/leaves_atlas, 2 x 2
    ///     sheets); their vertex colour is the tint and vertex alpha is how much the vertex sways in the wind.
    /// Low graphics keeps the code-built shapes (<see cref="On"/>); Medium simplifies the models further than High.
    /// Sizes and colliders never depend on the graphics setting, so two devices in one Wi-Fi game have the same map.
    /// </summary>
    public static class NatureModels
    {
        public static bool On => Settings.Quality > 0;

        public static readonly string[] CommonTrees = { "CommonTree_3", "CommonTree_4", "CommonTree_2" };
        public static readonly string[] Pines = { "Pine_5", "Pine_4", "Pine_2" };
        public static readonly string[] Rocks = { "Rock_Medium_1", "Rock_Medium_2", "Rock_Medium_3" };
        /// <summary>Trunk radius as a share of the tree's height (measured on the models; used for colliders at every graphics level).</summary>
        public static readonly float[] CommonTrunk = { 0.04f, 0.04f, 0.055f }, PineTrunk = { 0.03f, 0.03f, 0.03f };
        public static float Trunk(float[] set, int variant) => set[Mathf.Abs(variant) % set.Length];
        // The kit's trees are 3,000 - 6,000 triangles each, and a forest map has hundreds: far too much for a phone when
        // they are all merged into the map. Each model is simplified once when first used - trunk and branches down to
        // about SolidBudget triangles, leaf cards thinned to about LeafBudget (the ones kept are drawn larger).
        static int SolidBudget => Settings.Quality >= 2 ? 520 : 340;
        static int LeafBudget => Settings.Quality >= 2 ? 640 : 400;

        /// <summary>The tree drawn for a variant number.</summary>
        public static string Tree(string[] set, int variant) => set[Mathf.Abs(variant) % set.Length];

        class Raw
        {
            public Vector3[] sv, sn; public float[] sl; public int[] st;                        // solid part: position, normal, light/dark factor
            public Vector3[] lv, ln; public Vector2[] luv; public float[] lw; public int[] lt;   // leaves: + atlas uv, sway weight
            public Vector3 min, size;
        }
        static readonly Dictionary<string, Raw> raws = new Dictionary<string, Raw>();
        static Material leafMat;

        public static Material LeafMat
        {
            get
            {
                if (leafMat) return leafMat;
                var sh = Resources.Load<Shader>("Shaders/BIS_LitCutout");
                leafMat = new Material(sh ? sh : Mats.LitShader) { name = "BIS_Leaves", mainTexture = Resources.Load<Texture2D>("Models/Nature/Textures/leaves_atlas") };
                return leafMat;
            }
        }

        /// <summary>Which quarter of the leaf atlas a material's sheet is in (x, y = 0 or 1), or false for a solid material.</summary>
        static bool LeafQuad(string mat, out Vector2 q)
        {
            q = Vector2.zero;
            if (mat.StartsWith("Leaves_NormalTree")) { q = new Vector2(0, 0); return true; }
            if (mat.StartsWith("Leaves_Pine")) { q = new Vector2(1, 0); return true; }
            if (mat.StartsWith("Leaves_TwistedTree")) { q = new Vector2(0, 1); return true; }
            if (mat.StartsWith("Leaves")) { q = new Vector2(1, 1); return true; }
            return false;
        }

        /// <summary>Merges the vertices that fall in the same cell of a grid (averaging them) and drops the triangles that
        /// collapse; the grid is coarsened until the triangle count is inside the budget.</summary>
        static void Simplify(List<Vector3> v, List<Vector3> n, List<float> l, List<int> t, int budget, float height)
        {
            if (t.Count / 3 <= budget) return;
            var srcV = v.ToArray(); var srcN = n.ToArray(); var srcL = l.ToArray(); var srcT = t.ToArray();
            for (float cell = height / 44f; cell < height; cell *= 1.22f)
            {
                var cellOf = new Dictionary<Vector3Int, int>();
                var remap = new int[srcV.Length];
                var sumV = new List<Vector3>(); var sumN = new List<Vector3>(); var sumL = new List<float>(); var cnt = new List<int>();
                for (int i = 0; i < srcV.Length; i++)
                {
                    var key = new Vector3Int(Mathf.FloorToInt(srcV[i].x / cell), Mathf.FloorToInt(srcV[i].y / cell), Mathf.FloorToInt(srcV[i].z / cell));
                    if (!cellOf.TryGetValue(key, out int ci)) { ci = sumV.Count; cellOf[key] = ci; sumV.Add(Vector3.zero); sumN.Add(Vector3.zero); sumL.Add(0); cnt.Add(0); }
                    sumV[ci] += srcV[i]; sumN[ci] += srcN[i]; sumL[ci] += srcL[i]; cnt[ci]++;
                    remap[i] = ci;
                }
                var nt = new List<int>();
                for (int i = 0; i + 2 < srcT.Length; i += 3)
                {
                    int a = remap[srcT[i]], b = remap[srcT[i + 1]], c = remap[srcT[i + 2]];
                    if (a != b && b != c && a != c) { nt.Add(a); nt.Add(b); nt.Add(c); }
                }
                if (nt.Count / 3 > budget) continue;
                v.Clear(); n.Clear(); l.Clear(); t.Clear();
                for (int i = 0; i < sumV.Count; i++) { v.Add(sumV[i] / cnt[i]); n.Add(sumN[i].sqrMagnitude > 1e-6f ? sumN[i].normalized : Vector3.up); l.Add(sumL[i] / cnt[i]); }
                t.AddRange(nt);
                return;
            }
        }

        /// <summary>Keeps about <paramref name="budget"/> triangles of leaf cards: whole cards (connected pieces) are dropped
        /// evenly through the crown and the ones kept grow about their own middle to fill the gaps.</summary>
        static void Thin(List<Vector3> v, List<int> t, int budget)
        {
            int tris = t.Count / 3;
            if (tris <= budget) return;
            var parent = new int[v.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            System.Func<int, int> find = null;
            find = x => { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };
            for (int i = 0; i + 2 < t.Count; i += 3) { int a = find(t[i]); int b = find(t[i + 1]); int c = find(t[i + 2]); parent[b] = a; parent[find(c)] = a; }
            var order = new Dictionary<int, int>();
            for (int i = 0; i < parent.Length; i++) { int r = find(i); if (!order.ContainsKey(r)) order[r] = order.Count; }
            if (order.Count < 4) return;   // one welded piece: nothing to drop
            float keep = (float)budget / tris, grow = Mathf.Min(1.7f, 1f / Mathf.Sqrt(keep));
            var kept = new HashSet<int>();
            foreach (var kv in order) { float f = kv.Value * 0.6180339f; if (f - Mathf.Floor(f) < keep) kept.Add(kv.Key); }
            var mid = new Dictionary<int, Vector3>(); var num = new Dictionary<int, int>();
            for (int i = 0; i < parent.Length; i++) { int r = find(i); if (!kept.Contains(r)) continue; mid.TryGetValue(r, out var s); mid[r] = s + v[i]; num.TryGetValue(r, out int c); num[r] = c + 1; }
            for (int i = 0; i < parent.Length; i++) { int r = find(i); if (kept.Contains(r)) { Vector3 m = mid[r] / num[r]; v[i] = m + (v[i] - m) * grow; } }
            var nt = new List<int>();
            for (int i = 0; i + 2 < t.Count; i += 3) if (kept.Contains(find(t[i]))) { nt.Add(t[i]); nt.Add(t[i + 1]); nt.Add(t[i + 2]); }
            t.Clear(); t.AddRange(nt);
        }

        static Raw Load(string name)
        {
            string cacheKey = name + Settings.Quality;
            if (raws.TryGetValue(cacheKey, out var hit)) return hit;
            Raw raw = null;
            var go = Resources.Load<GameObject>("Models/Nature/" + name);
            if (go)
            {
                var sv = new List<Vector3>(); var sn = new List<Vector3>(); var sl = new List<float>(); var st = new List<int>();
                var lv = new List<Vector3>(); var ln = new List<Vector3>(); var luv = new List<Vector2>(); var lt = new List<int>();
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    var m = mf.sharedMesh;
                    if (!m || !m.isReadable) continue;
                    var mr = mf.GetComponent<MeshRenderer>();
                    var toRoot = mf.transform.localToWorldMatrix;
                    var mv = m.vertices; var mn = m.normals; var mu = m.uv;
                    for (int s = 0; s < m.subMeshCount; s++)
                    {
                        string mat = mr && mr.sharedMaterials.Length > s && mr.sharedMaterials[s] ? mr.sharedMaterials[s].name : "";
                        bool leaf = LeafQuad(mat, out Vector2 quad);
                        Texture2D bake = leaf ? null : Resources.Load<Texture2D>("Models/Nature/Textures/" + (mat.StartsWith("Rock") ? "bake_rocks" : "bake_bark"));
                        var map = new Dictionary<int, int>();
                        foreach (int ti in m.GetTriangles(s))
                        {
                            if (!map.TryGetValue(ti, out int ni))
                            {
                                Vector3 p = toRoot.MultiplyPoint3x4(mv[ti]);
                                Vector3 nn = mn.Length == mv.Length ? toRoot.MultiplyVector(mn[ti]).normalized : Vector3.up;
                                Vector2 uv = mu.Length == mv.Length ? mu[ti] : Vector2.zero;
                                if (leaf)
                                {
                                    ni = lv.Count; lv.Add(p); ln.Add(nn);
                                    luv.Add(new Vector2((Mathf.Clamp01(uv.x) + quad.x) * 0.5f, (Mathf.Clamp01(uv.y) + quad.y) * 0.5f));
                                }
                                else
                                {
                                    ni = sv.Count; sv.Add(p); sn.Add(nn);
                                    sl.Add(bake && bake.isReadable ? bake.GetPixelBilinear(uv.x, uv.y).grayscale : 0.5f);
                                }
                                map[ti] = ni;
                            }
                            (leaf ? lt : st).Add(ni);
                        }
                    }
                }
                if (sv.Count + lv.Count > 0)
                {
                    float tall = 0.1f;
                    foreach (var p in sv) tall = Mathf.Max(tall, p.y);
                    foreach (var p in lv) tall = Mathf.Max(tall, p.y);
                    if (lv.Count > 0) { Simplify(sv, sn, sl, st, SolidBudget, tall); Thin(lv, lt, LeafBudget); }
                    if (lv.Count > 0)
                    {
                        // drop the vertices of the leaf cards that were thinned out
                        var used = new Dictionary<int, int>(); var cv = new List<Vector3>(); var cn = new List<Vector3>(); var cu = new List<Vector2>();
                        for (int i = 0; i < lt.Count; i++)
                        {
                            if (!used.TryGetValue(lt[i], out int ni)) { ni = cv.Count; used[lt[i]] = ni; cv.Add(lv[lt[i]]); cn.Add(ln[lt[i]]); cu.Add(luv[lt[i]]); }
                            lt[i] = ni;
                        }
                        lv = cv; ln = cn; luv = cu;
                    }
                    var b = new Bounds(sv.Count > 0 ? sv[0] : lv[0], Vector3.zero);
                    foreach (var p in sv) b.Encapsulate(p);
                    foreach (var p in lv) b.Encapsulate(p);
                    // texture brightness -> a factor round 1 (0.72 .. 1.28) that shades the tint
                    float mean = 0; foreach (float f in sl) mean += f; mean = sl.Count > 0 ? Mathf.Max(0.05f, mean / sl.Count) : 1;
                    for (int i = 0; i < sl.Count; i++) sl[i] = Mathf.Clamp(1 + (sl[i] / mean - 1) * 0.8f, 0.72f, 1.28f);
                    // leaves are flat cards: lit by their own normals a crown is a jumble of light and dark cards, so the
                    // normals are rounded out from the middle of the crown (and up), which shades it as one soft shape
                    var lw = new float[lv.Count];
                    if (lv.Count > 0)
                    {
                        var lb = new Bounds(lv[0], Vector3.zero);
                        foreach (var p in lv) lb.Encapsulate(p);
                        Vector3 mid = lb.center - Vector3.up * lb.size.y * 0.25f;
                        for (int i = 0; i < lv.Count; i++)
                        {
                            ln[i] = ((lv[i] - mid).normalized + Vector3.up * 0.6f).normalized;
                            lw[i] = Mathf.Clamp01((lv[i].y - b.min.y) / Mathf.Max(0.01f, b.size.y));   // higher = sways more
                        }
                    }
                    raw = new Raw
                    {
                        sv = sv.ToArray(), sn = sn.ToArray(), sl = sl.ToArray(), st = st.ToArray(),
                        lv = lv.ToArray(), ln = ln.ToArray(), luv = luv.ToArray(), lw = lw, lt = lt.ToArray(),
                        min = b.min, size = b.size,
                    };
                }
            }
            raws[cacheKey] = raw;
            return raw;
        }

        static void Write(Raw raw, MeshKit solid, MeshKit leaves, Matrix4x4 m, Color solidTint, Color leafTint, float sway)
        {
            if (solid != null && raw.sv.Length > 0)
            {
                int b0 = solid.v.Count;
                for (int i = 0; i < raw.sv.Length; i++)
                {
                    solid.v.Add(solid.M.MultiplyPoint3x4(m.MultiplyPoint3x4(raw.sv[i])));
                    solid.n.Add(solid.M.MultiplyVector(m.MultiplyVector(raw.sn[i])).normalized);
                    Color c = solidTint * raw.sl[i]; c.a = 1;
                    solid.c.Add(c); solid.uv.Add(Vector2.zero);
                }
                foreach (int ti in raw.st) solid.t.Add(b0 + ti);
            }
            if (leaves != null && raw.lv.Length > 0)
            {
                int b0 = leaves.v.Count;
                for (int i = 0; i < raw.lv.Length; i++)
                {
                    leaves.v.Add(leaves.M.MultiplyPoint3x4(m.MultiplyPoint3x4(raw.lv[i])));
                    leaves.n.Add(leaves.M.MultiplyVector(m.MultiplyVector(raw.ln[i])).normalized);
                    Color c = leafTint; c.a = raw.lw[i] * sway;
                    leaves.c.Add(c); leaves.uv.Add(raw.luv[i]);
                }
                foreach (int ti in raw.lt) leaves.t.Add(b0 + ti);
            }
        }

        /// <summary>A tree standing at <paramref name="foot"/>, scaled to <paramref name="height"/> metres. False if the model is missing.</summary>
        public static bool AddTree(MeshKit solid, MeshKit leaves, string name, Vector3 foot, float yaw, float height, Color bark, Color leaf)
        {
            var raw = Load(name);
            if (raw == null) return false;
            float k = height / Mathf.Max(0.1f, raw.size.y);
            // the models' roots dip a little under their origin: sink them the same, scaled
            var m = Matrix4x4.TRS(foot, Quaternion.Euler(0, yaw, 0), Vector3.one * k);
            Write(raw, solid, leaves, m, bark, leaf, Mathf.Clamp01(height / 14f));
            return true;
        }

        /// <summary>Ground cover (bush, fern, plant): leaves only, no trunk to speak of, <paramref name="width"/> metres across.</summary>
        public static bool AddCover(MeshKit solid, MeshKit leaves, string name, Vector3 foot, float yaw, float width, Color tint)
        {
            var raw = Load(name);
            if (raw == null) return false;
            float k = width / Mathf.Max(0.1f, Mathf.Max(raw.size.x, raw.size.z));
            var m = Matrix4x4.TRS(foot, Quaternion.Euler(0, yaw, 0), Vector3.one * k);
            Write(raw, solid, leaves, m, new Color(0.35f, 0.27f, 0.2f), tint, 0.25f);
            return true;
        }

        /// <summary>A rock filling the box <paramref name="size"/> round <paramref name="center"/> (what MeshKit.Rock draws), in the colour asked for.</summary>
        public static bool AddRock(MeshKit k, Vector3 center, Vector3 size, Quaternion rot, Color col, int seed)
        {
            if (!On) return false;
            var raw = Load(Rocks[Mathf.Abs(seed) % Rocks.Length]);
            if (raw == null) return false;
            Vector3 mid = raw.min + raw.size * 0.5f;
            Vector3 s = new Vector3(size.x / raw.size.x, size.y / raw.size.y, size.z / raw.size.z);
            var m = Matrix4x4.TRS(center, rot * Quaternion.Euler(0, (seed & 3) * 90, 0), s) * Matrix4x4.Translate(-mid);
            Write(raw, k, null, m, col, Color.white, 0);
            return true;
        }
    }
}
