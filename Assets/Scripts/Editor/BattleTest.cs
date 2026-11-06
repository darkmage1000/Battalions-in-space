using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace BIS.EditorTools
{
    /// <summary>
    /// Dev harness for Play mode (call from execute_code): the editor does not tick while it is in the
    /// background, so Step() advances frames by hand (0.02 s each).
    /// </summary>
    public static class BattleTest
    {
        public static void Step(float seconds)
        {
            EditorApplication.isPaused = true;
            int n = Mathf.CeilToInt(seconds / 0.02f);
            for (int i = 0; i < n; i++) EditorApplication.Step();
        }

        public static string Start(bool space, int map, int f0, int f1, int bots = 16, int tickets = 200)
        {
            Step(0.06f);
            if (Battle.I) Battle.I.Finish(true);
            var cfg = new BattleConfig { space = space, land = Maps.Land[map % Maps.Land.Length], spaceMap = Maps.Space[map % Maps.Space.Length], factions = new[] { f0, f1 }, botsPerTeam = bots, tickets = tickets, difficulty = 1 };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Game.I.StartBattle(cfg, r => Game.I.ShowMenu());
            return $"built in {sw.ElapsedMilliseconds} ms";
        }

        public static string Report()
        {
            var b = Battle.I;
            if (!b) return "no battle";
            var sb = new StringBuilder();
            int alive = 0, inveh = 0, kills = 0, heroes = 0, awards = 0;
            foreach (var u in b.units) if (u && u.Alive) { alive++; if (u.InVehicle) inveh++; if (u.IsHero) heroes++; }
            foreach (var p in b.people) { kills += p.kills; foreach (var a in p.awards) if (a) awards++; }
            sb.Append($"t={b.time:0} alive {alive} inveh {inveh} heroes {heroes} awards {awards} veh {b.vehicles.Count} tickets {b.teams[0].tickets:0}/{b.teams[1].tickets:0} kills {kills} over {b.over} {b.result?.reason}\n");
            if (b.land) { foreach (var p in b.posts) sb.Append($"{p.postName}:{p.owner} "); sb.Append('\n'); }
            if (b.space)
            {
                foreach (var s in b.space.ships)
                {
                    sb.Append($"ship{s.team} hull {s.hull:0} shield {s.ShieldUp}: ");
                    foreach (var x in s.systems) sb.Append(x.sysName.Substring(0, 2) + (x.destroyed ? "X" : ((int)(x.hp / x.maxHp * 100)).ToString()) + " ");
                    sb.Append('\n');
                }
                var jf = typeof(BotBrain).GetField("job", BindingFlags.NonPublic | BindingFlags.Instance);
                var counts = new System.Collections.Generic.Dictionary<string, int>();
                foreach (var u in b.units)
                    if (u && u.Alive && u.bot != null)
                    {
                        var sh = b.space.ShipAt(u.transform.position);
                        string k = (u.InVehicle ? u.vehicle.def.kind.ToString() : (sh && sh.team != u.team ? "BOARDER" : "foot")) + ":" + jf.GetValue(u.bot);
                        counts[k] = counts.TryGetValue(k, out var c) ? c + 1 : 1;
                    }
                foreach (var kv in counts) sb.Append($"{kv.Key}={kv.Value} ");
            }
            return sb.ToString();
        }

        public static void SpawnPlayer(int cls = 0, int ship = -1)
        {
            var b = Battle.I;
            b.control.selClass = cls; b.control.selFoot = Mathf.Max(0, cls); b.control.selShip = Mathf.Max(0, ship); b.control.spaceOnFoot = ship < 0;
            b.control.TrySpawn(false);
        }

        /// <summary>Put the player (on foot) at a position looking at a target.</summary>
        public static void Place(Vector3 p, Vector3 look)
        {
            var b = Battle.I; var me = b.player.unit;
            if (!me) return;
            me.invulnUntil = Time.time + 9999;
            if (me.InVehicle) { me.vehicle.transform.position = p; me.vehicle.transform.rotation = Quaternion.LookRotation(look - p); return; }
            me.cc.enabled = false; me.transform.position = p; me.cc.enabled = true;
            Vector3 d = look - p;
            b.control.camYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            b.control.camPitch = -Mathf.Asin(Mathf.Clamp(d.normalized.y, -1, 1)) * Mathf.Rad2Deg;
        }
    }
}
