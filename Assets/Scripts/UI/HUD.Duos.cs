using UnityEngine;

namespace BIS
{
    /// <summary>Battle royale duos on screen: your mate's card under the radar, being down (bleed-out bar, revive
    /// progress), markers over a downed mate / their ID tag / the nearest revive station, and those on the radar and map.</summary>
    public partial class HUD
    {
        static readonly Color DuoGreen = new Color(0.4f, 1f, 0.55f);

        void DuoOverlay(PlayerControl pc, Unit me)
        {
            var b = battle; var r = b.royale;
            if (r == null || !r.Duos) return;
            float w = UI.W, h = UI.H;
            var mate = r.MateOf(b.player);
            MateCard(me, mate);

            // down: the world goes red, a bleed-out bar, and what to do
            if (me.knocked)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3);
                UI.Fill(new Rect(0, 0, w, h), new Color(0.6f, 0f, 0f, 0.16f + pulse * 0.06f));
                Rect kb = new Rect(w / 2 - 260, h / 2 + 90, 520, 96);
                UI.Box(kb);
                UI.Text(new Rect(kb.x, kb.y + 6, kb.width, 34), "KNOCKED DOWN", 30, UI.Bad, TextAnchor.MiddleCenter);
                UI.Bar(new Rect(kb.x + 20, kb.y + 44, kb.width - 40, 10), Mathf.Clamp01(me.hp / Unit.DownHp), UI.Bad);
                float rev = me.ReviveProgress;
                string line;
                if (rev >= 0)
                {
                    UI.Bar(new Rect(kb.x + 20, kb.y + 58, kb.width - 40, 8), rev, UI.Good);
                    line = $"{(mate != null ? mate.name : "Your mate")} is reviving you...";
                }
                else line = mate != null && mate.Alive ? $"Crawl to cover - {mate.name} can revive you   ({Mathf.CeilToInt(me.hp / Unit.DownHp * Unit.BleedTime)} s)" : "Bleeding out";
                UI.Text(new Rect(kb.x, kb.y + 68, kb.width, 26), line, 18, rev >= 0 ? UI.Good : Color.white, TextAnchor.MiddleCenter);
            }

            // reviving / calling in: the channel bar
            if (me.Reviving || me.CallingIn)
            {
                Rect ub = new Rect(w / 2 - 190, h / 2 + 110, 380, 46);
                UI.Box(ub);
                string who = me.Reviving ? (me.useTarget ? me.useTarget.displayName : "your mate") : me.tagOf >= 0 && me.tagOf < b.people.Count ? b.people[me.tagOf].name : "your mate";
                UI.Text(new Rect(ub.x, ub.y + 2, ub.width, 24), me.Reviving ? $"REVIVING {who.ToUpper()}" : $"CALLING {who.ToUpper()} BACK IN", 18, UI.Good, TextAnchor.MiddleCenter);
                UI.Bar(new Rect(ub.x + 12, ub.y + 30, ub.width - 24, 8), 1 - me.useT / Mathf.Max(0.01f, me.useDur), UI.Good);
            }

            // markers in the world
            Vector3 p = me.transform.position;
            if (mate != null && mate.unit && mate.unit.Alive)
            {
                var mu = mate.unit;
                Color c = mu.knocked ? UI.Bad : SquadColor;
                string label = mu.knocked ? $"+ {mate.name} DOWN  {Mathf.RoundToInt((mu.transform.position - p).magnitude)} m" : $"{mate.name}  {Mathf.RoundToInt((mu.transform.position - p).magnitude)} m";
                if (mu.knocked || (mu.transform.position - p).magnitude > 25) Marker(mu.transform.position + Vector3.up * (mu.Height + 0.6f), label, c, mu.knocked);
            }
            else if (mate != null && mate.dropped)
            {
                var tag = r.TagOnGround(mate);
                if (tag != null) Marker(tag.pos + Vector3.up * 1.2f, $"{mate.name}'s ID TAG  {Mathf.RoundToInt((tag.pos - p).magnitude)} m", DuoGreen, true);
            }
            if (me.tagOf >= 0)
            {
                int si = r.NearestStation(p, 99999);
                if (si >= 0)
                {
                    var s = r.stations[si];
                    Marker(s.pos + Vector3.up * 3.4f, $"REVIVE STATION  {Mathf.RoundToInt((s.pos - p).magnitude)} m", DuoGreen, true);
                }
                else Marker(p + Vector3.up * 3, "No revive stations left", UI.Dim, false);
            }
        }

        /// <summary>A label over a point in the world; pinned to the screen edge (with its arrow) when it is off screen.</summary>
        void Marker(Vector3 world, string text, Color col, bool keepOnScreen)
        {
            var cam = Game.Cam;
            Vector3 sp = cam.WorldToScreenPoint(world);
            Vector2 g = new Vector2(sp.x / UI.S, (Screen.height - sp.y) / UI.S);
            bool on = sp.z > 0 && g.x > 40 && g.x < UI.W - 40 && g.y > 60 && g.y < UI.H - 60;
            if (!on)
            {
                if (!keepOnScreen) return;
                Vector3 rel = cam.transform.InverseTransformPoint(world);
                Vector2 dir = new Vector2(rel.x, -rel.y); if (rel.z < 0) dir = -dir;
                if (dir.sqrMagnitude < 0.0001f) dir = Vector2.down;
                dir.Normalize();
                Vector2 cc = new Vector2(UI.W / 2, UI.H / 2);
                float k = Mathf.Min((UI.W / 2 - 80) / Mathf.Max(0.001f, Mathf.Abs(dir.x)), (UI.H / 2 - 90) / Mathf.Max(0.001f, Mathf.Abs(dir.y)));
                g = cc + dir * k;
            }
            UI.Dot(g, 7, Color.black); UI.Dot(g, 5, col);
            UI.Text(new Rect(g.x - 170, g.y - 34, 340, 24), text, 17, col, TextAnchor.MiddleCenter);
        }

        /// <summary>Under the radar: your mate's name, health and armour, or how they are doing.</summary>
        void MateCard(Unit me, Participant mate)
        {
            if (mate == null) return;
            var r = battle.royale;
            Rect c = new Rect(20, 258, 230, 66);
            UI.Box(c);
            UI.Text(new Rect(c.x + 10, c.y + 4, c.width - 20, 22), mate.name, 17, SquadColor);
            var mu = mate.unit;
            if (mu && mu.Alive)
            {
                if (mu.knocked)
                {
                    UI.Text(new Rect(c.x + 10, c.y + 24, c.width - 20, 20), mu.BeingRevived ? "DOWN - being revived" : "DOWN - revive them!", 15, UI.Bad);
                    UI.Bar(new Rect(c.x + 10, c.y + 48, c.width - 20, 8), Mathf.Clamp01(mu.hp / Unit.DownHp), UI.Bad);
                }
                else
                {
                    if (mu.armor > 0) UI.Bar(new Rect(c.x + 10, c.y + 30, c.width - 20, 5), Mathf.Clamp01(mu.armor / Unit.MaxArmor), ArmorCol);
                    UI.Bar(new Rect(c.x + 10, c.y + 38, c.width - 20, 9), Mathf.Clamp01(mu.hp / mu.maxHp), UI.Good);
                    UI.Text(new Rect(c.x + 10, c.y + 48, c.width - 20, 18), $"{Mathf.RoundToInt((mu.transform.position - me.transform.position).magnitude)} m", 13, UI.Dim);
                }
                return;
            }
            string s;
            if (!mate.dropped) s = "still in the dropship";
            else if (mate.placement > 0) s = "out of the match";
            else if (me.tagOf == battle.people.IndexOf(mate)) s = "you have their tag: revive station!";
            else if (r.TagCarrier(mate) is Unit tc && tc) s = "their tag is being carried";
            else if (r.TagOnGround(mate) != null) s = "OUT - pick up their ID tag";
            else s = "out";
            UI.Text(new Rect(c.x + 10, c.y + 26, c.width - 20, 36), s, 15, s.StartsWith("OUT") || s.StartsWith("you") ? DuoGreen : UI.Dim, TextAnchor.UpperLeft, true);
        }

        /// <summary>Radar: your mate, their tag, and revive stations (all when you carry a tag, else only close ones).</summary>
        void DuoRadar(Unit me, System.Func<Vector3, Vector2> map, Vector3 mp)
        {
            var r = battle.royale;
            if (r == null || !r.Duos) return;
            var mate = r.MateOf(battle.player);
            bool carrying = me.tagOf >= 0;
            for (int i = 0; i < r.stations.Count; i++)
            {
                var s = r.stations[i];
                if (s.used) continue;
                if (!carrying && (s.pos - mp).sqrMagnitude > 120 * 120) continue;
                var q = map(s.pos);
                UI.Fill(new Rect(q.x - 5, q.y - 1.5f, 10, 3), DuoGreen); UI.Fill(new Rect(q.x - 1.5f, q.y - 5, 3, 10), DuoGreen);
            }
            if (mate != null)
            {
                if (mate.unit && mate.unit.Alive && mate.unit.knocked) { var q = map(mate.unit.transform.position); UI.Dot(q, 6, Color.black); UI.Dot(q, 4.5f, UI.Bad); }
                var tag = mate.Alive ? null : r.TagOnGround(mate);
                if (tag != null) { var q = map(tag.pos); UI.Dot(q, 6, Color.black); UI.Dot(q, 4.5f, DuoGreen); }
            }
        }

        /// <summary>Big map: revive stations (used ones dim) and your mate's ID tag.</summary>
        void DuoMap(System.Func<Vector3, Vector2> M)
        {
            var r = battle.royale;
            if (r == null || !r.Duos) return;
            foreach (var s in r.stations)
            {
                var q = M(s.pos);
                Color c = s.used ? new Color(0.4f, 0.45f, 0.42f) : DuoGreen;
                UI.Fill(new Rect(q.x - 8, q.y - 3, 16, 6), Color.black); UI.Fill(new Rect(q.x - 3, q.y - 8, 6, 16), Color.black);
                UI.Fill(new Rect(q.x - 7, q.y - 2, 14, 4), c); UI.Fill(new Rect(q.x - 2, q.y - 7, 4, 14), c);
            }
            var mate = r.MateOf(battle.player);
            var tag = mate != null && !mate.Alive ? r.TagOnGround(mate) : null;
            if (tag != null)
            {
                var q = M(tag.pos);
                UI.Dot(q, 8, Color.black); UI.Dot(q, 6, DuoGreen);
                UI.Text(new Rect(q.x - 80, q.y + 8, 160, 18), "ID TAG", 13, DuoGreen, TextAnchor.UpperCenter);
            }
        }

        /// <summary>Eliminated screen, duos: can your mate still bring you back?</summary>
        string DuoDeadLine()
        {
            var b = battle; var r = b.royale;
            if (r == null || !r.Duos) return null;
            var mate = r.MateOf(b.player);
            if (mate == null || !mate.Alive) return null;
            if (r.TagCarrier(b.player) != null) return $"{mate.name} has your ID tag - waiting for a revive station...";
            return $"{mate.name} can bring you back: they pick up your ID tag and take it to a revive station";
        }
    }
}
