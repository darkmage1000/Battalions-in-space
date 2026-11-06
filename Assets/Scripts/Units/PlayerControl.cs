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
            var u = b.Spawn(b.player, b.space ? selFoot : selClass, hero, selPost, b.space && !spaceOnFoot && hero == null ? selShip : -1);
            if (u == null) { Say("Can't spawn there"); Sfx.Play2D("deny"); return; }
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
            if (me.InVehicle) me.vehicle.Leave(me, true);
            Vector3 p = me.transform.position; float yaw = me.yaw;
            var hero = b.FreeHero(b.player.team, selHero);
            b.units.Remove(me);
            Destroy(me.gameObject);
            b.player.heroReady = false; b.player.pointsAtHero = b.player.points; b.player.everHero = true;
            b.SpawnUnit(b.player, null, hero, p + Vector3.up * 0.2f, yaw);
            Fx.Burst(p + Vector3.up, hero.glow, 40, 6, 0.5f, 0.8f);
            Sfx.Play2D("award", 1f);
            HUD.Message($"You are {hero.name}!", hero.glow, 3);
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
                    Time.timeScale = paused ? 0 : 1;
                    Ctl.LockCursor(!paused && mode == Mode.Play);
                }
            }
            if (paused) return;
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
            if (me == null) { if (mode == Mode.Play && b.player.unit == null) mode = Mode.Spawn; return; }
            if (Cursor.lockState != CursorLockMode.Locked && !showMap) Ctl.LockCursor(true);

            if (Ctl.Hero && b.player.heroReady && !me.IsHero) { BecomeHeroNow(); return; }

            // bounds
            Vector3 p = me.InVehicle ? me.vehicle.transform.position : me.transform.position;
            bool outside = b.land ? !b.land.InBounds(p) : b.space && p.magnitude > b.space.boundRadius;
            if (outside) { outOfBounds += dt; if (outOfBounds > 10) { if (me.InVehicle) me.vehicle.TakeDamage(new DamageInfo { amount = 99999, team = -1 }); else me.Kill("deserted"); } }
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
            camYaw += Ctl.Look.x * zoomMul;
            camPitch = Mathf.Clamp(camPitch - Ctl.Look.y * zoomMul, -70, 75);
            Vector3 fwd = Quaternion.Euler(0, camYaw, 0) * Vector3.forward, right = Quaternion.Euler(0, camYaw, 0) * Vector3.right;
            var inp = new UnitInput
            {
                move = fwd * Ctl.Move.y + right * Ctl.Move.x,
                fire = Ctl.Fire, fireDown = Ctl.FireDown, aim = Ctl.Aim,
                jump = Ctl.Jump, jumpDown = Ctl.JumpDown, sprint = Ctl.Sprint, crouch = Ctl.Crouch,
                reload = Ctl.Reload, swap = Ctl.Swap, equip = Ctl.Equip, ability = Ctl.Ability,
            };
            aimPoint = CameraAim(me.transform);
            inp.aimPoint = aimPoint;
            me.input = inp;

            if (Ctl.Use)
            {
                Vehicle best = null; float bd = 36;
                foreach (var v in b.vehicles)
                {
                    if (!v || !v.CanEnter(me)) continue;
                    float d = (v.transform.position - me.transform.position).sqrMagnitude - v.radius * v.radius;
                    if (d < bd) { bd = d; best = v; }
                }
                if (best)
                {
                    best.Enter(me);
                    flyCamRot = best.transform.rotation; camYaw = best.transform.eulerAngles.y;
                }
            }
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
                if (v.CanExitHere()) { v.Leave(me, true); camYaw = v.transform.eulerAngles.y; return; }
                Say(v.IsShip ? "Fly into a hangar and slow down to get out" : "Land first to get out");
                Sfx.Play2D("deny");
            }
            var inp = new VehicleInput();
            if (me.seat == 0 && v.Flies)
            {
                float k = 0.5f;
                stick = Vector2.Lerp(stick, new Vector2(Mathf.Clamp(Ctl.Look.x * k, -1, 1), Mathf.Clamp(Ctl.Look.y * k, -1, 1)), Mathf.Clamp01(dt * 18));
                inp.steer = stick;
                inp.throttle = Ctl.Throttle;
                inp.roll = Ctl.Roll;
                inp.climb = v.IsShip ? 0 : Ctl.Climb;
                inp.boost = Ctl.Boost;
                inp.fire = Ctl.Fire; inp.alt = Ctl.Alt;
                aimPoint = v.transform.position + v.transform.forward * 400;
                // aim assist: converge on a ship near the nose
                float bestA = 4f;
                foreach (var o in b.vehicles)
                {
                    if (!o || o == v || !o.Alive || o.Team == me.team || o.IsEmpty) continue;
                    Vector3 to = o.transform.position - v.transform.position;
                    if (to.magnitude > 600) continue;
                    float a = Vector3.Angle(v.transform.forward, to);
                    if (a < bestA) { bestA = a; aimPoint = o.transform.position + o.vel * (to.magnitude / Mathf.Max(100, v.prim.def.speed)); }
                }
                inp.aimPoint = aimPoint;
                v.seatIn[0] = inp;
            }
            else
            {
                camYaw += Ctl.Look.x;
                camPitch = Mathf.Clamp(camPitch - Ctl.Look.y, -40, 60);
                Vector3 fwd = Quaternion.Euler(0, camYaw, 0) * Vector3.forward, right = Quaternion.Euler(0, camYaw, 0) * Vector3.right;
                aimPoint = CameraAim(v.transform);
                inp.aimPoint = aimPoint;
                inp.fire = Ctl.Fire; inp.alt = Ctl.Alt;
                if (me.seat == 0)
                {
                    inp.move = Vector3.ClampMagnitude(fwd * Ctl.Move.y + right * Ctl.Move.x, 1);
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
                if (selPost >= 0 && selPost < b.posts.Count) focus = b.posts[selPost].Pos;
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
