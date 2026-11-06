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
        bool space, against;
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
        }

        void Line(string s) { log.AppendLine($"{Time.realtimeSinceStartup:0.0} {s}"); Debug.Log("[nettest] " + s); System.IO.File.WriteAllText(File, log.ToString()); }
        void Bail() { Line("watchdog: gave up"); Application.Quit(); }

        IEnumerator Start()
        {
            Invoke(nameof(Bail), 240);
            Application.logMessageReceived += (c, st, t) => { if (t == LogType.Error || t == LogType.Exception) Line("ERROR " + c + " " + st.Split('\n')[0]); };
            yield return new WaitForSeconds(1);
            if (host) yield return Host(); else yield return Guest();
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
                    string g = gu ? $"guest {(gu.Alive ? "alive" : "dead")} mode {gu.net} at {gu.transform.position:F1} hp {gu.hp:0} {(gu.InVehicle ? "in " + gu.vehicle.def.name + " seat " + gu.seat : "on foot")}" : "guest has no soldier";
                    Line($"t {Time.realtimeSinceStartup - t0:0} alive {alive} vehicles {b.vehicles.Count} | {g} | guest shots {guestShots} pts {(rp != null ? rp.points : 0)} kills {(rp != null ? rp.kills : 0)} | out {Session.link?.bytesOut / 1024} KB in {Session.link?.bytesIn / 1024} KB");
                }
                yield return null;
            }
            Line("host ends the battle");
            if (!ended) { b.End(0, "self-test over"); yield return new WaitForSeconds(2); b.Finish(false); }
            yield return new WaitForSeconds(2);
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
            float start = Time.realtimeSinceStartup; float next = 0; bool triedVehicle = false, droveOff = false; float driveT = 0; Vehicle drove = null; Vector3 driveFrom = Vector3.zero; float askAt = -1, askedAt = -1; bool respawnLogged = false; int deaths = 0;
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
