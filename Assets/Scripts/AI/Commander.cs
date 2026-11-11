using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    public enum SquadOrder { Assault, Flank, Counter, Defend }

    public class Squad
    {
        public int team, id;
        public SquadOrder order;
        public CommandPost target;
        public Vector3 via;            // flank route waypoint
        public Vector3 stage;          // where an attack wave gathers before going in
        public bool gathering;
        public float phaseSince;
        public int version;            // bumped whenever the order changes
        public readonly List<Participant> members = new List<Participant>();
    }

    /// <summary>
    /// Land-battle team AI. Bots are grouped into squads of four; every few seconds (and whenever a post changes
    /// hands) each side splits its squads into a main assault, a flanking push on a second post, counter-attacks
    /// on posts that are being taken or were just lost, and defenders on the front-line posts. When an assault
    /// takes its post the squad moves straight on to the next. Bots spawn at the friendly post nearest their
    /// squad's objective (reinforcements), and assault squads grab vehicles.
    /// </summary>
    public class Commander
    {
        readonly Battle b;
        public readonly List<Squad>[] squads = { new List<Squad>(), new List<Squad>() };
        readonly Dictionary<Participant, Squad> of = new Dictionary<Participant, Squad>();
        readonly float[] replan = { 1f, 1.5f };
        readonly Dictionary<CommandPost, float>[] lostAt = { new Dictionary<CommandPost, float>(), new Dictionary<CommandPost, float>() };
        public const int SquadSize = 4;
        public const float GatherMax = 12f;      // the longest an attack wave waits at its staging point
        readonly CommandPost[] lastPrimary = new CommandPost[2];
        readonly float[] primarySince = new float[2];
        readonly int[] rotate = new int[2];

        public Commander(Battle battle)
        {
            b = battle;
            for (int t = 0; t < 2; t++)
            {
                Squad cur = null; int id = 0;
                // the player is the first member of the first squad of their side
                foreach (var p in b.people)
                {
                    if (p.team != t) continue;
                    if (cur == null || cur.members.Count >= SquadSize) { cur = new Squad { team = t, id = id++ }; squads[t].Add(cur); }
                    cur.members.Add(p); of[p] = cur;
                }
            }
        }

        public Squad SquadOf(Participant p) => p != null && of.TryGetValue(p, out var s) ? s : null;

        public void OnPostChanged(CommandPost cp, int lostBy)
        {
            if (lostBy >= 0) lostAt[lostBy][cp] = Time.time;
            replan[0] = Mathf.Min(replan[0], 0.5f);
            replan[1] = Mathf.Min(replan[1], 0.7f);
        }

        public void Tick(float dt)
        {
            for (int t = 0; t < 2; t++)
            {
                replan[t] -= dt;
                if (replan[t] <= 0) { replan[t] = 10f; Plan(t); }
                foreach (var s in squads[t]) Waves(s);
            }
        }

        /// <summary>Attack squads gather at a staging point, go in together, and regroup when the wave is spent.</summary>
        void Waves(Squad s)
        {
            if (s.target == null || s.order == SquadOrder.Defend) { s.gathering = false; return; }
            int alive = 0, staged = 0, atTarget = 0;
            foreach (var m in s.members)
            {
                if (!m.unit || !m.unit.Alive) continue;
                alive++;
                Vector3 p = m.unit.transform.position;
                if ((p - s.stage).sqrMagnitude < 35 * 35) staged++;
                if ((p - s.target.Pos).sqrMagnitude < 55 * 55) atTarget++;
            }
            float since = Time.time - s.phaseSince;
            // 2026-11-11: squads used to be sent back to regroup whenever nobody had reached the post within 25 s, then wait
            // up to 28 s - measured, attackers spent 41% of their time at staging points and 6% on their objective. Now a
            // wave gathers once (half the squad, or 12 s) and goes; it only regroups when it is all but wiped out.
            if (s.gathering)
            {
                if ((alive > 0 && staged >= Mathf.Max(2, Mathf.CeilToInt(alive * 0.5f))) || since > GatherMax) { s.gathering = false; s.phaseSince = Time.time; s.version++; }
            }
            else if (alive <= 1 && atTarget == 0 && since > 20 && s.order != SquadOrder.Counter) { s.gathering = true; s.phaseSince = Time.time; s.version++; }
        }

        Vector3 Front(int t)
        {
            Vector3 c = Vector3.zero; int n = 0;
            foreach (var p in b.posts) if (p.owner == t) { c += p.Pos; n++; }
            if (n > 0) return c / n;
            var hp = b.HomePost(t);
            return hp ? hp.Pos : Vector3.zero;
        }

        Vector3 EnemyFront(int t) => Front(1 - t);

        void Plan(int t)
        {
            var list = squads[t];
            if (list.Count == 0) return;
            Vector3 front = Front(t), enemy = EnemyFront(t);
            var own = new List<CommandPost>(); var other = new List<CommandPost>(); var hot = new List<CommandPost>();
            foreach (var p in b.posts)
            {
                if (p.owner == t)
                {
                    own.Add(p);
                    if (p.present[1 - t] > 0 || (p.capTeam != t && p.progress < 1)) hot.Add(p);
                }
                else
                {
                    other.Add(p);
                    if (lostAt[t].TryGetValue(p, out var when) && Time.time - when < 45) hot.Add(p);
                }
            }
            // nearest first; enemy home bases count as much further away, so they are taken last
            float Cost(CommandPost cp) => (cp.Pos - front).magnitude + (cp.pads.Count >= 3 && cp.owner >= 0 ? 220 : 0) - (cp.owner < 0 ? 30 : 0);
            other.Sort((x, y) => Cost(x).CompareTo(Cost(y)));
            hot.Sort((x, y) => (x.Pos - front).sqrMagnitude.CompareTo((y.Pos - front).sqrMagnitude));
            // front-line posts first: the ones nearest the enemy
            own.Sort((x, y) => (x.Pos - enemy).sqrMagnitude.CompareTo((y.Pos - enemy).sqrMagnitude));

            int n = list.Count;
            int defendN = own.Count > 0 ? Mathf.Max(1, Mathf.RoundToInt(n * 0.2f)) : 0;
            int counterN = hot.Count > 0 ? Mathf.Max(1, Mathf.RoundToInt(n * 0.25f)) : 0;
            if (other.Count == 0) { defendN = n - counterN; }
            int attackN = Mathf.Max(0, n - defendN - counterN);
            if (attackN == 0 && other.Count > 0 && n > 1) { attackN = 1; if (defendN > 1) defendN--; else counterN--; }

            // a main push that has stalled on the same post for a minute switches to another one
            CommandPost natural = other.Count > 0 ? other[0] : null;
            if (natural != lastPrimary[t]) { lastPrimary[t] = natural; primarySince[t] = Time.time; rotate[t] = 0; }
            int ordinary = 0; foreach (var cp in other) if (!(cp.pads.Count >= 3 && cp.owner >= 0)) ordinary++;   // home bases are not rotated onto
            int choices = Mathf.Min(3, Mathf.Max(1, ordinary));
            if (other.Count > 1 && Time.time - primarySince[t] > 60) { rotate[t] = (rotate[t] + 1) % choices; primarySince[t] = Time.time; }
            if (rotate[t] >= choices) rotate[t] = 0;
            CommandPost primary = other.Count > 0 ? other[Mathf.Min(rotate[t], other.Count - 1)] : null;
            CommandPost secondary = primary;
            if (other.Count > 1)
            {
                // the flank goes for a different post, preferring one off to the side of the main push
                Vector3 d0 = (primary.Pos - front); d0.y = 0;
                float best = -1;
                for (int i = 0; i < Mathf.Min(4, other.Count); i++)
                {
                    if (other[i] == primary) continue;
                    Vector3 di = other[i].Pos - front; di.y = 0;
                    float a = Vector3.Angle(d0, di) - (di.magnitude - d0.magnitude) * 0.05f;
                    if (other[i].owner < 0) a += 25;                       // neutral posts are easy pickings
                    if (other[i].pads.Count >= 3) a -= 40;                 // enemy home bases are tough
                    if (a > best) { best = a; secondary = other[i]; }
                }
            }
            int flankN = primary != secondary ? attackN / 2 : attackN >= 3 ? 1 : 0;
            int assaultN = attackN - flankN;

            var slots = new List<(SquadOrder, CommandPost)>();
            for (int i = 0; i < assaultN; i++) slots.Add((SquadOrder.Assault, primary));
            for (int i = 0; i < flankN; i++) slots.Add((SquadOrder.Flank, secondary));
            for (int i = 0; i < counterN; i++) slots.Add((SquadOrder.Counter, hot[i % hot.Count]));
            for (int i = 0; i < defendN; i++) slots.Add((SquadOrder.Defend, own[i % Mathf.Min(2, own.Count)]));

            // keep squads on orders that are still wanted, then hand out the rest
            var free = new List<Squad>();
            foreach (var s in list)
            {
                int k = slots.FindIndex(x => x.Item1 == s.order && x.Item2 == s.target);
                if (k >= 0 && s.target != null && (s.order == SquadOrder.Defend ? s.target.owner == t : s.target.owner != t || s.order == SquadOrder.Counter)) slots.RemoveAt(k);
                else free.Add(s);
            }
            foreach (var s in free)
            {
                if (slots.Count == 0) { Set(s, primary != null ? SquadOrder.Assault : SquadOrder.Defend, primary ?? (own.Count > 0 ? own[0] : null), front); continue; }
                var sl = slots[0]; slots.RemoveAt(0);
                Set(s, sl.Item1, sl.Item2, front);
            }
        }

        void Set(Squad s, SquadOrder o, CommandPost target, Vector3 front)
        {
            if (target == null) return;
            if (s.order == o && s.target == target) return;
            s.order = o; s.target = target; s.version++;
            Vector3 back = front - target.Pos; back.y = 0;
            s.stage = target.Pos + (back.sqrMagnitude > 1 ? back.normalized : Vector3.forward) * 70;
            if (b.land != null) { s.stage.y = b.land.Height(s.stage.x, s.stage.z); if (b.land.nav != null) s.stage = b.land.nav.SnapPoint(s.stage); }
            // close-by posts and counter-attacks go straight in
            s.gathering = o != SquadOrder.Defend && o != SquadOrder.Counter && (target.Pos - front).magnitude > 110;
            s.phaseSince = Time.time;
            if (o == SquadOrder.Flank)
            {
                Vector3 d = target.Pos - front; d.y = 0;
                Vector3 side = Vector3.Cross(Vector3.up, d.normalized) * ((s.id % 2 == 0) ? 1 : -1);
                Vector3 via = target.Pos + side * 75 - d.normalized * 45;
                if (b.land != null) { float lim = b.land.PlayHalf - 30; via.x = Mathf.Clamp(via.x, -lim, lim); via.z = Mathf.Clamp(via.z, -lim, lim); via.y = b.land.Height(via.x, via.z); if (b.land.nav != null) via = b.land.nav.SnapPoint(via); }
                s.via = via;
                s.stage = via;
            }
        }

        /// <summary>
        /// A safe squadmate in the field that this bot should spawn on: attacking squads reinforce a mate who is
        /// closer to the objective than any friendly post; defenders stay with the posts.
        /// </summary>
        public Unit MateToSpawnOn(Participant p)
        {
            var s = SquadOf(p);
            if (s == null || s.target == null || s.order == SquadOrder.Defend) return null;
            if (Random.value > 0.8f) return null;
            float postBest = float.MaxValue;
            foreach (var cp in b.posts) if (cp.SpawnableFor(p.team)) postBest = Mathf.Min(postBest, (cp.Pos - s.target.Pos).magnitude);
            Unit best = null; float bd = postBest - 40;      // only worth it if it saves a real walk
            foreach (var m in s.members)
            {
                if (m == p || !m.unit || !m.unit.Alive) continue;
                float d = (m.unit.transform.position - s.target.Pos).magnitude;
                if (d < bd && b.MateBlocked(m.unit) == null) { bd = d; best = m.unit; }
            }
            return best;
        }

        /// <summary>Where a bot of this squad should spawn: the friendly post nearest its objective (defenders at the post itself).</summary>
        public CommandPost SpawnPostFor(Participant p)
        {
            var s = SquadOf(p);
            if (s == null || s.target == null) return null;
            if (s.order == SquadOrder.Defend && s.target.SpawnableFor(p.team) && Random.value < 0.5f) return s.target;
            CommandPost best = null; float bd = float.MaxValue;
            // assault squads like to start where a vehicle is waiting
            bool wantRide = (s.order == SquadOrder.Assault || s.order == SquadOrder.Flank) && Random.value < 0.45f;
            foreach (var cp in b.posts)
            {
                if (!cp.SpawnableFor(p.team)) continue;
                float d = (cp.Pos - s.target.Pos).sqrMagnitude;
                if (wantRide)
                    foreach (var pad in cp.pads)
                        if (pad.vehicle && pad.vehicle.Alive && pad.vehicle.IsEmpty) { d *= 0.3f; break; }
                if (d < bd) { bd = d; best = cp; }
            }
            return best;
        }

        public static string OrderName(SquadOrder o) => o == SquadOrder.Assault ? "assault" : o == SquadOrder.Flank ? "flank" : o == SquadOrder.Counter ? "counter-attack" : "defend";
    }
}
