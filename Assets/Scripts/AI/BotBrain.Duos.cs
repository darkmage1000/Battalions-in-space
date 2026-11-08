using UnityEngine;

namespace BIS
{
    /// <summary>
    /// Battle royale duos for bots. Down: crawl toward the mate (or away from whoever is shooting). A mate down: once the
    /// fight is won (no target), go and revive them; mid-fight only when they are close to bleeding out and nothing has
    /// hit us for a moment. A mate out: pick up their ID tag when it is safe and carry it to the nearest free revive
    /// station. Otherwise stay near the mate (closely when the mate is a person).
    /// </summary>
    public partial class BotBrain
    {
        int rStation = -1;
        float duoT;

        Participant Mate => b.royale != null ? b.royale.MateOf(u.part) : null;

        /// <summary>Down: crawl to the mate, or away from the shooting. Nothing else.</summary>
        void KnockedThink(Vector3 pos)
        {
            Vector3 move = Vector3.zero;
            var m = Mate != null ? Mate.unit : null;
            if (u.BeingRevived) move = Vector3.zero;
            else if (m && m.Alive && !m.knocked)
            {
                Vector3 to = m.transform.position - pos; to.y = 0;
                if (to.magnitude > 1.6f) move = Toward(pos, m.transform.position);
            }
            else if (target != null && target.Alive)
            {
                Vector3 away = pos - target.AimPoint; away.y = 0;
                move = away.normalized;
            }
            if (cClimb == null && FloorHere == null) move = Avoid(pos, move, Time.deltaTime);
            Vector3 look = move.sqrMagnitude > 0.01f ? move : u.transform.forward;
            u.input = new UnitInput { move = Vector3.ClampMagnitude(new Vector3(move.x, 0, move.z), 1), aimPoint = u.Eye + look * 10 };
        }

        /// <summary>Mid-fight: drop the target to save a mate who is about to bleed out, if it is quiet enough.</summary>
        void DuoTargetCheck(Vector3 pos)
        {
            var m = Mate != null ? Mate.unit : null;
            if (!m || !m.Alive || !m.knocked) return;
            // the mate just went down: drop whatever errand we were on and go to them (fights still come first)
            if (target == null && rGoal != RGoal.Mate && !u.Using) rGoalT = 0;
            if (target == null) return;
            float left = m.hp / Unit.DownHp * Unit.BleedTime;
            float td = (target.AimPoint - pos).magnitude, md = (m.transform.position - pos).magnitude;
            bool quiet = Time.time - u.lastHurt > 2.5f;
            // about to bleed out, or the enemy is well away and the mate close: go
            if (quiet && ((left < 14 && (td > 22 || md < 6)) || (td > 35 && md < 25))) { target = null; seeT = 0; rGoalT = 0; }
        }

        /// <summary>The duo goals, checked before everything else in <see cref="PickRoyaleGoal"/>. True = a goal was set.</summary>
        bool PickDuoGoal(Vector3 pos, bool late)
        {
            var r = b.royale;
            var mate = Mate;
            if (mate == null) return false;
            var m = mate.unit;
            rStation = -1;
            // 1. revive a downed mate (unless they are far out in the zone)
            if (m && m.Alive && m.knocked && (m.transform.position - pos).magnitude < 160) { rGoal = RGoal.Mate; rGoalT = 2; return true; }
            if (late || r.OutsideZone(pos)) return false;
            // 2. carrying their tag: to the nearest free revive station
            if (u.tagOf >= 0)
            {
                int si = r.NearestStation(pos, 900);
                if (si >= 0) { rGoal = RGoal.Station; rStation = si; rGoalT = 10; return true; }
            }
            // 3. mate out: fetch their ID tag
            if (!mate.Alive && mate.dropped && mate.placement == 0)
            {
                var tag = r.TagOnGround(mate);
                if (tag != null && (tag.pos - pos).magnitude < 450 && !r.OutsideZone(tag.pos)) { rSkip.Remove(tag.id); rGoal = RGoal.Loot; rItem = tag; rGoalT = 10; return true; }
            }
            // 4. keep together: closely when the mate is a person, loosely between bots
            if (m && m.Alive && !m.knocked)
            {
                float d = (m.transform.position - pos).magnitude;
                float keep = mate.Human ? 26 : 85;
                if (d > keep)
                {
                    rGoal = RGoal.Roam;
                    Vector2 j = Random.insideUnitCircle * (mate.Human ? 5 : 14);
                    rRoam = b.land.nav.SnapPoint(m.transform.position + new Vector3(j.x, 0, j.y));
                    rGoalT = mate.Human ? 2 : 5;
                    return true;
                }
            }
            return false;
        }

        bool DuoGoalDone(Vector3 pos)
        {
            switch (rGoal)
            {
                case RGoal.Mate: { var m = Mate != null ? Mate.unit : null; return !m || !m.Alive || !m.knocked; }
                case RGoal.Station: return u.tagOf < 0 || !b.royale.StationFree(rStation);
                default: return false;
            }
        }

        Vector3 DuoGoalPos(Vector3 pos)
        {
            if (rGoal == RGoal.Mate) { var m = Mate != null ? Mate.unit : null; return m ? m.transform.position : pos; }
            if (rGoal == RGoal.Station && rStation >= 0 && rStation < b.royale.stations.Count) return b.royale.stations[rStation].pos;
            return pos;
        }

        /// <summary>At the downed mate / the station: start the channel. True when this handled the arrival.</summary>
        bool DuoArrive(float d, float yd)
        {
            if (rGoal == RGoal.Mate && d < Unit.ReviveRange - 0.5f && Mathf.Abs(yd) < 2f)
            {
                var m = Mate != null ? Mate.unit : null;
                if (m) u.StartRevive(m);
                return true;
            }
            if (rGoal == RGoal.Station && d < Unit.StationRange - 0.8f)
            {
                u.StartStation(rStation);
                rGoalT = 0;
                return true;
            }
            return false;
        }
    }
}
