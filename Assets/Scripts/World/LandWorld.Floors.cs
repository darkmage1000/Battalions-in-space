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
            // a sign of what it is: a parking-deck style band and a light on top
            k.Box(P(0, H + 1.5f, half - 0.3f), new Vector3(4, 1.2f, 0.2f), rot, MeshKit.Glow(new Color(0.4f, 0.8f, 1f) * 0.6f));
            towersBuilt++;
        }
    }
}
