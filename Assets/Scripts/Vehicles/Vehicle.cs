using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    public struct VehicleInput
    {
        public Vector3 move;        // ground vehicles: world-space wish direction (|move| <= 1)
        public Vector3 aimPoint;
        public bool fire, alt, boost;
        public float throttle;      // flyers: -1..1 change of throttle
        public Vector2 steer;       // flyers: yaw (x) / pitch (y) rate -1..1
        public float roll, climb;
    }

    /// <summary>
    /// Every vehicle: hover tanks and transports (ground), gunships (air over land) and starfighters (space).
    /// Seat 0 drives and fires the main weapons, seat 1 runs the gunner turret, the rest ride along.
    /// </summary>
    public class Vehicle : MonoBehaviour, IDamageable, IRepairable
    {
        public Battle battle;
        public VehicleDef def;
        public FactionDef faction;
        public int homeTeam;
        public float hp, maxHp;
        public VModel model;
        public Transform body, turret, gun, gunnerT;
        public Unit[] riders;
        public readonly VehicleInput[] seatIn = new VehicleInput[2];
        public WeaponState prim, sec, gunW;
        public Vector3 vel;
        public float speed, throttle;
        public bool landed;
        public float IdleTime { get; private set; }
        public Transform lockTarget;
        public float lockTime;
        public float lastHurt = -99;
        public Unit lastAttacker;
        public float radius = 2;
        public CapitalShip parkedIn;
        bool destroyed;
        int muzzleIdx, gunnerIdx;
        float yaw, smokeT, crashCd, fxT;
        Quaternion tilt = Quaternion.identity;
        AudioSource hum;
        static readonly RaycastHit[] hits = new RaycastHit[16];

        public int Team => DriverOrAny != null ? DriverOrAny.team : homeTeam;
        public bool Alive => !destroyed && hp > 0;
        public bool IsVehicle => true;
        public bool CanRepair => Alive && hp < maxHp;
        public Vector3 AimPoint => transform.position + transform.up * (def.IsGround ? 0.6f : 0f);
        public Unit Driver => riders != null && riders.Length > 0 ? riders[0] : null;
        public Unit DriverOrAny { get { if (riders == null) return null; foreach (var r in riders) if (r) return r; return null; } }
        public bool IsEmpty { get { if (riders == null) return true; foreach (var r in riders) if (r) return false; return true; } }
        public bool IsShip => def.IsShip;
        public bool Flies => def.Flies;
        public Vector3 Forward => transform.forward;

        public static Vehicle Spawn(Battle b, VehicleDef d, FactionDef f, int team, Vector3 pos, Quaternion rot)
        {
            var go = new GameObject(d.name);
            go.layer = Layers.Vehicle;
            go.transform.SetPositionAndRotation(pos, rot);
            var v = go.AddComponent<Vehicle>();
            v.Init(b, d, f, team);
            return v;
        }

        void Init(Battle b, VehicleDef d, FactionDef f, int team)
        {
            battle = b; def = d; faction = f; homeTeam = team;
            maxHp = hp = d.hp * b.VehicleHpMul(team);
            riders = new Unit[d.seats];
            model = VehicleModels.Get(d, f);
            body = MakePart("body", transform, Vector3.zero, model.body, true);
            if (model.turret) { turret = MakePart("turret", body, model.turretPos, model.turret, true); }
            if (model.gun) gun = MakePart("gun", turret ? turret : body, turret ? model.gunPos - model.turretPos : model.gunPos, model.gun, true);
            if (model.gunnerTurret && d.gunner != null) gunnerT = MakePart("gunner", body, model.gunnerPos, model.gunnerTurret, true);
            var bc = gameObject.AddComponent<BoxCollider>();
            var bnd = model.bounds;
            bc.center = bnd.center; bc.size = Vector3.Max(bnd.size, Vector3.one * 1.2f);
            radius = Mathf.Max(1.2f, Mathf.Min(bnd.extents.x, bnd.extents.z) * 0.8f);
            if (d.primary != null) prim = new WeaponState(d.primary);
            if (d.secondary != null) sec = new WeaponState(d.secondary);
            if (d.gunner != null) gunW = new WeaponState(d.gunner);
            yaw = transform.eulerAngles.y;
            hum = Sfx.Loop(d.IsShip ? "fighter" : "engine", transform, 0.0f, d.IsShip ? 120 : 70);
            if (d.Flies && !d.IsShip) landed = true;
        }

        Transform MakePart(string n, Transform parent, Vector3 local, Mesh m, bool shadows)
        {
            var go = new GameObject(n);
            go.layer = Layers.Vehicle;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Mats.Lit;
            r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        // ── seats ────────────────────────────────────────────────────────
        public int FreeSeat()
        {
            for (int i = 0; i < riders.Length; i++) if (!riders[i]) return i;
            return -1;
        }

        public bool CanEnter(Unit u) => Alive && FreeSeat() >= 0 && (IsEmpty || Team == u.team) && u.hero == null;

        public int Enter(Unit u)
        {
            if (!CanEnter(u)) return -1;
            int s = FreeSeat();
            riders[s] = u;
            u.vehicle = this; u.seat = s;
            u.cc.enabled = false;
            u.rig.SetVisible(false);
            u.velocity = Vector3.zero;
            homeTeam = u.team;
            IdleTime = 0;
            if (parkedIn && s == 0) { /* still parked until throttle */ }
            if (u.part.isPlayer) Sfx.Play2D("ui2", 0.7f);
            return s;
        }

        /// <summary>Move a rider to the next free seat (Q in a vehicle).</summary>
        public bool SwitchSeat(Unit u)
        {
            int from = System.Array.IndexOf(riders, u);
            if (from < 0) return false;
            for (int k = 1; k < riders.Length; k++)
            {
                int s = (from + k) % riders.Length;
                if (!riders[s]) { riders[from] = null; riders[s] = u; u.seat = s; return true; }
            }
            return false;
        }

        public bool CanExitHere()
        {
            if (!def.IsShip) return !def.Flies || landed || AltitudeAboveGround() < 4f;
            return battle.space != null && battle.space.HangarAt(transform.position) != null && speed < 22;
        }

        public void Leave(Unit u, bool place)
        {
            for (int i = 0; i < riders.Length; i++) if (riders[i] == u) riders[i] = null;
            u.vehicle = null;
            if (!place) return;
            if (def.IsShip)
            {
                var hangar = battle.space.HangarAt(transform.position);
                if (hangar != null) { Land(hangar); }
            }
            Vector3 side = transform.right * (radius + 1.6f);
            Vector3 p = transform.position + side;
            if (Physics.CheckCapsule(p + Vector3.up * 0.4f, p + Vector3.up * 1.7f, 0.45f, Layers.MoveMask, QueryTriggerInteraction.Ignore)) p = transform.position - side;
            p.y = battle.GroundY(p) + 0.2f;
            if (def.Flies && !def.IsShip && !landed) p.y = transform.position.y - 1.5f;
            u.transform.position = p;
            u.cc.enabled = true;
            u.rig.SetVisible(true);
            u.velocity = def.IsShip ? Vector3.zero : vel * 0.3f;
            IdleTime = 0;
        }

        public void PlaceRider(Unit u)
        {
            u.transform.position = transform.position + transform.up * 0.5f;
        }

        // ── damage / repair ──────────────────────────────────────────────
        public void TakeDamage(DamageInfo d)
        {
            if (!Alive) return;
            hp -= d.amount;
            lastHurt = Time.time;
            if (d.attacker) lastAttacker = d.attacker;
            foreach (var r in riders) if (r && r.part.isPlayer) HUD.Hurt(d.point);
            if (Driver && Driver.bot != null) Driver.bot.OnVehicleHurt(d);
            if (hp <= 0) Explode(d.attacker);
        }

        public float Repair(float amount)
        {
            if (!CanRepair) return 0;
            float before = hp;
            hp = Mathf.Min(maxHp, hp + amount);
            return hp - before;
        }

        void Explode(Unit killer)
        {
            if (destroyed) return;
            destroyed = true; hp = 0;
            Fx.Explosion(transform.position, def.IsShip ? 1.6f : 2.2f, true);
            for (int i = 0; i < 6; i++) Fx.Emit(transform.position, Random.onUnitSphere * 14, new Color(1, 0.6f, 0.2f, 1), 0.6f, 1.2f);
            foreach (var r in (Unit[])riders.Clone())
                if (r) { Leave(r, false); r.vehicle = null; r.cc.enabled = false; r.Die(killer, Vector3.up); }
            battle.OnVehicleDestroyed(this, killer);
            if (hum) hum.volume = 0;
            Destroy(gameObject, 0.05f);
        }

        public void SelfDestruct()
        {
            if (destroyed || !IsEmpty) return;
            destroyed = true;
            Fx.Smoke(transform.position, 3, 2);
            Destroy(gameObject);
        }

        public float AltitudeAboveGround() => transform.position.y - battle.GroundY(transform.position);

        // ── per frame ────────────────────────────────────────────────────
        public void Tick(float dt)
        {
            if (!Alive) return;
            prim?.Tick(dt); sec?.Tick(dt); gunW?.Tick(dt);
            if (IsEmpty) IdleTime += dt; else IdleTime = 0;
            if (def.IsShip && IsEmpty && IdleTime > 90) { SelfDestruct(); return; }   // starships have no pads; tidy up abandoned ones

            var drv = Driver;
            var din = drv ? seatIn[0] : default;
            if (def.IsGround) MoveGround(din, drv != null, dt);
            else MoveFlyer(din, drv != null, dt);

            if (drv) DriverWeapons(din, dt);
            if (gunnerT && riders.Length > 1)
            {
                var g = riders[1];
                if (g) GunnerWeapons(seatIn[1], g, dt);
            }

            // effects
            float sp = def.IsGround ? vel.magnitude : speed;
            if (hum) { hum.volume = Mathf.MoveTowards(hum.volume, IsEmpty && (landed || def.IsGround) ? 0.05f : 0.35f, dt); hum.pitch = 0.8f + sp / Mathf.Max(1, def.speed) * 0.6f; }
            fxT -= dt;
            if (fxT <= 0 && model.engines != null && (!IsEmpty || !def.IsGround))
            {
                fxT = 0.04f;
                Color ec = faction.body == Body.Slender ? new Color(0.5f, 1f, 0.85f, 1) : faction.body == Body.Machine ? new Color(1f, 0.75f, 0.3f, 1) : new Color(1f, 0.55f, 0.25f, 1);
                float pw = def.IsGround ? 0.4f : Mathf.Clamp01(throttle + 0.2f);
                if (Game.Cam && (Game.Cam.transform.position - transform.position).sqrMagnitude < 250 * 250)
                    foreach (var e in model.engines)
                        Fx.Emit(transform.TransformPoint(e), -transform.forward * (2 + pw * 6) + vel * 0.8f, ec, (def.IsShip ? 0.9f : 0.6f) * (0.6f + pw), 0.12f);
            }
            if (hp < maxHp * 0.35f)
            {
                smokeT -= dt;
                if (smokeT <= 0) { smokeT = 0.12f; Fx.Smoke(transform.position + Random.insideUnitSphere, 1.6f, 1.5f); if (Random.value < 0.3f) Fx.Emit(transform.position, Random.insideUnitSphere * 2, new Color(1, 0.5f, 0.2f, 1), 0.8f, 0.3f); }
            }
        }

        void MoveGround(VehicleInput inp, bool driven, float dt)
        {
            Vector3 pos = transform.position;
            Vector3 wish = driven ? inp.move * def.speed * (inp.boost ? def.boost : 1f) : Vector3.zero;
            if (driven)
            {
                Vector3 af = inp.aimPoint - pos; af.y = 0;
                if (af.sqrMagnitude > 1)
                {
                    float targetYaw = Mathf.Atan2(af.x, af.z) * Mathf.Rad2Deg;
                    float turnRate = def.turn * (wish.sqrMagnitude > 1 ? 1f : 0.6f);
                    if (def.kind == VKind.Transport && wish.sqrMagnitude > 1) targetYaw = Mathf.Atan2(wish.x, wish.z) * Mathf.Rad2Deg;
                    yaw = Mathf.MoveTowardsAngle(yaw, targetYaw, turnRate * dt);
                }
            }
            Vector3 hv = new Vector3(vel.x, 0, vel.z);
            hv = Vector3.MoveTowards(hv, wish, (driven ? 14f : 6f) * dt);
            // obstacles
            float dist = hv.magnitude * dt;
            if (dist > 0.001f)
            {
                Vector3 dir = hv / hv.magnitude;
                Vector3 from = pos + Vector3.up * (model.hover + 0.5f) - dir * 0.5f;
                int n = Physics.SphereCastNonAlloc(from, radius, dir, hits, dist + 0.6f, Layers.MoveMask & ~(1 << Layers.Ground), QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var h = hits[i];
                    if (h.collider.transform.IsChildOf(transform) || h.distance <= 0) continue;
                    Vector3 nrm = h.normal; nrm.y = 0;
                    if (nrm.sqrMagnitude < 0.01f) continue;
                    nrm.Normalize();
                    float into = Vector3.Dot(hv, -nrm);
                    if (into > 0)
                    {
                        if (into > 9 && crashCd <= 0) { crashCd = 0.6f; TakeDamage(new DamageInfo { amount = into * 6, team = -1, type = DmgType.Crush }); Sfx.Play("thud", h.point, 0.7f, 0.6f); }
                        hv += nrm * into;
                    }
                }
            }
            crashCd -= dt;
            vel.x = hv.x; vel.z = hv.z;
            pos += hv * dt;
            float gy = battle.GroundY(pos);
            float target = gy + model.hover + Mathf.Sin(Time.time * 2 + homeTeam) * 0.05f;
            if (pos.y > target + 0.2f) { vel.y += Physics.gravity.y * dt; pos.y += vel.y * dt; if (pos.y < target) { pos.y = target; vel.y = 0; } }
            else { pos.y = Mathf.Lerp(pos.y, target, 10 * dt); vel.y = 0; }
            if (battle.land != null && !battle.land.InBounds(pos)) { Vector3 back = -pos; back.y = 0; pos += back.normalized * 6 * dt; }
            transform.position = pos;
            Vector3 n2 = battle.land != null ? battle.land.Normal(pos.x, pos.z) : Vector3.up;
            Quaternion want = Quaternion.FromToRotation(Vector3.up, n2);
            tilt = Quaternion.Slerp(tilt, want, 5 * dt);
            float lean = Vector3.Dot(hv, Quaternion.Euler(0, yaw, 0) * Vector3.right) * 0.6f;
            transform.rotation = tilt * Quaternion.Euler(0, yaw, -lean);

            // turret follows aim
            if (driven)
            {
                Vector3 to = inp.aimPoint - (turret ? turret.position : transform.position);
                if (turret)
                {
                    Vector3 local = transform.InverseTransformDirection(to); local.y = 0;
                    if (local.sqrMagnitude > 0.01f) turret.localRotation = Quaternion.RotateTowards(turret.localRotation, Quaternion.LookRotation(local), 90 * dt);
                }
                if (gun)
                {
                    Vector3 lg = (gun.parent).InverseTransformDirection(inp.aimPoint - gun.position);
                    float pitchA = -Mathf.Atan2(lg.y, new Vector2(lg.x, lg.z).magnitude) * Mathf.Rad2Deg;
                    float yawA = turret ? 0 : Mathf.Clamp(Mathf.Atan2(lg.x, lg.z) * Mathf.Rad2Deg, -35, 35);
                    gun.localRotation = Quaternion.RotateTowards(gun.localRotation, Quaternion.Euler(Mathf.Clamp(pitchA, -25, 15), yawA, 0), 120 * dt);
                }
            }

            // crush infantry
            float spd = hv.magnitude;
            if (spd > 6)
                foreach (var u in battle.units)
                {
                    if (!u || !u.Alive || u.InVehicle || u.team == Team) continue;
                    if ((u.transform.position - pos).sqrMagnitude < (radius + 1) * (radius + 1))
                    {
                        Shots.Deal(u, spd * 22 * dt * 10, null, Driver, Team, u.AimPoint, hv.normalized, DmgType.Crush);
                        u.Push(hv.normalized * 8 + Vector3.up * 3);
                    }
                }
        }

        void MoveFlyer(VehicleInput inp, bool driven, float dt)
        {
            Vector3 pos = transform.position;
            Quaternion rot = transform.rotation;
            bool space = def.IsShip;
            if (driven)
            {
                throttle = Mathf.Clamp01(throttle + inp.throttle * dt * 0.9f);
                if (landed && (throttle > 0.05f || inp.climb > 0.2f))
                {
                    landed = false;
                    if (parkedIn) { parkedIn = null; }
                    pos += transform.up * 1.2f;
                }
            }
            else if (!landed)
            {
                if (space) throttle = Mathf.MoveTowards(throttle, 0, dt * 0.2f);
                else throttle = Mathf.MoveTowards(throttle, 0, dt * 0.4f);
            }

            if (landed)
            {
                speed = 0; vel = Vector3.zero;
                if (!space && battle.land != null)
                {
                    float gy = battle.GroundY(pos) + 0.9f;
                    pos.y = Mathf.Lerp(pos.y, gy, 4 * dt);
                }
                transform.position = pos;
                return;
            }

            if (driven)
            {
                float turn = def.turn * (inp.boost ? 0.75f : 1f);
                rot *= Quaternion.Euler(-inp.steer.y * turn * dt, inp.steer.x * turn * dt, inp.roll * turn * 1.4f * dt);
                // gentle auto-level for planetary gunships
                if (!space)
                {
                    Vector3 fwd = rot * Vector3.forward;
                    Quaternion level = Quaternion.LookRotation(fwd, Vector3.up);
                    if (Mathf.Abs(inp.roll) < 0.1f) rot = Quaternion.Slerp(rot, level, dt * 1.6f);
                }
            }
            else if (!space)
            {
                // pilotless gunship sinks
                pos += Vector3.down * 5 * dt;
            }

            float target = def.speed * throttle * (driven && inp.boost ? def.boost : 1f);
            if (!space && throttle < 0.1f) target = 0;
            speed = Mathf.MoveTowards(speed, target, (space ? 22 : 16) * dt);
            Vector3 fwdDir = rot * Vector3.forward;
            vel = fwdDir * speed;
            if (!space) vel += Vector3.up * inp.climb * 9;
            Vector3 step = vel * dt;

            // collisions
            float dist = step.magnitude;
            if (dist > 0.0001f)
            {
                float r = Mathf.Max(1f, radius * 0.7f);
                int n = Physics.SphereCastNonAlloc(pos, r, step / dist, hits, dist + 0.5f, Layers.MoveMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var h = hits[i];
                    if (h.collider.transform.IsChildOf(transform)) continue;
                    if (h.distance <= 0 && h.point == Vector3.zero) continue;
                    bool soft = space && battle.space.HangarAt(pos) != null && speed < 25;
                    float into = Vector3.Dot(vel, -h.normal);
                    if (into <= 0) continue;
                    if (!soft && into > 16 && crashCd <= 0)
                    {
                        crashCd = 0.5f;
                        TakeDamage(new DamageInfo { amount = into * (space ? 14 : 12), team = -1, type = DmgType.Crush });
                        Fx.Sparks(h.point, h.normal, Color.white, 16);
                        Sfx.Play("thud", h.point, 0.9f, 0.5f);
                        if (!Alive) return;
                    }
                    // slide along what we hit; only solid scenery bleeds speed
                    step += h.normal * into * dt * 1.05f;
                    if (h.collider.gameObject.layer != Layers.Vehicle) speed = Mathf.Max(speed * (soft ? 0.9f : 0.7f), Mathf.Min(speed, 8f));
                    if (soft && h.normal.y > 0.7f && driven == false) landed = true;
                }
            }
            crashCd -= dt;
            pos += step;

            if (!space && battle.land != null)
            {
                float gy = battle.GroundY(pos);
                if (pos.y < gy + 2.2f)
                {
                    if (speed > 22 && Vector3.Dot(fwdDir, Vector3.down) > 0.25f && crashCd <= 0) { crashCd = 0.6f; TakeDamage(new DamageInfo { amount = speed * 20, team = -1, type = DmgType.Crush }); }
                    pos.y = gy + 2.2f;
                    if (!driven || (throttle < 0.08f && inp.climb <= 0)) { landed = !driven || speed < 3; }
                }
                if (pos.y > gy + 140) pos.y = gy + 140;
                if (!battle.land.InBounds(pos)) { rot = Quaternion.RotateTowards(rot, Quaternion.LookRotation(-new Vector3(pos.x, 0, pos.z)), 40 * dt); }
            }
            transform.SetPositionAndRotation(pos, rot);
        }

        public void Land(Hangar hangar)
        {
            landed = true; speed = 0; throttle = 0; vel = Vector3.zero;
            Vector3 p = transform.position;
            p.y = hangar.FloorY + model.bounds.extents.y + 0.3f - model.bounds.center.y;
            Vector3 fwd = transform.forward; fwd = hangar.ship.transform.InverseTransformDirection(fwd); fwd.y = 0;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            transform.SetPositionAndRotation(p, hangar.ship.transform.rotation * Quaternion.LookRotation(fwd.normalized));
            parkedIn = hangar.ship;
        }

        // ── weapons ──────────────────────────────────────────────────────
        Vector3 Muzzle(int i)
        {
            var arr = model.muzzles;
            if (arr == null || arr.Length == 0) return transform.position + transform.forward * 3;
            var m = arr[i % arr.Length];
            if (gun) return gun.TransformPoint(m);
            return body.TransformPoint(m);
        }

        void DriverWeapons(VehicleInput inp, float dt)
        {
            var w = prim;
            if (sec != null && sec.def.lockOn) UpdateLock(dt, sec.def);
            if (inp.fire && w != null && w.Ready && !landed)
            {
                Vector3 m = Muzzle(muzzleIdx++);
                Vector3 dir = AimDir(m, inp.aimPoint);
                Fire(w.def, m, dir, null);
                w.Spend();
            }
            if (inp.alt && sec != null && sec.Ready && !landed)
            {
                if (sec.def.kind == WKind.Repair) RepairPulse(sec.def);
                else
                {
                    Vector3 m = Muzzle(muzzleIdx++);
                    Vector3 dir = AimDir(m, inp.aimPoint);
                    for (int p = 0; p < sec.def.pellets; p++)
                    {
                        Vector3 d = dir;
                        if (sec.def.spread > 0) d = Quaternion.Euler(Random.Range(-sec.def.spread, sec.def.spread), Random.Range(-sec.def.spread, sec.def.spread), 0) * d;
                        Fire(sec.def, m, d, sec.def.lockOn && lockTime >= 1f ? lockTarget : null);
                    }
                }
                sec.Spend();
            }
        }

        Vector3 AimDir(Vector3 from, Vector3 aimPoint)
        {
            Vector3 d = aimPoint - from;
            if (d.sqrMagnitude < 1) return transform.forward;
            d.Normalize();
            if (def.Flies)
            {
                // fixed forward guns converge on the aim point only near the nose
                if (Vector3.Angle(transform.forward, d) > 10) d = transform.forward;
            }
            return d;
        }

        void GunnerWeapons(VehicleInput inp, Unit g, float dt)
        {
            Vector3 to = inp.aimPoint - gunnerT.position;
            if (to.sqrMagnitude > 0.1f)
            {
                Vector3 local = body.InverseTransformDirection(to);
                if (def.kind == VKind.Bomber && local.z > 0) local.z = -local.z * 0.2f - 0.1f;   // tail gun covers the rear
                gunnerT.localRotation = Quaternion.RotateTowards(gunnerT.localRotation, Quaternion.LookRotation(local), 200 * dt);
            }
            if (inp.fire && gunW != null && gunW.Ready && !landed)
            {
                var mz = model.gunnerMuzzles;
                Vector3 m = gunnerT.TransformPoint(mz[gunnerIdx++ % mz.Length]);
                Vector3 dir = gunnerT.forward;
                if (gunW.def.spread > 0) dir = Quaternion.Euler(Random.Range(-gunW.def.spread, gunW.def.spread), Random.Range(-gunW.def.spread, gunW.def.spread), 0) * dir;
                Shots.Fire(gunW.def, m, dir, Team, g, transform, faction.bolt, 1, null, def.Flies ? vel : Vector3.zero);
                Fx.Muzzle(m, faction.bolt);
                Sfx.Play(gunW.def.sound, m, 0.5f, gunW.def.pitch);
                gunW.Spend();
            }
        }

        void Fire(WeaponDef w, Vector3 m, Vector3 dir, Transform homing)
        {
            if (w.spread > 0 && w.pellets <= 1) dir = Quaternion.Euler(Random.Range(-w.spread, w.spread), Random.Range(-w.spread, w.spread), 0) * dir;
            Shots.Fire(w, m, dir, Team, Driver, transform, faction.bolt, 1, homing, def.Flies ? vel * 0.9f : Vector3.zero);
            Fx.Muzzle(m, faction.bolt);
            Sfx.Play(w.sound, m, Driver && Driver.part.isPlayer ? 0.6f : 0.45f, w.pitch);
        }

        void UpdateLock(float dt, WeaponDef w)
        {
            Transform best = null; float bestA = 14f;
            foreach (var v in battle.vehicles)
            {
                if (!v || v == this || !v.Alive || v.Team == Team || v.IsEmpty) continue;
                if (def.IsShip && !v.def.IsShip) continue;
                Vector3 to = v.transform.position - transform.position;
                if (to.magnitude > w.range) continue;
                float a = Vector3.Angle(transform.forward, to);
                if (a < bestA) { bestA = a; best = v.transform; }
            }
            if (best != lockTarget) { lockTarget = best; lockTime = 0; }
            else if (best)
            {
                float before = lockTime; lockTime += dt;
                if (Driver && Driver.part.isPlayer && before < 1f && lockTime >= 1f) Sfx.Play2D("lock", 0.8f);
            }
        }

        void RepairPulse(WeaponDef w)
        {
            float healed = 0;
            foreach (var v in battle.vehicles)
            {
                if (!v || !v.Alive || v.Team != Team) continue;
                if ((v.transform.position - transform.position).sqrMagnitude < w.range * w.range) { healed += v.Repair(w.damage); v.prim?.Refill(); v.sec?.Refill(); }
            }
            for (int i = 0; i < 40; i++) Fx.Emit(transform.position, Random.onUnitSphere * w.range * 0.8f, new Color(0.4f, 1f, 0.5f, 1), 1.2f, 1f);
            Sfx.Play("repair", transform.position, 0.8f, 0.7f);
            if (healed > 0 && Driver) battle.AddRepair(Driver.part, healed);
        }

        void OnDestroy()
        {
            if (battle) battle.vehicles.Remove(this);
        }
    }
}
