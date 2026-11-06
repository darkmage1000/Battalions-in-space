using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    /// <summary>In-battle interface: HUD, radar, spawn screen, map, scoreboard, pause and results.</summary>
    public class HUD : MonoBehaviour
    {
        public Battle battle;
        static HUD I;
        class Msg { public string text; public Color col; public float until; }
        class KillEntry { public string text; public float until; }
        static readonly List<Msg> msgs = new List<Msg>();
        static readonly List<KillEntry> kills = new List<KillEntry>();
        static float hitT, hurtT, pointsT; static int pointsShown; static Vector3 hurtFrom;
        bool helpOpen;
        float fpsT, fps;

        void Awake() { I = this; msgs.Clear(); kills.Clear(); }

        public static void Message(string s, Color c, float t = 3)
        {
            msgs.Add(new Msg { text = s, col = c, until = Time.unscaledTime + t });
            if (msgs.Count > 5) msgs.RemoveAt(0);
        }

        public static void Kill(string killer, int kt, string victim, int vt, string weapon)
        {
            var b = Battle.I; if (b == null) return;
            string kc = kt >= 0 ? UI.Hex(b.TeamColor(kt)) : "aaaaaa", vc = UI.Hex(b.TeamColor(vt));
            string s = killer != null ? $"<color=#{kc}>{killer}</color>  <color=#bbbbbb>[{weapon}]</color>  <color=#{vc}>{victim}</color>" : $"<color=#{vc}>{victim}</color> <color=#bbbbbb>died</color>";
            kills.Add(new KillEntry { text = s, until = Time.unscaledTime + 6 });
            if (kills.Count > 6) kills.RemoveAt(0);
        }

        public static void HitMarker() { hitT = 0.18f; Sfx.Play2D("hitmark", 0.5f); }
        public static void Hurt(Vector3 from) { hurtT = 0.8f; hurtFrom = from; }
        public static void Points(int n) { if (pointsT <= 0) pointsShown = 0; pointsShown += n; pointsT = 1.8f; }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            hitT -= dt; hurtT -= dt; pointsT -= dt;
            fpsT += dt; if (fpsT > 0.5f) { fps = 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime); fpsT = 0; }
        }

        void OnGUI()
        {
            if (!battle) return;
            UI.Begin();
            var pc = battle.control;
            switch (pc.mode)
            {
                case PlayerControl.Mode.Play: PlayHUD(pc); break;
                case PlayerControl.Mode.Dead: TopBar(); DeadScreen(pc); Feed(); break;
                case PlayerControl.Mode.Spawn: TopBar(); SpawnScreen(pc); Feed(); break;
                case PlayerControl.Mode.Over: Results(); break;
            }
            if (pc.mode == PlayerControl.Mode.Play && !pc.paused) TouchInput.Draw();
            if (pc.showScore && pc.mode != PlayerControl.Mode.Over) Scoreboard();
            if (pc.showMap && pc.mode == PlayerControl.Mode.Play) BigMap(pc);
            if (pc.paused) Pause(pc);
            if (pc.noticeT > 0) UI.Text(new Rect(0, 760, UI.W, 40), pc.notice, 26, UI.Bad, TextAnchor.MiddleCenter);
            if (Settings.ShowFps) UI.Text(new Rect(UI.W - 120, UI.H - 34, 110, 30), $"{fps:0} fps", 18, UI.Dim, TextAnchor.MiddleRight);
        }

        // ── top bar ──────────────────────────────────────────────────────
        void TopBar()
        {
            var b = battle;
            float w = UI.W;
            int me = b.player.team, them = 1 - me;
            Rect bar = new Rect(w / 2 - 330, 10, 660, 64);
            UI.Box(bar);
            UI.Text(new Rect(bar.x + 14, bar.y + 4, 260, 28), b.Faction(me).name.ToUpper(), 18, b.TeamColor(me));
            UI.Text(new Rect(bar.x + 14, bar.y + 24, 260, 40), Mathf.Max(0, Mathf.CeilToInt(b.teams[me].tickets)).ToString(), 34, Color.white);
            UI.Text(new Rect(bar.xMax - 274, bar.y + 4, 260, 28), b.Faction(them).name.ToUpper(), 18, b.TeamColor(them), TextAnchor.UpperRight);
            UI.Text(new Rect(bar.xMax - 274, bar.y + 24, 260, 40), Mathf.Max(0, Mathf.CeilToInt(b.teams[them].tickets)).ToString(), 34, Color.white, TextAnchor.UpperRight);
            if (b.land)
            {
                float pw = 30, total = b.posts.Count * (pw + 6);
                for (int i = 0; i < b.posts.Count; i++)
                {
                    var p = b.posts[i];
                    Rect r = new Rect(w / 2 - total / 2 + i * (pw + 6), bar.y + 16, pw, pw);
                    Color c = p.owner < 0 ? new Color(0.6f, 0.6f, 0.6f) : b.TeamColor(p.owner);
                    UI.Fill(r, new Color(0, 0, 0, 0.6f));
                    UI.Fill(new Rect(r.x + 3, r.y + 3, r.width - 6, r.height - 6), c * (p.contested ? (Mathf.Sin(Time.unscaledTime * 10) > 0 ? 1 : 0.4f) : 1));
                    if (p.capTeam >= 0 && p.capTeam != p.owner && p.progress > 0) UI.Bar(new Rect(r.x, r.yMax + 2, r.width, 4), p.progress, b.TeamColor(p.capTeam));
                }
            }
            else
            {
                for (int t = 0; t < 2; t++)
                {
                    var s = b.space.ships[t == 0 ? me : them];
                    Rect r = new Rect(w / 2 - 140, bar.y + 12 + t * 22, 280, 16);
                    UI.Bar(r, s.HullFrac, b.TeamColor(s.team));
                    UI.Text(r, (s.ShieldUp ? "SHIELDED  " : "") + $"hull {Mathf.CeilToInt(s.HullFrac * 100)}%", 14, Color.white, TextAnchor.MiddleCenter);
                }
            }
            for (int t = 0; t < 2; t++)
            {
                var ts = b.teams[t];
                int diff = b.land ? b.teams[1 - t].posts - ts.posts : 0;
                if (diff > 0)
                {
                    bool mine = t == me;
                    UI.Text(new Rect(mine ? bar.x + 90 : bar.xMax - 330, bar.y + 34, 240, 30), "▼ bleeding", 16, UI.Bad, mine ? TextAnchor.UpperLeft : TextAnchor.UpperRight);
                }
                if (b.land && ts.noPostTimer > 0 && t == me) UI.Text(new Rect(0, bar.yMax + 30, w, 30), $"NO COMMAND POSTS! Retake one or lose in {Mathf.CeilToInt(45 - ts.noPostTimer)} s", 24, UI.Bad, TextAnchor.MiddleCenter);
            }
        }

        void Feed()
        {
            float w = UI.W;
            float y = 90;
            for (int i = kills.Count - 1; i >= 0; i--)
            {
                if (kills[i].until < Time.unscaledTime) { kills.RemoveAt(i); continue; }
            }
            foreach (var k in kills) { UI.Text(new Rect(w - 620, y, 600, 26), k.text, 18, null, TextAnchor.UpperRight); y += 26; }
            y = 140;
            for (int i = msgs.Count - 1; i >= 0; i--) if (msgs[i].until < Time.unscaledTime) msgs.RemoveAt(i);
            foreach (var m in msgs) { UI.Text(new Rect(0, y + 120, w, 34), m.text, 26, m.col, TextAnchor.MiddleCenter); y += 34; }
        }

        // ── playing ──────────────────────────────────────────────────────
        void PlayHUD(PlayerControl pc)
        {
            var b = battle;
            var me = pc.Me;
            TopBar();
            Feed();
            if (me == null) return;
            WorldMarkers(me);
            Radar(me);
            float w = UI.W, h = UI.H;

            // crosshair
            var cam = Game.Cam;
            Vector2 c = new Vector2(w / 2, h / 2);
            if (me.InVehicle && me.vehicle.Flies && me.seat == 0)
            {
                Vector3 sp = cam.WorldToScreenPoint(me.vehicle.transform.position + me.vehicle.transform.forward * 300);
                if (sp.z > 0) c = new Vector2(sp.x / UI.S, (Screen.height - sp.y) / UI.S);
                // lead marker on the aim-assisted target
                Vector3 ap = cam.WorldToScreenPoint(pc.aimPoint);
                if (ap.z > 0 && (pc.aimPoint - (me.vehicle.transform.position + me.vehicle.transform.forward * 400)).sqrMagnitude > 4)
                    UI.Frame(new Rect(ap.x / UI.S - 10, (Screen.height - ap.y) / UI.S - 10, 20, 20), UI.Gold, 2);
            }
            Color cc = hitT > 0 ? UI.Bad : pc.assistTarget != null ? new Color(1f, 0.6f, 0.45f) : Color.white;
            bool zoom = !me.InVehicle && me.input.aim && me.Weapon.def.zoomFov > 0;
            if (zoom)
            {
                UI.Fill(new Rect(0, h / 2 - 1, w, 2), new Color(0, 0, 0, 0.6f));
                UI.Fill(new Rect(w / 2 - 1, 0, 2, h), new Color(0, 0, 0, 0.6f));
                UI.Dot(c, 3, UI.Bad);
            }
            else
            {
                float g = 8 + (me.InVehicle ? 0 : me.Weapon.def.spread * 4);
                UI.Fill(new Rect(c.x - g - 10, c.y - 1, 10, 2), cc); UI.Fill(new Rect(c.x + g, c.y - 1, 10, 2), cc);
                UI.Fill(new Rect(c.x - 1, c.y - g - 10, 2, 10), cc); UI.Fill(new Rect(c.x - 1, c.y + g, 2, 10), cc);
                UI.Dot(c, 1.5f, cc);
            }
            if (hitT > 0) { UI.Line(c + new Vector2(-14, -14), c + new Vector2(-6, -6), Color.white, 2); UI.Line(c + new Vector2(14, -14), c + new Vector2(6, -6), Color.white, 2); UI.Line(c + new Vector2(-14, 14), c + new Vector2(-6, 6), Color.white, 2); UI.Line(c + new Vector2(14, 14), c + new Vector2(6, 6), Color.white, 2); }
            if (hurtT > 0)
            {
                Vector3 to = hurtFrom - cam.transform.position; to.y = 0;
                Vector3 f = cam.transform.forward; f.y = 0;
                float ang = Vector3.SignedAngle(f, to, Vector3.up) * Mathf.Deg2Rad;
                Vector2 p = c + new Vector2(Mathf.Sin(ang), -Mathf.Cos(ang)) * 110;
                UI.Dot(p, 10, new Color(1, 0.2f, 0.1f, hurtT));
                UI.Fill(new Rect(0, 0, w, h), new Color(0.8f, 0, 0, hurtT * 0.12f));
            }

            if (Settings.AimDebug) AimDebug(pc, me, c);

            // pilot target lock (follow mode)
            if (me.InVehicle && me.vehicle.Flies && me.seat == 0)
            {
                if (pc.flyLock != null)
                {
                    var tgt = pc.flyLock;
                    Vector3 sp = cam.WorldToScreenPoint(tgt.AimPoint);
                    float d = (tgt.AimPoint - me.vehicle.transform.position).magnitude;
                    Color lc = new Color(0.4f, 1f, 1f);
                    if (sp.z > 0 && sp.x > 0 && sp.x < Screen.width && sp.y > 0 && sp.y < Screen.height)
                    {
                        Vector2 g = new Vector2(sp.x / UI.S, (Screen.height - sp.y) / UI.S);
                        float s2 = 26 + Mathf.Sin(Time.unscaledTime * 6) * 3;
                        UI.Line(g + new Vector2(0, -s2), g + new Vector2(s2, 0), lc, 3); UI.Line(g + new Vector2(s2, 0), g + new Vector2(0, s2), lc, 3);
                        UI.Line(g + new Vector2(0, s2), g + new Vector2(-s2, 0), lc, 3); UI.Line(g + new Vector2(-s2, 0), g + new Vector2(0, -s2), lc, 3);
                        UI.Text(new Rect(g.x - 160, g.y + s2 + 4, 320, 22), $"{PlayerControl.NameOf(tgt)}  {Mathf.RoundToInt(d)} m", 16, lc, TextAnchor.UpperCenter);
                        Vector3 lp = cam.WorldToScreenPoint(pc.lockLead);
                        if (lp.z > 0)
                        {
                            Vector2 lg = new Vector2(lp.x / UI.S, (Screen.height - lp.y) / UI.S);
                            UI.Line(g, lg, new Color(lc.r, lc.g, lc.b, 0.4f), 1);
                            UI.Frame(new Rect(lg.x - 9, lg.y - 9, 18, 18), UI.Gold, 2);
                            UI.Dot(lg, 2, UI.Gold);
                        }
                    }
                    else
                    {
                        // off screen: arrow on the edge pointing toward the target
                        Vector3 rel = cam.transform.InverseTransformPoint(tgt.AimPoint);
                        Vector2 dir = new Vector2(rel.x, -rel.y);
                        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.down;
                        dir.Normalize();
                        Vector2 ctr = new Vector2(w / 2, h / 2);
                        Vector2 edge = ctr + dir * Mathf.Min(w, h) * 0.42f;
                        Vector2 side = new Vector2(-dir.y, dir.x);
                        UI.Line(edge + dir * 22, edge - dir * 6 + side * 14, lc, 4);
                        UI.Line(edge + dir * 22, edge - dir * 6 - side * 14, lc, 4);
                        UI.Text(new Rect(edge.x - 120, edge.y + 18, 240, 22), $"{PlayerControl.NameOf(tgt)}  {Mathf.RoundToInt(d)} m", 15, lc, TextAnchor.UpperCenter);
                    }
                    string release = TouchInput.Active ? "LOCK again to release" : Ctl.PadActive ? "R3 to release" : "F to release";
                    UI.Text(new Rect(0, h / 2 + 100, w, 26), $"FOLLOWING TARGET - steer to pull away, {release}", 18, lc, TextAnchor.MiddleCenter);
                }
                else UI.Text(new Rect(0, h / 2 + 100, w, 26), TouchInput.Active ? "LOCK: follow the target nearest the crosshair" : Ctl.PadActive ? "R3: lock on and follow a target" : "F / middle mouse: lock on and follow a target", 16, new Color(1, 1, 1, 0.45f), TextAnchor.MiddleCenter);
            }

            // lock-on
            Transform lockT = null; float lockTime = 0;
            if (me.InVehicle && me.seat == 0) { lockT = me.vehicle.lockTarget; lockTime = me.vehicle.lockTime; if (me.vehicle.sec == null || !me.vehicle.sec.def.lockOn) lockT = null; }
            else if (!me.InVehicle && me.Weapon.def.lockOn) { lockT = me.lockTarget; lockTime = me.lockTime * 1.1f; }
            if (lockT)
            {
                Vector3 sp = cam.WorldToScreenPoint(lockT.position);
                if (sp.z > 0)
                {
                    float s = Mathf.Lerp(60, 26, Mathf.Clamp01(lockTime));
                    var r = new Rect(sp.x / UI.S - s / 2, (Screen.height - sp.y) / UI.S - s / 2, s, s);
                    UI.Frame(r, lockTime >= 1 ? UI.Bad : UI.Gold, 2);
                    if (lockTime >= 1) UI.Text(new Rect(r.x - 40, r.yMax, r.width + 80, 24), "LOCKED", 16, UI.Bad, TextAnchor.UpperCenter);
                }
            }

            // bottom left: health + class
            Rect hb = new Rect(20, h - 120, 420, 100);
            UI.Box(hb);
            if (me.InVehicle)
            {
                var v = me.vehicle;
                UI.Text(new Rect(hb.x + 14, hb.y + 8, 400, 28), v.def.name + (me.seat == 0 ? "  - driver" : me.seat == 1 ? "  - gunner" : "  - passenger"), 20);
                UI.Bar(new Rect(hb.x + 14, hb.y + 44, 392, 18), v.hp / v.maxHp, Color.Lerp(UI.Bad, UI.Good, v.hp / v.maxHp));
                UI.Text(new Rect(hb.x + 14, hb.y + 42, 392, 22), $"{Mathf.CeilToInt(v.hp)} / {Mathf.CeilToInt(v.maxHp)}", 16, Color.white, TextAnchor.MiddleCenter);
                if (v.Flies) UI.Text(new Rect(hb.x + 14, hb.y + 68, 392, 26), $"speed {Mathf.RoundToInt(v.speed)}   throttle {Mathf.RoundToInt(v.throttle * 100)}%{(v.landed ? "   LANDED" : "")}", 18, UI.Dim);
            }
            else
            {
                UI.Text(new Rect(hb.x + 14, hb.y + 8, 400, 28), me.ClassName + (me.IsHero ? "  (hero)" : ""), 22, me.IsHero ? UI.Gold : Color.white);
                UI.Bar(new Rect(hb.x + 14, hb.y + 44, 392, 18), me.hp / me.maxHp, Color.Lerp(UI.Bad, UI.Good, me.hp / me.maxHp));
                UI.Text(new Rect(hb.x + 14, hb.y + 42, 392, 22), $"{Mathf.CeilToInt(me.hp)}", 16, Color.white, TextAnchor.MiddleCenter);
                string extra = "";
                if (me.cls != null && me.cls.special == Special.Jetpack) extra = $"jump-pack {Mathf.RoundToInt(me.jetFuel * 100)}%";
                if (me.AbilityName != null) extra += (extra.Length > 0 ? "   " : "") + (me.abilityCd > 0 ? $"{me.AbilityName} {Mathf.CeilToInt(me.abilityCd)}s" : $"[F] {me.AbilityName} ready");
                UI.Text(new Rect(hb.x + 14, hb.y + 68, 392, 26), extra, 18, UI.Dim);
            }

            // bottom right: weapon
            Rect wb = new Rect(w - 440, h - 120, 420, 100);
            UI.Box(wb);
            WeaponState ws = null; string wname;
            if (me.InVehicle)
            {
                var v = me.vehicle;
                if (me.seat == 0) { ws = v.prim; wname = v.prim != null ? v.prim.def.name + (v.sec != null ? $"  +  [RMB] {v.sec.def.name}" : "") : ""; }
                else if (me.seat == 1) { ws = v.gunW; wname = v.gunW != null ? v.gunW.def.name : ""; }
                else wname = "riding";
            }
            else { ws = me.Weapon; wname = ws.def.name + (me.weapons[1] != null ? $"   [Q] {me.weapons[1 - me.cur]?.def.name}" : ""); }
            UI.Text(new Rect(wb.x + 14, wb.y + 8, 400, 28), wname, 20);
            if (ws != null)
            {
                if (ws.def.UsesHeat) UI.Bar(new Rect(wb.x + 14, wb.y + 44, 392, 14), ws.heat, ws.overheated ? UI.Bad : Color.Lerp(UI.Gold, UI.Bad, ws.heat));
                UI.Text(new Rect(wb.x + 14, wb.y + 40, 392, 22), ws.Status, 16, Color.white, TextAnchor.MiddleRight);
            }
            if (!me.InVehicle && me.cls != null && me.cls.equip != Equip.None)
                UI.Text(new Rect(wb.x + 14, wb.y + 68, 392, 26), $"[G] {EquipName(me.cls.equip)} x{me.equipLeft}" + (me.HasDetpacksOut ? "  (G again to detonate)" : ""), 18, UI.Dim);
            if (me.InVehicle && me.seat == 0 && me.vehicle.sec != null) UI.Text(new Rect(wb.x + 14, wb.y + 68, 392, 26), $"{me.vehicle.sec.def.name}: {me.vehicle.sec.Status}", 18, UI.Dim);

            // hero / points / award progress
            var pp = b.player;
            Rect hp2 = new Rect(w / 2 - 210, h - 54, 420, 40);
            if (!me.IsHero)
            {
                UI.Bar(new Rect(hp2.x, hp2.y + 22, hp2.width, 8), pp.HeroProgress, UI.Gold);
                UI.Text(new Rect(hp2.x, hp2.y - 4, hp2.width, 26), pp.heroReady ? "HERO READY - press H" : $"{pp.points} points - hero {Mathf.RoundToInt(pp.HeroProgress * 100)}%", 18, pp.heroReady ? UI.Gold : UI.Dim, TextAnchor.MiddleCenter);
            }
            if (pointsT > 0) UI.Text(new Rect(c.x + 40, c.y + 20, 200, 30), $"+{pointsShown}", 24, UI.Gold);

            // capture progress
            if (b.land)
                foreach (var p in b.posts)
                {
                    Vector3 d = me.transform.position - p.Pos; d.y = 0;
                    if (d.magnitude > p.radius || me.InVehicle) continue;
                    Rect r = new Rect(w / 2 - 200, 180, 400, 54);
                    UI.Box(r);
                    string st = p.contested ? "CONTESTED" : p.owner == me.team && p.capTeam == me.team && p.progress >= 1 ? "Secured" : p.capTeam == me.team ? "Capturing..." : "Neutralising...";
                    UI.Text(new Rect(r.x, r.y + 2, r.width, 26), $"{p.postName}  -  {st}", 20, Color.white, TextAnchor.MiddleCenter);
                    UI.Bar(new Rect(r.x + 12, r.y + 32, r.width - 24, 12), p.progress, p.capTeam >= 0 ? b.TeamColor(p.capTeam) : Color.gray);
                }

            // prompts
            string prompt = null;
            if (!me.InVehicle) { var nv = pc.NearbyVehicle(); if (nv) prompt = $"[E] enter {nv.def.name}"; }
            else if (me.vehicle.IsShip && me.vehicle.CanExitHere()) prompt = "[E] land and get out";
            if (prompt != null) UI.Text(new Rect(0, h / 2 + 70, w, 30), prompt, 22, Color.white, TextAnchor.MiddleCenter);
            if (pc.outOfBounds > 0) UI.Text(new Rect(0, h / 2 - 140, w, 40), $"RETURN TO THE BATTLEFIELD  {Mathf.CeilToInt(10 - pc.outOfBounds)}", 30, UI.Bad, TextAnchor.MiddleCenter);
            if (b.space) SpaceStatus(me);
        }

        void AimDebug(PlayerControl pc, Unit me, Vector2 c)
        {
            var cam = Game.Cam;
            int shots = Shots.PlayerShots, hits = Shots.PlayerHits;
            string miss = Shots.LastMissBy >= 0 && Time.time - Shots.LastMissTime < 3 ? $"   last miss passed {Shots.LastMissBy:0.0} m from an enemy" : "";
            string tgt = pc.assistTarget != null ? $"assist: {PlayerControl.NameOf(pc.assistTarget)} {(pc.assistOnTarget ? "(shots bent onto it)" : "(pulling)")}" : AimAssist.On ? "assist: no target in range" : "assist: off for this device";
            float range = me.InVehicle ? (me.vehicle.prim != null ? me.vehicle.prim.def.EffectiveRange : 0) : me.Weapon.def.EffectiveRange;
            UI.Text(new Rect(c.x - 400, c.y + 120, 800, 24), $"hits {hits} / {shots}  ({(shots > 0 ? hits * 100 / shots : 0)}%){miss}", 17, UI.Gold, TextAnchor.MiddleCenter);
            UI.Text(new Rect(c.x - 400, c.y + 144, 800, 24), $"{tgt}   gun range {Mathf.RoundToInt(range)} m   device {Settings.DeviceNames[(int)Settings.Current]}", 15, UI.Dim, TextAnchor.MiddleCenter);
            if (pc.assistTarget != null)
            {
                Vector3 sp = cam.WorldToScreenPoint(pc.assistTarget.AimPoint);
                if (sp.z > 0) UI.Frame(new Rect(sp.x / UI.S - 14, (Screen.height - sp.y) / UI.S - 14, 28, 28), pc.assistOnTarget ? UI.Bad : UI.Gold, 2);
                Vector3 ap = cam.WorldToScreenPoint(pc.aimPoint);
                if (sp.z > 0 && ap.z > 0) UI.Dot(new Vector2(ap.x / UI.S, (Screen.height - ap.y) / UI.S), 3, UI.Good);
            }
        }

        public static string EquipName(Equip e) => e == Equip.Grenade ? "Grenades" : e == Equip.Mine ? "Mines" : e == Equip.Detpack ? "Detpacks" : e == Equip.Supply ? "Supply crates" : "";

        void WorldMarkers(Unit me)
        {
            var cam = Game.Cam;
            var b = battle;
            Vector3 mp = me.InVehicle ? me.vehicle.transform.position : me.transform.position;
            if (b.land)
                foreach (var p in b.posts)
                {
                    Vector3 sp = cam.WorldToScreenPoint(p.Pos + Vector3.up * 8);
                    if (sp.z <= 0) continue;
                    float d = (p.Pos - mp).magnitude;
                    Vector2 g = new Vector2(sp.x / UI.S, (Screen.height - sp.y) / UI.S);
                    Color c = p.owner < 0 ? new Color(0.85f, 0.85f, 0.85f) : b.TeamColor(p.owner);
                    UI.Fill(new Rect(g.x - 7, g.y - 7, 14, 14), new Color(0, 0, 0, 0.6f));
                    UI.Fill(new Rect(g.x - 5, g.y - 5, 10, 10), c);
                    UI.Text(new Rect(g.x - 120, g.y + 8, 240, 22), $"{p.postName}  {Mathf.RoundToInt(d)}m", 15, c, TextAnchor.UpperCenter);
                }
            // friendly name tags nearby
            foreach (var u in b.units)
            {
                if (!u || !u.Alive || u == me || u.InVehicle) continue;
                float d = (u.transform.position - mp).magnitude;
                if (u.team == me.team)
                {
                    bool mate = b.commander != null && b.commander.SquadOf(u.part) == b.commander.SquadOf(b.player) && b.commander.SquadOf(u.part) != null;
                    if (d > (mate ? 150 : 45)) continue;
                    Vector3 sp = cam.WorldToScreenPoint(u.transform.position + Vector3.up * (u.Height + 0.4f));
                    if (sp.z <= 0) continue;
                    UI.Text(new Rect(sp.x / UI.S - 80, (Screen.height - sp.y) / UI.S - 20, 160, 20), (mate ? "◆ " : "") + (u.IsHero ? u.hero.name : u.displayName), 14, mate ? SquadColor : b.TeamColor(u.team), TextAnchor.LowerCenter);
                }
                else if (u.IsHero && d < 160)
                {
                    Vector3 sp = cam.WorldToScreenPoint(u.transform.position + Vector3.up * (u.Height + 0.6f));
                    if (sp.z <= 0) continue;
                    UI.Text(new Rect(sp.x / UI.S - 120, (Screen.height - sp.y) / UI.S - 22, 240, 22), "ENEMY HERO: " + u.hero.name, 15, UI.Bad, TextAnchor.LowerCenter);
                }
            }
            if (b.space)
            {
                var enemy = b.space.ships[1 - me.team];
                foreach (var s in enemy.systems)
                {
                    if (s.destroyed) continue;
                    Vector3 sp = cam.WorldToScreenPoint(s.AimPoint);
                    if (sp.z <= 0) continue;
                    float d = (s.AimPoint - mp).magnitude;
                    if (s.interior && d > 60) continue;
                    if (!s.interior && d > 1400) continue;
                    Vector2 g = new Vector2(sp.x / UI.S, (Screen.height - sp.y) / UI.S);
                    float sz = Mathf.Clamp(1600 / Mathf.Max(1, d), 10, 40);
                    UI.Frame(new Rect(g.x - sz, g.y - sz, sz * 2, sz * 2), s.critical ? UI.Bad : new Color(1, 0.7f, 0.4f), 2);
                    if (d < 700 || s.critical) UI.Text(new Rect(g.x - 100, g.y + sz + 2, 200, 20), $"{s.sysName} {Mathf.CeilToInt(s.hp / s.maxHp * 100)}%", 14, s.critical ? UI.Bad : new Color(1, 0.7f, 0.4f), TextAnchor.UpperCenter);
                }
                // hangar entrance marker
                Vector3 hm = cam.WorldToScreenPoint(enemy.transform.TransformPoint(new Vector3(0, 10, 32)));
                if (hm.z > 0 && enemy.HangarOpen) UI.Text(new Rect(hm.x / UI.S - 100, (Screen.height - hm.y) / UI.S - 10, 200, 20), "▼ ENEMY HANGAR", 15, UI.Gold, TextAnchor.MiddleCenter);
                var home = b.space.ships[me.team];
                Vector3 hh = cam.WorldToScreenPoint(home.transform.TransformPoint(new Vector3(0, 10, 32)));
                if (hh.z > 0 && me.InVehicle) UI.Text(new Rect(hh.x / UI.S - 100, (Screen.height - hh.y) / UI.S - 10, 200, 20), "▼ our hangar", 14, UI.Good, TextAnchor.MiddleCenter);
            }
        }

        public static readonly Color SquadColor = new Color(0.45f, 1f, 0.55f);

        void Radar(Unit me)
        {
            var b = battle;
            Rect r = new Rect(20, 20, 230, 230);
            UI.Box(r, new Color(0.02f, 0.06f, 0.05f, 0.75f));
            Vector2 c = r.center;
            float range = b.space ? 900 : 120;
            var cam = Game.Cam;
            float yaw = cam.transform.eulerAngles.y * Mathf.Deg2Rad;
            Vector3 mp = me.InVehicle ? me.vehicle.transform.position : me.transform.position;
            Vector2 Map(Vector3 w)
            {
                Vector3 d = w - mp;
                float x = d.x * Mathf.Cos(yaw) - d.z * Mathf.Sin(yaw);
                float z = d.x * Mathf.Sin(yaw) + d.z * Mathf.Cos(yaw);
                Vector2 v = new Vector2(x, -z) / range * (r.width / 2 - 6);
                if (v.magnitude > r.width / 2 - 6) v = v.normalized * (r.width / 2 - 6);
                return c + v;
            }
            UI.Fill(new Rect(c.x - 0.5f, r.y + 6, 1, r.height - 12), new Color(1, 1, 1, 0.08f));
            UI.Fill(new Rect(r.x + 6, c.y - 0.5f, r.width - 12, 1), new Color(1, 1, 1, 0.08f));
            if (b.land)
                foreach (var p in b.posts) { var q = Map(p.Pos); Color pc = p.owner < 0 ? Color.white : b.TeamColor(p.owner); UI.Fill(new Rect(q.x - 6, q.y - 6, 12, 12), Color.black); UI.Fill(new Rect(q.x - 4, q.y - 4, 8, 8), pc); }
            else foreach (var s in b.space.ships) { var q = Map(s.transform.position); UI.Fill(new Rect(q.x - 10, q.y - 4, 20, 8), b.TeamColor(s.team)); }
            bool recon = b.Recon(me.team);
            var mySquad = b.commander != null ? b.commander.SquadOf(b.player) : null;
            if (mySquad != null && mySquad.target)
            {
                UI.Text(new Rect(r.x, r.yMax + 4, 360, 22), $"SQUAD {mySquad.id + 1}: {Commander.OrderName(mySquad.order)} {mySquad.target.postName}{(mySquad.gathering ? " (gathering)" : "")}", 16, SquadColor);
                var q = Map(mySquad.target.Pos); UI.Frame(new Rect(q.x - 8, q.y - 8, 16, 16), SquadColor, 2);
            }
            foreach (var u in b.units)
            {
                if (!u || !u.Alive || u == me || u.InVehicle) continue;
                bool friend = u.team == me.team;
                if (!friend && !recon && !(Time.time - u.lastFire < 1.5f) && (u.transform.position - mp).sqrMagnitude > 25 * 25) continue;
                if (!friend && u.Cloaked) continue;
                bool mate = friend && mySquad != null && mySquad.members.Contains(u.part);
                UI.Dot(Map(u.transform.position), u.IsHero || mate ? 4 : 2.5f, mate ? SquadColor : friend ? b.TeamColor(u.team) : UI.Bad);
            }
            foreach (var v in b.vehicles)
            {
                if (!v || !v.Alive || v == me.vehicle) continue;
                bool friend = v.Team == me.team;
                if (!friend && v.IsEmpty) continue;
                var q = Map(v.transform.position);
                UI.Fill(new Rect(q.x - 3.5f, q.y - 3.5f, 7, 7), friend ? b.TeamColor(v.Team) : UI.Bad);
            }
            UI.Fill(new Rect(c.x - 3, c.y - 3, 6, 6), Color.white);
            UI.Line(c, c + new Vector2(0, -14), Color.white, 2);
        }

        void SpaceStatus(Unit me)
        {
            var b = battle;
            float x = UI.W - 290, y = 90 + 6 * 26 + 20;
            for (int t = 0; t < 2; t++)
            {
                var s = b.space.ships[t == 0 ? me.team : 1 - me.team];
                Rect r = new Rect(x, y, 270, 30 + s.systems.Count * 0 + 7 * 22);
                UI.Box(r);
                UI.Text(new Rect(r.x + 10, r.y + 4, 260, 24), (t == 0 ? "OUR SHIP: " : "ENEMY: ") + s.faction.capitalName, 15, b.TeamColor(s.team));
                float yy = r.y + 28;
                var shown = new HashSet<SysKind>();
                foreach (var sys in s.systems)
                {
                    if (!shown.Add(sys.kind)) continue;
                    int alive = 0, total = 0;
                    foreach (var o in s.systems) if (o.kind == sys.kind) { total++; if (!o.destroyed) alive++; }
                    string n = sys.kind == SysKind.Turret ? "Turrets" : sys.kind == SysKind.Engine ? "Engines" : sys.sysName;
                    Color col = alive == 0 ? new Color(0.5f, 0.5f, 0.5f) : alive < total ? UI.Gold : Color.white;
                    UI.Text(new Rect(r.x + 12, yy, 250, 22), $"{(sys.critical ? "◆ " : "   ")}{n}" + (total > 1 ? $" {alive}/{total}" : alive == 0 ? "  - destroyed" : ""), 15, col);
                    yy += 20;
                }
                y = r.yMax + 8;
            }
        }

        // ── dead ─────────────────────────────────────────────────────────
        void DeadScreen(PlayerControl pc)
        {
            var k = pc.killer;
            string s = k ? $"Killed by <color=#{UI.Hex(battle.TeamColor(k.team))}>{(k.IsHero ? k.hero.name : k.displayName)}</color>" : "You were killed";
            UI.Text(new Rect(0, UI.H * 0.6f, UI.W, 40), s, 30, Color.white, TextAnchor.MiddleCenter);
        }

        // ── spawn screen ─────────────────────────────────────────────────
        void SpawnScreen(PlayerControl pc)
        {
            var b = battle;
            var f = b.Faction(b.player.team);
            float w = UI.W;
            Rect left = new Rect(30, 100, 520, 860);
            UI.Box(left);
            UI.Text(new Rect(left.x + 20, left.y + 12, 480, 34), b.space ? "CHOOSE YOUR CRAFT OR ROLE" : "CHOOSE YOUR CLASS", 26, UI.Gold);
            float y = left.y + 60;
            if (!b.space)
            {
                for (int i = 0; i < 5; i++)
                {
                    var cd = f.classes[i];
                    bool locked = pc.ClassLocked(i);
                    bool award = b.player.awards[i];
                    if (UI.Button(new Rect(left.x + 20, y, 480, 48), (locked ? "[locked] " : "") + cd.name + (award ? "  ★" : ""), pc.selClass == i && pc.selHero < 0, !locked, 24)) { pc.selClass = i; pc.selHero = -1; }
                    y += 54;
                }
                var c = f.classes[pc.selClass];
                y += 6;
                UI.Text(new Rect(left.x + 20, y, 480, 80), c.blurb, 19, UI.Dim, TextAnchor.UpperLeft, true); y += 56;
                bool hasAward = b.player.awards[pc.selClass];
                UI.Text(new Rect(left.x + 20, y, 480, 26), $"Weapon: {(hasAward ? c.award.name + " (award)" : c.primary.name)}", 19); y += 26;
                UI.Text(new Rect(left.x + 20, y, 480, 26), $"Sidearm: {c.secondary.name}", 19); y += 26;
                UI.Text(new Rect(left.x + 20, y, 480, 26), $"Gear: {EquipName(c.equip)} x{c.equipCount}", 19); y += 26;
                UI.Text(new Rect(left.x + 20, y, 480, 26), hasAward ? "Award weapon earned this battle" : $"Award: {c.awardName} - {b.player.classKills[pc.selClass]}/6 kills with this class (or 4 in one life)", 17, hasAward ? UI.Gold : UI.Dim, TextAnchor.UpperLeft, true); y += 46;
            }
            else
            {
                for (int i = 0; i < 4; i++)
                {
                    var sd = f.ships[i];
                    if (UI.Button(new Rect(left.x + 20, y, 480, 46), sd.name, !pc.spaceOnFoot && pc.selShip == i && pc.selHero < 0, true, 22)) { pc.selShip = i; pc.spaceOnFoot = false; pc.selHero = -1; }
                    y += 51;
                }
                for (int i = 0; i < 2; i++)
                {
                    var cd = i == 0 ? f.pilot : f.marine;
                    if (UI.Button(new Rect(left.x + 20, y, 480, 46), "On foot: " + cd.name, pc.spaceOnFoot && pc.selFoot == i && pc.selHero < 0, true, 22)) { pc.selFoot = i; pc.spaceOnFoot = true; pc.selHero = -1; }
                    y += 51;
                }
                y += 4;
                string info = pc.spaceOnFoot ? (pc.selFoot == 0 ? f.pilot : f.marine).blurb : f.ships[pc.selShip].blurb;
                UI.Text(new Rect(left.x + 20, y, 480, 70), info, 19, UI.Dim, TextAnchor.UpperLeft, true); y += 54;
                if (!pc.spaceOnFoot) { var sd = f.ships[pc.selShip]; UI.Text(new Rect(left.x + 20, y, 480, 26), $"Guns: {sd.primary.name}   Special: {sd.secondary.name}", 18); y += 28; }
                var home = b.space.ships[b.player.team];
                UI.Text(new Rect(left.x + 20, y, 480, 26), home.HangarOpen ? "Craft launch from your hangar." : "Hangar destroyed - craft launch outside the ship.", 17, home.HangarOpen ? UI.Dim : UI.Bad); y += 30;
            }
            // heroes
            y += 6;
            UI.Text(new Rect(left.x + 20, y, 480, 26), b.player.heroReady ? "HERO UNLOCKED - choose one:" : $"Heroes: {Mathf.RoundToInt(b.player.HeroProgress * 100)}% ({b.player.points} pts)", 20, b.player.heroReady ? UI.Gold : UI.Dim); y += 30;
            for (int i = 0; i < 4; i++)
            {
                var hd = f.heroes[i];
                if (UI.Button(new Rect(left.x + 20 + (i % 2) * 243, y + (i / 2) * 46, 237, 40), hd.name, pc.selHero == i, b.player.heroReady, 17)) pc.selHero = i;
            }
            y += 96;
            if (pc.selHero >= 0) UI.Text(new Rect(left.x + 20, y, 480, 60), $"{f.heroes[pc.selHero].title}: {f.heroes[pc.selHero].blurb} [F] {f.heroes[pc.selHero].abilityName}", 17, UI.Dim, TextAnchor.UpperLeft, true);

            // map + posts
            Rect map = new Rect(580, 100, Mathf.Min(860, w - 610), 760);
            UI.Box(map);
            float sq = Mathf.Min(map.height - 20, map.width - 260);
            if (b.land) DrawLandMap(new Rect(map.x + 10, map.y + 10, sq, sq), pc, true);
            else DrawSpaceMap(new Rect(map.x + 10, map.y + 10, map.width - 20, map.height - 20));
            if (b.land) SpawnList(pc, new Rect(map.x + sq + 22, map.y + 12, map.width - sq - 34, map.height - 24));

            string title = b.cfg.title ?? (b.land ? b.cfg.land.name : b.cfg.spaceMap.name);
            UI.Text(new Rect(580, 870, map.width, 34), title, 26, Color.white);
            UI.Text(new Rect(580, 902, map.width, 60), b.land ? b.cfg.land.blurb : b.cfg.spaceMap.blurb, 18, UI.Dim, TextAnchor.UpperLeft, true);
            bool can = b.CanSpawn(b.player.team);
            if (UI.Button(new Rect(w - 360, 990, 330, 64), pc.selHero >= 0 && b.player.heroReady ? "SPAWN AS HERO" : "SPAWN  (Enter)", false, can, 28, new Color(0.2f, 0.6f, 0.3f)))
                pc.TrySpawn(pc.selHero >= 0 && b.player.heroReady);
            if (UI.Button(new Rect(30, 990, 220, 64), "CONTROLS", helpOpen, true, 22)) helpOpen = !helpOpen;
            if (helpOpen) Controls(new Rect(w / 2 - 420, 160, 840, 700));
        }

        /// <summary>Posts you own (greyed while an enemy stands in one) and your squadmates (greyed unless safe).</summary>
        void SpawnList(PlayerControl pc, Rect r)
        {
            var b = battle;
            float yy = r.y;
            UI.Text(new Rect(r.x, yy, r.width, 26), "SPAWN AT", 20, UI.Gold); yy += 30;
            foreach (var p in b.posts)
            {
                if (p.owner != b.player.team) continue;
                bool ok = p.SpawnableFor(b.player.team);
                if (UI.Button(new Rect(r.x, yy, r.width, 36), ok ? p.postName : p.postName + " (attacked)", pc.selMate == null && pc.selPost == p.index, ok, 17)) { pc.selPost = p.index; pc.selMate = null; }
                yy += 40;
            }
            var sq = b.commander != null ? b.commander.SquadOf(b.player) : null;
            if (sq == null) return;
            yy += 8;
            UI.Text(new Rect(r.x, yy, r.width, 26), $"SQUAD {sq.id + 1}", 20, UI.Gold); yy += 26;
            UI.Text(new Rect(r.x, yy, r.width, 22), "Orders: " + Commander.OrderName(sq.order) + (sq.target ? " " + sq.target.postName : ""), 15, UI.Dim); yy += 26;
            foreach (var m in sq.members)
            {
                if (m.isPlayer) continue;
                string why = b.MateBlocked(m.unit);
                bool ok = why == null;
                string label = m.name + (ok ? "" : $" ({why})");
                if (UI.Button(new Rect(r.x, yy, r.width, 36), label, pc.selMate == m, ok, 15)) pc.selMate = m;
                yy += 40;
            }
            UI.Text(new Rect(r.x, yy, r.width, 60), $"Spawn beside a squadmate when no enemy is within {Battle.MateSafeRadius:0} m and they haven't been hit for {Battle.MateHurtSafe:0} s.", 14, UI.Dim, TextAnchor.UpperLeft, true);
        }

        public void DrawLandMap(Rect r, PlayerControl pc, bool pick)
        {
            var b = battle;
            var land = b.land;
            GUI.DrawTextureWithTexCoords(r, land.minimap, new Rect(0, 1, 1, -1));
            Vector2 M(Vector3 p) => new Vector2(r.x + (p.x + land.half) / land.size * r.width, r.y + (1 - (p.z + land.half) / land.size) * r.height);
            foreach (var p in b.posts)
            {
                var q = M(p.Pos);
                Color c = p.owner < 0 ? new Color(0.9f, 0.9f, 0.9f) : b.TeamColor(p.owner);
                bool sel = pc != null && pc.selPost == p.index;
                float s = sel ? 16 : 12;
                UI.Fill(new Rect(q.x - s - 2, q.y - s - 2, s * 2 + 4, s * 2 + 4), sel ? UI.Gold : Color.black);
                UI.Fill(new Rect(q.x - s, q.y - s, s * 2, s * 2), c);
                UI.Text(new Rect(q.x - 100, q.y + s + 2, 200, 22), p.postName, 16, Color.white, TextAnchor.UpperCenter);
                if (p.UnderAttack && Mathf.Repeat(Time.unscaledTime, 1) < 0.5f) UI.Frame(new Rect(q.x - s - 5, q.y - s - 5, s * 2 + 10, s * 2 + 10), UI.Bad, 2);
                if (pick && p.owner == b.player.team && Event.current.type == EventType.MouseDown && new Rect(q.x - 20, q.y - 20, 40, 40).Contains(Event.current.mousePosition)) { pc.selPost = p.index; pc.selMate = null; Sfx.Play2D("ui"); }
            }
            var mySquad = b.commander != null ? b.commander.SquadOf(b.player) : null;
            foreach (var u in b.units)
                if (u && u.Alive && u.team == b.player.team)
                {
                    bool mate = mySquad != null && !u.part.isPlayer && mySquad.members.Contains(u.part);
                    var q = M(u.transform.position);
                    if (mate)
                    {
                        bool sel = pc != null && pc.selMate == u.part;
                        UI.Fill(new Rect(q.x - 6, q.y - 6, 12, 12), sel ? Color.white : Color.black);
                        UI.Fill(new Rect(q.x - 4, q.y - 4, 8, 8), SquadColor);
                        if (pick) UI.Text(new Rect(q.x - 80, q.y + 6, 160, 18), u.part.name, 13, SquadColor, TextAnchor.UpperCenter);
                        if (pick && Event.current.type == EventType.MouseDown && new Rect(q.x - 14, q.y - 14, 28, 28).Contains(Event.current.mousePosition)) { pc.selMate = u.part; Sfx.Play2D("ui"); }
                    }
                    else UI.Dot(q, u.part.isPlayer ? 5 : 2.5f, u.part.isPlayer ? Color.white : b.TeamColor(u.team));
                }
            if (mySquad != null && mySquad.target) { var q = M(mySquad.target.Pos); UI.Frame(new Rect(q.x - 22, q.y - 22, 44, 44), SquadColor, 2); }
            foreach (var v in b.vehicles)
                if (v && v.Alive && v.Team == b.player.team && !v.IsEmpty) UI.Fill(new Rect(M(v.transform.position).x - 4, M(v.transform.position).y - 4, 8, 8), b.TeamColor(v.Team));
        }

        void DrawSpaceMap(Rect r)
        {
            var b = battle;
            UI.Fill(r, new Color(0, 0, 0.03f, 0.8f));
            for (int t = 0; t < 2; t++)
            {
                var s = b.space.ships[t];
                float y = t == b.player.team ? r.yMax - 190 : r.y + 30;
                Rect sh = new Rect(r.x + 40, y, r.width - 80, 140);
                UI.Fill(sh, b.TeamColor(t) * new Color(1, 1, 1, 0.25f));
                UI.Frame(sh, b.TeamColor(t), 2);
                UI.Text(new Rect(sh.x + 10, sh.y + 6, sh.width, 26), $"{s.faction.capitalName}  -  hull {Mathf.CeilToInt(s.HullFrac * 100)}%  {(s.ShieldUp ? "(shielded)" : "(shields down)")}", 20, Color.white);
                float x = sh.x + 14; float yy = sh.y + 40;
                foreach (var sys in s.systems)
                {
                    Color c = sys.destroyed ? new Color(0.3f, 0.3f, 0.3f) : Color.Lerp(UI.Bad, UI.Good, sys.hp / sys.maxHp);
                    Rect cell = new Rect(x, yy, 150, 40);
                    UI.Fill(cell, new Color(0, 0, 0, 0.5f));
                    UI.Text(new Rect(cell.x + 6, cell.y + 2, 144, 20), (sys.critical ? "◆ " : "") + sys.sysName, 14, c);
                    UI.Bar(new Rect(cell.x + 6, cell.y + 26, 136, 6), sys.hp / sys.maxHp, c);
                    x += 156; if (x + 150 > sh.xMax) { x = sh.x + 14; yy += 46; }
                }
            }
            UI.Text(new Rect(r.x, r.center.y - 60, r.width, 120),
                "Win by: running the enemy out of tickets, destroying their capital ship's hull,\nor destroying all their critical systems (◆ engines, life support, reactor).\nLife Support and the Reactor are inside: fly into the enemy hangar, land, get out and sabotage them.",
                18, UI.Dim, TextAnchor.MiddleCenter, true);
        }

        void BigMap(PlayerControl pc)
        {
            if (battle.land)
            {
                Rect r = new Rect(UI.W / 2 - 420, 120, 840, 840);
                UI.Box(r);
                DrawLandMap(new Rect(r.x + 10, r.y + 10, r.width - 20, r.height - 20), pc, false);
            }
            else
            {
                Rect r = new Rect(UI.W / 2 - 520, 140, 1040, 760);
                UI.Box(r);
                DrawSpaceMap(new Rect(r.x + 10, r.y + 10, r.width - 20, r.height - 20));
            }
        }

        // ── scoreboard ───────────────────────────────────────────────────
        void Scoreboard()
        {
            var b = battle;
            float w = UI.W;
            Rect r = new Rect(w / 2 - 700, 120, 1400, 820);
            UI.Box(r, new Color(0.03f, 0.04f, 0.06f, 0.9f));
            for (int t = 0; t < 2; t++)
            {
                int team = t == 0 ? b.player.team : 1 - b.player.team;
                float x = r.x + 20 + t * 690;
                UI.Text(new Rect(x, r.y + 12, 660, 32), $"{b.Faction(team).name}  -  {Mathf.Max(0, Mathf.CeilToInt(b.teams[team].tickets))} tickets", 24, b.TeamColor(team));
                UI.Text(new Rect(x, r.y + 48, 660, 24), "NAME                                   CLASS                         PTS    K    D", 16, UI.Dim);
                var list = b.people.FindAll(p => p.team == team);
                list.Sort((a, c) => c.points.CompareTo(a.points));
                float y = r.y + 74;
                foreach (var p in list)
                {
                    if (y > r.yMax - 24) break;
                    string cls = p.unit && p.unit.Alive ? p.unit.ClassName : "-";
                    Color col = p.isPlayer ? UI.Gold : Color.white;
                    UI.Text(new Rect(x, y, 260, 24), p.name, 17, col);
                    UI.Text(new Rect(x + 260, y, 240, 24), cls, 17, p.unit && p.unit.IsHero ? UI.Gold : UI.Dim);
                    UI.Text(new Rect(x + 500, y, 50, 24), p.points.ToString(), 17, col, TextAnchor.UpperRight);
                    UI.Text(new Rect(x + 560, y, 40, 24), p.kills.ToString(), 17, col, TextAnchor.UpperRight);
                    UI.Text(new Rect(x + 610, y, 40, 24), p.deaths.ToString(), 17, col, TextAnchor.UpperRight);
                    y += 22;
                }
            }
        }

        // ── pause / results ──────────────────────────────────────────────
        void Pause(PlayerControl pc)
        {
            float w = UI.W;
            UI.Fill(new Rect(0, 0, w, UI.H), new Color(0, 0, 0, 0.55f));
            Rect r = new Rect(w / 2 - 220, 300, 440, 420);
            UI.Box(r);
            UI.Text(new Rect(r.x, r.y + 20, r.width, 40), "PAUSED", 34, UI.Gold, TextAnchor.MiddleCenter);
            if (UI.Button(new Rect(r.x + 40, r.y + 100, 360, 60), "RESUME")) { pc.paused = false; Time.timeScale = 1; Ctl.LockCursor(pc.mode == PlayerControl.Mode.Play); }
            if (UI.Button(new Rect(r.x + 40, r.y + 175, 360, 60), "CONTROLS", helpOpen)) helpOpen = !helpOpen;
            if (UI.Button(new Rect(r.x + 40, r.y + 250, 360, 60), battle.cfg.conquest ? "RETREAT (counts as a loss)" : "QUIT TO MENU", false, true, 22, new Color(0.6f, 0.2f, 0.2f)))
            { Time.timeScale = 1; pc.paused = false; battle.Finish(true); return; }
            if (helpOpen) Controls(new Rect(w / 2 - 420, 120, 840, 700));
        }

        public static void Controls(Rect r)
        {
            UI.Box(r, new Color(0.03f, 0.04f, 0.06f, 0.96f));
            string s =
                "<b>ON FOOT</b>\n" +
                "WASD move   Mouse look   Left click fire   Right click aim / zoom / block (blade heroes)\n" +
                "Space jump (hold in the air with a jump-pack)   Shift sprint   Ctrl or C crouch   R reload\n" +
                "Q / wheel switch weapon   G grenade / mine / detpack / supply crate (G again detonates)\n" +
                "F class special or hero ability   E enter vehicle   H become a hero when unlocked\n" +
                "Tab scoreboard   M map   Esc pause\n\n" +
                "<b>GROUND VEHICLES</b>\n" +
                "WASD drive   Mouse aim turret   Left click main gun   Shift boost   Q change seat   E get out\n\n" +
                "<b>GUNSHIPS AND STARFIGHTERS</b>\n" +
                "Mouse steer   W / S throttle   A / D roll   Shift boost   Left click guns   Right click missiles / torpedoes / repair\n" +
                "F or middle mouse: lock on - your craft follows the target; steer hard or press F again to break off\n" +
                "Gunships: Space / Ctrl climb and descend.  Starfighters: fly into a hangar, slow down, E to land and get out.\n\n" +
                "<b>GAMEPAD</b>\n" +
                "Left stick move / throttle + roll   Right stick look / steer   RT fire   LT aim / secondary\n" +
                "A jump   B crouch   X reload   Y enter / exit   LB equipment   RB switch weapon   L3 sprint   R3 ability\n" +
                "D-pad up become hero   Back scoreboard   Start pause";
            UI.Text(new Rect(r.x + 24, r.y + 20, r.width - 48, r.height - 40), s, 19, Color.white, TextAnchor.UpperLeft, true);
        }

        void Results()
        {
            var b = battle;
            var res = b.result;
            if (res == null) return;
            float w = UI.W;
            UI.Fill(new Rect(0, 0, w, UI.H), new Color(0, 0, 0, Mathf.Clamp01(b.overTime * 0.5f) * 0.65f));
            if (b.overTime < 1.5f) return;
            bool won = res.winner == b.player.team;
            Rect r = new Rect(w / 2 - 640, 110, 1280, 860);
            UI.Box(r);
            UI.Text(new Rect(r.x, r.y + 20, r.width, 60), won ? "VICTORY" : "DEFEAT", 56, won ? UI.Good : UI.Bad, TextAnchor.MiddleCenter);
            UI.Text(new Rect(r.x, r.y + 86, r.width, 34), res.reason, 24, Color.white, TextAnchor.MiddleCenter);
            UI.Text(new Rect(r.x + 60, r.y + 150, 560, 30), "YOUR BATTLE", 24, UI.Gold);
            UI.Text(new Rect(r.x + 60, r.y + 190, 560, 200),
                $"Points: {res.playerPoints}\nKills: {res.playerKills}\nDeaths: {res.playerDeaths}\nCommand posts captured: {res.playerCaptures}\n" +
                (b.player.everHero ? "Fought as a hero\n" : "") + $"Tickets left: {res.ticketsLeft[b.player.team]} vs {res.ticketsLeft[1 - b.player.team]}", 22, Color.white);
            UI.Text(new Rect(r.x + 60, r.y + 400, 560, 30), "AWARDS", 24, UI.Gold);
            UI.Text(new Rect(r.x + 60, r.y + 440, 560, 200), res.awards.Count > 0 ? string.Join("\n", res.awards) : "none this time", 21, res.awards.Count > 0 ? Color.white : UI.Dim);
            UI.Text(new Rect(r.x + 680, r.y + 150, 560, 30), "MOST VALUABLE", 24, UI.Gold);
            UI.Text(new Rect(r.x + 680, r.y + 190, 560, 30), res.mvp, 21, Color.white);
            float y = r.y + 240;
            for (int t = 0; t < 2; t++)
            {
                var list = b.people.FindAll(p => p.team == t);
                list.Sort((a, c) => c.points.CompareTo(a.points));
                UI.Text(new Rect(r.x + 680, y, 560, 28), b.Faction(t).name, 20, b.TeamColor(t)); y += 28;
                for (int i = 0; i < Mathf.Min(5, list.Count); i++) { UI.Text(new Rect(r.x + 700, y, 560, 24), $"{list[i].name}  -  {list[i].points} pts, {list[i].kills} kills", 18, list[i].isPlayer ? UI.Gold : UI.Dim); y += 24; }
                y += 12;
            }
            if (UI.Button(new Rect(r.center.x - 180, r.yMax - 100, 360, 66), "CONTINUE", false, true, 28, new Color(0.2f, 0.6f, 0.3f)) || (Ctl.Confirm && b.overTime > 3))
                b.Finish(false);
        }
    }
}
