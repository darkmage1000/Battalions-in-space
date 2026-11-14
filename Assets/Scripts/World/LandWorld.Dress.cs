using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    /// <summary>
    /// Village props round every house (PropModels: Kenney's Fantasy Town and Survival kits): barrels or crates by the
    /// door, a lantern post, a cart along a side wall, a fenced garden or a plank pile at the back, now and then a market
    /// stall. Done in one pass after every house exists (BuildLayoutProps), so props can keep clear of all of them.
    /// Each house's props come from a hash of its position, never from the map's random stream, so the rest of the map
    /// is exactly what it was. Props are the same at every graphics level and the solid ones get colliders (cover).
    /// </summary>
    public partial class LandWorld
    {
        struct HousePose { public Vector2 c; public Quaternion rot; public float w, d; public int door; public bool shell, twoStory; public Theme theme; }
        readonly List<HousePose> housePoses = new List<HousePose>();
        readonly List<(Vector2 c, float r)> propSpots = new List<(Vector2 c, float r)>();

        // model units -> metres: the survival kit is drawn at about a third of life size, the town kit at about half
        const float CampScale = 3f, TownScale = 2f;
        // the kits are bright and toy-like: weathered wood for fences and carts, duller barrels
        static readonly Color OldWood = new Color(0.62f, 0.55f, 0.5f), Dull = new Color(0.82f, 0.78f, 0.74f);
        /// <summary>Editor statistics: props tried, then refused for road / water / post, a house, uneven ground, another prop, a tree or rock; placed.</summary>
        public static readonly int[] DressStats = new int[8];

        void NoteHouse(Vector2 c, Quaternion rot, float w, float d, int door, bool shell, bool twoStory) =>
            housePoses.Add(new HousePose { c = c, rot = rot, w = w, d = d, door = door, shell = shell, twoStory = twoStory, theme = here });

        /// <summary>How far a point is outside a house's footprint (negative inside).</summary>
        static float OutsideOf(HousePose h, Vector2 p)
        {
            Vector3 l = Quaternion.Inverse(h.rot) * new Vector3(p.x - h.c.x, 0, p.y - h.c.y);
            float dx = Mathf.Abs(l.x) - h.w / 2, dz = Mathf.Abs(l.z) - h.d / 2;
            if (dx < 0 && dz < 0) return Mathf.Max(dx, dz);
            return new Vector2(Mathf.Max(dx, 0), Mathf.Max(dz, 0)).magnitude;
        }

        /// <summary>
        /// Can a prop with this footprint (half sizes along its own x / z) stand here? Off roads and water, away from posts,
        /// on fairly even ground, outside every house with a walkway left past the others, clear of other props, trees and
        /// rocks. Gives the ground height to stand it on.
        /// </summary>
        bool PropFits(int self, Vector3 centre, float yaw, Vector2 half, float height, out float y)
        {
            y = 0; DressStats[0]++;
            var q = Quaternion.Euler(0, yaw, 0);
            Vector3 ax = q * Vector3.right * half.x, az = q * Vector3.forward * half.y;
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < 5; i++)
            {
                Vector3 p = i == 4 ? centre : centre + ax * (i % 2 == 0 ? -1 : 1) + az * (i < 2 ? -1 : 1);
                if (!Clear(p.x, p.z) || NearPost(p.x, p.z, 9)) { DressStats[1]++; return false; }
                var pp = new Vector2(p.x, p.z);
                for (int h = 0; h < housePoses.Count; h++)
                {
                    float o = OutsideOf(housePoses[h], pp);
                    if (h == self ? o < 0.05f : o < 1.2f) { DressStats[h == self ? 7 : 2]++; return false; }
                }
                float g = Height(p.x, p.z);
                lo = Mathf.Min(lo, g); hi = Mathf.Max(hi, g);
            }
            if (hi - lo > 0.75f) { DressStats[3]++; return false; }
            float r = Mathf.Max(half.x, half.y);
            foreach (var s in propSpots) if ((s.c - new Vector2(centre.x, centre.z)).sqrMagnitude < (s.r + r) * (s.r + r)) { DressStats[4]++; return false; }
            y = (lo + hi) * 0.5f - 0.05f;
            if (Physics.CheckBox(new Vector3(centre.x, y + height / 2 + 0.15f, centre.z), new Vector3(half.x, height / 2, half.y), q, 1 << Layers.Default, QueryTriggerInteraction.Ignore)) { DressStats[5]++; return false; }
            DressStats[6]++;
            propSpots.Add((new Vector2(centre.x, centre.z), r));
            return true;
        }

        void DressHouses(System.Func<float, float, MeshKit> kitAt)
        {
            Physics.SyncTransforms();
            for (int i = 0; i < housePoses.Count; i++) DressHouse(i, kitAt(housePoses[i].c.x, housePoses[i].c.y));
            housePoses.Clear(); propSpots.Clear();
        }

        void DressHouse(int self, MeshKit k)
        {
            var h = housePoses[self];
            var hr = new System.Random(unchecked((int)(Mathf.RoundToInt(h.c.x * 13.7f) * 73856093L ^ Mathf.RoundToInt(h.c.y * 17.3f) * 19349663L)));
            float HR(float a, float b) => a + (b - a) * (float)hr.NextDouble();
            Theme t = h.theme;
            bool rural = t == Theme.Forest || t == Theme.Sky, warm = rural || t == Theme.Desert;
            Vector3 c = new Vector3(h.c.x, 0, h.c.y);
            // side s: 0 = +z, 1 = +x, 2 = -z, 3 = -x (as HouseShell numbers them)
            Vector3 N(int s) => h.rot * (s == 0 ? Vector3.forward : s == 1 ? Vector3.right : s == 2 ? Vector3.back : Vector3.left);
            Vector3 A(int s) => h.rot * (s % 2 == 0 ? Vector3.right : Vector3.forward);
            float Half(int s) => (s % 2 == 0 ? h.d : h.w) / 2;
            float Len(int s) => s % 2 == 0 ? h.w : h.d;
            Vector3 At(int s, float u, float o) => c + N(s) * (Half(s) + o) + A(s) * u;
            float YawOf(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float shade = HR(0.85f, 1.08f);

            int door = h.door, back = (h.door + 2) % 4, side = (h.door + (hr.Next(2) == 0 ? 1 : 3)) % 4;
            float gap = 1.1f, L = Len(door);
            float clusterSide = hr.Next(2) == 0 ? -1 : 1;

            // 1. by the door: barrels, a crate stack or (now and then, in a village) a market stall
            if (hr.NextDouble() < 0.8)
            {
                float u = clusterSide * Mathf.Min(gap + HR(1.0f, 1.8f), L / 2 - 0.9f);
                bool stall = warm && hr.NextDouble() < 0.18;
                if (stall)
                {
                    Vector3 p = At(door, u, 1.5f);
                    float yaw = YawOf(N(door));
                    if (PropFits(self, p, yaw, new Vector2(1.15f, 1.15f), 2.6f, out float y))
                    {
                        PropModels.Add(k, hr.Next(2) == 0 ? "Town/stall-red" : "Town/stall-green", new Vector3(p.x, y, p.z), yaw + 180, TownScale * 1.1f, shade);
                        Col(new Vector3(p.x, y + 0.55f, p.z) + N(door) * 0.3f, new Vector3(2.3f, 1.1f, 0.6f), Quaternion.Euler(0, yaw, 0));   // the counter
                    }
                }
                else
                {
                    Vector3 p = At(door, u, 0.62f);
                    float yaw = YawOf(A(door));
                    bool barrels = t == Theme.Forest || t == Theme.Snow || (t != Theme.Urban && hr.NextDouble() < 0.5);
                    if (PropFits(self, p, yaw, new Vector2(0.45f, 1.15f), 1.2f, out float y))
                    {
                        Vector3 g = new Vector3(p.x, y, p.z), a = A(door);
                        if (barrels)
                        {
                            int n = 1 + hr.Next(2);
                            for (int i = 0; i < n; i++)
                                PropModels.Add(k, hr.NextDouble() < 0.3 ? "Camp/barrel-open" : "Camp/barrel", g + a * ((i - (n - 1) * 0.5f) * 0.76f), HR(0, 360), CampScale, shade, Dull);
                            Col(g + Vector3.up * 0.5f, new Vector3(0.74f, 1.0f, 0.76f * n), Quaternion.Euler(0, yaw, 0));
                        }
                        else
                        {
                            PropModels.Add(k, hr.NextDouble() < 0.3 ? "Camp/box-large-open" : "Camp/box-large", g, yaw + (hr.Next(2) * 180), CampScale, shade);
                            bool top = hr.NextDouble() < 0.6;
                            if (top) PropModels.Add(k, "Camp/box", g + a * HR(-0.3f, 0.3f) + Vector3.up * 0.75f, yaw + HR(-25, 25), CampScale, shade);
                            Col(g + Vector3.up * (top ? 0.75f : 0.38f), new Vector3(0.76f, top ? 1.5f : 0.76f, 1.5f), Quaternion.Euler(0, yaw, 0));
                        }
                    }
                }
            }

            // 2. a lantern post on the other side of the door (villages, desert towns)
            if (warm && hr.NextDouble() < 0.45)
            {
                float u = -clusterSide * Mathf.Min(gap + 0.55f, L / 2 - 0.4f);
                Vector3 p = At(door, u, 0.45f);
                if (PropFits(self, p, 0, new Vector2(0.2f, 0.2f), 2.6f, out float y))
                {
                    PropModels.Add(k, "Town/lantern", new Vector3(p.x, y, p.z), YawOf(N(door)), TownScale * 0.85f, shade);
                    ColCap(new Vector3(p.x, y, p.z), 0.14f, 2.6f);
                }
            }

            // 3. a cart along a side wall
            if (t != Theme.Urban && t != Theme.Volcanic && hr.NextDouble() < 0.35)
            {
                float Ls = Len(side);
                float u = HR(-1, 1) * Mathf.Max(0, Ls / 2 - 1.8f);
                Vector3 p = At(side, u, 1.35f);
                float yaw = YawOf(A(side)) + (hr.Next(2) == 0 ? 0 : 180);
                if (PropFits(self, p, yaw, new Vector2(0.95f, 1.4f), 1.3f, out float y))
                {
                    PropModels.Add(k, hr.NextDouble() < 0.3 ? "Town/cart-high" : "Town/cart", new Vector3(p.x, y, p.z), yaw, TownScale, shade, OldWood);
                    Col(new Vector3(p.x, y + 0.6f, p.z), new Vector3(1.75f, 1.2f, 2.6f), Quaternion.Euler(0, yaw, 0));
                }
            }

            // 4. at the back (not where a two-storey house has its outside stairs): a fenced garden in the villages,
            //    otherwise a pile of planks, a lean-to awning in the desert or a stack of crates
            if (h.twoStory) return;
            float Lb = Len(back);
            if (rural && hr.NextDouble() < 0.5)
            {
                float depth = HR(2.8f, 3.8f);
                Vector3 mid = At(back, 0, depth / 2 + 0.15f);
                if (PropFits(self, mid, YawOf(A(back)), new Vector2(depth / 2, Lb / 2), 1f, out float y))
                {
                    // three runs: corner out, across the back, back in to the other corner
                    Vector3 c0 = At(back, -Lb / 2 + 0.1f, 0.15f), c1 = At(back, -Lb / 2 + 0.1f, depth), c2 = At(back, Lb / 2 - 0.1f, depth), c3 = At(back, Lb / 2 - 0.1f, 0.15f);
                    void Run(Vector3 a, Vector3 b, bool gate)
                    {
                        Vector3 d = b - a; d.y = 0; float len = d.magnitude;
                        int n = Mathf.Max(1, Mathf.RoundToInt(len / 2.4f)); float seg = len / n;
                        for (int i = 0; i < n; i++)
                        {
                            Vector3 sp = a + d * ((i + 0.5f) / n); sp.y = Height(sp.x, sp.z) - 0.05f;
                            string m = gate && i == n / 2 ? "Town/fence-gate" : hr.NextDouble() < 0.08 ? "Town/fence-broken" : "Town/fence";
                            PropModels.Add(k, m, sp, YawOf(d), new Vector3(TownScale * 1.2f, TownScale * 1.25f, seg), shade, OldWood);
                        }
                        Vector3 cm = (a + b) / 2; cm.y = Height(cm.x, cm.z);
                        Col(cm + Vector3.up * 0.5f, new Vector3(0.18f, 1.0f, len), Quaternion.LookRotation(d));
                    }
                    Run(c0, c1, false); Run(c1, c2, true); Run(c2, c3, false);
                    // something left in the garden
                    Vector3 g = At(back, HR(-Lb / 4, Lb / 4), depth * 0.55f); g.y = Height(g.x, g.z);
                    if (hr.NextDouble() < 0.5) PropModels.Add(k, "Camp/workbench", g, YawOf(N(back)), CampScale, shade);
                    else PropModels.Add(k, "Camp/resource-planks", g, YawOf(A(back)) + HR(-20, 20), CampScale, shade);
                }
            }
            else if (hr.NextDouble() < 0.45)
            {
                float u = HR(-1, 1) * Mathf.Max(0, Lb / 2 - 1.4f);
                if (t == Theme.Desert && hr.NextDouble() < 0.5)
                {
                    Vector3 p = At(back, u, 1.0f);
                    if (PropFits(self, p, YawOf(N(back)), new Vector2(0.9f, 0.9f), 1.6f, out float y))
                    {
                        PropModels.Add(k, "Camp/tent-canvas", new Vector3(p.x, y, p.z), YawOf(N(back)) + 180, CampScale * 1.15f, shade);
                        PropModels.Add(k, "Camp/chest", new Vector3(p.x, y, p.z) + A(back) * 0.3f, YawOf(N(back)), CampScale, shade);
                    }
                }
                else if (t == Theme.Forest || t == Theme.Snow || t == Theme.Sky)
                {
                    Vector3 p = At(back, u, 0.85f);
                    float yaw = YawOf(A(back));
                    if (PropFits(self, p, yaw, new Vector2(0.6f, 1.0f), 0.6f, out float y))
                    {
                        Vector3 g = new Vector3(p.x, y, p.z);
                        PropModels.Add(k, "Camp/resource-planks", g, yaw + HR(-8, 8), CampScale, shade);
                        if (hr.NextDouble() < 0.5) PropModels.Add(k, "Camp/resource-planks", g + Vector3.up * 0.27f, yaw + HR(-12, 12), CampScale, shade);
                        Col(g + Vector3.up * 0.3f, new Vector3(1.1f, 0.6f, 1.9f), Quaternion.Euler(0, yaw, 0));
                    }
                }
                else
                {
                    Vector3 p = At(back, u, 0.62f);
                    float yaw = YawOf(A(back));
                    if (PropFits(self, p, yaw, new Vector2(0.45f, 1.15f), 1.5f, out float y))
                    {
                        Vector3 g = new Vector3(p.x, y, p.z);
                        PropModels.Add(k, "Camp/box-large", g, yaw, CampScale, shade);
                        PropModels.Add(k, "Camp/box", g + A(back) * HR(-0.35f, 0.35f) + Vector3.up * 0.75f, yaw + HR(-20, 20), CampScale, shade);
                        Col(g + Vector3.up * 0.75f, new Vector3(0.76f, 1.5f, 1.5f), Quaternion.Euler(0, yaw, 0));
                    }
                }
            }
        }
    }
}
