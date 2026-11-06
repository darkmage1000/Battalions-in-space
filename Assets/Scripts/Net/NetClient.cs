using UnityEngine;

namespace BIS.Net
{
    /// <summary>
    /// The joining side of a two-player game. Builds the same map as the host, keeps copies of the host's soldiers,
    /// vehicles, posts and capital ships up to date from snapshots and events, moves our own soldier / vehicle
    /// here so it feels instant, and sends our input and pose back. Damage, scoring and the rules are the host's.
    /// </summary>
    public static class NetClient
    {
        static Battle b;
        static float sendT;
        static int heldFlags;            // one-shot presses since the last input message
        static float lastUnitLock, lastVehLock;
        public static Battle Current => b;
        public static Vector3 DebugHostSeesMe;      // tests: where the host's snapshot puts our soldier
        public static bool Gone => b == null;

        public static void Reset() { b = null; heldFlags = 0; }

        // ── start ────────────────────────────────────────────────────────
        public static void Begin(R r)
        {
            var cfg = new BattleConfig { netClient = true };
            cfg.space = r.B();
            int land = r.U8(), sp = r.U8();
            cfg.land = Maps.Land[Mathf.Clamp(land, 0, Maps.Land.Length - 1)];
            cfg.spaceMap = Maps.Space[Mathf.Clamp(sp, 0, Maps.Space.Length - 1)];
            cfg.factions = new[] { r.U8(), r.U8() };
            cfg.tickets = r.I16(); cfg.botsPerTeam = r.U8(); cfg.difficulty = r.U8();
            int n = r.U16();
            cfg.rosterNames = new string[n]; cfg.rosterTeams = new int[n]; cfg.rosterHuman = new bool[n];
            for (int i = 0; i < n; i++) { cfg.rosterNames[i] = r.Str(); cfg.rosterTeams[i] = r.U8(); cfg.rosterHuman[i] = r.B(); }
            cfg.localIndex = r.U16();
            cfg.playerTeam = cfg.rosterTeams[Mathf.Clamp(cfg.localIndex, 0, n - 1)];
            cfg.title = "Wi-Fi battle with " + (Session.peerName ?? "the host");
            heldFlags = 0;
            Game.I.StartBattle(cfg, res => { b = null; Game.I.ShowMenu(); Game.I.menu.ShowMulti(); });
            b = Battle.I;
            Session.link?.Send(new W(Msg.Ready));
        }

        // ── every frame, before units / vehicles tick ────────────────────
        public static void PreUnits(float dt)
        {
            var link = Session.link;
            if (b == null) return;
            if (link == null || link.Closed)
            {
                if (!b.over)
                {
                    HUD.Message("Lost the connection to the host", new Color(1f, 0.6f, 0.4f), 6);
                    b.NetEnd(-1, "Lost the connection to the host", "");
                }
                Session.link = null; Session.role = Session.Role.None;
                return;
            }
            R start = null;
            while (link.TryRead(out var m))
            {
                var r = new R(m);
                if (r.type == Msg.Start) { start = r; break; }
                Handle(r);
                if (b == null) return;
            }
            if (start != null)
            {
                // the host started the next battle while we were still on this one
                var old = b;
                b = null;
                old.onEnd = null;
                old.Finish(false);
                Begin(start);
            }
        }

        static Unit UnitOf(int pid) => b != null && pid >= 0 && pid < b.people.Count ? b.people[pid].unit : null;
        static Unit Me => b.player.unit;

        static void Handle(R r)
        {
            switch (r.type)
            {
                case Msg.Snapshot: Snapshot(r); break;
                case Msg.LocalState: LocalState(r); break;
                case Msg.Stats: Stats(r); break;
                case Msg.UnitSpawn: UnitSpawn(r); break;
                case Msg.UnitDie:
                    {
                        int pid = r.U16(), kid = r.I16(); Vector3 dir = r.Dir();
                        var u = UnitOf(pid);
                        if (u && !u.dead)
                        {
                            u.ReplicaDie(dir);
                            u.part.diedAt = Time.time;
                            if (u.part.isPlayer) b.control.OnPlayerDied(UnitOf(kid));
                        }
                        break;
                    }
                case Msg.VehSpawn:
                    {
                        int id = r.U16(), fi = r.U8(), kind = r.U8(), team = r.S8(); Vector3 pos = r.V(); Quaternion rot = r.Q(); float hp = r.F(), maxHp = r.F();
                        if (b.VehicleById(id)) break;
                        var f = Factions.All[Mathf.Clamp(fi, 0, Factions.All.Length - 1)];
                        var v = Vehicle.Spawn(b, f.Vehicle((VKind)kind), f, Mathf.Max(0, team), pos, rot);
                        b.Track(v.gameObject);
                        v.netId = id; v.net = Unit.NetMode.Replica;
                        v.netPos = pos; v.netRot = rot; v.hp = hp; v.maxHp = maxHp;
                        b.vehicles.Add(v);
                        break;
                    }
                case Msg.VehGone: { int id = r.U16(); bool ex = r.B(); var v = b.VehicleById(id); if (v) v.NetGone(ex); break; }
                case Msg.Shot: Shot(r); break;
                case Msg.Fx: Fx.Explosion(r.V(), r.F(), r.B()); break;
                case Msg.Burst: { Vector3 p = r.V(); Color c = r.Col(); int n = r.U16(); float s = r.F(), z = r.F(), l = r.F(); Fx.Burst(p, c, n, s, z, l); break; }
                case Msg.Kill: { string k = r.Str(); int kt = r.S8(); string v = r.Str(); int vt = r.S8(); string w = r.Str(); HUD.Kill(string.IsNullOrEmpty(k) ? null : k, kt, v, vt, w); break; }
                case Msg.Text: { string s = r.Str(); Color c = r.Col(); float t = r.F(); string snd = r.Str(); HUD.Message(s, c, t); if (!string.IsNullOrEmpty(snd)) Sfx.Play2D(snd, 0.8f); break; }
                case Msg.Hit: { bool k = r.B(), v = r.B(); HUD.HitMarker(k, v); break; }
                case Msg.Hurt: HUD.Hurt(r.V()); break;
                case Msg.Points: HUD.Points(r.I16()); break;
                case Msg.Say: b.control.Say(r.Str()); Sfx.Play2D("deny"); break;
                case Msg.Award: b.player.awardsEarned.Add(r.Str()); break;
                case Msg.Push: { var me = Me; var v = r.V(); if (me && me.Alive && !me.InVehicle) me.Push(v); break; }
                case Msg.Warp:
                    {
                        Vector3 p = r.V(), vel = r.V();
                        var me = Me;
                        if (me && me.Alive)
                        {
                            if (me.vehicle) me.vehicle.ReleaseRider(me);
                            me.transform.position = p; me.velocity = vel;
                            me.cc.enabled = true; me.rig.SetVisible(true);
                            Physics.SyncTransforms();
                        }
                        break;
                    }
                case Msg.End: { int w = r.S8(); string why = r.Str(), mvp = r.Str(); b.NetEnd(w, why, mvp); break; }
                case Msg.Bye:
                    if (!b.over) b.NetEnd(-1, "The host left the game", "");
                    Session.link?.Close(); Session.link = null; Session.role = Session.Role.None;
                    break;
            }
        }

        static ClassDef ClassOf(FactionDef f, int ci)
        {
            if (ci == 5) return f.marine;
            if (ci == 6) return f.pilot;
            return ci >= 0 && ci < f.classes.Length ? f.classes[ci] : null;
        }

        static void UnitSpawn(R r)
        {
            int pid = r.U16(), ci = r.S8(), hi = r.S8(); Vector3 pos = r.V(); float yaw = r.F(); bool award = r.B();
            if (pid < 0 || pid >= b.people.Count) return;
            var p = b.people[pid];
            if (p.unit && !p.unit.dead) { b.units.Remove(p.unit); Object.Destroy(p.unit.gameObject); }   // swapped for a hero
            var f = b.Faction(p.team);
            var cls = hi >= 0 ? null : ClassOf(f, ci);
            var hero = hi >= 0 && hi < f.heroes.Length ? f.heroes[hi] : null;
            if (cls == null && hero == null) cls = f.classes[0];
            if (cls != null) p.awards[(int)cls.role] = award;
            var u = b.SpawnUnit(p, cls, hero, pos, yaw);
            u.net = p.isPlayer ? Unit.NetMode.GuestLocal : Unit.NetMode.Replica;
            u.netPos = pos; u.netYaw = yaw; u.netGrounded = true;
            if (p.isPlayer)
            {
                b.control.OnSpawned(u);
                if (hero != null) { Sfx.Play2D("award", 1f); HUD.Message($"You are {hero.name}!", hero.glow, 3); }
            }
        }

        static void Snapshot(R r)
        {
            r.F();
            b.teams[0].tickets = r.I16(); b.teams[1].tickets = r.I16();
            int np = r.U8();
            for (int i = 0; i < np; i++)
            {
                int own = r.S8(), cap = r.S8(); float prog = r.U8() / 255f; int p0 = r.U8(), p1 = r.U8();
                if (i < b.posts.Count) b.posts[i].NetSet(own, cap, prog, p0, p1);
            }
            int nu = r.U16();
            for (int i = 0; i < nu; i++)
            {
                int pid = r.U16(), f = r.U8(); Vector3 pos = r.V(), vel = r.V(); float yaw = r.I16() / 10f, pitch = r.S8(); int hp = r.U16();
                var u = UnitOf(pid);
                if (!u || u.dead) continue;
                if (u.part.isPlayer) DebugHostSeesMe = pos;
                u.hp = hp;
                u.netFlags = f;
                if (u.net == Unit.NetMode.Replica)
                {
                    u.netPos = pos; u.netVel = vel; u.netYaw = yaw; u.netPitch = pitch; u.netGrounded = (f & 2) != 0; u.netAge = 0;
                }
            }
            int nv = r.U16();
            var riders = new Unit[16];
            for (int i = 0; i < nv; i++)
            {
                int id = r.U16(); Vector3 pos = r.V(); Quaternion rot = r.Q(); int f = r.U8();
                Quaternion tq = (f & 1) != 0 ? r.Q() : Quaternion.identity, gq = (f & 2) != 0 ? r.Q() : Quaternion.identity, gnq = (f & 4) != 0 ? r.Q() : Quaternion.identity;
                int hp = r.U16(); float spd = r.F(), thr = r.U8() / 255f; Vector3 vel = r.V();
                int nr = r.U8();
                for (int k = 0; k < nr; k++) { int pid = r.U16(); var ru = pid == 65535 ? null : UnitOf(pid); riders[k] = ru && !ru.dead ? ru : null; }
                var v = b.VehicleById(id);
                if (!v || !v.Alive) continue;
                v.netPos = pos; v.netRot = rot; v.netTurret = tq; v.netGun = gq; v.netGunner = gnq;
                v.netSpeed = spd; v.netThrottle = thr; v.netVel = vel; v.netLanded = (f & 8) != 0; v.netAge = 0;
                if (hp > v.hp + 0.5f) v.lastRepaired = Time.time;
                v.hp = hp;
                var now = new Unit[v.riders.Length];
                for (int k = 0; k < now.Length && k < nr; k++) now[k] = riders[k];
                bool wasMine = v.riders.Length > 0 && v.riders[0] == Me && Me;
                bool inside = System.Array.IndexOf(v.riders, Me) >= 0 && Me;
                v.NetSetRiders(now);
                if (!inside && Me && System.Array.IndexOf(now, Me) >= 0) b.control.OnEntered(v);
            }
            int ns = r.U8();
            for (int i = 0; i < ns; i++)
            {
                float hull = r.F(); int n = r.U8();
                var sys = new float[n];
                for (int k = 0; k < n; k++) sys[k] = r.U16();
                if (b.space && i < b.space.ships.Length) b.space.ships[i].NetSet(hull, sys);
            }
        }

        static void ReadWeapon(R r, WeaponState ws)
        {
            if (!r.B()) return;
            float heat = r.U8() / 255f; bool over = r.B(); int clip = r.I16(), ammo = r.I16(); float rel = r.F();
            if (ws == null) return;
            ws.heat = heat; ws.overheated = over; ws.clip = clip; ws.ammo = ammo; ws.reload = rel;
        }

        static void LocalState(R r)
        {
            var me = Me;
            if (r.B())
            {
                float hp = r.F(), maxHp = r.F(); int cur = r.U8();
                bool ok = me && !me.dead;
                ReadWeapon(r, ok ? me.weapons[0] : null); ReadWeapon(r, ok ? me.weapons[1] : null);
                int eq = r.U8(); float acd = r.F(), lt = r.F(); int lid = r.I32(); float cloak = r.F();
                if (ok)
                {
                    me.hp = hp; me.maxHp = maxHp; me.equipLeft = eq; me.abilityCd = acd;
                    if (cur != me.cur && cur < 2 && me.weapons[cur] != null) { me.cur = cur; me.rig.SetWeapon(me.Weapon.def); }
                    var lv = b.VehicleById(lid);
                    me.lockTarget = lv ? lv.transform : null;
                    if (lastUnitLock < 0.9f && lt >= 0.9f && me.lockTarget) Sfx.Play2D("lock", 0.8f);
                    me.lockTime = lt; lastUnitLock = lt;
                    me.cloakUntil = cloak > 0 ? Time.time + cloak : 0;
                }
                if (r.B())
                {
                    var v = ok ? me.vehicle : null;
                    ReadWeapon(r, v ? v.prim : null); ReadWeapon(r, v ? v.sec : null); ReadWeapon(r, v ? v.gunW : null);
                    float vlt = r.F(); int vlid = r.I32(); float vhp = r.F();
                    if (v)
                    {
                        var tv = b.VehicleById(vlid);
                        v.lockTarget = tv ? tv.transform : null;
                        if (lastVehLock < 1f && vlt >= 1f && v.lockTarget) Sfx.Play2D("lock", 0.8f);
                        v.lockTime = vlt; lastVehLock = vlt;
                        v.hp = vhp;
                    }
                }
            }
            var p = b.player;
            p.points = r.I16(); p.heroReady = r.B(); p.pointsAtHero = r.I16(); p.heroCost = r.F();
        }

        static void Stats(R r)
        {
            int n = r.U16();
            for (int i = 0; i < n; i++)
            {
                int pts = r.I16(), k = r.I16(), d = r.I16(), c = r.I16(); bool hr = r.B(); int aw = r.U8(); bool eh = r.B();
                if (i >= b.people.Count) continue;
                var p = b.people[i];
                p.points = pts; p.kills = k; p.deaths = d; p.captures = c; p.heroReady = hr; p.everHero = eh;
                for (int a = 0; a < 5; a++) p.awards[a] = (aw & (1 << a)) != 0;
            }
        }

        static void Shot(R r)
        {
            var w = new WeaponDef { kind = (WKind)r.U8(), speed = r.F(), range = r.F(), boltLen = r.F(), boltWidth = r.F(), splash = r.F(), gravity = r.F(), lockOn = r.B(), guided = r.B(), sound = r.Str(), pitch = r.F() };
            Vector3 from = r.V(), dir = r.Dir(); int team = r.S8(); Color col = r.Col(); Vector3 inherit = r.V();
            int ik = r.U8(), iid = r.U16(), hid = r.I32();
            Transform ignore = null;
            bool mine = false;
            if (ik == 1) { var u = UnitOf(iid); if (u) { ignore = u.transform; mine = u.part.isPlayer; } }
            else if (ik == 2) { var v = b.VehicleById(iid); if (v) { ignore = v.transform; mine = v.net == Unit.NetMode.GuestLocal; } }
            else if (ik == 3 && b.space && iid >= 0 && iid < b.space.ships.Length) ignore = b.space.ships[iid].transform;
            var hv = hid >= 0 ? b.VehicleById(hid) : null;
            Shots.FireVisual(w, from, dir, team, ignore, col, hv ? hv.transform : null, inherit);
            if (w.kind != WKind.Grenade && !string.IsNullOrEmpty(w.sound) && ik != 3)
            {
                Fx.Muzzle(from, col);
                Sfx.Play(w.sound, from, mine ? 0.65f : 0.45f, w.pitch);
            }
        }

        // ── every frame, after the simulation: our input and pose ────────
        public static void PostFrame(float dt)
        {
            var link = Session.link;
            if (b == null || link == null || link.Closed) return;
            var me = Me;
            if (!me || !me.Alive) { heldFlags = 0; return; }
            sendT -= Time.unscaledDeltaTime;
            if (!me.InVehicle)
            {
                var i = me.input;
                int once = (i.fireDown ? 2 : 0) | (i.jumpDown ? 16 : 0) | (i.reload ? 128 : 0) | (i.swap ? 256 : 0) | (i.equip ? 512 : 0) | (i.ability ? 1024 : 0) | (i.use ? 2048 : 0);
                heldFlags |= once;
                // a Bladesworn's dash starts here at once; the host runs its damage
                if (i.ability && me.hero != null && me.hero.kind == HeroKind.Blade && me.abilityCd <= 0) me.LocalDash();
                if (sendT > 0) return;
                sendT = 1f / 60f;
                int f = heldFlags | (i.fire ? 1 : 0) | (i.aim ? 4 : 0) | (i.jump ? 8 : 0) | (i.sprint ? 32 : 0) | (i.crouch ? 64 : 0) | (me.grounded ? 4096 : 0);
                heldFlags = 0;
                link.Send(new W(Msg.Input).B(false).V(i.move).V(i.aimPoint).U16(f).F(me.spreadMul).V(me.transform.position).V(me.velocity));
            }
            else
            {
                if (sendT > 0) return;
                sendT = 1f / 60f;
                var v = me.vehicle;
                var vi = me.seat <= 1 ? v.seatIn[me.seat] : new VehicleInput { aimPoint = v.transform.position + v.transform.forward * 50 };
                bool drive = me.seat == 0 && v.net == Unit.NetMode.GuestLocal;
                int f = (vi.fire ? 1 : 0) | (vi.alt ? 2 : 0) | (vi.boost ? 4 : 0) | (vi.fromCamera ? 8 : 0) | (drive ? 16 : 0);
                var w = new W(Msg.Input).B(true).V(vi.move).V(vi.aimPoint).U8(f).F(vi.throttle).F(vi.steer.x).F(vi.steer.y).F(vi.roll).F(vi.climb).V(vi.camPos);
                if (drive)
                    w.U16(v.netId).V(v.transform.position).Q(v.transform.rotation).Q(v.turret ? v.turret.localRotation : Quaternion.identity).Q(v.gun ? v.gun.localRotation : Quaternion.identity)
                     .V(v.vel).F(v.speed).F(v.throttle).B(v.landed);
                link.Send(w);
            }
        }

        // ── requests to the host ─────────────────────────────────────────
        public static void RequestSpawn(int cls, int post, int mate, int ship, bool onFoot, int foot, bool asHero, int heroIdx)
        {
            Session.link?.Send(new W(Msg.SpawnReq).S8(cls).S8(post).I16(mate).S8(ship).B(onFoot).S8(foot).B(asHero).S8(heroIdx));
            Sfx.Play2D("ui");
        }
        public static void RequestEnter(Vehicle v) => Session.link?.Send(new W(Msg.EnterReq).U16(v.netId));
        public static void RequestExit() => Session.link?.Send(new W(Msg.ExitReq));
        public static void RequestSeat() => Session.link?.Send(new W(Msg.SeatReq));
        public static void RequestHero(int h) => Session.link?.Send(new W(Msg.HeroReq).S8(h));

        /// <summary>Our own crash, lava or fall (worked out here, applied by the host).</summary>
        public static void SelfDamage(Vehicle v, float amount, DmgType type)
        {
            Session.link?.Send(new W(Msg.SelfDmg).B(v != null).F(amount).U8((int)type));
        }
    }
}
