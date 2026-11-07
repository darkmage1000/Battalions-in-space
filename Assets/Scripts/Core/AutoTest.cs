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
        bool royaleOnly;
        int royalePlayers = 100;

        public static void MaybeStart()
        {
            var args = new System.Collections.Generic.List<string>(System.Environment.GetCommandLineArgs());
            // on a phone: adb shell am start -n com.battalions.space/com.unity3d.player.UnityPlayerGameActivity -e args "-autotest -quality 1"
            string extra = AndroidExtra("args");
            if (!string.IsNullOrEmpty(extra)) args.AddRange(extra.Split(' '));
            int qi = args.IndexOf("-quality");
            if (qi >= 0 && qi + 1 < args.Count && int.TryParse(args[qi + 1], out int q)) { Settings.Quality = Mathf.Clamp(q, 0, 2); Settings.Apply(); }
            if (args.Contains("-autotest"))
            {
                var at = new GameObject("AutoTest").AddComponent<AutoTest>();
                at.royaleOnly = args.Contains("-royale");
                int pi = args.IndexOf("-players");
                if (pi >= 0 && pi + 1 < args.Count && int.TryParse(args[pi + 1], out int np)) at.royalePlayers = np;
            }
            else NetTest.MaybeStart(args);
        }

        static string AndroidExtra(string key)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intent = act.Call<AndroidJavaObject>("getIntent"))
                    return intent.Call<string>("getStringExtra", key);
            }
            catch { return null; }
#else
            return null;
#endif
        }

        void Line(string s) { log.AppendLine(s); Debug.Log("[autotest] " + s); }

        /// <summary>A battle royale: build time, then frame rate in the dropship, gliding down and on the ground.</summary>
        IEnumerator RoyaleRun()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var cfg = new BattleConfig { land = Maps.Royale, royale = true, royalePlayers = royalePlayers, royaleClasses = true, factions = new[] { 0, 1 }, difficulty = 1, tickets = 1 };
            Game.I.StartBattle(cfg, r => Game.I.ShowMenu());
            long ms = sw.ElapsedMilliseconds;
            var b = Battle.I; var ry = b.royale;
            Line($"Battle royale {royalePlayers} players: built in {ms} ms, buildings {b.land.houses.Count}, loot {ry.loot.Count}, chests {ry.chests.Count}, vehicles {b.vehicles.Count}");
            ry.dropPickPos = b.land.RegionCentre(4) + new Vector3(40, 0, 40); ry.playerPicked = true;
            float t0 = Time.realtimeSinceStartup; int frames = 0;
            while (Time.realtimeSinceStartup - t0 < 6) { frames++; yield return null; }
            Line($"  dropship screen: {frames / 6f:0} fps");
            ry.playerWantsJump = true;
            t0 = Time.realtimeSinceStartup; frames = 0;
            while (Time.realtimeSinceStartup - t0 < 10) { frames++; if (b.player.unit) b.player.unit.invulnUntil = Time.time + 5; yield return null; }
            Line($"  gliding down: {frames / 10f:0} fps, {ry.AlivePlayers} alive");
            for (int s = 0; s < 3; s++)
            {
                t0 = Time.realtimeSinceStartup; frames = 0; float worst = 0;
                while (Time.realtimeSinceStartup - t0 < 15) { frames++; worst = Mathf.Max(worst, Time.unscaledDeltaTime); if (b.player.unit) b.player.unit.invulnUntil = Time.time + 5; yield return null; }
                Line($"  on the ground {s * 15}-{s * 15 + 15} s: {frames / 15f:0} fps (worst frame {worst * 1000:0} ms), {ry.AlivePlayers} alive, phase {ry.phase}, loot {ry.loot.Count}");
            }
            b.Finish(true);
            yield return new WaitForSeconds(1);
        }

        void Bail() { Line("watchdog: gave up after 300 s"); Write(); Application.Quit(); }
        void Write() { File.WriteAllText(Path.Combine(Application.persistentDataPath, "autotest.txt"), log.ToString()); }

        IEnumerator Start()
        {
            Invoke(nameof(Bail), 300);
            Application.logMessageReceived += (c, st, t) => { if (t == LogType.Error || t == LogType.Exception) Line("ERROR " + c); };
            Line($"start quality {Settings.Quality} animated soldiers {SoldierRig.UseMannequin} {Screen.width}x{Screen.height} {SystemInfo.graphicsDeviceName} shaders ok: {Mats.LitShader != null && Mats.LitShader.isSupported} {Mats.FxShader != null && Mats.FxShader.isSupported} {Mats.SkyShader != null && Mats.SkyShader.isSupported}");
            yield return new WaitForSeconds(1);
            yield return RoyaleRun();
            if (royaleOnly) { Write(); Application.Quit(); yield break; }
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
