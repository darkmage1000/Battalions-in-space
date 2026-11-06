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

        /// <summary>Every faction's tank, transport, gunship and four starships in rows high above the world.</summary>
        public static void VehicleLineup()
        {
            Game.I.ClearBackdrop(); Game.I.menu.Hide();
            var root = new GameObject("VehicleLineup"); root.transform.position = new Vector3(0, 3000, 0);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.SetParent(root.transform, false);
            floor.transform.localScale = new Vector3(160, 0.2f, 90); floor.transform.localPosition = new Vector3(0, -0.1f, 24); floor.GetComponent<Renderer>().sharedMaterial = Mats.Lit;
            for (int f = 0; f < 4; f++)
            {
                var fd = Factions.All[f];
                var defs = new[] { fd.tank, fd.transport, fd.air, fd.ships[0], fd.ships[1], fd.ships[2], fd.ships[3] };
                float x = -64;
                foreach (var d in defs)
                {
                    var m = VehicleModels.Get(d, fd);
                    var go = new GameObject(d.name); go.transform.SetParent(root.transform, false);
                    void Part(Mesh mesh, Vector3 lp) { if (!mesh) return; var p = new GameObject("p"); p.transform.SetParent(go.transform, false); p.transform.localPosition = lp; p.AddComponent<MeshFilter>().sharedMesh = mesh; p.AddComponent<MeshRenderer>().sharedMaterial = Mats.Lit; }
                    Part(m.body, Vector3.zero); Part(m.turret, m.turretPos); Part(m.gun, m.gunPos); if (d.gunner != null) Part(m.gunnerTurret, m.gunnerPos);
                    float w = Mathf.Max(m.bounds.size.x, 6);
                    x += w / 2 + 2;
                    go.transform.localPosition = new Vector3(x, 1.5f + m.bounds.extents.y, f * 15);
                    go.transform.localRotation = Quaternion.Euler(0, 150, 0);
                    x += w / 2;
                }
            }
            Game.I.sun.transform.rotation = Quaternion.Euler(45, 160, 0); Game.I.sun.shadows = LightShadows.Soft;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.55f);
            var cam = Game.Cam; cam.transform.position = new Vector3(0, 3040, -50); cam.transform.rotation = Quaternion.Euler(35, 0, 0); cam.fieldOfView = 55;
        }

        /// <summary>
        /// Walks a driverless test soldier out of every command post in 12 directions for `seconds` (with a
        /// jump when it stops moving) and reports the walks that ended less than `need` metres from the post.
        /// </summary>
        public static string PostEscape(float seconds = 6, float need = 28)
        {
            var b = Battle.I;
            var p = new Participant { name = "walker", team = 0 };
            var sb = new StringBuilder();
            int fails = 0, total = 0;
            foreach (var cp in b.posts)
                for (int k = 0; k < 12; k++)
                {
                    float a = k * 30 * Mathf.Deg2Rad;
                    Vector3 dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    Vector3 start = cp.Pos + dir * 2; start.y = b.GroundY(start) + 0.3f;
                    var u = b.SpawnUnit(p, Factions.Get(0).classes[0], null, start, 0);
                    u.bot = null; u.invulnUntil = Time.time + 999;
                    float lastD = 0, still = 0;
                    for (float t = 0; t < seconds; t += 0.02f)
                    {
                        u.input = new UnitInput { move = dir, aimPoint = u.Eye + dir * 50, sprint = true };
                        float d = Vector3.Distance(new Vector3(u.transform.position.x, 0, u.transform.position.z), new Vector3(cp.Pos.x, 0, cp.Pos.z));
                        if (d - lastD < 0.01f) still += 0.02f; else still = 0;
                        lastD = d;
                        EditorApplication.Step();
                    }
                    float dist = Vector3.Distance(new Vector3(u.transform.position.x, 0, u.transform.position.z), new Vector3(cp.Pos.x, 0, cp.Pos.z));
                    total++;
                    if (dist < need)
                    {
                        fails++;
                        string what = "";
                        if (Physics.SphereCast(u.transform.position + Vector3.up * 0.8f, 0.35f, dir, out var h, 2f, Layers.MoveMask & ~(1 << Layers.Ground), QueryTriggerInteraction.Ignore)) what = "blocked by " + h.collider.name + "/" + (h.collider.transform.parent ? h.collider.transform.parent.name : "");
                        else
                        {
                            Vector3 ahead = u.transform.position + dir * 3;
                            float rise = b.GroundY(ahead) - b.GroundY(u.transform.position);
                            what = $"slope rise {rise:0.0} m over 3 m";
                        }
                        sb.Append($"{cp.postName} dir {k * 30}: stopped at {dist:0.0} m ({what})\n");
                    }
                    b.units.Remove(u); Object.DestroyImmediate(u.gameObject);
                }
            return $"{fails} of {total} walks stuck\n" + sb;
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
