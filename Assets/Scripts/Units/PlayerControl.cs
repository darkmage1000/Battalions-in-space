using UnityEngine;

namespace BIS
{
    /// <summary>
    /// Turns Ctl input into UnitInput / VehicleInput for the player, runs the camera (shoulder cam on
    /// foot, orbit cam for ground vehicles, chase cam for flyers), the spawn / dead / pause / results flow.
    /// </summary>
    public class PlayerControl : MonoBehaviour
    {
        public enum Mode { Spawn, Play, Dead, Over }
        public Battle b;
        public Mode mode = Mode.Spawn;
        public bool paused, showScore, showMap, showHelp;
        public float camYaw, camPitch = 10;
        public int selClass, selPost = -1, selShip = 0, selHero = -1, selFoot = 0;
        public Participant selMate;      // spawn on this squadmate instead of a post
        public bool spaceOnFoot;
        public Vector3 aimPoint;
        public float outOfBounds;
        public Unit killer;
        float deadT, fov, camDist = 3.4f, flyRoll;
        Vector3 camVel;
        Quaternion flyCamRot = Quaternion.identity;
        Vector2 stick;
        public string notice; public float noticeT;
        public Unit Me => b.player.unit != null && b.player.unit.Alive ? b.player.unit : null;
        public static Vector3 testMove;     // self-tests only: added to the stick (world space)
        public static bool testFire;
        public static float testThrottle;
        Camera Cam => Game.Cam;

        public void Init(Battle battle)
        {
            b = battle;
            mode = Mode.Spawn;
            fov = Settings.Fov;
            if (b.land) { var hp = b.HomePost(b.player.team); if (hp) selPost = hp.index; }
            selClass = 0;
            if (b.space) { selShip = 0; spaceOnFoot = false; }
        }

        public void Say(string s, float t = 2.5f) { notice = s; noticeT = t; }

        public void OnPlayerDied(Unit by)
        {
            killer = by;
            mode = Mode.Dead; deadT = 0;
            Ctl.LockCursor(false);
        }

        public bool ClassLocked(int c) => b.cfg.lockedClasses != null && c < b.cfg.lockedClasses.Length && b.cfg.lockedClasses[c];

        public void TrySpawn(bool asHero)
        {
            if (!b.CanSpawn(b.player.team)) { Say("No command posts to spawn at!"); Sfx.Play2D("deny"); return; }
            HeroDef hero = null;
            if (asHero)
            {
                if (!b.player.heroReady) { Say("Hero not unlocked yet"); Sfx.Play2D("deny"); return; }
                hero = b.FreeHero(b.player.team, selHero);
            }
            else if (!b.space && ClassLocked(selClass)) { Say("That class is locked in this campaign - unlock it in the galaxy map"); Sfx.Play2D("deny"); return; }
            Unit u;
            if (b.land && selMate != null)
            {
                string why = b.MateBlocked(selMate.unit);
                if (why != null) { Say($"Can't spawn on {selMate.name}: {why}"); Sfx.Play2D("deny"); return; }
                float mw = b.WaitForMate(b.player, selMate);
                if (mw > 0) { Say($"Squad reinforcements in {Mathf.CeilToInt(mw)} s"); Sfx.Play2D("deny"); return; }
                if (Net.Session.IsClient) { Net.NetClient.RequestSpawn(selClass, -1, b.people.IndexOf(selMate), -1, false, 0, asHero, selHero); return; }
                u = b.SpawnOnMate(b.player, selClass, hero, selMate.unit);
            }
            else
            {
                if (b.land && selPost >= 0 && selPost < b.posts.Count && b.posts[selPost].owner == b.player.team && !b.posts[selPost].SpawnableFor(b.player.team))
                { Say($"{b.posts[selPost].postName} is under attack - pick another post or a squadmate"); Sfx.Play2D("deny"); return; }
                float pw = b.land && selPost >= 0 && selPost < b.posts.Count ? b.WaitFor(b.player, b.posts[selPost]) : b.WaitFor(b.player, null);
                if (pw > 0) { Say($"Reinforcements in {Mathf.CeilToInt(pw)} s"); Sfx.Play2D("deny"); return; }
                if (Net.Session.IsClient) { Net.NetClient.RequestSpawn(selClass, selPost, -1, selShip, spaceOnFoot, selFoot, asHero, selHero); return; }
                u = b.Spawn(b.player, b.space ? selFoot : selClass, hero, selPost, b.space && !spaceOnFoot && hero == null ? selShip : -1);
            }
            if (u == null) { Say("Can't spawn there"); Sfx.Play2D("deny"); return; }
            OnSpawned(u);
        }

        /// <summary>Wi-Fi guest: the host put us in a vehicle - point the camera its way.</summary>
        public void OnEntered(Vehicle v) { flyCamRot = v.transform.rotation; camYaw = v.transform.eulerAngles.y; }

        /// <summary>Into the battle with a fresh soldier (straight after spawning; on a Wi-Fi guest when the host's answer arrives).</summary>
        public void OnSpawned(Unit u)
        {
            mode = Mode.Play;
            camYaw = u.InVehicle ? u.vehicle.transform.eulerAngles.y : u.transform.eulerAngles.y; camPitch = 8;
            if (u.InVehicle) flyCamRot = u.vehicle.transform.rotation;
            Ctl.LockCursor(true);
            Sfx.Play2D("ui2");
        }

        void BecomeHeroNow()
        {
            var me = Me;
            if (me == null || !b.player.heroReady) return;
            if (Net.Session.IsClient) { Net.NetClient.RequestHero(selHero); return; }
            var u = b.MakeHeroNow(b.player, selHero);
            if (u == null) return;
            Sfx.Play2D("award", 1f);
            HUD.Message($"You are {u.hero.name}!", u.hero.glow, 3);
        }

        // ── per frame (called by Battle before units tick) ───────────────
        public void Tick(float dt)
        {
            if (noticeT > 0) noticeT -= dt;
            if (b.over && mode != Mode.Over) { mode = Mode.Over; Ctl.LockCursor(false); paused = false; Time.timeScale = 1; }
            if (Ctl.Pause && mode != Mode.Over)
            {
                if (showMap) showMap = false;
                else
                {
                    paused = !paused;
                    Time.timeScale = paused && !Net.Session.Active ? 0 : 1;     // a Wi-Fi game keeps going for the other player
                    Ctl.LockCursor(!paused && mode == Mode.Play);
                }
            }
            if (paused)
            {
                // the battle runs on in a Wi-Fi game: stand still and hold fire while the menu is up
                var pm = Me;
                if (Net.Session.Active && pm != null)
                {
                    if (pm.InVehicle) { if (pm.seat <= 1) pm.vehicle.seatIn[pm.seat] = new VehicleInput { aimPoint = pm.vehicle.seatIn[pm.seat].aimPoint }; }
                    else pm.input = new UnitInput { aimPoint = pm.input.aimPoint };
                }
                return;
            }
            showScore = Ctl.ScoreHeld;
            if (Ctl.Map && mode == Mode.Play) showMap = !showMap;

            switch (mode)
            {
                case Mode.Dead:
                    deadT += dt;
                    if (deadT > 3.5f) { mode = Mode.Spawn; }
                    break;
                case Mode.Spawn:
                    if (Ctl.Confirm) TrySpawn(false);
                    break;
                case Mode.Play:
                    PlayTick(dt);
                    break;
            }
        }

        void PlayTick(float dt)
        {
            var me = Me;
            assistTarget = null;
            if (flyLock != null && (me == null || !me.InVehicle || me.vehicle != lockVehicle || me.seat != 0)) DropLock(null);
            if (me == null) { if (mode == Mode.Play && b.player.unit == null) mode = Mode.Spawn; return; }
            if (Cursor.lockState != CursorLockMode.Locked && !showMap) Ctl.LockCursor(true);

            if (Ctl.Hero && b.player.heroReady && !me.IsHero) { BecomeHeroNow(); return; }

            // bounds
            Vector3 p = me.InVehicle ? me.vehicle.transform.position : me.transform.position;
            bool outside = b.land ? !b.land.InBounds(p) : b.space && p.magnitude > b.space.boundRadius;
            if (outside) { outOfBounds += dt; if (outOfBounds > 10 && !Net.Session.IsClient) { if (me.InVehicle) me.vehicle.TakeDamage(new DamageInfo { amount = 99999, team = -1 }); else me.Kill("deserted"); } }
            else outOfBounds = 0;

            if (me.InVehicle) VehicleTick(me, dt);
            else FootTick(me, dt);
        }

        Vector3 CameraAim(Transform ignore, float maxDist = 800)
        {
            var cam = Cam;
            Ray r = new Ray(cam.transform.position, cam.transform.forward);
            var hits = Physics.RaycastAll(r, maxDist, Layers.ShotMask, QueryTriggerInteraction.Ignore);
            float best = maxDist; Vector3 pt = r.GetPoint(maxDist);
            foreach (var h in hits)
            {
                if (ignore && h.collider.transform.IsChildOf(ignore)) continue;
                if (h.distance < 1.2f) continue;
                if (h.distance < best) { best = h.distance; pt = h.point; }
            }
            return pt;
        }

        void FootTick(Unit me, float dt)
        {
            float zoomMul = me.input.aim && me.Weapon.def.zoomFov > 0 ? 0.25f : me.input.aim ? 0.6f : 1f;
            var wd = me.Weapon.def;
            float range = wd.EffectiveRange;
            float lookMul = Assist(me.transform, range, wd.kind == WKind.Beam || wd.kind == WKind.Blade || wd.Hitscan ? 0 : wd.speed, true, true, false, dt, wd.kind != WKind.Blade);
            camYaw += Ctl.Look.x * zoomMul * lookMul;
            camPitch = Mathf.Clamp(camPitch - Ctl.Look.y * zoomMul * lookMul, -70, 75);
            Vector3 fwd = Quaternion.Euler(0, camYaw, 0) * Vector3.forward, right = Quaternion.Euler(0, camYaw, 0) * Vector3.right;
            var inp = new UnitInput
            {
                move = fwd * Ctl.Move.y + right * Ctl.Move.x,
                fire = Ctl.Fire, fireDown = Ctl.FireDown, aim = Ctl.Aim,
                jump = Ctl.Jump, jumpDown = Ctl.JumpDown, sprint = Ctl.Sprint, crouch = Ctl.Crouch,
                reload = Ctl.Reload, swap = Ctl.Swap, equip = Ctl.Equip, ability = Ctl.Ability,
            };
            aimPoint = Magnet(CameraAim(me.transform));
            inp.aimPoint = aimPoint;
            if (testMove.sqrMagnitude > 0) inp.move = Vector3.ClampMagnitude(inp.move + testMove, 1);
            if (testFire) { inp.fire = true; inp.fireDown = Time.frameCount % 10 == 0; }
            if (AutoFireOk(wd)) { inp.fire = true; inp.fireDown = true; }
            // the player's own shots are tighter than the bots', tighter still on touch and when assist has a target
            me.spreadMul = (Settings.Current == Settings.InputDevice.Touch ? 0.45f : Settings.Current == Settings.InputDevice.Gamepad ? 0.55f : 0.7f) * (assistOnTarget ? 0.6f : 1f);
            me.input = inp;
            var ws = me.Weapon;
            if (inp.aim && wd.zoomFov > 0 && ws.clip == 0 && ws.ammo > 0) Say("Lower the scope to reload", 0.3f);
            else if (wd.zoomFov > 0 && ws.clip == 0 && ws.ammo == 0) Say("Out of rifle rounds - find ammo or a supply crate", 0.3f);

            if (Ctl.Use)
            {
                Vehicle best = null; float bd = 36;
                foreach (var v in b.vehicles)
                {
                    if (!v || !v.CanEnter(me)) continue;
                    float d = (v.transform.position - me.transform.position).sqrMagnitude - v.radius * v.radius;
                    if (d < bd) { bd = d; best = v; }
                }
                if (best && Net.Session.IsClient) Net.NetClient.RequestEnter(best);
                else if (best)
                {
                    best.Enter(me);
                    flyCamRot = best.transform.rotation; camYaw = best.transform.eulerAngles.y;
                }
            }
        }

        // aim assist for the third-person cameras
        public IDamageable assistTarget;
        Vector3 assistPoint; float assistAngle = 999;

        /// <summary>Finds the assist target for the camera view, pulls the view toward it and returns the look-speed multiplier.</summary>
        float Assist(Transform ignore, float range, float shotSpeed, bool infantry, bool vehicles, bool shipsOnly, float dt, bool pull)
        {
            assistTarget = null; assistAngle = 999; assistOnTarget = false;
            if (!AimAssist.On || range <= 0) return 1;
            var cam = Cam;
            float zoom = cam.fieldOfView / Mathf.Max(20, Settings.Fov);          // smaller cones when zoomed in
            float cone = AimAssist.PullCone * zoom;
            var t = AimAssist.Find(b, b.player.team, cam.transform.position, cam.transform.forward, cone, range, shotSpeed, ignore, infantry, vehicles, shipsOnly, out assistPoint, out assistAngle);
            assistTarget = t;
            if (t == null) return 1;
            bool engaging = Ctl.Look.sqrMagnitude > 0.0001f || Ctl.Fire || Ctl.Move.sqrMagnitude > 0.01f;
            if (pull) AimAssist.PullView(ref camYaw, ref camPitch, cam.transform.position, assistPoint, AimAssist.PullRate * zoom * (engaging ? 1f : 0.35f), dt);
            return assistAngle <= AimAssist.MagnetCone * zoom * 1.3f ? AimAssist.Friction : 1;
        }

        public bool assistOnTarget;

        /// <summary>Bends the shot onto the assist target when it is inside the magnetism cone.</summary>
        Vector3 Magnet(Vector3 raw)
        {
            assistOnTarget = false;
            if (assistTarget == null || !assistTarget.Alive) return raw;
            float zoom = Cam.fieldOfView / Mathf.Max(20, Settings.Fov);
            assistOnTarget = assistAngle <= AimAssist.MagnetCone * zoom;
            return assistOnTarget ? assistPoint : raw;
        }

        /// <summary>Touch auto-fire: shoot when assist is on an enemy that is within the gun's effective range.</summary>
        bool AutoFireOk(WeaponDef w)
        {
            if (!TouchInput.Active || !Settings.AutoFire || !AimAssist.On || !assistOnTarget || assistTarget == null) return false;
            if (w.kind == WKind.Repair || w.kind == WKind.Sniper) return false;     // snipers take their own shots
            if ((w.kind == WKind.Rocket || w.kind == WKind.Cannon) && assistTarget is Unit && w.lockOn) return false;
            return true;
        }

        // target lock for gunships and starfighters
        public IDamageable flyLock;
        public Vector3 lockLead;
        Vehicle lockVehicle;
        float lockBreakT;

        static Vector3 VelOf(IDamageable t) => t is Vehicle tv ? tv.vel : t is Unit tu ? tu.velocity : Vector3.zero;

        public static string NameOf(IDamageable t)
        {
            if (t is Vehicle tv) return tv.def.name;
            if (t is Unit tu) return tu.IsHero ? tu.hero.name : tu.displayName;
            return "target";
        }

        void ToggleFlyLock(Vehicle v, Unit me, float shot)
        {
            if (flyLock != null) { DropLock("Lock released"); return; }
            bool space = v.IsShip;
            var t = AimAssist.Find(b, me.team, v.transform.position, v.transform.forward, space ? 26 : 22, space ? 1100 : 520, shot,
                                   v.transform, !space, true, space, out _, out _);
            if (t == null) { Say("No target near the crosshair", 1.5f); Sfx.Play2D("deny"); return; }
            flyLock = t; lockVehicle = v; lockBreakT = 0;
            Sfx.Play2D("lock", 0.9f); Sfx.Play2D("ui2", 0.5f);
        }

        bool LockValid(Vehicle v)
        {
            if (flyLock == null || !flyLock.Alive || !(flyLock is Component c) || !c) return false;
            if (flyLock is Unit u && u.InVehicle) return false;
            float max = v.IsShip ? 1400 : 700;
            return (flyLock.AimPoint - v.transform.position).sqrMagnitude < max * max;
        }

        public void DropLock(string why)
        {
            if (flyLock == null) return;
            flyLock = null;
            if (lockVehicle) lockVehicle.forcedLock = null;
            lockVehicle = null;
            if (why != null) { Say(why, 1.2f); Sfx.Play2D("beep", 0.5f); }
        }

        public Vehicle NearbyVehicle()
        {
            var me = Me; if (me == null || me.InVehicle) return null;
            Vehicle best = null; float bd = 36;
            foreach (var v in b.vehicles)
            {
                if (!v || !v.CanEnter(me)) continue;
                float d = (v.transform.position - me.transform.position).sqrMagnitude - v.radius * v.radius;
                if (d < bd) { bd = d; best = v; }
            }
            return best;
        }

        void VehicleTick(Unit me, float dt)
        {
            var v = me.vehicle;
            if (Ctl.Use)
            {
                if (v.CanExitHere() && Net.Session.IsClient) { Net.NetClient.RequestExit(); return; }
                if (v.CanExitHere()) { v.Leave(me, true); camYaw = v.transform.eulerAngles.y; return; }
                Say(v.IsShip ? "Fly into a hangar and slow down to get out" : "Land first to get out");
                Sfx.Play2D("deny");
            }
            if (Ctl.Swap && v.riders.Length > 1 && Net.Session.IsClient) { if (v.FreeSeat() >= 0) Net.NetClient.RequestSeat(); else { Say("No free seat", 1.5f); Sfx.Play2D("deny"); } }
            else if (Ctl.Swap && v.riders.Length > 1)
            {
                if (v.SwitchSeat(me)) { Say(me.seat == 0 ? "Driver seat" : me.seat == 1 && v.gunnerT ? "Gunner seat" : "Passenger seat", 1.5f); Sfx.Play2D("ui"); camYaw = v.transform.eulerAngles.y; }
                else { Say("No free seat", 1.5f); Sfx.Play2D("deny"); }
            }
            var inp = new VehicleInput();
            if (me.seat == 0 && v.Flies)
            {
                float k = 0.5f;
                Vector2 rawStick = new Vector2(Mathf.Clamp(Ctl.Look.x * k, -1, 1), Mathf.Clamp(Ctl.Look.y * k, -1, 1));
                stick = Vector2.Lerp(stick, rawStick, Mathf.Clamp01(dt * 18));
                inp.steer = stick;
                inp.throttle = Ctl.Throttle + testThrottle;
                if (v.Stalling) Say("Too slow to stay up - speed up or land (gunships capture posts only when landed)", 0.3f);
                inp.roll = Ctl.Roll;
                inp.climb = v.IsShip ? 0 : Ctl.Climb;
                inp.boost = Ctl.Boost;
                inp.fire = Ctl.Fire; inp.alt = Ctl.Alt;
                aimPoint = v.transform.position + v.transform.forward * 400;
                // guns converge on a target near the nose (always a little; more with aim assist on),
                // and with aim assist the stick is nudged toward it
                bool space = v.IsShip;
                float conv = AimAssist.On ? Mathf.Min(10f, Mathf.Max(4f, AimAssist.MagnetCone * (space ? 1.8f : 1.4f))) : 4f;
                float pullCone = AimAssist.On ? AimAssist.PullCone * (space ? 1.6f : 1.3f) : 0;
                float shot = v.prim != null ? v.prim.def.speed : 300;
                float effRange = v.prim != null ? Mathf.Min(space ? 650 : 350, v.prim.def.EffectiveRange) : 300;
                var t = AimAssist.Find(b, me.team, v.transform.position, v.transform.forward, Mathf.Max(conv, pullCone), effRange, shot,
                                       v.transform, !space, true, space, out var tp, out var ta);
                assistTarget = t;
                assistOnTarget = t != null && ta <= conv;
                if (t != null)
                {
                    if (ta <= conv) { aimPoint = tp; if (TouchInput.Active && Settings.AutoFire && AimAssist.On) inp.fire = true; }
                    if (ta <= pullCone)
                    {
                        Vector3 local = v.transform.InverseTransformDirection((tp - v.transform.position).normalized);
                        float g = AimAssist.PullRate / 60f;
                        stick += new Vector2(Mathf.Clamp(local.x * 6, -1, 1), Mathf.Clamp(local.y * 6, -1, 1)) * g * Mathf.Clamp01(dt * 12);
                        stick = Vector2.ClampMagnitude(stick, 1);
                        inp.steer = stick;
                    }
                }
                // target lock: the craft follows the locked target; the player's own input blends over it
                if (Ctl.Ability || Input.GetKeyDown(KeyCode.T) || Input.GetMouseButtonDown(2)) ToggleFlyLock(v, me, shot);
                if (flyLock != null && !LockValid(v)) DropLock("Lock lost");
                if (flyLock != null)
                {
                    Vector3 tpos = flyLock.AimPoint, tvel = VelOf(flyLock);
                    Vector3 to = tpos - v.transform.position;
                    float d = to.magnitude;
                    lockLead = tpos + tvel * (d / Mathf.Max(100, shot)) * 0.9f;
                    Vector3 follow = lockLead;
                    if (!space && b.land != null)
                    {
                        follow.y = Mathf.Max(follow.y, b.GroundY(follow) + 14);
                        if (v.AltitudeAboveGround() < 12) follow.y = Mathf.Max(follow.y, v.transform.position.y + 20);
                    }
                    Vector3 local = v.transform.InverseTransformDirection((follow - v.transform.position).normalized);
                    Vector2 auto = new Vector2(Mathf.Clamp(Mathf.Atan2(local.x, local.z) * 2.2f, -1, 1),
                                               Mathf.Clamp(Mathf.Atan2(local.y, Mathf.Max(0.05f, local.z)) * 2.2f, -1, 1));
                    if (local.z < 0) auto.y = local.y >= 0 ? 1 : -1;      // target behind: loop round
                    float own = rawStick.magnitude;
                    float wOwn = Mathf.Clamp01(own * 1.8f);
                    inp.steer = Vector2.Lerp(auto, stick, wOwn);
                    stick = inp.steer;
                    if (Mathf.Abs(Ctl.Roll) < 0.1f) inp.roll = Mathf.Clamp(-local.x * 1.1f - v.transform.right.y * 0.9f, -1, 1);
                    if (Mathf.Abs(Ctl.Throttle) < 0.1f)
                    {
                        // match the target's speed and hold a little behind it, so it does not shoot past
                        float hold = space ? 110 : 70;
                        float want = Mathf.Clamp((tvel.magnitude + (d - hold) * 0.35f) / Mathf.Max(1, v.def.speed), space ? 0.25f : 0.15f, 1f);
                        inp.throttle = Mathf.Clamp((want - v.throttle) * 4, -1, 1);
                        if (d > hold * 4 && local.z > 0.9f) inp.boost = true;
                    }
                    // a strong, held input breaks the lock
                    if (own > 0.75f) { lockBreakT += dt; if (lockBreakT > 0.45f) DropLock("Lock released"); } else lockBreakT = 0;
                    if (flyLock != null && Vector3.Angle(v.transform.forward, lockLead - v.transform.position) < Mathf.Max(conv, 6)) aimPoint = lockLead;
                    v.forcedLock = flyLock != null ? ((Component)flyLock).transform : null;
                }
                inp.aimPoint = aimPoint;
                v.seatIn[0] = inp;
            }
            else
            {
                var gw = me.seat == 0 ? v.prim : me.seat == 1 ? v.gunW : null;
                float lookMul = gw != null ? Assist(v.transform, gw.def.EffectiveRange, gw.def.speed, true, true, false, dt, true) : 1;
                camYaw += Ctl.Look.x * lookMul;
                camPitch = Mathf.Clamp(camPitch - Ctl.Look.y * lookMul, -40, 60);
                Vector3 fwd = Quaternion.Euler(0, camYaw, 0) * Vector3.forward, right = Quaternion.Euler(0, camYaw, 0) * Vector3.right;
                aimPoint = gw != null ? Magnet(CameraAim(v.transform)) : CameraAim(v.transform);
                inp.aimPoint = aimPoint;
                inp.fromCamera = true; inp.camPos = Cam.transform.position;
                inp.fire = Ctl.Fire || (gw != null && AutoFireOk(gw.def)); inp.alt = Ctl.Alt;
                if (me.seat == 0)
                {
                    inp.move = Vector3.ClampMagnitude(fwd * Ctl.Move.y + right * Ctl.Move.x + testMove, 1);
                    inp.boost = Ctl.Boost;
                    v.seatIn[0] = inp;
                }
                else if (me.seat == 1) v.seatIn[1] = inp;
            }
        }

        // ── camera ───────────────────────────────────────────────────────
        void LateUpdate()
        {
            if (!b || paused && mode != Mode.Play) { }
            var cam = Cam;
            if (!cam || !b) return;
            float dt = Time.unscaledDeltaTime;
            var me = b.player.unit;
            float wantFov = Settings.Fov;

            if (mode == Mode.Spawn || (mode == Mode.Over && (me == null || !me.Alive)))
            {
                OverviewCam(cam, dt);
            }
            else if (mode == Mode.Dead || (me != null && !me.Alive))
            {
                Vector3 focus = me ? me.transform.position + Vector3.up : cam.transform.position + cam.transform.forward * 10;
                if (killer && killer.Alive && deadT > 1.2f) focus = killer.InVehicle ? killer.vehicle.transform.position : killer.AimPoint;
                Vector3 want = (me ? me.transform.position : focus) + Vector3.up * 6 - Quaternion.Euler(0, camYaw, 0) * Vector3.forward * 8;
                cam.transform.position = Vector3.SmoothDamp(cam.transform.position, want, ref camVel, 0.6f, 100, dt);
                cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, Quaternion.LookRotation(focus - cam.transform.position), dt * 4);
            }
            else if (me != null && me.InVehicle)
            {
                var v = me.vehicle;
                if (v.Flies && me.seat == 0)
                {
                    flyCamRot = Quaternion.Slerp(flyCamRot, v.transform.rotation, dt * (v.IsShip ? 5f : 4f));
                    Vector3 want = v.transform.position + flyCamRot * new Vector3(0, v.model.camHeight * 0.7f, -v.model.camDist);
                    want = CamCollide(v.transform.position + v.transform.up * 1.5f, want, v.transform);
                    cam.transform.position = Vector3.Lerp(cam.transform.position, want, dt * 14);
                    cam.transform.rotation = flyCamRot * Quaternion.Euler(6, 0, 0);
                    wantFov = Settings.Fov + (Ctl.Boost ? 10 : 0);
                }
                else
                {
                    Quaternion q = Quaternion.Euler(camPitch, camYaw, 0);
                    Vector3 pivot = v.transform.position + Vector3.up * v.model.camHeight;
                    Vector3 want = pivot - q * Vector3.forward * v.model.camDist;
                    want = CamCollide(pivot, want, v.transform);
                    cam.transform.position = want;
                    cam.transform.rotation = q;
                }
            }
            else if (me != null)
            {
                bool zoom = me.input.aim && me.Weapon.def.zoomFov > 0;
                bool aiming = me.input.aim && !zoom;
                Quaternion q = Quaternion.Euler(camPitch, camYaw, 0);
                float h = me.Height * 0.92f;
                Vector3 pivot = me.transform.position + Vector3.up * h;
                float want = zoom ? 0.3f : aiming ? 2.2f : 3.9f * (me.IsHero ? 1.25f : 1f);
                camDist = Mathf.Lerp(camDist, want, dt * 12);
                Vector3 shoulder = q * new Vector3(zoom ? 0.15f : 0.85f, 0.3f, 0);
                Vector3 desired = pivot + shoulder - q * Vector3.forward * camDist;
                desired = CamCollide(pivot, desired, me.transform);
                cam.transform.position = desired;
                cam.transform.rotation = q;
                if (zoom) wantFov = me.Weapon.def.zoomFov;
                else if (aiming) wantFov = Settings.Fov * 0.8f;
                me.rig.SetVisible(!zoom);
            }
            fov = Mathf.Lerp(fov, wantFov, dt * 10);
            cam.fieldOfView = fov;
        }

        Vector3 CamCollide(Vector3 pivot, Vector3 want, Transform ignore)
        {
            Vector3 d = want - pivot;
            float len = d.magnitude;
            if (len < 0.01f) return want;
            var hits = Physics.SphereCastAll(pivot, 0.25f, d / len, len, Layers.SolidMask | (1 << Layers.Vehicle), QueryTriggerInteraction.Ignore);
            float best = len;
            foreach (var h in hits)
            {
                if (ignore && h.collider.transform.IsChildOf(ignore)) continue;
                if (h.distance < best) best = h.distance;
            }
            return pivot + d / len * Mathf.Max(0.2f, best - 0.1f);
        }

        void OverviewCam(Camera cam, float dt)
        {
            Vector3 pos; Quaternion rot;
            float t = Time.unscaledTime * 0.04f;
            if (b.land)
            {
                Vector3 focus = Vector3.zero;
                if (selMate != null && selMate.unit && selMate.unit.Alive) focus = selMate.unit.transform.position;
                else if (selPost >= 0 && selPost < b.posts.Count) focus = b.posts[selPost].Pos;
                float r = 120;
                pos = focus + new Vector3(Mathf.Cos(t) * r, 90, Mathf.Sin(t) * r);
                rot = Quaternion.LookRotation(focus - pos);
            }
            else
            {
                var ship = b.space.ships[b.player.team];
                Vector3 focus = ship.transform.position + Vector3.up * 10;
                pos = focus + ship.transform.rotation * new Vector3(Mathf.Cos(t * 2) * 380, 140, 260 + Mathf.Sin(t * 2) * 60);
                rot = Quaternion.LookRotation(focus - pos + ship.transform.forward * 300);
            }
            cam.transform.position = Vector3.SmoothDamp(cam.transform.position, pos, ref camVel, 0.8f, 2000, dt);
            cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, rot, dt * 2);
        }
    }
}
