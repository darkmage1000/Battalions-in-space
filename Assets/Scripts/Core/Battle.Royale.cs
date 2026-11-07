using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    public partial class Battle
    {
        /// <summary>
        /// A battle royale: the big multi-biome map, then everyone (you, a Wi-Fi guest, bots up to the player count) on a team
        /// of their own - a duo shares one - plus the boss, who waits off the map until its time comes.
        /// </summary>
        void BuildRoyale()
        {
            var lgo = new GameObject("LandWorld"); lgo.transform.SetParent(root, false);
            land = lgo.AddComponent<LandWorld>();
            land.Build(cfg.land, this);

            int nextTeam;
            if (cfg.netClient)
            {
                nextTeam = 0;
                for (int i = 0; i < cfg.rosterNames.Length; i++)
                {
                    var p = new Participant { name = cfg.rosterNames[i], team = cfg.rosterTeams[i], aimSkill = 1, respawn = 999 };
                    if (cfg.rosterFactions != null && i < cfg.rosterFactions.Length) p.faction = cfg.rosterFactions[i];
                    if (cfg.rosterClasses != null && i < cfg.rosterClasses.Length) p.royaleClass = cfg.rosterClasses[i];
                    if (cfg.rosterBoss != null && i < cfg.rosterBoss.Length) p.boss = cfg.rosterBoss[i];
                    if (i == cfg.localIndex) { p.isPlayer = true; player = p; }
                    else p.remote = cfg.rosterHuman[i];
                    people.Add(p);
                    nextTeam = Mathf.Max(nextTeam, p.team + 1);
                }
            }
            else
            {
                player = new Participant { name = Profiles.Name, team = 0, isPlayer = true, aimSkill = 1, faction = cfg.royaleFaction, royaleClass = cfg.royaleClass };
                people.Add(player);
                nextTeam = 1;
                if (cfg.netHost)
                {
                    var g = new Participant { name = cfg.remoteName, team = cfg.royaleDuo ? 0 : nextTeam++, remote = true, aimSkill = 1, respawn = 999, faction = Random.Range(0, 4), royaleClass = Random.Range(0, 5) };
                    people.Add(g);
                }
                // bots: names from all four races, numbered when a name comes round again
                var used = new Dictionary<string, int>();
                int bots = Mathf.Max(1, cfg.royalePlayers - people.Count);
                for (int i = 0; i < bots; i++)
                {
                    int fi = Random.Range(0, 4);
                    var f = Factions.Get(fi);
                    string nm = f.names[Random.Range(0, f.names.Length)];
                    if (used.TryGetValue(nm, out int k)) { used[nm] = k + 1; nm += " " + (k + 1); } else used[nm] = 1;
                    float skill = Mathf.Clamp01(0.2f + cfg.difficulty * 0.28f + Random.Range(-0.2f, 0.2f));
                    people.Add(new Participant { name = nm, team = nextTeam++, aimSkill = skill, faction = fi, royaleClass = Random.Range(0, 5), respawn = 999 });
                }
                people.Add(new Participant { name = "Dreadnought Warlord", team = nextTeam, boss = true, aimSkill = 0.75f, faction = 2, respawn = 999 });
            }

            // one team per player (a duo shares one) and one for the boss
            int teamCount = 0; foreach (var p in people) teamCount = Mathf.Max(teamCount, p.team + 1);
            teams = new TeamState[teamCount];
            for (int t = 0; t < teamCount; t++)
            {
                int fi = 0;
                foreach (var p in people) if (p.team == t && p.faction >= 0) { fi = p.faction; break; }
                var f = Factions.Get(fi);
                teams[t] = new TeamState { index = t, faction = f, tickets = 1, startTickets = 1, color = TeamTint(f, t) };
            }
            foreach (var p in people) p.heroCost = 99999;

            royale = gameObject.AddComponent<Royale>();
            royale.Init(this, cfg.netClient);

            var pc = gameObject.AddComponent<PlayerControl>();
            control = pc;
            pc.Init(this);
            gameObject.AddComponent<HUD>().battle = this;
            Sfx.PlayMusic(true);
            Ctl.LockCursor(false);
            if (cfg.netHost) Net.NetHost.Begin(this);
        }
    }
}
