using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using BIS.Net;

namespace BIS
{
    /// <summary>
    /// Wi-Fi self-test. Run one copy with -nethost (optionally -space, -against) and another with -netjoin 127.0.0.1
    /// (or -netjoin auto to find it by broadcast). The host starts a battle once the guest is in; the guest spawns,
    /// walks, shoots, takes a vehicle for a drive and gets out. Each writes autotest_net_host.txt / _guest.txt.
    /// </summary>
    public class NetTest : MonoBehaviour
    {
        readonly StringBuilder log = new StringBuilder();
        bool host;
        string joinIp;
        bool space, against, royale;
        string File => Path.Combine(Application.persistentDataPath, host ? "autotest_net_host.txt" : "autotest_net_guest.txt");

        public static void MaybeStart(System.Collections.Generic.List<string> args)
        {
            int hi = args.IndexOf("-nethost"), ji = args.IndexOf("-netjoin");
            if (hi < 0 && ji < 0) return;
            var t = new GameObject("NetTest").AddComponent<NetTest>();
            t.host = hi >= 0;
            t.joinIp = ji >= 0 && ji + 1 < args.Count ? args[ji + 1] : "auto";
            t.space = args.Contains("-space");
            t.against = args.Contains("-against");
            t.royale = args.Contains("-royale");
        }

        void Line(string s) { log.AppendLine($"{Time.realtimeSinceStartup:0.0} {s}"); Debug.Log("[nettest] " + s); System.IO.File.WriteAllText(File, log.ToString()); }
        void Bail() { Line("watchdog: gave up"); Application.Quit(); }

        IEnumerator Start()
        {
            Invoke(nameof(Bail), 240);
            Application.logMessageReceived += (c, st, t) => { if (t == LogType.Error || t == LogType.Exception) Line("ERROR " + c + " " + st.Split('\n')[0]); };
            yield return new WaitForSeconds(1);
            if (royale) { if (host) yield return RoyaleHost(); else yield return RoyaleGuest(); }
            else if (host) yield return Host(); else yield return Guest();
            yield return new WaitForSeconds(2);
            Application.Quit();
        }

        IEnumerator Host()
        {
            Session.together = !against;
            Session.HostLobby(() => "self-test");
            Line($"hosting on {NetLink.LocalIp()}");
            float t0 = Time.realtimeSinceStartup;
            while (Session.peerName == null && Time.realtimeSinceStartup - t0 < 90) { Session.HostLobbyUpdate(); yield return null; }
            if (Session.peerName == null) { Line("nobody joined"); yield break; }
            Line($"{Session.peerName} joined");
            var cfg = new BattleConfig { space = space, land = Maps.Land[0], spaceMap = Maps.Space[0], factions = new[] { 0, 1 }, botsPerTeam = 10, tickets = 200, netHost = true,
                                         remoteTeam = against ? 1 : 0, remoteName = Session.peerName };
            bool ended = false;
            Game.I.StartBattle(cfg, r => { ended = true; Game.I.ShowMenu(); });
            yield return new WaitForSeconds(1);
            var b = Battle.I;
            b.control.selShip = 0; b.control.TrySpawn(false);
            var rp = b.RemoteHuman;
            int guestShots = 0; float lastFire = -1;
            t0 = Time.realtimeSinceStartup;
            float next = 0;
            while (Time.realtimeSinceStartup - t0 < 60 && !ended)
            {
                var gu = rp != null ? rp.unit : null;
                if (gu && gu.lastFire != lastFire) { if (lastFire >= 0) guestShots++; lastFire = gu.lastFire; }
                if (Time.realtimeSinceStartup - t0 > next)
                {
                    next += 5;
                    int alive = 0; foreach (var u in b.units) if (u && u.Alive) alive++;
                    string g = gu ? $"guest {(gu.Alive ? "alive" : "dead")} mode {gu.net} at {gu.transform.position:F1} hp {gu.hp:0} {(gu.InVehicle ? "in " + gu.vehicle.def.name + " seat " + gu.seat + (gu.vehicle.evadeAt > 0 ? $" (rolled at {gu.vehicle.evadeAt:0.0})" : "") : "on foot")}" : "guest has no soldier";
                    Line($"t {Time.realtimeSinceStartup - t0:0} alive {alive} vehicles {b.vehicles.Count} | {g} | guest shots {guestShots} pts {(rp != null ? rp.points : 0)} kills {(rp != null ? rp.kills : 0)} | out {Session.link?.bytesOut / 1024} KB in {Session.link?.bytesIn / 1024} KB");
                }
                yield return null;
            }
            Line("host ends the battle");
            if (!ended) { b.End(0, "self-test over"); yield return new WaitForSeconds(2); b.Finish(false); }
            yield return new WaitForSeconds(2);
            Session.Shutdown();
        }

        // ── battle royale over Wi-Fi: a duo drops in, the guest loots and heals ──
        IEnumerator RoyaleHost()
        {
            Session.HostLobby(() => "self-test royale");
            Line($"hosting a battle royale on {NetLink.LocalIp()}");
            float t0 = Time.realtimeSinceStartup;
            while (Session.peerName == null && Time.realtimeSinceStartup - t0 < 90) { Session.HostLobbyUpdate(); yield return null; }
            if (Session.peerName == null) { Line("nobody joined"); yield break; }
            Line($"{Session.peerName} joined");
            var cfg = new BattleConfig { land = Maps.Royale, royale = true, royalePlayers = 40, royaleClasses = true, royaleClass = 0, royaleFaction = 0, factions = new[] { 0, 1 },
                                         difficulty = 1, tickets = 1, netHost = true, royaleDuo = !against, remoteName = Session.peerName };
            bool ended = false;
            Game.I.StartBattle(cfg, r => { ended = true; Game.I.ShowMenu(); });
            yield return new WaitForSeconds(1);
            var b = Battle.I; var ry = b.royale;
            var rp = b.RemoteHuman;
            Line($"royale built: {b.people.Count} people, loot {ry.loot.Count}, chests {ry.chests.Count}, guest team {rp?.team} (mine {b.player.team})");
            ry.dropPickPos = new Vector3(0, b.land.Top(0, 0), 0); ry.playerPicked = true;
            t0 = Time.realtimeSinceStartup; float next = 0; bool hurtGuest = false;
            while (Time.realtimeSinceStartup - t0 < 75 && !ended)
            {
                if (b.player.unit) b.player.unit.invulnUntil = Time.time + 5;
                var gh = rp != null ? rp.unit : null;
                if (gh && gh.Alive && !gh.gliding && !hurtGuest && Time.realtimeSinceStartup - t0 > 38) { hurtGuest = true; gh.TakeDamage(new DamageInfo { amount = 120, type = DmgType.Environment, team = -1, point = gh.transform.position }); Line($"hurt the guest: hp {gh.hp:0}"); }
                if (Time.realtimeSinceStartup - t0 > next)
                {
                    next += 5;
                    var gu = rp != null ? rp.unit : null;
                    string g = gu ? $"guest {(gu.Alive ? "alive" : "dead")} {gu.net} at {gu.transform.position:F1} gliding {gu.gliding} hp {gu.hp:0} armour {gu.armor:0} medkits {gu.medkits} shields {gu.shields} weapons {gu.weapons[0]?.def.name}/{gu.weapons[1]?.def.name} using {gu.Using}"
                                  : $"guest not down yet (dropped {rp?.dropped}, picked {rp?.dropAt:F0})";
                    Line($"t {Time.realtimeSinceStartup - t0:0} {ry.phase} drop {ry.dropT:0} alive {ry.AlivePlayers} loot {ry.loot.Count} | {g} | out {Session.link?.bytesOut / 1024} KB");
                }
                yield return null;
            }
            Line("host ends the battle");
            if (!ended) { b.End(b.player.team, "self-test over"); yield return new WaitForSeconds(2); b.Finish(false); }
            yield return new WaitForSeconds(2);
            Session.Shutdown();
        }

        IEnumerator RoyaleGuest()
        {
            float t0 = Time.realtimeSinceStartup;
            while (!Session.Join(joinIp == "auto" ? "127.0.0.1" : joinIp)) { if (Time.realtimeSinceStartup - t0 > 60) { Line("could not connect"); yield break; } yield return new WaitForSeconds(1); }
            Line("connected");
            t0 = Time.realtimeSinceStartup;
            while ((Battle.I == null || !Battle.I.cfg.netClient) && Time.realtimeSinceStartup - t0 < 90) { Session.ClientLobbyUpdate(); yield return null; }
            var b = Battle.I;
            if (b == null || b.royale == null) { Line("no royale started"); yield break; }
            var ry = b.royale;
            yield return new WaitForSeconds(2);
            Line($"royale on the guest: {b.people.Count} people, we are '{b.player.name}' team {b.player.team} mode {b.control.mode}; loot {ry.loot.Count} chests {ry.chests.Count} phase {ry.phase} drop {ry.dropT:0}");
            // pick a spot by the city and jump
            ry.dropPickPos = new Vector3(30, b.land.Top(30, 30), 30); ry.playerPicked = true; ry.playerWantsJump = true;
            t0 = Time.realtimeSinceStartup;
            while (!(b.player.unit && b.player.unit.Alive) && Time.realtimeSinceStartup - t0 < 30) yield return null;
            var me = b.player.unit;
            if (!me) { Line("never dropped"); yield break; }
            Line($"dropped: {me.net} at {me.transform.position:F0} gliding {me.gliding} class {me.ClassName}");
            while (me && me.Alive && me.gliding && Time.realtimeSinceStartup - t0 < 50) { PlayerControl.testMove = Vector3.zero; yield return null; }
            Line($"landed at {me.transform.position:F1} after {Time.realtimeSinceStartup - t0:0} s; host sees us at {NetClient.DebugHostSeesMe:F1}");
            // walk to the nearest loot and ask for it, a few times
            int took = 0; float start = Time.realtimeSinceStartup, next = 0;
            var asked = new System.Collections.Generic.HashSet<int>();
            while (Time.realtimeSinceStartup - start < 45 && me && me.Alive && !b.over)
            {
                // the nearest item we can take: step next to it (a test, not a path-finder) and ask the host for it
                LootItem it = null; float bd = float.MaxValue;
                foreach (var x in ry.loot) { if (x.taken || ry.CantTake(me, x) != null || asked.Contains(x.id)) continue; float d = (x.pos - me.transform.position).sqrMagnitude; if (d < bd) { bd = d; it = x; } }
                if (it != null && took < 8)
                {
                    Vector3 to = it.pos - me.transform.position; to.y = 0;
                    if (to.magnitude > 1.6f)
                    {
                        me.cc.enabled = false; me.transform.position = it.pos - to.normalized * 1.2f + Vector3.up * 0.2f; me.cc.enabled = true; Physics.SyncTransforms();
                        yield return new WaitForSeconds(0.4f);
                    }
                    PlayerControl.testMove = Vector3.zero;
                    asked.Add(it.id);
                    NetClient.RequestLoot(0, it.id); took++; Line($"asked for {(it.kind == LootKind.Weapon ? Tiers.Names[it.tier] + " " : "")}{it.Name} (id {it.id})");
                    yield return new WaitForSeconds(0.8f);
                    Line($"  now: taken on our side {it.taken}, medkits {me.medkits} shields {me.shields} keys {me.keys} weapons {me.weapons[0]?.def.name}({me.weapons[0]?.def.tier})/{me.weapons[1]?.def.name}({me.weapons[1]?.def.tier})");
                }
                if (me.medkits > 0 && me.hp < me.maxHp && !me.Using && Time.frameCount % 30 == 0) { NetClient.RequestHeal(); Line($"asked to heal at hp {me.hp:0}"); }
                if (Time.realtimeSinceStartup - start > next)
                {
                    next += 5;
                    Line($"t {Time.realtimeSinceStartup - start:0} hp {me.hp:0} armour {me.armor:0} medkits {me.medkits} shields {me.shields} keys {me.keys} weapons {me.weapons[0]?.def.name}({me.weapons[0]?.def.tier})/{me.weapons[1]?.def.name}({me.weapons[1]?.def.tier}) | loot here {ry.loot.Count} | {ry.phase} zone r {ry.zoneR:0} next {ry.nextR:0} timer {ry.zoneTimer:0} alive {ry.AlivePlayers} | in {Session.link?.bytesIn / 1024} KB");
                }
                yield return null;
            }
            PlayerControl.testMove = Vector3.zero;
            Line($"asked for {took} items");
            t0 = Time.realtimeSinceStartup;
            while (Battle.I != null && !Battle.I.over && Time.realtimeSinceStartup - t0 < 30) yield return null;
            Line(Battle.I != null && Battle.I.over ? $"battle over: {Battle.I.result?.reason}" : "battle did not end");
            if (Battle.I != null) Battle.I.Finish(false);
            yield return new WaitForSeconds(1);
            Session.Shutdown();
        }

        IEnumerator Guest()
        {
            string ip = joinIp;
            if (ip == "auto")
            {
                Session.StartBrowsing();
                float tb = Time.realtimeSinceStartup;
                while (Session.disco.found.Count == 0 && Time.realtimeSinceStartup - tb < 30) { Session.disco.Update(); yield return null; }
                foreach (var k in Session.disco.found.Keys) { ip = k; break; }
                Line(ip == "auto" ? "found no game by broadcast" : $"found a game by broadcast at {ip}");
                if (ip == "auto") yield break;
            }
            float t0 = Time.realtimeSinceStartup;
            while (!Session.Join(ip)) { if (Time.realtimeSinceStartup - t0 > 60) { Line("could not connect"); yield break; } yield return new WaitForSeconds(1); }
            Line($"connected to {ip}");
            t0 = Time.realtimeSinceStartup;
            while ((Battle.I == null || !Battle.I.cfg.netClient) && Time.realtimeSinceStartup - t0 < 90) { Session.ClientLobbyUpdate(); yield return null; }
            var b = Battle.I;
            if (b == null) { Line("the host never started"); yield break; }
            Line($"battle started: {(b.space ? b.cfg.spaceMap.name : b.cfg.land.name)}, {b.people.Count} people, we are '{b.player.name}' on team {b.player.team}");
            yield return new WaitForSeconds(1.5f);
            Line($"replicas: {b.units.Count} soldiers, {b.vehicles.Count} vehicles");
            b.control.selShip = 0; b.control.spaceOnFoot = false;
            t0 = Time.realtimeSinceStartup;
            while (!(b.player.unit && b.player.unit.Alive) && Time.realtimeSinceStartup - t0 < 20) { if (b.control.mode == PlayerControl.Mode.Spawn) b.control.TrySpawn(false); yield return new WaitForSeconds(1); }
            var me = b.player.unit;
            if (!me) { Line("never spawned: " + b.control.notice); yield break; }
            Line($"spawned as {me.ClassName} mode {me.net} at {me.transform.position:F1}{(me.InVehicle ? " in " + me.vehicle.def.name : "")}");
            // walk and shoot
            float start = Time.realtimeSinceStartup; float next = 0; bool triedVehicle = false, droveOff = false; float driveT = 0; Vehicle drove = null; Vector3 driveFrom = Vector3.zero; float askAt = -1, askedAt = -1; bool respawnLogged = false; int deaths = 0; bool evadeLogged = false;
            while (Time.realtimeSinceStartup - start < 50 && b == Battle.I && !b.over)
            {
                float t = Time.realtimeSinceStartup - start;
                me = b.player.unit;
                PlayerControl.testMove = new Vector3(Mathf.Sin(t * 0.5f), 0, Mathf.Cos(t * 0.5f));
                PlayerControl.testFire = t % 4 < 1.5f;
                if (me && me.Alive && !me.InVehicle && !triedVehicle && t > 15)
                {
                    var v = b.control.NearbyVehicle();
                    if (!v)
                    {
                        // go to the nearest free vehicle
                        Vehicle best = null; float bd = float.MaxValue;
                        foreach (var x in b.vehicles) if (x && x.CanEnter(me) && !x.def.Flies) { float d = (x.transform.position - me.transform.position).sqrMagnitude; if (d < bd) { bd = d; best = x; } }
                        if (best && bd < 120 * 120 && askAt < 0) { me.transform.position = best.transform.position + best.transform.right * (best.radius + 1.5f); Physics.SyncTransforms(); askAt = t + 1; }
                        v = null;
                    }
                    if (askAt >= 0 && t < askAt) { PlayerControl.testMove = Vector3.zero; v = null; }
                    if (v) { NetClient.RequestEnter(v); Line($"asked to get into the {v.def.name}"); triedVehicle = true; askedAt = t; }
                    else if (t > 25) { triedVehicle = true; Line("no vehicle near to try"); }
                }
                if (askedAt > 0 && t - askedAt > 2) { askedAt = -2;  Line($"2 s after asking: {(me && me.InVehicle ? "in the " + me.vehicle.def.name + " seat " + me.seat : "still on foot")}; notice '{b.control.notice}'"); }
                PlayerControl.testThrottle = me && me.InVehicle && me.seat == 0 && !droveOff && me.vehicle.Flies ? 1 : 0;
                if (me && me.InVehicle && me.seat == 0 && !droveOff) PlayerControl.testMove = me.vehicle.transform.forward;
                if (me && me.Alive && me.InVehicle && me.seat == 0 && !droveOff)
                {
                    if (drove == null) { drove = me.vehicle; driveFrom = drove.transform.position; driveT = t; Line($"in the {drove.def.name}: vehicle mode {drove.net}"); }
                    if (drove.CanEvadeKind && t - driveT > 3 && drove.evadeAt < 0) { PlayerControl.testEvade = true; }
                    if (drove.CanEvadeKind && drove.evadeAt > 0 && t - driveT > 3.5f && !evadeLogged) { evadeLogged = true; Line($"rolled our craft at {drove.evadeAt:0.0} (evading {drove.Evading})"); }
                    if (t - driveT > 6)
                    {
                        Line($"drove {(drove.transform.position - driveFrom).magnitude:0.0} m; host sees it {(drove.netPos - drove.transform.position).magnitude:0.0} m from us; asking to get out");
                        droveOff = true;
                        NetClient.RequestExit();
                    }
                }
                if (t > next)
                {
                    next += 5;
                    me = b.player.unit;
                    int alive = 0; foreach (var u in b.units) if (u && u.Alive) alive++;
                    string mine = me ? $"me {(me.Alive ? "alive" : "dead")} {me.net} hp {me.hp:0} at {me.transform.position:F1} {(me.InVehicle ? "in " + me.vehicle.def.name + " (" + me.vehicle.net + ")" : "on foot")}" : "no soldier";
                    Line($"t {t:0} soldiers {alive} vehicles {b.vehicles.Count} tickets {b.teams[0].tickets:0}/{b.teams[1].tickets:0} | {mine} | host-side pos {NetClient.DebugHostSeesMe:F1} | pts {b.player.points} kills {b.player.kills} | in {Session.link?.bytesIn / 1024} KB out {Session.link?.bytesOut / 1024} KB | mode {b.control.mode}");
                }
                if ((!me || !me.Alive) && b.control.mode == PlayerControl.Mode.Spawn && Time.frameCount % 30 == 0) { b.control.TrySpawn(false); if (!respawnLogged && b.player.unit && b.player.unit.Alive) { respawnLogged = true; Line($"respawned as {b.player.unit.ClassName}"); } }
                if (!respawnLogged && deaths == 0 && me && !me.Alive) deaths = 1;
                if (deaths == 1 && me && me.Alive && !respawnLogged) { respawnLogged = true; Line($"respawned as {me.ClassName} at {me.transform.position:F1}"); }
                yield return null;
            }
            PlayerControl.testMove = Vector3.zero; PlayerControl.testFire = false; PlayerControl.testThrottle = 0;
            t0 = Time.realtimeSinceStartup;
            while (Battle.I != null && !Battle.I.over && Time.realtimeSinceStartup - t0 < 20) yield return null;
            Line(Battle.I != null && Battle.I.over ? $"battle over: {Battle.I.result?.reason}" : "battle did not end");
            if (Battle.I != null) Battle.I.Finish(false);
            yield return new WaitForSeconds(1);
            Session.Shutdown();
        }
    }
}
