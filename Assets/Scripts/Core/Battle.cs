using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    public enum Bonus { None, Reinforcements, FieldMedics, EnergyCells, ArmoredPlating, Saboteurs, VeteranOfficers, Garrison, Recon }

    public static class Bonuses
    {
        public static readonly string[] Names = { "None", "Reinforcements", "Field Medics", "Energy Cells", "Armoured Plating", "Saboteurs", "Veteran Officers", "Garrison", "Recon Drones" };
        public static readonly string[] Info =
        {
            "No bonus.",
            "+25% reinforcement tickets.",
            "Your infantry heal slowly when out of combat.",
            "+15% weapon damage for your side.",
            "+30% health for your vehicles and starfighters.",
            "Enemy vehicles start damaged and respawn more slowly.",
            "Heroes unlock with half the points.",
            "Start the battle holding an extra command post (land).",
            "All enemies show on your radar.",
        };
        public static readonly int[] Cost = { 0, 600, 500, 700, 600, 650, 800, 550, 400 };
    }

    public class BattleConfig
    {
        public bool space;
        public LandMapDef land;
        public SpaceMapDef spaceMap;
        public int[] factions = { 0, 1 };
        public int playerTeam;
        public int tickets = 200;
        public int botsPerTeam = 16;
        public int difficulty = 1;
        public Bonus[] bonus = { Bonus.None, Bonus.None };
        public bool[] lockedClasses;          // conquest: player's side cannot use these roles yet
        public string title;
        public bool conquest;
        // Wi-Fi play
        public bool netHost, netClient;
        public int remoteTeam;                 // host: the guest's side
        public string remoteName;
        public string[] rosterNames;           // guest: everyone in the host's order
        public int[] rosterTeams;
        public bool[] rosterHuman;
        public int localIndex;                 // guest: which of them is us
        // battle royale
        public bool royale;
        public int royalePlayers = 100;
        public bool royaleClasses;             // on: everyone picks a class (its ability and equipment); weapons come from loot either way
        public int royaleClass;                // the player's class when classes are on
        public int royaleFaction;              // the player's race
        public bool royaleDuo;                 // Wi-Fi: the guest drops on the host's team
        public int[] rosterFactions;           // guest: everyone's race (royale)
        public int[] rosterClasses;            // guest: everyone's class (royale)
        public bool[] rosterBoss;              // guest: which of them is the boss (royale)
    }

    public class BattleResult
    {
        public int winner = -1;
        public string reason;
        public int playerPoints, playerKills, playerDeaths, playerCaptures;
        public List<string> awards = new List<string>();
        public string mvp;
        public bool quit;
        public int[] ticketsLeft = new int[2];
    }

    public class TeamState
    {
        public int index;
        public FactionDef faction;
        public float tickets;
        public int startTickets;
        public float bleedTimer;
        public float noPostTimer;
        public int posts;
        public Color color;
    }

    /// <summary>Runs one battle (land or space): world, teams, people, spawning, scoring and the win rules.</summary>
    public partial class Battle : MonoBehaviour
    {
        public static Battle I;
        public BattleConfig cfg;
        public TeamState[] teams = new TeamState[2];       // two sides; in a battle royale one per player (or per duo) plus the boss
        public readonly List<Participant> people = new List<Participant>();
        public Participant player;
        public readonly List<Unit> units = new List<Unit>();
        public readonly List<Vehicle> vehicles = new List<Vehicle>();
        public readonly List<CommandPost> posts = new List<CommandPost>();
        public LandWorld land;
        public SpaceWorld space;
        public PlayerControl control;
        public Commander commander;
        public Royale royale;                 // the battle royale rules (null in ordinary battles)
        public bool over;
        public float overTime;
        public BattleResult result;
        public float time;
        public System.Action<BattleResult> onEnd;
        Transform root;
        float medicTick, heroThink;
        public float shipAlertT;

        public static Battle Create(BattleConfig cfg, System.Action<BattleResult> onEnd)
        {
            var go = new GameObject("Battle");
            var b = go.AddComponent<Battle>();
            b.cfg = cfg; b.onEnd = onEnd;
            b.Build();
            return b;
        }

        public FactionDef Faction(int t) => teams[t].faction;
        public Color TeamColor(int t) => teams[t].color;
        public Bonus BonusOf(int t) => cfg.bonus != null && t >= 0 && t < cfg.bonus.Length ? cfg.bonus[t] : Bonus.None;
        public float DamageMul(int t) => t >= 0 && t < 2 && BonusOf(t) == Bonus.EnergyCells ? 1.15f : 1f;
        public float HealthMul(int t) => 1f;
        public float VehicleHpMul(int t) => (t >= 0 && BonusOf(t) == Bonus.ArmoredPlating ? 1.3f : 1f) * (t >= 0 && BonusOf(1 - t) == Bonus.Saboteurs ? 0.7f : 1f);
        public float VehicleRespawnMul(int t) => t >= 0 && BonusOf(1 - t) == Bonus.Saboteurs ? 1.5f : 1f;
        public float CaptureMul(int t) => 1f;
        public bool Recon(int t) => BonusOf(t) == Bonus.Recon;

        void Build()
        {
            I = this;
            root = transform;
            gameObject.AddComponent<Shots>();
            Shots.ResetStats();
            if (cfg.royale) { BuildRoyale(); return; }
            for (int t = 0; t < 2; t++)
            {
                var f = Factions.Get(cfg.factions[t]);
                int tk = cfg.tickets;
                if (BonusOf(t) == Bonus.Reinforcements) tk = Mathf.RoundToInt(tk * 1.25f);
                teams[t] = new TeamState { index = t, faction = f, tickets = tk, startTickets = tk, color = TeamTint(f, t) };
            }
            if (cfg.space)
            {
                var go = new GameObject("SpaceWorld"); go.transform.SetParent(root, false);
                space = go.AddComponent<SpaceWorld>();
                space.Build(cfg.spaceMap, this);
            }
            else
            {
                var go = new GameObject("LandWorld"); go.transform.SetParent(root, false);
                land = go.AddComponent<LandWorld>();
                land.Build(cfg.land, this);
                for (int t = 0; t < 2; t++)
                    if (BonusOf(t) == Bonus.Garrison)
                    {
                        CommandPost best = null; float bd = float.MaxValue;
                        Vector3 home = HomePost(t) ? HomePost(t).Pos : Vector3.zero;
                        foreach (var p in posts) if (p.owner < 0) { float d = (p.Pos - home).sqrMagnitude; if (d < bd) { bd = d; best = p; } }
                        if (best) best.ForceOwner(t);
                    }
            }

            // people
            if (cfg.netClient)
            {
                // a Wi-Fi guest: the same people in the same order as the host (so squads come out the same); only the names matter here
                for (int i = 0; i < cfg.rosterNames.Length; i++)
                {
                    var p = new Participant { name = cfg.rosterNames[i], team = cfg.rosterTeams[i], aimSkill = 1, respawn = 999 };
                    if (i == cfg.localIndex) { p.isPlayer = true; player = p; }
                    else p.remote = cfg.rosterHuman[i];
                    people.Add(p);
                }
            }
            else
            {
                player = new Participant { name = Profiles.Name, team = cfg.playerTeam, isPlayer = true, aimSkill = 1 };
                people.Add(player);
                if (cfg.netHost) people.Add(new Participant { name = cfg.remoteName, team = cfg.remoteTeam, remote = true, aimSkill = 1, respawn = 999 });
                for (int t = 0; t < 2; t++)
                {
                    var f = Faction(t);
                    int humans = 0; foreach (var hp in people) if (hp.team == t) humans++;
                    int n = cfg.botsPerTeam - humans;
                    for (int i = 0; i < n; i++)
                    {
                        float skill = Mathf.Clamp01(0.25f + cfg.difficulty * 0.3f + Random.Range(-0.15f, 0.15f));
                        var p = new Participant { name = f.names[i % f.names.Length], team = t, aimSkill = skill, respawn = Random.Range(0.2f, 3.5f) };
                        p.classChoice = PickBotClass(t);
                        people.Add(p);
                    }
                }
            }
            foreach (var p in people) p.heroCost = BonusOf(p.team) == Bonus.VeteranOfficers ? 18 : p.Human ? 36 : 52;
            if (land) commander = new Commander(this);

            var pc = gameObject.AddComponent<PlayerControl>();
            control = pc;
            pc.Init(this);
            gameObject.AddComponent<HUD>().battle = this;
            Sfx.PlayMusic(true);
            Ctl.LockCursor(false);
            if (cfg.netHost) Net.NetHost.Begin(this);
        }

        /// <summary>The other human in a Wi-Fi game (null otherwise).</summary>
        public Participant RemoteHuman { get { foreach (var p in people) if (p.remote) return p; return null; } }

        int nextNetId;
        public Vehicle VehicleById(int id)
        {
            foreach (var v in vehicles) if (v && v.netId == id) return v;
            return null;
        }

        /// <summary>Swap a living soldier for a hero on the spot (the H key / HERO button).</summary>
        public Unit MakeHeroNow(Participant p, int prefer)
        {
            var me = p.unit;
            if (me == null || !me.Alive || !p.heroReady || me.IsHero) return null;
            if (me.InVehicle) me.vehicle.Leave(me, true);
            Vector3 pos = me.transform.position; float yaw = me.yaw;
            var hero = FreeHero(p.team, prefer);
            units.Remove(me);
            Destroy(me.gameObject);
            p.heroReady = false; p.pointsAtHero = p.points; p.everHero = true;
            var u = SpawnUnit(p, null, hero, pos + Vector3.up * 0.2f, yaw);
            Fx.Burst(pos + Vector3.up, hero.glow, 40, 6, 0.5f, 0.8f);
            return u;
        }

        static Color TeamTint(FactionDef f, int t)
        {
            Color c = f.bolt;
            return Color.Lerp(c, Color.white, 0.15f);
        }

        public CommandPost HomePost(int t)
        {
            CommandPost best = null;
            foreach (var p in posts)
                if (cfg.land.posts[p.index].owner == t && cfg.land.posts[p.index].IsHome) best = p;
            return best;
        }

        public int PickBotClass(int t)
        {
            float r = Random.value;
            int c = r < 0.38f ? 0 : r < 0.6f ? 1 : r < 0.69f ? 2 : r < 0.84f ? 3 : 4;   // marksmen are 9%: they hang back and outlive everyone
            if (t == cfg.playerTeam && cfg.lockedClasses != null && cfg.lockedClasses[c]) c = 0;
            return c;
        }

        public void Track(GameObject go) { if (go && root) go.transform.SetParent(root, true); }

        public float GroundY(Vector3 p)
        {
            if (land) return land.Top(p.x, p.z);
            if (space) return space.FloorY(p);
            return 0;
        }

        // ── spawning ─────────────────────────────────────────────────────
        public Unit SpawnUnit(Participant p, ClassDef c, HeroDef h, Vector3 pos, float yaw)
        {
            var go = new GameObject(p.name);
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            var u = go.AddComponent<Unit>();
            u.Init(this, p, Faction(p.team), c, h);
            if (!p.isPlayer && !p.remote && !cfg.netClient) { u.bot = new BotBrain(u); }
            if (p.remote && cfg.netHost) { u.net = Unit.NetMode.HostRemote; u.netPos = pos; }
            p.unit = u;
            p.lifeKills = 0;
            units.Add(u);
            Net.NetHost.OnUnitSpawned(u);
            return u;
        }

        public Vehicle SpawnVehicle(VehicleDef d, int team, Vector3 pos, Quaternion rot) => SpawnVehicle(d, Faction(team), team, pos, rot);

        public Vehicle SpawnVehicle(VehicleDef d, FactionDef f, int team, Vector3 pos, Quaternion rot)
        {
            var v = Vehicle.Spawn(this, d, f, team, pos, rot);
            v.transform.SetParent(root, true);
            if (BonusOf(1 - team) == Bonus.Saboteurs) v.hp *= 0.75f;
            vehicles.Add(v);
            v.netId = ++nextNetId;
            Net.NetHost.OnVehicleSpawned(v);
            return v;
        }

        public bool CanSpawn(int team)
        {
            if (space) return !space.ships[team].destroyed;
            foreach (var p in posts) if (p.owner == team) return true;
            return false;
        }

        // -- spawn timers -------------------------------------------------------
        public const float RespawnPost = 10f;      // after dying, before you can spawn at a post (or your ship in space)
        public const float PressureExtra = 5f;     // more if enemies are within PressureRadius of that post
        public const float PressureRadius = 50f;
        public const float RespawnSquad = 16f;     // before you can spawn on a squadmate
        public const float MateCooldown = 30f;     // a squadmate can be spawned on once per this long

        public bool Pressured(CommandPost cp, int team)
        {
            float r2 = PressureRadius * PressureRadius;
            foreach (var u in units) if (u && u.Alive && u.team != team && (u.transform.position - cp.Pos).sqrMagnitude < r2) return true;
            return false;
        }

        /// <summary>Seconds this participant must still wait before spawning at this post (null = their ship / any post).</summary>
        public float WaitFor(Participant p, CommandPost cp)
        {
            float need = RespawnPost + (cp != null && Pressured(cp, p.team) ? PressureExtra : 0);
            return Mathf.Max(0, need - (Time.time - p.diedAt));
        }

        /// <summary>Seconds before this participant may spawn on that squadmate.</summary>
        public float WaitForMate(Participant p, Participant mate) => Mathf.Max(0, Mathf.Max(RespawnSquad - (Time.time - p.diedAt), mate.mateUsedUntil - Time.time));

        // -- squad spawning ---------------------------------------------------
        public int mateSpawns;                     // stats for tests
        public const float MateSafeRadius = 30f;   // no enemy this close
        public const float MateHurtSafe = 6f;      // and not hurt for this long

        /// <summary>Why this squadmate can't be spawned on right now, or null if they can.</summary>
        public string MateBlocked(Unit m)
        {
            if (!land) return "only on planets";
            if (!m || !m.Alive) return "down";
            if (m.InVehicle) return "in a vehicle";
            if (!m.grounded) return "in the air";
            if (Time.time - m.lastHurt < MateHurtSafe) return "under fire";
            Vector3 mp = m.transform.position;
            float r2 = MateSafeRadius * MateSafeRadius;
            foreach (var u in units)
                if (u && u.Alive && u.team != m.team && (u.transform.position - mp).sqrMagnitude < r2 && !u.Cloaked) return "enemies near";
            foreach (var v in vehicles)
                if (v && v.Alive && v.Team != m.team && !v.IsEmpty && (v.transform.position - mp).sqrMagnitude < r2 * 2.5f) return "enemy vehicle near";
            foreach (var cp in posts)
                if (cp.owner >= 0 && cp.owner != m.team && (cp.Pos - mp).sqrMagnitude < (cp.radius + 6) * (cp.radius + 6)) return "in an enemy post";
            return null;
        }

        /// <summary>A free spot a couple of metres behind the squadmate.</summary>
        public bool MateSpot(Unit m, out Vector3 spot)
        {
            Vector3 back = -m.transform.forward; back.y = 0; back = back.sqrMagnitude > 0.01f ? back.normalized : Vector3.back;
            for (int i = 0; i < 10; i++)
            {
                float a = (i == 0 ? 0 : Random.Range(-100f, 100f));
                Vector3 d = Quaternion.Euler(0, a, 0) * back;
                Vector3 p = m.transform.position + d * Random.Range(1.8f, 3.2f);
                p.y = GroundY(p) + 0.1f;
                if (Mathf.Abs(p.y - m.transform.position.y) > 2.5f) continue;
                if (!Physics.CheckCapsule(p + Vector3.up * 0.5f, p + Vector3.up * 1.6f, 0.45f, Layers.MoveMask | (1 << Layers.Unit), QueryTriggerInteraction.Ignore)) { spot = p; return true; }
            }
            spot = Vector3.zero; return false;
        }

        /// <summary>Spawn beside a squadmate in the field (land only, when the mate is safe).</summary>
        public Unit SpawnOnMate(Participant p, int classIndex, HeroDef hero, Unit mate)
        {
            if (over || MateBlocked(mate) != null || !MateSpot(mate, out var spot)) return null;
            var f = Faction(p.team);
            if (hero != null) { p.heroReady = false; p.pointsAtHero = p.points; p.everHero = true; }
            var cls = f.classes[Mathf.Clamp(classIndex, 0, 4)];
            mateSpawns++;
            mate.part.mateUsedUntil = Time.time + MateCooldown;
            return SpawnUnit(p, hero != null ? null : cls, hero, spot, mate.transform.eulerAngles.y);
        }

        public CommandPost PickPost(int team, Vector3 near, Participant who = null)
        {
            CommandPost best = null; float bd = float.MaxValue;
            foreach (var p in posts)
            {
                if (!p.SpawnableFor(team)) continue;
                if (who != null && WaitFor(who, p) > 0) continue;
                float d = (p.Pos - near).sqrMagnitude + Random.Range(0, 4000f);
                if (d < bd) { bd = d; best = p; }
            }
            return best;
        }

        /// <summary>Spawn a participant on land at a post (or in space in its hangar / a ship).</summary>
        public Unit Spawn(Participant p, int classIndex, HeroDef hero, int postIndex, int shipIndex)
        {
            if (over || !CanSpawn(p.team)) return null;
            var f = Faction(p.team);
            if (hero != null) { p.heroReady = false; p.pointsAtHero = p.points; p.everHero = true; }
            if (space)
            {
                var ship = space.ships[p.team];
                ClassDef c = classIndex == 1 ? f.marine : f.pilot;
                if (shipIndex >= 0 && hero == null)
                {
                    var def = f.ships[shipIndex];
                    Vector3 pos; Quaternion rot; bool parked = ship.HangarOpen;
                    if (parked)
                    {
                        pos = FreeParkSlot(ship) + Vector3.up * (VehicleModels.Get(def, f).bounds.extents.y + 0.4f);
                        rot = ship.transform.rotation;
                    }
                    else
                    {
                        pos = ship.transform.TransformPoint(new Vector3(Random.Range(-60, 60), Random.Range(10, 40), 110));
                        rot = ship.transform.rotation;
                    }
                    var v = SpawnVehicle(def, p.team, pos, rot);
                    if (parked) v.Land(ship.hangar); else { v.landed = false; v.throttle = 0.6f; v.speed = def.speed * 0.5f; }
                    var u = SpawnUnit(p, c, null, pos, rot.eulerAngles.y);
                    v.Enter(u);
                    return u;
                }
                Vector3 fp = ship.FootSpawn();
                return SpawnUnit(p, c, hero, fp, ship.transform.eulerAngles.y + 180);
            }
            CommandPost post = postIndex >= 0 && postIndex < posts.Count && posts[postIndex].SpawnableFor(p.team) && WaitFor(p, posts[postIndex]) <= 0 ? posts[postIndex] : PickPost(p.team, p.unit ? p.unit.transform.position : Vector3.zero, p);
            if (!post) return null;
            Vector3 sp = post.SpawnPoint();
            Vector3 toCentre = -post.Pos; toCentre.y = 0;
            float yaw = Mathf.Atan2(toCentre.x, toCentre.z) * Mathf.Rad2Deg + Random.Range(-30, 30);
            var cls = f.classes[Mathf.Clamp(classIndex, 0, 4)];
            return SpawnUnit(p, hero != null ? null : cls, hero, sp, yaw);
        }

        Vector3 FreeParkSlot(CapitalShip ship)
        {
            for (int i = 0; i < 8; i++)
            {
                int s = (i + Random.Range(0, 8)) % 8;
                Vector3 p = ship.ParkSlot(s);
                if (!Physics.CheckSphere(p + Vector3.up * 3, 5, 1 << Layers.Vehicle)) return p;
            }
            return ship.ParkSlot(Random.Range(0, 8)) + Vector3.up * 6;
        }

        public bool TeamHasHero(int t)
        {
            foreach (var u in units) if (u && u.Alive && u.team == t && u.IsHero) return true;
            return false;
        }

        public HeroDef FreeHero(int t, int prefer = -1)
        {
            var f = Faction(t);
            var used = new HashSet<HeroDef>();
            foreach (var u in units) if (u && u.Alive && u.hero != null) used.Add(u.hero);
            if (prefer >= 0 && !used.Contains(f.heroes[prefer])) return f.heroes[prefer];
            var list = new List<HeroDef>();
            foreach (var h in f.heroes) if (!used.Contains(h)) list.Add(h);
            return list.Count > 0 ? list[Random.Range(0, list.Count)] : f.heroes[0];
        }

        // ── frame ────────────────────────────────────────────────────────
        void Update()
        {
            float dt = Time.deltaTime;
            control.Tick(dt);
            if (dt <= 0) return;
            time += dt;
            if (cfg.netClient) { ClientFrame(dt); return; }
            if (cfg.netHost) Net.NetHost.PreUnits(dt);

            if (!over && !royale)
            {
                // bot respawns
                foreach (var p in people)
                {
                    if (p.isPlayer || p.remote || (p.unit && p.unit.Alive)) continue;
                    p.respawn -= dt;
                    if (p.respawn > 0) continue;
                    if (!CanSpawn(p.team)) { p.respawn = 2; continue; }
                    BotSpawn(p);
                }
            }

            for (int i = units.Count - 1; i >= 0; i--)
            {
                var u = units[i];
                if (!u) { units.RemoveAt(i); continue; }
                if (u.bot != null && u.Alive && !over) u.bot.Think(dt);
                if (over && u.Alive) { u.input.fire = false; u.input.move = Vector3.zero; }
                u.Tick(dt);
            }
            for (int i = vehicles.Count - 1; i >= 0; i--)
            {
                var v = vehicles[i];
                if (!v) { vehicles.RemoveAt(i); continue; }
                v.Tick(dt);
            }
            Physics.SyncTransforms();
            if (!over)
            {
                foreach (var p in posts) p.Tick(dt);
                commander?.Tick(dt);
                FillPlayerVehicle(dt);
                if (space) foreach (var s in space.ships) s.Tick(dt);
                if (royale) royale.Rules(dt); else Rules(dt);
            }
            else
            {
                overTime += dt;
                if (space) foreach (var s in space.ships) s.Tick(dt);
            }
            if (cfg.netHost) Net.NetHost.PostFrame(dt);
        }

        /// <summary>A Wi-Fi guest's frame: messages in, draw the host's world, our own soldier / vehicle moves here, input out.</summary>
        void ClientFrame(float dt)
        {
            Net.NetClient.PreUnits(dt);
            if (Net.NetClient.Current != this) return;
            for (int i = units.Count - 1; i >= 0; i--)
            {
                var u = units[i];
                if (!u) { units.RemoveAt(i); continue; }
                if (over && u.Alive) { u.input.fire = false; u.input.move = Vector3.zero; }
                u.Tick(dt);
            }
            for (int i = vehicles.Count - 1; i >= 0; i--)
            {
                var v = vehicles[i];
                if (!v) { vehicles.RemoveAt(i); continue; }
                v.Tick(dt);
            }
            Physics.SyncTransforms();
            teams[0].posts = teams[1].posts = 0;
            foreach (var p in posts) { p.VisualTick(dt); if (p.owner >= 0) teams[p.owner].posts++; }
            if (space) foreach (var s in space.ships) s.Tick(dt);
            if (over) overTime += dt;
            Net.NetClient.PostFrame(dt);
        }

        /// <summary>Guest: a post changed hands in the host's battle - say so from our side.</summary>
        public void NetPostChanged(CommandPost cp, int was, int now)
        {
            if (now >= 0)
            {
                bool mine = now == player.team;
                HUD.Message($"{(mine ? "We captured" : "The enemy captured")} {cp.postName}", mine ? new Color(0.5f, 1f, 0.6f) : new Color(1f, 0.5f, 0.4f), 3);
                Sfx.Play2D(mine ? "capture" : "lost", 0.7f);
            }
            else if (was == player.team) { HUD.Message($"We lost {cp.postName}", new Color(1f, 0.5f, 0.4f), 3); Sfx.Play2D("lost", 0.6f); }
        }

        /// <summary>Guest: the host's battle ended.</summary>
        public void NetEnd(int winner, string reason, string mvp)
        {
            if (over) return;
            over = true;
            result = new BattleResult { winner = winner, reason = reason, mvp = mvp };
            result.playerPoints = player.points; result.playerKills = player.kills; result.playerDeaths = player.deaths; result.playerCaptures = player.captures;
            result.awards.AddRange(player.awardsEarned);
            result.ticketsLeft[0] = Mathf.Max(0, Mathf.RoundToInt(teams[0].tickets));
            result.ticketsLeft[1] = Mathf.Max(0, Mathf.RoundToInt(teams[1].tickets));
            Sfx.Play2D(winner == player.team ? "award" : "lost", 1f);
            Sfx.PlayMusic(false);
            if (winner >= 0) Profiles.Record(this, winner == player.team);
        }

        void BotSpawn(Participant p)
        {
            HeroDef hero = null;
            var rh = RemoteHuman;
            if (p.heroReady && !TeamHasHero(p.team) && !(player.team == p.team && (player.heroReady || (player.unit && player.unit.IsHero)))
                && !(rh != null && rh.team == p.team && (rh.heroReady || (rh.unit && rh.unit.IsHero))))
                hero = FreeHero(p.team);
            if (space)
            {
                int ship = -1, cls = 0;
                float r = Random.value;
                bool defenders = CountOnFoot(p.team) < 3;
                // keep the sky readable: at most this many craft per side; the rest wait their turn
                if (!defenders && hero == null && CountShips(p.team) >= MaxShipsPerSide) { p.respawn = 3; return; }
                if (hero == null && !defenders)
                {
                    ship = r < 0.42f ? 0 : r < 0.62f ? 2 : r < 0.88f ? 1 : 3;
                }
                else cls = Random.value < 0.6f ? 1 : 0;
                Spawn(p, cls, hero, -1, ship);
            }
            else
            {
                p.classChoice = PickBotClass(p.team);
                var mate = commander != null && hero == null ? commander.MateToSpawnOn(p) : null;
                if (mate)
                {
                    float mw = WaitForMate(p, mate.part);
                    if (mw > 0 && mw < 7) { p.respawn = mw + 0.1f; return; }   // worth waiting a few seconds for
                    if (mw > 0) mate = null;
                }
                Unit spawned = mate ? SpawnOnMate(p, p.classChoice, null, mate) : null;
                if (!spawned)
                {
                    var post = commander != null && Random.value < 0.85f ? commander.SpawnPostFor(p) : null;
                    if (post && WaitFor(p, post) > 0) post = null;
                    spawned = Spawn(p, p.classChoice, hero, post ? post.index : -1, -1);
                }
                if (!spawned) { p.respawn = 1; return; }
            }
            p.respawn = 5;
        }

        public const int MaxShipsPerSide = 8;
        int CountShips(int t)
        {
            int n = 0;
            foreach (var v in vehicles) if (v && v.Alive && v.IsShip && !v.IsEmpty && v.Team == t) n++;
            return n;
        }

        int CountOnFoot(int t)
        {
            int n = 0;
            foreach (var u in units) if (u && u.Alive && u.team == t && !u.InVehicle) n++;
            return n;
        }

        float rideT;
        /// <summary>Friendly bots nearby (squadmates first) climb into the player's free seats when the player is driving and slow.</summary>
        void FillPlayerVehicle(float dt)
        {
            rideT -= dt;
            if (rideT > 0) return;
            rideT = 1f;
            FillVehicleOf(player);
            var rh = RemoteHuman;
            if (rh != null) FillVehicleOf(rh);
        }

        void FillVehicleOf(Participant human)
        {
            var me = human.unit;
            if (!land || !me || !me.Alive || !me.InVehicle || me.seat != 0) return;
            var v = me.vehicle;
            if (v.speed > 4 || v.def.IsShip) return;
            if (v.def.Flies && !v.landed && v.AltitudeAboveGround() > 4) return;
            int free = 0; foreach (var r in v.riders) if (!r) free++;
            foreach (var u in units) if (u && u.Alive && u.bot != null && u.bot.WantsRide && u.team == me.team) free--;
            if (free <= 0) return;
            var sq = commander != null ? commander.SquadOf(human) : null;
            var cands = new List<(Unit u, float score)>();
            foreach (var u in units)
            {
                if (!u || !u.Alive || u.bot == null || u.team != me.team || u.InVehicle || u.IsHero || u.bot.WantsRide) continue;
                float d = (u.transform.position - v.transform.position).magnitude;
                if (d > 35 || Time.time - u.lastHurt < 3) continue;
                bool mate = sq != null && sq.members.Contains(u.part);
                cands.Add((u, d - (mate ? 30 : 0)));
            }
            cands.Sort((a, c) => a.score.CompareTo(c.score));
            for (int i = 0; i < cands.Count && free > 0; i++, free--) cands[i].u.bot.RideWith(v);
        }

        void Rules(float dt)
        {
            // posts and bleeding
            if (land)
            {
                teams[0].posts = teams[1].posts = 0;
                foreach (var p in posts) if (p.owner >= 0) teams[p.owner].posts++;
                for (int t = 0; t < 2; t++)
                {
                    var ts = teams[t];
                    int diff = teams[1 - t].posts - ts.posts;
                    if (diff > 0)
                    {
                        ts.bleedTimer -= dt;
                        if (ts.bleedTimer <= 0) { ts.bleedTimer = Mathf.Max(1.5f, 7f / diff); ts.tickets -= 1; }
                    }
                    else ts.bleedTimer = 7f;
                    if (ts.posts == 0) { ts.noPostTimer += dt; } else ts.noPostTimer = 0;
                }
            }

            // field medics
            medicTick -= dt;
            if (medicTick <= 0)
            {
                medicTick = 1;
                foreach (var u in units)
                    if (u && u.Alive && !u.InVehicle && BonusOf(u.team) == Bonus.FieldMedics && Time.time - u.lastHurt > 5) u.Heal(u.maxHp * 0.02f);
            }

            // participants close to heroes
            heroThink -= dt;
            if (heroThink <= 0)
            {
                heroThink = 1;
                foreach (var p in people)
                    if (!p.heroReady && p.HeroProgress >= 1 && !(p.unit && p.unit.IsHero))
                    {
                        p.heroReady = true;
                        if (p.isPlayer) { HUD.Message("HERO UNLOCKED - press H to become a hero now, or pick one at your next spawn", Color.yellow, 5); Sfx.Play2D("award", 0.8f); }
                        else if (p.remote) Net.NetHost.Text("HERO UNLOCKED - tap HERO to become a hero now, or pick one at your next spawn", Color.yellow, 5, "award");
                    }
            }

            // end conditions
            for (int t = 0; t < 2; t++)
            {
                var ts = teams[t];
                if (ts.tickets <= 0) { End(1 - t, $"{ts.faction.name} ran out of reinforcements"); return; }
                if (land && ts.noPostTimer > 20f && !AnyAlive(t)) { End(1 - t, $"{ts.faction.name} lost every command post"); return; }
                if (land && ts.noPostTimer > 45f) { End(1 - t, $"{ts.faction.name} could not retake a command post"); return; }
            }
            shipAlertT -= dt;
        }

        bool AnyAlive(int t)
        {
            foreach (var u in units) if (u && u.Alive && u.team == t) return true;
            return false;
        }

        // ── events ───────────────────────────────────────────────────────
        public void OnUnitKilled(Unit victim, Unit killer, WeaponDef w)
        {
            var vp = victim.part;
            vp.deaths++;
            vp.unit = victim;
            if (victim.team >= 0 && victim.team < teams.Length) teams[victim.team].tickets -= 1;
            vp.diedAt = Time.time;
            vp.respawn = RespawnPost;
            string kname = killer ? killer.displayName : null;
            if (killer && killer.team != victim.team)
            {
                var kp = killer.part;
                kp.kills++;
                AddPoints(kp, victim.IsHero ? 4 : 2);
                if (killer.IsHero) killer.Heal(killer.maxHp * 0.08f);
                if (killer.cls != null && !killer.InVehicle && !royale)
                {
                    int role = (int)killer.cls.role;
                    kp.classKills[role]++;
                    kp.lifeKills++;
                    if (!kp.awards[role] && (kp.classKills[role] >= 6 || kp.lifeKills >= 4))
                    {
                        kp.awards[role] = true;
                        var award = killer.cls.award;
                        if (killer.Alive && award != null && killer.weapons[0].def == killer.cls.primary)
                        {
                            killer.weapons[0] = new WeaponState(award);
                            if (killer.cur == 0) killer.rig.SetWeapon(award);
                        }
                        if (kp.isPlayer)
                        {
                            HUD.Message($"AWARD: {killer.cls.awardName} for the {killer.cls.name} - yours for the rest of the battle", new Color(1f, 0.85f, 0.3f), 6);
                            Sfx.Play2D("award", 0.9f);
                            player.awardsEarned.Add(killer.cls.awardName);
                        }
                        else if (kp.remote)
                        {
                            Net.NetHost.Text($"AWARD: {killer.cls.awardName} for the {killer.cls.name} - yours for the rest of the battle", new Color(1f, 0.85f, 0.3f), 6, "award");
                            Net.NetHost.Award(killer.cls.awardName);
                        }
                    }
                }
            }
            HUD.Kill(kname, killer ? killer.team : -1, victim.displayName, victim.team, w != null ? w.name : killer == null ? "accident" : "");
            Net.NetHost.OnKill(kname, killer ? killer.team : -1, victim.displayName, victim.team, w != null ? w.name : killer == null ? "accident" : "");
            if (royale) royale.OnUnitKilled(victim, killer);
            if (vp.isPlayer) control.OnPlayerDied(killer);
        }

        public void OnVehicleDestroyed(Vehicle v, Unit killer)
        {
            if (killer && killer.team != v.Team) AddPoints(killer.part, v.IsShip ? 2 : 3);
            if (killer && killer.part.isPlayer) HUD.Message($"Destroyed {v.def.name}", Color.white, 2);
            else if (killer && killer.part.remote) Net.NetHost.Text($"Destroyed {v.def.name}", Color.white, 2, null);
            vehicles.Remove(v);
        }

        public void OnPostCaptured(CommandPost cp, int t)
        {
            commander?.OnPostChanged(cp, -1);
            foreach (var u in units)
                if (u && u.Alive && u.team == t && !u.InVehicle && (u.transform.position - cp.Pos).sqrMagnitude < cp.radius * cp.radius)
                { AddPoints(u.part, 4); u.part.captures++; }
            bool mine = t == player.team;
            HUD.Message($"{(mine ? "We captured" : "The enemy captured")} {cp.postName}", mine ? new Color(0.5f, 1f, 0.6f) : new Color(1f, 0.5f, 0.4f), 3);
            Sfx.Play2D(mine ? "capture" : "lost", 0.7f);
        }

        public void OnPostNeutralised(CommandPost cp, int lost, int by)
        {
            commander?.OnPostChanged(cp, lost);
            foreach (var u in units)
                if (u && u.Alive && u.team == by && !u.InVehicle && (u.transform.position - cp.Pos).sqrMagnitude < cp.radius * cp.radius) AddPoints(u.part, 1);
            if (lost == player.team) { HUD.Message($"We lost {cp.postName}", new Color(1f, 0.5f, 0.4f), 3); Sfx.Play2D("lost", 0.6f); }
        }

        public void OnSystemDestroyed(CapitalShip ship, Subsystem s, Unit by)
        {
            teams[ship.team].tickets -= 15;
            if (by && by.team != ship.team) AddPoints(by.part, 10);
            bool mine = ship.team == player.team;
            HUD.Message($"{(mine ? "Our" : "Enemy")} {s.sysName} destroyed!{(s.critical ? " (critical system)" : "")}", mine ? new Color(1f, 0.5f, 0.4f) : new Color(0.5f, 1f, 0.6f), 4);
            Sfx.Play2D(mine ? "alarm" : "capture", 0.7f);
            if (s.kind == SysKind.Shield) HUD.Message(mine ? "Our shields are down!" : "Enemy shields are down - hit their systems!", Color.yellow, 4);
            var rh = RemoteHuman;
            if (rh != null)
            {
                bool theirs = ship.team == rh.team;
                Net.NetHost.Text($"{(theirs ? "Our" : "Enemy")} {s.sysName} destroyed!{(s.critical ? " (critical system)" : "")}", theirs ? new Color(1f, 0.5f, 0.4f) : new Color(0.5f, 1f, 0.6f), 4, theirs ? "alarm" : "capture");
                if (s.kind == SysKind.Shield) Net.NetHost.Text(theirs ? "Our shields are down!" : "Enemy shields are down - hit their systems!", Color.yellow, 4, null);
            }
        }

        public void AlertShip(CapitalShip ship, Subsystem s)
        {
            if (shipAlertT > 0 || !s.interior) return;
            bool told = false;
            if (ship.team == player.team) { HUD.Message($"Intruders! Our {s.sysName} is under attack", new Color(1f, 0.6f, 0.3f), 4); Sfx.Play2D("alarm", 0.6f); told = true; }
            var rh = RemoteHuman;
            if (rh != null && ship.team == rh.team) { Net.NetHost.Text($"Intruders! Our {s.sysName} is under attack", new Color(1f, 0.6f, 0.3f), 4, "alarm"); told = true; }
            if (told) shipAlertT = 20;
        }

        public void OnCapitalDestroyed(CapitalShip ship, Unit by)
        {
            for (int i = 0; i < 10; i++) Fx.Explosion(ship.transform.TransformPoint(new Vector3(Random.Range(-140, 140), Random.Range(-10, 30), Random.Range(-40, 30))), 5, true);
            if (by) AddPoints(by.part, 20);
            End(1 - ship.team, $"The {ship.faction.capitalName} was destroyed");
        }

        public void OnCapitalCrippled(CapitalShip ship, Unit by)
        {
            if (by) AddPoints(by.part, 15);
            End(1 - ship.team, $"The {ship.faction.capitalName} lost all its critical systems");
        }

        public void AddRepair(Participant p, float hp)
        {
            if (p == null) return;
            p.repairAcc += hp;
            while (p.repairAcc >= 400) { p.repairAcc -= 400; AddPoints(p, 1); }
        }

        public void AddPoints(Participant p, int n)
        {
            if (p == null) return;
            p.points += n;
            if (p.isPlayer) HUD.Points(n);
            else if (p.remote) Net.NetHost.Points(n);
        }

        // ── end ──────────────────────────────────────────────────────────
        public void End(int winner, string reason)
        {
            if (over) return;
            over = true;
            result = new BattleResult { winner = winner, reason = reason };
            result.playerPoints = player.points; result.playerKills = player.kills; result.playerDeaths = player.deaths; result.playerCaptures = player.captures;
            result.awards.AddRange(player.awardsEarned);
            result.ticketsLeft[0] = Mathf.Max(0, Mathf.RoundToInt(teams[0].tickets));
            result.ticketsLeft[1] = Mathf.Max(0, Mathf.RoundToInt(teams[1].tickets));
            Participant mvp = null;
            foreach (var p in people) if (!p.boss && (mvp == null || (royale ? p.kills > mvp.kills : p.points > mvp.points))) mvp = p;
            result.mvp = mvp != null ? (royale ? $"{mvp.name} - {mvp.kills} kills" : $"{mvp.name} ({Faction(mvp.team).shortName}) - {mvp.points} pts") : "";
            bool won = winner == player.team;
            Sfx.Play2D(won ? "award" : "lost", 1f);
            Sfx.PlayMusic(false);
            Net.NetHost.OnEnd(winner, reason, result.mvp);
            Profiles.Record(this, won);
        }

        public void Finish(bool quit)
        {
            if (result == null) { result = new BattleResult { winner = -1, reason = "Left the battle" }; }
            result.quit = quit;
            var cb = onEnd; var r = result;
            Teardown();
            cb?.Invoke(r);
        }

        void Teardown()
        {
            if (I == this) I = null;
            Ctl.LockCursor(false);
            Destroy(gameObject);
        }

        void OnDestroy() { if (I == this) I = null; }
    }
}
