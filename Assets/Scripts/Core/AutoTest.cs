using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace BIS
{
    /// <summary>
    /// Launch the built game with -autotest: it plays every land and space map for a few seconds with the
    /// player spawned, runs a short auto-resolved Conquest campaign, writes autotest.txt next to the log and quits.
    /// </summary>
    public class AutoTest : MonoBehaviour
    {
        readonly StringBuilder log = new StringBuilder();

        public static void MaybeStart()
        {
            foreach (var a in System.Environment.GetCommandLineArgs())
                if (a == "-autotest") { new GameObject("AutoTest").AddComponent<AutoTest>(); return; }
        }

        void Line(string s) { log.AppendLine(s); Debug.Log("[autotest] " + s); }

        void Bail() { Line("watchdog: gave up after 300 s"); Write(); Application.Quit(); }
        void Write() { File.WriteAllText(Path.Combine(Application.persistentDataPath, "autotest.txt"), log.ToString()); }

        IEnumerator Start()
        {
            Invoke(nameof(Bail), 300);
            Application.logMessageReceived += (c, st, t) => { if (t == LogType.Error || t == LogType.Exception) Line("ERROR " + c); };
            Line($"start {Screen.width}x{Screen.height} {SystemInfo.graphicsDeviceName} shaders ok: {Mats.LitShader != null && Mats.LitShader.isSupported} {Mats.FxShader != null && Mats.FxShader.isSupported} {Mats.SkyShader != null && Mats.SkyShader.isSupported}");
            yield return new WaitForSeconds(1);
            for (int m = 0; m < Maps.Land.Length + Maps.Space.Length; m++)
            {
                bool space = m >= Maps.Land.Length;
                int f0 = m % 4, f1 = (m + 1 + m / 4) % 4; if (f1 == f0) f1 = (f0 + 1) % 4;
                var cfg = new BattleConfig { space = space, land = Maps.Land[m % Maps.Land.Length], spaceMap = Maps.Space[space ? m - Maps.Land.Length : 0], factions = new[] { f0, f1 }, botsPerTeam = 16, tickets = 200 };
                Game.I.StartBattle(cfg, r => Game.I.ShowMenu());
                yield return new WaitForSeconds(2);
                var b = Battle.I;
                b.control.selShip = 0; b.control.TrySpawn(false);
                float t0 = Time.realtimeSinceStartup; int frames = 0;
                while (Time.realtimeSinceStartup - t0 < 12) { frames++; yield return null; }
                int alive = 0; foreach (var u in b.units) if (u && u.Alive) alive++;
                int kills = 0; foreach (var p in b.people) kills += p.kills;
                Line($"{(space ? cfg.spaceMap.name : cfg.land.name)} {Factions.Get(f0).shortName} v {Factions.Get(f1).shortName}: {frames / 12f:0} fps, alive {alive}, vehicles {b.vehicles.Count}, kills {kills}, tickets {b.teams[0].tickets:0}/{b.teams[1].tickets:0}, player {(b.player.unit && b.player.unit.Alive ? "alive" : "dead")}, music {(Sfx.Music && Sfx.Music.clip ? Sfx.Music.clip.name : "none")} {(Sfx.Music && Sfx.Music.isPlaying ? "playing" : "silent")}, sfx files {Sfx.FileCount}");
                b.Finish(true);
                yield return new WaitForSeconds(1);
            }
            // conquest
            var c = Campaign.New(0, new[] { 1, 2 }, 1, 100, 8);
            Game.I.menu.OpenCampaign(c);
            yield return new WaitForSeconds(2);
            Line($"conquest opened: {c.Planets(0)} worlds, {c.FleetCount(0)} fleets");
            Game.I.menu.EndTurn();
            yield return new WaitForSeconds(3);
            Line($"conquest after one turn: turn {c.turn}, log '{(c.log.Count > 0 ? c.log[c.log.Count - 1] : "")}'");
            Write();
            Campaign.DeleteSave();
            Application.Quit();
        }
    }
}
