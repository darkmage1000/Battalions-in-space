using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    /// <summary>
    /// Small hand-made props - barrels, crates, carts, fences, lanterns, market stalls, tents, plank piles - from Kenney's
    /// CC0 Fantasy Town Kit (Resources/Models/Props/Town) and Survival Kit (Props/Camp). Both kits paint their models from
    /// one small palette texture; the colours are read once per vertex (like GunModels) and the model is written into a
    /// map chunk's mesh, so a prop is never a GameObject of its own. Sizes are given in metres by the caller.
    /// </summary>
    public static class PropModels
    {
        class Raw { public Vector3[] v, n; public Color[] c; public int[] t; public Vector3 size; }
        static readonly Dictionary<string, Raw> raws = new Dictionary<string, Raw>();

        /// <summary>The model's size in its own units (x, y, z), or zero if it is missing.</summary>
        public static Vector3 Size(string path) => Load(path)?.size ?? Vector3.zero;

        static Raw Load(string path)
        {
            if (raws.TryGetValue(path, out var hit)) return hit;
            Raw raw = null;
            var go = Resources.Load<GameObject>("Models/Props/" + path);
            if (go)
            {
                string kit = path.Substring(0, Mathf.Max(0, path.IndexOf('/')));
                var fallback = Resources.Load<Texture2D>("Models/Props/" + kit + "/Textures/colormap");
                bool lamp = path.Contains("lantern");
                var v = new List<Vector3>(); var n = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    var m = mf.sharedMesh;
                    if (!m || !m.isReadable) continue;
                    var mr = mf.GetComponent<MeshRenderer>();
                    var toRoot = mf.transform.localToWorldMatrix;
                    var mv = m.vertices; var mn = m.normals; var mu = m.uv;
                    for (int s = 0; s < m.subMeshCount; s++)
                    {
                        var mat = mr && s < mr.sharedMaterials.Length ? mr.sharedMaterials[s] : null;
                        var tex = mat ? mat.mainTexture as Texture2D : null;
                        if (!tex || !tex.isReadable) tex = fallback;
                        Color flat = mat && mat.HasProperty("_Color") ? mat.color : Color.gray;
                        var map = new Dictionary<int, int>();
                        foreach (int ti in m.GetTriangles(s))
                        {
                            if (!map.TryGetValue(ti, out int ni))
                            {
                                ni = v.Count; map[ti] = ni;
                                v.Add(toRoot.MultiplyPoint3x4(mv[ti]));
                                n.Add(mn.Length == mv.Length ? toRoot.MultiplyVector(mn[ti]).normalized : Vector3.up);
                                Color col = tex && tex.isReadable && mu.Length == mv.Length ? tex.GetPixelBilinear(mu[ti].x, mu[ti].y) : flat;
                                col.a = 1;
                                // a lantern's bright warm glass is self-lit
                                if (lamp && col.r > 0.85f && col.g > 0.65f && col.b < 0.6f) col.a = 0;
                                c.Add(col);
                            }
                            t.Add(ni);
                        }
                    }
                }
                if (v.Count > 0)
                {
                    var b = new Bounds(v[0], Vector3.zero);
                    foreach (var p in v) b.Encapsulate(p);
                    // origin: centred in x / z, standing on y = 0
                    Vector3 o = new Vector3(b.center.x, b.min.y, b.center.z);
                    for (int i = 0; i < v.Count; i++) v[i] -= o;
                    raw = new Raw { v = v.ToArray(), n = n.ToArray(), c = c.ToArray(), t = t.ToArray(), size = b.size };
                }
            }
            raws[path] = raw;
            return raw;
        }

        /// <summary>
        /// Writes a prop into the kit standing at <paramref name="foot"/>, turned by <paramref name="yaw"/>, scaled per axis
        /// (model units to metres). Colours can be shaded by <paramref name="shade"/>. False if the model is missing.
        /// </summary>
        public static bool Add(MeshKit k, string path, Vector3 foot, float yaw, Vector3 scale, float shade = 1, Color? tint = null)
        {
            var raw = Load(path);
            if (raw == null) return false;
            var m = Matrix4x4.TRS(foot, Quaternion.Euler(0, yaw, 0), scale);
            int b0 = k.v.Count;
            for (int i = 0; i < raw.v.Length; i++)
            {
                k.v.Add(k.M.MultiplyPoint3x4(m.MultiplyPoint3x4(raw.v[i])));
                k.n.Add(k.M.MultiplyVector(m.MultiplyVector(raw.n[i])).normalized);
                Color c = raw.c[i];
                if (c.a > 0.5f) { float a = c.a; c *= shade; if (tint.HasValue) c *= tint.Value; c.a = a; }
                k.c.Add(c); k.uv.Add(Vector2.zero);
            }
            foreach (int ti in raw.t) k.t.Add(b0 + ti);
            return true;
        }

        public static bool Add(MeshKit k, string path, Vector3 foot, float yaw, float scale, float shade = 1, Color? tint = null) => Add(k, path, foot, yaw, Vector3.one * scale, shade, tint);
    }
}
