using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    /// <summary>
    /// Loads the CC0 models in Resources/Models (Quaternius, Kenney) as single meshes the game draws with its own
    /// shaders: every mesh under the prefab is combined as the prefab places it, optionally turned / scaled / moved,
    /// and either recoloured per vertex from its material colours (flat-colour models, drawn with Mats.Lit) or
    /// left white for a textured material (Mats.LitTex). Results are cached by path + options.
    /// </summary>
    public static class ImportedModels
    {
        static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();
        static readonly Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();

        /// <param name="path">Resources path, e.g. "Models/Kit/satelliteDish".</param>
        /// <param name="bakeColors">true = vertex colours from the materials; false = white (texture does the colour).</param>
        /// <param name="rot">turn applied after combining (e.g. to make the nose face +z).</param>
        /// <param name="size">scale so the longest side is this many metres (0 = keep), applied with <paramref name="scale"/>.</param>
        /// <param name="ground">true = bottom at y 0 and centred in x / z; false = centred on the bounds.</param>
        public static Mesh Load(string path, bool bakeColors, Quaternion rot, float size = 0, float scale = 1, bool ground = true, Color tint = default)
        {
            string key = $"{path}|{bakeColors}|{rot.eulerAngles}|{size}|{scale}|{ground}|{tint}";
            if (cache.TryGetValue(key, out var hit) && hit) return hit;
            var go = Resources.Load<GameObject>(path);
            if (!go) return null;
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var cols = new List<Color>(); var tris = new List<int>();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh;
                if (!m || !m.isReadable) continue;
                var mr = mf.GetComponent<MeshRenderer>();
                var mats = mr ? mr.sharedMaterials : new Material[0];
                var toRoot = mf.transform.localToWorldMatrix;   // as the prefab places it: its root rotation and unit scale included
                int baseV = verts.Count;
                var mv = m.vertices; var mn = m.normals; var mu = m.uv;
                for (int i = 0; i < mv.Length; i++)
                {
                    verts.Add(toRoot.MultiplyPoint3x4(mv[i]));
                    norms.Add(mn.Length == mv.Length ? toRoot.MultiplyVector(mn[i]).normalized : Vector3.up);
                    uvs.Add(mu.Length == mv.Length ? mu[i] : Vector2.zero);
                    cols.Add(Color.white);
                }
                for (int s = 0; s < m.subMeshCount; s++)
                {
                    Color c = Color.white;
                    if (bakeColors && s < mats.Length && mats[s]) c = mats[s].HasProperty("_Color") ? mats[s].color : Color.white;
                    c.a = 1;
                    if (bakeColors && s < mats.Length && mats[s] && mats[s].name.ToLower().Contains("light")) c.a = 0;   // Kenney "light" material glows
                    var st = m.GetTriangles(s);
                    for (int i = 0; i < st.Length; i++) { tris.Add(baseV + st[i]); cols[baseV + st[i]] = c; }
                }
            }
            if (verts.Count == 0) return null;
            // turn, then measure, scale and place
            for (int i = 0; i < verts.Count; i++) { verts[i] = rot * verts[i]; norms[i] = rot * norms[i]; }
            var b = new Bounds(verts[0], Vector3.zero);
            foreach (var v in verts) b.Encapsulate(v);
            float k = scale * (size > 0 ? size / Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) : 1);
            Vector3 origin = ground ? new Vector3(b.center.x, b.min.y, b.center.z) : b.center;
            bool tinted = tint.a > 0;
            for (int i = 0; i < verts.Count; i++)
            {
                verts[i] = (verts[i] - origin) * k;
                if (tinted) { var c = cols[i]; float a = c.a; c = Color.Lerp(c, c * tint * 1.6f, 0.55f); c.a = a; cols[i] = c; }
            }
            var mesh = new Mesh { name = path, indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs); mesh.SetColors(cols); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            cache[key] = mesh;
            return mesh;
        }

        public static Texture2D Tex(string path)
        {
            if (texCache.TryGetValue(path, out var t) && t) return t;
            t = Resources.Load<Texture2D>(path);
            texCache[path] = t;
            return t;
        }

        /// <summary>Adds a renderer + mesh filter child drawing the mesh (shadows on).</summary>
        public static GameObject Spawn(Mesh m, Material mat, Transform parent, Vector3 pos, Quaternion rot, string name = "model")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }
    }
}
