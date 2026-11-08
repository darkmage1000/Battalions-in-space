using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    /// <summary>
    /// Battle royale bots: land with a sidearm, loot the nearest buildings and chests (better guns, medkits, shield cells),
    /// heal when safe, fight with the gun that suits the range, move into the next zone in time (by vehicle when far),
    /// open vaults with a key, and take on the boss only when well armed. The boss guards its landing site.
    /// </summary>
    public partial class BotBrain
    {
        enum RGoal { None, Loot, Chest, Vault, Zone, Roam, Boss, High, Mate, Station }
        RGoal rGoal;
        LootItem rItem;
        LootChest rChest;
        LandWorld.VaultSite rVault;
        float rGoalT, rScanT, rHealT, rStuckT, rProgD = 1e9f, rProgT;
        Vector3 rRoam;
        readonly HashSet<int> rSkip = new HashSet<int>();
        readonly HashSet<LootChest> rSkipChests = new HashSet<LootChest>();
        static readonly List<LootItem> rScratch = new List<LootItem>();

        float GunValue(WeaponState w) => w == null || OutOfAmmo(w) ? 0 : LootTable.Value(w.def);
        float BestGun => Mathf.Max(GunValue(u.weapons[0]), GunValue(u.weapons[1]));
        float WorstGun => u.weapons[1] == null ? 0 : Mathf.Min(GunValue(u.weapons[0]), GunValue(u.weapons[1]));
        bool Armed => (u.weapons[0] != null && u.weapons[0].def.tier >= 0) || (u.weapons[1] != null && u.weapons[1].def.tier >= 0);
        bool Strong => Armed && BestGun > 200 && u.hp > u.maxHp * 0.55f;
        bool Supplied => Armed && u.medkits + u.shields >= 2;
        float aggression;

        /// <summary>Is this bot running out of time to reach the next zone? (distance to its edge against the time left)</summary>
        bool LateForZone(Vector3 pos)
        {
            var r = b.royale;
            if (r.phase != Royale.Phase.Play || r.zonePhase >= Royale.Phases.Length) return false;
            float toEdge = (new Vector2(pos.x, pos.z) - r.nextC).magnitude - r.nextR;
            if (toEdge <= 0) return false;
            float left = r.shrinking ? r.zoneTimer : r.zoneTimer + Royale.Phases[r.zonePhase].shrink * 0.6f;
            return left < toEdge / 6.5f + 15;
        }

        static float Fit(WeaponDef w, float d, bool vehicle)
        {
            switch (w.kind)
            {
                case WKind.Shotgun: return d < 14 ? 1.7f : d < 24 ? 0.7f : 0.08f;
                case WKind.Sniper: return d > 45 ? 1.6f : d > 20 ? 0.8f : 0.35f;
                case WKind.Rocket: return vehicle ? 2.5f : d > 14 && d < 70 ? 0.75f : 0.15f;
                case WKind.Beam: return d < w.range ? 1.6f : 0f;
                case WKind.Repair: return 0;
                case WKind.Pistol: return d < 60 ? 0.85f : 0.5f;
                default: return d < 80 ? 1f : 0.55f;
            }
        }

        static float PreferFor(WeaponDef w)
        {
            switch (w.kind)
            {
                case WKind.Shotgun: return 7; case WKind.Sniper: return 70; case WKind.Rocket: return 35; case WKind.Beam: return 9; case WKind.Pistol: return 16;
                default: return 24;
            }
        }

        void RoyaleInfantry(float dt)
        {
            var r = b.royale;
            if (u.part.boss) { BossThink(dt); return; }
            think -= dt; seeT -= dt; strafeT -= dt; abilityT -= dt; grenadeT -= dt; enterT -= dt; rGoalT -= dt; rScanT -= dt; rHealT -= dt;
            Vector3 pos = u.transform.position;
            if (u.gliding)
            {
                Vector3 to = u.part.dropAt - pos; to.y = 0;
                Vector3 gm = to.magnitude > 3 ? to.normalized : Vector3.zero;
                // duos: a bot following its leader keeps beside the leader's chute all the way down
                var lead = r.Follower(u.part) ? r.LeaderOf(u.part).unit : null;
                if (lead && lead.Alive && lead.gliding) gm = Royale.FollowMove(u, lead, r.FollowSpot(lead));
                u.input = new UnitInput { move = gm, aimPoint = u.Eye + u.transform.forward * 30 };
                return;
            }
            if (u.knocked)
            {
                if (think <= 0) { think = 0.4f; var kt = FindTarget(u.Eye, 40, false); if (kt != null) target = kt; }
                KnockedThink(pos);
                return;
            }

            if (think <= 0)
            {
                think = Random.Range(0.25f, 0.4f);
                // how far a bot looks for a fight: just landed or still on the starting sidearm = only what is close
                // (loot first); armed = medium range; a sniper further. Whoever shoots it gets answered (OnHurt).
                float range = u.Weapon.def.kind == WKind.Sniper ? 170 : u.Weapon.def.kind == WKind.Shotgun ? 40 : 75;
                if (aggression <= 0) aggression = Random.Range(0.35f, 1f);    // some bots pick fights, others keep their heads down
                range *= aggression;
                if (!Armed || Time.time - u.spawnTime < 35) range = Mathf.Min(range, 26);
                else if (FloorHere is LandWorld.UpperFloor hf && hf.climb.tower) range *= 1.4f;   // the view from up there
                var t = FindTarget(u.Eye, range, false);
                // leave the boss alone unless well armed (or it is already after us)
                if (t is Unit tu && tu.part.boss && !Strong && Time.time - u.lastHurt > 2) t = null;
                if (t != null) { if (t != target) trackTime = 0; target = t; seeT = 1.4f; }
                else if (seeT <= 0) target = null;
            }
            if (target != null && (target is Object to0 && !to0 || !target.Alive)) target = null;
            DuoTargetCheck(pos);

            Vector3 move = Vector3.zero;
            bool fire = false, aim = false, crouch = false, sprint = false;
            Vector3 aimPoint = u.Eye + u.transform.forward * 30;

            if (wantVehicle)
            {
                if (!wantVehicle.CanEnter(u) || target != null) wantVehicle = null;
                else
                {
                    Vector3 to = wantVehicle.transform.position - pos; to.y = 0;
                    if (to.magnitude < wantVehicle.radius + 3.5f) { wantVehicle.Enter(u); wantVehicle = null; objT = 0; return; }
                    move = Toward(pos, wantVehicle.transform.position); sprint = true;
                    aimPoint = wantVehicle.transform.position;
                }
            }
            else if (target != null) RoyaleFight(dt, pos, ref move, ref aimPoint, ref fire, ref aim, ref crouch, ref sprint);
            else
            {
                // patch up when nothing is shooting
                if (u.Using) { u.input = new UnitInput { aimPoint = u.input.aimPoint, crouch = true }; return; }
                if (rHealT <= 0 && Time.time - u.lastHurt > 2.5f)
                {
                    rHealT = 1.2f;
                    if ((u.hp < u.maxHp * 0.8f && u.medkits > 0) || (u.armor < Unit.MaxArmor - 70 && u.shields > 0)) { if (u.UseBest()) return; }
                }
                if (rGoalT <= 0 || GoalDone(pos)) PickRoyaleGoal(pos);
                Vector3 goal = GoalPos(pos);
                Vector3 to = goal - pos; float yd = to.y; to.y = 0;
                float d = to.magnitude;
                if (rGoal == RGoal.Loot && rItem != null && d < 1.9f && Mathf.Abs(yd) < 2.2f)
                {
                    int worst = u.weapons[1] == null ? 1 : GunValue(u.weapons[0]) <= GunValue(u.weapons[1]) ? 0 : 1;
                    if (!r.Take(u, rItem, worst)) rSkip.Add(rItem.id);
                    rItem = null; rGoalT = 0;
                }
                else if (rGoal == RGoal.Chest && rChest != null && d < 2.3f) { r.OpenChest(u, rChest); rChest = null; rGoalT = 0.5f; rScanT = 0; }
                else if (rGoal == RGoal.Vault && rVault != null && d < 3f) { r.TryOpenVault(u, rVault); rVault = null; rGoalT = 0; }
                else if (DuoArrive(d, yd)) { }
                else if (d > 1.2f || (rGoal != RGoal.High && Mathf.Abs(yd) > 2.2f)) { move = ClimbToward(pos, goal); sprint = d > 18 || rGoal == RGoal.Zone; }
                // stuck on the way to an item (a wall, a roof): give it up
                if (rGoal == RGoal.Loot || rGoal == RGoal.Chest) { if (stuckT > 0.8f) rStuckT += dt; if (rStuckT > 4) { if (rItem != null) rSkip.Add(rItem.id); rItem = null; rChest = null; rGoalT = 0; rStuckT = 0; } }
                // any goal: no real progress toward it for 6 s (a wall, a fence, a roof) = give it up and pick another
                float left = LeftToGo(pos, goal, d);
                if (left < rProgD - 2) { rProgD = left; rProgT = 0; }
                else if (left > 2.5f) { rProgT += dt; if (rProgT > 6) { if (rItem != null) rSkip.Add(rItem.id); if (rChest != null) rSkipChests.Add(rChest); rItem = null; rChest = null; rGoalT = 0; rProgT = 0; rProgD = 1e9f; } }
                aimPoint = u.Eye + (move.sqrMagnitude > 0.01f ? move : u.transform.forward) * 30 + Vector3.up * 0.4f;
                TickHigh(pos, d, ref aimPoint, ref crouch);
                // a long way to go: take a vehicle if one is close
                if (enterT <= 0 && d > 200 && !u.IsHero && cClimb == null)
                {
                    enterT = 6;
                    foreach (var v in b.vehicles)
                        if (v && v.def.IsGround && v.CanEnter(u) && v.IsEmpty && (v.transform.position - pos).sqrMagnitude < 45 * 45) { wantVehicle = v; break; }
                }
                if (abilityT <= 0 && u.abilityCd <= 0 && u.cls != null && u.cls.special == Special.Cloak && rGoal == RGoal.Zone) { u.input.ability = true; abilityT = 20; }
            }

            // keep inside the battlefield
            float lim = b.land.PlayHalf - 15;
            if (Mathf.Abs(pos.x) > lim || Mathf.Abs(pos.z) > lim) move = (-pos).normalized;
            if (OutOfAmmo(u.Weapon) && u.weapons[1 - u.cur] != null && !OutOfAmmo(u.weapons[1 - u.cur])) u.input.swap = true;
            // on a climb route or an upper floor the way is known: no feelers (they swerve at parapets and jump bots off ledges)
            if (cClimb == null && FloorHere == null) move = Avoid(pos, move, dt); else lastPos = pos;
            if (u.cls != null && u.cls.special == Special.Jetpack && !u.grounded && move.sqrMagnitude > 0.1f && stuckT > 0.6f) u.input.jump = true;

            u.input.move = Vector3.ClampMagnitude(new Vector3(move.x, 0, move.z), 1);
            u.input.aimPoint = aimPoint;
            u.input.fire = fire;
            u.input.fireDown = fire && Random.value < 0.3f;
            u.input.aim = aim;
            u.input.crouch = crouch;
            u.input.sprint = sprint && !fire;
        }

        void RoyaleFight(float dt, Vector3 pos, ref Vector3 move, ref Vector3 aimPoint, ref bool fire, ref bool aim, ref bool crouch, ref bool sprint)
        {
            var r = b.royale;
            trackTime += dt;
            Vector3 tp = target.AimPoint;
            Vector3 to = tp - pos; to.y = 0;
            float d = to.magnitude;
            Vector3 dir = d > 0.1f ? to / d : u.transform.forward;
            if (strafeT <= 0) { strafeT = Random.Range(0.7f, 2f); strafe = Random.value < 0.5f ? -1 : 1; }
            Vector3 side = Vector3.Cross(Vector3.up, dir) * strafe;
            var w = u.Weapon;
            bool tVeh = target is Vehicle;
            // the right gun for the range
            if (u.weapons[1] != null && Random.value < 0.15f)
            {
                float here = GunValue(w) * Fit(w.def, d, tVeh), other = GunValue(u.weapons[1 - u.cur]) * Fit(u.weapons[1 - u.cur].def, d, tVeh);
                if (other > here * 1.25f) u.input.swap = true;
            }
            float pref = PreferFor(w.def);
            float approach = d > pref * 1.3f ? 1 : d < pref * 0.6f ? -0.7f : 0;
            move = dir * approach + side * 0.75f;
            // the zone comes first when it is about to hurt
            if (r.OutsideZone(pos) || (r.shrinking && r.OutsideNext(pos))) move = Toward(pos, new Vector3(r.nextC.x, pos.y, r.nextC.y)) + side * 0.3f;
            if (w.def.kind == WKind.Sniper) { aim = d > 30; crouch = d > 50; if (d > 40) move = side * 0.2f; }
            float boltSpeed = w.def.Hitscan ? 1e6f : w.def.speed;
            aimPoint = AimWithError(u.Eye, Lead(u.Eye, target, boltSpeed), dt);
            burst -= dt;
            if (burst < -0.45f) burst = Random.Range(0.6f, 1.5f);
            bool heatOk = !w.def.UsesHeat || w.heat < 0.82f;
            fire = burst > 0 && heatOk && d < (w.def.kind == WKind.Sniper ? 260 : w.def.kind == WKind.Shotgun ? 30 : 120);
            if (w.def.kind == WKind.Rocket && !tVeh && d < 10) fire = false;
            if (w.def.kind == WKind.Beam) fire = d < w.def.range;
            if (w.def.kind == WKind.Repair) { fire = false; if (u.weapons[0] != null && u.cur != 0) u.input.swap = true; }
            // grenades, abilities
            if (grenadeT <= 0 && u.cls != null && u.cls.equip == Equip.Grenade && u.equipLeft > 0 && d > 9 && d < 26 && target is Unit)
            {
                grenadeT = Random.Range(6, 14);
                u.input.equip = true;
                aimPoint = tp + Vector3.up * d * 0.12f;
            }
            if (abilityT <= 0 && u.abilityCd <= 0 && u.cls != null && (u.cls.special == Special.Frenzy || u.cls.special == Special.Rally) && d < 30) { u.input.ability = true; abilityT = Random.Range(4, 9); }
            var fl = FloorHere;
            if (fl != null)
            {
                bool zoneCall = r.OutsideZone(pos) || (r.shrinking && r.OutsideNext(pos));
                if (zoneCall) move = ClimbToward(pos, new Vector3(r.nextC.x, pos.y, r.nextC.y));
                else
                {
                    move = KeepOnFloor(pos, move * (fl.climb.tower ? 0.6f : 1f), fl);
                    // behind a parapet: duck between bursts (snipers already lie low)
                    if (fl.climb.tower && w.def.kind != WKind.Sniper && burst < 0 && d > 12) crouch = true;
                }
            }
            // badly hurt and far enough away: back off and heal behind cover
            if (u.hp < u.maxHp * 0.3f && u.medkits > 0 && d > 35 && Time.time - u.lastHurt > 1.5f && !u.Using) { u.UseBest(); fire = false; }
            if (u.Using) { move = Vector3.zero; fire = false; }
        }

        bool GoalDone(Vector3 pos)
        {
            switch (rGoal)
            {
                case RGoal.Loot: return rItem == null || rItem.taken;
                case RGoal.Chest: return rChest == null || rChest.opened;
                case RGoal.Vault: return rVault == null || rVault.open || u.keys == 0;
                case RGoal.Zone: return !b.royale.OutsideNext(pos) && (new Vector2(pos.x, pos.z) - b.royale.nextC).magnitude < b.royale.nextR * 0.75f;
                case RGoal.Boss: return b.royale.boss == null || !b.royale.boss.Alive || !Strong;
                case RGoal.Roam: { Vector3 d = rRoam - pos; d.y = 0; return d.magnitude < 4; }
                case RGoal.High: return HighDone(pos);
                case RGoal.Mate: case RGoal.Station: return DuoGoalDone(pos);
                default: return true;
            }
        }

        Vector3 GoalPos(Vector3 pos)
        {
            switch (rGoal)
            {
                case RGoal.Loot: return rItem != null ? rItem.pos : pos;
                case RGoal.Chest: return rChest != null ? rChest.pos + rChest.rot * Vector3.forward * 1.2f : pos;
                case RGoal.Vault: return rVault != null ? rVault.lockPoint : pos;
                case RGoal.Boss: return b.royale.boss != null && b.royale.boss.Alive ? b.royale.boss.unit.transform.position : pos;
                case RGoal.Mate: case RGoal.Station: return DuoGoalPos(pos);
                default: return rRoam;
            }
        }

        void PickRoyaleGoal(Vector3 pos)
        {
            var r = b.royale;
            rGoalT = Random.Range(5f, 9f);
            rProgD = 1e9f; rProgT = 0;
            rStuckT = 0;
            if (rItem != null && rItem.claimedBy == u) rItem.claimedBy = null;
            rItem = null; rChest = null; rVault = null;
            if (rHigh != null && rHigh.holder == u) rHigh.holder = null;
            rHigh = null;
            bool late = LateForZone(pos);
            // 0. duos: revive the mate, carry their tag to a station, fetch their tag, keep together
            if (PickDuoGoal(pos, late)) return;
            // 1. the zone
            if (r.OutsideZone(pos) || (late && r.OutsideNext(pos)))
            {
                rGoal = RGoal.Zone;
                Vector2 c = r.nextC + (new Vector2(pos.x, pos.z) - r.nextC).normalized * r.nextR * Random.Range(0.2f, 0.6f);
                rRoam = b.land.nav.SnapPoint(new Vector3(c.x, 0, c.y));
                rGoalT = 4;
                return;
            }
            // 2. a vault, with a key
            if (u.keys > 0)
                foreach (var v in b.land.vaults)
                    if (!v.open && (v.lockPoint - pos).magnitude < 450 && !r.OutsideNext(v.lockPoint)) { rGoal = RGoal.Vault; rVault = v; return; }
            // 3. loot nearby: guns better than what we carry, supplies we lack, keys; chests are worth a detour
            if (rScanT <= 0 || true)
            {
                rScanT = 1.5f;
                r.LootAround(pos, 70, rScratch);
                float best = 0; LootItem pick = null;
                float worst = WorstGun, top = BestGun;
                foreach (var it in rScratch)
                {
                    if (rSkip.Contains(it.id) || (it.claimedBy && it.claimedBy != u && it.claimedBy.Alive && Time.time < it.claimUntil)) continue;
                    if (r.CantTake(u, it) != null) continue;
                    float climbCost = 0;
                    if (it.pos.y - b.land.Top(it.pos.x, it.pos.z) > 2.5f && Mathf.Abs(it.pos.y - pos.y) > 2f)
                    {
                        var itf = b.land.FloorAt(it.pos);
                        if (itf == null) continue;                  // on a roof or a ledge with no way up
                        climbCost = 8 + (itf.c.y - b.land.Top(itf.c.x, itf.c.z)) * 2.5f;   // up the stairs / ramps
                    }
                    float want;
                    switch (it.kind)
                    {
                        case LootKind.Weapon: { float v = LootTable.Value(it.weapon); want = v > worst * 1.15f ? (v - worst) / 40f + (top < 150 ? 3 : 0) : 0; break; }
                        case LootKind.Medkit: want = u.medkits < 2 ? 2.2f : u.medkits < 4 && !Supplied ? 0.8f : 0; break;
                        case LootKind.Shield: want = u.shields < 2 ? 2.4f : u.shields < 4 && !Supplied ? 0.9f : 0; break;
                        case LootKind.Ammo: want = 1.2f; break;
                        case LootKind.Key: want = 4f; break;
                        default: want = Supplied ? 0 : 0.6f; break;
                    }
                    if (want <= 0) continue;
                    float dd = (it.pos - pos).magnitude + climbCost;
                    float s = want / (8 + dd);
                    if (s > best) { best = s; pick = it; }
                }
                var chest = r.NearestChest(pos, 55);
                if (chest != null && !rSkipChests.Contains(chest) && !(chest.claimedBy && chest.claimedBy != u && chest.claimedBy.Alive && Time.time < chest.claimUntil))
                {
                    float s = 3.2f / (8 + (chest.pos - pos).magnitude);
                    if (s > best) { rGoal = RGoal.Chest; rChest = chest; chest.claimedBy = u; chest.claimUntil = Time.time + 20; return; }
                }
                if (pick != null) { rGoal = RGoal.Loot; rItem = pick; pick.claimedBy = u; pick.claimUntil = Time.time + 15; return; }
            }
            // high ground: a long gun and a tower in the zone - go and hold its roof for a while
            if (!late && PickHighGround(pos)) return;
            // 4. the boss, if we are ready for it
            if (Strong && r.boss != null && r.boss.Alive && (r.boss.unit.transform.position - pos).magnitude < 320 && Random.value < 0.75f) { rGoal = RGoal.Boss; return; }
            // late in the match a well-supplied bot goes looking for someone (where it last heard shooting, roughly)
            if (r.AlivePlayers <= 30 && Supplied && Random.value < 0.65f)
            {
                Unit prey = null; float pd = 300 * 300;
                foreach (var o in b.units)
                {
                    if (!o || !o.Alive || o.team == u.team || o.part.boss) continue;
                    float d2 = (o.transform.position - pos).sqrMagnitude * (o.part.Human ? 0.5f : 1f);   // people draw them in
                    if (d2 < pd) { pd = d2; prey = o; }
                }
                if (prey) { rGoal = RGoal.Roam; Vector2 j = Random.insideUnitCircle * 15; rRoam = b.land.nav.SnapPoint(prey.transform.position + new Vector3(j.x, 0, j.y)); rGoalT = 6; return; }
            }
            // 5. wander toward buildings inside the next zone
            rGoal = RGoal.Roam;
            Vector3 dest = Vector3.zero; bool found = false;
            var land = b.land;
            for (int i = 0; i < 10 && land.houses.Count > 0; i++)
            {
                var h = land.houses[Random.Range(0, land.houses.Count)];
                if (h.vault) continue;
                float hd = (h.center - pos).magnitude;
                if (hd > 220 || r.OutsideNext(h.center)) continue;
                dest = h.doorOut; found = true; break;
            }
            if (!found)
            {
                Vector2 c = r.nextC + Random.insideUnitCircle * r.nextR * 0.7f;
                Vector3 toward = new Vector3(c.x, 0, c.y) - pos; toward.y = 0;
                if (toward.magnitude > 150) toward = toward.normalized * 150;
                dest = land.nav.SnapPoint(pos + toward);
            }
            rRoam = dest;
        }

        /// <summary>Royale drivers: head for the next zone; get out near it or when a fight starts close by.</summary>
        void RoyaleDrive(Vehicle v, float dt, ref Vector3 goal)
        {
            var r = b.royale;
            Vector3 pos = v.transform.position;
            if (objT <= 0)
            {
                objT = 5;
                Vector2 c = r.nextC + (new Vector2(pos.x, pos.z) - r.nextC).normalized * r.nextR * 0.4f;
                moveGoal = b.land.nav.SnapPoint(new Vector3(c.x, 0, c.y), true);
            }
            goal = moveGoal;
            Vector3 to = moveGoal - pos; to.y = 0;
            bool arrived = to.magnitude < 30 || (!r.OutsideNext(pos) && !r.shrinking && Random.value < 0.002f);
            bool close = target != null && (target.AimPoint - pos).magnitude < 40 && v.def.kind == VKind.Transport;
            if ((arrived || close) && v.speed < 6 && v.CanExitHere()) { v.Leave(u, true); rGoalT = 0; }
        }

        // ── the boss ─────────────────────────────────────────────────────
        void BossThink(float dt)
        {
            var r = b.royale;
            think -= dt; seeT -= dt; strafeT -= dt; abilityT -= dt;
            Vector3 pos = u.transform.position, lair = r.bossLair;
            float fromLair = (pos - lair).magnitude;
            if (think <= 0)
            {
                think = 0.3f;
                var t = FindTarget(u.Eye, 85, false);
                if (t != null && (t.AimPoint - lair).magnitude > 90) t = null;     // it never chases far from home
                if (t != null) { target = t; seeT = 2.5f; } else if (seeT <= 0) target = null;
            }
            if (target != null && (target is Object to0 && !to0 || !target.Alive)) target = null;
            Vector3 move = Vector3.zero; bool fire = false;
            Vector3 aimPoint = u.Eye + u.transform.forward * 30;
            if (target != null)
            {
                Vector3 to = target.AimPoint - pos; to.y = 0;
                float d = to.magnitude;
                if (strafeT <= 0) { strafeT = Random.Range(1.5f, 3f); strafe = -strafe; }
                Vector3 dir = d > 0.1f ? to / d : u.transform.forward;
                move = (d > 30 ? dir : d < 14 ? -dir : Vector3.zero) + Vector3.Cross(Vector3.up, dir) * strafe * 0.5f;
                aimPoint = AimWithError(u.Eye, Lead(u.Eye, target, u.Weapon.def.Hitscan ? 1e6f : u.Weapon.def.speed), dt);
                burst -= dt; if (burst < -0.7f) burst = Random.Range(1f, 2f);
                fire = burst > 0 && (!u.Weapon.def.UsesHeat || u.Weapon.heat < 0.85f);
                if (abilityT <= 0 && u.abilityCd <= 0 && d > 10 && d < 70) { u.input.ability = true; abilityT = Random.Range(8, 12); }
            }
            if (fromLair > 40) move = Toward(pos, lair);
            else if (target == null)
            {
                float a = Time.time * 0.15f + u.part.name.Length;
                Vector3 patrol = lair + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 14;
                if ((patrol - pos).magnitude > 3) move = Toward(pos, patrol) * 0.5f;
            }
            move = Avoid(pos, move, dt);
            u.input.move = Vector3.ClampMagnitude(new Vector3(move.x, 0, move.z), 1);
            u.input.aimPoint = aimPoint;
            u.input.fire = fire;
            u.input.fireDown = fire && Random.value < 0.3f;
        }
    }
}
