using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    /// <summary>
    /// AI for one bot: infantry on land (posts, cover-less skirmishing, grenades, abilities, vehicles),
    /// drivers / gunners / gunship pilots, and in space: dogfighting, bombing runs on capital ship systems,
    /// boarding the enemy hangar and sabotaging Life Support / the Reactor, and defending its own ship.
    /// </summary>
    public partial class BotBrain
    {
        enum SpaceJob { Dogfight, Bomb, Board, Escort, Defend, Sabotage }
        enum RunPhase { Approach, Attack, Break }

        readonly Unit u;
        readonly Battle b;
        float think, seeT, objT, strafeT, strafe = 1, stuckT, noiseT, burst, sideStepT, abilityT, grenadeT, enterT;
        float evadeRoll = -1;   // starfighter pilots: a die rolled once per missile chase - do we roll out of this one?
        public int wantShipKind = -1;  // space: spawned on foot in our hangar, running to a parked craft of this kind
        float shuttleWait;             // a shuttle pilot waits a little for boarders
        public bool HeadingForShip => wantShipKind >= 0 || (b.space != null && wantVehicle != null);
        bool chased;            // starfighter pilots: an enemy craft sits behind us with its nose on us (checked every think)
        float dodgeRollT;       // next time a pilot under fire may throw an evasive roll that isn't for a missile
        float weaveSeed = Random.value * 10f;
        Vector3 lastPos, noise, moveGoal, sideStep;
        IDamageable target;
        Vector3 targetVel;
        CommandPost objective;
        Vehicle wantVehicle;
        float trackTime;
        SpaceJob job;
        RunPhase run;
        Vector3 runPoint;
        Subsystem sysTarget;
        int navGoal = -1, navNext = -1;
        float detpackT;
        bool boarded;
        float skill;
        readonly NavAgent navAgent = new NavAgent();

        /// <summary>Flat direction toward a point, routed round ridges, rivers and buildings on land maps.</summary>
        Vector3 Toward(Vector3 pos, Vector3 goal, bool vehicle = false)
        {
            var nav = b.land != null ? b.land.nav : null;
            Vector3 d = goal - pos; d.y = 0;
            if (nav == null) return d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.zero;
            return nav.Steer(navAgent, pos, goal, vehicle);
        }

        public BotBrain(Unit unit)
        {
            u = unit; b = unit.battle;
            skill = unit.part.aimSkill;
            lastPos = u.transform.position;
            think = Random.Range(0, 0.3f);
            abilityT = Random.Range(3, 10);
            grenadeT = Random.Range(3, 8);
            if (b.space != null)
            {
                job = SpaceJob.Defend;
                if (u.InVehicle || true) PickSpaceJob();
            }
        }

        float Range => u.Weapon.def.kind == WKind.Sniper ? 220 : u.IsHero && u.hero.kind == HeroKind.Blade ? 60 : 95;
        float PreferDist
        {
            get
            {
                if (u.IsHero) return u.hero.kind == HeroKind.Blade ? 1.5f : u.hero.kind == HeroKind.Mystic ? 9 : 22;
                switch (u.RoleOf) { case Role.Heavy: return 35; case Role.Marksman: return 90; case Role.Engineer: return 9; case Role.Specialist: return 18; default: return 24; }
            }
        }

        public void OnHurt(DamageInfo d)
        {
            if (d.attacker && d.attacker.Alive && d.attacker.team != u.team && (target == null || !target.Alive || Random.value < 0.5f))
            {
                target = d.attacker.InVehicle ? (IDamageable)d.attacker.vehicle : d.attacker;
                seeT = 2.5f;
            }
        }

        public void OnVehicleHurt(DamageInfo d) => OnHurt(d);

        static bool OutOfAmmo(WeaponState w) => w != null && !w.def.UsesHeat && w.def.clip > 0 && w.clip <= 0 && w.ammo <= 0;

        // ── entry ────────────────────────────────────────────────────────
        public void Think(float dt)
        {
            if (u.InVehicle)
            {
                var v = u.vehicle;
                if (u.seat == 0)
                {
                    if (v.def.IsGround) Drive(v, dt);
                    else if (v.def.IsShip) Pilot(v, dt);
                    else FlyGunship(v, dt);
                }
                else if (v.gunnerT && u.seat == 1) Gunner(v, dt);
                else Passenger(v, dt);
                return;
            }
            if (b.space != null) SpaceInfantry(dt);
            else if (b.royale != null) RoyaleInfantry(dt);
            else LandInfantry(dt);
        }

        // ── perception ───────────────────────────────────────────────────
        bool Visible(Vector3 from, IDamageable t)
        {
            Vector3 to = t.AimPoint - from;
            float d = to.magnitude;
            if (d < 0.5f) return true;
            if (Physics.Raycast(from, to / d, out var hit, d - 0.6f, Layers.SightMask, QueryTriggerInteraction.Ignore))
            {
                var hd = hit.collider.GetComponentInParent<IDamageable>();
                return hd == t;
            }
            return true;
        }

        // who is being shot at by how many bots (refreshed a couple of times a second): the next bot looking for a target
        // counts a soldier that already has several bots on it as further away, so a side spreads its fire instead of
        // everyone in view turning on the same person (measured: 9 of 11 enemies in view on the player at once)
        static readonly Dictionary<IDamageable, int> focus = new Dictionary<IDamageable, int>();
        static float focusAt = -9;
        static void RefreshFocus(Battle b)
        {
            if (Time.time - focusAt < 0.4f && Time.time >= focusAt) return;
            focusAt = Time.time; focus.Clear();
            foreach (var o in b.units)
            {
                if (!o || !o.Alive || o.bot == null) continue;
                var t = o.bot.target;
                if (t == null || (t is Object to && !to) || !t.Alive) continue;
                focus.TryGetValue(t, out int n); focus[t] = n + 1;
            }
        }
        float FocusMul(IDamageable e)
        {
            if (!focus.TryGetValue(e, out int n)) return 1;
            if (target == e) n--;
            return n <= 0 ? 1 : 1 + 0.4f * n * n;      // 1 other: x1.4, 2: x2.6, 3: x4.6, 4: x7.4 (on distance squared)
        }

        IDamageable FindTarget(Vector3 eye, float range, bool preferVehicles, bool shipsOnly = false)
        {
            RefreshFocus(b);
            IDamageable best = null; float bd = range * range;
            int checks = 0;
            var cand = new List<(IDamageable, float)>(16);
            if (!shipsOnly)
                foreach (var e in b.units)
                {
                    if (!e || !e.Alive || e.team == u.team || e.InVehicle || e.Cloaked) continue;
                    float d2 = (e.transform.position - eye).sqrMagnitude;
                    // the royale's bots are drawn to people; in a battle a person is one soldier among many
                    if (d2 < bd) cand.Add((e, d2 * (preferVehicles ? 2.5f : 1f) * (e.part.Human && b.royale != null ? 0.8f : 1f) * (e.knocked ? 8f : 1f) * (e.part.boss ? 1f : FocusMul(e))));   // someone standing (who shoots back) first, the downed after
                }
            foreach (var v in b.vehicles)
            {
                if (!v || !v.Alive || v.Team == u.team || v.IsEmpty) continue;
                if (shipsOnly && !v.IsShip) continue;
                float d2 = (v.transform.position - eye).sqrMagnitude;
                if (d2 < bd * 1.4f) cand.Add((v, d2 * (preferVehicles ? 0.3f : 1.6f)));
            }
            cand.Sort((x, y) => x.Item2.CompareTo(y.Item2));
            foreach (var c in cand)
            {
                if (checks++ > 4) break;
                if (Visible(eye, c.Item1)) { best = c.Item1; break; }
            }
            return best;
        }

        Vector3 VelOf(IDamageable t)
        {
            if (t is Unit tu) return tu.velocity;
            if (t is Vehicle tv) return tv.vel;
            return Vector3.zero;
        }

        Vector3 Lead(Vector3 from, IDamageable t, float speed)
        {
            Vector3 p = t.AimPoint;
            float tt = (p - from).magnitude / Mathf.Max(30, speed);
            return p + VelOf(t) * tt * 0.85f;
        }

        Vector3 AimWithError(Vector3 from, Vector3 point, float dt, float mul = 1f)
        {
            noiseT -= dt;
            float dist = (point - from).magnitude;
            if (noiseT <= 0)
            {
                noiseT = Random.Range(0.25f, 0.5f);
                // 2026-11-08: about 30% wider than before (the player's own aim help was cut at the same time)
                float errDeg = Mathf.Lerp(9.5f, 1.9f, skill) * Mathf.Lerp(1.6f, 0.6f, Mathf.Clamp01(trackTime / 3f)) * mul;
                // battle royale: bots are full strength against people but shakier against each other, so the
                // field doesn't wipe itself out before the zone and the players get a say
                if (b.royale != null && !u.part.boss && target is Unit tu && !tu.part.Human) errDeg *= 1.6f;
                noise = Random.insideUnitSphere * Mathf.Tan(errDeg * Mathf.Deg2Rad) * dist;
            }
            return point + noise;
        }

        // ── land infantry ────────────────────────────────────────────────
        Squad squad;
        int squadVersion = -1;
        bool flanking;
        bool houseGoal;          // moveGoal is a spot inside a building: hold there a while
        public static int HousePicks;
        float holdT;
        bool holdCrouch;

        /// <summary>Defenders and snipers sometimes take a building near the objective instead of standing in the open.</summary>
        bool PickHouse(Vector3 near, float within)
        {
            var land = b.land;
            if (land == null || land.houses.Count == 0) return false;
            LandWorld.House best = null; float bd = within * within;
            foreach (var h in land.houses)
            {
                float d = (h.center - near).sqrMagnitude + Random.Range(0, 900f);
                if (d < bd) { bd = d; best = h; }
            }
            if (best == null) return false;
            Vector3 l = new Vector3(Random.Range(-best.w * 0.3f, best.w * 0.3f), 0, Random.Range(-best.d * 0.3f, best.d * 0.3f));
            moveGoal = best.center + best.rot * l;
            houseGoal = true; holdT = Random.Range(15f, 32f); holdCrouch = Random.value < 0.5f;
            objT = 60; HousePicks++;
            return true;
        }
        public string OrderText => squad != null ? Commander.OrderName(squad.order) + (squad.target ? " " + squad.target.postName : "") : "";

        void PickObjective()
        {
            objT = Random.Range(8, 14);
            squad = b.commander != null ? b.commander.SquadOf(u.part) : null;
            // still on the way to (or holding) a building for the same orders: keep it
            if (houseGoal && holdT > 0 && squad != null && squad.version == squadVersion) { objT = 10; return; }
            if (squad != null && squad.target != null)
            {
                squadVersion = squad.version;
                objective = squad.target;
                float spreadR = squad.order == SquadOrder.Defend ? objective.radius * 1.3f : objective.radius * 0.7f;
                Vector2 r2 = Random.insideUnitCircle * spreadR;
                moveGoal = objective.Pos + new Vector3(r2.x, 0, r2.y);
                // a flanking squad swings wide first, unless it is already close
                flanking = squad.order == SquadOrder.Flank && (u.transform.position - objective.Pos).magnitude > 70 && (u.transform.position - squad.via).magnitude > 18;
                if (flanking) moveGoal = squad.via + new Vector3(r2.x, 0, r2.y) * 0.5f;
                // an attack wave waits at its staging point until enough of the squad is there
                if (squad.gathering) { flanking = false; Vector2 r3 = Random.insideUnitCircle * 12; moveGoal = squad.stage + new Vector3(r3.x, 0, r3.y); }
                houseGoal = false;
                bool marks = u.RoleOf == Role.Marksman && !u.IsHero;
                if (!squad.gathering && !flanking && (squad.order == SquadOrder.Defend ? Random.value < 0.5f : marks && Random.value < 0.6f))
                    PickHouse(objective.Pos, marks ? 90 : 60);
                return;
            }
            CommandPost best = null; float bs = float.MaxValue;
            Vector3 p = u.transform.position;
            bool defend = Random.value < 0.22f;
            foreach (var cp in b.posts)
            {
                float d = (cp.Pos - p).magnitude;
                float s;
                if (cp.owner == u.team)
                {
                    bool threatened = cp.present[1 - u.team] > 0 || (cp.capTeam != u.team && cp.progress < 1);
                    if (!threatened && !defend) continue;
                    s = d * (threatened ? 0.5f : 1.2f);
                }
                else s = d * (cp.owner < 0 ? 0.75f : 1f);
                s += Random.Range(0, 120);
                if (s < bs) { bs = s; best = cp; }
            }
            if (!best) best = b.posts.Count > 0 ? b.posts[Random.Range(0, b.posts.Count)] : null;
            objective = best;
            if (objective)
            {
                Vector2 r = Random.insideUnitCircle * objective.radius * 0.6f;
                moveGoal = objective.Pos + new Vector3(r.x, 0, r.y);
            }
        }

        void LandInfantry(float dt)
        {
            think -= dt; seeT -= dt; objT -= dt; strafeT -= dt; abilityT -= dt; grenadeT -= dt; enterT -= dt;
            Vector3 pos = u.transform.position;
            if (squad == null && b.commander != null) squad = b.commander.SquadOf(u.part);
            bool newOrders = squad != null && squad.version != squadVersion;
            if (newOrders || objT <= 0 || objective == null || (squad == null && objective.owner == u.team && objective.progress >= 1 && objective.present[1 - u.team] == 0 && Random.value < 0.02f)) PickObjective();

            if (think <= 0)
            {
                think = Random.Range(0.25f, 0.4f);
                // squads on the move only stop for close threats (or whoever is shooting them); defenders and marksmen engage at full range
                bool onTheMove = squad != null && squad.order != SquadOrder.Defend && !squad.gathering && objective && (objective.Pos - pos).magnitude > objective.radius * 1.5f
                                 && u.RoleOf != Role.Marksman && Time.time - u.lastHurt > 2.5f;
                var t = FindTarget(u.Eye, onTheMove ? Mathf.Min(Range, 60) : Range, u.RoleOf == Role.Heavy && !u.IsHero);
                if (t != null) { if (t != target) trackTime = 0; target = t; seeT = 1.2f; }
                else if (seeT <= 0) target = null;
                // grab a vehicle near spawn now and then
                if (wantVehicle == null && target == null && enterT <= 0 && !u.IsHero && Time.time - u.spawnTime < 15 && u.RoleOf != Role.Marksman)
                {
                    enterT = 99;
                    bool attacking = squad == null || squad.order != SquadOrder.Defend;
                    float range = attacking ? 50 : 25;
                    foreach (var v in b.vehicles)
                        if (v && v.CanEnter(u) && (v.Team == u.team || v.IsEmpty) && (v.transform.position - pos).sqrMagnitude < range * range &&
                            Random.value < (v.Driver ? (attacking ? 0.55f : 0.2f) : (attacking ? 0.8f : 0.25f))) { wantVehicle = v; break; }
                }
            }
            if (target != null && (target is Object to0 && !to0 || !target.Alive)) target = null;

            Vector3 move = Vector3.zero;
            bool fire = false, aim = false, crouch = false, sprint = false;
            Vector3 aimPoint = u.Eye + u.transform.forward * 30;

            if (wantVehicle)
            {
                if (!wantVehicle.CanEnter(u)) wantVehicle = null;
                else
                {
                    Vector3 to = wantVehicle.transform.position - pos; to.y = 0;
                    if (to.magnitude < wantVehicle.radius + 3.5f) { wantVehicle.Enter(u); wantVehicle = null; return; }
                    move = Toward(pos, wantVehicle.transform.position); sprint = true;
                    aimPoint = wantVehicle.transform.position;
                }
            }
            else if (target != null)
            {
                trackTime += dt;
                Vector3 tp = target.AimPoint;
                Vector3 to = tp - pos; to.y = 0;
                float d = to.magnitude;
                Vector3 dir = d > 0.1f ? to / d : u.transform.forward;
                if (strafeT <= 0) { strafeT = Random.Range(0.8f, 2.2f); strafe = Random.value < 0.5f ? -1 : 1; }
                Vector3 side = Vector3.Cross(Vector3.up, dir) * strafe;
                float pref = PreferDist;
                bool advancing = squad != null && squad.order != SquadOrder.Defend && !squad.gathering && objective && (objective.Pos - pos).magnitude > 55;
                if (u.RoleOf == Role.Marksman && !u.IsHero && advancing)
                {
                    // snipers in an attacking squad keep up with it and only settle in near the objective
                    move = Toward(pos, flanking ? squad.via : objective.Pos) * 0.85f + side * 0.25f;
                    aim = d > 30 && burst > -0.2f;          // stops to scope for the shot (an unscoped sniper shot wanders 4 degrees)
                    if (aim) { move *= 0.15f; crouch = d > 50; }
                }
                else if (u.RoleOf == Role.Marksman && !u.IsHero) { crouch = d > 30; move = d < 20 ? -dir : Vector3.zero; aim = true; }
                else
                {
                    float approach = d > pref * 1.3f ? 1 : d < pref * 0.6f ? -0.7f : 0;
                    move = dir * approach + side * 0.75f;
                    // keep drifting toward the objective when the fight is far from it; attacking squads fight while advancing
                    bool pushing = squad != null && squad.order != SquadOrder.Defend && !squad.gathering;
                    Vector3 toObj = objective ? objective.Pos - pos : Vector3.zero; toObj.y = 0;
                    if (objective && pushing && toObj.magnitude > objective.radius * 0.8f) move += Toward(pos, flanking ? squad.via : objective.Pos) * (d > 15 ? 0.9f : 0.5f);
                    else if (objective && toObj.magnitude > 30 && d > 35) move += Toward(pos, objective.Pos) * 0.6f;
                    if (houseGoal && d > 7)
                    {
                        // heading for a building: keep going and fight from inside it
                        Vector3 tg = moveGoal - pos; tg.y = 0;
                        if (tg.magnitude > 1.5f) move = Toward(pos, moveGoal) * 0.9f + side * 0.15f;
                        else { move = Vector3.zero; crouch = holdCrouch; holdT -= dt; }
                    }
                }
                float boltSpeed = u.Weapon.def.Hitscan ? 1e6f : u.Weapon.def.speed;
                Vector3 lead = Lead(u.Eye, target, boltSpeed);
                aimPoint = AimWithError(u.Eye, lead, dt);
                // shoot in bursts, and do not cook the weapon
                var w = u.Weapon;
                burst -= dt;
                if (burst < -0.5f) burst = Random.Range(0.6f, 1.5f);
                bool heatOk = !w.def.UsesHeat || w.heat < 0.82f;
                // no spraying from beyond the gun's useful range (carbines 60 m, pistols 55 m): close in instead
                float fireRange = Mathf.Min(Range, Mathf.Max(30, w.def.EffectiveRange));
                fire = burst > 0 && heatOk && d < fireRange;
                // at distance a soldier plants their feet (and kneels) for the burst, then moves again between bursts:
                // a moving shooter's shots spread x1.3, a crouched one's x0.6
                if (fire && d > 30 && u.RoleOf != Role.Marksman && !u.IsHero && w.def.kind != WKind.Rocket && Time.time - u.lastHurt > 0.6f) { move = Vector3.zero; crouch = d > 42; }   // (zero, not slow: a slow walk trips the stuck check)
                if (w.def.kind == WKind.Rocket && !(target is Vehicle) && d > 40) fire = false;
                if (w.def.kind == WKind.Blade) fire = d < 3.5f;
                if (w.def.kind == WKind.Beam) fire = d < w.def.range;
                if (w.def.kind == WKind.Repair) { fire = false; if (u.weapons[0] != null && u.cur != 0) u.input.swap = true; }
                if (w.def.kind == WKind.Shotgun) fire = d < 22;
                if (u.Blocking) fire = false;
                // switch weapon for the situation
                if (u.weapons[1] != null && !u.IsHero)
                {
                    bool wantPrimary = true;
                    if (u.RoleOf == Role.Heavy && !(target is Vehicle) && d < 30) wantPrimary = false;
                    if (u.RoleOf == Role.Marksman && d < 18) wantPrimary = false;
                    if (u.RoleOf == Role.Engineer) wantPrimary = true;
                    if (OutOfAmmo(u.weapons[0])) wantPrimary = false;
                    if ((u.cur == 0) != wantPrimary && Random.value < 0.1f) u.input.swap = true;
                }
                // grenades
                if (grenadeT <= 0 && u.cls != null && u.cls.equip == Equip.Grenade && u.equipLeft > 0 && d > 9 && d < 26 && target is Unit)
                {
                    grenadeT = Random.Range(6, 14);
                    u.input.equip = true;
                    aimPoint = tp + Vector3.up * d * 0.12f;
                }
                // abilities
                if (abilityT <= 0 && u.abilityCd <= 0)
                {
                    bool use = false;
                    if (u.IsHero)
                    {
                        switch (u.hero.kind)
                        {
                            case HeroKind.Blade: use = d > 5 && d < 13; break;
                            case HeroKind.Gunslinger: use = d < 40; break;
                            case HeroKind.Juggernaut: use = d > 12 && d < 70; break;
                            case HeroKind.Mystic: use = d < 11; break;
                        }
                    }
                    else if (u.cls.special == Special.Frenzy || u.cls.special == Special.Rally) use = d < 30;
                    if (use) { u.input.ability = true; abilityT = Random.Range(4, 9); }
                }
                // blade heroes block when shot at from range
                if (u.IsHero && u.hero.kind == HeroKind.Blade && d > 5 && Time.time - u.lastHurt < 1.5f && Random.value < 0.7f) { aim = true; move = dir; }
                if (u.IsHero && u.hero.kind == HeroKind.Blade) { move = dir + side * 0.2f; sprint = d > 8; }
            }
            else if (objective)
            {
                Vector3 to = moveGoal - pos; to.y = 0;
                float d = to.magnitude;
                if (flanking && d < 10) { flanking = false; Vector2 r0 = Random.insideUnitCircle * objective.radius * 0.7f; moveGoal = objective.Pos + new Vector3(r0.x, 0, r0.y); }
                if (houseGoal && d < 2.5f)
                {
                    // holding a building: crouch at the spot, look out, stay until the timer runs out
                    holdT -= dt; crouch = holdCrouch;   // one pose for the whole hold (a fresh roll every frame bobbed up and down)
                    if (holdT <= 0) { houseGoal = false; objT = 0; }
                }
                else if (d > 2) { move = Toward(pos, moveGoal); sprint = d > 30; }
                else
                {
                    bool defending = squad != null && squad.order == SquadOrder.Defend;
                    Vector2 r = Random.insideUnitCircle * objective.radius * (defending ? 1.3f : 0.7f);
                    moveGoal = squad != null && squad.gathering ? squad.stage + new Vector3(r.x, 0, r.y) * 1.2f : objective.Pos + new Vector3(r.x, 0, r.y);
                    if (squad != null && squad.gathering) defending = true;
                    if (Random.value < (defending ? 0.6f : 0.3f)) crouch = true;
                }
                aimPoint = u.Eye + (move.sqrMagnitude > 0.01f ? move : u.transform.forward) * 30 + Vector3.up * 0.5f;
                // engineers fix friendly vehicles on the way
                if (u.RoleOf == Role.Engineer && !u.IsHero && Time.frameCount % 10 == 0) TryRepairNearby(ref move, ref aimPoint, ref fire);
                // specials
                if (abilityT <= 0 && u.abilityCd <= 0 && u.cls != null && u.cls.special == Special.Cloak && d > 30) { u.input.ability = true; abilityT = 20; }
                // heavy lays a mine at the objective
                if (u.cls != null && u.cls.equip == Equip.Mine && u.equipLeft > 0 && d < 6 && Random.value < 0.004f) u.input.equip = true;
            }

            // keep it inside the battlefield
            if (b.land != null)
            {
                float lim = b.land.PlayHalf - 15;
                if (Mathf.Abs(pos.x) > lim || Mathf.Abs(pos.z) > lim) move = (-pos).normalized;
            }

            if (OutOfAmmo(u.Weapon) && u.weapons[1 - u.cur] != null && !OutOfAmmo(u.weapons[1 - u.cur])) u.input.swap = true;
            move = Avoid(pos, move, dt);
            if (u.cls != null && u.cls.special == Special.Jetpack && !u.grounded && move.sqrMagnitude > 0.1f && stuckT > 0.6f) u.input.jump = true;
            // floating isles: never step off an edge (strafing, dodging or a stale route)
            if (b.land != null && b.land.Floating && move.sqrMagnitude > 0.01f)
            {
                Vector3 md = new Vector3(move.x, 0, move.z).normalized;
                if (b.land.Ledge(pos, pos + md * 1.4f) || b.land.Ledge(pos, pos + md * 2.6f)) move = Vector3.zero;
            }

            u.input.move = Vector3.ClampMagnitude(new Vector3(move.x, 0, move.z), 1);
            u.input.aimPoint = aimPoint;
            u.input.fire = fire;
            u.input.fireDown = fire && Random.value < 0.25f;
            u.input.aim = aim;
            u.input.crouch = crouch;
            u.input.sprint = sprint && !fire;
        }

        void TryRepairNearby(ref Vector3 move, ref Vector3 aimPoint, ref bool fire)
        {
            foreach (var v in b.vehicles)
            {
                if (!v || !v.Alive || v.Team != u.team || v.hp > v.maxHp * 0.8f) continue;
                Vector3 to = v.transform.position - u.transform.position;
                if (to.magnitude > 25) continue;
                if (u.cur != 1) { u.input.swap = true; return; }
                if (to.magnitude > 4) move = new Vector3(to.x, 0, to.z).normalized;
                else { move = Vector3.zero; aimPoint = v.AimPoint; fire = true; }
                return;
            }
            if (u.cur != 0 && u.weapons[0] != null) u.input.swap = true;
        }

        /// <summary>Feelers against walls / rocks; sidestep and jump when stuck.</summary>
        Vector3 Avoid(Vector3 pos, Vector3 move, float dt)
        {
            if (move.sqrMagnitude < 0.01f) { stuckT = 0; lastPos = pos; return move; }
            Vector3 dir = new Vector3(move.x, 0, move.z).normalized;
            Vector3 knee = pos + Vector3.up * 0.7f;
            const int mask = (1 << Layers.Default) | (1 << Layers.Hull) | (1 << Layers.Vehicle);
            if (Physics.SphereCast(knee, 0.35f, dir, out var hit, 2.4f, mask, QueryTriggerInteraction.Ignore))
            {
                Vector3 l = Quaternion.Euler(0, -65, 0) * dir, r = Quaternion.Euler(0, 65, 0) * dir;
                bool lf = !Physics.SphereCast(knee, 0.35f, l, out _, 3f, mask), rf = !Physics.SphereCast(knee, 0.35f, r, out _, 3f, mask);
                if (lf && (!rf || strafe < 0)) dir = l; else if (rf) dir = r; else dir = Vector3.Reflect(dir, hit.normal);
            }
            // stuck detection
            float moved = (pos - lastPos).magnitude;
            if (moved < 0.6f * dt * 3) stuckT += dt; else stuckT = Mathf.Max(0, stuckT - dt * 2);
            lastPos = pos;
            if (stuckT > 1.0f && sideStepT <= 0)
            {
                sideStepT = 1.0f;
                sideStep = Quaternion.Euler(0, Random.value < 0.5f ? 100 : -100, 0) * dir;
                u.input.jumpDown = true;
                stuckT = 0;
            }
            if (sideStepT > 0) { sideStepT -= dt; return sideStep; }
            return dir * move.magnitude;
        }

        // ── ground vehicle driver ────────────────────────────────────────
        void Drive(Vehicle v, float dt)
        {
            think -= dt; objT -= dt; seeT -= dt;
            Vector3 pos = v.transform.position;
            Vector3 royaleGoal = pos;
            if (b.royale != null) RoyaleDrive(v, dt, ref royaleGoal);
            else if (objT <= 0 || objective == null) PickObjective();
            if (!u.InVehicle) return;
            if (think <= 0)
            {
                think = 0.35f;
                var t = FindTarget(pos + Vector3.up * 2.5f, 150, true);
                if (t != null) { target = t; seeT = 1.5f; }
                else if (seeT <= 0) target = null;
            }
            if (target != null && (target is Object to0 && !to0 || !target.Alive)) target = null;
            if (v.hp < v.maxHp * 0.12f && Random.value < 0.02f) { v.Leave(u, true); return; }

            var inp = new VehicleInput();
            Vector3 goal = b.royale != null ? royaleGoal : objective ? moveGoal : pos;
            Vector3 aimAt = pos + v.transform.forward * 40;
            if (target != null)
            {
                Vector3 tp = target.AimPoint;
                float d = (tp - pos).magnitude;
                var w = v.prim;
                aimAt = AimWithError(pos, Lead(pos + Vector3.up * 2, target, w != null ? w.def.speed : 150), dt);
                inp.fire = w != null && (!w.def.UsesHeat || w.heat < 0.75f) && d < (v.def.kind == VKind.Tank ? 160 : 110);
                if (v.def.kind == VKind.Tank) { float pref = 45; if (d < pref * 0.6f) goal = pos - (tp - pos).normalized * 20; else if (d < pref * 1.4f) goal = pos + Vector3.Cross(Vector3.up, (tp - pos).normalized) * strafe * 10; }
            }
            Vector3 to = goal - pos; to.y = 0;
            float dist = to.magnitude;
            Vector3 move = dist > 6 ? Toward(pos, goal, true) : Vector3.zero;
            if (dist < 6 && objective) { Vector2 r = Random.insideUnitCircle * objective.radius; moveGoal = objective.Pos + new Vector3(r.x, 0, r.y) * 1.5f; }
            strafeT -= dt; if (strafeT <= 0) { strafeT = Random.Range(2, 5); strafe = -strafe; }
            // obstacle feelers
            if (move.sqrMagnitude > 0.01f)
            {
                Vector3 from = pos + Vector3.up * 1.5f;
                if (Physics.SphereCast(from, v.radius, move, out var hit, 9, (1 << Layers.Default) | (1 << Layers.Vehicle), QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(v.transform))
                {
                    Vector3 l = Quaternion.Euler(0, -60, 0) * move, r = Quaternion.Euler(0, 60, 0) * move;
                    move = !Physics.SphereCast(from, v.radius, l, out _, 9, 1 << Layers.Default) ? l : r;
                }
                float moved = (pos - lastPos).magnitude;
                if (moved < 1.5f * dt) stuckT += dt; else stuckT = 0;
                if (stuckT > 2) { sideStepT = 1.5f; sideStep = -move; stuckT = 0; }
                if (sideStepT > 0) { sideStepT -= dt; move = sideStep; }
            }
            lastPos = pos;
            // let passengers off at the objective
            if (objective && (objective.Pos - pos).magnitude < 22)
                for (int i = 1; i < v.riders.Length; i++)
                    if (v.riders[i] && v.riders[i].bot != null && (i > 1 || !v.gunnerT)) { var r = v.riders[i]; v.Leave(r, true); r.bot.objective = objective; }
            inp.move = move;
            inp.aimPoint = target != null ? aimAt : (move.sqrMagnitude > 0.01f ? pos + move * 40 + Vector3.up * 1.5f : aimAt);
            inp.boost = dist > 80 && target == null;
            v.seatIn[0] = inp;
        }

        void Gunner(Vehicle v, float dt)
        {
            think -= dt; seeT -= dt;
            if (think <= 0)
            {
                think = 0.35f;
                var t = FindTarget(v.gunnerT.position, 130, v.def.kind == VKind.Transport, v.IsShip);
                if (t != null) { target = t; seeT = 1.5f; } else if (seeT <= 0) target = null;
            }
            if (target != null && (target is Object to0 && !to0 || !target.Alive)) target = null;
            var inp = new VehicleInput();
            if (target != null)
            {
                inp.aimPoint = AimWithError(v.gunnerT.position, Lead(v.gunnerT.position, target, v.gunW.def.speed), dt);
                inp.fire = !v.gunW.def.UsesHeat || v.gunW.heat < 0.8f;
            }
            else inp.aimPoint = v.gunnerT.position + v.transform.forward * 30;
            v.seatIn[1] = inp;
            if (!v.Driver && !v.def.Flies && Random.value < 0.01f) v.Leave(u, true);
        }

        /// <summary>Asked to ride along: walk to the vehicle and get in.</summary>
        public void RideWith(Vehicle v) { wantVehicle = v; enterT = 99; }
        public bool WantsRide => wantVehicle != null;

        void Passenger(Vehicle v, float dt)
        {
            // space: a shuttle that has set down in the enemy hangar (or whose pilot got out there) - everyone storms the ship
            if (b.space != null)
            {
                var enemy = b.space.ships[1 - u.team];
                float lz = enemy.transform.InverseTransformPoint(v.transform.position).z;
                bool there = enemy.hangar.Contains(v.transform.position) && lz < 28 && (v.landed || !v.Driver || v.speed < 14);
                if (there && v.CanExitHere()) { v.Leave(u, true); job = SpaceJob.Sabotage; boarded = true; return; }
                if (v.hp < v.maxHp * 0.15f && v.CanExitHere()) v.Leave(u, true);
                return;
            }
            // riding with the player: stay aboard while they drive; get out when they stop at a post that isn't ours
            if (v.Driver && v.Driver.part.Human)
            {
                if (v.speed < 1.5f && v.CanExitHere())
                    foreach (var cp in b.posts)
                        if (cp.owner != u.team && (cp.Pos - v.transform.position).sqrMagnitude < (cp.radius + 12) * (cp.radius + 12) && Random.value < 0.02f) { v.Leave(u, true); return; }
                if (v.hp < v.maxHp * 0.15f && v.CanExitHere()) v.Leave(u, true);
                return;
            }
            if (!v.Driver && Random.value < 0.02f && v.CanExitHere()) v.Leave(u, true);
            if (v.hp < v.maxHp * 0.15f && v.CanExitHere()) v.Leave(u, true);
        }

        // ── flight helpers ───────────────────────────────────────────────
        static void Steer(Vehicle v, Vector3 point, float throttleWant, ref VehicleInput inp, float gain = 2.4f)
        {
            Vector3 to = point - v.transform.position;
            if (to.sqrMagnitude < 0.01f) return;
            Vector3 local = v.transform.InverseTransformDirection(to.normalized);
            inp.steer.x = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * gain, -1, 1);
            inp.steer.y = Mathf.Clamp(Mathf.Atan2(local.y, Mathf.Max(0.05f, local.z)) * gain, -1, 1);
            if (local.z < 0) inp.steer.y = local.y >= 0 ? 1 : -1;
            float rightUp = v.transform.right.y;
            inp.roll = Mathf.Clamp(-local.x * 1.1f - rightUp * 0.9f, -1, 1);
            inp.throttle = Mathf.Clamp((throttleWant - v.throttle) * 4, -1, 1);
        }

        bool AvoidObstacles(Vehicle v, ref Vector3 point, float look)
        {
            Vector3 p = v.transform.position;
            Vector3 f = v.transform.forward;
            float dist = Mathf.Max(60, v.speed * look);
            if (Physics.SphereCast(p, 7, f, out var hit, dist, Layers.SolidMask, QueryTriggerInteraction.Ignore))
            {
                Vector3 away = (hit.normal + Vector3.up * 0.6f).normalized;
                point = p + (f + away * 2.2f).normalized * 120;
                return true;
            }
            return false;
        }

        // ── planetary gunship ────────────────────────────────────────────
        const float GunshipInfantryRange = 160f;
        void FlyGunship(Vehicle v, float dt)
        {
            think -= dt; objT -= dt;
            Vector3 pos = v.transform.position;
            if (objT <= 0 || objective == null) PickObjective();
            if (think <= 0)
            {
                think = 0.5f;
                var t = FindTarget(pos, 260, true);
                // soldiers only from fairly close (a gunship used to find people 260 m off and cut them down at once)
                if (t is Unit && (t.AimPoint - pos).magnitude > GunshipInfantryRange) t = null;
                target = t;
            }
            if (target != null && (target is Object to0 && !to0 || !target.Alive)) target = null;
            var inp = new VehicleInput();
            float gy = b.GroundY(pos);
            Vector3 point;
            if (target != null)
            {
                Vector3 tp = target.AimPoint;
                float d = (tp - pos).magnitude;
                if (run == RunPhase.Break)
                {
                    point = runPoint;
                    if ((runPoint - pos).magnitude < 40 || d > 230) run = RunPhase.Attack;
                }
                else
                {
                    point = Lead(pos, target, v.prim.def.speed);
                    bool soldier = target is Unit;
                    if (d < 45) { run = RunPhase.Break; Vector3 away = (pos - tp); away.y = 0; runPoint = pos + away.normalized * 180 + Random.insideUnitSphere * 40; runPoint.y = b.GroundY(runPoint) + 55; }
                    float ang = Vector3.Angle(v.transform.forward, point - pos);
                    burst -= dt; if (burst < -1.1f) burst = Random.Range(0.8f, 1.4f);
                    inp.fire = ang < 8 && d < (soldier ? GunshipInfantryRange - 20 : 220) && v.prim.heat < 0.8f && (!soldier || burst > 0);
                    inp.alt = ang < 10 && d < 260 && target is Vehicle && v.lockTime >= 1f;
                }
                // the guns aim with a bot's error (much wider against a soldier); the nose still points at the lead
                inp.aimPoint = run == RunPhase.Break ? point : AimWithError(pos, point, dt, target is Unit ? 1.6f : 1.15f);
            }
            else
            {
                Vector3 c = objective ? objective.Pos : Vector3.zero;
                float a = Time.time * 0.25f + u.part.name.GetHashCode() % 10;
                point = c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 80;
                point.y = b.GroundY(point) + 50;
                inp.aimPoint = point;
            }
            float minAlt = gy + 18;
            if (point.y < minAlt && (target == null || run == RunPhase.Break)) point.y = minAlt + 10;
            if (pos.y < gy + 12) point.y = Mathf.Max(point.y, pos.y + 30);
            AvoidObstacles(v, ref point, 2f);
            Steer(v, point, target != null ? 0.75f : 0.6f, ref inp);
            inp.climb = pos.y < gy + 10 ? 1 : 0;
            v.seatIn[0] = inp;
        }

        // ── space pilot ──────────────────────────────────────────────────
        void PickSpaceJob()
        {
            if (!u.InVehicle) { job = SpaceJob.Defend; return; }
            var k = u.vehicle.def.kind;
            float r = Random.value;
            switch (k)
            {
                case VKind.Bomber: job = r < 0.85f ? SpaceJob.Bomb : SpaceJob.Dogfight; break;
                case VKind.Support: job = r < 0.45f ? SpaceJob.Board : SpaceJob.Escort; break;
                case VKind.Dropship: job = SpaceJob.Board; break;
                case VKind.Interceptor: job = r < 0.9f ? SpaceJob.Dogfight : SpaceJob.Board; break;
                default: job = r < 0.72f ? SpaceJob.Dogfight : r < 0.86f ? SpaceJob.Board : SpaceJob.Bomb; break;
            }
            run = RunPhase.Approach;
        }

        Subsystem PickSystem(CapitalShip enemy)
        {
            Subsystem best = null; float bs = float.MaxValue;
            foreach (var s in enemy.systems)
            {
                if (s.destroyed || s.interior) continue;
                float score = (s.transform.position - u.vehicle.transform.position).magnitude;
                if (s.kind == SysKind.Shield) score *= 0.2f;
                else if (s.kind == SysKind.Engine) score *= enemy.ShieldUp ? 2.5f : 0.4f;
                else if (s.kind == SysKind.Turret) score *= 1.1f;
                score += Random.Range(0, 150);
                if (score < bs) { bs = score; best = s; }
            }
            return best;
        }

        void Pilot(Vehicle v, float dt)
        {
            think -= dt;
            if (job == SpaceJob.Defend || job == SpaceJob.Sabotage) PickSpaceJob();
            var space = b.space;
            Vector3 pos = v.transform.position;
            var inp = new VehicleInput();
            var enemy = space.ships[1 - u.team];
            var home = space.ships[u.team];

            // a shuttle waits in the hangar for a few boarders (up to 8 s)
            if (v.def.kind == VKind.Dropship && v.landed && home.hangar.Contains(pos) && !boarded)
            {
                int aboard = 0; foreach (var rd in v.riders) if (rd) aboard++;
                shuttleWait += dt;
                if (aboard < 4 && shuttleWait < 8) { v.seatIn[0] = new VehicleInput { aimPoint = pos + v.transform.forward * 50 }; return; }
            }
            // taxi out of the hangar first
            if (v.landed || (home.hangar.Contains(pos) && !boarded))
            {
                Vector3 exit = home.transform.TransformPoint(new Vector3(0, 10, 90));
                inp.aimPoint = exit;
                Steer(v, exit, 0.55f, ref inp, 3f);
                inp.climb = 1;
                v.seatIn[0] = inp;
                return;
            }

            if (think <= 0)
            {
                think = 0.4f;
                if (job == SpaceJob.Dogfight || job == SpaceJob.Escort || target == null)
                {
                    var t = FindTarget(pos, job == SpaceJob.Dogfight ? 1100 : 450, true, true);
                    if (t != null) target = t;
                }
                if (job == SpaceJob.Bomb && (sysTarget == null || sysTarget.destroyed)) sysTarget = PickSystem(enemy);
                if (job == SpaceJob.Bomb && sysTarget == null) job = SpaceJob.Dogfight;
                chased = false;
                foreach (var o in b.vehicles)
                {
                    if (!o || o == v || !o.Alive || !o.IsShip || o.landed || o.Team == u.team) continue;
                    Vector3 rel = pos - o.transform.position;
                    float rd = rel.magnitude;
                    if (rd > 420 || rd < 1) continue;
                    if (Vector3.Dot(v.transform.forward, -rel / rd) < -0.2f && Vector3.Dot(o.transform.forward, rel / rd) > 0.92f) { chased = true; break; }
                }
            }
            if (target != null && (target is Object to0 && !to0 || !target.Alive)) target = null;

            Vector3 point = pos + v.transform.forward * 200;
            float throttleWant = 0.85f;
            bool boost = false;

            switch (job)
            {
                case SpaceJob.Board:
                    {
                        if (!enemy.HangarOpen && Random.value < 0.01f) { job = SpaceJob.Dogfight; break; }
                        Vector3 local = enemy.transform.InverseTransformPoint(pos);
                        Vector3 inside = enemy.transform.TransformPoint(new Vector3(0, 4, 12));
                        bool aligned = Mathf.Abs(local.x) < 24 && Mathf.Abs(local.y - 9) < 9 && local.z > 0;
                        if (enemy.hangar.Contains(pos))
                        {
                            point = inside; throttleWant = 0.1f;
                            if ((v.speed < 14 && local.z < 26) || (inside - pos).magnitude < 12)   // well inside, not on the lip of the deck
                            {
                                boarded = true;
                                v.Land(enemy.hangar);
                                foreach (var r in (Unit[])v.riders.Clone()) if (r) { v.Leave(r, true); if (r.bot != null) { r.bot.job = SpaceJob.Sabotage; r.bot.boarded = true; } }
                                return;
                            }
                        }
                        else if (!aligned || local.z > 320)
                        {
                            // line up on the hangar axis well out in front of it
                            point = enemy.transform.TransformPoint(new Vector3(0, 9, Mathf.Max(200, local.z * 0.6f)));
                            if (local.z > 160 && Mathf.Abs(local.x) < 60 && local.z < 360) point = enemy.transform.TransformPoint(new Vector3(0, 9, local.z - 80));
                            throttleWant = 0.9f; boost = (point - pos).magnitude > 500;
                        }
                        else
                        {
                            point = enemy.transform.TransformPoint(new Vector3(0, 9, Mathf.Max(0, local.z - 60)));
                            throttleWant = local.z > 140 ? 0.6f : 0.28f;
                        }
                        inp.aimPoint = point;
                        Steer(v, point, throttleWant, ref inp, 3f);
                        v.seatIn[0] = inp;
                        return;
                    }
                case SpaceJob.Bomb:
                    if (sysTarget)
                    {
                        Vector3 sp = sysTarget.AimPoint;
                        float d = (sp - pos).magnitude;
                        if (run == RunPhase.Break)
                        {
                            point = runPoint; throttleWant = 1; boost = true;
                            if ((runPoint - pos).magnitude < 80) run = RunPhase.Approach;
                        }
                        else
                        {
                            point = sp; throttleWant = d < 500 ? 0.75f : 1f; boost = d > 700;
                            float ang = Vector3.Angle(v.transform.forward, sp - pos);
                            inp.fire = ang < 6 && d < 600;
                            inp.alt = ang < 5 && d < 520 && d > 60;
                            if (d < 110)
                            {
                                run = RunPhase.Break;
                                Vector3 outDir = (pos - enemy.transform.position).normalized;
                                runPoint = pos + (outDir + Random.insideUnitSphere * 0.5f).normalized * 520;
                            }
                        }
                    }
                    break;
                case SpaceJob.Escort:
                    {
                        // stay near friendly ships and pulse repairs
                        Vehicle buddy = null; float bd = float.MaxValue;
                        foreach (var o in b.vehicles) if (o && o != v && o.Alive && o.IsShip && o.Team == u.team && !o.landed) { float d2 = (o.transform.position - pos).sqrMagnitude; if (d2 < bd) { bd = d2; buddy = o; } }
                        if (v.sec != null && v.sec.Ready && v.sec.def.kind == WKind.Repair)
                        {
                            int hurt = 0;
                            foreach (var o in b.vehicles) if (o && o.Alive && o.Team == u.team && o.hp < o.maxHp * 0.8f && (o.transform.position - pos).sqrMagnitude < 60 * 60) hurt++;
                            if (hurt > 0 || v.hp < v.maxHp * 0.7f) inp.alt = true;
                        }
                        if (target != null && (target.AimPoint - pos).magnitude < 400) goto case SpaceJob.Dogfight;
                        if (buddy) { point = buddy.transform.position - buddy.transform.forward * 40 + Vector3.up * 15; throttleWant = Mathf.Clamp01(buddy.speed / v.def.speed + 0.15f); }
                        else point = enemy.transform.position + Vector3.up * 200;
                    }
                    break;
                case SpaceJob.Dogfight:
                default:
                    if (target != null)
                    {
                        Vector3 tp = target.AimPoint;
                        float d = (tp - pos).magnitude;
                        Vector3 lead = Lead(pos, target, v.prim.def.speed);
                        point = AimWithError(pos, lead, dt * 0.5f);
                        float ang = Vector3.Angle(v.transform.forward, lead - pos);
                        inp.fire = ang < 3.5f + (1 - skill) * 2.5f && d < 420 && v.prim.heat < 0.7f;
                        inp.alt = v.sec != null && ((v.sec.def.lockOn && v.lockTime >= 1f && d < 650) || (v.sec.def.kind == WKind.Shotgun && ang < 6 && d < 200));
                        throttleWant = d < 80 ? 0.5f : d > 300 ? 1f : 0.8f;
                        boost = d > 500;
                        // overshoot: if very close and facing it, break off
                        if (d < 30) point = pos + (v.transform.forward + Vector3.Cross(v.transform.forward, Vector3.up) * strafe).normalized * 200;
                        // nose to nose: aim to pass beside it rather than through it (a pilot keeps shooting until ~90 m,
                        // then slides off to the side - a joust, not a ram)
                        else if (target is Vehicle tv && tv.IsShip && d < 260 && Vector3.Dot(tv.transform.forward, (pos - tp) / Mathf.Max(1, d)) > 0.75f)
                        {
                            Vector3 side = Vector3.Cross(v.transform.forward, v.transform.up) * strafe;
                            float off = Mathf.Lerp(45, 14, Mathf.InverseLerp(90, 260, d));
                            point = point + side * off + v.transform.up * off * 0.4f;
                            if (d < 90) inp.fire = false;
                        }
                    }
                    else
                    {
                        // patrol between the fleets, drift toward the enemy ship
                        float a = Time.time * 0.12f + u.part.name.GetHashCode() % 7;
                        point = Vector3.Lerp(home.transform.position, enemy.transform.position, 0.5f + 0.25f * Mathf.Sin(a)) + new Vector3(Mathf.Cos(a) * 300, Mathf.Sin(a * 1.7f) * 120, 0);
                    }
                    break;
            }

            // nothing to shoot or bomb right now and under fire (or with someone on our tail): weave so we're hard to hit,
            // and now and then throw the evasive roll
            bool busy = inp.fire || inp.alt || job == SpaceJob.Board || (job == SpaceJob.Bomb && run != RunPhase.Break && sysTarget);
            bool hurtNow = Time.time - v.lastHurt < 2.5f;
            if (!busy && (hurtNow || chased))
            {
                float t = Time.time * (1.6f + skill * 0.8f) + weaveSeed;
                float amp = 70 + skill * 60;
                Vector3 fwd = v.transform.forward;
                Vector3 right = Vector3.Cross(Vector3.up, fwd); if (right.sqrMagnitude < 0.01f) right = v.transform.right; right.Normalize();
                Vector3 up = Vector3.Cross(fwd, right);
                point = pos + (point - pos).normalized * 200 + (right * Mathf.Sin(t) + up * Mathf.Sin(t * 1.37f + 1) * 0.7f) * amp;
                throttleWant = Mathf.Max(throttleWant, 0.95f);
                if (hurtNow && v.EvadeReadyIn <= 0 && Time.time > dodgeRollT)
                {
                    dodgeRollT = Time.time + Random.Range(4f, 9f);
                    if (Random.value < 0.25f + skill * 0.45f) { inp.evade = true; inp.evadeSide = Mathf.Sin(t) > 0 ? 1 : -1; }
                }
            }

            // bounds
            if (pos.magnitude > space.boundRadius - 150) point = Vector3.zero;
            bool avoiding = AvoidObstacles(v, ref point, 2.6f);
            if (avoiding) { inp.fire = false; throttleWant = Mathf.Min(throttleWant, 0.7f); }
            inp.aimPoint = v.transform.position + v.transform.forward * 400;
            if (target != null && job == SpaceJob.Dogfight) inp.aimPoint = Lead(pos, target, v.prim.def.speed);
            if (job == SpaceJob.Bomb && sysTarget) inp.aimPoint = sysTarget.AimPoint;
            Steer(v, point, throttleWant, ref inp);
            inp.boost = boost && !avoiding;
            if (v.hp < v.maxHp * 0.3f && job != SpaceJob.Board && Random.value < 0.002f) job = SpaceJob.Escort;
            // a missile homing on us: better pilots roll out of it more often (and wait until it is close)
            if (Shots.Incoming(v.transform, out float md, out _) > 0)
            {
                if (evadeRoll < 0) evadeRoll = Random.value;
                if (v.EvadeReadyIn <= 0 && evadeRoll < 0.2f + skill * 0.5f && md < 50 + skill * 90) { inp.evade = true; inp.evadeSide = 0; evadeRoll = 2; }
            }
            else evadeRoll = -1;
            v.seatIn[0] = inp;
        }

        // ── on foot inside a capital ship ────────────────────────────────
        /// <summary>Space, on foot in our own hangar: run to a parked craft (or a shuttle that is taking boarders) and get in.</summary>
        bool RunToShip()
        {
            var space = b.space;
            Vector3 pos = u.transform.position;
            var home = space.ships[u.team];
            if (wantVehicle && !wantVehicle.CanEnter(u)) wantVehicle = null;
            if (!wantVehicle && wantShipKind >= 0)
            {
                Vehicle best = null; float bd = float.MaxValue;
                foreach (var v in b.vehicles)
                {
                    if (!v || !v.Alive || !v.IsShip || !v.landed || !v.IsEmpty || !v.CanEnter(u) || !home.hangar.Contains(v.transform.position)) continue;
                    float d = (v.transform.position - pos).sqrMagnitude * (v.def.kind == (VKind)((int)VKind.Fighter + wantShipKind) ? 0.3f : 1f);
                    if (d < bd) { bd = d; best = v; }
                }
                wantVehicle = best;
            }
            if (!wantVehicle) { if (wantShipKind >= 0 && Time.time - u.spawnTime > 12) wantShipKind = -1; return wantShipKind >= 0; }
            Vector3 to = wantVehicle.transform.position - pos; to.y = 0;
            var ext = wantVehicle.model != null ? wantVehicle.model.bounds.extents : Vector3.one * wantVehicle.radius;
            if (to.magnitude < Mathf.Max(wantVehicle.radius, Mathf.Max(ext.x, ext.z)) + 3.2f)
            {
                if (wantVehicle.Enter(u) >= 0) { if (u.seat == 0) { PickSpaceJob(); shuttleWait = 0; } }
                wantVehicle = null; wantShipKind = -1;
                return true;
            }
            Vector3 move = to.normalized;
            move = Avoid(pos, move, Time.deltaTime);
            u.input.move = new Vector3(move.x, 0, move.z);
            u.input.aimPoint = wantVehicle.transform.position + Vector3.up;
            u.input.sprint = true; u.input.fire = false;
            return true;
        }

        void SpaceInfantry(float dt)
        {
            think -= dt; seeT -= dt; detpackT -= dt;
            var space = b.space;
            Vector3 pos = u.transform.position;
            if ((wantShipKind >= 0 || wantVehicle) && RunToShip()) return;
            var inShip = space.ShipAt(pos);
            if (!inShip) { u.input = default; u.input.aimPoint = pos + Vector3.forward; return; }
            bool enemyShip = inShip.team != u.team;
            if (enemyShip && job != SpaceJob.Sabotage) job = SpaceJob.Sabotage;

            if (think <= 0)
            {
                think = 0.3f;
                var t = FindTarget(u.Eye, 60, false);
                if (t != null) { if (t != target) trackTime = 0; target = t; seeT = 1.2f; }
                else if (seeT <= 0) target = null;
                if (job == SpaceJob.Sabotage)
                {
                    if (sysTarget == null || sysTarget.destroyed)
                    {
                        sysTarget = null;
                        var ls = inShip.Sys(SysKind.LifeSupport); var rc = inShip.Sys(SysKind.Reactor);
                        var opts = new List<Subsystem>();
                        if (ls && !ls.destroyed) opts.Add(ls);
                        if (rc && !rc.destroyed) opts.Add(rc);
                        if (opts.Count > 0) sysTarget = opts[Random.Range(0, opts.Count)];
                    }
                    navGoal = sysTarget ? (sysTarget.kind == SysKind.LifeSupport ? CapitalShip.NodeLS : CapitalShip.NodeReactor) : CapitalShip.NodeHangar;
                }
                else if (navGoal < 0 || Random.value < 0.01f)
                {
                    int[] posts = { CapitalShip.NodeLS, CapitalShip.NodeReactor, CapitalShip.NodeJunction, CapitalShip.NodeDoor, 7, 8 };
                    navGoal = posts[Random.Range(0, posts.Length)];
                }
            }
            if (target != null && (target is Object to0 && !to0 || !target.Alive)) target = null;

            Vector3 move = Vector3.zero, aimPoint = u.Eye + u.transform.forward * 20;
            bool fire = false;
            int here = inShip.NearestNode(pos);
            if (navNext < 0 || (inShip.Node(navNext) - pos).sqrMagnitude < 2.2f) navNext = inShip.NextNode(here, Mathf.Max(0, navGoal));
            Vector3 np = inShip.Node(navNext);
            Vector3 toN = np - pos; toN.y = 0;
            bool atGoal = here == navGoal && toN.magnitude < 3;

            if (target != null)
            {
                trackTime += dt;
                Vector3 lead = Lead(u.Eye, target, u.Weapon.def.speed);
                aimPoint = AimWithError(u.Eye, lead, dt);
                burst -= dt; if (burst < -0.4f) burst = Random.Range(0.6f, 1.4f);
                var w = u.Weapon;
                fire = burst > 0 && (!w.def.UsesHeat || w.heat < 0.82f) && w.def.kind != WKind.Repair && w.def.kind != WKind.Rocket;
                if (w.def.kind == WKind.Repair || (w.def.kind == WKind.Rocket && !(target is Vehicle))) u.input.swap = true;
                Vector3 to = target.AimPoint - pos; to.y = 0;
                strafeT -= dt; if (strafeT <= 0) { strafeT = Random.Range(0.7f, 1.8f); strafe = -strafe; }
                move = Vector3.Cross(Vector3.up, to.normalized) * strafe * 0.7f + (to.magnitude > 18 ? to.normalized * 0.5f : Vector3.zero);
            }
            else if (job == SpaceJob.Sabotage && sysTarget && (atGoal || (sysTarget.AimPoint - pos).magnitude < 9))
            {
                aimPoint = sysTarget.AimPoint;
                // engineers / pilots plant detpacks; marines use rockets then rifles
                if (u.cls != null && u.cls.equip == Equip.Detpack)
                {
                    if (u.equipLeft > 0 && detpackT <= 0 && (sysTarget.AimPoint - pos).magnitude < 8) { u.input.equip = true; detpackT = 1.5f; }
                    else if (u.HasDetpacksOut && detpackT <= 0 && detpackT > -0.2f) { }
                    if (u.HasDetpacksOut && u.equipLeft <= 0 && detpackT <= -1.5f) { u.input.equip = true; detpackT = 2; }
                    fire = u.Weapon.def.kind != WKind.Repair;
                    if (u.Weapon.def.kind == WKind.Repair) u.input.swap = true;
                }
                else
                {
                    if (u.weapons[1] != null && u.weapons[1].def.kind == WKind.Rocket && u.weapons[1].ammo + u.weapons[1].clip > 0 && u.cur == 0) u.input.swap = true;
                    if (u.Weapon.def.kind == WKind.Rocket && u.Weapon.ammo + u.Weapon.clip <= 0) u.input.swap = true;
                    fire = true;
                }
                u.input.fireDown = fire;
            }
            else if (!enemyShip && atGoal)
            {
                // defenders: hold and repair our own systems
                if (u.cls != null && u.cls.pilot)
                {
                    foreach (var s in inShip.systems)
                        if (!s.destroyed && s.interior && s.hp < s.maxHp && (s.AimPoint - pos).magnitude < 12)
                        {
                            if (u.Weapon.def.kind != WKind.Repair) u.input.swap = true;
                            aimPoint = s.AimPoint;
                            Vector3 tos = s.AimPoint - pos; tos.y = 0;
                            move = tos.magnitude > 4 ? tos.normalized : Vector3.zero;
                            fire = u.Weapon.def.kind == WKind.Repair;
                            break;
                        }
                }
                if (Random.value < 0.01f) navGoal = -1;
            }
            else
            {
                move = toN.magnitude > 0.3f ? toN.normalized : Vector3.zero;
                aimPoint = u.Eye + (move.sqrMagnitude > 0.01f ? move : u.transform.forward) * 20;
            }
            if (target == null && move.sqrMagnitude < 0.01f && !atGoal) move = toN.normalized;

            if (OutOfAmmo(u.Weapon) && u.weapons[1 - u.cur] != null && !OutOfAmmo(u.weapons[1 - u.cur])) u.input.swap = true;
            move = Avoid(pos, move, dt);
            u.input.move = Vector3.ClampMagnitude(new Vector3(move.x, 0, move.z), 1);
            u.input.aimPoint = aimPoint;
            u.input.fire = fire;
            u.input.sprint = target == null && move.sqrMagnitude > 0.1f;
            u.input.crouch = false;
        }
    }
}
