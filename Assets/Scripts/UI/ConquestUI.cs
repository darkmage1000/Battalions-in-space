using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    /// <summary>Galactic Conquest screens: setup, the galaxy map with fleets / shop / log, battle prompts and the ending.</summary>
    public partial class MenuUI
    {
        // setup
        int gcFaction = 0;
        readonly bool[] gcOpp = { false, true, false, false };
        int gcTickets = 150, gcBots = 16, gcDiff = 1;
        // running
        public Campaign camp;
        GalaxyView view;
        int selPlanet = -1;
        int movingFleet = -1;
        readonly List<int> moveTargets = new List<int>();
        readonly Queue<int> aiQueue = new Queue<int>();
        bool shopOpen;
        int chosenBonus = -1;     // index into camp.bonuses for the pending battle
        string banner; float bannerT;
        float aiDelay;

        // ── setup ────────────────────────────────────────────────────────
        void ConquestSetup()
        {
            Title("Galactic Conquest");
            float w = UI.W;
            Rect r = new Rect(w / 2 - 760, 190, 1520, 760);
            UI.Box(r);
            float x = r.x + 30, y = r.y + 24;
            UI.Text(new Rect(x, y, 500, 30), "YOUR FACTION", 22, UI.Gold); y += 36;
            for (int i = 0; i < 4; i++)
            {
                var f = Factions.All[i];
                if (UI.Button(new Rect(x, y, 470, 54), f.name, gcFaction == i, true, 24, f.second)) { gcFaction = i; gcOpp[i] = false; if (!AnyOpp()) gcOpp[(i + 1) % 4] = true; }
                y += 60;
            }
            y += 10;
            var mf = Factions.All[gcFaction];
            UI.Text(new Rect(x, y, 470, 120), mf.blurb + "\n\"" + mf.motto + "\"", 19, UI.Dim, TextAnchor.UpperLeft, true);

            float ox = r.x + 540; y = r.y + 24;
            UI.Text(new Rect(ox, y, 500, 30), "OPPONENTS (one or more)", 22, UI.Gold); y += 36;
            for (int i = 0; i < 4; i++)
            {
                var f = Factions.All[i];
                if (UI.Button(new Rect(ox, y, 470, 54), f.name, gcOpp[i] && i != gcFaction, i != gcFaction, 24, f.second)) { gcOpp[i] = !gcOpp[i]; if (!AnyOpp()) gcOpp[i] = true; }
                y += 60;
            }
            y += 16;
            UI.Text(new Rect(ox, y, 470, 160), "Each faction starts with its home world, the worlds nearest to it and two fleets. Move one fleet a turn. Take every enemy world to win.", 19, UI.Dim, TextAnchor.UpperLeft, true);

            float sx = r.x + 1050; y = r.y + 24;
            UI.Text(new Rect(sx, y, 440, 30), "RULES", 22, UI.Gold); y += 40;
            gcTickets = Mathf.RoundToInt(UI.Slider(new Rect(sx, y, 440, 40), "Tickets", gcTickets, 50, 400, gcTickets.ToString()) / 10f) * 10; y += 50;
            gcBots = Mathf.RoundToInt(UI.Slider(new Rect(sx, y, 440, 40), "Per side", gcBots, 4, 32, gcBots.ToString())); y += 50;
            gcDiff = Mathf.RoundToInt(UI.Slider(new Rect(sx, y, 440, 40), "Difficulty", gcDiff, 0, 2, Settings.DifficultyNames[gcDiff])); y += 70;
            if (Campaign.HasSave)
            {
                if (savedPreview == null) savedPreview = Campaign.Load();
                var c = savedPreview;
                if (c != null && c.winner == -1)
                {
                    UI.Text(new Rect(sx, y, 440, 60), $"Saved campaign: {Factions.Get(c.player).name}, turn {c.turn}, {c.Planets(c.player)} worlds", 18, Color.white, TextAnchor.UpperLeft, true); y += 54;
                    if (UI.Button(new Rect(sx, y, 440, 60), "CONTINUE CAMPAIGN", false, true, 26, new Color(0.2f, 0.5f, 0.7f))) { OpenCampaign(Campaign.Load()); return; }
                    y += 70;
                }
            }
            if (UI.Button(new Rect(r.x + 30, r.yMax + 20, 260, 64), "BACK", false, true, 26)) screen = Screen.Main;
            if (UI.Button(new Rect(r.xMax - 380, r.yMax + 20, 380, 64), "NEW CAMPAIGN", false, true, 30, new Color(0.2f, 0.6f, 0.3f)))
            {
                var opp = new List<int>();
                for (int i = 0; i < 4; i++) if (gcOpp[i] && i != gcFaction) opp.Add(i);
                var c = Campaign.New(gcFaction, opp.ToArray(), gcDiff, gcTickets, gcBots);
                c.Save();
                OpenCampaign(c);
            }
        }
        Campaign savedPreview;

        bool AnyOpp() { for (int i = 0; i < 4; i++) if (gcOpp[i] && i != gcFaction) return true; return false; }

        public void OpenCampaign(Campaign c)
        {
            if (c == null) return;
            camp = c;
            savedPreview = null;
            Game.I.ClearBackdrop();
            if (view) Destroy(view.gameObject);
            view = GalaxyView.Create();
            selPlanet = Galaxy.Home[c.player]; movingFleet = -1; moveTargets.Clear(); aiQueue.Clear();
            view.Focus(selPlanet);
            screen = Screen.Conquest;
            Banner($"{Factions.Get(c.player).name} - turn {c.turn}");
        }

        void CloseCampaign()
        {
            if (view) Destroy(view.gameObject);
            view = null; camp = null; savedPreview = null;
            Game.I.ShowMenu();
            screen = Screen.ConquestSetup;
        }

        void Banner(string s) { banner = s; bannerT = 3; }

        // ── galaxy map ───────────────────────────────────────────────────
        void ConquestMapScreen()
        {
            if (camp == null || !view) { screen = Screen.ConquestSetup; return; }
            var c = camp;
            float w = UI.W;
            if (Event.current.type == EventType.Repaint) { bannerT -= Time.unscaledDeltaTime; RunAI(); }
            if (camp == null || !view) return;
            view.Refresh(c, selPlanet, movingFleet >= 0 ? moveTargets : null);

            // planet labels
            for (int i = 0; i < Galaxy.Planets.Length; i++)
            {
                var sp = view.ScreenOf(i, -2.6f);
                int o = c.owner[i];
                Color col = o >= 0 ? Factions.Get(o).bolt : Color.gray;
                UI.Text(new Rect(sp.x - 110, sp.y, 220, 24), Galaxy.Planets[i].name, 19, col, TextAnchor.UpperCenter);
                var fl = c.FleetAt(i);
                if (fl != null) UI.Text(new Rect(sp.x - 110, sp.y + 22, 220, 20), Factions.Get(fl.faction).shortName + " fleet", 14, Factions.Get(fl.faction).bolt, TextAnchor.UpperCenter);
            }

            // top bar
            var pf = Factions.Get(c.player);
            Rect top = new Rect(w / 2 - 520, 10, 1040, 70);
            UI.Box(top);
            UI.Text(new Rect(top.x + 20, top.y + 8, 500, 30), pf.name.ToUpper(), 24, pf.bolt);
            UI.Text(new Rect(top.x + 20, top.y + 38, 640, 26), $"Turn {c.turn}   Credits {c.credits[c.player]}   (+{c.Income(c.player)} a turn)   Worlds {c.Planets(c.player)}   Fleets {c.FleetCount(c.player)}", 19);
            UI.Text(new Rect(top.xMax - 420, top.y + 8, 400, 30), c.moved ? "Fleet moved - end your turn" : "Select a fleet and move it, or end your turn", 18, c.moved ? UI.Gold : UI.Dim, TextAnchor.UpperRight);
            UI.Text(new Rect(top.xMax - 420, top.y + 38, 400, 26), $"Battles won {c.battlesWon} / lost {c.battlesLost}", 17, UI.Dim, TextAnchor.UpperRight);

            // standings
            Rect st = new Rect(20, 100, 360, 60 + c.factions.Length * 56);
            UI.Box(st);
            UI.Text(new Rect(st.x + 16, st.y + 10, 330, 28), "FACTIONS", 20, UI.Gold);
            float y = st.y + 44;
            foreach (var f in c.factions)
            {
                var fd = Factions.Get(f);
                UI.Text(new Rect(st.x + 16, y, 330, 24), fd.name + (f == c.player ? " (you)" : "") + (c.Alive(f) ? "" : " - defeated"), 18, c.Alive(f) ? fd.bolt : UI.Dim);
                UI.Text(new Rect(st.x + 16, y + 22, 330, 24), $"{c.Planets(f)} worlds, {c.FleetCount(f)} fleets" + (f != c.player ? $", {c.credits[f]} cr" : ""), 15, UI.Dim);
                y += 56;
            }

            // log
            Rect lg = new Rect(20, st.yMax + 12, 360, 300);
            UI.Box(lg);
            UI.Text(new Rect(lg.x + 16, lg.y + 8, 330, 26), "REPORTS", 18, UI.Gold);
            y = lg.y + 36;
            for (int i = Mathf.Max(0, c.log.Count - 10); i < c.log.Count; i++) { UI.Text(new Rect(lg.x + 16, y, 335, 26), c.log[i], 15, Color.white, TextAnchor.UpperLeft, true); y += 25; }

            Rect pp = new Rect(w - 480, 100, 460, 600);
            if (selPlanet >= 0) PlanetPanel(c, pp);

            // buttons
            if (UI.Button(new Rect(20, UI.H - 90, 220, 64), "MENU", false, true, 24)) { c.Save(); CloseCampaign(); return; }
            if (UI.Button(new Rect(260, UI.H - 90, 280, 64), "BUY / UNLOCK", shopOpen, true, 24)) shopOpen = !shopOpen;
            bool busy = aiQueue.Count > 0 || c.pending.active;
            if (UI.Button(new Rect(w - 360, UI.H - 90, 340, 64), busy ? "ENEMY TURN..." : "END TURN", false, !busy && c.winner == -1, 28, new Color(0.2f, 0.6f, 0.3f))) EndTurn();
            UI.Text(new Rect(560, UI.H - 70, 700, 30), "Click a world.  Wheel zoom, WASD / right-drag pan.", 17, UI.Dim);

            if (shopOpen) Shop(c, new Rect(w / 2 - 430, 150, 860, 760));
            if (bannerT > 0) UI.Text(new Rect(0, 100, w, 50), banner, 36, UI.Gold, TextAnchor.MiddleCenter);
            if (c.pending.active) BattlePrompt(c);
            if (camp == null) return;
            if (c.winner != -1) Ending(c);

            // clicks on planets
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && !shopOpen && !c.pending.active && c.winner == -1)
            {
                Vector2 m = e.mousePosition;
                bool overUI = st.Contains(m) || lg.Contains(m) || top.Contains(m) || (selPlanet >= 0 && pp.Contains(m)) || m.y > UI.H - 100;
                if (!overUI)
                {
                    int p = view.Pick(Input.mousePosition);
                    if (p >= 0)
                    {
                        if (movingFleet >= 0 && moveTargets.Contains(p)) DoMove(p);
                        else { selPlanet = p; movingFleet = -1; moveTargets.Clear(); Sfx.Play2D("ui"); }
                        e.Use();
                    }
                }
            }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { if (movingFleet >= 0) { movingFleet = -1; moveTargets.Clear(); } else if (shopOpen) shopOpen = false; }
        }

        void PlanetPanel(Campaign c, Rect r)
        {
            var pd = Galaxy.Planets[selPlanet];
            UI.Box(r);
            int o = c.owner[selPlanet];
            float y = r.y + 14;
            UI.Text(new Rect(r.x + 20, y, 420, 36), pd.name, 30, o >= 0 ? Factions.Get(o).bolt : Color.white); y += 40;
            UI.Text(new Rect(r.x + 20, y, 420, 26), o >= 0 ? "Held by the " + Factions.Get(o).name : "Unclaimed", 18, UI.Dim); y += 30;
            UI.Text(new Rect(r.x + 20, y, 420, 50), pd.blurb, 17, Color.white, TextAnchor.UpperLeft, true); y += 48;
            UI.Text(new Rect(r.x + 20, y, 420, 24), $"Battlefield: {Maps.Land[pd.land].theme} (like {Maps.Land[pd.land].name})", 16, UI.Dim); y += 24;
            UI.Text(new Rect(r.x + 20, y, 420, 24), $"Orbit: {Maps.Space[pd.space].name}", 16, UI.Dim); y += 24;
            UI.Text(new Rect(r.x + 20, y, 420, 24), $"Income: {pd.income} credits a turn", 16, UI.Dim); y += 24;
            UI.Text(new Rect(r.x + 20, y, 420, 46), $"Bonus: {Bonuses.Names[(int)pd.bonus]} - {Bonuses.Info[(int)pd.bonus]}", 16, UI.Gold, TextAnchor.UpperLeft, true); y += 48;
            var fl = c.FleetAt(selPlanet);
            UI.Text(new Rect(r.x + 20, y, 420, 26), fl != null ? $"Fleet in orbit: {Factions.Get(fl.faction).name}" : "No fleet in orbit", 18, fl != null ? Factions.Get(fl.faction).bolt : UI.Dim); y += 36;
            bool myTurn = aiQueue.Count == 0 && !c.pending.active && c.winner == -1;
            if (fl != null && fl.faction == c.player)
            {
                if (UI.Button(new Rect(r.x + 20, y, 420, 56), movingFleet == fl.id ? "PICK A GREEN WORLD (Esc cancels)" : c.moved ? "ALREADY MOVED THIS TURN" : "MOVE THIS FLEET", movingFleet == fl.id, myTurn && !c.moved, 20))
                {
                    movingFleet = fl.id; moveTargets.Clear();
                    foreach (var n in Galaxy.Neighbours(selPlanet)) { var o2 = c.FleetAt(n); if (o2 == null || o2.faction != c.player) moveTargets.Add(n); }
                }
                y += 64;
                foreach (var n in Galaxy.Neighbours(selPlanet))
                {
                    var o2 = c.FleetAt(n);
                    string what = o2 != null && o2.faction != c.player ? "space battle" : c.owner[n] != c.player ? "invasion" : "move";
                    if (o2 != null && o2.faction == c.player) what = "blocked by your fleet";
                    UI.Text(new Rect(r.x + 30, y, 410, 22), $"→ {Galaxy.Planets[n].name}: {what}", 15, what.StartsWith("blocked") ? UI.Dim : what == "move" ? Color.white : UI.Bad); y += 22;
                    if (movingFleet == fl.id && moveTargets.Contains(n) && UI.Button(new Rect(r.xMax - 120, y - 22, 100, 22), "GO", false, true, 14)) { DoMove(n); return; }
                }
            }
            else if (o == c.player && fl == null)
            {
                bool can = c.credits[c.player] >= Campaign.FleetCost && c.FleetCount(c.player) < Campaign.MaxFleets && myTurn;
                if (UI.Button(new Rect(r.x + 20, y, 420, 56), $"BUILD FLEET HERE ({Campaign.FleetCost} cr)", false, can, 20))
                {
                    c.credits[c.player] -= Campaign.FleetCost; c.AddFleet(c.player, selPlanet);
                    c.Log($"You built a fleet at {pd.name}."); c.Save();
                }
                y += 64;
                if (c.FleetCount(c.player) >= Campaign.MaxFleets) UI.Text(new Rect(r.x + 20, y, 420, 24), $"Fleet limit ({Campaign.MaxFleets}) reached", 16, UI.Dim);
            }
        }

        public void DoMove(int to)
        {
            var c = camp;
            var f = c.Fleet(movingFleet);
            movingFleet = -1; moveTargets.Clear();
            if (f == null) return;
            var pb = c.Move(f, to);
            c.moved = true;
            selPlanet = to;
            view.Focus(to);
            if (pb != null) { c.pending = pb; chosenBonus = -1; }
            c.Save();
            Sfx.Play2D("ui2");
        }

        public void EndTurn()
        {
            var c = camp;
            movingFleet = -1; moveTargets.Clear();
            foreach (var f in c.factions) if (f != c.player && c.Alive(f)) aiQueue.Enqueue(f);
            aiDelay = 0.4f;
            Banner("Enemy turn");
            if (aiQueue.Count == 0) NewTurn();
        }

        void RunAI()
        {
            var c = camp;
            if (c.pending.active || c.winner != -1) return;
            if (aiQueue.Count == 0) return;
            aiDelay -= Time.unscaledDeltaTime;
            if (aiDelay > 0) return;
            aiDelay = 0.6f;
            int f = aiQueue.Dequeue();
            var pb = c.AITurn(f);
            if (pb != null) { c.pending = pb; chosenBonus = -1; Banner($"{Factions.Get(f).name} attacks {Galaxy.Planets[pb.planet].name}!"); selPlanet = pb.planet; view.Focus(pb.planet); }
            if (aiQueue.Count == 0 && !c.pending.active) NewTurn();
            c.Save();
        }

        /// <summary>Dev / test: run every queued AI move immediately.</summary>
        public void FlushAI() { while (aiQueue.Count > 0 && !camp.pending.active && camp.winner == -1) { aiDelay = 0; RunAI(); } }
        public bool AIBusy => aiQueue.Count > 0;

        void NewTurn()
        {
            var c = camp;
            if (c.winner != -1) return;
            c.turn++;
            c.moved = false;
            int inc = c.Income(c.player);
            c.credits[c.player] += inc;
            c.Log($"Turn {c.turn}: +{inc} credits.");
            Banner($"Turn {c.turn}");
            c.Save();
        }

        // ── shop ─────────────────────────────────────────────────────────
        void Shop(Campaign c, Rect r)
        {
            UI.Box(r, new Color(0.04f, 0.05f, 0.08f, 0.96f));
            UI.Text(new Rect(r.x + 24, r.y + 14, 600, 34), $"QUARTERMASTER  -  {c.credits[c.player]} credits", 26, UI.Gold);
            float y = r.y + 64;
            var f = Factions.Get(c.player);
            UI.Text(new Rect(r.x + 24, y, 800, 26), "CLASSES (unlocked classes can be used in every battle)", 19, Color.white); y += 32;
            for (int i = 0; i < 5; i++)
            {
                bool have = c.unlocked[i];
                UI.Text(new Rect(r.x + 24, y + 10, 400, 30), f.classes[i].name, 20, have ? UI.Good : Color.white);
                if (!have && UI.Button(new Rect(r.x + 440, y + 4, 380, 44), $"UNLOCK ({Campaign.ClassCost[i]} cr)", false, c.credits[c.player] >= Campaign.ClassCost[i], 19))
                { c.credits[c.player] -= Campaign.ClassCost[i]; c.unlocked[i] = true; c.Log($"Unlocked the {f.classes[i].name}."); c.Save(); }
                if (have) UI.Text(new Rect(r.x + 440, y + 10, 380, 30), "unlocked", 18, UI.Dim);
                y += 50;
            }
            y += 12;
            UI.Text(new Rect(r.x + 24, y, 800, 26), $"BATTLE BONUSES (one used per battle, carry up to {Campaign.MaxBonuses}) - from worlds you hold", 19, Color.white); y += 32;
            foreach (var bo in c.AvailableBonuses(c.player))
            {
                if (bo == Bonus.None) continue;
                int cost = Bonuses.Cost[(int)bo];
                UI.Text(new Rect(r.x + 24, y + 2, 400, 26), Bonuses.Names[(int)bo], 19, UI.Gold);
                UI.Text(new Rect(r.x + 24, y + 26, 410, 22), Bonuses.Info[(int)bo], 14, UI.Dim);
                if (UI.Button(new Rect(r.x + 440, y + 4, 380, 42), $"BUY ({cost} cr)", false, c.credits[c.player] >= cost && c.bonuses.Count < Campaign.MaxBonuses, 18))
                { c.credits[c.player] -= cost; c.bonuses.Add((int)bo); c.Save(); }
                y += 52;
            }
            y += 6;
            string inv = c.bonuses.Count == 0 ? "none" : string.Join(", ", c.bonuses.ConvertAll(b => Bonuses.Names[b]));
            UI.Text(new Rect(r.x + 24, y, 800, 26), "Carrying: " + inv, 18, Color.white);
            if (UI.Button(new Rect(r.center.x - 120, r.yMax - 70, 240, 54), "CLOSE", false, true, 22)) shopOpen = false;
        }

        // ── battles ──────────────────────────────────────────────────────
        void BattlePrompt(Campaign c)
        {
            var pb = c.pending;
            float w = UI.W;
            UI.Fill(new Rect(0, 0, w, UI.H), new Color(0, 0, 0, 0.5f));
            Rect r = new Rect(w / 2 - 520, 200, 1040, 640);
            UI.Box(r, new Color(0.04f, 0.05f, 0.08f, 0.97f));
            var pd = Galaxy.Planets[pb.planet];
            bool attacking = pb.attacker == c.player;
            int enemy = attacking ? pb.defender : pb.attacker;
            string title = pb.space ? $"BATTLE OVER {pd.name.ToUpper()}" : attacking ? $"INVASION OF {pd.name.ToUpper()}" : $"DEFENCE OF {pd.name.ToUpper()}";
            UI.Text(new Rect(r.x, r.y + 24, r.width, 50), title, 40, UI.Gold, TextAnchor.MiddleCenter);
            UI.Text(new Rect(r.x, r.y + 80, r.width, 30), $"{Factions.Get(c.player).name}  vs  {Factions.Get(enemy).name}", 24, Color.white, TextAnchor.MiddleCenter);
            string what = pb.space ? $"Space battle - {Maps.Space[pd.space].name}. Destroy their capital ship, its critical systems, or run them out of tickets. Lose and that fleet is gone."
                                   : $"Land battle on {pd.name} ({Maps.Land[pd.land].theme}). {(attacking ? "Win to take the world; lose and your fleet falls back." : "Hold the world or lose it.")}";
            UI.Text(new Rect(r.x + 50, r.y + 130, r.width - 100, 80), what, 20, UI.Dim, TextAnchor.UpperCenter, true);
            float y = r.y + 230;
            UI.Text(new Rect(r.x + 50, y, 600, 28), "BONUS FOR THIS BATTLE", 20, UI.Gold); y += 34;
            if (UI.Button(new Rect(r.x + 50, y, 300, 44), "None", chosenBonus < 0, true, 18)) chosenBonus = -1;
            for (int i = 0; i < c.bonuses.Count; i++)
                if (UI.Button(new Rect(r.x + 50 + ((i + 1) % 3) * 315, y + ((i + 1) / 3) * 52, 300, 44), Bonuses.Names[c.bonuses[i]], chosenBonus == i, true, 18)) chosenBonus = i;
            y += 110;
            if (chosenBonus >= 0) UI.Text(new Rect(r.x + 50, y, r.width - 100, 30), Bonuses.Info[c.bonuses[chosenBonus]], 18, Color.white);
            y += 40;
            string locked = "";
            var f = Factions.Get(c.player);
            for (int i = 0; i < 5; i++) if (!c.unlocked[i]) locked += f.classes[i].name + ", ";
            if (locked.Length > 0 && !pb.space) UI.Text(new Rect(r.x + 50, y, r.width - 100, 30), "Locked classes: " + locked.TrimEnd(',', ' ') + " (unlock them with BUY / UNLOCK)", 17, UI.Dim);
            if (UI.Button(new Rect(r.x + 60, r.yMax - 100, 440, 70), "FIGHT", false, true, 32, new Color(0.2f, 0.6f, 0.3f))) { Fight(c); return; }
            if (UI.Button(new Rect(r.xMax - 500, r.yMax - 100, 440, 70), "AUTO-RESOLVE", false, true, 26)) AutoResolvePending();
        }

        public void AutoResolvePending()
        {
            var c = camp; var pb = c.pending;
            var bonus = TakeBonus(c);
            bool won = c.AutoResolve(pb, true);
            if (bonus != Bonus.None && Random.value < 0.15f && pb.attacker == c.player) won = true;
            if (bonus != Bonus.None && Random.value < 0.15f && pb.defender == c.player) won = false;
            Resolve(c, pb, won, 0);
        }

        Bonus TakeBonus(Campaign c)
        {
            if (chosenBonus < 0 || chosenBonus >= c.bonuses.Count) return Bonus.None;
            var b = (Bonus)c.bonuses[chosenBonus];
            c.bonuses.RemoveAt(chosenBonus);
            chosenBonus = -1;
            return b;
        }

        public void Fight(Campaign c)
        {
            var pb = c.pending;
            var pd = Galaxy.Planets[pb.planet];
            int enemy = pb.attacker == c.player ? pb.defender : pb.attacker;
            var aiBonuses = c.AvailableBonuses(enemy);
            Bonus aiBonus = aiBonuses.Count > 0 && Random.value < 0.4f + c.difficulty * 0.2f ? aiBonuses[Random.Range(0, aiBonuses.Count)] : Bonus.None;
            var cfg = new BattleConfig
            {
                space = pb.space,
                land = pd.LandMap,
                spaceMap = Maps.Space[pd.space],
                factions = new[] { c.player, enemy },
                playerTeam = 0,
                tickets = c.tickets,
                botsPerTeam = c.bots,
                difficulty = c.difficulty,
                bonus = new[] { TakeBonus(c), aiBonus },
                lockedClasses = new[] { !c.unlocked[0], !c.unlocked[1], !c.unlocked[2], !c.unlocked[3], !c.unlocked[4] },
                conquest = true,
                title = pb.space ? $"Battle over {pd.name}" : pb.attacker == c.player ? $"Invasion of {pd.name}" : $"Defence of {pd.name}",
            };
            c.Save();
            if (view) Destroy(view.gameObject);
            view = null;
            Game.I.StartBattle(cfg, res =>
            {
                Game.I.ClearBackdrop();
                view = GalaxyView.Create(); view.Focus(pb.planet);
                bool playerWon = res.winner == 0 && !res.quit;
                bool attackerWon = (pb.attacker == c.player) == playerWon;
                screen = Screen.Conquest;
                Resolve(c, pb, attackerWon, res.playerPoints);
            });
        }

        void Resolve(Campaign c, PendingBattle pb, bool attackerWon, int points)
        {
            c.pending = new PendingBattle();
            c.Apply(pb, attackerWon, points);
            bool pWon = (pb.attacker == c.player) == attackerWon;
            Banner(pWon ? "VICTORY" : "DEFEAT");
            if (pb.followUpLand && attackerWon && c.winner == -1)
            {
                var land = new PendingBattle { planet = pb.planet, attacker = pb.attacker, defender = c.owner[pb.planet], fleetAttacker = pb.fleetAttacker, fromPlanet = pb.fromPlanet, active = true };
                if (land.attacker == c.player || land.defender == c.player) { c.pending = land; chosenBonus = -1; Banner($"Orbit secured - now invade {Galaxy.Planets[pb.planet].name}!"); }
                else c.Apply(land, c.AutoResolve(land, false));
            }
            if (!c.pending.active && aiQueue.Count == 0 && pb.attacker != c.player) NewTurn();
            c.Save();
        }

        void Ending(Campaign c)
        {
            float w = UI.W;
            UI.Fill(new Rect(0, 0, w, UI.H), new Color(0, 0, 0, 0.6f));
            Rect r = new Rect(w / 2 - 500, 260, 1000, 480);
            UI.Box(r, new Color(0.04f, 0.05f, 0.08f, 0.97f));
            bool won = c.winner == c.player;
            UI.Text(new Rect(r.x, r.y + 40, r.width, 70), won ? "THE GALAXY IS YOURS" : "YOUR EMPIRE HAS FALLEN", 52, won ? UI.Good : UI.Bad, TextAnchor.MiddleCenter);
            UI.Text(new Rect(r.x + 60, r.y + 150, r.width - 120, 120), won ? $"The {Factions.Get(c.player).name} rules all {Galaxy.Planets.Length} worlds after {c.turn} turns.\n\"{Factions.Get(c.player).motto}\"" : $"Defeated on turn {c.turn}. Battles won: {c.battlesWon}, lost: {c.battlesLost}.", 24, Color.white, TextAnchor.UpperCenter, true);
            if (UI.Button(new Rect(r.center.x - 200, r.yMax - 110, 400, 70), "BACK TO MENU", false, true, 28)) { Campaign.DeleteSave(); CloseCampaign(); }
        }
    }
}
