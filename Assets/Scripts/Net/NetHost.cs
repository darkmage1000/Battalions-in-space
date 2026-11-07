using System.Collections.Generic;
using UnityEngine;

namespace BIS.Net
{
    /// <summary>
    /// The hosting side of a two-player game. The battle runs as normal; this takes the guest's input and the pose
    /// of the soldier / vehicle they move on their own device, answers their requests (spawn, enter, leave, seat,
    /// hero) with the same rules the local player gets, and sends snapshots and events.
    /// </summary>
    public static partial class NetHost
    {
        static Battle b;
        static Participant remote;
        static bool ready;
        static float snapT, localT, statsT, oob;
        static UnitInput footIn;
        static VehicleInput vehIn;
        static float spreadMul = 1;
        public static bool Active => Session.IsHost && b != null && Session.link != null && !Session.link.Closed;
        public static Participant Remote => remote;

        public static void Reset() { b = null; remote = null; ready = false; footIn = default; vehIn = default; }

        /// <summary>Called at the end of Battle.Build on the host: tell the guest what to build.</summary>
        public static void Begin(Battle battle)
        {
            b = battle; ready = false; footIn = default; vehIn = default;
            remote = battle.RemoteHuman;
            var c = battle.cfg;
            var w = new W(Msg.Start).B(c.space).U8(Mathf.Max(0, System.Array.IndexOf(Maps.Land, c.land))).U8(Mathf.Max(0, System.Array.IndexOf(Maps.Space, c.spaceMap)))
                .U8(c.factions[0]).U8(c.factions[1]).I16(c.tickets).U8(c.botsPerTeam).U8(c.difficulty);
            w.U16(battle.people.Count);
            foreach (var p in battle.people) w.Str(p.name).U8(p.team).B(p.Human);
            w.U16(remote != null ? battle.people.IndexOf(remote) : 0);
            w.B(c.royale);
            if (c.royale)
            {
                w.B(c.royaleClasses).U8(c.royalePlayers);
                foreach (var p in battle.people) w.U8(Mathf.Max(0, p.faction)).U8(p.royaleClass).B(p.boss);
            }
            Session.link?.Send(w);
        }

        static int Pid(Participant p) => p == null || b == null ? -1 : b.people.IndexOf(p);
        static int Pid(Unit u) => u ? Pid(u.part) : -1;

        // ── every frame, before units / vehicles tick ────────────────────
        public static void PreUnits(float dt)
        {
            if (b == null) return;
            var link = Session.link;
            if (link == null) return;
            if (link.Closed) { GuestLeft(); return; }
            while (link.TryRead(out var m)) Handle(new R(m));
            if (remote == null) return;
            var u = remote.unit;
            if (!u || !u.Alive) return;
            if (u.InVehicle)
            {
                if (u.seat <= 1) u.vehicle.seatIn[u.seat] = vehIn;
            }
            else
            {
                u.input = footIn;
                u.spreadMul = spreadMul;
                footIn.jumpDown = footIn.swap = footIn.equip = footIn.ability = footIn.reload = footIn.use = footIn.fireDown = false;
            }
            // the same out-of-bounds rule the local player has
            Vector3 p = u.InVehicle ? u.vehicle.transform.position : u.transform.position;
            bool outside = b.land ? !b.land.InBounds(p) : b.space && p.magnitude > b.space.boundRadius;
            if (outside) { oob += dt; if (oob > 10) { oob = 0; if (u.InVehicle) u.vehicle.TakeDamage(new DamageInfo { amount = 99999, team = -1 }); else u.Kill("deserted"); } }
            else oob = 0;
        }

        /// <summary>The guest went away: their soldier falls and a bot takes their place from the next spawn.</summary>
        static void GuestLeft()
        {
            var link = Session.link;
            Session.link = null;
            link?.Close();
            Session.peerName = null;
            if (remote == null) return;
            if (remote.unit && remote.unit.Alive) { remote.unit.net = Unit.NetMode.None; remote.unit.Kill("left the game"); }
            HUD.Message(remote.name + " left the game - a bot takes their place", new Color(1f, 0.7f, 0.4f), 4);
            remote.remote = false; remote.respawn = 3;
            remote = null;
        }

        static void Handle(R r)
        {
            switch (r.type)
            {
                case Msg.Ready: ready = true; SendFullState(); break;
                case Msg.Input:
                    {
                        bool inVeh = r.B();
                        var u = remote?.unit;
                        if (!inVeh)
                        {
                            var i = new UnitInput { move = r.V(), aimPoint = r.V() };
                            int f = r.U16();
                            i.fire = (f & 1) != 0; i.fireDown = (f & 2) != 0; i.aim = (f & 4) != 0; i.jump = (f & 8) != 0; i.jumpDown = (f & 16) != 0;
                            i.sprint = (f & 32) != 0; i.crouch = (f & 64) != 0; i.reload = (f & 128) != 0; i.swap = (f & 256) != 0; i.equip = (f & 512) != 0;
                            i.ability = (f & 1024) != 0; i.use = (f & 2048) != 0;
                            bool grounded = (f & 4096) != 0;
                            float sm = r.F(); Vector3 pos = r.V(), vel = r.V();
                            spreadMul = sm;
                            // keep one-shot presses until a frame has used them
                            i.jumpDown |= footIn.jumpDown; i.swap |= footIn.swap; i.equip |= footIn.equip; i.ability |= footIn.ability; i.reload |= footIn.reload; i.use |= footIn.use; i.fireDown |= footIn.fireDown;
                            footIn = i;
                            if (u && u.Alive && !u.InVehicle)
                            {
                                u.netPos = pos; u.netVel = vel; u.netGrounded = grounded;
                                u.netFlags = i.crouch ? 1 : 0;
                            }
                        }
                        else
                        {
                            var v = new VehicleInput { move = r.V(), aimPoint = r.V() };
                            int f = r.U8();
                            v.fire = (f & 1) != 0; v.alt = (f & 2) != 0; v.boost = (f & 4) != 0; v.fromCamera = (f & 8) != 0;
                            v.throttle = r.F(); v.steer = new Vector2(r.F(), r.F()); v.roll = r.F(); v.climb = r.F(); v.camPos = r.V();
                            vehIn = v;
                            if ((f & 16) != 0)
                            {
                                // the guest drives: where their vehicle is
                                int id = r.U16(); Vector3 pos = r.V(); Quaternion rot = r.Q(), tq = r.Q(), gq = r.Q(); Vector3 vel = r.V();
                                float spd = r.F(), thr = r.F(); bool landed = r.B();
                                var veh = u && u.Alive && u.InVehicle && u.seat == 0 ? u.vehicle : null;
                                if (veh && veh.netId == id)
                                {
                                    veh.netPos = pos; veh.netRot = rot; veh.netTurret = tq; veh.netGun = gq; veh.netVel = vel;
                                    veh.netSpeed = spd; veh.netThrottle = thr; veh.netLanded = landed;
                                }
                            }
                        }
                        break;
                    }
                case Msg.SpawnReq:
                    {
                        int cls = r.S8(), post = r.S8(), mate = r.I16(), ship = r.S8(); bool onFoot = r.B(); int foot = r.S8(); bool asHero = r.B(); int heroIdx = r.S8();
                        string why = Spawn(cls, post, mate, ship, onFoot, foot, asHero, heroIdx);
                        if (why != null) Session.link?.Send(new W(Msg.Say).Str(why));
                        break;
                    }
                case Msg.EnterReq:
                    {
                        int id = r.U16();
                        var u = remote?.unit; if (!u || !u.Alive || u.InVehicle) break;
                        var v = b.VehicleById(id);
                        if (v && v.CanEnter(u) && (v.transform.position - u.transform.position).magnitude < v.radius + 7) v.Enter(u);
                        else Session.link?.Send(new W(Msg.Say).Str("Can't get in"));
                        break;
                    }
                case Msg.ExitReq: { var u = remote?.unit; if (u && u.Alive && u.InVehicle && u.vehicle.CanExitHere()) u.vehicle.Leave(u, true); break; }
                case Msg.SeatReq: { var u = remote?.unit; if (u && u.Alive && u.InVehicle && !u.vehicle.SwitchSeat(u)) Session.link?.Send(new W(Msg.Say).Str("No free seat")); break; }
                case Msg.HeroReq:
                    {
                        int h = r.S8();
                        if (remote != null && remote.heroReady && remote.unit && remote.unit.Alive && !remote.unit.IsHero) b.MakeHeroNow(remote, h);
                        break;
                    }
                case Msg.SelfDmg:
                    {
                        // the guest's own crash / lava / fall, worked out on their device
                        bool veh = r.B(); float amt = r.F(); var type = (DmgType)r.U8();
                        var u = remote?.unit; if (!u || !u.Alive) break;
                        if (veh) { if (u.InVehicle) u.vehicle.TakeDamage(new DamageInfo { amount = amt, team = -1, type = type, point = u.vehicle.transform.position }); }
                        else if (!u.InVehicle) u.TakeDamage(new DamageInfo { amount = amt, team = -1, type = DmgType.Environment, point = u.transform.position });
                        break;
                    }
                case Msg.Evade:
                    {
                        // the guest rolled their craft: missiles here lose it and lockers start over
                        int id = r.U16(); int side = r.S8();
                        var u = remote?.unit;
                        if (u && u.Alive && u.InVehicle && u.seat == 0 && u.vehicle.netId == id) u.vehicle.Evade(side, true);
                        break;
                    }
                case Msg.Bye: GuestLeft(); break;
                default: HandleRoyale(r); break;
            }
        }

        /// <summary>A craft on the host rolled: the guest draws the roll and its copies of missiles lose it too.</summary>
        public static void OnEvade(Vehicle v, int side)
        {
            if (!Live || v.netId <= 0) return;
            Session.link.Send(new W(Msg.Evade).U16(v.netId).S8(side));
        }

        /// <summary>The guest's spawn request, checked with the rules PlayerControl.TrySpawn uses for the local player.</summary>
        static string Spawn(int cls, int post, int mate, int ship, bool onFoot, int foot, bool asHero, int heroIdx)
        {
            var p = remote;
            if (p == null || (p.unit && p.unit.Alive) || b.over) return null;
            if (!b.CanSpawn(p.team)) return "No command posts to spawn at!";
            HeroDef hero = null;
            if (asHero) { if (!p.heroReady) return "Hero not unlocked yet"; hero = b.FreeHero(p.team, heroIdx); }
            Unit u;
            if (b.land && mate >= 0 && mate < b.people.Count)
            {
                var m = b.people[mate];
                string why = b.MateBlocked(m.unit);
                if (why != null) return $"Can't spawn on {m.name}: {why}";
                float mw = b.WaitForMate(p, m);
                if (mw > 0) return $"Squad reinforcements in {Mathf.CeilToInt(mw)} s";
                u = b.SpawnOnMate(p, Mathf.Clamp(cls, 0, 4), hero, m.unit);
            }
            else
            {
                if (b.land && post >= 0 && post < b.posts.Count && b.posts[post].owner == p.team && !b.posts[post].SpawnableFor(p.team)) return $"{b.posts[post].postName} is under attack - pick another post";
                float pw = b.land && post >= 0 && post < b.posts.Count ? b.WaitFor(p, b.posts[post]) : b.WaitFor(p, null);
                if (pw > 0) return $"Reinforcements in {Mathf.CeilToInt(pw)} s";
                u = b.Spawn(p, b.space ? foot : Mathf.Clamp(cls, 0, 4), hero, post, b.space && !onFoot && hero == null ? ship : -1);
            }
            return u == null ? "Can't spawn there" : null;
        }

        // ── every frame, after the simulation ────────────────────────────
        public static void PostFrame(float dt)
        {
            if (!Active || !ready) return;
            snapT -= dt; localT -= dt; statsT -= dt;
            if (snapT <= 0) { snapT = 0.05f; SendSnapshot(); }
            if (localT <= 0) { localT = 0.1f; SendLocal(); }
            if (statsT <= 0) { statsT = 1f; SendStats(); }
        }

        static void SendSnapshot()
        {
            var w = new W(Msg.Snapshot).F(b.time).I16(Mathf.RoundToInt(b.teams[0].tickets)).I16(Mathf.RoundToInt(b.teams[1].tickets));
            w.U8(b.posts.Count);
            foreach (var cp in b.posts) w.S8(cp.owner).S8(cp.capTeam).U8(Mathf.RoundToInt(cp.progress * 255)).U8(cp.present[0]).U8(cp.present[1]);
            var us = new List<Unit>();
            snapCount++;
            foreach (var u in b.units) if (u && u.Alive && !SkipFar(u)) us.Add(u);
            w.U16(us.Count);
            foreach (var u in us)
            {
                int f = (u.crouching ? 1 : 0) | (u.grounded ? 2 : 0) | (u.Cloaked ? 4 : 0) | (u.Blocking ? 8 : 0) | (u.InVehicle ? 16 : 0) | (u.cur == 1 ? 32 : 0)
                      | (Time.time - u.lastFire < 0.15f ? 64 : 0);
                w.U16(Pid(u)).U8(f).V(u.transform.position).V(u.velocity).I16(Mathf.RoundToInt(Mathf.Repeat(u.yaw, 360) * 10)).S8(Mathf.RoundToInt(u.pitch)).U16(Mathf.CeilToInt(u.hp));
            }
            var vs = new List<Vehicle>();
            foreach (var v in b.vehicles) if (v && v.Alive) vs.Add(v);
            w.U16(vs.Count);
            foreach (var v in vs)
            {
                int f = (v.turret ? 1 : 0) | (v.gun ? 2 : 0) | (v.gunnerT ? 4 : 0) | (v.landed ? 8 : 0);
                w.U16(v.netId).V(v.transform.position).Q(v.transform.rotation).U8(f);
                if (v.turret) w.Q(v.turret.localRotation);
                if (v.gun) w.Q(v.gun.localRotation);
                if (v.gunnerT) w.Q(v.gunnerT.localRotation);
                w.U16(Mathf.CeilToInt(v.hp)).F(v.speed).U8(Mathf.RoundToInt(v.throttle * 255)).V(v.vel);
                w.U8(v.riders.Length);
                foreach (var rd in v.riders) w.U16(rd ? Pid(rd) : 65535);
            }
            if (b.space)
            {
                w.U8(b.space.ships.Length);
                foreach (var s in b.space.ships)
                {
                    w.F(Mathf.Max(0, s.hull)).U8(s.systems.Count);
                    foreach (var x in s.systems) w.U16(Mathf.CeilToInt(Mathf.Max(0, x.hp)));
                }
            }
            else w.U8(0);
            Session.link.Send(w);
        }

        static void WriteWeapon(W w, WeaponState ws)
        {
            w.B(ws != null);
            if (ws != null) w.U8(Mathf.RoundToInt(ws.heat * 255)).B(ws.overheated).I16(ws.clip).I16(ws.ammo).F(ws.reload);
        }

        static void SendLocal()
        {
            if (remote == null) return;
            var u = remote.unit;
            var w = new W(Msg.LocalState).B(u && u.Alive);
            if (u && u.Alive)
            {
                w.F(u.hp).F(u.maxHp).U8(u.cur);
                WriteWeapon(w, u.weapons[0]); WriteWeapon(w, u.weapons[1]);
                w.U8(u.equipLeft).F(u.abilityCd).F(u.lockTime).I32(u.lockTarget ? VehicleIdOf(u.lockTarget) : -1).F(Mathf.Max(0, u.cloakUntil - Time.time));
                var v = u.vehicle;
                w.B(v != null);
                if (v != null)
                {
                    WriteWeapon(w, v.prim); WriteWeapon(w, v.sec); WriteWeapon(w, v.gunW);
                    w.F(v.lockTime).I32(v.lockTarget ? VehicleIdOf(v.lockTarget) : -1).F(v.hp);
                    float threat = v.LockOnMe(out Vector3 threatFrom);
                    w.F(threat).V(threatFrom);
                }
            }
            w.I16(remote.points).B(remote.heroReady).I16(remote.pointsAtHero).F(remote.heroCost);
            if (b.royale != null) WriteRoyaleLocal(w, u);
            Session.link.Send(w);
        }

        static int VehicleIdOf(Transform t) { var v = t ? t.GetComponent<Vehicle>() : null; return v ? v.netId : -1; }

        static void SendStats()
        {
            var w = new W(Msg.Stats).U16(b.people.Count);
            foreach (var p in b.people)
            {
                int aw = 0; for (int i = 0; i < 5; i++) if (p.awards[i]) aw |= 1 << i;
                w.I16(p.points).I16(p.kills).I16(p.deaths).I16(p.captures).B(p.heroReady).U8(aw).B(p.everHero);
            }
            Session.link.Send(w);
        }

        /// <summary>Everything that exists now, for a guest that has just finished building the map.</summary>
        static void SendFullState()
        {
            foreach (var v in b.vehicles) if (v && v.Alive) OnVehicleSpawned(v);
            foreach (var u in b.units) if (u && u.Alive) OnUnitSpawned(u);
            if (b.royale != null) SendRoyaleFull();
            SendSnapshot(); SendLocal(); SendStats();
        }

        // ── events from the game ─────────────────────────────────────────
        static bool Live => Active && ready;

        /// <summary>Class index on the wire: 0-4 the land classes, 5 the space marine, 6 the pilot, -1 a hero.</summary>
        public static int ClassIndex(Unit u)
        {
            if (u.cls == null) return -1;
            if (u.cls == u.faction.marine) return 5;
            if (u.cls == u.faction.pilot) return 6;
            return System.Array.IndexOf(u.faction.classes, u.cls);
        }

        public static void OnUnitSpawned(Unit u)
        {
            if (!Live) return;
            int hero = u.hero != null ? System.Array.IndexOf(u.faction.heroes, u.hero) : -1;
            bool award = u.cls != null && u.cls.award != null && u.weapons[0] != null && u.weapons[0].def == u.cls.award;
            Session.link.Send(new W(Msg.UnitSpawn).U16(Pid(u)).S8(ClassIndex(u)).S8(hero).V(u.transform.position).F(u.yaw).B(award));
        }

        public static void OnUnitDied(Unit u, Unit killer, Vector3 dir)
        {
            if (!Live) return;
            Session.link.Send(new W(Msg.UnitDie).U16(Pid(u)).I16(Pid(killer)).Dir(dir.sqrMagnitude > 0.01f ? dir : Vector3.forward));
        }

        public static void OnVehicleSpawned(Vehicle v)
        {
            if (!Live) return;
            int fi = System.Array.IndexOf(Factions.All, v.faction);
            Session.link.Send(new W(Msg.VehSpawn).U16(v.netId).U8(fi).U8((int)v.def.kind).S8(v.homeTeam).V(v.transform.position).Q(v.transform.rotation).F(v.hp).F(v.maxHp));
        }

        public static void OnVehicleGone(Vehicle v, bool exploded)
        {
            if (!Live) return;
            Session.link.Send(new W(Msg.VehGone).U16(v.netId).B(exploded));
        }

        public static void OnShot(WeaponDef wd, Vector3 from, Vector3 dir, int team, Transform ignore, Color col, Transform homing, Vector3 inheritVel)
        {
            if (!Live) return;
            int ik = 0, iid = 0;
            if (ignore)
            {
                var iu = ignore.GetComponent<Unit>(); var iv = ignore.GetComponent<Vehicle>(); var ic = ignore.GetComponent<CapitalShip>();
                if (iu) { ik = 1; iid = Pid(iu); } else if (iv) { ik = 2; iid = iv.netId; } else if (ic) { ik = 3; iid = ic.team; }
            }
            int hid = homing ? VehicleIdOf(homing) : -1;
            Session.link.Send(new W(Msg.Shot).U8((int)wd.kind).F(wd.speed).F(wd.range).F(wd.boltLen).F(wd.boltWidth).F(wd.splash).F(wd.gravity).B(wd.lockOn).B(wd.guided).Str(wd.sound).F(wd.pitch).F(wd.proximity)
                .V(from).Dir(dir).S8(team).Col(col).V(inheritVel).U8(ik).U16(iid).I32(hid));
        }

        public static void OnExplosion(Vector3 pos, float scale, bool big)
        {
            if (!Live) return;
            Session.link.Send(new W(Msg.Fx).V(pos).F(scale).B(big));
        }

        public static void OnBurst(Vector3 pos, Color col, int count, float speed, float size, float life)
        {
            if (!Live) return;
            Session.link.Send(new W(Msg.Burst).V(pos).Col(col).U16(count).F(speed).F(size).F(life));
        }

        public static void OnKill(string killer, int kt, string victim, int vt, string weapon)
        {
            if (!Live) return;
            Session.link.Send(new W(Msg.Kill).Str(killer).S8(kt).Str(victim).S8(vt).Str(weapon));
        }

        /// <summary>A message on the guest's screen (with an optional sound).</summary>
        public static void Text(string s, Color c, float t, string sound) { if (Live) Session.link.Send(new W(Msg.Text).Str(s).Col(c).F(t).Str(sound)); }
        public static void Hit(bool kill, bool vehicle) { if (Live) Session.link.Send(new W(Msg.Hit).B(kill).B(vehicle)); }
        /// <summary>Damage the guest dealt: the number and the target's health / armour, for its screen.</summary>
        public static void Damage(IDamageable t, Vector3 p, float hpDmg, float arDmg, bool kill)
        {
            if (!Live) return;
            var u = t as Unit; var v = t as Vehicle;
            Session.link.Send(new W(Msg.Dmg).V(p).F(hpDmg).F(arDmg).B(kill).I16(u ? Pid(u) : -1).I32(v ? v.netId : -1).F(u ? u.armor : 0));
        }
        public static void Hurt(Vector3 from) { if (Live) Session.link.Send(new W(Msg.Hurt).V(from)); }
        public static void Points(int n) { if (Live) Session.link.Send(new W(Msg.Points).I16(n)); }
        public static void Award(string name) { if (Live) Session.link.Send(new W(Msg.Award).Str(name)); }
        public static void Push(Vector3 v) { if (Live) Session.link.Send(new W(Msg.Push).V(v)); }

        /// <summary>The host moved the guest's soldier (out of a vehicle): the guest jumps it there.</summary>
        public static void Warp(Unit u) { if (Live) Session.link.Send(new W(Msg.Warp).V(u.transform.position).V(u.velocity)); }

        public static void OnEnd(int winner, string reason, string mvp)
        {
            if (!Active) return;
            Session.link.Send(new W(Msg.End).S8(winner).Str(reason).Str(mvp));
        }
    }
}
