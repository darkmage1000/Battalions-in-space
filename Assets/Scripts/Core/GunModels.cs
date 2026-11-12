using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    /// <summary>
    /// Hand-made gun models (Kenney's CC0 Blaster Kit, Resources/Models/Guns) in place of the box guns. Each faction has its
    /// own model for each kind of gun, and the kit's bright accent colour is swapped for the faction's (or a loot tier's),
    /// so a Concord rifle and a Vorrkai rifle are told apart by shape and by colour. The kit paints its models from one
    /// small palette texture; the colours are read off it per vertex so the game's own vertex-colour shader draws them.
    /// A model is turned so its barrel runs along +z with the grip at the origin - where the code-built guns had theirs.
    /// </summary>
    public static class GunModels
    {
        // which blaster (a..r) each faction carries, by kind. Factions: 0 Concord, 1 Vorrkai, 2 Synod, 3 Veil.
        static readonly string[] rifle = { "d", "n", "p", "j" };
        static readonly string[] carbine = { "g", "m", "g", "m" };
        static readonly string[] sniper = { "f", "e", "f", "e" };
        static readonly string[] rocket = { "h", "b", "b", "h" };
        static readonly string[] shotgun = { "r", "o", "l", "q" };
        static readonly string[] pistol = { "a", "c", "i", "k" };

        /// <summary>The blaster for this weapon in this faction's hands, and how long it is drawn (metres), or null when
        /// the weapon has no kit model (blades, beams, repair tools).</summary>
        public static string ModelFor(WeaponDef w, int faction, out float length)
        {
            int f = Mathf.Clamp(faction, 0, 3);
            length = 0;
            switch (w.kind)
            {
                case WKind.Rifle: length = 0.92f; return rifle[f];
                case WKind.Repeater: if (w.splash > 0) { length = 1.05f; return "l"; } length = 0.78f; return carbine[f];
                case WKind.Sniper: length = 1.32f; return sniper[f];
                case WKind.Rocket: length = 1.05f; return rocket[f];
                case WKind.Shotgun: length = 0.82f; return shotgun[f];
                case WKind.Pistol: length = 0.4f; return pistol[f];
                default: return null;
            }
        }

        const float Slim = 0.62f;
        class Raw { public Vector3[] v, n; public Color[] c; public int[] t; }
        static readonly Dictionary<string, Raw> raws = new Dictionary<string, Raw>();

        /// <summary>The model as the kit made it, turned barrel-forward (+z) with the grip at the origin, 1 unit long.</summary>
        static Raw Load(string letter)
        {
            if (raws.TryGetValue(letter, out var hit)) return hit;
            Raw raw = null;
            var go = Resources.Load<GameObject>("Models/Guns/blaster-" + letter);
            if (go)
            {
                var verts = new List<Vector3>(); var norms = new List<Vector3>(); var cols = new List<Color>(); var tris = new List<int>();
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    var m = mf.sharedMesh;
                    if (!m || !m.isReadable) continue;
                    var mr = mf.GetComponent<MeshRenderer>();
                    var mat = mr ? mr.sharedMaterial : null;
                    var tex = mat ? mat.mainTexture as Texture2D : null;
                    if (!tex || !tex.isReadable) tex = Resources.Load<Texture2D>("Models/Guns/Textures/colormap");
                    Color flat = mat && mat.HasProperty("_Color") ? mat.color : Color.gray;
                    var toRoot = mf.transform.localToWorldMatrix;
                    int b0 = verts.Count;
                    var mv = m.vertices; var mn = m.normals; var mu = m.uv;
                    for (int i = 0; i < mv.Length; i++)
                    {
                        verts.Add(toRoot.MultiplyPoint3x4(mv[i]));
                        norms.Add(mn.Length == mv.Length ? toRoot.MultiplyVector(mn[i]).normalized : Vector3.up);
                        Color c = tex && tex.isReadable && mu.Length == mv.Length ? tex.GetPixelBilinear(mu[i].x, mu[i].y) : flat;
                        c.a = 1; cols.Add(c);
                    }
                    for (int s = 0; s < m.subMeshCount; s++) foreach (int ti in m.GetTriangles(s)) tris.Add(b0 + ti);
                }
                if (verts.Count > 0)
                {
                    // the barrel is the longest horizontal side; put it on z
                    var b = new Bounds(verts[0], Vector3.zero);
                    foreach (var p in verts) b.Encapsulate(p);
                    Quaternion q = b.size.x > b.size.z ? Quaternion.Euler(0, 90, 0) : Quaternion.identity;
                    for (int i = 0; i < verts.Count; i++) { verts[i] = q * verts[i]; norms[i] = q * norms[i]; }
                    b = new Bounds(verts[0], Vector3.zero);
                    foreach (var p in verts) b.Encapsulate(p);
                    // the grip hangs lowest: the bottom fifth of the model. Its z is the hand; the end further from it is the muzzle
                    float gz = 0; int gn = 0; float low = b.min.y + b.size.y * 0.2f;
                    foreach (var p in verts) if (p.y <= low) { gz += p.z; gn++; }
                    gz = gn > 0 ? gz / gn : b.center.z;
                    bool flip = b.max.z - gz < gz - b.min.z;
                    if (flip)
                    {
                        var turn = Quaternion.Euler(0, 180, 0);
                        for (int i = 0; i < verts.Count; i++) { verts[i] = turn * verts[i]; norms[i] = turn * norms[i]; }
                        gz = -gz;
                        b = new Bounds(verts[0], Vector3.zero);
                        foreach (var p in verts) b.Encapsulate(p);
                    }
                    float k = 1f / Mathf.Max(0.0001f, b.size.z);
                    // origin: the hand - at the grip along z, centred across, a little under the top of the body
                    Vector3 origin = new Vector3(b.center.x, b.min.y + b.size.y * 0.62f, gz);
                    // the kit's blasters are toy-chunky: slimmed across and in height so they sit right in a soldier's hands
                    for (int i = 0; i < verts.Count; i++) { Vector3 p = (verts[i] - origin) * k; p.x *= Slim; p.y *= Slim; verts[i] = p; }
                    raw = new Raw { v = verts.ToArray(), n = norms.ToArray(), c = cols.ToArray(), t = tris.ToArray() };
                }
            }
            raws[letter] = raw;
            return raw;
        }

        /// <summary>How far ahead of the grip the muzzle of this weapon's model is (0 = no model).</summary>
        public static float MuzzleZ(WeaponDef w, int faction, float sizeMul = 1f)
        {
            string letter = ModelFor(w, faction, out float length);
            var raw = letter != null ? Load(letter) : null;
            if (raw == null) return 0;
            float mz = 0;
            foreach (var p in raw.v) if (p.z > mz) mz = p.z;
            return mz * length * sizeMul;
        }

        /// <summary>The kit's bright colours become the accent (keeping their shading); its greys stay metal.</summary>
        static Color Recolour(Color c, Color accent, Color metalTint)
        {
            Color.RGBToHSV(c, out _, out float s, out float v);
            if (s > 0.28f) { Color a = accent * Mathf.Lerp(0.55f, 1.15f, v); a.a = 1; return a; }
            Color g = Color.Lerp(c, c * metalTint, 0.5f); g.a = 1; return g;
        }

        /// <summary>
        /// Adds the weapon's model to a mesh kit at <paramref name="at"/> (grip there, barrel along <paramref name="rot"/> * +z).
        /// Returns the distance from the grip to the muzzle, or 0 if this weapon has no model (the caller builds its own).
        /// </summary>
        public static float Add(MeshKit k, WeaponDef w, int faction, Color accent, Vector3 at, Quaternion rot, float sizeMul = 1f)
        {
            string letter = ModelFor(w, faction, out float length);
            if (letter == null) return 0;
            var raw = Load(letter);
            if (raw == null) return 0;
            length *= sizeMul;
            Color metalTint = new Color(0.62f, 0.64f, 0.7f);
            int b0 = k.v.Count; float muzzle = 0;
            for (int i = 0; i < raw.v.Length; i++)
            {
                Vector3 p = raw.v[i] * length;
                if (p.z > muzzle) muzzle = p.z;
                k.v.Add(k.M.MultiplyPoint3x4(at + rot * p));
                k.n.Add(k.M.MultiplyVector(rot * raw.n[i]).normalized);
                k.c.Add(Recolour(raw.c[i], accent, metalTint));
                k.uv.Add(Vector2.zero);
            }
            foreach (int ti in raw.t) k.t.Add(b0 + ti);
            return muzzle;
        }
    }
}
