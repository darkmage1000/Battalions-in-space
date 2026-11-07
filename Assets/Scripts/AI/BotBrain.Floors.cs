using UnityEngine;

namespace BIS
{
    /// <summary>
    /// Battle royale bots in buildings with more than one floor. The walk grid only knows the ground, so every
    /// two-storey house and open tower carries a climb route (`LandWorld.Climb`: outside -> stair foot -> stair top ->
    /// across the floor -> next ramp ...). A bot whose goal is on an upper floor walks the grid to the route's first node,
    /// then follows the nodes up; a bot up there whose goal is somewhere else follows them back down. In a fight on an
    /// upper floor it keeps its feet on the slab (no strafing off a ledge) and ducks behind the parapets.
    /// High ground: a bot with a long gun may climb a tower inside the next zone and hold its roof for a while.
    /// </summary>
    public partial class BotBrain
    {
        LandWorld.Climb cClimb;     // the route being walked (null = on the ground grid)
        int cIdx;                   // the node we are heading for
        bool cArrived;              // on the goal's floor: walk straight to the goal
        float cCheckT, cLastCall;
        int cFrom = -1;             // the node we left (the leg is cFrom -> cIdx)
        float rHighUntil;           // holding high ground until then (0 = still on the way)
        float rHighCd;              // no new high-ground climb before then
        LandWorld.Climb rHigh;

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        /// <summary>The upper floor this bot is standing on, if any.</summary>
        LandWorld.UpperFloor FloorHere => b.land != null && b.land.climbs.Count > 0 ? b.land.FloorAt(u.transform.position) : null;

        /// <summary>Like Toward, but knows the floors: routes up and down stairs and ramps when the goal and the bot are on
        /// different floors.</summary>
        Vector3 ClimbToward(Vector3 pos, Vector3 goal)
        {
            var land = b.land;
            if (land == null || land.climbs.Count == 0) return Toward(pos, goal);
            var gf = land.FloorAt(goal);
            // coming back to the route after a while (a fight, healing): from wherever we are on a floor, go back to its
            // middle first - every leg from there is known to be clear, a leg from a random spot is not
            bool resumed = Time.time - cLastCall > 0.5f;
            cLastCall = Time.time;
            if (resumed)
            {
                var rf = land.FloorAt(pos);
                if (rf != null)
                {
                    cClimb = rf.climb; cFrom = -1;
                    int w0 = gf != null && gf.climb == rf.climb ? gf.node : 0;
                    if (w0 == rf.node) { cIdx = rf.node; cArrived = true; }
                    else { cIdx = rf.node; cArrived = false; }
                    cCheckT = 1f;
                }
            }
            // where are we? (every second, or when not on a route: we may have been pushed, fallen or knocked off)
            cCheckT -= Time.deltaTime;
            if (cClimb == null || cCheckT <= 0)
            {
                cCheckT = 1f;
                var hf = land.FloorAt(pos);
                if (hf != null)
                {
                    if (cClimb != hf.climb) { cClimb = hf.climb; cIdx = hf.node; cArrived = false; cFrom = -1; }
                    else if (!cArrived && (cIdx < hf.lo || cIdx > hf.hi)) { cIdx = hf.node; cFrom = -1; }
                }
                else
                {
                    cArrived = false;
                    if (cClimb != null && cIdx <= 1 && Want(goal) == 0 && pos.y - land.Top(pos.x, pos.z) < 0.9f) cClimb = null;
                    else if (cClimb != null)
                    {
                        var near = land.ClimbNear(pos, 3.5f, out int seg);
                        if (near != cClimb) cClimb = null;                  // fell or was knocked off: back to the grid
                        else if (cIdx != seg && cIdx != seg + 1) { bool upw = Want(goal) > seg; cIdx = upw ? seg + 1 : seg; cFrom = upw ? seg : seg + 1; }
                    }
                    else if (pos.y - land.Top(pos.x, pos.z) > 1.6f)
                    {
                        var near = land.ClimbNear(pos, 3f, out int seg);    // half way up a ramp or a staircase
                        if (near != null) { cClimb = near; bool upw = Want(goal) > seg; cIdx = upw ? seg + 1 : seg; cFrom = upw ? seg : seg + 1; }
                    }
                }
            }
            if (cClimb == null)
            {
                if (gf == null) return Toward(pos, goal);
                Vector3 e = gf.climb.nodes[0];
                if (Flat(e - pos).magnitude > 2.2f) return Toward(pos, e);
                cClimb = gf.climb; cIdx = 1; cFrom = 0; cArrived = false;
            }
            int want = gf != null && gf.climb == cClimb ? gf.node : 0;
            if (cArrived && cIdx == want && want > 0) return FloorStep(gf, pos, goal);
            cArrived = false;
            Vector3 node = cClimb.nodes[cIdx];
            if (Flat(node - pos).magnitude < 0.9f && Mathf.Abs(node.y - pos.y) < 1.9f)
            {
                if (cIdx == want)
                {
                    if (want == 0) { cClimb = null; return Toward(pos, goal); }
                    cArrived = true;
                    return FloorStep(gf, pos, goal);
                }
                cFrom = cIdx;
                cIdx += want > cIdx ? 1 : -1;
                node = cClimb.nodes[cIdx];
            }
            // keep to the leg (a ramp is only 2.3 m wide): aim at a point a little ahead on the line from the last node
            Vector3 aim = node;
            if (cFrom >= 0 && cFrom < cClimb.nodes.Length && Mathf.Abs(cFrom - cIdx) == 1)
            {
                Vector3 a = Flat(cClimb.nodes[cFrom]), bb = Flat(node), p = Flat(pos), ab = bb - a;
                float len = ab.magnitude;
                if (len > 0.5f)
                {
                    float t = Mathf.Clamp(Vector3.Dot(p - a, ab / len), 0, len);
                    float off = (a + ab / len * t - p).magnitude;
                    if (off < 3f) aim = a + ab / len * Mathf.Min(len, t + 1.6f);
                }
            }
            Vector3 to = Flat(aim - pos);
            return to.sqrMagnitude > 0.0001f ? to.normalized : Vector3.zero;
        }

        /// <summary>The node of the current climb the goal is at (its floor), or 0 = back down to the ground.</summary>
        int Want(Vector3 goal)
        {
            var gf = b.land.FloorAt(goal);
            return cClimb != null && gf != null && gf.climb == cClimb ? gf.node : 0;
        }

        /// <summary>On a floor: straight to the goal, or through the inner doorway when it is in the other room.</summary>
        Vector3 FloorStep(LandWorld.UpperFloor f, Vector3 pos, Vector3 goal)
        {
            Vector3 aim = goal;
            if (f.divX < 900)
            {
                float a = f.LocalX(pos) - f.divX, g = f.LocalX(goal) - f.divX;
                if (Mathf.Sign(a) != Mathf.Sign(g))
                {
                    Vector3 mine = f.World(f.divX + Mathf.Sign(a) * 0.9f, f.gapZ), theirs = f.World(f.divX + Mathf.Sign(g) * 0.9f, f.gapZ);
                    aim = Flat(mine - pos).magnitude < 0.8f || Mathf.Abs(a) < 0.5f ? theirs : mine;
                }
            }
            Vector3 to = Flat(aim - pos);
            return to.sqrMagnitude > 0.0001f ? to.normalized : Vector3.zero;
        }

        /// <summary>How far is left to walk to the goal (along the climb route when on one) - for "am I getting anywhere".</summary>
        float LeftToGo(Vector3 pos, Vector3 goal, float flat)
        {
            var land = b.land;
            if (land == null || land.climbs.Count == 0 || cArrived) return flat;   // on the goal's floor: just the walk across it
            var gf = land.FloorAt(goal);
            var cl = cClimb ?? (gf != null ? gf.climb : null);
            if (cl == null) return flat;
            int want = gf != null && gf.climb == cl ? gf.node : 0;
            int from = cClimb != null ? cIdx : 0;
            float sum = (cl.nodes[from] - pos).magnitude;
            for (int i = from; i != want; i += want > from ? 1 : -1) sum += (cl.nodes[i + (want > from ? 1 : -1)] - cl.nodes[i]).magnitude;
            if (gf == null || gf.climb != cl) sum += Flat(goal - cl.nodes[0]).magnitude;
            else sum += Flat(goal - cl.nodes[want]).magnitude;
            return sum;
        }

        /// <summary>In a fight on an upper floor: never step off the slab (or into the stairwell).</summary>
        Vector3 KeepOnFloor(Vector3 pos, Vector3 move, LandWorld.UpperFloor f)
        {
            if (move.sqrMagnitude < 0.0001f) return move;
            Vector3 next = pos + Flat(move).normalized * 1.4f;
            next.y = f.c.y;
            if (f.Contains(next, 0.5f)) return move;
            Vector3 back = Flat(f.c - pos);
            return back.sqrMagnitude > 1 ? back.normalized * 0.35f : Vector3.zero;
        }

        /// <summary>Pick the roof of an open tower inside the next zone and go and hold it (long guns like it most).</summary>
        bool PickHighGround(Vector3 pos)
        {
            var r = b.royale; var land = b.land;
            if (land.climbs.Count == 0 || Time.time < rHighCd || !Armed || r.phase != Royale.Phase.Play) return false;
            bool sniper = HasKind(WKind.Sniper), rifle = HasKind(WKind.Rifle);
            float chance = sniper ? 0.6f : rifle ? 0.22f : 0.06f;
            if (r.AlivePlayers <= 40) chance *= 1.5f;
            if (Random.value > chance) { rHighCd = Time.time + 12; return false; }
            LandWorld.Climb best = null; float bs = float.MaxValue;
            foreach (var cl in land.climbs)
            {
                if (!cl.tower || r.OutsideNext(cl.c)) continue;
                if (cl.holder && cl.holder != u && cl.holder.Alive && Time.time < cl.holdUntil) continue;
                float d = Flat(cl.c - pos).magnitude;
                if (d > 180) continue;
                float s = d + (new Vector2(cl.c.x, cl.c.z) - r.nextC).magnitude * 0.25f;
                if (s < bs) { bs = s; best = cl; }
            }
            if (best == null) { rHighCd = Time.time + 15; return false; }
            // a spot at the edge of the roof facing the middle of the zone
            var top = best.Top;
            Vector3 toZone = Flat(new Vector3(r.nextC.x, 0, r.nextC.y) - top.c);
            Vector3 lz = Quaternion.Inverse(top.rot) * (toZone.sqrMagnitude > 1 ? toZone.normalized : Vector3.forward);
            float e = top.hx - 1.1f;
            Vector2[] spots = { new Vector2(0, e), new Vector2(0, -e), new Vector2(e, 0), new Vector2(-e, 0), new Vector2(e * 0.7f, e * 0.7f), new Vector2(-e * 0.7f, e * 0.7f), new Vector2(e * 0.7f, -e * 0.7f), new Vector2(-e * 0.7f, -e * 0.7f) };
            Vector3 pick = top.c; float pd = -2;
            foreach (var sp in spots)
            {
                Vector3 wpt = top.World(sp.x, sp.y);
                if (!top.Contains(wpt, 0.6f)) continue;
                float dot = Vector2.Dot(sp.normalized, new Vector2(lz.x, lz.z)) + Random.Range(0, 0.35f);
                if (dot > pd) { pd = dot; pick = wpt; }
            }
            rGoal = RGoal.High; rHigh = best; rRoam = pick; rHighUntil = 0;
            best.holder = u; best.holdUntil = Time.time + 90;
            rGoalT = 70;
            return true;
        }

        bool HasKind(WKind k) => (u.weapons[0] != null && u.weapons[0].def.kind == k && !OutOfAmmo(u.weapons[0])) || (u.weapons[1] != null && u.weapons[1].def.kind == k && !OutOfAmmo(u.weapons[1]));

        /// <summary>Holding a roof: arrived at the spot, the hold timer runs; the goal ends when it runs out or the zone moves away.</summary>
        void TickHigh(Vector3 pos, float flat, ref Vector3 aimPoint, ref bool crouch)
        {
            if (rGoal != RGoal.High) return;
            var f = FloorHere;
            bool there = flat < 1.4f && f != null && rHigh != null && f.climb == rHigh;
            if (there && rHighUntil <= 0) { rHighUntil = Time.time + Random.Range(25f, 45f); rGoalT = 60; rHigh.holdUntil = rHighUntil + 5; }
            if (!there) return;
            // look out over the parapet, sweeping slowly; duck now and then
            Vector3 outw = Flat(pos - rHigh.Top.c);
            if (outw.sqrMagnitude < 0.5f) outw = u.transform.forward;
            float sweep = Mathf.Sin(Time.time * 0.4f + u.part.name.Length) * 55;
            aimPoint = u.Eye + Quaternion.Euler(0, sweep, 0) * outw.normalized * 40 + Vector3.down * 4;
            crouch = Mathf.Sin(Time.time * 0.7f + u.part.name.Length * 1.3f) > 0.55f;
        }

        bool HighDone(Vector3 pos)
        {
            var r = b.royale;
            if (rHigh == null) return true;
            if (rHighUntil > 0 && Time.time > rHighUntil) { rHighCd = Time.time + 40; return true; }
            if (r.OutsideNext(rHigh.c) || (LateForZone(pos) && r.OutsideNext(pos))) return true;
            return false;
        }
    }
}
