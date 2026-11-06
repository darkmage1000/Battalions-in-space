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

        /// <summary>Builds a land map and saves Previews/Maps/&lt;name&gt;.png: the minimap beside the bot walk grid (grey walk, dark blocked, tan walk-only, blue wade).</summary>
        public static string MapShot(int map)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Start(false, map, 0, 1, 2, 200);
            long ms = sw.ElapsedMilliseconds;
            var land = Battle.I.land;
            var nav = land.nav.Picture();
            int s = 512;
            var outT = new Texture2D(s * 2 + 8, s, TextureFormat.RGB24, false);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    // the minimap covers the whole map; the nav grid only the playable square
                    float wx = -land.half + (x + 0.5f) / s * land.size, wz = -land.half + (y + 0.5f) / s * land.size;
                    outT.SetPixel(x, y, land.minimap.GetPixelBilinear((x + 0.5f) / s, (y + 0.5f) / s));
                    float u = (wx + land.PlayHalf) / (land.PlayHalf * 2), v = (wz + land.PlayHalf) / (land.PlayHalf * 2);
                    Color c = u < 0 || v < 0 || u > 1 || v > 1 ? Color.black : nav.GetPixel(Mathf.Clamp((int)(u * nav.width), 0, nav.width - 1), Mathf.Clamp((int)(v * nav.height), 0, nav.height - 1));
                    outT.SetPixel(x + s + 8, y, c);
                }
            foreach (var cp in Battle.I.posts)
            {
                float u = (cp.Pos.x + land.half) / land.size, v = (cp.Pos.z + land.half) / land.size;
                for (int dy = -6; dy <= 6; dy++) for (int dx = -6; dx <= 6; dx++)
                    {
                        Color c = cp.owner < 0 ? Color.white : cp.owner == 0 ? Color.cyan : Color.red;
                        int px = (int)(u * s) + dx, py = (int)(v * s) + dy;
                        if (px >= 0 && py >= 0 && px < s && py < s) outT.SetPixel(px, py, c);
                        float nu = (cp.Pos.x + land.PlayHalf) / (land.PlayHalf * 2), nv = (cp.Pos.z + land.PlayHalf) / (land.PlayHalf * 2);
                        int qx = (int)(nu * s) + dx + s + 8, qy = (int)(nv * s) + dy;
                        if (qx >= s + 8 && qy >= 0 && qx < s * 2 + 8 && qy < s) outT.SetPixel(qx, qy, c);
                    }
            }
            outT.Apply();
            System.IO.Directory.CreateDirectory("Previews/Maps");
            string path = $"Previews/Maps/{land.def.name.Replace(' ', '_')}.png";
            System.IO.File.WriteAllBytes(path, outT.EncodeToPNG());
            return $"{land.def.name}: built in {ms} ms, blocked foot {land.nav.blockedFoot} veh {land.nav.blockedVeh} -> {path}";
        }

        static float[] wAlone, wContest, wNone, wNear;
        static int[] wOwnerAt;
        static StringBuilder wLog;
        static float wTime;

        /// <summary>
        /// Steps the battle and records, per post: seconds with only the enemy of its owner inside (taking it),
        /// seconds contested, seconds with enemies within 60 m but not inside, and every change of hands.
        /// Call repeatedly (each call ≤ ~50 s); WatchReport() prints the totals.
        /// </summary>
        public static void Watch(float seconds, bool reset = false)
        {
            var b = Battle.I;
            if (reset || wAlone == null || wAlone.Length != b.posts.Count)
            {
                int n = b.posts.Count;
                wAlone = new float[n]; wContest = new float[n]; wNone = new float[n]; wNear = new float[n]; wOwnerAt = new int[n];
                for (int i = 0; i < n; i++) wOwnerAt[i] = b.posts[i].owner;
                wLog = new StringBuilder(); wTime = 0;
            }
            EditorApplication.isPaused = true;
            int steps = Mathf.CeilToInt(seconds / 0.02f);
            for (int s = 0; s < steps; s++)
            {
                EditorApplication.Step();
                wTime += 0.02f;
                for (int i = 0; i < b.posts.Count; i++)
                {
                    var cp = b.posts[i];
                    if (cp.owner != wOwnerAt[i]) { wLog.Append($"{b.time:0}s {cp.postName}: {wOwnerAt[i]} -> {cp.owner}\n"); wOwnerAt[i] = cp.owner; }
                    int o = cp.owner;
                    int foe = o >= 0 ? 1 - o : -1;
                    if (o < 0) { if (cp.present[0] + cp.present[1] > 0 && !cp.contested) wAlone[i] += 0.02f; }
                    else if (cp.present[foe] > 0 && cp.present[o] == 0) wAlone[i] += 0.02f;
                    if (cp.contested) wContest[i] += 0.02f;
                    if (foe >= 0 && cp.present[foe] == 0)
                    {
                        bool near = false;
                        foreach (var u in b.units) if (u && u.Alive && u.team == foe && (u.transform.position - cp.Pos).sqrMagnitude < 60 * 60) { near = true; break; }
                        if (near) wNear[i] += 0.02f; else wNone[i] += 0.02f;
                    }
                }
            }
        }

        public static string WatchReport()
        {
            var b = Battle.I;
            var sb = new StringBuilder($"watched {wTime:0} s, tickets {b.teams[0].tickets:0}/{b.teams[1].tickets:0}, squad spawns {b.mateSpawns}\n");
            for (int i = 0; i < b.posts.Count; i++)
                sb.Append($"{b.posts[i].postName} (owner {b.posts[i].owner}, r {b.posts[i].radius}): taking {wAlone[i]:0}s contested {wContest[i]:0}s foes-near-not-in {wNear[i]:0}s no-foes {wNone[i]:0}s\n");
            sb.Append(wLog);
            if (b.commander != null)
                for (int t = 0; t < 2; t++)
                    foreach (var s in b.commander.squads[t])
                    {
                        int alive = 0; float dsum = 0;
                        foreach (var m in s.members) if (m.unit && m.unit.Alive) { alive++; if (s.target) dsum += Vector3.Distance(m.unit.transform.position, s.target.Pos); }
                        sb.Append($"t{t} sq{s.id} {Commander.OrderName(s.order)} {(s.target ? s.target.postName : "-")} gather {s.gathering} alive {alive} avgDist {(alive > 0 ? dsum / alive : 0):0}\n");
                    }
            return sb.ToString();
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
