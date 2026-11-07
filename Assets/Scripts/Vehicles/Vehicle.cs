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
        public bool fromCamera;      // the player's input: shots that the barrel can't clear start on the camera line
        public Vector3 camPos;
        public bool evade;           // starfighters: an evasive roll this frame
        public int evadeSide;        // -1 left, +1 right, 0 = pick (away from the nearest missile)
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
        public float lastHurt = -99, lastRepaired = -99;   // lastRepaired: the HUD shows a filling health bar for a while
        public Unit lastAttacker;
        public float radius = 2;
        public CapitalShip parkedIn;
        public Transform forcedLock;
        public float spawnTime;
        // Wi-Fi play: netId names the vehicle in messages. On the guest a vehicle is a Replica (drawn from snapshots) or,
        // while the guest drives it, GuestLocal (driven here, its weapons fire on the host). On the host a vehicle the
        // guest drives takes its pose from the guest (Driver.net == HostRemote).
        public int netId;
        public Unit.NetMode net;
        public Vector3 netPos, netVel;
        public Quaternion netRot = Quaternion.identity, netTurret = Quaternion.identity, netGun = Quaternion.identity, netGunner = Quaternion.identity;
        public float netSpeed, netThrottle, netAge;
        public bool netLanded;
        float lavaT;   // the player's target lock (missiles home on it)
        bool destroyed;
        int muzzleIdx, gunnerIdx;
        float yaw, smokeT, crashCd, fxT;
        // space craft brush past each other instead of meeting head on: a sideways slip when a pass would touch
        // (`CraftSlip`), and a bounce that decays after a glancing knock (`bump`)
        Vector3 bump;
        public float lastNearMiss = -99;
        public const float SlipLook = 1.1f, SlipMax = 34f, SlipMargin = 4f;
        public const float BumpDamagePer = 3f, BumpCap = 0.3f, BumpMinClose = 18f, BumpPush = 14f;
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
            if (model.mat) body.GetComponent<MeshRenderer>().sharedMaterial = model.mat;
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
            spawnTime = Time.time;
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
            if (u.net == Unit.NetMode.HostRemote) Net.NetHost.Warp(u);
        }

        public void PlaceRider(Unit u)
        {
            u.transform.position = transform.position + transform.up * 0.5f;
        }

        // ── damage / repair ──────────────────────────────────────────────
        /// <summary>Starships are shielded for a few seconds after they leave the hangar.</summary>
        public bool LaunchShield => def.IsShip && (landed || Time.time - launchTime < 6f);
        float launchTime = -99;
        bool wasLanded = true;

        public static System.Action<Vehicle, DamageInfo> DebugDamage;   // test harness only

        public void TakeDamage(DamageInfo d)
        {
            if (net == Unit.NetMode.Replica) return;
            if (net == Unit.NetMode.GuestLocal) { if (d.team == -1 && Alive) Net.NetClient.SelfDamage(this, d.amount, d.type); return; }
            if (!Alive) return;
            DebugDamage?.Invoke(this, d);
            if (LaunchShield && d.type != DmgType.Crush) d.amount *= 0.2f;
            // a boarding run: craft in the enemy's hangar lane or hangar take half damage
            if (def.IsShip && battle.space != null && d.type != DmgType.Crush && Team >= 0 && battle.space.ships[1 - Team].InHangarApproach(transform.position)) d.amount *= 0.5f;
            hp -= d.amount;
            lastHurt = Time.time;
            if (d.attacker) lastAttacker = d.attacker;
            foreach (var r in riders) if (r && r.part.isPlayer) HUD.Hurt(d.point); else if (r && r.part.remote) Net.NetHost.Hurt(d.point);
            if (Driver && Driver.bot != null) Driver.bot.OnVehicleHurt(d);
            if (hp <= 0) Explode(d.attacker);
        }

        public float Repair(float amount)
        {
            if (!CanRepair) return 0;
            float before = hp;
            hp = Mathf.Min(maxHp, hp + amount);
            if (hp > before) lastRepaired = Time.time;
            return hp - before;
        }

        void Explode(Unit killer)
        {
            if (destroyed) return;
            destroyed = true; hp = 0;
            Fx.netMute = true;      // the guest blows up its own copy when it hears VehGone
            Fx.Explosion(transform.position, def.IsShip ? 1.6f : 2.2f, true);
            Fx.netMute = false;
            for (int i = 0; i < 6; i++) Fx.Emit(transform.position, Random.onUnitSphere * 14, new Color(1, 0.6f, 0.2f, 1), 0.6f, 1.2f);
            foreach (var r in (Unit[])riders.Clone())
                if (r) { Leave(r, false); r.vehicle = null; r.cc.enabled = false; r.Die(killer, Vector3.up); }
            Net.NetHost.OnVehicleGone(this, true);
            battle.OnVehicleDestroyed(this, killer);
            if (hum) hum.volume = 0;
            Destroy(gameObject, 0.05f);
        }

        public void SelfDestruct()
        {
            if (destroyed || !IsEmpty) return;
            destroyed = true;
            Net.NetHost.OnVehicleGone(this, false);
            Fx.Smoke(transform.position, 3, 2);
            Destroy(gameObject);
        }

        public float AltitudeAboveGround() => transform.position.y - battle.GroundY(transform.position);

        // ── evasive roll (starfighters) ──────────────────────────────────
        // A barrel roll with a sideways jink. Missiles homing on the craft within EvadeBreak lose it and fly on
        // straight (one further out keeps coming, so roll when it is close), and for a moment nobody can lock on.
        public const float EvadeCooldown = 7f, EvadeTime = 0.75f, EvadeSlip = 34f, EvadeBreak = 160f, EvadeJam = 1.6f;
        public float evadeAt = -99;
        public int evadeSide = 1;
        // Wi-Fi guest: the strongest enemy lock on the vehicle we sit in, from the host's LocalState
        public float netThreat, netThreatAt = -99;
        public Vector3 netThreatFrom;
        public bool CanEvadeKind => def.IsShip;
        public bool Evading => Time.time - evadeAt < EvadeTime;
        /// <summary>Just rolled: enemies can't start a lock on this craft yet.</summary>
        public bool Jammed => Time.time - evadeAt < EvadeJam;
        public float EvadeReadyIn => Mathf.Max(0, evadeAt + EvadeCooldown - Time.time);

        /// <summary>Roll out of the way. remote = told by the other side of a Wi-Fi game (no cooldown check, not sent on).</summary>
        public bool Evade(int side, bool remote = false)
        {
            if (!CanEvadeKind || !Alive || landed) return false;
            if (!remote && EvadeReadyIn > 0) return false;
            if (side == 0)
            {
                // away from the nearest missile, else the other way to last time
                if (Shots.Incoming(transform, out _, out Vector3 at) > 0) side = Vector3.Dot(at - transform.position, transform.right) > 0 ? -1 : 1;
                else side = -evadeSide;
            }
            evadeAt = Time.time; evadeSide = side > 0 ? 1 : -1;
            int lost = Shots.LoseTarget(transform, EvadeBreak);
            foreach (var v in battle.vehicles) if (v && v.lockTarget == transform) { v.lockTarget = null; v.lockTime = 0; }
            foreach (var u in battle.units) if (u && u.lockTarget == transform) { u.lockTarget = null; u.lockTime = 0; }
            // decoy sparks thrown off behind the roll
            Vector3 back = transform.position - transform.forward * 2.5f;
            for (int i = 0; i < 18; i++) Fx.Emit(back, -transform.forward * 10 + vel * 0.5f + Random.insideUnitSphere * 12, new Color(1f, 0.85f, 0.45f, 1), 0.55f, 0.7f);
            Sfx.Play("jet", transform.position, 0.7f, 1.4f);
            var drv = Driver;
            if (drv && drv.part.isPlayer)
            {
                Sfx.Play2D("ui", 0.5f);
                if (lost > 0) HUD.Message(lost > 1 ? $"EVADED {lost} MISSILES" : "MISSILE EVADED", UI.Good, 2);
            }
            if (!remote)
            {
                if (net == Unit.NetMode.GuestLocal) Net.NetClient.SendEvade(this, evadeSide);
                else Net.NetHost.OnEvade(this, evadeSide);
            }
            return true;
        }

        /// <summary>
        /// How hard an enemy is locking on to this vehicle: 0 none, below 1 locking on, 1 locked (missiles will home).
        /// from = where the strongest lock comes from.
        /// </summary>
        public float LockOnMe(out Vector3 from)
        {
            from = transform.position;
            if (Net.Session.IsClient) { from = netThreatFrom; return Time.time - netThreatAt < 0.6f ? netThreat : 0; }
            float best = 0; int team = Team;
            foreach (var v in battle.vehicles)
            {
                if (!v || v == this || !v.Alive || v.lockTarget != transform || v.IsEmpty || v.Team == team || v.sec == null || !v.sec.def.lockOn) continue;
                float k = Mathf.Clamp01(v.lockTime);
                if (k > best) { best = k; from = v.transform.position; }
            }
            foreach (var u in battle.units)
            {
                if (!u || !u.Alive || u.InVehicle || u.lockTarget != transform || u.team == team || !u.Weapon.def.lockOn) continue;
                float k = Mathf.Clamp01(u.lockTime / 0.9f);
                if (k > best) { best = k; from = u.AimPoint; }
            }
            return best;
        }

        public const float StallFrac = 0.4f;       // gunships below this share of top speed start to sink
        /// <summary>A planetary gunship flying too slowly (after the first 4 s off the ground).</summary>
        public bool Stalling => def.Flies && !def.IsShip && !landed && Time.time - launchTime > 4f && speed < def.speed * StallFrac;

        // ── per frame ────────────────────────────────────────────────────
        public void Tick(float dt)
        {
            if (!Alive) return;
            if (net == Unit.NetMode.Replica) { ReplicaTick(dt); Effects(dt); return; }
            prim?.Tick(dt); sec?.Tick(dt); gunW?.Tick(dt);
            if (wasLanded && !landed) launchTime = Time.time;
            wasLanded = landed;
            if (IsEmpty) IdleTime += dt; else IdleTime = 0;
            if (def.IsShip && IsEmpty && IdleTime > 90 && !landed) { SelfDestruct(); return; }   // parked craft wait in their hangar   // starships have no pads; tidy up abandoned ones

            var drv = Driver;
            var din = drv ? seatIn[0] : default;
            if (drv && din.evade && drv.net != Unit.NetMode.HostRemote) Evade(din.evadeSide);
            if (drv && drv.net == Unit.NetMode.HostRemote) NetPose(dt);
            else if (def.IsGround) MoveGround(din, drv != null, dt);
            else MoveFlyer(din, drv != null, dt);
            // floating isles: a craft that sinks into the clouds is lost
            if (battle.land != null && battle.land.Floating && transform.position.y < battle.land.KillY && hp > 0)
            {
                TakeDamage(new DamageInfo { amount = maxHp * 10, team = -1, type = DmgType.Crush, point = transform.position });
                if (!Alive) return;
            }

            if (net == Unit.NetMode.GuestLocal)
            {
                // the host fires our guns; the gunner's turret is wherever the host says
                if (gunnerT) gunnerT.localRotation = Quaternion.Slerp(gunnerT.localRotation, netGunner, 1 - Mathf.Exp(-15 * dt));
            }
            else
            {
                if (drv) DriverWeapons(din, dt);
                if (gunnerT && riders.Length > 1)
                {
                    var g = riders[1];
                    if (g) GunnerWeapons(seatIn[1], g, dt);
                }
            }
            Effects(dt);
        }

        void Effects(float dt)
        {
            // the evasive barrel roll is drawn on the body only, so the craft's heading (and the camera) stay steady
            if (CanEvadeKind && body)
            {
                float e = (Time.time - evadeAt) / EvadeTime;
                if (e >= 0 && e < 1) body.localRotation = Quaternion.Euler(0, 0, -evadeSide * 360f * Mathf.SmoothStep(0, 1, e));
                else if (e < 1.5f) body.localRotation = Quaternion.identity;
            }
            float sp = def.IsGround ? vel.magnitude : speed;
            if (hum) { hum.volume = Mathf.MoveTowards(hum.volume, (IsEmpty && (landed || def.IsGround) ? 0.05f : 0.35f) * Sfx.Gain(def.IsShip ? "fighter" : "engine"), dt); hum.pitch = 0.8f + sp / Mathf.Max(1, def.speed) * 0.6f; }
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
            // ground vehicles can't climb cliffs and ridges
            if (battle.land != null && hv.sqrMagnitude > 0.01f)
            {
                Vector3 np = pos + hv * dt;
                float step = new Vector2(hv.x, hv.z).magnitude * dt;
                if ((battle.GroundY(np) - battle.GroundY(pos)) / Mathf.Max(0.001f, step) > 0.75f)
                {
                    Vector3 nn = battle.land.Normal(np.x, np.z); Vector3 nh = new Vector3(nn.x, 0, nn.z);
                    if (nh.sqrMagnitude > 0.0001f) { nh.Normalize(); float up = Vector3.Dot(hv, -nh); if (up > 0) hv += nh * up * 1.1f; }
                    else hv *= 0.2f;
                }
                if (battle.land.LiquidAt(pos.x, pos.z, out float liq, out bool lava) && battle.land.Height(pos.x, pos.z) < liq && battle.land.Top(pos.x, pos.z) < liq + 0.1f)
                {
                    if (lava) { lavaT += dt; if (lavaT > 0.25f) { lavaT = 0; TakeDamage(new DamageInfo { amount = 30, type = DmgType.Environment, team = -1, point = pos }); } }
                    else hv *= 1 - 0.6f * Mathf.Min(1, dt * 4);
                }
            }
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

            Crush(hv, pos, dt);
        }

        void Crush(Vector3 hv, Vector3 pos, float dt)
        {
            // crush infantry
            float spd = hv.magnitude;
            if (spd > 6 && !Net.Session.IsClient)
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

        // -- Wi-Fi play ----------------------------------------------------
        /// <summary>Host: the guest drives this vehicle on their device; take its pose (the guns still fire here).</summary>
        void NetPose(float dt)
        {
            Vector3 p = transform.position;
            p = (netPos - p).sqrMagnitude > 64 ? netPos : Vector3.Lerp(p, netPos, 1 - Mathf.Exp(-25 * dt));
            transform.SetPositionAndRotation(p, Quaternion.Slerp(transform.rotation, netRot, 1 - Mathf.Exp(-25 * dt)));
            if (turret) turret.localRotation = netTurret;
            if (gun) gun.localRotation = netGun;
            vel = netVel; speed = netSpeed; throttle = netThrottle;
            if (landed && !netLanded && parkedIn) parkedIn = null;
            landed = netLanded;
            yaw = transform.eulerAngles.y;
            if (def.IsGround) Crush(new Vector3(vel.x, 0, vel.z), transform.position, dt);
        }

        /// <summary>Guest: a host vehicle, drawn where the snapshots say.</summary>
        void ReplicaTick(float dt)
        {
            netAge += dt;
            Vector3 target = netPos + netVel * Mathf.Min(netAge, 0.3f);
            Vector3 p = transform.position;
            float k = 1 - Mathf.Exp(-12 * dt);
            p = (target - p).sqrMagnitude > 400 ? target : Vector3.Lerp(p, target, k);
            transform.SetPositionAndRotation(p, Quaternion.Slerp(transform.rotation, netRot, k));
            float kt = 1 - Mathf.Exp(-15 * dt);
            if (turret) turret.localRotation = Quaternion.Slerp(turret.localRotation, netTurret, kt);
            if (gun) gun.localRotation = Quaternion.Slerp(gun.localRotation, netGun, kt);
            if (gunnerT)
            {
                var g = riders.Length > 1 ? riders[1] : null;
                if (g && g.net == Unit.NetMode.GuestLocal)
                {
                    // we are the gunner: turn the turret to our crosshair here, at once
                    Vector3 to = seatIn[1].aimPoint - gunnerT.position;
                    if (to.sqrMagnitude > 0.1f)
                    {
                        Vector3 local = body.InverseTransformDirection(to);
                        if (def.kind == VKind.Bomber && local.z > 0) local.z = -local.z * 0.2f - 0.1f;
                        gunnerT.localRotation = Quaternion.RotateTowards(gunnerT.localRotation, Quaternion.LookRotation(local), 720 * dt);
                    }
                }
                else gunnerT.localRotation = Quaternion.Slerp(gunnerT.localRotation, netGunner, kt);
            }
            vel = netVel; speed = netSpeed; throttle = netThrottle; landed = netLanded;
        }

        /// <summary>Guest: we took the driver's seat - carry on from where the host had it.</summary>
        public void BeginLocal()
        {
            if (net == Unit.NetMode.GuestLocal) return;
            net = Unit.NetMode.GuestLocal;
            transform.SetPositionAndRotation(netPos, netRot);
            vel = netVel; speed = netSpeed; throttle = netThrottle; landed = netLanded;
            yaw = transform.eulerAngles.y;
            tilt = Quaternion.FromToRotation(Vector3.up, transform.up);
            wasLanded = landed;
        }

        /// <summary>Guest: who sits where, from the host's snapshot.</summary>
        public void NetSetRiders(Unit[] now)
        {
            for (int i = 0; i < riders.Length; i++)
            {
                var old = riders[i];
                if (old && System.Array.IndexOf(now, old) < 0) ReleaseRider(old);
            }
            for (int i = 0; i < riders.Length && i < now.Length; i++)
            {
                var u = now[i];
                riders[i] = u;
                if (!u) continue;
                if (u.vehicle && u.vehicle != this) u.vehicle.ReleaseRider(u);
                bool fresh = u.vehicle != this;
                u.vehicle = this; u.seat = i;
                if (fresh)
                {
                    u.cc.enabled = false; u.rig.SetVisible(false); u.velocity = Vector3.zero;
                    if (u.part.isPlayer) Sfx.Play2D("ui2", 0.7f);
                }
            }
            bool mine = riders.Length > 0 && riders[0] && riders[0].net == Unit.NetMode.GuestLocal;
            if (mine) BeginLocal(); else net = Unit.NetMode.Replica;
        }

        /// <summary>Guest: a rider got out (the host placed them).</summary>
        public void ReleaseRider(Unit u)
        {
            for (int i = 0; i < riders.Length; i++) if (riders[i] == u) riders[i] = null;
            if (u.vehicle == this) u.vehicle = null;
            if (!u.dead) { u.cc.enabled = true; u.rig.SetVisible(true); }
            if (net == Unit.NetMode.GuestLocal && (riders.Length == 0 || !riders[0] || riders[0].net != Unit.NetMode.GuestLocal)) net = Unit.NetMode.Replica;
        }

        /// <summary>Guest: the host's vehicle exploded or was recalled.</summary>
        public void NetGone(bool exploded)
        {
            if (destroyed) return;
            destroyed = true; hp = 0;
            foreach (var r in (Unit[])riders.Clone()) if (r) ReleaseRider(r);
            if (exploded)
            {
                Fx.Explosion(transform.position, def.IsShip ? 1.6f : 2.2f, true);
                for (int i = 0; i < 6; i++) Fx.Emit(transform.position, Random.onUnitSphere * 14, new Color(1, 0.6f, 0.2f, 1), 0.6f, 1.2f);
            }
            else Fx.Smoke(transform.position, 3, 2);
            if (hum) hum.volume = 0;
            battle.vehicles.Remove(this);
            Destroy(gameObject, 0.05f);
        }

        void MoveFlyer(VehicleInput inp, bool driven, float dt)
        {
            Vector3 pos = transform.position;
            Quaternion rot = transform.rotation;
            bool space = def.IsShip;
            if (driven)
            {
                throttle = Mathf.Clamp01(throttle + inp.throttle * dt * 0.9f);
                // inside any hangar the ship eases off by itself, so it can stop and its pilot can get out
                if (space && !landed)
                {
                    bool inHangar = battle.space.HangarAt(pos) != null;
                    bool mouth = false;
                    foreach (var cs in battle.space.ships)
                        if (cs && cs.InHangarApproach(pos) && cs.transform.InverseTransformPoint(pos).z < 95 && Vector3.Dot(transform.forward, -cs.transform.forward) > 0.3f) mouth = true;
                    // flying in: the ship slows itself for the last stretch and the hangar, so it can stop and its pilot can get out
                    if (inHangar && throttle > 0.12f) throttle = Mathf.MoveTowards(throttle, 0.12f, dt * 1.5f);
                    float cap = inHangar ? 18 : mouth ? 32 : 9999;
                    if (speed > cap) speed = Mathf.MoveTowards(speed, cap, 120 * dt);
                }
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
            if (space && Evading)
            {
                // the jink: about 16 m to the side over the roll
                float e = (Time.time - evadeAt) / EvadeTime;
                vel += rot * Vector3.right * (evadeSide * EvadeSlip * Mathf.Sin(e * Mathf.PI));
            }
            if (!space)
            {
                // a gunship needs forward speed to stay up: hanging still over a post it loses lift, sinks and sets down
                float lift = Stalling ? Mathf.Clamp01(speed / (def.speed * StallFrac)) : 1f;
                vel += Vector3.up * inp.climb * 9 * Mathf.Max(0.25f, lift);
                if (Stalling) vel += Vector3.down * (1 - lift) * 7f;
            }
            if (space)
            {
                vel += CraftSlip(pos);
                vel += bump;
                bump = Vector3.MoveTowards(bump, Vector3.zero, BumpPush * 1.6f * dt);
            }
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
                    var other = space ? h.collider.GetComponentInParent<Vehicle>() : null;
                    if (other && other != this && other.def.IsShip)
                    {
                        // two craft touching: a glancing knock that throws them apart, never a double kill
                        float close = Vector3.Dot(vel - other.vel, -h.normal);
                        if (close > BumpMinClose && crashCd <= 0)
                        {
                            crashCd = 0.8f;
                            TakeDamage(new DamageInfo { amount = Mathf.Min(close * BumpDamagePer, maxHp * BumpCap), team = -1, type = DmgType.Crush, point = h.point });
                            Fx.Sparks(h.point, h.normal, Color.white, 22);
                            Sfx.Play("thud", h.point, 0.9f, 0.6f);
                            if (!Alive) return;
                        }
                        bump = h.normal * BumpPush;
                        step += h.normal * (into * dt + 0.4f);
                        continue;
                    }
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
                    if (!space && Stalling && h.normal.y > 0.6f && inp.climb <= 0) { landed = true; speed = 0; }   // a stalled gunship settles on what it sinks onto
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
                // a stalled gunship that has sunk this low sets down (and can't shoot until it takes off again)
                if (Stalling && pos.y - gy < 5f && inp.climb <= 0) { landed = true; speed = 0; }
                if (!battle.land.InBounds(pos)) { rot = Quaternion.RotateTowards(rot, Quaternion.LookRotation(-new Vector3(pos.x, 0, pos.z)), 40 * dt); }
            }
            transform.SetPositionAndRotation(pos, rot);
        }

        /// <summary>Space: if another craft's pass would come within touching distance in the next ~1 s, slide
        /// sideways out of its way (both craft do it, so each moves half). Ramming on purpose still works when slow
        /// and close; flying past each other at speed no longer ends in a collision.</summary>
        Vector3 CraftSlip(Vector3 pos)
        {
            if (battle == null || battle.vehicles == null) return Vector3.zero;
            Vector3 slip = Vector3.zero;
            foreach (var o in battle.vehicles)
            {
                if (!o || o == this || !o.Alive || !o.def.IsShip || o.landed) continue;
                Vector3 p = o.transform.position - pos;
                if (p.sqrMagnitude > 160 * 160) continue;
                Vector3 rv = vel - o.vel;
                float rv2 = rv.sqrMagnitude;
                if (rv2 < 25) continue;
                float t = Vector3.Dot(p, rv) / rv2;
                if (t <= 0 || t > SlipLook) continue;
                Vector3 closest = p - rv * t;          // where the other craft will be relative to us at the closest point
                float need = radius + o.radius + SlipMargin;
                float miss = closest.magnitude;
                if (miss >= need) continue;
                Vector3 away = miss > 0.05f ? -closest / miss : Vector3.Cross(rv.normalized, Vector3.up).normalized;
                if (away.sqrMagnitude < 0.01f) away = transform.right;
                if (miss <= 0.05f && GetInstanceID() < o.GetInstanceID()) away = -away;   // dead centre: the two go opposite ways
                float sp = Mathf.Min(SlipMax, (need - miss) * 0.6f / Mathf.Max(0.18f, t));
                slip += away * sp;
                lastNearMiss = Time.time;
            }
            return Vector3.ClampMagnitude(slip, SlipMax);
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
                Vector3 m = ShotOrigin(Muzzle(muzzleIdx++), inp);
                Vector3 dir = AimDir(m, inp.aimPoint);
                Fire(w.def, m, dir, null);
                w.Spend();
            }
            if (inp.alt && sec != null && sec.Ready && !landed)
            {
                if (sec.def.kind == WKind.Repair) RepairPulse(sec.def);
                else
                {
                    Vector3 m = ShotOrigin(Muzzle(muzzleIdx++), inp);
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

        /// <summary>
        /// Where a shot by the player should start: the muzzle, unless something blocks the muzzle's line to the
        /// aim point near the vehicle while the camera's view of it is clear (low cover, a rock in front of the
        /// barrel) - then from the point of the camera line level with the muzzle, so you hit what you see.
        /// </summary>
        Vector3 ShotOrigin(Vector3 muzzle, VehicleInput inp)
        {
            if (!inp.fromCamera) return muzzle;
            Vector3 to = inp.aimPoint - muzzle; float d = to.magnitude;
            if (d < 2) return muzzle;
            const int mask = (1 << Layers.Default) | (1 << Layers.Ground) | (1 << Layers.Hull) | (1 << Layers.Vehicle);
            float near = Mathf.Min(d - 1.5f, 25f);
            foreach (var h in Physics.RaycastAll(muzzle, to / d, near, mask, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(transform)) continue;
                Vector3 cd = inp.aimPoint - inp.camPos; float cl = cd.magnitude; if (cl < 1) return muzzle;
                cd /= cl;
                float t = Mathf.Clamp(Vector3.Dot(muzzle - inp.camPos, cd), 0, cl - 1);
                return inp.camPos + cd * t;
            }
            return muzzle;
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
                gunnerT.localRotation = Quaternion.RotateTowards(gunnerT.localRotation, Quaternion.LookRotation(local), 720 * dt);
            }
            if (inp.fire && gunW != null && gunW.Ready && !landed)
            {
                var mz = model.gunnerMuzzles;
                Vector3 m = ShotOrigin(gunnerT.TransformPoint(mz[gunnerIdx++ % mz.Length]), inp);
                // shots go to the crosshair (the turret only shows where); the bomber's tail gun keeps to its rear arc
                Vector3 dir = gunnerT.forward;
                Vector3 toAim = inp.aimPoint - m;
                if (toAim.sqrMagnitude > 1 && (def.kind != VKind.Bomber || Vector3.Angle(gunnerT.forward, toAim) < 25)) dir = toAim.normalized;
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
            if (forcedLock && Vector3.Angle(transform.forward, forcedLock.position - transform.position) < 35 && (forcedLock.position - transform.position).magnitude < w.range)
            {
                var fv = forcedLock.GetComponent<Vehicle>();
                if (fv && fv.Jammed) { lockTarget = null; lockTime = 0; return; }   // it just rolled out of the lock
                best = forcedLock; bestA = -1;
                if (best != lockTarget) { lockTarget = best; lockTime = 0.4f; }
                float before0 = lockTime; lockTime += dt * 1.6f;
                if (Driver && Driver.part.isPlayer && before0 < 1f && lockTime >= 1f) Sfx.Play2D("lock", 0.8f);
                return;
            }
            foreach (var v in battle.vehicles)
            {
                if (!v || v == this || !v.Alive || v.Team == Team || v.IsEmpty || v.Jammed) continue;
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
