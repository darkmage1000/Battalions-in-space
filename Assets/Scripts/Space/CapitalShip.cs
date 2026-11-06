using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    public enum SysKind { Shield, Engine, Turret, Hangar, LifeSupport, Reactor }

    /// <summary>A destructible capital ship system (exterior or interior).</summary>
    public class Subsystem : MonoBehaviour, IDamageable, IRepairable
    {
        public CapitalShip ship;
        public SysKind kind;
        public string sysName;
        public float hp, maxHp;
        public bool critical, interior, destroyed;
        public Renderer glowR;
        public Transform aim;           // turrets: rotating head
        public Transform barrel;
        float cool, think, smokeT;
        Vehicle target;

        public int Team => ship.team;
        public bool Alive => !destroyed;
        public bool IsVehicle => true;
        public bool CanRepair => !destroyed && hp < maxHp;
        public Vector3 AimPoint => (aim ? aim.position : transform.position);

        public void TakeDamage(DamageInfo d)
        {
            if (destroyed || ship.battle.over) return;
            float a = d.amount;
            if (interior)
            {
                // only boarders on foot can hurt interior systems
                if (d.attacker != null && d.attacker.InVehicle) return;
            }
            else if (kind != SysKind.Shield && ship.ShieldUp) a *= 0.3f;
            hp -= a;
            if (d.attacker) ship.battle.AlertShip(ship, this);
            if (hp <= 0) { hp = 0; destroyed = true; ship.OnSystemDestroyed(this, d.attacker); }
        }

        public float Repair(float amount)
        {
            if (!CanRepair) return 0;
            float b = hp; hp = Mathf.Min(maxHp, hp + amount); return hp - b;
        }

        public void Tick(float dt)
        {
            if (destroyed || Net.Session.IsClient)
            {
                if (!destroyed) return;
                smokeT -= dt;
                if (smokeT <= 0 && Game.Cam && (Game.Cam.transform.position - transform.position).sqrMagnitude < 900 * 900)
                {
                    smokeT = interior ? 0.3f : 0.15f;
                    Fx.Smoke(transform.position + Random.insideUnitSphere * 3, interior ? 2 : 6, interior ? 2 : 4);
                    if (Random.value < 0.4f) Fx.Emit(transform.position + Random.insideUnitSphere * 3, Random.insideUnitSphere * 4, new Color(1, 0.5f, 0.2f, 1), interior ? 1 : 3, 0.4f);
                }
                return;
            }
            if (kind != SysKind.Turret || ship.battle.over) return;
            think -= dt; cool -= dt;
            if (think <= 0)
            {
                think = 0.5f;
                target = null; float best = 760 * 760;
                foreach (var v in ship.battle.vehicles)
                {
                    if (!v || !v.Alive || v.Team == ship.team || v.IsEmpty || !v.IsShip || v.LaunchShield) continue;
                    if (ship.InHangarApproach(v.transform.position)) continue;   // the guns can't bear on the hangar mouth
                    float d2 = (v.transform.position - aim.position).sqrMagnitude;
                    if (d2 < best && Vector3.Dot(v.transform.position - aim.position, ship.transform.TransformDirection(TurretFacing)) > -0.2f) { best = d2; target = v; }
                }
            }
            if (target && target.Alive)
            {
                var w = ship.turretWeapon;
                Vector3 to = target.transform.position - aim.position;
                float t = to.magnitude / w.speed;
                Vector3 lead = target.transform.position + target.vel * t * 0.9f;
                Vector3 dir = (lead - aim.position).normalized;
                aim.rotation = Quaternion.RotateTowards(aim.rotation, Quaternion.LookRotation(dir), 60 * dt);
                if (cool <= 0 && Vector3.Angle(aim.forward, dir) < 6)
                {
                    cool = 1f / w.rate * Random.Range(0.8f, 1.4f);
                    Vector3 from = barrel ? barrel.position : aim.position + aim.forward * 4;
                    Vector3 d = Quaternion.Euler(Random.Range(-w.spread, w.spread), Random.Range(-w.spread, w.spread), 0) * aim.forward;
                    Shots.Fire(w, from, d, ship.team, null, ship.transform, ship.faction.bolt);
                    Sfx.Play("heavy", from, 0.6f, 0.7f, 500);
                }
            }
        }

        Vector3 TurretFacing => aim ? ship.transform.InverseTransformDirection(transform.up) : Vector3.forward;
    }

    public class Hangar
    {
        public CapitalShip ship;
        public Vector3 min = new Vector3(-36, -1, -6), max = new Vector3(36, 20, 34);
        public float FloorY => ship.transform.position.y;
        public bool Contains(Vector3 world)
        {
            var l = ship.transform.InverseTransformPoint(world);
            return l.x > min.x && l.x < max.x && l.y > min.y && l.y < max.y && l.z > min.z && l.z < max.z;
        }
    }

    /// <summary>
    /// A capital ship built from boxes in code: exterior hull styled per faction, a hangar bay that opens
    /// toward the enemy, interior corridors leading to Life Support and the Reactor, and destructible
    /// systems. The ship is stationary; local +z faces the enemy fleet.
    /// </summary>
    public class CapitalShip : MonoBehaviour, IDamageable
    {
        public Battle battle;
        public int team;
        public FactionDef faction;
        public float hull, maxHull = 30000;
        public bool destroyed, crippled;
        public readonly List<Subsystem> systems = new List<Subsystem>();
        public Hangar hangar;
        public WeaponDef turretWeapon;
        public readonly List<Vector3> nodes = new List<Vector3>();          // interior nav (local)
        public readonly List<int[]> links = new List<int[]>();
        public const int NodeHangar = 0, NodeDoor = 1, NodeJunction = 2, NodeLS = 4, NodeReactor = 6, NodeHangarFront = 9;
        Transform hullRoot;
        MeshKit hullKit, glowKit;
        Renderer shieldBubble, hangarField;
        float broadside;

        public int Team => team;
        public bool Alive => !destroyed;
        public bool IsVehicle => true;
        public Vector3 AimPoint => transform.position + transform.up * 10;
        public bool ShieldUp => systems.Exists(s => s.kind == SysKind.Shield && !s.destroyed);
        /// <summary>The lane straight in front of the hangar (and the hangar itself): a blind spot for the point-defence guns, so boarding is possible.</summary>
        public bool InHangarApproach(Vector3 world)
        {
            var l = transform.InverseTransformPoint(world);
            return l.z > -6 && l.z < 260 && Mathf.Abs(l.x) < 34 + Mathf.Max(0, l.z - 30) * 0.2f && l.y > -12 && l.y < 30 + Mathf.Max(0, l.z - 30) * 0.12f;
        }

        public bool HangarOpen => systems.Exists(s => s.kind == SysKind.Hangar && !s.destroyed);
        public Subsystem Sys(SysKind k) => systems.Find(s => s.kind == k);
        public float HullFrac => hull / maxHull;

        public void TakeDamage(DamageInfo d)
        {
            if (destroyed || battle.over) return;
            // the hull mostly falls through its systems; direct hits only scratch it
            float a = d.amount * (ShieldUp ? 0.1f : 1f);
            a *= d.weapon != null && d.weapon.kind == WKind.Torpedo ? 0.22f : 0.05f;
            hull -= a;
            if (hull <= 0) { hull = 0; destroyed = true; battle.OnCapitalDestroyed(this, d.attacker); }
        }

        // ── build ────────────────────────────────────────────────────────
        public void Build(Battle b, int t, FactionDef f, Vector3 pos, Quaternion rot)
        {
            battle = b; team = t; faction = f;
            transform.SetPositionAndRotation(pos, rot);
            hull = maxHull;
            turretWeapon = W.CapTurret("Point-defence Battery");
            turretWeapon.damage *= f.dmgMul;
            hangar = new Hangar { ship = this };
            hullRoot = new GameObject("hull").transform;
            hullRoot.SetParent(transform, false);
            hullKit = new MeshKit(); glowKit = new MeshKit();

            Color hc = Hull(f), hd = HullDark(f), trim = f.second;
            Color floor = new Color(0.32f, 0.33f, 0.36f), wall = new Color(0.55f, 0.56f, 0.6f), deck = new Color(0.25f, 0.26f, 0.29f);
            if (f.body == Body.Reptile) { wall = new Color(0.45f, 0.3f, 0.26f); floor = new Color(0.25f, 0.18f, 0.16f); }
            if (f.body == Body.Machine) { wall = new Color(0.42f, 0.42f, 0.4f); floor = new Color(0.22f, 0.22f, 0.22f); }
            if (f.body == Body.Slender) { wall = new Color(0.5f, 0.62f, 0.64f); floor = new Color(0.24f, 0.3f, 0.34f); }

            // ── mid section around the hangar and interior ──
            Solid(new Vector3(0, -8, -7.5f), new Vector3(104, 16, 75), hd);                 // floor slab
            Solid(new Vector3(0, 24, -7.5f), new Vector3(104, 8, 75), hc);                  // roof slab
            Solid(new Vector3(-44, 10, 12), new Vector3(16, 20, 36), hc);                   // hangar side blocks
            Solid(new Vector3(44, 10, 12), new Vector3(16, 20, 36), hc);
            Solid(new Vector3(0, 10, -46), new Vector3(104, 20, 2), hc);                    // back skin
            Solid(new Vector3(-51, 10, -26), new Vector3(2, 20, 40), hc);                   // side skins behind the hangar
            Solid(new Vector3(51, 10, -26), new Vector3(2, 20, 40), hc);
            // hangar interior surfaces
            Panel(new Vector3(0, 0.02f, 14), new Vector3(72, 0.04f, 40), deck);
            Solid(new Vector3(-19.25f, 10, -6.5f), new Vector3(33.5f, 20, 1), wall);           // back wall with door
            Solid(new Vector3(19.25f, 10, -6.5f), new Vector3(33.5f, 20, 1), wall);
            Solid(new Vector3(0, 12, -6.5f), new Vector3(5, 16, 1), wall);
            Solid(new Vector3(0, 19.5f, 12), new Vector3(72, 1, 36), new Color(0.3f, 0.3f, 0.33f)); // hangar ceiling
            for (int i = -2; i <= 2; i++) Glow(new Vector3(i * 14, 18.9f, 12), new Vector3(1.2f, 0.2f, 30), new Color(0.9f, 0.95f, 1f));
            for (int i = 0; i < 6; i++) { Glow(new Vector3(-35.4f, 3, i * 6 - 3), new Vector3(0.2f, 0.4f, 2.5f), trim); Glow(new Vector3(35.4f, 3, i * 6 - 3), new Vector3(0.2f, 0.4f, 2.5f), trim); }
            Glow(new Vector3(0, 0.05f, 29.5f), new Vector3(72, 0.1f, 0.6f), new Color(1f, 0.8f, 0.3f));
            Glow(new Vector3(0, 4.6f, -6.0f), new Vector3(6, 0.3f, 0.2f), new Color(1f, 0.8f, 0.3f));
            // corridor
            Solid(new Vector3(-2.75f, 2.25f, -15.5f), new Vector3(0.5f, 4.5f, 18), wall);
            Solid(new Vector3(2.75f, 2.25f, -15.5f), new Vector3(0.5f, 4.5f, 18), wall);
            Solid(new Vector3(0, 4.75f, -15.5f), new Vector3(6, 0.5f, 18), deck);
            for (int i = 0; i < 4; i++) Glow(new Vector3(0, 4.45f, -9 - i * 4.5f), new Vector3(1.2f, 0.1f, 1.2f), new Color(0.9f, 0.95f, 1f));
            // cross corridor
            Solid(new Vector3(-18.5f, 2.25f, -24.25f), new Vector3(31, 4.5f, 0.5f), wall);
            Solid(new Vector3(18.5f, 2.25f, -24.25f), new Vector3(31, 4.5f, 0.5f), wall);
            Solid(new Vector3(0, 2.25f, -30.25f), new Vector3(68, 4.5f, 0.5f), wall);
            Solid(new Vector3(0, 4.75f, -27.25f), new Vector3(68, 0.5f, 6.5f), deck);
            for (int i = -3; i <= 3; i++) Glow(new Vector3(i * 9, 4.45f, -27.25f), new Vector3(1.2f, 0.1f, 1.2f), new Color(0.9f, 0.95f, 1f));
            // life support room (west) and reactor room (east)
            for (int s = -1; s <= 1; s += 2)
            {
                float cx = 42 * s;
                Solid(new Vector3(50.25f * s, 4, -28), new Vector3(0.5f, 8, 24), wall);
                Solid(new Vector3(cx, 4, -40.25f), new Vector3(16, 8, 0.5f), wall);
                Solid(new Vector3(cx, 4, -15.75f), new Vector3(16, 8, 0.5f), wall);
                Solid(new Vector3(33.75f * s, 4, -35.25f), new Vector3(0.5f, 8, 10), wall);      // west/east wall with door to the cross corridor
                Solid(new Vector3(33.75f * s, 4, -19.75f), new Vector3(0.5f, 8, 8), wall);
                Solid(new Vector3(33.75f * s, 6.25f, -27.25f), new Vector3(0.5f, 3.5f, 6.5f), wall);
                Solid(new Vector3(cx, 8.25f, -28), new Vector3(16.5f, 0.5f, 24.5f), deck);
                Glow(new Vector3(cx, 7.9f, -28), new Vector3(8, 0.1f, 1), new Color(0.9f, 0.95f, 1f));
                Glow(new Vector3(cx, 7.9f, -22), new Vector3(8, 0.1f, 1), new Color(0.9f, 0.95f, 1f));
                Glow(new Vector3(cx, 7.9f, -34), new Vector3(8, 0.1f, 1), new Color(0.9f, 0.95f, 1f));
            }
            Panel(new Vector3(0, 0.02f, -27), new Vector3(100, 0.04f, 30), floor);

            // ── fore and aft sections, faction styled ──
            BuildExterior(f, hc, hd, trim);

            // finish meshes
            var hullGo = hullKit.Make("hull_mesh", hullRoot, false);
            hullGo.layer = Layers.Hull;
            glowKit.Make("hull_glow", hullRoot, false).layer = Layers.Hull;

            // hangar atmosphere field
            var fk = new MeshKit();
            fk.Quad(new Vector3(-36, 0, 30.2f), new Vector3(-36, 20, 30.2f), new Vector3(36, 20, 30.2f), new Vector3(36, 0, 30.2f), Color.white);
            fk.Quad(new Vector3(36, 0, 30.2f), new Vector3(36, 20, 30.2f), new Vector3(-36, 20, 30.2f), new Vector3(-36, 0, 30.2f), Color.white);
            var fg = fk.Make("hangar_field", transform, false, Mats.Additive(new Color(f.bolt.r, f.bolt.g, f.bolt.b, 0.12f), false));
            hangarField = fg.GetComponent<Renderer>();

            // ── systems ──
            AddShield(new Vector3(70, 30, -12));
            AddEngine(new Vector3(-158, 6, -22));
            AddEngine(new Vector3(-158, 6, 4));
            AddTurret(new Vector3(-100, 28.5f, 12), Vector3.up);
            AddTurret(new Vector3(100, 28.5f, 12), Vector3.up);
            AddTurret(new Vector3(-110, 4, 31.5f), Vector3.forward);
            AddTurret(new Vector3(110, 4, 31.5f), Vector3.forward);
            AddHangarControl(new Vector3(0, 17.8f, 27));
            AddLifeSupport(new Vector3(-42, 0, -28));
            AddReactor(new Vector3(42, 0, -28));

            InteriorProps();

            // interior lights
            Lamp(new Vector3(0, 16, 12), 55, new Color(0.9f, 0.93f, 1f), 1.6f);
            Lamp(new Vector3(0, 3.5f, -20), 24, new Color(0.9f, 0.93f, 1f), 1.4f);
            Lamp(new Vector3(-42, 6, -28), 20, new Color(0.5f, 0.9f, 1f), 1.6f);
            Lamp(new Vector3(42, 6, -28), 20, new Color(1f, 0.7f, 0.45f), 1.6f);

            // nav nodes (local)
            nodes.AddRange(new[] { new Vector3(0, 0, 10), new Vector3(0, 0, -4), new Vector3(0, 0, -27), new Vector3(-36, 0, -27), new Vector3(-41, 0, -21),
                                   new Vector3(36, 0, -27), new Vector3(41, 0, -20), new Vector3(-24, 0, 12), new Vector3(24, 0, 12), new Vector3(0, 0, 25) });
            int[][] e = { new[] { 9, 0 }, new[] { 7, 0 }, new[] { 8, 0 }, new[] { 0, 1 }, new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 4 }, new[] { 2, 5 }, new[] { 5, 6 }, new[] { 7, 9 }, new[] { 8, 9 } };
            links.AddRange(e);
        }

        /// <summary>Quaternius "Ultimate Modular Sci-Fi" (CC0) props: crates and containers along the hangar walls, consoles and tanks in the system rooms.</summary>
        void InteriorProps()
        {
            var r = new System.Random(team * 101 + 7);
            float Rn(float a, float b) => a + (b - a) * (float)r.NextDouble();
            void Put(string model, Vector3 local, float yaw, float height)
            {
                var raw = ImportedModels.Load("Models/Interior/" + model, true, Quaternion.identity, 0, 1, true);
                if (!raw) return;
                float k = height / Mathf.Max(0.01f, raw.bounds.size.y);
                var m = ImportedModels.Load("Models/Interior/" + model, true, Quaternion.identity, 0, k, true);
                var go = ImportedModels.Spawn(m, Mats.Lit, hullRoot, Vector3.zero, Quaternion.identity, model);
                go.transform.localPosition = local; go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
                go.layer = Layers.Hull;
                var bc = go.AddComponent<BoxCollider>(); bc.center = m.bounds.center; bc.size = m.bounds.size * 0.95f;
            }
            // hangar: stacks along both side walls, clear of the parking slots (x -24..24) and the door
            string[] cargo = { "Props_Crate", "Props_CrateLong", "Props_ContainerFull", "Props_Crate", "Props_Capsule" };
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < 5; i++)
                {
                    string mdl = cargo[(i + (s > 0 ? 2 : 0)) % cargo.Length];
                    float h = mdl == "Props_ContainerFull" ? 3.2f : mdl == "Props_Capsule" ? 3f : mdl == "Props_CrateLong" ? 1.4f : 1.6f;
                    Put(mdl, new Vector3(s * Rn(32.2f, 33.4f), 0, 1 + i * 5.6f + Rn(-0.6f, 0.6f)), s > 0 ? -90 : 90, h);
                }
            Put("Props_Shelf_Tall", new Vector3(-30, 0, -5.2f), 0, 3.4f);
            Put("Props_Shelf_Tall", new Vector3(30, 0, -5.2f), 0, 3.4f);
            // system rooms: consoles and tanks against the outer walls, the system in the middle stays clear
            for (int s = -1; s <= 1; s += 2)
            {
                float cx = 42 * s;
                Put("Props_Computer", new Vector3(cx - 5, 0, -39), 0, 2.2f);
                Put("Props_ComputerSmall", new Vector3(cx + 5, 0, -39), 0, 1.8f);
                Put("Props_Vessel_Tall", new Vector3(49 * s, 0, -36), 0, 3.2f);
                Put("Props_Vessel", new Vector3(49 * s, 0, -20), 0, 2.2f);
                Put("Props_Shelf", new Vector3(cx + 4, 0, -16.9f), 180, 2.2f);
            }
        }

        void Lamp(Vector3 p, float range, Color c, float inten)
        {
            var lg = new GameObject("lamp"); lg.transform.SetParent(transform, false); lg.transform.localPosition = p;
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.range = range; l.color = c; l.intensity = inten; l.shadows = LightShadows.None;
        }

        static Color Hull(FactionDef f) => f.body == Body.Reptile ? new Color(0.36f, 0.2f, 0.17f) : f.body == Body.Machine ? new Color(0.36f, 0.37f, 0.38f) : f.body == Body.Slender ? new Color(0.5f, 0.66f, 0.68f) : new Color(0.72f, 0.74f, 0.78f);
        static Color HullDark(FactionDef f) { var c = Hull(f) * 0.62f; c.a = 1; return c; }

        void Solid(Vector3 c, Vector3 s, Color col)
        {
            hullKit.Box(c, s, col);
            var go = new GameObject("h");
            go.layer = Layers.Hull;
            go.transform.SetParent(hullRoot, false);
            go.transform.localPosition = c;
            go.AddComponent<BoxCollider>().size = s;
        }
        void SolidRot(Vector3 c, Vector3 s, Quaternion q, Color col)
        {
            hullKit.Box(c, s, q, col);
            var go = new GameObject("h");
            go.layer = Layers.Hull;
            go.transform.SetParent(hullRoot, false);
            go.transform.localPosition = c; go.transform.localRotation = q;
            go.AddComponent<BoxCollider>().size = s;
        }
        void Panel(Vector3 c, Vector3 s, Color col) => hullKit.Box(c, s, col);
        void Glow(Vector3 c, Vector3 s, Color col) { col.a = 0; glowKit.Box(c, s, col); }

        void BuildExterior(FactionDef f, Color hc, Color hd, Color trim)
        {
            switch (f.body)
            {
                case Body.Reptile:
                    SolidRot(new Vector3(100, 6, -7), new Vector3(96, 30, 66), Quaternion.identity, hc);
                    hullKit.Nose(new Vector3(168, 4, -7), new Vector3(60, 26, 40), Quaternion.Euler(0, 90, 0), hd, -0.2f);
                    Solid(new Vector3(160, 4, -7), new Vector3(30, 22, 34), hd);
                    SolidRot(new Vector3(-100, 6, -7), new Vector3(96, 32, 70), Quaternion.identity, hc);
                    for (int i = 0; i < 9; i++) hullKit.Cone(new Vector3(-130 + i * 32, 30, -7), 4, 22, Quaternion.Euler(0, 0, -25), f.accent, 6);
                    hullKit.Cone(new Vector3(180, 20, 14), 3, 28, Quaternion.Euler(0, 0, -70), f.accent, 6);
                    hullKit.Cone(new Vector3(180, -10, -28), 3, 28, Quaternion.Euler(0, 0, -110), f.accent, 6);
                    Solid(new Vector3(-80, 34, -20), new Vector3(30, 18, 24), hd);
                    Glow(new Vector3(-80, 38, -7.9f), new Vector3(20, 2, 0.4f), new Color(1f, 0.6f, 0.2f));
                    break;
                case Body.Machine:
                    Solid(new Vector3(100, 6, -7), new Vector3(96, 34, 74), hc);
                    Solid(new Vector3(155, 6, -7), new Vector3(20, 24, 54), hd);
                    Solid(new Vector3(-100, 6, -7), new Vector3(96, 34, 74), hc);
                    for (int i = 0; i < 4; i++) { hullKit.Cyl(new Vector3(-120 + i * 80, 6, -7), 30, 6, Quaternion.Euler(0, 0, 90), hd, 8); }
                    Solid(new Vector3(-60, 36, -7), new Vector3(40, 10, 30), hd);
                    Solid(new Vector3(-60, 46, -7), new Vector3(12, 14, 12), hc);
                    for (int i = 0; i < 10; i++) Glow(new Vector3(-140 + i * 30, 6, 30.3f), new Vector3(10, 1.2f, 0.4f), f.second);
                    break;
                case Body.Slender:
                    hullKit.Sphere(new Vector3(110, 6, -7), new Vector3(120, 30, 60), Quaternion.identity, hc, 12, 6);
                    Solid(new Vector3(100, 6, -7), new Vector3(80, 22, 48), hc);
                    hullKit.Sphere(new Vector3(-105, 6, -7), new Vector3(110, 34, 68), Quaternion.identity, hc, 12, 6);
                    Solid(new Vector3(-100, 6, -7), new Vector3(80, 26, 56), hc);
                    hullKit.Wedge(new Vector3(-60, 40, -7), new Vector3(8, 30, 70), Quaternion.Euler(0, 90, 0), trim);
                    hullKit.Wedge(new Vector3(60, -22, -7), new Vector3(6, 22, 60), Quaternion.Euler(180, 90, 0), trim);
                    Glow(new Vector3(0, 28.3f, -7), new Vector3(100, 0.4f, 2), f.bolt);
                    break;
                default:
                    Solid(new Vector3(100, 6, -7), new Vector3(96, 34, 74), hc);
                    hullKit.Nose(new Vector3(180, 6, -7), new Vector3(74, 34, 70), Quaternion.Euler(0, 90, 0), hc, -0.3f);
                    var bq = Quaternion.Euler(0, 90, 0);
                    hullKit.Taper(new Vector3(140, 6, -7), new Vector2(74, 34), new Vector2(60, 24), 1, Quaternion.identity, hc);
                    Solid(new Vector3(-100, 6, -7), new Vector3(96, 34, 74), hc);
                    Solid(new Vector3(-95, 34, -15), new Vector3(36, 22, 30), hd);
                    Solid(new Vector3(-95, 50, -15), new Vector3(24, 10, 20), hc);
                    Glow(new Vector3(-95, 51, -4.9f), new Vector3(18, 1.6f, 0.4f), new Color(0.7f, 0.85f, 1f));
                    for (int i = 0; i < 8; i++) Glow(new Vector3(-140 + i * 40, 18, 30.3f), new Vector3(16, 0.8f, 0.4f), trim);
                    _ = bq;
                    break;
            }
            // trim stripes and engine block
            Solid(new Vector3(-150, 6, -9), new Vector3(8, 28, 60), hd);
            for (int i = 0; i < 6; i++) Glow(new Vector3(60 + i * 12, -15.9f, 10), new Vector3(6, 0.3f, 3), trim);
        }

        Subsystem Make(SysKind k, string n, Vector3 local, float hp, bool crit, bool inside)
        {
            var go = new GameObject(n);
            go.layer = Layers.Hull;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = local;
            var s = go.AddComponent<Subsystem>();
            s.ship = this; s.kind = k; s.sysName = n; s.maxHp = s.hp = hp; s.critical = crit; s.interior = inside;
            systems.Add(s);
            return s;
        }

        void Visual(Subsystem s, MeshKit k, Vector3 colliderSize, Vector3 colliderCenter)
        {
            var go = k.Make("vis", s.transform, true);
            go.layer = Layers.Hull;
            var bc = s.gameObject.AddComponent<BoxCollider>();
            bc.size = colliderSize; bc.center = colliderCenter;
        }

        void AddShield(Vector3 p)
        {
            var s = Make(SysKind.Shield, "Shield Generator", p, 18000, false, false);
            var k = new MeshKit();
            k.Cyl(new Vector3(0, 2, 0), 10, 4, Quaternion.identity, HullDark(faction), 12);
            k.Sphere(new Vector3(0, 6, 0), new Vector3(16, 12, 16), Quaternion.identity, new Color(0.7f, 0.72f, 0.76f), 12, 6);
            k.Cyl(new Vector3(0, 12, 0), 1.5f, 6, Quaternion.identity, MeshKit.Glow(faction.bolt), 8);
            Visual(s, k, new Vector3(16, 14, 16), new Vector3(0, 6, 0));
            s.aim = s.transform;
            // ship-wide shimmer bubble while the generator stands
            var bk = new MeshKit();
            bk.Sphere(Vector3.zero, new Vector3(380, 110, 150), Quaternion.identity, Color.white, 24, 12);
            var bg = bk.Make("shield_bubble", transform, false, Mats.Additive(new Color(faction.bolt.r, faction.bolt.g, faction.bolt.b, 0.035f), false));
            bg.transform.localPosition = new Vector3(0, 6, -7);
            shieldBubble = bg.GetComponent<Renderer>();
        }

        void AddEngine(Vector3 p)
        {
            var s = Make(SysKind.Engine, "Engine", p, 8000, true, false);
            var k = new MeshKit();
            k.Cyl(Vector3.zero, 11, 16, Quaternion.Euler(0, 0, 90), HullDark(faction), 12);
            k.Cyl(new Vector3(-8.2f, 0, 0), 9, 0.6f, Quaternion.Euler(0, 0, 90), MeshKit.Glow(faction.body == Body.Slender ? new Color(0.5f, 1f, 0.85f) : faction.body == Body.Machine ? new Color(1f, 0.75f, 0.3f) : new Color(0.55f, 0.75f, 1f)), 12);
            Visual(s, k, new Vector3(16, 22, 22), Vector3.zero);
            s.aim = s.transform;
        }

        void AddTurret(Vector3 p, Vector3 up)
        {
            var s = Make(SysKind.Turret, "Turret", p, 2200, false, false);
            s.transform.localRotation = Quaternion.FromToRotation(Vector3.up, up);
            var k = new MeshKit();
            k.Cyl(new Vector3(0, 1, 0), 3.5f, 2, Quaternion.identity, HullDark(faction), 10);
            var baseGo = k.Make("base", s.transform, true); baseGo.layer = Layers.Hull;
            var head = new GameObject("head").transform;
            head.SetParent(s.transform, false); head.localPosition = new Vector3(0, 3.2f, 0);
            var hk = new MeshKit();
            hk.Box(Vector3.zero, new Vector3(4, 2.4f, 4), new Color(0.5f, 0.5f, 0.52f));
            hk.Cyl(new Vector3(-0.8f, 0, 3.5f), 0.35f, 5, Quaternion.Euler(90, 0, 0), new Color(0.2f, 0.2f, 0.22f), 6);
            hk.Cyl(new Vector3(0.8f, 0, 3.5f), 0.35f, 5, Quaternion.Euler(90, 0, 0), new Color(0.2f, 0.2f, 0.22f), 6);
            hk.Box(new Vector3(0, 0.6f, 2.05f), new Vector3(1.4f, 0.4f, 0.1f), MeshKit.Glow(faction.bolt));
            var hg = hk.Make("head_mesh", head, true); hg.layer = Layers.Hull;
            s.aim = head;
            s.barrel = new GameObject("barrel").transform; s.barrel.SetParent(head, false); s.barrel.localPosition = new Vector3(0, 0, 6.5f);
            var bc = s.gameObject.AddComponent<BoxCollider>(); bc.size = new Vector3(7, 6, 7); bc.center = new Vector3(0, 2.5f, 0);
        }

        void AddHangarControl(Vector3 p)
        {
            var s = Make(SysKind.Hangar, "Hangar Control", p, 5000, false, false);
            var k = new MeshKit();
            k.Box(Vector3.zero, new Vector3(10, 3, 4), new Color(0.35f, 0.35f, 0.38f));
            k.Box(new Vector3(0, -0.6f, 2.05f), new Vector3(8, 1, 0.1f), MeshKit.Glow(new Color(1f, 0.8f, 0.3f)));
            Visual(s, k, new Vector3(10, 3, 4), Vector3.zero);
            s.aim = s.transform;
        }

        void AddLifeSupport(Vector3 p)
        {
            var s = Make(SysKind.LifeSupport, "Life Support", p, 4000, true, true);
            var k = new MeshKit();
            for (int i = 0; i < 3; i++)
            {
                k.Cyl(new Vector3(-4 + i * 4, 2.5f, -4), 1.2f, 5, Quaternion.identity, new Color(0.6f, 0.62f, 0.66f), 10);
                k.Cyl(new Vector3(-4 + i * 4, 2.5f, -4), 1.25f, 2.5f, Quaternion.identity, MeshKit.Glow(new Color(0.3f, 0.9f, 1f)), 10);
            }
            k.Box(new Vector3(0, 1, 2), new Vector3(8, 2, 3), new Color(0.4f, 0.42f, 0.45f));
            k.Box(new Vector3(0, 1.6f, 3.55f), new Vector3(6, 0.6f, 0.1f), MeshKit.Glow(new Color(0.3f, 1f, 0.6f)));
            Visual(s, k, new Vector3(10, 5, 6), new Vector3(0, 2.5f, -3));
            var aim = new GameObject("aim").transform; aim.SetParent(s.transform, false); aim.localPosition = new Vector3(0, 2, 0); s.aim = aim;
        }

        void AddReactor(Vector3 p)
        {
            var s = Make(SysKind.Reactor, "Reactor Core", p, 4500, true, true);
            var k = new MeshKit();
            k.Cyl(new Vector3(0, 0.5f, 0), 4, 1, Quaternion.identity, new Color(0.3f, 0.3f, 0.32f), 12);
            k.Cyl(new Vector3(0, 3.8f, 0), 2.2f, 5.6f, Quaternion.identity, MeshKit.Glow(new Color(1f, 0.55f, 0.2f)), 12);
            for (int i = 0; i < 4; i++) k.Box(Quaternion.Euler(0, i * 90, 0) * new Vector3(0, 3.5f, 3), new Vector3(1.2f, 6.5f, 1.2f), Quaternion.Euler(0, i * 90, 0), new Color(0.4f, 0.4f, 0.42f));
            k.Cyl(new Vector3(0, 7.2f, 0), 3.5f, 0.8f, Quaternion.identity, new Color(0.3f, 0.3f, 0.32f), 12);
            Visual(s, k, new Vector3(8, 8, 8), new Vector3(0, 4, 0));
            var aim = new GameObject("aim").transform; aim.SetParent(s.transform, false); aim.localPosition = new Vector3(0, 3.5f, 0); s.aim = aim;
        }

        // ── runtime ──────────────────────────────────────────────────────
        /// <summary>Wi-Fi guest: hull and system health from the host's snapshot.</summary>
        public void NetSet(float hullHp, float[] sys)
        {
            hull = hullHp;
            if (hull <= 0) destroyed = true;
            for (int i = 0; i < sys.Length && i < systems.Count; i++)
            {
                systems[i].hp = sys[i];
                if (sys[i] <= 0 && !systems[i].destroyed) systems[i].destroyed = true;
            }
        }

        public void Tick(float dt)
        {
            foreach (var s in systems) s.Tick(dt);
            if (shieldBubble) shieldBubble.enabled = ShieldUp && !destroyed;
            if (hangarField) hangarField.enabled = HangarOpen;
            if (destroyed || battle.over || Net.Session.IsClient) return;
            // ambient broadsides at the enemy capital ship
            broadside -= dt;
            if (broadside <= 0)
            {
                broadside = Random.Range(6f, 10f);
                var enemy = battle.space.ships[1 - team];
                if (enemy && !enemy.destroyed)
                {
                    var w = W.CapTurret("Broadside"); w.damage = 120; w.speed = 220; w.boltLen = 18; w.boltWidth = 1.6f; w.range = 1500;
                    Vector3 from = transform.TransformPoint(new Vector3(Random.Range(-120, 120), 20, 32));
                    Vector3 to = enemy.transform.TransformPoint(new Vector3(Random.Range(-120, 120), Random.Range(0, 20), 0));
                    Shots.Fire(w, from, (to - from).normalized, team, null, transform, faction.bolt);
                    Sfx.Play("cannon", from, 0.6f, 0.5f, 900);
                }
            }
        }

        public void OnSystemDestroyed(Subsystem s, Unit by)
        {
            Fx.Explosion(s.AimPoint, s.interior ? 2.2f : 4f, true);
            for (int i = 0; i < 4; i++) Fx.Explosion(s.AimPoint + Random.insideUnitSphere * (s.interior ? 3 : 8), 1.5f);
            hull -= maxHull * HullShare(s.kind);
            battle.OnSystemDestroyed(this, s, by);
            if (hull <= 0 && !destroyed) { hull = 0; destroyed = true; battle.OnCapitalDestroyed(this, by); return; }
            if (!crippled && !systems.Exists(x => x.critical && !x.destroyed)) { crippled = true; battle.OnCapitalCrippled(this, by); }
        }

        public static float HullShare(SysKind k)
        {
            switch (k) { case SysKind.Turret: return 0.04f; case SysKind.Shield: return 0.12f; case SysKind.Engine: return 0.1f; case SysKind.Hangar: return 0.08f; default: return 0.12f; }
        }

        public Vector3 Node(int i) => transform.TransformPoint(nodes[i]);

        public int NearestNode(Vector3 world)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < nodes.Count; i++) { float d = (Node(i) - world).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
            return best;
        }

        /// <summary>Next node on the shortest path from a to b (BFS over the small graph).</summary>
        public int NextNode(int a, int b)
        {
            if (a == b) return b;
            var prev = new int[nodes.Count];
            for (int i = 0; i < prev.Length; i++) prev[i] = -1;
            var q = new Queue<int>(); q.Enqueue(a); prev[a] = a;
            while (q.Count > 0)
            {
                int n = q.Dequeue();
                if (n == b) break;
                foreach (var l in links)
                {
                    int m = l[0] == n ? l[1] : l[1] == n ? l[0] : -1;
                    if (m >= 0 && prev[m] < 0) { prev[m] = n; q.Enqueue(m); }
                }
            }
            if (prev[b] < 0) return b;
            int cur = b;
            while (prev[cur] != a && prev[cur] != cur) cur = prev[cur];
            return cur;
        }

        public Vector3 ParkSlot(int i)
        {
            float x = (i % 4) * 16 - 24, z = i < 4 ? 20 : 6;
            return transform.TransformPoint(new Vector3(x, 0, z));
        }

        public Vector3 FootSpawn()
        {
            return transform.TransformPoint(new Vector3(Random.Range(-20f, 20f), 0.2f, Random.Range(-3f, 2f)));
        }
    }
}
