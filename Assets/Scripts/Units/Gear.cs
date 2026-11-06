using UnityEngine;

namespace BIS
{
    /// <summary>Anti-vehicle (and anti-infantry) mine. Arms after 1.5 s.</summary>
    public class Mine : MonoBehaviour
    {
        Unit owner; int team; float armAt; static Mesh mesh;
        public static void Place(Unit u, Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 2, Vector3.down, out var h, 6, Layers.SolidMask)) p = h.point;
            var go = new GameObject("mine");
            if (!mesh) { var k = new MeshKit(); k.Cyl(new Vector3(0, 0.05f, 0), 0.32f, 0.1f, Quaternion.identity, new Color(0.25f, 0.25f, 0.25f), 10); k.Cyl(new Vector3(0, 0.12f, 0), 0.08f, 0.04f, Quaternion.identity, MeshKit.Glow(Color.red), 6); mesh = k.Build("mine"); }
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = Mats.Lit;
            go.transform.position = p;
            var m = go.AddComponent<Mine>(); m.owner = u; m.team = u.team; m.armAt = Time.time + 1.5f;
            if (u.battle) u.battle.Track(go);
            Destroy(go, 120);
        }
        void Update()
        {
            if (Time.time < armAt || Battle.I == null) return;
            if (Time.frameCount % 4 != 0) return;
            Vector3 p = transform.position;
            foreach (var v in Battle.I.vehicles)
                if (v && v.Alive && v.Team != team && !v.IsEmpty && !v.def.Flies && (v.transform.position - p).sqrMagnitude < 9) { Blow(); return; }
            foreach (var u in Battle.I.units)
                if (u && u.Alive && u.team != team && !u.InVehicle && (u.transform.position - p).sqrMagnitude < 2.2f) { Blow(); return; }
        }
        void Blow()
        {
            var w = new WeaponDef { name = "Mine", kind = WKind.Rocket, damage = 0, splash = 4, splashDmg = 650, vsVehicle = 1, vsInfantry = 0.5f };
            Shots.Splash(transform.position + Vector3.up * 0.3f, 4, 650, w, owner, team, null);
            Fx.Explosion(transform.position, 1.2f);
            Destroy(gameObject);
        }
    }

    /// <summary>Remote charge: thrown, sticks where it lands, set off by pressing EQUIP again.</summary>
    public class Detpack : MonoBehaviour
    {
        Unit owner; int team; Vector3 vel; bool stuck; static Mesh mesh; Transform stuckTo; Vector3 localPos;
        public static Detpack Throw(Unit u, Vector3 p, Vector3 dir)
        {
            var go = new GameObject("detpack");
            if (!mesh) { var k = new MeshKit(); k.Box(Vector3.zero, new Vector3(0.3f, 0.14f, 0.22f), new Color(0.4f, 0.35f, 0.2f)); k.Box(new Vector3(0, 0.08f, 0), new Vector3(0.06f, 0.03f, 0.06f), MeshKit.Glow(Color.red)); mesh = k.Build("detpack"); }
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = Mats.Lit;
            go.transform.position = p;
            var d = go.AddComponent<Detpack>(); d.owner = u; d.team = u.team; d.vel = dir * 9 + Vector3.up * 2.5f;
            if (u.battle) u.battle.Track(go);
            return d;
        }
        void Update()
        {
            if (stuck)
            {
                if (stuckTo) transform.position = stuckTo.TransformPoint(localPos);
                return;
            }
            float dt = Time.deltaTime;
            vel += Physics.gravity * dt;
            Vector3 step = vel * dt;
            if (Physics.Raycast(transform.position, step.normalized, out var h, step.magnitude + 0.1f, Layers.ShotMask & ~(1 << Layers.Unit), QueryTriggerInteraction.Ignore))
            {
                stuck = true;
                transform.position = h.point + h.normal * 0.06f;
                transform.rotation = Quaternion.FromToRotation(Vector3.up, h.normal);
                stuckTo = h.collider.transform; localPos = stuckTo.InverseTransformPoint(transform.position);
                Sfx.Play("thud", h.point, 0.5f, 1.8f);
            }
            else transform.position += step;
        }
        public void Blow()
        {
            if (!this) return;
            var w = new WeaponDef { name = "Detpack", kind = WKind.Rocket, damage = 0, splash = 6, splashDmg = 1100, vsVehicle = 1.4f, vsInfantry = 0.6f };
            Shots.Splash(transform.position, 6, 1100, w, owner, team, null);
            Fx.Explosion(transform.position, 1.6f, true);
            Destroy(gameObject);
        }
    }

    /// <summary>Heals and resupplies friendly infantry standing near it for a while.</summary>
    public class SupplyCrate : MonoBehaviour
    {
        int team; float tick; int ticks; static Mesh mesh;
        public static void Place(Unit u, Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 2, Vector3.down, out var h, 6, Layers.SolidMask)) p = h.point;
            var go = new GameObject("supply");
            if (!mesh) { var k = new MeshKit(); k.Box(new Vector3(0, 0.3f, 0), new Vector3(0.8f, 0.6f, 0.6f), new Color(0.3f, 0.32f, 0.3f)); k.Box(new Vector3(0, 0.3f, 0.31f), new Vector3(0.3f, 0.1f, 0.02f), MeshKit.Glow(new Color(0.3f, 1f, 0.4f))); k.Box(new Vector3(0, 0.3f, 0.31f), new Vector3(0.1f, 0.3f, 0.02f), MeshKit.Glow(new Color(0.3f, 1f, 0.4f))); mesh = k.Build("supply"); }
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = Mats.Lit;
            go.transform.position = p; go.transform.rotation = Quaternion.Euler(0, u.yaw, 0);
            go.AddComponent<SupplyCrate>().team = u.team;
            if (u.battle) u.battle.Track(go);
            Destroy(go, 30);
        }
        void Update()
        {
            tick -= Time.deltaTime;
            if (tick > 0 || Battle.I == null) return;
            tick = 1f; ticks++;
            foreach (var u in Battle.I.units)
                if (u && u.Alive && u.team == team && !u.InVehicle && (u.transform.position - transform.position).sqrMagnitude < 20)
                {
                    u.Heal(u.maxHp * 0.08f);
                    // scoped rifles get one round every 3 s here, so a crate can't keep a sniper going for ever
                    foreach (var w in u.weapons) if (w != null && w.def.clip > 0 && (w.def.zoomFov <= 0 || ticks % 3 == 0)) w.ammo = Mathf.Min(w.def.ammo, w.ammo + (w.def.zoomFov > 0 ? 1 : Mathf.Max(1, w.def.clip / 2)));
                    Fx.Emit(u.AimPoint, Vector3.up * 2, new Color(0.3f, 1f, 0.4f, 1), 0.3f, 0.5f);
                }
        }
    }

    /// <summary>Health + ammo canister dropped by fallen soldiers.</summary>
    public class Pickup : MonoBehaviour
    {
        static Mesh mesh; float spin;
        public static void Drop(Vector3 p)
        {
            if (Battle.I == null) return;
            if (Physics.Raycast(p + Vector3.up, Vector3.down, out var h, 4, Layers.SolidMask)) p = h.point + Vector3.up * 0.35f;
            var go = new GameObject("pickup");
            if (!mesh) { var k = new MeshKit(); k.Cyl(Vector3.zero, 0.16f, 0.36f, Quaternion.identity, new Color(0.85f, 0.85f, 0.85f), 8); k.Cyl(Vector3.zero, 0.17f, 0.12f, Quaternion.identity, MeshKit.Glow(new Color(0.3f, 1f, 0.4f)), 8); mesh = k.Build("pickup"); }
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = Mats.Lit;
            go.transform.position = p;
            go.AddComponent<Pickup>();
            Battle.I.Track(go);
            Destroy(go, 25);
        }
        void Update()
        {
            spin += Time.deltaTime * 120;
            transform.rotation = Quaternion.Euler(0, spin, 15);
            if (Battle.I == null || Time.frameCount % 3 != 0) return;
            foreach (var u in Battle.I.units)
                if (u && u.Alive && !u.InVehicle && (u.transform.position + Vector3.up * 0.4f - transform.position).sqrMagnitude < 1.8f)
                {
                    u.Heal(u.maxHp * 0.3f);
                    foreach (var w in u.weapons) if (w != null && w.def.clip > 0) w.ammo = Mathf.Min(w.def.ammo, w.ammo + w.def.clip);
                    if (u.part.isPlayer) Sfx.Play2D("ui2", 0.6f);
                    Destroy(gameObject);
                    return;
                }
        }
    }
}
