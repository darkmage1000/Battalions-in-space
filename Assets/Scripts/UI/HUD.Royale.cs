using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    /// <summary>Battle royale screens: the drop map, players left and the zone timer, armour and supplies, loot prompts,
    /// the zone on the radar and the map, the elimination and results screens.</summary>
    public partial class HUD
    {
        static readonly Color ZoneCol = new Color(0.45f, 0.65f, 1f), NextCol = Color.white, ArmorCol = new Color(0.4f, 0.75f, 1f);

        void RoyaleGUI(PlayerControl pc)
        {
            var b = battle;
            switch (pc.mode)
            {
                case PlayerControl.Mode.Drop: DropScreen(pc); Feed(); break;
                case PlayerControl.Mode.Play: PlayHUD(pc); RoyaleOverlay(pc); break;
                case PlayerControl.Mode.Dead: RoyaleTop(); Feed(); RoyaleDead(pc); break;
                case PlayerControl.Mode.Over: RoyaleResults(pc); break;
                default: RoyaleTop(); Feed(); break;
            }
            if (pc.mode == PlayerControl.Mode.Play && !pc.paused) TouchInput.Draw();
            if (pc.showScore && pc.mode != PlayerControl.Mode.Over) RoyaleStandings();
            if (pc.showMap && pc.mode == PlayerControl.Mode.Play)
            {
                Rect r = new Rect(UI.W / 2 - 440, 110, 880, 880);
                UI.Box(r);
                DrawRoyaleMap(new Rect(r.x + 10, r.y + 10, r.width - 20, r.height - 20), false);
            }
            if (pc.paused) Pause(pc);
            if (pc.noticeT > 0) UI.Text(new Rect(0, 760, UI.W, 40), pc.notice, 26, UI.Bad, TextAnchor.MiddleCenter);
            if (Settings.ShowFps) UI.Text(new Rect(UI.W - 120, UI.H - 34, 110, 30), $"{fps:0} fps", 18, UI.Dim, TextAnchor.MiddleRight);
        }

        // ── top: players left, kills, the zone ───────────────────────────
        void RoyaleTop()
        {
            var b = battle; var r = b.royale;
            float w = UI.W;
            Rect bar = new Rect(w / 2 - 300, 10, 600, 64);
            UI.Box(bar);
            UI.Text(new Rect(bar.x + 16, bar.y + 4, 180, 24), "ALIVE", 16, UI.Dim);
            UI.Text(new Rect(bar.x + 16, bar.y + 22, 180, 40), $"{r.AlivePlayers}", 34, Color.white);
            UI.Text(new Rect(bar.x + 130, bar.y + 4, 120, 24), "KILLS", 16, UI.Dim);
            UI.Text(new Rect(bar.x + 130, bar.y + 22, 120, 40), $"{b.player.kills}", 34, UI.Gold);
            bool closing = r.shrinking;
            UI.Text(new Rect(bar.x + 240, bar.y + 8, 344, 26), r.ZoneStatus, 20, closing ? UI.Bad : ZoneCol, TextAnchor.UpperRight);
            if (r.phase == Royale.Phase.Play)
            {
                float frac = r.zonePhase >= Royale.Phases.Length ? 1 : closing ? 1 - r.zoneTimer / Royale.Phases[r.zonePhase].shrink : 1 - r.zoneTimer / Royale.Phases[r.zonePhase].wait;
                UI.Bar(new Rect(bar.x + 250, bar.y + 40, 334, 8), Mathf.Clamp01(frac), closing ? UI.Bad : ZoneCol);
            }
            var me = b.control.Me;
            if (me != null) UI.Text(new Rect(0, bar.yMax + 2, w, 24), b.land.RegionName(me.transform.position), 17, new Color(1, 1, 1, 0.7f), TextAnchor.MiddleCenter);
        }

        // ── extra in-play pieces ─────────────────────────────────────────
        void RoyaleOverlay(PlayerControl pc)
        {
            var b = battle; var r = b.royale;
            var me = pc.Me;
            if (me == null) return;
            float w = UI.W, h = UI.H;
            Vector3 p = me.InVehicle ? me.vehicle.transform.position : me.transform.position;
            // outside the zone: red edge, a warning and the way back in
            if (r.OutsideZone(p))
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6);
                UI.Fill(new Rect(0, 0, w, h), new Color(0.25f, 0.35f, 1f, 0.1f + pulse * 0.06f));
                Vector2 to = r.zoneC - new Vector2(p.x, p.z);
                float dist = to.magnitude - r.zoneR;
                UI.Text(new Rect(0, 150, w, 36), $"OUTSIDE THE ZONE - {Mathf.CeilToInt(dist)} m to safety", 28, UI.Bad, TextAnchor.MiddleCenter);
                DrawArrowTo(new Vector3(r.zoneC.x, p.y, r.zoneC.y), UI.Bad);
            }
            else if (r.phase == Royale.Phase.Play && r.OutsideNext(p) && r.zonePhase < Royale.Phases.Length)
            {
                UI.Text(new Rect(0, 150, w, 30), "You are outside the next zone", 20, new Color(1f, 0.85f, 0.6f), TextAnchor.MiddleCenter);
                DrawArrowTo(new Vector3(r.nextC.x, p.y, r.nextC.y), new Color(1f, 0.85f, 0.6f));
            }
            // using a medkit / shield cell
            if (me.Using)
            {
                Rect ub = new Rect(w / 2 - 160, h / 2 + 110, 320, 46);
                UI.Box(ub);
                UI.Text(new Rect(ub.x, ub.y + 2, ub.width, 24), me.useKind == LootKind.Medkit ? "USING MEDKIT" : "CHARGING SHIELD", 18, me.useKind == LootKind.Medkit ? UI.Good : ArmorCol, TextAnchor.MiddleCenter);
                UI.Bar(new Rect(ub.x + 12, ub.y + 30, ub.width - 24, 8), 1 - me.useT / me.useDur, me.useKind == LootKind.Medkit ? UI.Good : ArmorCol);
            }
            // a marker over the boss when it is close
            if (r.boss != null && r.boss.Alive)
            {
                var bu = r.boss.unit;
                Vector3 sp = Game.Cam.WorldToScreenPoint(bu.AimPoint + Vector3.up * 3);
                float d = (bu.transform.position - p).magnitude;
                if (sp.z > 0 && d < 220)
                {
                    Vector2 g = new Vector2(sp.x / UI.S, (Screen.height - sp.y) / UI.S);
                    UI.Text(new Rect(g.x - 160, g.y - 40, 320, 24), $"DREADNOUGHT WARLORD  {Mathf.RoundToInt(d)} m", 17, new Color(1f, 0.55f, 0.25f), TextAnchor.MiddleCenter);
                    UI.Bar(new Rect(g.x - 90, g.y - 14, 180, 8), bu.hp / bu.maxHp, new Color(1f, 0.45f, 0.2f));
                }
            }
        }

        void DrawArrowTo(Vector3 target, Color col)
        {
            var cam = Game.Cam;
            Vector3 rel = cam.transform.InverseTransformPoint(target);
            Vector2 dir = new Vector2(rel.x, rel.z); if (dir.sqrMagnitude < 0.0001f) dir = Vector2.up; dir.Normalize();
            Vector2 c = new Vector2(UI.W / 2, 220);
            Vector2 tip = c + new Vector2(dir.x, -dir.y) * 34, side = new Vector2(dir.y, dir.x) * 14;
            Vector2 back = c - new Vector2(dir.x, -dir.y) * 6;
            UI.Line(tip, back + new Vector2(side.x, side.y), col, 5);
            UI.Line(tip, back - new Vector2(side.x, side.y), col, 5);
        }

        /// <summary>Bottom middle: what is in your pockets.</summary>
        void RoyaleInventory(Unit me, float w, float h)
        {
            Rect r = new Rect(w / 2 - 250, h - 70, 500, 56);
            UI.Box(r);
            string heal = TouchInput.Active ? "HEAL" : Ctl.PadActive ? "D-up" : "H";
            UI.Text(new Rect(r.x + 14, r.y + 6, 160, 26), $"Medkits  {me.medkits}", 19, me.medkits > 0 ? UI.Good : UI.Dim);
            UI.Text(new Rect(r.x + 170, r.y + 6, 180, 26), $"Shield cells  {me.shields}", 19, me.shields > 0 ? ArmorCol : UI.Dim);
            UI.Text(new Rect(r.x + 360, r.y + 6, 130, 26), me.keys > 0 ? $"VAULT KEY x{me.keys}" : "no key", 19, me.keys > 0 ? UI.Gold : UI.Dim, TextAnchor.UpperRight);
            UI.Text(new Rect(r.x + 14, r.y + 30, 470, 24), $"[{heal}] use a medkit when hurt, else a shield cell", 15, UI.Dim);
        }

        // ── the map ──────────────────────────────────────────────────────
        public void DrawRoyaleMap(Rect r, bool pick)
        {
            var b = battle; var land = b.land; var ry = b.royale;
            GUI.DrawTexture(r, land.minimap);
            Vector2 M(Vector3 p) => new Vector2(r.x + (p.x + land.half) / land.size * r.width, r.y + (1 - (p.z + land.half) / land.size) * r.height);
            float px = r.width / land.size;
            // region names
            for (int i = 0; i < land.def.regions.Length; i++)
            {
                var q = M(land.RegionCentre(i));
                UI.Text(new Rect(q.x - 110, q.y - 12, 220, 24), land.def.regions[i].name, 15, new Color(1, 1, 1, 0.85f), TextAnchor.MiddleCenter);
            }
            // vaults and the boss
            foreach (var v in land.vaults)
            {
                var q = M(v.lockPoint);
                UI.Fill(new Rect(q.x - 7, q.y - 7, 14, 14), Color.black);
                UI.Fill(new Rect(q.x - 5, q.y - 5, 10, 10), v.open ? UI.Dim : UI.Gold);
                UI.Text(new Rect(q.x - 60, q.y + 6, 120, 18), v.open ? "vault (open)" : "VAULT", 12, UI.Gold, TextAnchor.UpperCenter);
            }
            if (ry.boss != null && ry.boss.Alive)
            {
                var q = M(ry.boss.unit.transform.position);
                UI.Dot(q, 9, Color.black); UI.Dot(q, 7, new Color(1f, 0.4f, 0.15f));
                UI.Text(new Rect(q.x - 80, q.y + 9, 160, 18), "WARLORD", 13, new Color(1f, 0.55f, 0.25f), TextAnchor.UpperCenter);
            }
            // the zone now and next
            if (ry.phase == Royale.Phase.Play)
            {
                Circle(M(new Vector3(ry.zoneC.x, 0, ry.zoneC.y)), ry.zoneR * px, ZoneCol, 3);
                if (ry.zonePhase < Royale.Phases.Length) Circle(M(new Vector3(ry.nextC.x, 0, ry.nextC.y)), ry.nextR * px, NextCol, 2);
            }
            // me and my team
            foreach (var u in b.units)
                if (u && u.Alive && u.team == b.player.team)
                {
                    var q = M(u.InVehicle ? u.vehicle.transform.position : u.transform.position);
                    UI.Dot(q, u.part.isPlayer ? 7 : 6, Color.black);
                    UI.Dot(q, u.part.isPlayer ? 5 : 4, u.part.isPlayer ? Color.white : SquadColor);
                }
            if (pick)
            {
                var dp = ry.dropPickPos;
                var q = M(dp);
                if (ry.playerPicked) { UI.Frame(new Rect(q.x - 14, q.y - 14, 28, 28), UI.Gold, 3); UI.Text(new Rect(q.x - 100, q.y + 16, 200, 22), "LANDING HERE", 16, UI.Gold, TextAnchor.UpperCenter); }
                var e = Event.current;
                if (e.type == EventType.MouseDown && r.Contains(e.mousePosition))
                {
                    float wx = (e.mousePosition.x - r.x) / r.width * land.size - land.half;
                    float wz = (1 - (e.mousePosition.y - r.y) / r.height) * land.size - land.half;
                    float lim = land.PlayHalf - 25;
                    wx = Mathf.Clamp(wx, -lim, lim); wz = Mathf.Clamp(wz, -lim, lim);
                    ry.dropPickPos = new Vector3(wx, land.Top(wx, wz), wz);
                    ry.playerPicked = true;
                    Sfx.Play2D("ui");
                    e.Use();
                }
            }
            else
            {
                var me = b.control.Me;
                if (me != null) { var q = M(me.transform.position); Vector3 f = Game.Cam.transform.forward; UI.Line(q, q + new Vector2(f.x, -f.z).normalized * 22, Color.white, 3); }
            }
        }

        static void Circle(Vector2 c, float rad, Color col, float wd)
        {
            const int n = 72;
            Vector2 prev = c + new Vector2(rad, 0);
            for (int i = 1; i <= n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2;
                Vector2 q = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad;
                UI.Line(prev, q, col, wd);
                prev = q;
            }
        }

        // ── before the jump ──────────────────────────────────────────────
        void DropScreen(PlayerControl pc)
        {
            var b = battle; var ry = b.royale;
            float w = UI.W, h = UI.H;
            float side = Mathf.Min(h - 150, 900);
            Rect mr = new Rect(w / 2 - side / 2 - 220, 70, side, side);
            UI.Box(new Rect(mr.x - 8, mr.y - 8, mr.width + 16, mr.height + 16));
            DrawRoyaleMap(mr, true);
            Rect info = new Rect(mr.xMax + 30, 70, 420, side);
            UI.Box(info);
            float y = info.y + 18, x = info.x + 22;
            UI.Text(new Rect(x, y, 380, 40), "BATTLE ROYALE", 32, UI.Gold); y += 46;
            UI.Text(new Rect(x, y, 380, 60), $"JUMP IN {Mathf.CeilToInt(Mathf.Max(0, ry.dropT))}", 44, Color.white); y += 64;
            UI.Text(new Rect(x, y, 380, 30), $"{ry.TotalPlayers} players - last one standing wins", 19, UI.Dim); y += 40;
            UI.Text(new Rect(x, y, 380, 150),
                "Tap the map to pick where you land, then JUMP (or wait for the countdown).\n\nYou glide down under a grav-chute: steer with the move stick.",
                18, Color.white, TextAnchor.UpperLeft, true); y += 160;
            var f = Factions.Get(b.player.faction >= 0 ? b.player.faction : 0);
            string cls = b.cfg.royaleClasses ? f.classes[Mathf.Clamp(b.player.royaleClass, 0, 4)].name : "Survivor (classes off)";
            UI.Text(new Rect(x, y, 380, 26), $"{f.name}", 20, f.bolt); y += 28;
            UI.Text(new Rect(x, y, 380, 26), cls, 18, Color.white); y += 40;
            UI.Text(new Rect(x, y, 380, 150),
                "Loot: weapons (grey, green, blue, purple, gold - better the rarer), medkits, shield cells, ammo.\nBlue chests hold better loot. The zone shrinks: stay inside. A boss lands later. Vaults need a key.",
                16, UI.Dim, TextAnchor.UpperLeft, true);
            if (UI.Button(new Rect(info.x + 22, info.yMax - 90, info.width - 44, 70), ry.playerWantsJump ? "JUMPING..." : "JUMP NOW", false, !ry.playerWantsJump, 30, new Color(0.2f, 0.6f, 0.3f)))
                ry.playerWantsJump = true;
        }

        // ── eliminated ───────────────────────────────────────────────────
        void RoyaleDead(PlayerControl pc)
        {
            var b = battle;
            float w = UI.W;
            int place = b.player.placement > 0 ? b.player.placement : b.royale.AliveTeams + 1;
            Rect r = new Rect(w / 2 - 360, UI.H - 300, 720, 230);
            UI.Box(r);
            UI.Text(new Rect(r.x, r.y + 12, r.width, 50), $"#{place}  ELIMINATED", 40, UI.Bad, TextAnchor.MiddleCenter);
            var k = pc.killer;
            string by = k ? $"by {(k.part.boss ? "the Dreadnought Warlord" : k.displayName)}" : "by the zone";
            UI.Text(new Rect(r.x, r.y + 64, r.width, 30), $"{by}   -   {b.player.kills} kills", 22, Color.white, TextAnchor.MiddleCenter);
            if (pc.spectate && pc.spectate.Alive) UI.Text(new Rect(r.x, r.y + 98, r.width, 26), $"Watching {pc.spectate.displayName}   ({b.royale.AlivePlayers} left)", 18, UI.Dim, TextAnchor.MiddleCenter);
            if (UI.Button(new Rect(r.center.x - 170, r.yMax - 84, 340, 64), "LEAVE THE MATCH", false, true, 24, new Color(0.6f, 0.25f, 0.2f)))
            {
                if (b.result == null) b.result = new BattleResult { winner = -1, reason = $"Finished #{place}" };
                b.Finish(false);
            }
        }

        // ── the end ──────────────────────────────────────────────────────
        void RoyaleResults(PlayerControl pc)
        {
            var b = battle; var res = b.result;
            if (res == null) return;
            float w = UI.W;
            UI.Fill(new Rect(0, 0, w, UI.H), new Color(0, 0, 0, Mathf.Clamp01(b.overTime * 0.5f) * 0.65f));
            if (b.overTime < 1.5f) return;
            int place = b.player.placement > 0 ? b.player.placement : 1;
            bool won = res.winner == b.player.team;
            Rect r = new Rect(w / 2 - 560, 140, 1120, 760);
            UI.Box(r);
            UI.Text(new Rect(r.x, r.y + 24, r.width, 80), won ? "#1  LAST ONE STANDING" : $"#{place}", 64, won ? UI.Gold : Color.white, TextAnchor.MiddleCenter);
            UI.Text(new Rect(r.x, r.y + 110, r.width, 34), res.reason, 24, Color.white, TextAnchor.MiddleCenter);
            UI.Text(new Rect(r.x + 80, r.y + 180, 460, 300),
                $"Placed: #{place} of {b.royale.TotalPlayers}\nKills: {b.player.kills}\nSurvived: {Mathf.FloorToInt(b.royale.playTime / 60)}:{Mathf.FloorToInt(b.royale.playTime % 60):00}\nBoss: {(b.royale.bossDead ? "brought down" : "still standing")}",
                26, Color.white);
            UI.Text(new Rect(r.x + 600, r.y + 180, 460, 30), "MOST KILLS", 24, UI.Gold);
            var list = b.people.FindAll(p => !p.boss);
            list.Sort((a, c) => c.kills.CompareTo(a.kills));
            float y = r.y + 220;
            for (int i = 0; i < Mathf.Min(8, list.Count); i++) { UI.Text(new Rect(r.x + 600, y, 460, 28), $"{list[i].name}  -  {list[i].kills}", 21, list[i].isPlayer ? UI.Gold : list[i].team == b.player.team ? SquadColor : UI.Dim); y += 30; }
            if (UI.Button(new Rect(r.center.x - 180, r.yMax - 100, 360, 66), "CONTINUE", false, true, 28, new Color(0.2f, 0.6f, 0.3f)) || (Ctl.Confirm && b.overTime > 3))
                b.Finish(false);
        }

        void RoyaleStandings()
        {
            var b = battle;
            Rect r = new Rect(UI.W / 2 - 360, 140, 720, 640);
            UI.Box(r, new Color(0.03f, 0.04f, 0.06f, 0.92f));
            UI.Text(new Rect(r.x, r.y + 14, r.width, 34), $"{b.royale.AlivePlayers} of {b.royale.TotalPlayers} still standing", 26, UI.Gold, TextAnchor.MiddleCenter);
            var list = b.people.FindAll(p => !p.boss);
            list.Sort((a, c) => (c.Alive ? 1 : 0) != (a.Alive ? 1 : 0) ? (c.Alive ? 1 : 0).CompareTo(a.Alive ? 1 : 0) : c.kills.CompareTo(a.kills));
            float y = r.y + 64;
            for (int i = 0; i < Mathf.Min(18, list.Count); i++)
            {
                var p = list[i];
                Color c = p.isPlayer ? UI.Gold : p.team == b.player.team ? SquadColor : p.Alive ? Color.white : UI.Dim;
                UI.Text(new Rect(r.x + 40, y, 420, 28), (p.Alive || !p.dropped ? "" : $"#{p.placement}  ") + p.name, 20, c);
                UI.Text(new Rect(r.x + 460, y, 220, 28), $"{p.kills} kills", 20, c, TextAnchor.UpperRight);
                y += 30;
            }
        }
    }
}
