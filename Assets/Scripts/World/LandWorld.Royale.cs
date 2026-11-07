using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    /// <summary>
    /// Battle royale places: walled compounds of large enterable buildings (barracks, spaceport hangars, research labs),
    /// sealed vaults that only a key opens, and a crashed warship section. Plus the biome-aware sky that follows the camera.
    /// </summary>
    public partial class LandWorld
    {
        /// <summary>A vault: a sealed bunker whose door slides open for a key.</summary>
        public class VaultSite
        {
            public House house;
            public Transform door;
            public Vector3 closedPos, openPos, lockPoint, inside;
            public bool open;
            public float openT;
        }
        public readonly List<VaultSite> vaults = new List<VaultSite>();

        /// <summary>Areas levelled and kept free of scattered props (compounds, vaults, the wreck): centre, radius, level.</summary>
        readonly List<(Vector2 c, float r, float level)> pads = new List<(Vector2, float, float)>();

        /// <summary>Called from PrepareLayout for the royale features: level the ground they stand on.</summary>
        void PreparePads()
        {
            if (def.features == null) return;
            foreach (var f in def.features)
            {
                float r;
                switch (f.kind)
                {
                    case FeatureKind.Compound: r = f.width * NS * 1.12f; break;
                    case FeatureKind.Vault: r = 15; break;
                    case FeatureKind.Wreck: r = 30; break;
                    default: continue;
                }
                Vector2 c = f.pts[0] * NS;
                float sum = 0; int n = 0;
                for (int a = 0; a < 12; a++)
                    for (int d = 0; d < 3; d++)
                    {
                        float an = a * Mathf.PI / 6;
                        Vector2 q = c + new Vector2(Mathf.Cos(an), Mathf.Sin(an)) * r * (0.15f + d * 0.35f);
                        sum += RawHeight(q.x, q.y); n++;
                    }
                pads.Add((c, r, sum / n));
            }
        }

        float PadHeight(Vector2 q, float y, ref byte tg)
        {
            foreach (var p in pads)
            {
                float d = Vector2.Distance(q, p.c);
                if (d >= p.r + 18) continue;
                y = Mathf.Lerp(p.level, y, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(p.r, p.r + 18, d)));
                if (d < p.r) tg |= TagClear;
            }
            return y;
        }

        // ── compound ─────────────────────────────────────────────────────
        void Compound(Vector2 c, float rad, int count, int style, System.Func<float, float, MeshKit> kitAt, List<(Vector2 c, float r)> placed)
        {
            At(c.x, c.y);
            var k = kitAt(c.x, c.y);
            Color wallC = style == 2 ? new Color(0.86f, 0.88f, 0.92f) : style == 1 ? new Color(0.74f, 0.68f, 0.58f) : new Color(0.56f, 0.58f, 0.54f);
            Color dark = Shade(wallC, 0.6f);
            int made = 0;
            for (int i = 0; i < count * 4 && made < count; i++)
            {
                bool outer = count > 6 ? made % 2 == 0 : true;
                float ring = rad * (outer ? 0.66f : 0.3f) * (i >= count * 2 ? 0.85f : 1f);
                float ang = (made * 360f / count + (i / count) * 23f + (outer ? 0 : 30)) * Mathf.Deg2Rad;
                Vector2 q = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * ring;
                float w, dp, hgt, doorW;
                switch (style)
                {
                    case 1: w = R(20, 24); dp = R(15, 18); hgt = R(8, 10); doorW = 7f; break;     // hangar
                    case 2: w = R(12, 15); dp = R(10, 12); hgt = 5.5f; doorW = 2.4f; break;       // lab
                    default: w = R(14, 18); dp = R(8, 10); hgt = 5f; doorW = 2.4f; break;         // barracks
                }
                float rr = Mathf.Sqrt(w * w + dp * dp) / 2;
                bool ok = true;
                foreach (var b in placed) if (Vector2.Distance(b.c, q) < b.r + rr + 3f) { ok = false; break; }
                for (int a = 0; a < 8 && ok; a++) { float an = a * Mathf.PI / 4; if (OnRoad(q.x + Mathf.Cos(an) * rr, q.y + Mathf.Sin(an) * rr)) ok = false; }
                if (!ok || OnRoad(q.x, q.y)) continue;
                Vector2 toC = (c - q).normalized;
                var rot = Quaternion.LookRotation(new Vector3(toC.x, 0, toC.y));     // the door (side 0, +z) faces the yard
                float lo = float.MaxValue, hi = float.MinValue;
                for (int s = 0; s < 4; s++)
                {
                    Vector3 corner = rot * new Vector3((s % 2 == 0 ? -0.5f : 0.5f) * w, 0, (s < 2 ? -0.5f : 0.5f) * dp);
                    float hh = Height(q.x + corner.x, q.y + corner.z);
                    lo = Mathf.Min(lo, hh); hi = Mathf.Max(hi, hh);
                }
                if (hi - lo > 4.5f) continue;
                placed.Add((q, rr));
                made++;
                var kk = kitAt(q.x, q.y);
                bool two = style != 1 && R() < 0.5f;
                if (two) TwoStory(kk, new Vector3(q.x, hi, q.y), rot, w, dp, lo, 0, wallC);
                else HouseShell(kk, new Vector3(q.x, hi, q.y), rot, w, dp, lo, 0, doorW, hgt, style != 1, wallC);
                Vector3 top = new Vector3(q.x, hi + (two ? 9.2f : hgt + 0.4f), q.y);
                if (style == 1)
                {
                    // a curved hangar roof and the big door's frame
                    kk.Cyl(top + Vector3.up * 0.2f, dp * 0.5f, w + 0.6f, rot * Quaternion.Euler(0, 0, 90), Shade(wallC, 0.85f), 10, -1, true);
                    kk.Box(new Vector3(q.x, hi, q.y) + rot * new Vector3(0, hgt - 0.5f, dp / 2 + 0.3f), new Vector3(doorW + 1.4f, 1f, 0.5f), rot, new Color(0.9f, 0.7f, 0.2f));
                }
                else if (style == 2)
                {
                    kk.Cyl(top + rot * new Vector3(w * 0.25f, 1.2f, 0), 0.9f, 2.4f, Quaternion.identity, MeshKit.Glow(new Color(0.4f, 0.85f, 1f) * 0.7f), 8);
                    kk.Box(top + rot * new Vector3(-w * 0.2f, 0.6f, 0), new Vector3(3, 1.2f, 2.2f), rot, Shade(wallC, 0.8f));
                }
                else
                {
                    // barracks: a row of vents and a stripe
                    kk.Box(new Vector3(q.x, hi, q.y) + rot * new Vector3(0, hgt * 0.75f, dp / 2 + 0.05f), new Vector3(w * 0.9f, 0.3f, 0.1f), rot, new Color(0.75f, 0.6f, 0.2f));
                    for (int v = -1; v <= 1; v++) kk.Box(top + rot * new Vector3(v * w * 0.3f, 0.4f, 0), new Vector3(1.4f, 0.8f, 1.4f), rot, dark);
                }
            }

            // a lookout: an open tower inside the wall (military base and spaceport)
            if (style != 2)
                for (int t = 0; t < 8; t++)
                {
                    float ang = (t * 45 + 22) * Mathf.Deg2Rad;
                    Vector2 q = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad * 0.8f;
                    int before = towersBuilt;
                    OpenTower(q, 14, style == 1 ? 4 : 3, kitAt, placed);
                    if (towersBuilt > before) break;
                }

            // the yard
            if (style == 1)
            {
                // landing pads with a derelict starfighter or two
                for (int p = 0; p < 3; p++)
                {
                    float ang = (p * 120 + 60) * Mathf.Deg2Rad;
                    Vector2 q = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad * 0.28f;
                    float y = Height(q.x, q.y);
                    Vector3 pp = new Vector3(q.x, y, q.y);
                    k.Cyl(pp + Vector3.up * 0.08f, 8, 0.25f, Quaternion.identity, new Color(0.34f, 0.34f, 0.36f), 16);
                    k.Ring(pp + Vector3.up * 0.22f, 7.2f, 7.7f, MeshKit.Glow(new Color(1f, 0.75f, 0.3f) * 0.6f), 32);
                    k.Box(pp + Vector3.up * 0.22f, new Vector3(5, 0.04f, 0.6f), Quaternion.identity, new Color(0.9f, 0.85f, 0.75f));
                    k.Box(pp + Vector3.up * 0.22f, new Vector3(0.6f, 0.04f, 5), Quaternion.identity, new Color(0.9f, 0.85f, 0.75f));
                    if (p != 1) Wreck(k, pp + Vector3.up * 0.2f, Quaternion.Euler(0, R(0, 360), 0), Vector3.up);
                }
                for (int t = 0; t < 4; t++)
                {
                    float ang = (t * 90 + 15) * Mathf.Deg2Rad;
                    Vector2 q = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad * 0.9f;
                    if (OnRoad(q.x, q.y)) continue;
                    Vector3 tp = new Vector3(q.x, Height(q.x, q.y), q.y);
                    k.Cyl(tp + Vector3.up * 3, 2.2f, 6, Quaternion.identity, new Color(0.82f, 0.8f, 0.76f), 12);
                    k.Cyl(tp + Vector3.up * 6.1f, 2.25f, 0.3f, Quaternion.identity, new Color(0.85f, 0.35f, 0.2f), 12);
                    ColCap(tp, 2.2f, 6.2f);
                }
            }
            else if (style == 0)
            {
                // sandbag rows in the yard (waist high: vault over them) and a flag
                for (int s = 0; s < 6; s++)
                {
                    float ang = (s * 60 + 20) * Mathf.Deg2Rad;
                    Vector2 q = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad * 0.42f;
                    if (OnRoad(q.x, q.y)) continue;
                    var sq = Quaternion.Euler(0, s * 60 + 110, 0);
                    Vector3 sp = new Vector3(q.x, Height(q.x, q.y), q.y);
                    k.Rock(sp + Vector3.up * 0.5f, new Vector3(4.5f, 1.1f, 1.1f), sq, new Color(0.55f, 0.5f, 0.38f), rng.Next(), 0.12f);
                    Col(sp + Vector3.up * 0.5f, new Vector3(4.4f, 1f, 1f), sq);
                }
                Vector3 fp = new Vector3(c.x, Height(c.x, c.y), c.y);
                k.Cyl(fp + Vector3.up * 5, 0.12f, 10, Quaternion.identity, new Color(0.7f, 0.7f, 0.72f), 6);
                k.Box(fp + new Vector3(0.9f, 9.3f, 0), new Vector3(1.8f, 1.1f, 0.05f), Quaternion.identity, new Color(0.7f, 0.2f, 0.18f));
                ColCap(fp, 0.3f, 10);
            }
            else
            {
                // research: a big dish and coolant tanks
                Vector3 dp0 = new Vector3(c.x, Height(c.x, c.y), c.y);
                k.Cyl(dp0 + Vector3.up * 3, 0.6f, 6, Quaternion.identity, new Color(0.7f, 0.72f, 0.76f), 8);
                k.Cone(dp0 + Vector3.up * 7.2f, 5f, 2.4f, Quaternion.Euler(150, 30, 0), new Color(0.9f, 0.92f, 0.95f), 16);
                k.Sphere(dp0 + Vector3.up * 6.4f, Vector3.one * 0.7f, Quaternion.identity, MeshKit.Glow(new Color(1f, 0.3f, 0.2f)), 6, 4);
                ColCap(dp0, 0.8f, 7);
                for (int t = 0; t < 3; t++)
                {
                    float ang = (t * 120 + 40) * Mathf.Deg2Rad;
                    Vector2 q = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad * 0.42f;
                    if (OnRoad(q.x, q.y)) continue;
                    Vector3 tp = new Vector3(q.x, Height(q.x, q.y), q.y);
                    k.Cyl(tp + Vector3.up * 2, 1.4f, 4, Quaternion.identity, new Color(0.8f, 0.82f, 0.86f), 10);
                    k.Cyl(tp + Vector3.up * 2.2f, 1.45f, 0.5f, Quaternion.identity, MeshKit.Glow(new Color(0.3f, 0.9f, 1f) * 0.6f), 10);
                    ColCap(tp, 1.4f, 4);
                }
            }

            // the perimeter wall: tall concrete with gates where roads come in and at the four corners
            float circ = 2 * Mathf.PI * rad;
            int segs = Mathf.Max(12, Mathf.RoundToInt(circ / 7.5f));
            for (int s = 0; s < segs; s++)
            {
                float a = (s + 0.5f) / segs * Mathf.PI * 2;
                float deg = a * Mathf.Rad2Deg;
                if (Mathf.Abs(Mathf.DeltaAngle(deg, 45)) < 7 || Mathf.Abs(Mathf.DeltaAngle(deg, 225)) < 7) continue;    // two walk-in gates
                Vector2 q = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad;
                Vector2 tng = new Vector2(-Mathf.Sin(a), Mathf.Cos(a));
                float len = circ / segs - 1.2f;
                if (OnRoad(q.x, q.y) || OnRoad(q.x + tng.x * len / 2, q.y + tng.y * len / 2) || OnRoad(q.x - tng.x * len / 2, q.y - tng.y * len / 2)) continue;
                bool hit = false;
                foreach (var b in placed) if (Vector2.Distance(b.c, q) < b.r + 2) { hit = true; break; }
                if (hit) continue;
                float y = Mathf.Min(Height(q.x + tng.x * len / 2, q.y + tng.y * len / 2), Height(q.x - tng.x * len / 2, q.y - tng.y * len / 2));
                Vector3 p = new Vector3(q.x, y, q.y);
                var wq = Quaternion.LookRotation(new Vector3(tng.x, 0, tng.y));
                const float H = 2.8f;
                k.Box(p + Vector3.up * (H / 2 - 0.4f), new Vector3(0.8f, H + 0.8f, len), wq, wallC);
                k.Box(p + Vector3.up * (H + 0.05f), new Vector3(1f, 0.2f, len + 0.1f), wq, dark);
                Col(p + Vector3.up * (H / 2 - 0.4f), new Vector3(0.8f, H + 0.8f, len), wq);
            }
        }

        // ── vault ────────────────────────────────────────────────────────
        void BuildVault(Vector2 c, float yaw, System.Func<float, float, MeshKit> kitAt, List<(Vector2 c, float r)> placed)
        {
            At(c.x, c.y);
            var rot = Quaternion.Euler(0, yaw, 0);
            const float w = 11, dp = 9, hgt = 4.6f;
            float lo = float.MaxValue, hi = float.MinValue;
            for (int s = 0; s < 4; s++)
            {
                Vector3 corner = rot * new Vector3((s % 2 == 0 ? -0.5f : 0.5f) * w, 0, (s < 2 ? -0.5f : 0.5f) * dp);
                float hh = Height(c.x + corner.x, c.y + corner.z);
                lo = Mathf.Min(lo, hh); hi = Mathf.Max(hi, hh);
            }
            placed.Add((c, 8));
            var k = kitAt(c.x, c.y);
            Color steel = new Color(0.36f, 0.38f, 0.42f);
            var hs = HouseShell(k, new Vector3(c.x, hi, c.y), rot, w, dp, lo, 0, 2.4f, hgt, false, steel);
            if (hs == null) return;
            hs.vault = true;
            Vector3 dn = rot * Vector3.forward;
            Vector3 front = hs.door;
            // hazard stripes and a red lock light over the door, armour ribs on the walls
            for (int i = -3; i <= 3; i++)
                k.Box(front + Vector3.up * (hgt - 0.6f) + dn * 0.3f + rot * Vector3.right * i * 0.7f, new Vector3(0.35f, 0.5f, 0.06f), rot, i % 2 == 0 ? new Color(0.95f, 0.75f, 0.15f) : new Color(0.1f, 0.1f, 0.1f));
            k.Sphere(front + Vector3.up * (hgt - 0.1f) + dn * 0.35f, Vector3.one * 0.5f, Quaternion.identity, MeshKit.Glow(new Color(1f, 0.25f, 0.15f)), 6, 4);
            for (int s = 0; s < 4; s++)
            {
                var sq = rot * Quaternion.Euler(0, s * 90, 0);
                float half = (s % 2 == 0 ? dp : w) / 2 + 0.3f;
                for (int i = -1; i <= 1; i += 2)
                    k.Box(new Vector3(c.x, hi, c.y) + sq * new Vector3(i * ((s % 2 == 0 ? w : dp) / 2 - 0.6f), hgt / 2, half), new Vector3(0.6f, hgt, 0.4f), sq, Shade(steel, 0.75f));
            }
            // the door: a sliding steel slab that blocks the doorway until a key opens it
            var dk = new MeshKit();
            dk.Box(Vector3.zero, new Vector3(2.5f, 2.8f, 0.45f), Color.Lerp(steel, new Color(0.6f, 0.62f, 0.66f), 0.5f));
            dk.Box(new Vector3(0, 0, 0.25f), new Vector3(1.6f, 0.3f, 0.06f), new Color(0.95f, 0.75f, 0.15f));
            dk.Cyl(new Vector3(0, 0.4f, 0.26f), 0.35f, 0.08f, Quaternion.Euler(90, 0, 0), MeshKit.Glow(new Color(1f, 0.3f, 0.2f)), 10);
            var dgo = dk.Make("vaultDoor", transform, true);
            Vector3 closed = front + Vector3.up * 1.4f;
            dgo.transform.SetPositionAndRotation(closed, Quaternion.LookRotation(dn));
            var bc = dgo.AddComponent<BoxCollider>(); bc.size = new Vector3(2.5f, 2.8f, 0.6f);
            vaults.Add(new VaultSite
            {
                house = hs, door = dgo.transform, closedPos = closed, openPos = closed + Vector3.down * 3.05f,
                lockPoint = hs.doorOut, inside = hs.center,
            });
        }

        /// <summary>Slides a vault door down into the floor (called by the battle royale when a key is used).</summary>
        public void OpenVault(VaultSite v)
        {
            if (v.open) return;
            v.open = true; v.openT = 0;
            var bc = v.door.GetComponent<BoxCollider>(); if (bc) bc.enabled = false;
        }

        void TickVaults(float dt)
        {
            foreach (var v in vaults)
                if (v.open && v.openT < 1)
                {
                    v.openT = Mathf.Min(1, v.openT + dt / 2.2f);
                    v.door.position = Vector3.Lerp(v.closedPos, v.openPos, Mathf.SmoothStep(0, 1, v.openT));
                }
        }

        // ── the crashed warship ──────────────────────────────────────────
        void WarshipWreck(Vector2 c, float yaw, System.Func<float, float, MeshKit> kitAt, List<(Vector2 c, float r)> placed)
        {
            At(c.x, c.y);
            var rot = Quaternion.Euler(0, yaw, 0);
            const float w = 13, dp = 34, hgt = 8;
            float lo = float.MaxValue, hi = float.MinValue;
            for (int s = 0; s < 4; s++)
            {
                Vector3 corner = rot * new Vector3((s % 2 == 0 ? -0.5f : 0.5f) * w, 0, (s < 2 ? -0.5f : 0.5f) * dp);
                float hh = Height(c.x + corner.x, c.y + corner.z);
                lo = Mathf.Min(lo, hh); hi = Mathf.Max(hi, hh);
            }
            placed.Add((c, 20));
            var k = kitAt(c.x, c.y);
            Color hull = new Color(0.42f, 0.44f, 0.48f), burnt = new Color(0.17f, 0.15f, 0.14f);
            // the breach is on the long side (door side 1, +x)
            HouseShell(k, new Vector3(c.x, hi, c.y), rot, w, dp, lo, 1, 5f, hgt, false, hull);
            Vector3 b = new Vector3(c.x, hi, c.y);
            // nose, engines, armour plating and scorch marks round it
            k.Nose(b + rot * new Vector3(0, hgt * 0.45f, dp / 2 + 6), new Vector3(w * 0.95f, hgt * 0.9f, 12), rot, hull);
            Col(b + rot * new Vector3(0, hgt * 0.45f, dp / 2 + 4), new Vector3(w * 0.8f, hgt * 0.9f, 8), rot);
            for (int e = -1; e <= 1; e++)
            {
                Vector3 ep = b + rot * new Vector3(e * 3.8f, hgt * 0.45f, -dp / 2 - 2.2f);
                k.Cyl(ep, 1.8f, 4.4f, rot * Quaternion.Euler(90, 0, 0), Shade(hull, 0.7f), 10, 2.3f);
                k.Cyl(ep + rot * new Vector3(0, 0, -2.25f), 1.3f, 0.2f, rot * Quaternion.Euler(90, 0, 0), MeshKit.Glow(new Color(1f, 0.45f, 0.15f) * 0.4f), 10);
            }
            Col(b + rot * new Vector3(0, hgt * 0.45f, -dp / 2 - 2.2f), new Vector3(w, 4, 4.4f), rot);
            for (int i = 0; i < 6; i++)
            {
                Vector3 pp = b + rot * new Vector3(R(-0.5f, 0.5f) * w, hgt + 0.5f, R(-0.45f, 0.45f) * dp);
                k.Box(pp, new Vector3(R(3, 6), 0.5f, R(3, 7)), rot * Quaternion.Euler(R(-12, 12), R(-20, 20), R(-12, 12)), i % 2 == 0 ? hull : burnt);
            }
            k.Box(b + rot * new Vector3(-w / 2 - 0.1f, hgt * 0.5f, 0), new Vector3(0.1f, hgt * 0.35f, dp * 0.8f), rot, burnt);
            for (int i = 0; i < 10; i++)
            {
                float a = R(0, Mathf.PI * 2), d = R(20, 34);
                Vector2 q = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                if (OnRoad(q.x, q.y)) continue;
                float s = R(1.2f, 3.5f);
                Vector3 rp = new Vector3(q.x, Height(q.x, q.y), q.y);
                k.Rock(rp + Vector3.up * s * 0.2f, new Vector3(s * 1.4f, s * 0.6f, s), Quaternion.Euler(0, R(0, 360), 0), i % 3 == 0 ? burnt : Shade(hull, R(0.6f, 0.9f)), rng.Next(), 0.4f);
                ColSphere(rp + Vector3.up * s * 0.15f, s * 0.45f);
            }
        }

        // ── sky that follows the region the camera is in ─────────────────
        Material skyMat;
        int skyRegion = -1;
        float regionCheckT;

        void TickBiomeSky(float dt)
        {
            if (!Biomes || !Game.Cam || skyMat == null) return;
            regionCheckT -= dt;
            if (regionCheckT <= 0) { regionCheckT = 0.5f; Vector3 cp = Game.Cam.transform.position; skyRegion = RegionAt(cp.x, cp.z); }
            if (skyRegion < 0) return;
            var t = Info(def.regions[skyRegion].theme);
            float k = 1 - Mathf.Exp(-dt * 0.6f);
            RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, t.fog, k);
            RenderSettings.fogDensity = Mathf.Lerp(RenderSettings.fogDensity, t.fogDensity * 0.6f, k);
            RenderSettings.ambientSkyColor = Color.Lerp(RenderSettings.ambientSkyColor, t.ambientSky, k);
            RenderSettings.ambientGroundColor = Color.Lerp(RenderSettings.ambientGroundColor, t.ambientGround, k);
            RenderSettings.ambientEquatorColor = Color.Lerp(RenderSettings.ambientSkyColor, RenderSettings.ambientGroundColor, 0.5f);
            skyMat.SetColor("_Top", Color.Lerp(skyMat.GetColor("_Top"), t.skyTop, k));
            skyMat.SetColor("_Horizon", Color.Lerp(skyMat.GetColor("_Horizon"), t.skyHorizon, k));
            skyMat.SetColor("_Bottom", Color.Lerp(skyMat.GetColor("_Bottom"), t.skyBottom, k));
            if (Game.I && Game.I.sun) Game.I.sun.color = Color.Lerp(Game.I.sun.color, t.sun, k);
        }

        /// <summary>Royale map: buildings you can enter (where the loot is) drawn on the minimap, vaults in gold.</summary>
        void PaintBuildings()
        {
            int n = minimap.width;
            float step = size / n * 0.5f;
            foreach (var h in houses)
            {
                Color c = h.vault ? new Color(1f, 0.78f, 0.25f) : new Color(0.2f, 0.19f, 0.18f);
                for (float z = -h.d / 2; z <= h.d / 2; z += step)
                    for (float x = -h.w / 2; x <= h.w / 2; x += step)
                    {
                        Vector3 q = h.center + h.rot * new Vector3(x, 0, z);
                        int px = Mathf.FloorToInt((q.x + half) / size * n), py = Mathf.FloorToInt((q.z + half) / size * n);
                        if (px < 0 || py < 0 || px >= n || py >= n) continue;
                        bool edge = Mathf.Abs(x) > h.w / 2 - step * 1.5f || Mathf.Abs(z) > h.d / 2 - step * 1.5f;
                        minimap.SetPixel(px, py, edge ? c : Color.Lerp(c, new Color(0.55f, 0.52f, 0.48f), 0.45f));
                    }
            }
            minimap.Apply();
        }

        public string RegionName(Vector3 p) { int r = RegionAt(p.x, p.z); return r >= 0 ? def.regions[r].name : def.name; }

        /// <summary>A spot at the side of a road (vehicles are parked there), with the road's direction.</summary>
        public bool RandomRoadSpot(System.Random r, out Vector3 p, out Vector3 dir)
        {
            p = Vector3.zero; dir = Vector3.forward;
            if (roads.Count == 0) return false;
            for (int i = 0; i < 12; i++)
            {
                var l = roads[r.Next(roads.Count)];
                float s = (float)r.NextDouble() * l.len;
                Vector2 c = l.At(s), d = l.DirAt(s), side = new Vector2(-d.y, d.x) * (l.w * 0.5f + 3f) * (r.NextDouble() < 0.5 ? 1 : -1);
                Vector2 q = c + side;
                if (Mathf.Abs(q.x) > PlayHalf - 25 || Mathf.Abs(q.y) > PlayHalf - 25) continue;
                p = new Vector3(q.x, 0, q.y);
                if (nav != null && !nav.Walkable(p, true)) continue;
                p.y = Top(q.x, q.y);
                dir = new Vector3(d.x, 0, d.y);
                return true;
            }
            return false;
        }
    }
}
