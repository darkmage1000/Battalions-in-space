using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    /// <summary>
    /// Low-poly mesh builder with flat shading and vertex colours. Every model in the game
    /// (soldiers, vehicles, ships, props, capital ships) is put together from these shapes.
    /// Colour alpha 0 = self-lit (glows) with BIS/Lit.
    /// </summary>
    public class MeshKit
    {
        public readonly List<Vector3> v = new List<Vector3>(256);
        public readonly List<Vector3> n = new List<Vector3>(256);
        public readonly List<Color32> c = new List<Color32>(256);
        public readonly List<Vector2> uv = new List<Vector2>(256);
        public readonly List<int> t = new List<int>(512);
        public Matrix4x4 M = Matrix4x4.identity;

        public int Count => v.Count;
        public void Clear() { v.Clear(); n.Clear(); c.Clear(); uv.Clear(); t.Clear(); M = Matrix4x4.identity; }

        public static Color Glow(Color col) { col.a = 0; return col; }

        public void Tri(Vector3 a, Vector3 b, Vector3 d, Color col)
        {
            a = M.MultiplyPoint3x4(a); b = M.MultiplyPoint3x4(b); d = M.MultiplyPoint3x4(d);
            Vector3 nn = Vector3.Cross(b - a, d - a);
            if (nn.sqrMagnitude < 1e-12f) return;
            nn.Normalize();
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(d);
            n.Add(nn); n.Add(nn); n.Add(nn);
            Color32 k = col; c.Add(k); c.Add(k); c.Add(k);
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(0, 1));
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 d, Vector3 e, Color col)
        {
            a = M.MultiplyPoint3x4(a); b = M.MultiplyPoint3x4(b); d = M.MultiplyPoint3x4(d); e = M.MultiplyPoint3x4(e);
            Vector3 nn = Vector3.Cross(b - a, e - a);
            if (nn.sqrMagnitude < 1e-12f) nn = Vector3.Cross(d - b, e - b);
            if (nn.sqrMagnitude < 1e-12f) return;
            nn.Normalize();
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(d); v.Add(e);
            for (int k = 0; k < 4; k++) n.Add(nn);
            Color32 q = col; c.Add(q); c.Add(q); c.Add(q); c.Add(q);
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
            t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        /// <summary>Box from 8 corners order: bottom (-y) a b c d, top e f g h (counter-clockwise from -x-z).</summary>
        public void Hexa(Vector3[] p, Color col)
        {
            Quad(p[0], p[1], p[2], p[3], col); // bottom (faces down)
            Quad(p[7], p[6], p[5], p[4], col); // top
            Quad(p[0], p[4], p[5], p[1], col); // -z
            Quad(p[1], p[5], p[6], p[2], col); // +x
            Quad(p[2], p[6], p[7], p[3], col); // +z
            Quad(p[3], p[7], p[4], p[0], col); // -x
        }

        static readonly Vector3[] tmp = new Vector3[8];

        public void Box(Vector3 center, Vector3 size, Color col) => Box(center, size, Quaternion.identity, col);

        public void Box(Vector3 center, Vector3 size, Quaternion rot, Color col)
        {
            Vector3 h = size * 0.5f;
            tmp[0] = center + rot * new Vector3(-h.x, -h.y, -h.z);
            tmp[1] = center + rot * new Vector3(h.x, -h.y, -h.z);
            tmp[2] = center + rot * new Vector3(h.x, -h.y, h.z);
            tmp[3] = center + rot * new Vector3(-h.x, -h.y, h.z);
            tmp[4] = center + rot * new Vector3(-h.x, h.y, -h.z);
            tmp[5] = center + rot * new Vector3(h.x, h.y, -h.z);
            tmp[6] = center + rot * new Vector3(h.x, h.y, h.z);
            tmp[7] = center + rot * new Vector3(-h.x, h.y, h.z);
            Hexa(tmp, col);
        }

        /// <summary>Tapered box: bottom size sb, top size st (x,z), height h, centred at c (bottom at c.y - h/2).</summary>
        public void Taper(Vector3 center, Vector2 sb, Vector2 st, float h, Quaternion rot, Color col, Vector2 topShift = default)
        {
            float y0 = -h * 0.5f, y1 = h * 0.5f;
            tmp[0] = center + rot * new Vector3(-sb.x * 0.5f, y0, -sb.y * 0.5f);
            tmp[1] = center + rot * new Vector3(sb.x * 0.5f, y0, -sb.y * 0.5f);
            tmp[2] = center + rot * new Vector3(sb.x * 0.5f, y0, sb.y * 0.5f);
            tmp[3] = center + rot * new Vector3(-sb.x * 0.5f, y0, sb.y * 0.5f);
            tmp[4] = center + rot * new Vector3(-st.x * 0.5f + topShift.x, y1, -st.y * 0.5f + topShift.y);
            tmp[5] = center + rot * new Vector3(st.x * 0.5f + topShift.x, y1, -st.y * 0.5f + topShift.y);
            tmp[6] = center + rot * new Vector3(st.x * 0.5f + topShift.x, y1, st.y * 0.5f + topShift.y);
            tmp[7] = center + rot * new Vector3(-st.x * 0.5f + topShift.x, y1, st.y * 0.5f + topShift.y);
            Hexa(tmp, col);
        }

        /// <summary>Cylinder along local Y; centre c, radius r, height h.</summary>
        public void Cyl(Vector3 center, float r, float h, Quaternion rot, Color col, int seg = 8, float rTop = -1, bool caps = true)
        {
            if (rTop < 0) rTop = r;
            float y0 = -h * 0.5f, y1 = h * 0.5f;
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2 / seg, a1 = (i + 1) * Mathf.PI * 2 / seg;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                Vector3 b0 = center + rot * (d0 * r + Vector3.up * y0), b1 = center + rot * (d1 * r + Vector3.up * y0);
                Vector3 t0 = center + rot * (d0 * rTop + Vector3.up * y1), t1 = center + rot * (d1 * rTop + Vector3.up * y1);
                Quad(b0, t0, t1, b1, col);
                if (caps)
                {
                    if (rTop > 0.0001f) Tri(center + rot * (Vector3.up * y1), t1, t0, col);
                    Tri(center + rot * (Vector3.up * y0), b0, b1, col);
                }
            }
        }

        /// <summary>Low-poly sphere (UV sphere), scaled by size (ellipsoid).</summary>
        public void Sphere(Vector3 center, Vector3 size, Quaternion rot, Color col, int seg = 8, int rings = 5)
        {
            Vector3 h = size * 0.5f;
            for (int r = 0; r < rings; r++)
            {
                float p0 = Mathf.PI * r / rings - Mathf.PI / 2, p1 = Mathf.PI * (r + 1) / rings - Mathf.PI / 2;
                for (int s = 0; s < seg; s++)
                {
                    float a0 = s * Mathf.PI * 2 / seg, a1 = (s + 1) * Mathf.PI * 2 / seg;
                    Vector3 P(float p, float a) => center + rot * Vector3.Scale(new Vector3(Mathf.Cos(p) * Mathf.Cos(a), Mathf.Sin(p), Mathf.Cos(p) * Mathf.Sin(a)), h);
                    Vector3 q0 = P(p0, a0), q1 = P(p0, a1), q2 = P(p1, a1), q3 = P(p1, a0);
                    if (r == 0) Tri(q0, q3, q2, col);
                    else if (r == rings - 1) Tri(q0, q3, q1, col);
                    else Quad(q0, q3, q2, q1, col);
                }
            }
        }

        /// <summary>Lumpy boulder: icosahedron-ish sphere with jittered vertices.</summary>
        public void Rock(Vector3 center, Vector3 size, Quaternion rot, Color col, int seed, float jitter = 0.25f)
        {
            // a real boulder gets a hand-made rock model (not on Low graphics); smooth lumps (snow caps, hedges, sandbags) stay as built here
            if (jitter >= 0.2f && NatureModels.AddRock(this, center, size, rot, col, seed)) return;
            var rng = new System.Random(seed);
            const int seg = 7, rings = 5;
            var pts = new Vector3[(rings + 1) * (seg + 1)];
            Vector3 h = size * 0.5f;
            for (int r = 0; r <= rings; r++)
                for (int s = 0; s <= seg; s++)
                {
                    float p = Mathf.PI * r / rings - Mathf.PI / 2, a = (s % seg) * Mathf.PI * 2 / seg;
                    float j = 1 + ((float)rng.NextDouble() - 0.5f) * 2 * jitter;
                    if (r == 0 || r == rings) j = 1;
                    Vector3 d = new Vector3(Mathf.Cos(p) * Mathf.Cos(a), Mathf.Sin(p), Mathf.Cos(p) * Mathf.Sin(a)) * j;
                    pts[r * (seg + 1) + s] = d;
                }
            for (int s = 0; s <= rings; s++) pts[s * (seg + 1) + seg] = pts[s * (seg + 1)];
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    Vector3 P(int rr, int ss)
                    {
                        Vector3 d = pts[rr * (seg + 1) + ss];
                        if (d.y < -0.3f) d.y = -0.3f - (d.y + 0.3f) * 0.2f; // flatter bottom
                        return center + rot * Vector3.Scale(d, h);
                    }
                    Color cc = col * (0.88f + 0.12f * (float)rng.NextDouble()); cc.a = col.a;
                    Vector3 q0 = P(r, s), q1 = P(r, s + 1), q2 = P(r + 1, s + 1), q3 = P(r + 1, s);
                    Tri(q0, q3, q2, cc); Tri(q0, q2, q1, cc);
                }
        }

        /// <summary>Cone (pointy) along local Y from base at bottom.</summary>
        public void Cone(Vector3 center, float r, float h, Quaternion rot, Color col, int seg = 8) =>
            Cyl(center, r, h, rot, col, seg, 0f);

        /// <summary>Triangular prism / wedge: base box footprint, ridge along z at top centre, shifted by ridgeX.</summary>
        public void Wedge(Vector3 center, Vector3 size, Quaternion rot, Color col, float ridgeX = 0)
        {
            Vector3 h = size * 0.5f;
            Vector3 P(float x, float y, float z) => center + rot * new Vector3(x, y, z);
            Vector3 a = P(-h.x, -h.y, -h.z), b = P(h.x, -h.y, -h.z), d = P(h.x, -h.y, h.z), e = P(-h.x, -h.y, h.z);
            Vector3 r0 = P(ridgeX * h.x, h.y, -h.z), r1 = P(ridgeX * h.x, h.y, h.z);
            Quad(a, b, d, e, col);
            Quad(b, r0, r1, d, col);
            Quad(e, r1, r0, a, col);
            Tri(a, r0, b, col);
            Tri(d, r1, e, col);
        }

        /// <summary>Pointed nose: square base at -z/2 (size w,h) tapering to a point at +z/2.</summary>
        public void Nose(Vector3 center, Vector3 size, Quaternion rot, Color col, float tipY = 0)
        {
            Vector3 h = size * 0.5f;
            Vector3 P(float x, float y, float z) => center + rot * new Vector3(x, y, z);
            Vector3 a = P(-h.x, -h.y, -h.z), b = P(h.x, -h.y, -h.z), d = P(h.x, h.y, -h.z), e = P(-h.x, h.y, -h.z);
            Vector3 tip = P(0, tipY * h.y, h.z);
            Quad(a, e, d, b, col);
            Tri(a, b, tip, col); Tri(b, d, tip, col); Tri(d, e, tip, col); Tri(e, a, tip, col);
        }

        /// <summary>Flat ring on the XZ plane (for capture rings etc.).</summary>
        public void Ring(Vector3 center, float r0, float r1, Color col, int seg = 32)
        {
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2 / seg, a1 = (i + 1) * Mathf.PI * 2 / seg;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                Quad(center + d0 * r0, center + d1 * r0, center + d1 * r1, center + d0 * r1, col);
            }
        }

        public Mesh Build(string name = "mesh")
        {
            var m = new Mesh { name = name };
            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v); m.SetNormals(n); m.SetColors(c); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Make a GameObject with this mesh using the shared lit material.</summary>
        public GameObject Make(string name, Transform parent, bool shadows = true, Material mat = null)
        {
            var go = new GameObject(name);
            if (parent) go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = Build(name);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat ? mat : Mats.Lit;
            mr.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }
    }
}
