using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    /// <summary>
    /// Buildings with more than one floor (battle royale): two-storey houses with rooms (an inner wall with a doorway on
    /// each floor, an outside staircase up to the top floor's door) and open concrete towers (floors on columns, open
    /// sides with waist-high parapets, a ramp up to every floor, a roof deck) for high ground.
    /// Upper floors are kept out of `houses` (the bot walk grid has one height per cell); they are in `upperFloors`
    /// for loot.
    /// </summary>
    public partial class LandWorld
    {
        public readonly List<House> upperFloors = new List<House>();
        public int towersBuilt, twoStoreysBuilt;

        /// <summary>A walking route up a building for bots: node 0 is on the ground outside, then one point after another
        /// (stair foot, stair top, across the floor...) - every upper floor is one of the nodes.</summary>
        public class Climb
        {
            public Vector3[] nodes;
            public bool tower;
            public Vector3 c;
            public float reach;                         // how far from c the building goes (for finding it)
            public readonly List<UpperFloor> levels = new List<UpperFloor>();
            public Unit holder; public float holdUntil; // a bot keeping the top of a tower
            public UpperFloor Top => levels[levels.Count - 1];
        }

        /// <summary>One floor above the ground: its slab (for "is this point on it") and its node on the climb.</summary>
        public class UpperFloor
        {
            public Climb climb;
            public int node;
            public int lo, hi;                           // the route nodes that are on (or start from) this floor
            public Vector3 c;                            // middle, at the floor's top
            public Quaternion rot;
            public float hx, hz;                         // half size of the slab
            public float holeX = 999, holeHalf, holeZ0, holeZ1;   // a stairwell hole (towers)
            public float divX = 999, gapZ;                         // an inner wall across x = divX with a doorway at z = gapZ (houses)
            public float LocalX(Vector3 p) => (Quaternion.Inverse(rot) * (p - c)).x;
            public bool Contains(Vector3 p, float inset = 0, float below = 0.8f, float above = 2.4f)
            {
                if (p.y < c.y - below || p.y > c.y + above) return false;
                Vector3 l = Quaternion.Inverse(rot) * (p - c);
                if (Mathf.Abs(l.x) > hx - inset || Mathf.Abs(l.z) > hz - inset) return false;
                if (Mathf.Abs(l.x - holeX) < holeHalf + inset && l.z > holeZ0 - inset && l.z < holeZ1 + inset) return false;
                return true;
            }
            public Vector3 World(float x, float z) => c + rot * new Vector3(x, 0, z);
        }

        public readonly List<Climb> climbs = new List<Climb>();
        public readonly List<UpperFloor> floorsUp = new List<UpperFloor>();

        /// <summary>The upper floor a point stands on (null on the ground or anywhere else).</summary>
        public UpperFloor FloorAt(Vector3 p)
        {
            foreach (var cl in climbs)
            {
                float dx = p.x - cl.c.x, dz = p.z - cl.c.z;
                if (dx * dx + dz * dz > cl.reach * cl.reach) continue;
                foreach (var f in cl.levels) if (f.Contains(p)) return f;
            }
            return null;
        }

        /// <summary>The climb whose route passes closest to p (within `max` metres of a leg, heights counted double) and
        /// that leg: from node `seg` to node `seg + 1`.</summary>
        public Climb ClimbNear(Vector3 p, float max, out int seg)
        {
            seg = -1; Climb best = null; float bd = max * max;
            Vector3 ps = new Vector3(p.x, p.y * 2, p.z);
            foreach (var cl in climbs)
            {
                float dx = p.x - cl.c.x, dz = p.z - cl.c.z;
                if (dx * dx + dz * dz > (cl.reach + max) * (cl.reach + max)) continue;
                for (int i = 0; i < cl.nodes.Length - 1; i++)
                {
                    Vector3 a = cl.nodes[i], b = cl.nodes[i + 1];
                    a.y *= 2; b.y *= 2;
                    Vector3 ab = b - a;
                    float t = ab.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector3.Dot(ps - a, ab) / ab.sqrMagnitude) : 0;
                    float d2 = (a + ab * t - ps).sqrMagnitude;
                    if (d2 < bd) { bd = d2; best = cl; seg = i; }
                }
            }
            return best;
        }

        /// <summary>A wall across the inside of a floor with a doorway, making two rooms.</summary>
        void InnerWall(MeshKit k, Vector3 floor, Quaternion rot, float w, float dp, float hh, Color col)
        {
            float x = w * 0.18f, gapZ = -dp * 0.12f, gap = 1.7f;
            float z0 = -dp / 2 + 0.45f, z1 = dp / 2 - 0.45f;
            void Seg(float a, float b)
            {
                if (b - a < 0.3f) return;
                Vector3 c = floor + rot * new Vector3(x, hh / 2, (a + b) / 2);
                k.Box(c, new Vector3(0.3f, hh, b - a), rot, col);
                Col(c, new Vector3(0.3f, hh, b - a), rot);
            }
            Seg(z0, gapZ - gap / 2);
            Seg(gapZ + gap / 2, z1);
            Vector3 lintel = floor + rot * new Vector3(x, hh - 0.4f, gapZ);
            k.Box(lintel, new Vector3(0.3f, 0.8f, gap), rot, col);
        }

        /// <summary>Two floors: the ground floor (enterable, rooms) and an upper floor reached by an outside staircase
        /// at the back, also split into rooms.</summary>
        House TwoStory(MeshKit k, Vector3 p, Quaternion rot, float w, float dp, float lowGround, int door, Color? wallCol = null)
        {
            const float FH = 4f;
            Color col = (wallCol ?? WallColor); col.a = 1;
            var ground = HouseShell(k, p, rot, w, dp, lowGround, door, 2.2f, FH, true, wallCol, false);
            if (ground == null) return null;
            InnerWall(k, p + Vector3.up * 0.2f, rot, w, dp, FH, Shade(col, 0.92f));
            Vector3 up = p + Vector3.up * (FH + 0.4f);
            var upper = HouseShell(k, up, rot, w, dp, up.y, (door + 2) % 4, 1.8f, 3.6f, true, wallCol, true);
            if (upper != null)
            {
                houses.Remove(upper);       // the walk grid only knows the ground floor
                upperFloors.Add(upper);
                var cl = new Climb { c = upper.center, reach = Mathf.Max(w, dp) * 0.75f + 10 };
                var uf = new UpperFloor { climb = cl, c = upper.center, rot = rot, hx = w / 2 - 0.3f, hz = dp / 2 - 0.3f, divX = w * 0.18f, gapZ = -dp * 0.12f };
                // a door on the +x side opens into the small room: through the inner doorway to the middle
                if ((door + 2) % 4 == 1) cl.nodes = new[] { upper.doorOut, upper.door, upper.doorIn, uf.World(uf.divX + 0.9f, uf.gapZ), uf.World(uf.divX - 0.9f, uf.gapZ), upper.center };
                else cl.nodes = new[] { upper.doorOut, upper.door, upper.doorIn, upper.center };
                uf.node = cl.nodes.Length - 1; uf.lo = 1; uf.hi = uf.node;
                cl.levels.Add(uf); climbs.Add(cl); floorsUp.Add(uf);
            }
            InnerWall(k, up + Vector3.up * 0.2f, rot, w, dp, 3.6f, Shade(col, 0.92f));
            twoStoreysBuilt++;
            return ground;
        }

        /// <summary>
        /// An open tower: `floors` storeys of concrete slabs on columns, sides open behind waist-high parapets, a ramp up
        /// to each floor (alternating sides so they never stack) and a roof deck with a parapet - high ground.
        /// </summary>
        void OpenTower(Vector2 c, float size, int floors, System.Func<float, float, MeshKit> kitAt, List<(Vector2 c, float r)> placed)
        {
            if (!PropSpot(c.x, c.y, 26)) return;
            foreach (var pl in placed) if (Vector2.Distance(pl.c, c) < pl.r + size * 0.72f + 2) return;
            float W = size, half = W / 2, FH = 4.2f, sw = 2.6f, land = 2.2f, L = W - 2 * land;
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < 4; i++) { float hh = Height(c.x + (i % 2 == 0 ? -half : half), c.y + (i < 2 ? -half : half)); lo = Mathf.Min(lo, hh); hi = Mathf.Max(hi, hh); }
            if (hi - lo > 3f) { CityTower(c, W, W, R(20, 40), kitAt, placed); return; }
            placed.Add((c, W * 0.72f));
            var k = kitAt(c.x, c.y);
            var rot = Quaternion.Euler(0, rng.Next(4) * 90, 0);
            Vector3 b = new Vector3(c.x, hi, c.y);
            Color con = Color.Lerp(new Color(0.58f, 0.57f, 0.55f), new Color(0.5f, 0.52f, 0.56f), R()), dark = Shade(con, 0.7f), stripe = new Color(0.85f, 0.65f, 0.15f);
            Vector3 P(float x, float y, float z) => b + rot * new Vector3(x, y, z);
            void Solid(Vector3 local, Vector3 sz, Color col, bool ground)
            {
                Vector3 wc = P(local.x, local.y, local.z);
                k.Box(wc, sz, rot, col);
                var go = new GameObject(ground ? "floor" : "c"); go.transform.SetParent(colRoot, false);
                go.transform.SetPositionAndRotation(wc, rot);
                go.AddComponent<BoxCollider>().size = sz;
                if (ground) go.layer = Layers.Ground;
            }
            // footing to the lowest ground, and the ground slab
            float foot = hi - lo + 0.6f;
            Solid(new Vector3(0, 0.2f - foot / 2, 0), new Vector3(W, foot, W), dark, true);
            float H = floors * FH;
            // columns at the corners and halfway along each side
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                {
                    if (j == 0) continue;      // none halfway along the ramp sides
                    Solid(new Vector3(i * (half - 0.5f), H / 2 + 0.2f, j * (half - 0.5f)), new Vector3(0.9f, H, 0.9f), con, false);
                }
            for (int f = 1; f <= floors; f++)
            {
                float y = f * FH;
                // the ramp that arrives here comes up the -x side on odd floors and the +x side on even ones
                int arriveSide = (f - 1) % 2 == 0 ? -1 : 1;
                float stripX = arriveSide * (half - sw / 2);
                // slab: the middle, the side strip that has no ramp hole, and the landing at the top of the ramp
                Solid(new Vector3(0, y, 0), new Vector3(W - 2 * sw, 0.4f, W), con, true);
                Solid(new Vector3(-stripX, y, 0), new Vector3(sw, 0.4f, W), con, true);
                Solid(new Vector3(stripX, y, L / 2 + land / 2), new Vector3(sw, 0.4f, land), con, true);
                k.Box(P(0, y - 0.25f, half - 0.05f), new Vector3(W, 0.15f, 0.1f), rot, stripe);
                // parapets along the open sides (with gaps), waist high: cover for whoever holds the floor
                float py = y + 0.7f;
                for (int s = 0; s < 4; s++)
                {
                    Vector3 n = s == 0 ? Vector3.forward : s == 1 ? Vector3.right : s == 2 ? Vector3.back : Vector3.left;
                    Vector3 along = s % 2 == 0 ? Vector3.right : Vector3.forward;
                    for (int seg = -1; seg <= 1; seg += 2)
                    {
                        Vector3 mid = n * (half - 0.2f) + along * seg * W * 0.27f;
                        Vector3 sz = s % 2 == 0 ? new Vector3(W * 0.36f, 1.1f, 0.3f) : new Vector3(0.3f, 1.1f, W * 0.36f);
                        // keep the stairwell side open where the ramp below arrives (the hole) - its parapet would block nothing
                        Solid(new Vector3(mid.x, py, mid.z), sz, dark, false);
                    }
                }
                // the ramp from the floor below up to this one, rising toward +z along the strip
                float rampLen = Mathf.Sqrt(L * L + FH * FH);
                float pitch = -Mathf.Atan2(FH, L) * Mathf.Rad2Deg;
                var rq = rot * Quaternion.Euler(pitch, 0, 0);
                Vector3 rc = P(stripX, y - FH / 2 + 0.05f, 0);
                k.Box(rc, new Vector3(sw - 0.3f, 0.3f, rampLen), rq, dark);
                var rg = new GameObject("ramp"); rg.transform.SetParent(colRoot, false); rg.transform.SetPositionAndRotation(rc, rq);
                rg.AddComponent<BoxCollider>().size = new Vector3(sw - 0.3f, 0.3f, rampLen); rg.layer = Layers.Ground;
                for (int st = 0; st < 10; st++)
                {
                    float t = (st + 0.5f) / 10f;
                    k.Box(P(stripX, y - FH + t * FH + 0.12f, -L / 2 + t * L), new Vector3(sw - 0.5f, 0.05f, 0.25f), rot, stripe * 0.8f);
                }
                // loot spot on this floor (the middle part, clear of the stairwell strips)
                var hs = new House { center = P(0, y + 0.2f, 0), rot = rot, w = W - 2 * sw - 1, d = W - 3 };
                hs.door = hs.doorIn = hs.doorOut = hs.center;
                upperFloors.Add(hs);
            }
            // the bots' route up: in from outside to the first ramp's foot, then each ramp and across each floor to the next
            {
                var nodes = new List<Vector3>();
                float sIn = -(half - sw / 2) + 0.4f;           // the first ramp's strip (-x), a little in from its edge
                Vector3 eZ = P(sIn, 0, -half - 1.6f), eX = P(-half - 1.6f, 0, -half + 1.5f);
                eZ.y = Height(eZ.x, eZ.z); eX.y = Height(eX.x, eX.z);
                nodes.Add(Mathf.Abs(eZ.y - (hi + 0.2f)) <= Mathf.Abs(eX.y - (hi + 0.2f)) ? eZ : eX);
                var cl = new Climb { tower = true, c = b, reach = half * 1.45f + 4 };
                for (int f = 1; f <= floors; f++)
                {
                    int side = (f - 1) % 2 == 0 ? -1 : 1;
                    float s = side * (half - sw / 2), s2 = s - side * 0.4f;
                    float y0 = (f - 1) * FH + 0.2f, y1 = f * FH + 0.2f;
                    nodes.Add(P(s2, y0, -half + 1.5f));              // foot of the ramp
                    nodes.Add(P(s2, y1, half - 1.2f));               // top of the ramp
                    nodes.Add(P(0.3f * s, y1, half - 2.0f));         // off the landing, clear of the stairwell
                    var uf = new UpperFloor { climb = cl, node = nodes.Count, c = P(0, y1, 0), rot = rot, hx = half - 0.35f, hz = half - 0.35f };
                    if (f > 0) { uf.holeX = s; uf.holeHalf = sw / 2; uf.holeZ0 = -half - 1; uf.holeZ1 = half - land; }
                    nodes.Add(uf.c);                                 // the middle of the floor
                    uf.lo = uf.node - 2; uf.hi = f < floors ? uf.node + 3 : uf.node;
                    cl.levels.Add(uf); floorsUp.Add(uf);
                    if (f < floors) nodes.Add(P(-0.3f * s, y1, -half + 2.0f));   // toward the next ramp's foot
                }
                cl.nodes = nodes.ToArray();
                climbs.Add(cl);
            }
            // a sign of what it is: a parking-deck style band and a light on top
            k.Box(P(0, H + 1.5f, half - 0.3f), new Vector3(4, 1.2f, 0.2f), rot, MeshKit.Glow(new Color(0.4f, 0.8f, 1f) * 0.6f));
            towersBuilt++;
        }
    }
}
