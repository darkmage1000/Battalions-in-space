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
    public class Battle : MonoBehaviour
    {
        public static Battle I;
        public BattleConfig cfg;
        public readonly TeamState[] teams = new TeamState[2];
        public readonly List<Participant> people = new List<Participant>();
        public Participant player;
        public readonly List<Unit> units = new List<Unit>();
        public readonly List<Vehicle> vehicles = new List<Vehicle>();
        public readonly List<CommandPost> posts = new List<CommandPost>();
        public LandWorld land;
        public SpaceWorld space;
        public PlayerControl control;
        public Commander commander;
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
            player = new Participant { name = "You", team = cfg.playerTeam, isPlayer = true, aimSkill = 1 };
            people.Add(player);
            for (int t = 0; t < 2; t++)
            {
                var f = Faction(t);
                int n = cfg.botsPerTeam - (t == cfg.playerTeam ? 1 : 0);
                for (int i = 0; i < n; i++)
                {
                    float skill = Mathf.Clamp01(0.25f + cfg.difficulty * 0.3f + Random.Range(-0.15f, 0.15f));
                    var p = new Participant { name = f.names[i % f.names.Length], team = t, aimSkill = skill, respawn = Random.Range(0.2f, 3.5f) };
                    p.classChoice = PickBotClass(t);
                    people.Add(p);
                }
            }
            foreach (var p in people) p.heroCost = BonusOf(p.team) == Bonus.VeteranOfficers ? 18 : p.isPlayer ? 36 : 52;
            if (land) commander = new Commander(this);

            var pc = gameObject.AddComponent<PlayerControl>();
            control = pc;
            pc.Init(this);
            gameObject.AddComponent<HUD>().battle = this;
            Sfx.Music.Stop();
            Ctl.LockCursor(false);
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
                if (cfg.land.posts[p.index].owner == t && cfg.land.posts[p.index].vehicles.Length >= 3) best = p;
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
            if (!p.isPlayer) { u.bot = new BotBrain(u); }
            p.unit = u;
            p.lifeKills = 0;
            units.Add(u);
            return u;
        }

        public Vehicle SpawnVehicle(VehicleDef d, int team, Vector3 pos, Quaternion rot)
        {
            var v = Vehicle.Spawn(this, d, Faction(team), team, pos, rot);
            v.transform.SetParent(root, true);
            if (BonusOf(1 - team) == Bonus.Saboteurs) v.hp *= 0.75f;
            vehicles.Add(v);
            return v;
        }

        public bool CanSpawn(int team)
        {
            if (space) return !space.ships[team].destroyed;
            foreach (var p in posts) if (p.owner == team) return true;
            return false;
        }

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
            return SpawnUnit(p, hero != null ? null : cls, hero, spot, mate.transform.eulerAngles.y);
        }

        public CommandPost PickPost(int team, Vector3 near)
        {
            CommandPost best = null; float bd = float.MaxValue;
            foreach (var p in posts)
            {
                if (!p.SpawnableFor(team)) continue;
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
            CommandPost post = postIndex >= 0 && postIndex < posts.Count && posts[postIndex].SpawnableFor(p.team) ? posts[postIndex] : PickPost(p.team, p.unit ? p.unit.transform.position : Vector3.zero);
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

            if (!over)
            {
                // bot respawns
                foreach (var p in people)
                {
                    if (p.isPlayer || (p.unit && p.unit.Alive)) continue;
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
                if (space) foreach (var s in space.ships) s.Tick(dt);
                Rules(dt);
            }
            else
            {
                overTime += dt;
                if (space) foreach (var s in space.ships) s.Tick(dt);
            }
        }

        void BotSpawn(Participant p)
        {
            HeroDef hero = null;
            if (p.heroReady && !TeamHasHero(p.team) && !(player.team == p.team && (player.heroReady || (player.unit && player.unit.IsHero))))
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
                Unit spawned = mate ? SpawnOnMate(p, p.classChoice, null, mate) : null;
                if (!spawned)
                {
                    var post = commander != null && Random.value < 0.85f ? commander.SpawnPostFor(p) : null;
                    spawned = Spawn(p, p.classChoice, hero, post ? post.index : -1, -1);
                }
                if (!spawned) { p.respawn = 2; return; }
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
            teams[victim.team].tickets -= 1;
            vp.respawn = 5;
            string kname = killer ? killer.displayName : null;
            if (killer && killer.team != victim.team)
            {
                var kp = killer.part;
                kp.kills++;
                AddPoints(kp, victim.IsHero ? 4 : 2);
                if (killer.IsHero) killer.Heal(killer.maxHp * 0.08f);
                if (killer.cls != null && !killer.InVehicle)
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
                    }
                }
            }
            HUD.Kill(kname, killer ? killer.team : -1, victim.displayName, victim.team, w != null ? w.name : killer == null ? "accident" : "");
            if (vp.isPlayer) control.OnPlayerDied(killer);
        }

        public void OnVehicleDestroyed(Vehicle v, Unit killer)
        {
            if (killer && killer.team != v.Team) AddPoints(killer.part, v.IsShip ? 2 : 3);
            if (killer && killer.part.isPlayer) HUD.Message($"Destroyed {v.def.name}", Color.white, 2);
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
        }

        public void AlertShip(CapitalShip ship, Subsystem s)
        {
            if (ship.team != player.team || shipAlertT > 0) return;
            if (s.interior) { shipAlertT = 20; HUD.Message($"Intruders! Our {s.sysName} is under attack", new Color(1f, 0.6f, 0.3f), 4); Sfx.Play2D("alarm", 0.6f); }
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
            foreach (var p in people) if (mvp == null || p.points > mvp.points) mvp = p;
            result.mvp = mvp != null ? $"{mvp.name} ({Faction(mvp.team).shortName}) - {mvp.points} pts" : "";
            bool won = winner == player.team;
            Sfx.Play2D(won ? "award" : "lost", 1f);
            Sfx.Music.Play();
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
