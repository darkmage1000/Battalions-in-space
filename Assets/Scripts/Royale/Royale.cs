using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    /// <summary>
    /// Battle royale rules on top of a land battle: up to 100 players (you, maybe a Wi-Fi partner, the rest bots), each on a
    /// team of their own (a Wi-Fi duo shares one). Everyone picks a landing spot and glides down from the dropship, finds
    /// weapons and supplies in buildings and chests, and fights while a zone shrinks on a timer. Vehicles stand around the map,
    /// a boss lands a few minutes in and drops legendary loot, and vaults open only with a key (10% of legendary finds come with
    /// one). Last team standing wins.
    /// </summary>
    public class Royale : MonoBehaviour
    {
        public Battle b;
        public enum Phase { Drop, Play }
        public Phase phase = Phase.Drop;

        // -- the drop ------------------------------------------------------------
        public const float DropTime = 20f;          // the dropship's countdown: everyone has jumped by then
        public const float DropAlt = 190f;          // jump height above the ground
        public float dropT = DropTime;
        public bool playerPicked;
        public bool playerWantsJump;
        public Vector3 dropPickPos;               // where the player wants to land (tap the map)
        public bool usedUse;                     // the player's E went to loot / a chest / a vault this frame
        readonly Dictionary<Participant, float> jumpAt = new Dictionary<Participant, float>();

        // -- the zone ------------------------------------------------------------
        // per phase: seconds before it starts to shrink, seconds it takes, the new radius, damage per second (share of max health)
        public static readonly (float wait, float shrink, float r, float dmg)[] Phases =
        {
            (110, 70, 520, 0.008f), (85, 55, 300, 0.015f), (70, 45, 170, 0.025f), (55, 40, 90, 0.04f), (45, 35, 40, 0.06f), (30, 35, 0, 0.09f),
        };
        public Vector2 zoneC, nextC;
        public float zoneR, nextR, startR;
        public int zonePhase;
        public float zoneTimer;
        public bool shrinking;
        Vector2 fromC; float fromR;
        float zoneTick;
        Transform wall;
        Material wallMat;

        // -- loot ----------------------------------------------------------------
        public readonly List<LootItem> loot = new List<LootItem>();
        public readonly List<LootChest> chests = new List<LootChest>();
        readonly Dictionary<long, List<LootItem>> grid = new Dictionary<long, List<LootItem>>();
        const float GridCell = 24f;
        int nextId;
        System.Random rng;
        Transform lootRoot;
        float cullT, pickT;
        readonly List<LootItem> shown = new List<LootItem>();
        const float ShowRange = 95f;
        public int keysSpawned;

        // -- the boss ------------------------------------------------------------
        public Participant boss;
        public float bossAt;
        public bool bossSpawned, bossDead;
        public Vector3 bossLair;
        public string bossPlace;
        static HeroDef bossDef;

        // -- standings -----------------------------------------------------------
        public int AliveTeams, AlivePlayers;
        public bool client;                       // a Wi-Fi guest: loot, chests and the zone come from the host
        public bool jumpRequested;
        readonly Dictionary<int, LootItem> byId = new Dictionary<int, LootItem>();
        public float playTime;
        public int TotalPlayers;

        /// <summary>Once half the players are gone the zone waits less between phases, so the end doesn't drag.</summary>
        float WaitMul => Mathf.Clamp(AlivePlayers / (float)Mathf.Max(1, TotalPlayers) * 2f, 0.55f, 1f);
        public float ZoneDamage => Phases[Mathf.Clamp(zonePhase, 0, Phases.Length - 1)].dmg;
        public bool OutsideZone(Vector3 p) => phase == Phase.Play && (new Vector2(p.x, p.z) - zoneC).sqrMagnitude > zoneR * zoneR;
        public bool OutsideNext(Vector3 p) => phase == Phase.Play && (new Vector2(p.x, p.z) - nextC).sqrMagnitude > nextR * nextR;

        public void Init(Battle battle, bool asClient = false)
        {
            b = battle; client = asClient;
            rng = new System.Random(System.Environment.TickCount);
            var land = b.land;
            startR = land.PlayHalf * 1.45f;
            zoneC = nextC = Vector2.zero;
            zoneR = nextR = startR;
            dropPickPos = new Vector3(0, land.Top(0, 0), 0);
            lootRoot = new GameObject("loot").transform; lootRoot.SetParent(transform, false);
            foreach (var p in b.people) if (!p.boss) TotalPlayers++;
            AlivePlayers = TotalPlayers; AliveTeams = TotalPlayers;
            BuildWall();
            boss = null;
            foreach (var p in b.people) if (p.boss) boss = p;
            if (client) return;
            SpawnLoot();
            SpawnChests();
            SpawnVehicles();
            // bots pick where to land: most go for buildings, some for the edges; they jump at different moments
            foreach (var p in b.people)
            {
                if (p.Human || p.boss) continue;
                p.dropAt = BotDropSpot();
                jumpAt[p] = Random.Range(1.5f, DropTime - 0.5f);
            }
        }

        // ── spawning loot, chests and vehicles ───────────────────────────
        Vector3 RandomGround(float within)
        {
            var land = b.land;
            for (int i = 0; i < 30; i++)
            {
                float x = (float)(rng.NextDouble() * 2 - 1) * within, z = (float)(rng.NextDouble() * 2 - 1) * within;
                Vector3 p = new Vector3(x, 0, z);
                if (!land.nav.Walkable(p)) continue;
                if (land.LiquidAt(x, z, out float lt, out _) && land.Top(x, z) < lt + 0.05f) continue;
                p.y = land.Top(x, z);
                return p;
            }
            return new Vector3(0, land.Top(0, 0), 0);
        }

        Vector3 InHouse(LandWorld.House h, float margin = 1.3f)
        {
            float x = (float)(rng.NextDouble() * 2 - 1) * Mathf.Max(0.2f, h.w / 2 - margin), z = (float)(rng.NextDouble() * 2 - 1) * Mathf.Max(0.2f, h.d / 2 - margin);
            Vector3 p = h.center + h.rot * new Vector3(x, 0, z);
            p.y = h.center.y + 0.04f;
            return p;
        }

        void SpawnLoot()
        {
            var land = b.land;
            foreach (var h in land.houses)
            {
                if (h.vault) continue;
                float area = h.w * h.d;
                int n = area > 220 ? 4 : area > 120 ? 3 : area > 70 ? 2 : 1;
                if (rng.NextDouble() < 0.4) n++;
                int bonus = area > 160 ? 1 : 0;           // compounds, hangars and the wreck hold better loot
                for (int i = 0; i < n; i++) RandomItem(InHouse(h), bonus);
            }
            // out in the open: fewer and plainer
            int outside = Mathf.RoundToInt(TotalPlayers * 3.6f);
            for (int i = 0; i < outside; i++) RandomItem(RandomGround(land.PlayHalf * 0.94f), -1);
            // the vaults: the best there is
            foreach (var v in land.vaults)
            {
                for (int i = 0; i < 4; i++) Drop(LootKind.Weapon, 4, LootTable.Weapon(4, rng), 1, InHouse(v.house, 1.6f), false);
                Drop(LootKind.Shield, 3, null, 3, InHouse(v.house, 1.6f), false);
                Drop(LootKind.Medkit, 3, null, 3, InHouse(v.house, 1.6f), false);
            }
        }

        void RandomItem(Vector3 p, int bonus)
        {
            var kind = LootTable.RollKind(rng);
            int tier = LootTable.RollTier(rng, bonus);
            if (kind == LootKind.Weapon) Drop(kind, tier, LootTable.Weapon(tier, rng), 1, p);
            else Drop(kind, kind == LootKind.Shield || kind == LootKind.Medkit ? Mathf.Clamp(tier, 0, 3) : 1, null, kind == LootKind.Medkit || kind == LootKind.Shield ? 1 + (tier >= 3 ? 1 : 0) : 1, p);
        }

        /// <summary>Put an item on the ground. Legendary finds come with a vault key one time in ten.</summary>
        public LootItem Drop(LootKind kind, int tier, WeaponDef w, int count, Vector3 p, bool keyChance = true, int forceId = 0)
        {
            var it = new LootItem { id = forceId > 0 ? forceId : ++nextId, kind = kind, tier = tier, weapon = w, count = count, pos = p };
            byId[it.id] = it;
            var go = new GameObject("loot");
            go.transform.SetParent(lootRoot, false);
            go.transform.SetPositionAndRotation(p, Quaternion.Euler(0, (float)rng.NextDouble() * 360, 0));
            go.AddComponent<MeshFilter>().sharedMesh = LootModels.For(it);
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = Mats.Lit; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.enabled = false;
            it.go = go;
            loot.Add(it);
            it.cell = CellKey(p);
            if (!grid.TryGetValue(it.cell, out var list)) grid[it.cell] = list = new List<LootItem>();
            list.Add(it);
            if (!client) Net.NetHost.LootAdd(it);
            if (!client && keyChance && tier >= 4 && kind == LootKind.Weapon && rng.NextDouble() < 0.1)
            {
                keysSpawned++;
                Vector2 o = Random.insideUnitCircle.normalized * 0.9f;
                Drop(LootKind.Key, 4, null, 1, p + new Vector3(o.x, 0, o.y), false);
            }
            return it;
        }

        static long CellKey(Vector3 p) => ((long)Mathf.FloorToInt(p.x / GridCell) << 32) ^ (uint)Mathf.FloorToInt(p.z / GridCell);

        void SpawnChests()
        {
            var land = b.land;
            int n = Mathf.RoundToInt(TotalPlayers * 0.8f);
            for (int i = 0; i < n; i++)
            {
                Vector3 p; Quaternion q = Quaternion.Euler(0, (float)rng.NextDouble() * 360, 0);
                if (land.houses.Count > 0 && rng.NextDouble() < 0.55)
                {
                    var h = land.houses[rng.Next(land.houses.Count)];
                    if (h.vault) continue;
                    p = InHouse(h, 1.8f);
                    q = h.rot * Quaternion.Euler(0, rng.Next(4) * 90, 0);
                }
                else p = RandomGround(land.PlayHalf * 0.9f);
                var c = new LootChest { pos = p, rot = q, bonus = rng.NextDouble() < 0.15 ? 2 : 1 };
                var go = new GameObject("chest");
                go.transform.SetParent(lootRoot, false);
                go.transform.SetPositionAndRotation(p, q);
                go.AddComponent<MeshFilter>().sharedMesh = LootModels.Chest(false);
                go.AddComponent<MeshRenderer>().sharedMaterial = Mats.Lit;
                var bc = go.AddComponent<BoxCollider>(); bc.center = new Vector3(0, 0.45f, 0); bc.size = new Vector3(1.3f, 0.9f, 0.8f);
                c.go = go;
                chests.Add(c);
            }
        }

        void SpawnVehicles()
        {
            var land = b.land;
            int ground = Mathf.Max(6, TotalPlayers / 7), air = Mathf.Max(2, TotalPlayers / 25);
            for (int i = 0; i < ground + air; i++)
            {
                bool isAir = i >= ground;
                var f = Factions.All[rng.Next(Factions.All.Length)];
                var def = isAir ? f.air : rng.NextDouble() < 0.35 ? f.tank : f.transport;
                Vector3 p; Vector3 dir;
                if (!isAir && land.RandomRoadSpot(rng, out p, out dir)) { }
                else { p = RandomGround(land.PlayHalf * 0.85f); dir = Quaternion.Euler(0, (float)rng.NextDouble() * 360, 0) * Vector3.forward; }
                if (Physics.CheckSphere(p + Vector3.up * 2.5f, isAir ? 6 : 4, (1 << Layers.Vehicle) | (1 << Layers.Default), QueryTriggerInteraction.Ignore)) continue;
                p.y = land.Top(p.x, p.z) + (isAir ? 1.2f : 1f);
                b.SpawnVehicle(def, f, -1, p, Quaternion.LookRotation(dir));
            }
        }

        readonly List<Vector3> dropSpots = new List<Vector3>();

        /// <summary>Where a bot lands: usually by a building, sometimes in the open, and spread out - a few hot spots,
        /// but mostly 55 m or more from anyone else, so the first minutes are about loot rather than a brawl.</summary>
        Vector3 BotDropSpot()
        {
            var land = b.land;
            Vector3 best = Vector3.zero; float bestGap = -1;
            bool hot = rng.NextDouble() < 0.15;          // some bots go where the others are
            for (int i = 0; i < 12; i++)
            {
                Vector3 p;
                if (land.houses.Count > 0 && rng.NextDouble() < 0.6)
                {
                    var h = land.houses[rng.Next(land.houses.Count)];
                    if (h.vault) continue;
                    Vector2 o = Random.insideUnitCircle * 10;
                    p = h.doorOut + new Vector3(o.x, 0, o.y);
                }
                else p = RandomGround(land.PlayHalf * 0.88f);
                float gap = float.MaxValue;
                foreach (var s in dropSpots) gap = Mathf.Min(gap, (s - p).magnitude);
                if (hot) gap = -gap;
                if (gap > bestGap) { bestGap = gap; best = p; }
                if (!hot && gap > 70) break;
            }
            dropSpots.Add(best);
            return best;
        }

        // ── the zone wall ────────────────────────────────────────────────
        void BuildWall()
        {
            const int seg = 96;
            var m = new Mesh { name = "zoneWall" };
            var v = new List<Vector3>(); var t = new List<int>(); var c = new List<Color>(); var uv = new List<Vector2>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2;
                Vector3 d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v.Add(d + Vector3.down * 80); v.Add(d + Vector3.up * 320);
                c.Add(new Color(1, 1, 1, 0.9f)); c.Add(new Color(1, 1, 1, 0f));
                uv.Add(new Vector2(i * 0.5f, 0)); uv.Add(new Vector2(i * 0.5f, 1));
            }
            for (int i = 0; i < seg; i++)
            {
                int a = i * 2, bb = a + 1, cc = a + 2, dd = a + 3;
                t.Add(a); t.Add(bb); t.Add(cc); t.Add(cc); t.Add(bb); t.Add(dd);     // inside
                t.Add(a); t.Add(cc); t.Add(bb); t.Add(cc); t.Add(dd); t.Add(bb);     // outside
            }
            m.SetVertices(v); m.SetColors(c); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.bounds = new Bounds(Vector3.zero, new Vector3(4000, 1000, 4000));
            var go = new GameObject("zone");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            wallMat = Mats.Additive(new Color(0.35f, 0.55f, 1f, 0.32f), false);
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = wallMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            wall = go.transform;
        }

        // ── the rules, every frame (host / single player) ────────────────
        public void Rules(float dt)
        {
            if (phase == Phase.Drop) DropTick(dt);
            else
            {
                playTime += dt;
                ZoneTick(dt);
                if (!bossSpawned && playTime >= bossAt) SpawnBoss();
            }
            pickT -= dt;
            if (pickT <= 0) { pickT = 0.15f; AutoPickups(); }
            CountAlive();
            Net.NetHost.RoyaleTick(dt);
            if (phase == Phase.Play && AliveTeams <= 1 && !b.over)
            {
                int winner = -1;
                foreach (var p in b.people) if (!p.boss && (p.Alive || !p.dropped)) { winner = p.team; break; }
                foreach (var p in b.people) if (!p.boss && p.team == winner) p.placement = 1;
                var wp = winner >= 0 ? FirstOf(winner) : null;
                b.End(winner, wp != null ? (TeamSize(winner) > 1 ? $"{TeamNames(winner)} are the last ones standing" : $"{wp.name} is the last one standing") : "Nobody survived");
            }
        }

        Participant FirstOf(int team) { foreach (var p in b.people) if (p.team == team && !p.boss) return p; return null; }
        int TeamSize(int team) { int n = 0; foreach (var p in b.people) if (p.team == team && !p.boss) n++; return n; }
        string TeamNames(int team) { var s = new List<string>(); foreach (var p in b.people) if (p.team == team && !p.boss) s.Add(p.name); return string.Join(" and ", s); }

        void CountAlive()
        {
            var seen = new HashSet<int>();
            int players = 0;
            foreach (var p in b.people)
            {
                if (p.boss) continue;
                bool alive = p.Alive || (phase == Phase.Drop && !p.dropped && p.placement == 0);
                if (!alive) continue;
                players++; seen.Add(p.team);
            }
            AliveTeams = seen.Count; AlivePlayers = players;
        }

        void DropTick(float dt)
        {
            dropT -= dt;
            float gone = DropTime - dropT;
            foreach (var p in b.people)
            {
                if (p.dropped || p.boss) continue;
                bool go;
                if (p.isPlayer) go = playerWantsJump || dropT <= 0;
                else if (p.remote) go = p.dropAt != Vector3.zero && (remoteJump || dropT <= 0);
                else go = jumpAt.TryGetValue(p, out float at) && gone >= at;
                if (!go) continue;
                if (p.isPlayer) p.dropAt = playerPicked ? dropPickPos : RandomGround(b.land.PlayHalf * 0.6f);
                if (p.dropAt == Vector3.zero) p.dropAt = p.Human ? RandomGround(b.land.PlayHalf * 0.6f) : BotDropSpot();
                Jump(p);
            }
            bool all = true;
            foreach (var p in b.people) if (!p.boss && !p.dropped) all = false;
            if (all && dropT <= 0) StartPlay();
        }

        public bool remoteJump;

        void Jump(Participant p)
        {
            var land = b.land;
            Vector3 at = p.dropAt;
            at.x = Mathf.Clamp(at.x, -land.PlayHalf + 20, land.PlayHalf - 20); at.z = Mathf.Clamp(at.z, -land.PlayHalf + 20, land.PlayHalf - 20);
            float gy = land.Top(at.x, at.z);
            Vector2 off = Random.insideUnitCircle * (p.Human ? 0 : 30);
            Vector3 pos = new Vector3(at.x - 40 + off.x, gy + DropAlt, at.z - 40 + off.y);
            var cls = ClassFor(p);
            var u = b.SpawnUnit(p, cls, null, pos, 45);
            p.dropped = true;
            u.StartGlide();
            if (p.isPlayer) { b.control.OnSpawned(u); HUD.Message("Steer your grav-chute to the ground", new Color(0.6f, 0.85f, 1f), 4); }
        }

        void StartPlay()
        {
            phase = Phase.Play;
            zonePhase = 0; shrinking = false;
            zoneR = startR; zoneC = Vector2.zero;
            PickNext();
            zoneTimer = Phases[0].wait;
            bossAt = Random.Range(150f, 210f);
            HUD.Message("Everyone has jumped - find weapons and stay inside the zone", Color.white, 5);
            Net.NetHost.Text("Everyone has jumped - find weapons and stay inside the zone", Color.white, 5, null);
        }

        void PickNext()
        {
            var land = b.land;
            nextR = Phases[zonePhase].r;
            float room = Mathf.Max(0, zoneR - nextR) * 0.92f;
            for (int i = 0; i < 40; i++)
            {
                Vector2 c = zoneC + Random.insideUnitCircle * room;
                float lim = land.PlayHalf - nextR * 0.55f - 10;
                if (Mathf.Abs(c.x) > lim || Mathf.Abs(c.y) > lim) continue;
                Vector3 w3 = new Vector3(c.x, 0, c.y);
                if (!land.nav.Walkable(w3)) continue;
                if (land.LiquidAt(c.x, c.y, out _, out bool lava) && lava) continue;
                nextC = c;
                return;
            }
            nextC = Vector2.MoveTowards(zoneC, Vector2.zero, room * 0.5f);
        }

        void ZoneTick(float dt)
        {
            zoneTimer -= dt;
            if (!shrinking && zoneTimer <= 0 && zonePhase < Phases.Length)
            {
                shrinking = true; fromC = zoneC; fromR = zoneR; zoneTimer = Phases[zonePhase].shrink;
                Say("The zone is closing in!", new Color(0.55f, 0.75f, 1f), 4, "alarm");
            }
            else if (shrinking)
            {
                float total = Phases[zonePhase].shrink;
                float t = Mathf.Clamp01(1 - zoneTimer / total);
                zoneC = Vector2.Lerp(fromC, nextC, t); zoneR = Mathf.Lerp(fromR, nextR, t);
                if (zoneTimer <= 0)
                {
                    shrinking = false;
                    zoneC = nextC; zoneR = nextR;
                    zonePhase++;
                    if (zonePhase < Phases.Length) { PickNext(); zoneTimer = Phases[zonePhase].wait * WaitMul; Say("New zone marked on the map", Color.white, 3, null); }
                    else { zonePhase = Phases.Length; zoneTimer = 99999; }
                }
            }
            // damage outside, faster each phase
            zoneTick -= dt;
            if (zoneTick <= 0)
            {
                zoneTick = 0.5f;
                float dmg = Phases[Mathf.Clamp(zonePhase, 0, Phases.Length - 1)].dmg * (zonePhase >= Phases.Length ? 1.5f : 1f);
                foreach (var u in b.units)
                {
                    if (!u || !u.Alive || u.part.boss) continue;
                    Vector3 p = u.InVehicle ? u.vehicle.transform.position : u.transform.position;
                    if (!OutsideZone(p)) continue;
                    u.TakeDamage(new DamageInfo { amount = u.maxHp * dmg * 0.5f, type = DmgType.Environment, team = -1, point = p });
                }
            }
        }

        public void Say(string text, Color c, float t, string sound)
        {
            HUD.Message(text, c, t);
            if (sound != null) Sfx.Play2D(sound, 0.6f);
            Net.NetHost.Text(text, c, t, sound);
        }

        // ── deaths and placements ────────────────────────────────────────
        public void OnUnitKilled(Unit victim, Unit killer)
        {
            var vp = victim.part;
            if (vp.boss) { BossKilled(victim, killer); return; }
            SpillInventory(victim);
            CountAlive();
            // the whole team is out: they finish behind everyone still standing
            bool teamAlive = false;
            foreach (var p in b.people) if (p.team == vp.team && !p.boss && p.Alive) teamAlive = true;
            if (!teamAlive)
                foreach (var p in b.people) if (p.team == vp.team && !p.boss && p.placement == 0) p.placement = AliveTeams + 1;
            if (killer && killer.part.isPlayer && killer != victim) HUD.Message($"{AlivePlayers} left", UI.Dim, 2);
        }

        /// <summary>Everything a fallen player carried lands where they fell.</summary>
        void SpillInventory(Unit u)
        {
            Vector3 c = u.transform.position + Vector3.up * 0.1f;
            float y = b.GroundY(c);
            int k = 0;
            Vector3 Spot() { float a = k++ * 2.1f; Vector3 p = c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (0.7f + k * 0.25f); p.y = Mathf.Max(y, b.GroundY(p)) + 0.04f; return p; }
            foreach (var w in u.weapons)
                if (w != null && w.def.tier >= 0 && w.def.kind != WKind.Repair) Drop(LootKind.Weapon, w.def.tier, w.def, 1, Spot(), false);
            if (u.medkits > 0) Drop(LootKind.Medkit, 1, null, u.medkits, Spot(), false);
            if (u.shields > 0) Drop(LootKind.Shield, 1, null, u.shields, Spot(), false);
            for (int i = 0; i < u.keys; i++) Drop(LootKind.Key, 4, null, 1, Spot(), false);
            u.medkits = u.shields = u.keys = 0;
        }

        // ── picking things up ────────────────────────────────────────────
        public LootItem NearestLoot(Vector3 p, float r, bool weaponsOnly = false, bool suppliesOnly = false)
        {
            LootItem best = null; float bd = r * r;
            int cx = Mathf.FloorToInt(p.x / GridCell), cz = Mathf.FloorToInt(p.z / GridCell);
            int span = Mathf.CeilToInt(r / GridCell);
            for (int dz = -span; dz <= span; dz++)
                for (int dx = -span; dx <= span; dx++)
                {
                    long key = ((long)(cx + dx) << 32) ^ (uint)(cz + dz);
                    if (!grid.TryGetValue(key, out var list)) continue;
                    foreach (var it in list)
                    {
                        if (it.taken) continue;
                        if (weaponsOnly && it.kind != LootKind.Weapon) continue;
                        if (suppliesOnly && it.kind == LootKind.Weapon) continue;
                        Vector3 d = it.pos - p;
                        if (Mathf.Abs(d.y) > 2.6f) continue;
                        d.y = 0;
                        float d2 = d.sqrMagnitude;
                        if (d2 < bd) { bd = d2; best = it; }
                    }
                }
            return best;
        }

        /// <summary>Items in range (for bots choosing what to go for).</summary>
        public void LootAround(Vector3 p, float r, List<LootItem> into)
        {
            into.Clear();
            int cx = Mathf.FloorToInt(p.x / GridCell), cz = Mathf.FloorToInt(p.z / GridCell);
            int span = Mathf.CeilToInt(r / GridCell);
            for (int dz = -span; dz <= span; dz++)
                for (int dx = -span; dx <= span; dx++)
                {
                    long key = ((long)(cx + dx) << 32) ^ (uint)(cz + dz);
                    if (!grid.TryGetValue(key, out var list)) continue;
                    foreach (var it in list) if (!it.taken && (it.pos - p).sqrMagnitude < r * r) into.Add(it);
                }
        }

        public LootChest NearestChest(Vector3 p, float r)
        {
            LootChest best = null; float bd = r * r;
            foreach (var c in chests)
            {
                if (c.opened) continue;
                Vector3 d = c.pos - p; if (Mathf.Abs(d.y) > 2.6f) continue; d.y = 0;
                if (d.sqrMagnitude < bd) { bd = d.sqrMagnitude; best = c; }
            }
            return best;
        }

        public LandWorld.VaultSite VaultNear(Vector3 p, float r)
        {
            foreach (var v in b.land.vaults) if (!v.open && (v.lockPoint - p).sqrMagnitude < r * r) return v;
            return null;
        }

        /// <summary>Why this item can't be taken right now (full pockets, nothing to reload), or null.</summary>
        public string CantTake(Unit u, LootItem it)
        {
            switch (it.kind)
            {
                case LootKind.Medkit: return u.medkits >= Unit.MaxMedkits ? "Medkits full" : null;
                case LootKind.Shield: return u.shields >= Unit.MaxShields ? "Shield cells full" : null;
                case LootKind.Ammo:
                    foreach (var w in u.weapons) if (w != null && !w.def.UsesHeat && w.def.clip > 0 && w.ammo < w.def.ammo) return null;
                    return "No weapon needs ammo";
                case LootKind.Gear: return u.cls == null || u.cls.equip == Equip.None ? "No equipment to refill" : u.equipLeft >= 6 ? "Equipment full" : null;
                default: return null;
            }
        }

        /// <summary>Take an item: weapons fill the empty slot or swap with the one in hand (which drops), the rest goes in the pockets.</summary>
        public bool Take(Unit u, LootItem it)
        {
            if (it == null || it.taken || u == null || !u.Alive || u.InVehicle) return false;
            if (CantTake(u, it) != null) return false;
            switch (it.kind)
            {
                case LootKind.Weapon:
                    int slot = u.weapons[1] == null ? 1 : u.cur;
                    if (u.weapons[0] != null && u.weapons[0].def.kind == WKind.Pistol && u.weapons[0].def.tier < 0 && u.weapons[1] != null && u.weapons[1].def.tier >= 0) slot = 0;   // the starting pistol goes first
                    var old = u.weapons[slot];
                    if (old != null && old.def.tier >= 0 && old.def.kind != WKind.Repair) Drop(LootKind.Weapon, old.def.tier, old.def, 1, it.pos, false);
                    u.weapons[slot] = new WeaponState(it.weapon);
                    u.cur = slot;
                    u.rig.SetWeapon(it.weapon);
                    break;
                case LootKind.Medkit: u.medkits = Mathf.Min(Unit.MaxMedkits, u.medkits + it.count); break;
                case LootKind.Shield: u.shields = Mathf.Min(Unit.MaxShields, u.shields + it.count); break;
                case LootKind.Ammo: foreach (var w in u.weapons) if (w != null && w.def.clip > 0) { w.ammo = w.def.ammo; if (w.clip == 0) w.StartReload(); } break;
                case LootKind.Gear: u.equipLeft = Mathf.Min(6, u.equipLeft + 2); break;
                case LootKind.Key: u.keys++; break;
            }
            Remove(it);
            if (u.part.isPlayer)
            {
                Sfx.Play2D(it.kind == LootKind.Key ? "award" : "ui2", 0.6f);
                HUD.Message((it.kind == LootKind.Weapon ? Tiers.Names[it.tier] + " " : "") + it.Name + (it.kind == LootKind.Key ? " - open a vault with it" : ""), Tiers.Of(it.tier), 2);
            }
            else if (u.part.remote) Net.NetHost.Text((it.kind == LootKind.Weapon ? Tiers.Names[it.tier] + " " : "") + it.Name, Tiers.Of(it.tier), 2, "ui2");
            return true;
        }

        /// <summary>Wi-Fi guest: the host says this item was taken.</summary>
        public void ClientRemove(int id) { if (byId.TryGetValue(id, out var it) && !it.taken) Remove(it); }
        public LootItem ById(int id) => byId.TryGetValue(id, out var it) ? it : null;

        /// <summary>Wi-Fi guest: the host's chests (where they stand, whether they are open).</summary>
        public void ClientChest(Vector3 p, Quaternion q, bool opened)
        {
            var c = new LootChest { pos = p, rot = q, opened = opened };
            var go = new GameObject("chest");
            go.transform.SetParent(lootRoot, false);
            go.transform.SetPositionAndRotation(p, q);
            go.AddComponent<MeshFilter>().sharedMesh = LootModels.Chest(opened);
            go.AddComponent<MeshRenderer>().sharedMaterial = Mats.Lit;
            var bc = go.AddComponent<BoxCollider>(); bc.center = new Vector3(0, 0.45f, 0); bc.size = new Vector3(1.3f, 0.9f, 0.8f);
            c.go = go;
            chests.Add(c);
        }

        public void ClientChestOpened(int i)
        {
            if (i < 0 || i >= chests.Count || chests[i].opened) return;
            var c = chests[i];
            c.opened = true;
            c.go.GetComponent<MeshFilter>().sharedMesh = LootModels.Chest(true);
            Fx.Burst(c.pos + Vector3.up * 0.8f, new Color(0.5f, 0.9f, 1f), 18, 3, 0.3f, 0.6f);
        }

        void Remove(LootItem it)
        {
            it.taken = true;
            byId.Remove(it.id);
            if (it.go) Destroy(it.go);
            if (grid.TryGetValue(it.cell, out var list)) list.Remove(it);
            loot.Remove(it);
            shown.Remove(it);
            Net.NetHost.LootGone(it.id);
        }

        public void OpenChest(Unit u, LootChest c)
        {
            if (c.opened) return;
            c.opened = true;
            c.go.GetComponent<MeshFilter>().sharedMesh = LootModels.Chest(true);
            Fx.Burst(c.pos + Vector3.up * 0.8f, new Color(0.5f, 0.9f, 1f), 18, 3, 0.3f, 0.6f);
            Sfx.Play("repair", c.pos, 0.7f, 1.3f);
            Vector3 fwd = c.rot * Vector3.forward;
            Vector3 Spot(float side) { Vector3 p = c.pos + fwd * 1.1f + c.rot * Vector3.right * side; p.y = b.GroundY(p) + 0.04f; if (Mathf.Abs(p.y - c.pos.y) > 1) p.y = c.pos.y + 0.04f; return p; }
            int wt = LootTable.RollTier(rng, c.bonus);
            Drop(LootKind.Weapon, wt, LootTable.Weapon(wt, rng), 1, Spot(-0.6f));
            Drop(rng.NextDouble() < 0.5 ? LootKind.Shield : LootKind.Medkit, 2, null, 1 + rng.Next(2), Spot(0.6f));
            Drop(rng.NextDouble() < 0.6 ? LootKind.Ammo : LootKind.Gear, 1, null, 1, Spot(0f));
            Net.NetHost.ChestOpened(chests.IndexOf(c));
        }

        public bool TryOpenVault(Unit u, LandWorld.VaultSite v)
        {
            if (v == null || v.open || u.keys <= 0) return false;
            u.keys--;
            b.land.OpenVault(v);
            Sfx.Play("alarm", v.lockPoint, 1f, 0.8f);
            Say($"{u.displayName} opened the vault at {b.land.RegionName(v.lockPoint)}!", new Color(1f, 0.8f, 0.3f), 5, "award");
            Net.NetHost.VaultOpened(b.land.vaults.IndexOf(v));
            return true;
        }

        /// <summary>Supplies are picked up by walking over them (if there is room); guns need E so you choose.</summary>
        void AutoPickups()
        {
            foreach (var p in b.people)
            {
                if (!p.Human || !p.Alive || p.unit.InVehicle) continue;
                var it = NearestLoot(p.unit.transform.position, 1.5f, false, true);
                if (it != null && it.kind != LootKind.Weapon && CantTake(p.unit, it) == null) Take(p.unit, it);
            }
        }

        // ── the boss ─────────────────────────────────────────────────────
        public static HeroDef BossDef
        {
            get
            {
                if (bossDef != null) return bossDef;
                var f = Factions.All[2];
                HeroDef src = f.heroes[0];
                foreach (var h in f.heroes) if (h.kind == HeroKind.Juggernaut) src = h;
                var w = src.weapon.Copy();
                w.damage *= 1.5f; w.splashDmg *= 1.4f; w.name = "Siege Cannon"; w.tier = -1;
                bossDef = new HeroDef
                {
                    name = "Dreadnought Warlord", title = "Boss", kind = HeroKind.Juggernaut, weapon = w, abilityName = "BARRAGE",
                    hp = 5000, speed = 4.6f, scale = 2.8f, main = new Color(0.25f, 0.22f, 0.24f), accent = new Color(0.9f, 0.3f, 0.15f), glow = new Color(1f, 0.35f, 0.15f),
                    blurb = "A walking fortress. Kill it for legendary loot.",
                };
                return bossDef;
            }
        }

        void SpawnBoss()
        {
            bossSpawned = true;
            if (boss == null) return;
            var land = b.land;
            // a region whose centre is inside the next zone, otherwise the nearest one to it
            int best = -1; float bd = float.MaxValue;
            for (int i = 0; i < land.def.regions.Length; i++)
            {
                Vector3 rc = land.RegionCentre(i);
                float d = (new Vector2(rc.x, rc.z) - nextC).magnitude - nextR * 0.8f + Random.Range(0, 120f);
                if (d < bd) { bd = d; best = i; }
            }
            Vector3 c = land.RegionCentre(best);
            Vector2 o = Random.insideUnitCircle * 30;
            Vector3 p = land.nav.SnapPoint(c + new Vector3(o.x, 0, o.y));
            p.y = land.Top(p.x, p.z) + 0.3f;
            bossLair = p; bossPlace = land.def.regions[best].name;
            var u = b.SpawnUnit(boss, null, BossDef, p, Random.Range(0, 360));
            Fx.Explosion(p, 2, true);
            Say($"A DREADNOUGHT WARLORD has landed at {bossPlace}! Kill it for legendary loot", new Color(1f, 0.55f, 0.25f), 7, "alarm");
        }

        void BossKilled(Unit boss, Unit killer)
        {
            bossDead = true;
            Vector3 c = boss.transform.position;
            float y = b.GroundY(c);
            Vector3 Spot(int k) { float a = k * 1.25f; Vector3 p = c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 2.4f; p.y = Mathf.Max(y, b.GroundY(p)) + 0.04f; return p; }
            for (int i = 0; i < 3; i++) Drop(LootKind.Weapon, 4, LootTable.Weapon(4, rng), 1, Spot(i));
            Drop(LootKind.Shield, 3, null, 3, Spot(3));
            Drop(LootKind.Medkit, 3, null, 3, Spot(4));
            Fx.Explosion(c, 2.5f, true);
            Say(killer ? $"{killer.displayName} brought down the Dreadnought Warlord - legendary loot at {bossPlace}!" : "The Dreadnought Warlord has fallen!", new Color(1f, 0.8f, 0.3f), 6, "award");
            if (killer) b.AddPoints(killer.part, 10);
        }

        // ── the player's class / loadout ─────────────────────────────────
        /// <summary>
        /// What a player lands with. Classes on: the class's health, speed, ability and equipment (one to start), with only the
        /// race's sidearm (engineers keep their repair tool). Classes off: everyone is a Survivor with a sidearm and nothing else.
        /// Every other weapon is found.
        /// </summary>
        public ClassDef ClassFor(Participant p)
        {
            var f = b.Faction(p.team);
            if (p.faction >= 0) f = Factions.Get(p.faction);
            var pistol = LootTable.Secondary(f);
            ClassDef c;
            if (b.cfg.royaleClasses)
            {
                var src = f.classes[Mathf.Clamp(p.royaleClass, 0, 4)];
                c = new ClassDef { name = src.name, blurb = src.blurb, role = src.role, hp = src.hp, speed = src.speed, primary = pistol,
                                   secondary = src.role == Role.Engineer ? src.secondary : null, equip = src.equip, equipCount = 1, special = src.special };
            }
            else
            {
                var src = f.classes[0];
                c = new ClassDef { name = "Survivor", blurb = "Lands with a sidearm. Everything else is out there.", role = Role.Assault, hp = 300, speed = 6.2f,
                                   primary = pistol, equip = Equip.Grenade, equipCount = 0, special = Special.None };
            }
            return c;
        }

        // ── visuals: the wall, which loot is drawn, vault doors ──────────
        void Update()
        {
            if (!b) return;
            if (wall)
            {
                float r = Mathf.Max(1, zoneR);
                wall.position = new Vector3(zoneC.x, 0, zoneC.y);
                wall.localScale = new Vector3(r, 1, r);
                wall.gameObject.SetActive(phase == Phase.Play);
                if (wallMat) wallMat.mainTextureOffset = new Vector2(Time.time * 0.05f, 0);
            }
            cullT -= Time.deltaTime;
            if (cullT <= 0 && Game.Cam)
            {
                cullT = 0.25f;
                Vector3 cp = Game.Cam.transform.position;
                var me = b.control != null ? b.control.Me : null;
                if (me != null) cp = me.transform.position;
                foreach (var it in shown) if (it.go) { var mr = it.go.GetComponent<MeshRenderer>(); if ((it.pos - cp).sqrMagnitude > ShowRange * ShowRange * 1.2f) mr.enabled = false; }
                shown.RemoveAll(it => !it.go || !it.go.GetComponent<MeshRenderer>().enabled);
                int cx = Mathf.FloorToInt(cp.x / GridCell), cz = Mathf.FloorToInt(cp.z / GridCell), span = Mathf.CeilToInt(ShowRange / GridCell);
                for (int dz = -span; dz <= span; dz++)
                    for (int dx = -span; dx <= span; dx++)
                    {
                        long key = ((long)(cx + dx) << 32) ^ (uint)(cz + dz);
                        if (!grid.TryGetValue(key, out var list)) continue;
                        foreach (var it in list)
                        {
                            if (it.taken || !it.go) continue;
                            var mr = it.go.GetComponent<MeshRenderer>();
                            if (!mr.enabled && (it.pos - cp).sqrMagnitude < ShowRange * ShowRange) { mr.enabled = true; shown.Add(it); }
                        }
                    }
            }
            // shown loot turns slowly so it catches the eye
            float spin = Time.deltaTime * 40;
            foreach (var it in shown) if (it.go && it.kind != LootKind.Weapon) it.go.transform.Rotate(0, spin, 0, Space.World);
        }

        // ── for the HUD ──────────────────────────────────────────────────
        public string ZoneStatus
        {
            get
            {
                if (phase == Phase.Drop) return b.player.dropped ? $"OTHERS JUMPING  {Mathf.CeilToInt(Mathf.Max(0, dropT))}" : $"JUMP IN {Mathf.CeilToInt(Mathf.Max(0, dropT))}";
                if (zonePhase >= Phases.Length) return "FINAL ZONE";
                string t = $"{Mathf.FloorToInt(zoneTimer / 60)}:{Mathf.FloorToInt(zoneTimer % 60):00}";
                return shrinking ? $"ZONE CLOSING  {t}" : $"ZONE SHRINKS IN  {t}";
            }
        }
    }
}
