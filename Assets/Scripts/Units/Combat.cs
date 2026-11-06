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
            Shot s = I.pool.Count > 0 ? I.pool.Pop() : new Shot();
            s.pos = s.prev = from; s.vel = dir.normalized * w.speed + inheritVel; s.life = w.range / Mathf.Max(1, w.speed) * 1.1f + 0.2f;
            s.w = w; s.team = team; s.owner = owner; s.ignore = ignore; s.col = col; s.dmgMul = dmgMul; s.homing = homing;
            s.grenade = w.kind == WKind.Grenade; s.trailTimer = 0;
            s.player = owner && owner.part != null && owner.part.isPlayer;
            if (s.player && !s.grenade) PlayerShots++;
            if (s.grenade) s.life = 4f;
            float boost = w.kind == WKind.Rocket || w.kind == WKind.Missile || w.kind == WKind.Torpedo ? 1.6f : 1.3f;
            s.mat = Mats.Additive(new Color(col.r * boost, col.g * boost, col.b * boost, 1));
            I.live.Add(s);
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
                if (!done)
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
                    if (dist > 0.0001f && Cast(s, s.pos, step / dist, dist, out var hit))
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
                else if (s.grenade || s.w.splash > 0) Impact(s, s.pos, Vector3.up, null);

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

        void Impact(Shot s, Vector3 p, Vector3 normal, Collider c)
        {
            var w = s.w;
            IDamageable target = c ? c.GetComponentInParent<IDamageable>() : null;

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
                else if (Battle.I != null)
                {
                    // how far did the miss pass from the nearest enemy?
                    float best = 6;
                    foreach (var eu in Battle.I.units)
                        if (eu && eu.Alive && eu.team != s.team && !eu.InVehicle)
                        {
                            Vector3 a = s.prev, b2 = p; Vector3 ab = b2 - a; float t = Mathf.Clamp01(Vector3.Dot(eu.AimPoint - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
                            float d = Vector3.Distance(a + ab * t, eu.AimPoint);
                            if (d < best) best = d;
                        }
                    if (best < 6) { LastMissBy = best; LastMissTime = Time.time; }
                }
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
                Fx.Explosion(p, Mathf.Clamp(r / 4.5f, 0.6f, 2.2f), w.kind == WKind.Torpedo);
            }
            else
            {
                Fx.Sparks(p, normal, s.col, w.kind == WKind.Sniper ? 12 : 5);
                if (target == null && c != null) Sfx.Play("hit", p, 0.18f, Random.Range(0.8f, 1.2f), 40);
            }
        }

        public static void Deal(IDamageable target, float amount, WeaponDef w, Unit attacker, int team, Vector3 p, Vector3 dir, DmgType type)
        {
            if (target == null || !target.Alive || target.Team == team) return;
            float mul = target.IsVehicle ? (w != null ? w.vsVehicle : 1) : (w != null ? w.vsInfantry : 1);
            if (attacker && attacker.DamageBoost > 1) mul *= attacker.DamageBoost;
            var b = Battle.I;
            if (b != null) mul *= b.DamageMul(team);
            target.TakeDamage(new DamageInfo { amount = amount * mul, type = type, attacker = attacker, team = team, point = p, dir = dir, weapon = w });
            if (attacker && attacker.part != null && attacker.part.isPlayer && type != DmgType.Environment) HUD.HitMarker();
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
