using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    public static class Layers
    {
        public const int Default = 0, Ground = 8, Unit = 9, Vehicle = 10, Hull = 11, Ghost = 2;
        public const int ShotMask = (1 << Default) | (1 << Ground) | (1 << Unit) | (1 << Vehicle) | (1 << Hull);
        public const int SightMask = (1 << Default) | (1 << Ground) | (1 << Hull);
        public const int SolidMask = (1 << Default) | (1 << Ground) | (1 << Hull);
        public const int MoveMask = (1 << Default) | (1 << Ground) | (1 << Hull) | (1 << Vehicle);
        public const int DamageMask = (1 << Unit) | (1 << Vehicle) | (1 << Hull) | (1 << Default);
    }

    public struct DamageInfo
    {
        public float amount;
        public DmgType type;
        public Unit attacker;
        public int team;
        public Vector3 point, dir;
        public WeaponDef weapon;
        public float vsVehicle, vsInfantry;
    }

    public interface IDamageable
    {
        int Team { get; }
        bool Alive { get; }
        bool IsVehicle { get; }      // vehicles, ships and capital ship systems take weapon.vsVehicle
        Vector3 AimPoint { get; }
        void TakeDamage(DamageInfo d);
    }

    /// <summary>One player or bot for the whole battle; a new Unit is made each time they spawn.</summary>
    public class Participant
    {
        public string name;
        public int team;
        public bool isPlayer;
        public bool remote;           // the other human in a Wi-Fi game (host: the guest; guest: the host)
        public bool Human => isPlayer || remote;
        public Unit unit;
        public Vehicle shipOnly;      // space pilots who never left their seat
        public float respawn;
        public float diedAt = -999;        // spawn timers count from here
        public float mateUsedUntil;        // someone spawned on this participant: not again until then
        public int points, kills, deaths, captures;
        public readonly int[] classKills = new int[5];
        public readonly bool[] awards = new bool[5];
        public int lifeKills;
        public int pointsAtHero;
        public bool heroReady;
        public int classChoice;
        public int spawnPost = -1;
        public int shipChoice = -1;   // -1 = on foot, else index into faction ships
        public float heroCost = 36;
        public bool everHero;
        public bool boss;            // battle royale: the boss (not a player; never counted among the living)
        public int placement;        // battle royale: where this player finished (1 = last one standing)
        public int royaleClass;      // battle royale: class picked (when classes are on)
        public int faction = -1;     // battle royale: this player's race
        public Vector3 dropAt;       // battle royale: where they chose to land
        public bool dropped;         // battle royale: has left the dropship
        public float aimSkill;       // bots: 0..1
        public float repairAcc;
        public readonly List<string> awardsEarned = new List<string>();
        public bool Alive => unit != null && unit.Alive;
        public float HeroProgress => Mathf.Clamp01((points - pointsAtHero) / heroCost);
    }

    /// <summary>Bolts, rockets, missiles, torpedoes and grenades, drawn with Graphics.DrawMesh.</summary>
    public class Shots : MonoBehaviour
    {
        public static Shots I;
        class Shot
        {
            public Vector3 pos, vel, prev;
            public float life;
            public WeaponDef w;
            public int team;
            public Unit owner;
            public Transform ignore;
            public Transform homing;
            public Color col;
            public float dmgMul = 1;
            public bool grenade;
            public float trailTimer;
            public Material mat;
            public bool player;
            public bool visual;          // a hitscan tracer: drawn only, the hit has already happened
            public bool fake;            // a Wi-Fi guest's copy of the host's shot: effects only, no damage
        }
        readonly List<Shot> live = new List<Shot>(512);
        readonly Stack<Shot> pool = new Stack<Shot>();
        Mesh bolt, ball;
        static readonly RaycastHit[] hits = new RaycastHit[8];
        static readonly Collider[] overlap = new Collider[64];
        static readonly HashSet<IDamageable> splashSet = new HashSet<IDamageable>();

        void Awake()
        {
            I = this;
            var k = new MeshKit();
            // two crossed quads along z, uv.x across the width so the radial glow tex makes an oval
            k.v.AddRange(new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f),
                                 new Vector3(0, -0.5f, -0.5f), new Vector3(0, 0.5f, -0.5f), new Vector3(0, 0.5f, 0.5f), new Vector3(0, -0.5f, 0.5f) });
            for (int i = 0; i < 8; i++) { k.n.Add(Vector3.up); k.c.Add(new Color32(255, 255, 255, 255)); }
            k.uv.AddRange(new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            k.t.AddRange(new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 });
            bolt = k.Build("bolt");
            bolt.bounds = new Bounds(Vector3.zero, Vector3.one * 2);
            k.Clear();
            k.Sphere(Vector3.zero, Vector3.one, Quaternion.identity, Color.white, 6, 4);
            ball = k.Build("ball");
        }

        public void Clear() { foreach (var s in live) pool.Push(s); live.Clear(); }

        // the player's aim statistics (Options > Show aim and hit statistics)
        public static int PlayerShots, PlayerHits;
        public static float LastMissBy = -1, LastMissTime = -99;
        public static void ResetStats() { PlayerShots = PlayerHits = 0; LastMissBy = -1; }

        public static void Fire(WeaponDef w, Vector3 from, Vector3 dir, int team, Unit owner, Transform ignore, Color col, float dmgMul = 1, Transform homing = null, Vector3 inheritVel = default)
        {
            if (!I) return;
            Net.NetHost.OnShot(w, from, dir, team, ignore, col, homing, inheritVel);
            I.Launch(w, from, dir, team, owner, ignore, col, dmgMul, homing, inheritVel, false);
        }

        /// <summary>A Wi-Fi guest draws the host's shot: it flies and sparks / explodes where it lands, but hurts nothing.</summary>
        public static void FireVisual(WeaponDef w, Vector3 from, Vector3 dir, int team, Transform ignore, Color col, Transform homing, Vector3 inheritVel)
        {
            if (I) I.Launch(w, from, dir, team, null, ignore, col, 1, homing, inheritVel, true);
        }

        /// <summary>Missiles and rockets homing on t right now: how many, and the nearest one's distance and position.</summary>
        public static int Incoming(Transform t, out float nearest, out Vector3 at)
        {
            nearest = float.MaxValue; at = default;
            if (!I || !t) return 0;
            int n = 0; Vector3 p = t.position;
            foreach (var s in I.live)
                if (s.homing == t && !s.visual) { n++; float d = (s.pos - p).magnitude; if (d < nearest) { nearest = d; at = s.pos; } }
            return n;
        }

        /// <summary>An evasive roll: missiles homing on t within range lose it and fly straight on. Returns how many.</summary>
        public static int LoseTarget(Transform t, float range)
        {
            if (!I || !t) return 0;
            int n = 0; Vector3 p = t.position;
            foreach (var s in I.live)
                if (s.homing == t && (s.pos - p).sqrMagnitude < range * range) { s.homing = null; n++; }
            return n;
        }

        void Launch(WeaponDef w, Vector3 from, Vector3 dir, int team, Unit owner, Transform ignore, Color col, float dmgMul, Transform homing, Vector3 inheritVel, bool fake)
        {
            if (w.Hitscan && homing == null) { FireHitscan(w, from, dir.normalized, team, owner, ignore, col, dmgMul, fake); return; }
            Shot s = pool.Count > 0 ? pool.Pop() : new Shot();
            s.visual = false; s.fake = fake;
            s.pos = s.prev = from; s.vel = dir.normalized * w.speed + inheritVel; s.life = w.range / Mathf.Max(1, w.speed) * 1.1f + 0.2f;
            s.w = w; s.team = team; s.owner = owner; s.ignore = ignore; s.col = col; s.dmgMul = dmgMul; s.homing = homing;
            s.grenade = w.kind == WKind.Grenade; s.trailTimer = 0;
            s.player = owner && owner.part != null && owner.part.isPlayer;
            if (s.player && !s.grenade) PlayerShots++;
            if (s.grenade) s.life = 4f;
            float boost = w.kind == WKind.Rocket || w.kind == WKind.Missile || w.kind == WKind.Torpedo ? 1.6f : 1.3f;
            s.mat = Mats.Additive(new Color(col.r * boost, col.g * boost, col.b * boost, 1));
            live.Add(s);
        }

        /// <summary>Instant hit along the line, then a tracer that races to the hit point (or full range on a miss).</summary>
        void FireHitscan(WeaponDef w, Vector3 from, Vector3 dir, int team, Unit owner, Transform ignore, Color col, float dmgMul, bool fake)
        {
            Shot s = pool.Count > 0 ? pool.Pop() : new Shot();
            s.w = w; s.team = team; s.owner = owner; s.ignore = ignore; s.col = col; s.dmgMul = dmgMul; s.homing = null;
            s.grenade = false; s.trailTimer = 0; s.visual = false; s.fake = fake;
            s.player = owner && owner.part != null && owner.part.isPlayer;
            if (s.player) PlayerShots++;
            s.pos = s.prev = from;
            s.vel = dir * Mathf.Max(w.speed, 1);
            float dist = w.range;
            float boost = 1.3f;
            s.mat = Mats.Additive(new Color(col.r * boost, col.g * boost, col.b * boost, 1));
            if (Cast(s, from, dir, w.range, out var hit))
            {
                dist = hit.distance;
                s.prev = from;
                Impact(s, hit.point, hit.normal, hit.collider);
            }
            else if (s.player) NoteMiss(from, from + dir * w.range, team);
            // the tracer: fast enough to read as instant, slow enough to see where it went
            float tracer = Mathf.Max(w.speed * 3f, 420f);
            s.vel = dir * tracer;
            s.pos = from + dir * Mathf.Min(w.boltLen, dist);
            s.life = Mathf.Max(0.02f, (dist - Mathf.Min(w.boltLen, dist)) / tracer);
            s.visual = true;
            live.Add(s);
        }

        static void NoteMiss(Vector3 a, Vector3 p, int team)
        {
            if (Battle.I == null) return;
            float best = 6;
            foreach (var eu in Battle.I.units)
                if (eu && eu.Alive && eu.team != team && !eu.InVehicle)
                {
                    Vector3 ab = p - a; float t = Mathf.Clamp01(Vector3.Dot(eu.AimPoint - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
                    float d = Vector3.Distance(a + ab * t, eu.AimPoint);
                    if (d < best) best = d;
                }
            if (best < 6) { LastMissBy = best; LastMissTime = Time.time; }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var s = live[i];
                s.life -= dt;
                bool done = s.life <= 0;
                if (s.visual)
                {
                    if (!done) { s.prev = s.pos; s.pos += s.vel * dt; }
                }
                else if (!done)
                {
                    // homing / guided
                    if (s.homing && (s.w.lockOn || s.w.guided))
                    {
                        Vector3 to = (s.homing.position - s.pos);
                        float turn = (s.w.kind == WKind.Rocket ? 70f : 110f) * dt;
                        s.vel = Vector3.RotateTowards(s.vel, to.normalized * s.vel.magnitude, turn * Mathf.Deg2Rad, 0);
                    }
                    else if (s.w.guided && s.owner && s.owner.Alive)
                    {
                        Vector3 to = s.owner.input.aimPoint - s.pos;
                        if (to.sqrMagnitude > 4) s.vel = Vector3.RotateTowards(s.vel, to.normalized * s.vel.magnitude, 80f * Mathf.Deg2Rad * dt, 0);
                    }
                    if (s.grenade || s.w.gravity > 0) s.vel += Physics.gravity * (s.grenade ? 1f : s.w.gravity) * dt;
                    Vector3 step = s.vel * dt;
                    float dist = step.magnitude;
                    // proximity fuse: bursts beside an enemy soldier instead of flying past at chest height
                    if (s.w.proximity > 0 && Battle.I != null && (!s.owner || (s.pos - s.owner.transform.position).sqrMagnitude > 16))
                    {
                        float pr2 = s.w.proximity * s.w.proximity;
                        foreach (var eu in Battle.I.units)
                        {
                            if (!eu || !eu.Alive || eu.team == s.team || eu.InVehicle) continue;
                            Vector3 ab = step; float tt = Mathf.Clamp01(Vector3.Dot(eu.AimPoint - s.pos, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
                            Vector3 cp = s.pos + ab * tt;
                            if ((cp - eu.AimPoint).sqrMagnitude < pr2) { Impact(s, cp, -ab.normalized, null); done = true; break; }
                        }
                    }
                    if (done) { }
                    else if (dist > 0.0001f && Cast(s, s.pos, step / dist, dist, out var hit))
                    {
                        if (s.grenade && hit.collider.GetComponentInParent<IDamageable>() == null && s.life > 3.4f)
                        {
                            // bounce once early in flight
                            s.vel = Vector3.Reflect(s.vel, hit.normal) * 0.45f;
                            s.pos = hit.point + hit.normal * 0.15f;
                            Sfx.Play("thud", hit.point, 0.4f, 1.6f);
                        }
                        else
                        {
                            Impact(s, hit.point, hit.normal, hit.collider);
                            done = true;
                        }
                    }
                    else { s.prev = s.pos; s.pos += step; }

                    // trails for rockets / torpedoes / grenades
                    if (!done && (s.w.kind == WKind.Rocket || s.w.kind == WKind.Missile || s.w.kind == WKind.Torpedo || s.grenade))
                    {
                        s.trailTimer -= dt;
                        if (s.trailTimer <= 0)
                        {
                            s.trailTimer = 0.03f;
                            Fx.Emit(s.pos, Random.insideUnitSphere * 0.3f, new Color(1f, 0.7f, 0.35f, 1), s.w.boltWidth * 1.4f, 0.15f);
                            if (!s.grenade) Fx.Emit(s.pos, Random.insideUnitSphere * 0.4f, new Color(0.55f, 0.55f, 0.55f, 0.35f), 0.7f, 1.0f, true);
                        }
                    }
                }
                else if (!s.visual && (s.grenade || s.w.splash > 0)) Impact(s, s.pos, Vector3.up, null);

                if (done) { live.RemoveAt(i); pool.Push(s); continue; }

                // draw
                Vector3 d = s.vel.sqrMagnitude > 0.01f ? s.vel.normalized : Vector3.forward;
                if (s.grenade)
                {
                    Graphics.DrawMesh(ball, Matrix4x4.TRS(s.pos, Quaternion.identity, Vector3.one * 0.25f), s.mat, 0);
                }
                else
                {
                    float len = s.w.boltLen, wid = s.w.boltWidth;
                    var m = Matrix4x4.TRS(s.pos - d * len * 0.5f, Quaternion.LookRotation(d), new Vector3(wid * 2.2f, wid * 2.2f, len));
                    Graphics.DrawMesh(bolt, m, s.mat, 0);
                }
            }
        }

        bool Cast(Shot s, Vector3 from, Vector3 dir, float dist, out RaycastHit best)
        {
            // the player's bolts are a little fatter (0.2 m) so a well-aimed shot is not lost to a thin capsule edge
            int n = s.player ? Physics.SphereCastNonAlloc(from, 0.2f, dir, hits, dist, Layers.ShotMask, QueryTriggerInteraction.Ignore)
                             : Physics.RaycastNonAlloc(from, dir, hits, dist, Layers.ShotMask, QueryTriggerInteraction.Ignore);
            best = default;
            float bd = float.MaxValue; bool found = false;
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                if (s.ignore && h.collider.transform.IsChildOf(s.ignore)) continue;
                if (h.distance <= 0 && h.point == Vector3.zero) { h.point = from; h.normal = -dir; }
                if (h.distance < bd) { bd = h.distance; best = h; found = true; }
            }
            return found;
        }

        public static System.Action<Vector3, Collider, WeaponDef, Unit> DebugImpact;   // test harness only

        void Impact(Shot s, Vector3 p, Vector3 normal, Collider c)
        {
            var w = s.w;
            DebugImpact?.Invoke(p, c, w, s.owner);
            IDamageable target = c ? c.GetComponentInParent<IDamageable>() : null;
            if (s.fake)
            {
                if (w.splash > 0 || s.grenade) Fx.Explosion(p, Mathf.Clamp((s.grenade ? 6f : w.splash) / 4.5f, 0.6f, 2.2f), w.kind == WKind.Torpedo);
                else
                {
                    Fx.Sparks(p, normal, s.col, w.kind == WKind.Sniper ? 12 : 5);
                    if (target == null && c != null) Sfx.Play("hit", p, 0.18f, Random.Range(0.8f, 1.2f), 40);
                }
                return;
            }

            // a Bladesworn hero blocking deflects bolts back
            if (target is Unit u && u.Blocking && w.kind != WKind.Rocket && w.kind != WKind.Missile && w.kind != WKind.Torpedo && !s.grenade && u.team != s.team)
            {
                Vector3 inDir = s.vel.normalized;
                if (Vector3.Dot(inDir, u.transform.forward) < -0.2f)
                {
                    Vector3 back = s.owner ? (s.owner.AimPoint - p).normalized : -inDir;
                    Fire(w, p + back * 0.6f, back + Random.insideUnitSphere * 0.08f, u.team, u, u.transform, u.faction.bolt, s.dmgMul);
                    Fx.Sparks(p, -inDir, u.hero != null ? u.hero.glow : Color.white, 8);
                    Sfx.Play("blade", p, 0.5f, 1.6f);
                    return;
                }
            }

            if (s.player && !s.grenade)
            {
                if (target != null && target.Alive && target.Team != s.team) PlayerHits++;
                else NoteMiss(s.prev, p, s.team);      // how far did the miss pass from the nearest enemy?
            }
            if (target != null && target.Alive && target.Team != s.team)
            {
                Deal(target, w.damage * s.dmgMul, w, s.owner, s.team, p, s.vel.normalized, w.splash > 0 ? DmgType.Explosive : DmgType.Bolt);
            }
            if (w.splash > 0 || s.grenade)
            {
                float r = s.grenade ? 6f : w.splash;
                float sd = (s.grenade ? 260f : w.splashDmg) * s.dmgMul;
                Splash(p, r, sd, w, s.owner, s.team, target);
                Fx.netMute = true;     // the guest draws this explosion from its own copy of the shot
                Fx.Explosion(p, Mathf.Clamp(r / 4.5f, 0.6f, 2.2f), w.kind == WKind.Torpedo);
                Fx.netMute = false;
            }
            else
            {
                Fx.Sparks(p, normal, s.col, w.kind == WKind.Sniper ? 12 : 5);
                if (target == null && c != null) Sfx.Play("hit", p, 0.18f, Random.Range(0.8f, 1.2f), 40);
            }
        }

        public static void Deal(IDamageable target, float amount, WeaponDef w, Unit attacker, int team, Vector3 p, Vector3 dir, DmgType type)
        {
            if (target == null || !target.Alive || target.Team == team || Net.Session.IsClient) return;
            float mul = target.IsVehicle ? (w != null ? w.vsVehicle : 1) : (w != null ? w.vsInfantry : 1);
            if (attacker && attacker.DamageBoost > 1) mul *= attacker.DamageBoost;
            var b = Battle.I;
            if (b != null) mul *= b.DamageMul(team);
            var tu = target as Unit; var tv = target as Vehicle;
            float hp0 = tu ? tu.hp : tv ? tv.hp : 0, ar0 = tu ? tu.armor : 0;
            target.TakeDamage(new DamageInfo { amount = amount * mul, type = type, attacker = attacker, team = team, point = p, dir = dir, weapon = w });
            if (attacker && attacker.part != null && type != DmgType.Environment)
            {
                float hpDmg = Mathf.Max(0, hp0 - Mathf.Max(0, tu ? tu.hp : tv ? tv.hp : 0)), arDmg = tu ? Mathf.Max(0, ar0 - tu.armor) : 0;
                bool kill = !target.Alive;
                if (attacker.part.isPlayer) { HUD.HitMarker(kill, target.IsVehicle); HUD.Damage(target, p, hpDmg, arDmg, kill); }
                else if (attacker.part.remote) { Net.NetHost.Hit(kill, target.IsVehicle); Net.NetHost.Damage(target, p, hpDmg, arDmg, kill); }
            }
        }

        public static void Splash(Vector3 p, float r, float dmg, WeaponDef w, Unit attacker, int team, IDamageable skip)
        {
            splashSet.Clear();
            int n = Physics.OverlapSphereNonAlloc(p, r, overlap, Layers.DamageMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var d = overlap[i].GetComponentInParent<IDamageable>();
                if (d == null || d == skip || !d.Alive || d.Team == team || !splashSet.Add(d)) continue;
                Vector3 cp = overlap[i].ClosestPoint(p);
                float f = 1 - Mathf.Clamp01((cp - p).magnitude / r);
                if (f <= 0) continue;
                // blocked by solid ground or walls?
                Vector3 aim = d.AimPoint;
                if (Physics.Linecast(p + (aim - p).normalized * 0.3f, aim, out var lh, Layers.SolidMask, QueryTriggerInteraction.Ignore) &&
                    lh.collider.GetComponentInParent<IDamageable>() != d && (lh.point - aim).sqrMagnitude > 1f) f *= 0.35f;
                Deal(d, dmg * (0.35f + 0.65f * f), w, attacker, team, cp, (aim - p).normalized, DmgType.Explosive);
                if (d is Unit u) u.Push((aim - p).normalized * 9 * f + Vector3.up * 4 * f);
            }
        }
    }
}
