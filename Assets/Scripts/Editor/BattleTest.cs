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

        /// <summary>
        /// Can the bots get from every post to every other on foot and in a vehicle? (A* on the walk grid.)
        /// Build the map in a call of its own first: colliders of a map finished in the same call are still there.
        /// </summary>
        public static string Routes()
        {
            var b = Battle.I;
            if (!b || !b.land) return "no land battle";
            var nav = b.land.nav;
            var m = typeof(Nav).GetMethod("AStar", BindingFlags.NonPublic | BindingFlags.Instance);
            var path = new System.Collections.Generic.List<Vector3>();
            int foot = 0, veh = 0, n = 0; var sb = new StringBuilder();
            for (int i = 0; i < b.posts.Count; i++)
                for (int j = i + 1; j < b.posts.Count; j++)
                {
                    n++;
                    if ((bool)m.Invoke(nav, new object[] { b.posts[i].Pos, b.posts[j].Pos, false, path })) foot++; else sb.Append($"no foot route {b.posts[i].postName} - {b.posts[j].postName}; ");
                    if ((bool)m.Invoke(nav, new object[] { b.posts[i].Pos, b.posts[j].Pos, true, path })) veh++; else sb.Append($"no vehicle route {b.posts[i].postName} - {b.posts[j].postName}; ");
                }
            return $"{b.cfg.land.name}: foot {foot}/{n}, vehicle {veh}/{n}, enterable buildings {b.land.houses.Count}. {sb}";
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
                    void Part(Mesh mesh, Vector3 lp, Material mat = null) { if (!mesh) return; var p = new GameObject("p"); p.transform.SetParent(go.transform, false); p.transform.localPosition = lp; p.AddComponent<MeshFilter>().sharedMesh = mesh; p.AddComponent<MeshRenderer>().sharedMaterial = mat ? mat : Mats.Lit; }
                    Part(m.body, Vector3.zero, m.mat); Part(m.turret, m.turretPos); Part(m.gun, m.gunPos); if (d.gunner != null) Part(m.gunnerTurret, m.gunnerPos);
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

        /// <summary>Every faction's five classes and a hero standing in rows on a floor 3000 m up (needs a land battle running).</summary>
        public static string SoldierLineup()
        {
            var b = Battle.I;
            var old = GameObject.Find("LineupFloor"); if (old) Object.Destroy(old);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "LineupFloor";
            floor.transform.position = new Vector3(0, 2999.9f, 10); floor.transform.localScale = new Vector3(40, 0.2f, 40); floor.GetComponent<Renderer>().sharedMaterial = Mats.Lit;
            floor.layer = Layers.Ground;
            int n = 0;
            for (int f = 0; f < 4; f++)
                for (int ci = 0; ci < 6; ci++)
                {
                    var p = new Participant { name = "L" + f + ci, team = f % 2 };
                    var fd = Factions.All[f];
                    Vector3 pos = new Vector3((ci - 2.5f) * 1.9f, 3000.2f, f * 3.2f);
                    var u = b.SpawnUnit(p, ci < 5 ? fd.classes[ci] : null, ci < 5 ? null : fd.heroes[f % fd.heroes.Length], pos, 180);
                    u.bot = null; u.invulnUntil = Time.time + 9999;
                    u.input = new UnitInput { aimPoint = u.Eye + u.transform.forward * 30 };
                    n++;
                }
            RenderSettings.fog = false;
            Game.I.sun.transform.rotation = Quaternion.Euler(40, 200, 0);
            return n + " soldiers";
        }

        /// <summary>Imported models in a row 3000 m up (flat ground, sun), camera looking at them; returns sizes.</summary>
        public static string ModelRow(string[] paths, string[] textures, float size, float camYaw = 150, float rotX = 0)
        {
            Game.I.ClearBackdrop(); Game.I.menu.Hide();
            var old = GameObject.Find("ModelRow"); if (old) Object.Destroy(old);
            var root = new GameObject("ModelRow"); root.transform.position = new Vector3(0, 3000, 0);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.SetParent(root.transform, false);
            floor.transform.localScale = new Vector3(paths.Length * size * 1.4f + 20, 0.2f, size * 3); floor.transform.localPosition = new Vector3(0, -0.1f, 0); floor.GetComponent<Renderer>().sharedMaterial = Mats.Lit;
            var sb = new StringBuilder();
            float x = -(paths.Length - 1) * size * 0.7f;
            for (int i = 0; i < paths.Length; i++)
            {
                var tex = textures != null && i < textures.Length && textures[i] != null ? ImportedModels.Tex(textures[i]) : null;
                var m = ImportedModels.Load(paths[i], tex == null, Quaternion.Euler(rotX, 0, 0), size, 1, true);
                if (!m) { sb.Append(paths[i] + " missing\n"); continue; }
                ImportedModels.Spawn(m, tex ? Mats.LitTex(tex, Color.white) : Mats.Lit, root.transform, root.transform.position + new Vector3(x, 0.5f, 0), Quaternion.identity, paths[i]);
                sb.Append($"{paths[i]} {m.bounds.size:0.0}\n");
                x += size * 1.4f;
            }
            Game.I.sun.transform.rotation = Quaternion.Euler(45, 160, 0); Game.I.sun.shadows = LightShadows.Soft;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.55f); RenderSettings.fog = false;
            var cam = Game.Cam; float span = paths.Length * size * 1.4f;
            cam.transform.rotation = Quaternion.Euler(28, camYaw, 0);
            cam.transform.position = root.transform.position + Vector3.up * 2 - cam.transform.forward * span * 0.75f;
            cam.fieldOfView = 50;
            return sb.ToString();
        }

        /// <summary>Builds a land map and saves Previews/Maps/&lt;name&gt;.png: the minimap beside the bot walk grid (grey walk, dark blocked, tan walk-only, blue wade).</summary>
        /// <summary>Starts a battle royale (players = you + bots; classes on / off).</summary>
        /// <summary>Watch every bot on foot for a while and say how they spend their time: fighting, moving, standing about,
        /// stuck, blind to an enemy in plain view - plus shots, hits and kills. Works in land battles and the royale.</summary>
        public static string BotSample(float seconds, float step = 0.5f)
        {
            var b = Battle.I; if (!b) return "no battle";
            var BF = BindingFlags.NonPublic | BindingFlags.Instance;
            var bt = typeof(BotBrain);
            FieldInfo fTarget = bt.GetField("target", BF), fStuck = bt.GetField("stuckT", BF), fGoal = bt.GetField("moveGoal", BF), fSquad = bt.GetField("squad", BF), fObj = bt.GetField("objective", BF);
            int n = 0, withTarget = 0, firing = 0, moving = 0, still = 0, stuck = 0, blind = 0, hurtNoTarget = 0, gathering = 0, inVeh = 0, aimingSniper = 0, sniperFiring = 0, sniperScoped = 0;
            float tdSum = 0; int tdN = 0; int far = 0;
            int shots = 0, unitHits = 0, allHits = 0;
            var lastFire = new System.Collections.Generic.Dictionary<Unit, float>();
            Shots.DebugImpact = (p, c, w, o) => { if (o && o.bot != null) { allHits++; if (c && c.GetComponentInParent<Unit>() is Unit hu && hu.team != o.team) unitHits++; } };
            int kills0 = 0; foreach (var p in b.people) kills0 += p.kills;
            int steps = Mathf.CeilToInt(seconds / step);
            int flips = 0, attackers = 0, onObjective = 0, sameHuman = 0, humanSamples = 0;
            var owners = new int[b.posts.Count]; for (int i = 0; i < owners.Length; i++) owners[i] = b.posts[i].owner;
            float tick0 = b.teams.Length >= 2 ? b.teams[0].tickets + b.teams[1].tickets : 0;
            for (int s = 0; s < steps; s++)
            {
                Step(step);
                for (int i = 0; i < owners.Length; i++) if (b.posts[i].owner != owners[i]) { owners[i] = b.posts[i].owner; flips++; }
                // how many bots are on one person at once
                foreach (var hp in b.people)
                {
                    if (!hp.Human || !hp.unit || !hp.unit.Alive) continue;
                    humanSamples++;
                    foreach (var u in b.units) if (u && u.Alive && u.bot != null && u.team != hp.team && fTarget.GetValue(u.bot) == (object)hp.unit) sameHuman++;
                }
                foreach (var u in b.units)
                {
                    if (!u || !u.Alive || u.bot == null || u.part.boss) continue;
                    if (!u.InVehicle && fSquad != null && fSquad.GetValue(u.bot) is Squad aq && aq.target != null && aq.order != SquadOrder.Defend)
                    { attackers++; if ((u.transform.position - aq.target.Pos).magnitude < aq.target.radius * 1.2f) onObjective++; }
                    if (u.InVehicle) { inVeh++; continue; }
                    if (u.gliding || u.knocked) continue;
                    n++;
                    var tgt = fTarget.GetValue(u.bot) as IDamageable;
                    bool has = tgt != null && tgt.Alive;
                    float sp = new Vector3(u.velocity.x, 0, u.velocity.z).magnitude;
                    if (lastFire.TryGetValue(u, out float lf) && u.lastFire > lf + 0.001f) shots++;
                    lastFire[u] = u.lastFire;
                    if (has)
                    {
                        withTarget++; if (u.input.fire) firing++;
                        float d = (tgt.AimPoint - u.transform.position).magnitude; tdSum += d; tdN++; if (d > 60) far++;
                        if (u.Weapon.def.kind == WKind.Sniper) { aimingSniper++; if (u.input.fire) { sniperFiring++; if (u.input.aim) sniperScoped++; } }
                    }
                    else
                    {
                        if (sp > 0.6f) moving++; else still++;
                        if (Time.time - u.lastHurt < 2f) hurtNoTarget++;
                        // an enemy in plain view within 80 m that this bot is not reacting to?
                        Unit near = null; float bd = 80 * 80;
                        foreach (var e in b.units) { if (!e || !e.Alive || e.team == u.team || e.InVehicle || e.Cloaked) continue; float d2 = (e.transform.position - u.transform.position).sqrMagnitude; if (d2 < bd) { bd = d2; near = e; } }
                        if (near && !Physics.Linecast(u.Eye, near.AimPoint, Layers.SightMask, QueryTriggerInteraction.Ignore)) blind++;
                    }
                    if ((float)fStuck.GetValue(u.bot) > 0.5f) stuck++;
                    var sq = fSquad != null ? fSquad.GetValue(u.bot) as Squad : null;
                    if (sq != null && sq.gathering) gathering++;
                }
            }
            Shots.DebugImpact = null;
            int kills1 = 0; foreach (var p in b.people) kills1 += p.kills;
            float botMin = n * step / 60f;
            float P(int x) => n > 0 ? 100f * x / n : 0;
            var sb = new StringBuilder();
            sb.Append($"{seconds:0} s, {n} bot samples ({botMin:0.0} bot-minutes on foot, {inVeh * step / 60f:0.0} in vehicles)\n");
            sb.Append($"has a target {P(withTarget):0}% (firing {(withTarget > 0 ? 100f * firing / withTarget : 0):0}% of that, avg range {(tdN > 0 ? tdSum / tdN : 0):0} m, {(tdN > 0 ? 100f * far / tdN : 0):0}% beyond 60 m)\n");
            sb.Append($"no target: moving {P(moving):0}%, standing {P(still):0}%; of all samples: enemy in plain view within 80 m but ignored {P(blind):0}%, just been hit with no target {P(hurtNoTarget):0}%\n");
            sb.Append($"stuck {P(stuck):0}%, squad waiting at a staging point {P(gathering):0}%\n");
            sb.Append($"shots seen {shots} ({shots / Mathf.Max(0.01f, botMin):0.0}/bot-min), impacts {allHits}, hits on enemies {unitHits} ({(allHits > 0 ? 100f * unitHits / allHits : 0):0}% of impacts, {unitHits / Mathf.Max(0.01f, botMin):0.0}/bot-min), kills {kills1 - kills0} ({(kills1 - kills0) / Mathf.Max(0.01f, botMin):0.00}/bot-min)\n");
            if (owners.Length > 0) sb.Append($"posts changing hands {flips} ({flips / (seconds / 60f):0.0}/min), attackers inside their target's ring {(attackers > 0 ? 100f * onObjective / attackers : 0):0}% of the time, tickets spent {tick0 - (b.teams[0].tickets + b.teams[1].tickets):0}\n");
            if (humanSamples > 0) sb.Append($"bots targeting a person at once: {sameHuman / (float)humanSamples:0.0}\n");
            if (aimingSniper > 0) sb.Append($"snipers: {aimingSniper} samples with a target, firing {sniperFiring}, scoped while firing {sniperScoped}\n");
            return sb.ToString();
        }

        public static string StartRoyale(int players = 100, bool classes = true, int faction = 0, int cls = 0, bool duos = false)
        {
            Step(0.06f);
            if (Battle.I) Battle.I.Finish(true);
            var cfg = new BattleConfig { land = Maps.Royale, royale = true, royalePlayers = players, royaleClasses = classes, royaleFaction = faction, royaleClass = cls, factions = new[] { faction, (faction + 1) % 4 }, difficulty = 1, tickets = 1, royaleDuos = duos };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Game.I.StartBattle(cfg, r => Game.I.ShowMenu());
            var b = Battle.I;
            return $"built in {sw.ElapsedMilliseconds} ms: houses {b.land.houses.Count}, vaults {b.land.vaults.Count}, loot {b.royale.loot.Count}, chests {b.royale.chests.Count}, keys {b.royale.keysSpawned}, vehicles {b.vehicles.Count}, people {b.people.Count}, teams {b.teams.Length}, nav blocked foot {b.land.nav.blockedFoot}";
        }

        public static string RoyaleReport()
        {
            var b = Battle.I; if (!b || !b.royale) return "no royale";
            var r = b.royale;
            int inVeh = 0, gliding = 0, using_ = 0, armored = 0, withKey = 0, kills = 0; float best = 0, bestSum = 0; int nb = 0;
            var goals = new System.Collections.Generic.Dictionary<string, int>();
            var gf = typeof(BotBrain).GetField("rGoal", BindingFlags.NonPublic | BindingFlags.Instance);
            var tiers = new int[5];
            foreach (var u in b.units)
            {
                if (!u || !u.Alive || u.part.boss) continue;
                if (u.InVehicle) inVeh++; if (u.gliding) gliding++; if (u.Using) using_++; if (u.armor > 0) armored++; if (u.keys > 0) withKey++;
                float v = 0; foreach (var w in u.weapons) if (w != null) { v = Mathf.Max(v, LootTable.Value(w.def)); if (w.def.tier >= 0) tiers[w.def.tier]++; }
                bestSum += v; nb++; best = Mathf.Max(best, v);
                if (u.bot != null) { string g = gf.GetValue(u.bot).ToString(); goals[g] = goals.TryGetValue(g, out int c) ? c + 1 : 1; }
            }
            foreach (var p in b.people) kills += p.kills;
            if (r.Duos)
            {
                int down = 0, tags = 0, carrying = 0, reviving = 0, calling = 0, used = 0;
                foreach (var u in b.units) if (u && u.Alive) { if (u.knocked) down++; if (u.tagOf >= 0) carrying++; if (u.Reviving) reviving++; if (u.CallingIn) calling++; }
                foreach (var it in r.loot) if (it.kind == LootKind.Tag) tags++;
                foreach (var s in r.stations) if (s.used) used++;
                goals["DUOS down"] = down; goals["tags on ground"] = tags; goals["carrying"] = carrying; goals["reviving"] = reviving; goals["calling in"] = calling; goals["stations used"] = used; goals["stations"] = r.stations.Count;
                goals["revives"] = Royale.Revives; goals["respawns"] = Royale.Respawns; goals["knocks"] = Royale.Knocks;
            }
            int opened = 0; foreach (var c in r.chests) if (c.opened) opened++;
            var sb = new StringBuilder();
            sb.Append($"t={b.time:0} phase {r.phase} drop {r.dropT:0} alive {r.AlivePlayers} teams {r.AliveTeams} kills {kills} over {b.over} {b.result?.reason}\n");
            sb.Append($"zone phase {r.zonePhase} r {r.zoneR:0} -> {r.nextR:0} timer {r.zoneTimer:0} shrinking {r.shrinking}\n");
            sb.Append($"loot left {r.loot.Count} chests opened {opened}/{r.chests.Count} keys {r.keysSpawned} held {withKey} vaults open {b.land.vaults.FindAll(v => v.open).Count}\n");
            sb.Append($"boss spawned {r.bossSpawned} at {r.bossPlace} alive {(r.boss != null && r.boss.Alive)} dead {r.bossDead} hp {(r.boss != null && r.boss.unit ? r.boss.unit.hp : 0):0}\n");
            sb.Append($"in vehicles {inVeh} gliding {gliding} healing {using_} armoured {armored} gun avg {(nb > 0 ? bestSum / nb : 0):0} best {best:0} tiers held {string.Join("/", tiers)}\n");
            foreach (var kv in goals) sb.Append($"{kv.Key}={kv.Value} ");
            var me = b.player.unit;
            if (me) sb.Append($"\nplayer alive {me.Alive} at {me.transform.position} gliding {me.gliding} placement {b.player.placement}");
            return sb.ToString();
        }

        public static string MapShot(int map)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (map < 0) StartRoyale(2); else Start(false, map, 0, 1, 2, 200);
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
        /// <summary>Render the game camera from any point (no HUD) to Previews/Maps/&lt;file&gt;.png.</summary>
        public static string CamShot(Vector3 pos, Vector3 look, string file, int w = 1600, int h = 900)
        {
            var cam = Game.Cam;
            var keepP = cam.transform.position; var keepR = cam.transform.rotation;
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
            RenderTexture.active = rt;
            var t = new Texture2D(w, h, TextureFormat.RGB24, false);
            t.ReadPixels(new Rect(0, 0, w, h), 0, 0); t.Apply();
            RenderTexture.active = null; Object.DestroyImmediate(rt);
            cam.transform.SetPositionAndRotation(keepP, keepR);
            System.IO.Directory.CreateDirectory("Previews/Maps");
            string path = $"Previews/Maps/{file}.png";
            System.IO.File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            return path;
        }

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
