using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BIS
{
    [Serializable]
    public class PlanetDef
    {
        public string name, blurb;
        public Vector2 pos;
        public int land;        // index into Maps.Land (theme + post layout)
        public int seed;
        public int space;       // index into Maps.Space for orbital fights
        public Bonus bonus;
        public int income;
        public Color color;
        public LandMapDef LandMap => Maps.Land[land].Variant(name, seed);
    }

    /// <summary>The galaxy of Battalions in Space: twelve worlds joined by hyperlanes.</summary>
    public static class Galaxy
    {
        public static readonly PlanetDef[] Planets =
        {
            P("Harrak", "Sun-scorched mining world of dunes and arches.", -1.0f, -0.35f, 0, 1041, 0, Bonus.EnergyCells, 120, new Color(0.85f, 0.62f, 0.4f)),
            P("Elos", "A forest moon of giant trees; the Concord's cradle.", -2.6f, -1.8f, 1, 2203, 1, Bonus.FieldMedics, 150, new Color(0.35f, 0.62f, 0.3f)),
            P("Frostfall", "Ice ridges over a buried relay network.", -0.4f, 1.9f, 2, 3371, 2, Bonus.Recon, 100, new Color(0.85f, 0.9f, 1f)),
            P("Cinder", "A world still being born from lava.", 1.1f, 0.3f, 3, 4489, 0, Bonus.ArmoredPlating, 140, new Color(0.6f, 0.25f, 0.15f)),
            P("Vessa Prime", "Wooded hills around an old Concord fortress.", -1.6f, -2.9f, 1, 5120, 2, Bonus.Garrison, 110, new Color(0.3f, 0.5f, 0.35f)),
            P("Duun", "Glass deserts turned to Synod foundries.", 2.6f, -2.0f, 0, 6311, 1, Bonus.Saboteurs, 130, new Color(0.9f, 0.75f, 0.5f)),
            P("Kharos", "Volcanic throne world of the Dominion.", 2.8f, 2.2f, 3, 7019, 0, Bonus.Reinforcements, 160, new Color(0.5f, 0.2f, 0.12f)),
            P("Ysil", "Frozen sanctum of the Kessari Veil.", -2.8f, 2.4f, 2, 8183, 1, Bonus.VeteranOfficers, 150, new Color(0.7f, 0.85f, 0.95f)),
            P("Oberon's Reach", "Rainforest borderland between empires.", 0.4f, -1.6f, 1, 9277, 2, Bonus.Reinforcements, 100, new Color(0.25f, 0.55f, 0.4f)),
            P("Taln", "Salt flats and drifting dust storms.", 1.9f, -0.8f, 0, 1093, 2, Bonus.EnergyCells, 90, new Color(0.88f, 0.8f, 0.6f)),
            P("Morrow", "A dim snowball at the galaxy's edge.", -2.0f, 0.6f, 2, 1171, 0, Bonus.ArmoredPlating, 90, new Color(0.75f, 0.8f, 0.9f)),
            P("Pyrrhus", "Ash plains ringed by fire mountains.", 1.0f, 2.6f, 3, 1259, 1, Bonus.FieldMedics, 110, new Color(0.45f, 0.22f, 0.18f)),
        };

        public static readonly int[][] Lanes =
        {
            new[] { 1, 4 }, new[] { 1, 10 }, new[] { 4, 8 }, new[] { 1, 0 }, new[] { 0, 8 }, new[] { 0, 10 }, new[] { 0, 3 },
            new[] { 8, 9 }, new[] { 8, 5 }, new[] { 9, 5 }, new[] { 9, 3 }, new[] { 3, 6 }, new[] { 3, 11 }, new[] { 6, 11 },
            new[] { 11, 2 }, new[] { 2, 7 }, new[] { 2, 10 }, new[] { 7, 10 }, new[] { 2, 3 }, new[] { 5, 6 },
        };

        /// <summary>Home world of each faction (Concord, Dominion, Synod, Veil).</summary>
        public static readonly int[] Home = { 1, 6, 5, 7 };

        static PlanetDef P(string n, string b, float x, float y, int land, int seed, int space, Bonus bonus, int income, Color c)
            => new PlanetDef { name = n, blurb = b, pos = new Vector2(x, y), land = land, seed = seed, space = space, bonus = bonus, income = income, color = c };

        public static bool Linked(int a, int b)
        {
            foreach (var l in Lanes) if ((l[0] == a && l[1] == b) || (l[0] == b && l[1] == a)) return true;
            return false;
        }

        public static List<int> Neighbours(int a)
        {
            var r = new List<int>();
            foreach (var l in Lanes) { if (l[0] == a) r.Add(l[1]); else if (l[1] == a) r.Add(l[0]); }
            return r;
        }

        public static int Hops(int a, int b)
        {
            var dist = new int[Planets.Length];
            for (int i = 0; i < dist.Length; i++) dist[i] = -1;
            var q = new Queue<int>(); q.Enqueue(a); dist[a] = 0;
            while (q.Count > 0) { int n = q.Dequeue(); foreach (var m in Neighbours(n)) if (dist[m] < 0) { dist[m] = dist[n] + 1; q.Enqueue(m); } }
            return dist[b];
        }
    }

    [Serializable]
    public class FleetState
    {
        public int id, faction, planet;
    }

    /// <summary>A pending battle the player has to fight (or auto-resolve).</summary>
    [Serializable]
    public class PendingBattle
    {
        public bool active;
        public bool space;
        public int planet, attacker, defender;     // faction ids
        public int fleetAttacker = -1, fleetDefender = -1;
        public int fromPlanet = -1;
        public bool followUpLand;                  // after winning in orbit, invade the planet
    }

    /// <summary>Saved Galactic Conquest state and the rules that change it.</summary>
    [Serializable]
    public class Campaign
    {
        public const int FleetCost = 1500, MaxFleets = 6, MaxBonuses = 3;
        public static readonly int[] ClassCost = { 0, 900, 900, 1100, 1300 };
        public int player;
        public int[] factions;
        public int[] owner;
        public List<FleetState> fleets = new List<FleetState>();
        public int[] credits = new int[4];
        public int turn = 1;
        public List<int> bonuses = new List<int>();   // player's bought bonuses (Bonus as int)
        public bool[] unlocked = new bool[5];
        public bool moved;
        public int nextFleetId = 1;
        public int winner = -1;
        public int difficulty = 1;
        public int tickets = 150;
        public int bots = 16;
        public List<string> log = new List<string>();
        public PendingBattle pending = new PendingBattle();
        public int battlesWon, battlesLost;

        public static string SavePath => Path.Combine(Application.persistentDataPath, "conquest.json");
        public static bool HasSave => File.Exists(SavePath);

        public static Campaign New(int player, int[] opponents, int difficulty, int tickets, int bots)
        {
            var c = new Campaign { player = player, difficulty = difficulty, tickets = tickets, bots = bots };
            var list = new List<int> { player }; list.AddRange(opponents);
            c.factions = list.ToArray();
            int n = Galaxy.Planets.Length;
            c.owner = new int[n];
            // each world goes to the nearest home world (by hyperlane hops, ties by distance)
            for (int p = 0; p < n; p++)
            {
                int best = -1; float bs = float.MaxValue;
                foreach (var f in c.factions)
                {
                    int home = Galaxy.Home[f];
                    float s = Galaxy.Hops(p, home) * 10 + Vector2.Distance(Galaxy.Planets[p].pos, Galaxy.Planets[home].pos);
                    if (s < bs) { bs = s; best = f; }
                }
                c.owner[p] = best;
            }
            foreach (var f in c.factions)
            {
                c.credits[f] = 1500;
                c.AddFleet(f, Galaxy.Home[f]);
                // a second fleet on the owned world nearest the front
                int second = -1; float sd = float.MaxValue;
                for (int p = 0; p < n; p++)
                {
                    if (c.owner[p] != f || p == Galaxy.Home[f]) continue;
                    float d = float.MaxValue;
                    for (int q = 0; q < n; q++) if (c.owner[q] != f) d = Mathf.Min(d, Galaxy.Hops(p, q));
                    if (d < sd) { sd = d; second = p; }
                }
                if (second >= 0) c.AddFleet(f, second);
            }
            c.unlocked = new[] { true, true, true, false, false };
            c.Log($"Turn 1. The {Factions.Get(player).name} prepares for war.");
            return c;
        }

        public void Log(string s)
        {
            log.Add(s);
            if (log.Count > 40) log.RemoveAt(0);
        }

        public FleetState AddFleet(int f, int planet)
        {
            var fl = new FleetState { id = nextFleetId++, faction = f, planet = planet };
            fleets.Add(fl);
            return fl;
        }

        public FleetState FleetAt(int planet) => fleets.Find(x => x.planet == planet);
        public FleetState Fleet(int id) => fleets.Find(x => x.id == id);
        public int FleetCount(int f) => fleets.FindAll(x => x.faction == f).Count;
        public int Planets(int f) { int n = 0; foreach (var o in owner) if (o == f) n++; return n; }
        public bool Alive(int f) => Planets(f) > 0;
        public int Income(int f)
        {
            int s = 200;
            for (int p = 0; p < owner.Length; p++) if (owner[p] == f) s += Galaxy.Planets[p].income;
            return s;
        }

        public List<Bonus> AvailableBonuses(int f)
        {
            var r = new List<Bonus>();
            for (int p = 0; p < owner.Length; p++) if (owner[p] == f && !r.Contains(Galaxy.Planets[p].bonus)) r.Add(Galaxy.Planets[p].bonus);
            return r;
        }

        public void Save()
        {
            try { File.WriteAllText(SavePath, JsonUtility.ToJson(this, true)); } catch (Exception e) { Debug.LogWarning("conquest save failed: " + e.Message); }
        }

        public static Campaign Load()
        {
            try { return JsonUtility.FromJson<Campaign>(File.ReadAllText(SavePath)); } catch { return null; }
        }

        public static void DeleteSave() { try { if (HasSave) File.Delete(SavePath); } catch { } }

        // ── outcomes ─────────────────────────────────────────────────────
        public void CheckWinner()
        {
            if (!Alive(player)) { winner = -2; Log("Your last world has fallen."); return; }
            bool any = false;
            foreach (var f in factions) if (f != player && Alive(f)) any = true;
            if (!any) { winner = player; Log("The galaxy is yours!"); }
            // factions with no worlds lose their fleets
            fleets.RemoveAll(x => !Alive(x.faction));
        }

        /// <summary>Resolve a battle without playing it. Returns true if the attacker wins.</summary>
        public bool AutoResolve(PendingBattle pb, bool playerInvolved)
        {
            float pA = 0.5f;
            pA += (Planets(pb.attacker) - Planets(pb.defender)) * 0.025f;
            if (!pb.space && FleetAt(pb.planet) == null) pA += 0.1f;          // undefended world
            if (playerInvolved) pA += (pb.attacker == player ? 1 : -1) * (0.08f - difficulty * 0.08f);
            pA = Mathf.Clamp(pA, 0.15f, 0.85f);
            return UnityEngine.Random.value < pA;
        }

        /// <summary>Apply the result of a battle (fought or auto-resolved).</summary>
        public void Apply(PendingBattle pb, bool attackerWon, int playerPoints = 0)
        {
            var pl = Galaxy.Planets[pb.planet];
            string an = Factions.Get(pb.attacker).shortName, dn = Factions.Get(pb.defender).shortName;
            var af = Fleet(pb.fleetAttacker);
            if (pb.space)
            {
                if (attackerWon)
                {
                    var df = Fleet(pb.fleetDefender); if (df != null) fleets.Remove(df);
                    Log($"{an} fleet destroyed the {dn} fleet over {pl.name}.");
                    if (af != null) af.planet = pb.planet;
                    if (owner[pb.planet] != pb.attacker) pb.followUpLand = true;
                }
                else
                {
                    if (af != null) fleets.Remove(af);
                    Log($"{an} fleet was destroyed over {pl.name}.");
                }
            }
            else
            {
                if (attackerWon)
                {
                    owner[pb.planet] = pb.attacker;
                    if (af != null) af.planet = pb.planet;
                    var df = FleetAt(pb.planet); if (df != null && df.faction != pb.attacker) fleets.Remove(df);
                    Log($"{an} captured {pl.name}!");
                }
                else
                {
                    if (af != null) { if (pb.fromPlanet >= 0 && FleetAt(pb.fromPlanet) == null) af.planet = pb.fromPlanet; else fleets.Remove(af); }
                    Log($"{an} invasion of {pl.name} was thrown back.");
                }
            }
            // rewards
            int win = 500, lose = 150;
            bool pWon = (pb.attacker == player && attackerWon) || (pb.defender == player && !attackerWon);
            if (pb.attacker == player || pb.defender == player)
            {
                credits[player] += (pWon ? win : lose) + playerPoints * 5;
                if (pWon) battlesWon++; else battlesLost++;
                int ai = pb.attacker == player ? pb.defender : pb.attacker;
                credits[ai] += pWon ? lose : win;
            }
            else { credits[pb.attacker] += attackerWon ? win : lose; credits[pb.defender] += attackerWon ? lose : win; }
            CheckWinner();
        }

        // ── turns ────────────────────────────────────────────────────────
        /// <summary>Start a move. Returns a battle the player must fight, or null.</summary>
        public PendingBattle Move(FleetState f, int to)
        {
            int from = f.planet;
            var there = FleetAt(to);
            var pb = new PendingBattle { planet = to, attacker = f.faction, fleetAttacker = f.id, fromPlanet = from };
            if (there != null && there.faction != f.faction)
            {
                pb.space = true; pb.defender = there.faction; pb.fleetDefender = there.id; pb.active = true;
                return pb;
            }
            if (there != null && there.faction == f.faction) return null;   // blocked by own fleet
            if (owner[to] != f.faction)
            {
                pb.space = false; pb.defender = owner[to]; pb.active = true;
                return pb;
            }
            f.planet = to;
            Log($"{Factions.Get(f.faction).shortName} fleet moved to {Galaxy.Planets[to].name}.");
            return null;
        }

        /// <summary>One AI faction's turn: income, purchases and one fleet move. A battle against the player is returned instead of resolved.</summary>
        public PendingBattle AITurn(int f)
        {
            credits[f] += Income(f);
            if (!Alive(f)) return null;
            // build fleets on frontier worlds
            while (credits[f] >= FleetCost + 400 && FleetCount(f) < Mathf.Min(MaxFleets, Planets(f)))
            {
                int best = -1; int bs = 99;
                for (int p = 0; p < owner.Length; p++)
                {
                    if (owner[p] != f || FleetAt(p) != null) continue;
                    int d = 99; foreach (var n in Galaxy.Neighbours(p)) if (owner[n] != f) d = 1;
                    if (d < bs) { bs = d; best = p; }
                    if (best < 0) best = p;
                }
                if (best < 0) break;
                credits[f] -= FleetCost; AddFleet(f, best);
                Log($"{Factions.Get(f).shortName} built a fleet at {Galaxy.Planets[best].name}.");
            }
            // choose a move
            FleetState mf = null; int target = -1; float bestScore = 0;
            foreach (var fl in fleets)
            {
                if (fl.faction != f) continue;
                foreach (var n in Galaxy.Neighbours(fl.planet))
                {
                    var o = FleetAt(n);
                    float s = 0;
                    if (o != null && o.faction == f) continue;
                    if (owner[n] != f && o == null) s = 3 + Galaxy.Planets[n].income / 100f;
                    else if (o != null) s = 2.2f;
                    else s = 0.4f;   // repositioning
                    if (owner[n] == player || (o != null && o.faction == player)) s *= difficulty == 0 ? 0.7f : 1.1f;
                    s *= UnityEngine.Random.Range(0.7f, 1.3f);
                    // do not leave the home world empty if threatened
                    if (fl.planet == Galaxy.Home[f]) s *= 0.7f;
                    if (s > bestScore) { bestScore = s; mf = fl; target = n; }
                }
            }
            if (mf == null || UnityEngine.Random.value < 0.12f) { Log($"{Factions.Get(f).shortName} holds position."); return null; }
            var pb = Move(mf, target);
            if (pb == null) return null;
            if (pb.defender == player) return pb;   // the player decides
            bool won = AutoResolve(pb, false);
            Apply(pb, won);
            if (pb.followUpLand && won)
            {
                var land = new PendingBattle { planet = pb.planet, attacker = f, defender = owner[pb.planet], fleetAttacker = mf.id, fromPlanet = pb.fromPlanet, active = true };
                if (land.defender == player) return land;
                Apply(land, AutoResolve(land, false));
            }
            return null;
        }
    }
}
