using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    /// <summary>A bot's current route; kept between frames so paths are planned now and then, not every frame.</summary>
    public class NavAgent
    {
        public readonly List<Vector3> path = new List<Vector3>();
        public int idx;
        public Vector3 goal;
        public float replanAt, skipCheckAt;
        public bool straight, has, vehicle;
    }

    /// <summary>
    /// Walkability grid over a land map (4 m cells) for infantry and for ground vehicles, built once from the
    /// terrain, bridges and every static collider. Bots ask Steer() for a direction: straight at the goal when
    /// the way is clear, otherwise along an A* route round ridges, rivers, lava, buildings and forests.
    /// </summary>
    public class Nav
    {
        public const float Cell = 4f;
        readonly LandWorld land;
        readonly int n;
        readonly float x0;
        readonly float[] top;
        readonly byte[] foot, veh;      // 0 = blocked, otherwise move cost x10
        const float FootRise = 0.95f, VehRise = 0.62f;   // max height change per metre between cells
        public int blockedFoot, blockedVeh;

        static int plansFrame = -1, plansThisFrame;
        const int PlansPerFrame = 3;

        public Nav(LandWorld l)
        {
            land = l;
            x0 = -l.PlayHalf;
            n = Mathf.CeilToInt(l.PlayHalf * 2 / Cell);
            top = new float[n * n]; foot = new byte[n * n]; veh = new byte[n * n];
            const int obstacles = (1 << Layers.Default) | (1 << Layers.Hull);
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int k = j * n + i;
                    float x = x0 + (i + 0.5f) * Cell, z = x0 + (j + 0.5f) * Cell;
                    float y = l.Top(x, z);
                    top[k] = y;
                    byte f = 10, v = 10;
                    if (l.LiquidAt(x, z, out float lt, out bool lava) && y < lt + 0.05f && Mathf.Abs(y - l.Height(x, z)) < 0.01f)
                    {
                        if (lava || lt - y > 1.25f) { f = 0; v = lava || lt - y > 1.6f ? (byte)0 : (byte)30; }
                        else { f = 40; v = 30; }
                    }
                    Vector3 p = new Vector3(x, y, z);
                    if (f > 0 && Physics.CheckCapsule(p + Vector3.up * 0.7f, p + Vector3.up * 1.7f, 0.8f, obstacles, QueryTriggerInteraction.Ignore)) f = 0;
                    if (v > 0 && Physics.CheckCapsule(p + Vector3.up * 1.2f, p + Vector3.up * 2.4f, 2.3f, obstacles, QueryTriggerInteraction.Ignore)) v = 0;
                    foot[k] = f; veh[k] = v;
                }
            // keep routes a little off walls
            var f2 = (byte[])foot.Clone(); var v2 = (byte[])veh.Clone();
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int k = j * n + i;
                    if (foot[k] > 0 && NearBlocked(foot, i, j)) f2[k] = (byte)Mathf.Min(250, foot[k] + 12);
                    if (veh[k] > 0 && NearBlocked(veh, i, j)) v2[k] = (byte)Mathf.Min(250, veh[k] + 20);
                    if (foot[k] == 0) blockedFoot++;
                    if (veh[k] == 0) blockedVeh++;
                }
            System.Array.Copy(f2, foot, foot.Length); System.Array.Copy(v2, veh, veh.Length);
            OpenHouses(l);
        }

        // buildings you can walk into: cells inside them and the doorway are opened by hand, and every step that
        // touches one of those cells must not pass through a wall (checked with a ray), so routes use the door
        readonly HashSet<int> houseCells = new HashSet<int>();
        readonly Dictionary<int, Vector3> cellPos = new Dictionary<int, Vector3>();
        const int WallMask = (1 << Layers.Default) | (1 << Layers.Hull);
        public int houseCount;
        readonly List<(Vector2 c, float r)> houseZones = new List<(Vector2, float)>();

        void OpenHouses(LandWorld l)
        {
            foreach (var h in l.houses)
            {
                houseCount++;
                houseZones.Add((new Vector2(h.center.x, h.center.z), Mathf.Sqrt(h.w * h.w + h.d * h.d) * 0.5f + 1.5f));
                var inv = Quaternion.Inverse(h.rot);
                // interior cells
                float r = Mathf.Max(h.w, h.d);
                for (float dz = -r; dz <= r; dz += Cell * 0.5f)
                    for (float dx = -r; dx <= r; dx += Cell * 0.5f)
                    {
                        Vector3 q = h.center + new Vector3(dx, 0, dz);
                        Vector3 lq = inv * (q - h.center);
                        if (Mathf.Abs(lq.x) > h.w / 2 - 0.9f || Mathf.Abs(lq.z) > h.d / 2 - 0.9f) continue;
                        int k = CellOf(q);
                        if (k < 0 || houseCells.Contains(k)) continue;
                        // the cell centre may lie outside the building: put the cell's point inside it
                        Vector3 cc = Center(k); Vector3 lc = inv * (cc - h.center);
                        lc.x = Mathf.Clamp(lc.x, -h.w / 2 + 1, h.w / 2 - 1); lc.z = Mathf.Clamp(lc.z, -h.d / 2 + 1, h.d / 2 - 1);
                        Vector3 inside = h.center + h.rot * lc; inside.y = h.center.y;
                        houseCells.Add(k); cellPos[k] = inside; foot[k] = 12; top[k] = h.center.y;
                    }
                foreach (var pt in new[] { h.doorIn, h.door, h.doorOut })
                {
                    int k = CellOf(pt);
                    if (k < 0) continue;
                    houseCells.Add(k); cellPos[k] = pt; foot[k] = 12; top[k] = pt.y;
                }
            }
        }

        Vector3 CellPoint(int k) => cellPos.TryGetValue(k, out var p) ? p : Center(k);

        bool NearBlocked(byte[] g, int i, int j)
        {
            for (int dj = -1; dj <= 1; dj++)
                for (int di = -1; di <= 1; di++)
                {
                    int a = i + di, b = j + dj;
                    if (a < 0 || b < 0 || a >= n || b >= n || g[b * n + a] == 0) return true;
                    if (Mathf.Abs(top[b * n + a] - top[j * n + i]) > (g == foot ? FootRise : VehRise) * Cell * Mathf.Sqrt(di * di + dj * dj)) return true;
                }
            return false;
        }

        int CellOf(Vector3 p)
        {
            int i = Mathf.FloorToInt((p.x - x0) / Cell), j = Mathf.FloorToInt((p.z - x0) / Cell);
            if (i < 0 || j < 0 || i >= n || j >= n) return -1;
            return j * n + i;
        }
        Vector3 Center(int k) => new Vector3(x0 + (k % n + 0.5f) * Cell, top[k], x0 + (k / n + 0.5f) * Cell);

        public bool Walkable(Vector3 p, bool vehicle = false) { int k = CellOf(p); return k >= 0 && (vehicle ? veh : foot)[k] > 0; }

        bool Step(byte[] g, int a, int b)
        {
            if (g[b] == 0) return false;
            int ai = a % n, aj = a / n, bi = b % n, bj = b / n;
            float dist = Cell * Mathf.Sqrt((ai - bi) * (ai - bi) + (aj - bj) * (aj - bj));
            if (Mathf.Abs(top[b] - top[a]) > (g == foot ? FootRise : VehRise) * dist) return false;
            if (houseCells.Count > 0 && (houseCells.Contains(a) || houseCells.Contains(b)))
            {
                if (g != foot) return false;
                Vector3 pa = CellPoint(a) + Vector3.up * 0.9f, pb = CellPoint(b) + Vector3.up * 0.9f;
                if (Physics.Linecast(pa, pb, WallMask, QueryTriggerInteraction.Ignore)) return false;
            }
            return true;
        }

        /// <summary>Nearest open cell to p within a few cells (for goals that land in a rock or a wall).</summary>
        int Snap(byte[] g, Vector3 p, int range = 6)
        {
            int k = CellOf(new Vector3(Mathf.Clamp(p.x, x0 + 1, -x0 - 1), 0, Mathf.Clamp(p.z, x0 + 1, -x0 - 1)));
            if (k < 0) return -1;
            if (g[k] > 0) return k;
            int ci = k % n, cj = k / n, best = -1; float bd = float.MaxValue;
            for (int r = 1; r <= range && best < 0; r++)
                for (int dj = -r; dj <= r; dj++)
                    for (int di = -r; di <= r; di++)
                    {
                        if (Mathf.Abs(di) != r && Mathf.Abs(dj) != r) continue;
                        int a = ci + di, b = cj + dj;
                        if (a < 0 || b < 0 || a >= n || b >= n) continue;
                        int q = b * n + a;
                        if (g[q] == 0) continue;
                        float d = di * di + dj * dj;
                        if (d < bd) { bd = d; best = q; }
                    }
            return best;
        }

        public Vector3 SnapPoint(Vector3 p, bool vehicle = false)
        {
            int k = Snap(vehicle ? veh : foot, p, 10);
            return k >= 0 ? Center(k) : p;
        }

        /// <summary>True if a walker can go in a straight line from a to b (sampled every 2 m on the grid).</summary>
        public bool LineClear(Vector3 a, Vector3 b, bool vehicle)
        {
            var g = vehicle ? veh : foot;
            int prev = CellOf(a);
            if (prev < 0) return false;
            Vector3 d = b - a; d.y = 0;
            float len = d.magnitude;
            int steps = Mathf.CeilToInt(len / 2f);
            // a line that passes a building must not go through its walls (one sphere cast along the whole line)
            Vector2 a2 = new Vector2(a.x, a.z), b2 = new Vector2(b.x, b.z);
            foreach (var hz in houseZones)
            {
                Vector2 ab = b2 - a2; float tt = ab.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector2.Dot(hz.c - a2, ab) / ab.sqrMagnitude) : 0;
                if ((a2 + ab * tt - hz.c).sqrMagnitude > hz.r * hz.r) continue;
                int kb = CellOf(b); float ya = top[prev] + 0.9f, yb = (kb >= 0 ? top[kb] : top[prev]) + 0.9f;
                Vector3 pa = new Vector3(a.x, ya, a.z), pb = new Vector3(b.x, yb, b.z);
                if (Physics.SphereCast(pa, 0.35f, pb - pa, out _, (pb - pa).magnitude, WallMask, QueryTriggerInteraction.Ignore)) return false;
                break;
            }
            Vector3 prevP = a;
            for (int s = 1; s <= steps; s++)
            {
                Vector3 q = a + d * (s / (float)steps);
                int k = CellOf(q);
                if (k < 0) return false;
                if (k == prev) continue;
                if (g[k] == 0 || g[k] >= 40 || !Step(g, prev, k)) return false;
                // near a building the real line matters, not the cell points: it must not clip a wall
                if (houseCells.Count > 0 && (houseCells.Contains(prev) || houseCells.Contains(k)))
                {
                    Vector3 pa = new Vector3(prevP.x, top[prev] + 0.9f, prevP.z), pb = new Vector3(q.x, top[k] + 0.9f, q.z);
                    if (Physics.SphereCast(pa, 0.35f, pb - pa, out _, (pb - pa).magnitude, WallMask, QueryTriggerInteraction.Ignore)) return false;
                }
                prev = k; prevP = q;
            }
            return true;
        }

        // A* scratch
        float[] gScore; int[] from; int[] stamp; int curStamp;
        readonly List<(float f, int k)> heap = new List<(float, int)>();

        bool AStar(Vector3 a, Vector3 b, bool vehicle, List<Vector3> outPath)
        {
            var g = vehicle ? veh : foot;
            int s = Snap(g, a), t = Snap(g, b);
            if (s < 0 || t < 0) return false;
            if (gScore == null) { gScore = new float[n * n]; from = new int[n * n]; stamp = new int[n * n]; }
            curStamp++;
            heap.Clear();
            gScore[s] = 0; from[s] = -1; stamp[s] = curStamp;
            Push(H(s, t), s);
            int ti = t % n, tj = t / n, expanded = 0;
            bool found = false;
            while (heap.Count > 0 && expanded < 9000)
            {
                var (fcur, k) = Pop();
                if (k == t) { found = true; break; }
                expanded++;
                int ci = k % n, cj = k / n;
                float gk = gScore[k];
                if (fcur - H(k, t) > gk + 0.01f) continue;
                for (int dj = -1; dj <= 1; dj++)
                    for (int di = -1; di <= 1; di++)
                    {
                        if (di == 0 && dj == 0) continue;
                        int a2 = ci + di, b2 = cj + dj;
                        if (a2 < 0 || b2 < 0 || a2 >= n || b2 >= n) continue;
                        int q = b2 * n + a2;
                        if (!Step(g, k, q)) continue;
                        if (di != 0 && dj != 0 && (g[cj * n + a2] == 0 || g[b2 * n + ci] == 0)) continue;   // no corner cutting
                        float c = (di != 0 && dj != 0 ? 1.414f : 1f) * g[q] * 0.1f;
                        float ng = gk + c;
                        if (stamp[q] == curStamp && ng >= gScore[q]) continue;
                        stamp[q] = curStamp; gScore[q] = ng; from[q] = k;
                        int qi = q % n, qj = q / n;
                        Push(ng + Octile(qi, qj, ti, tj), q);
                    }
            }
            if (!found) return false;
            outPath.Clear();
            for (int k = t; k >= 0; k = from[k]) outPath.Add(CellPoint(k));
            outPath.Reverse();
            // pull the route tight
            var tight = new List<Vector3> { outPath[0] };
            int i0 = 0;
            while (i0 < outPath.Count - 1)
            {
                int far = i0 + 1;
                for (int j = Mathf.Min(outPath.Count - 1, i0 + 24); j > i0 + 1; j--)
                    if (LineClear(outPath[i0], outPath[j], vehicle)) { far = j; break; }
                tight.Add(outPath[far]);
                i0 = far;
            }
            outPath.Clear(); outPath.AddRange(tight);
            outPath[outPath.Count - 1] = b;
            return true;
        }

        float H(int a, int b) => Octile(a % n, a / n, b % n, b / n);
        static float Octile(int ai, int aj, int bi, int bj) { int dx = Mathf.Abs(ai - bi), dz = Mathf.Abs(aj - bj); return Mathf.Max(dx, dz) + 0.414f * Mathf.Min(dx, dz); }

        void Push(float f, int k)
        {
            heap.Add((f, k));
            int i = heap.Count - 1;
            while (i > 0) { int p = (i - 1) / 2; if (heap[p].f <= heap[i].f) break; var tmp = heap[p]; heap[p] = heap[i]; heap[i] = tmp; i = p; }
        }
        (float, int) Pop()
        {
            var top0 = heap[0];
            int last = heap.Count - 1;
            heap[0] = heap[last]; heap.RemoveAt(last);
            int i = 0;
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, m = i;
                if (l < heap.Count && heap[l].f < heap[m].f) m = l;
                if (r < heap.Count && heap[r].f < heap[m].f) m = r;
                if (m == i) break;
                var tmp = heap[m]; heap[m] = heap[i]; heap[i] = tmp; i = m;
            }
            return top0;
        }

        /// <summary>Which way to walk (flat unit vector) to get from pos to goal.</summary>
        public Vector3 Steer(NavAgent a, Vector3 pos, Vector3 goal, bool vehicle)
        {
            Vector3 direct = goal - pos; direct.y = 0;
            if (direct.sqrMagnitude < 1) return Vector3.zero;
            float now = Time.time;
            bool replan = !a.has || a.vehicle != vehicle || (goal - a.goal).sqrMagnitude > 36 || now >= a.replanAt;
            if (!a.straight && a.has && a.idx < a.path.Count && (Flat(a.path[a.idx] - pos)).sqrMagnitude > 30 * 30) replan = true;
            if (replan)
            {
                if (Time.frameCount != plansFrame) { plansFrame = Time.frameCount; plansThisFrame = 0; }
                if (plansThisFrame < PlansPerFrame || !a.has)
                {
                    plansThisFrame++;
                    a.goal = goal; a.has = true; a.idx = 0; a.vehicle = vehicle;
                    a.replanAt = now + Random.Range(4f, 6f);
                    a.straight = LineClear(pos, goal, vehicle);
                    if (!a.straight && !AStar(pos, goal, vehicle, a.path)) a.straight = true;
                    if (!a.straight && a.path.Count > 1) a.idx = 1;
                }
            }
            if (a.straight) return direct.normalized;
            float reach = vehicle ? 7 : 3;
            while (a.idx < a.path.Count - 1 && Flat(a.path[a.idx] - pos).sqrMagnitude < reach * reach) a.idx++;
            // cut corners when the next point is already in view
            if (now >= a.skipCheckAt && a.idx < a.path.Count - 1)
            {
                a.skipCheckAt = now + 0.5f;
                if (LineClear(pos, a.path[a.idx + 1], vehicle)) a.idx++;
            }
            if (a.idx >= a.path.Count) return direct.normalized;
            Vector3 to = Flat(a.path[a.idx] - pos);
            return to.sqrMagnitude > 0.01f ? to.normalized : direct.normalized;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        /// <summary>Picture of the grid for checking a map: grey = walk, dark = blocked, blue = wading.</summary>
        public Texture2D Picture()
        {
            var t = new Texture2D(n, n, TextureFormat.RGB24, false);
            for (int k = 0; k < n * n; k++)
            {
                int ci = k % n, cj = k / n;
                int open = 0;
                for (int dj = -1; dj <= 1; dj++) for (int di = -1; di <= 1; di++) { if (di == 0 && dj == 0) continue; int a = ci + di, b2 = cj + dj; if (a >= 0 && b2 >= 0 && a < n && b2 < n && Step(foot, k, b2 * n + a)) open++; }
                Color c = foot[k] == 0 || open == 0 ? new Color(0.15f, 0.1f, 0.1f) : open < 8 ? new Color(0.45f, 0.3f, 0.25f) : foot[k] >= 40 ? new Color(0.3f, 0.5f, 0.9f) : veh[k] == 0 ? new Color(0.65f, 0.6f, 0.45f) : new Color(0.85f, 0.85f, 0.85f);
                t.SetPixel(k % n, k / n, c);
            }
            t.Apply();
            return t;
        }
    }
}
