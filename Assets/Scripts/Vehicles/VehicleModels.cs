using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    /// <summary>Code-built vehicle and starship models, styled per faction. Cached per faction + vehicle.</summary>
    public class VModel
    {
        public Mesh body, turret, gun, gunnerTurret;
        public Vector3 turretPos, gunPos, gunnerPos;
        public Vector3[] muzzles, gunnerMuzzles, engines;
        public Bounds bounds;
        public float hover = 1.2f;
        public float camDist = 9, camHeight = 3;
        public Material mat;          // null = Mats.Lit (vertex colours); imported ships use a textured material
    }

    public static class VehicleModels
    {
        static readonly Dictionary<string, VModel> cache = new Dictionary<string, VModel>();
        static Color G(Color c) { c.a = 0; return c; }
        static Color D(Color c, float k) { var r = c * k; r.a = 1; return r; }

        public static VModel Get(VehicleDef d, FactionDef f)
        {
            string key = f.id + "_" + d.kind;
            if (cache.TryGetValue(key, out var m) && m.body) return m;
            m = new VModel();
            switch (d.kind)
            {
                case VKind.Tank: Tank(m, f); break;
                case VKind.Transport: Transport(m, f); break;
                case VKind.Air: Gunship(m, f); break;
                case VKind.Fighter: Fighter(m, f); break;
                case VKind.Bomber: Bomber(m, f); break;
                case VKind.Interceptor: Interceptor(m, f); break;
                case VKind.Dropship: Dropship(m, f); break;
                default: Support(m, f); break;
            }
            m.bounds = m.body.bounds;
            if (d.IsShip && d.kind != VKind.Dropship) UseShipArt(m, d, f);
            cache[key] = m;
            return m;
        }

        // Quaternius "Ultimate Spaceships" (CC0) per faction and role: model, texture, size vs the code-built ship.
        // Pancake and Spitfire are left out on purpose (too close to well-known film ships).
        static readonly string[,] ShipArt =
        {
            //            Fighter                      Bomber                          Interceptor                  Support
            /* Concord */ { "Striker:Striker_Blue",     "Executioner:Executioner_Blue", "Dispatcher:Dispatcher_Purple", "Insurgent:Insurgent_Blue" },
            /* Vorrkai */ { "Zenith:Zenith_Red",        "Insurgent:Insurgent_Red",      "Bob:Bob_Orange",               "Imperial:Imperial_Red" },
            /* Synod   */ { "Omen:Omen_Orange",         "Imperial:Imperial_Orange",     "Zenith:Zenith_Red",            "Challenger:Challenger_Green" },
            /* Veil    */ { "Dispatcher:Dispatcher_Purple", "Insurgent:Insurgent_Purple", "Striker:Striker_Blue",       "Challenger:Challenger_Green" },
        };

        static void UseShipArt(VModel m, VehicleDef d, FactionDef f)
        {
            int fi = System.Array.IndexOf(Factions.All, f);
            int role = d.kind == VKind.Fighter ? 0 : d.kind == VKind.Bomber ? 1 : d.kind == VKind.Interceptor ? 2 : 3;
            if (fi < 0 || fi >= ShipArt.GetLength(0)) return;
            var parts = ShipArt[fi, role].Split(':');
            var tex = ImportedModels.Tex("Models/Ships/" + parts[1]);
            var raw = ImportedModels.Load("Models/Ships/" + parts[0], false, Quaternion.identity, 0, 1, false);
            if (!raw || !tex) return;
            // as long as the code-built ship it replaces (a little longer for bombers and support craft)
            float want = m.bounds.size.z * (role == 1 || role == 3 ? 1.15f : 1.05f);
            float k = want / Mathf.Max(0.01f, raw.bounds.size.z);
            var mesh = ImportedModels.Load("Models/Ships/" + parts[0], false, Quaternion.identity, 0, k, false);
            Color tint = Color.Lerp(Color.white, f.second, 0.18f); tint.a = 1;
            m.body = mesh;
            m.mat = Mats.LitTex(tex, tint);
            m.bounds = mesh.bounds;
            // engines at the tail, guns at the front corners of the new hull
            var b = mesh.bounds;
            m.engines = new[] { new Vector3(-b.extents.x * 0.25f, b.center.y, b.min.z), new Vector3(b.extents.x * 0.25f, b.center.y, b.min.z) };
            m.muzzles = new[] { new Vector3(-b.extents.x * 0.45f, b.center.y, b.max.z * 0.7f), new Vector3(b.extents.x * 0.45f, b.center.y, b.max.z * 0.7f) };
        }

        static void Repulsors(MeshKit k, float w, float l, Color glow)
        {
            k.Box(new Vector3(-w * 0.35f, -0.62f, l * 0.3f), new Vector3(w * 0.22f, 0.12f, l * 0.22f), G(glow));
            k.Box(new Vector3(w * 0.35f, -0.62f, l * 0.3f), new Vector3(w * 0.22f, 0.12f, l * 0.22f), G(glow));
            k.Box(new Vector3(-w * 0.35f, -0.62f, -l * 0.3f), new Vector3(w * 0.22f, 0.12f, l * 0.22f), G(glow));
            k.Box(new Vector3(w * 0.35f, -0.62f, -l * 0.3f), new Vector3(w * 0.22f, 0.12f, l * 0.22f), G(glow));
        }

        static void Tank(VModel m, FactionDef f)
        {
            var k = new MeshKit();
            Color a = f.main, b = f.second, dk = D(f.main, 0.5f), glow = f.bolt;
            switch (f.body)
            {
                case Body.Reptile:
                    k.Taper(Vector3.zero, new Vector2(4.4f, 7.4f), new Vector2(3.6f, 6f), 1.3f, Quaternion.identity, a);
                    k.Nose(new Vector3(0, 0, 4.2f), new Vector3(4f, 1.2f, 1.6f), Quaternion.identity, b, -0.3f);
                    for (int i = 0; i < 4; i++) k.Cone(new Vector3(-1.6f + i * 1.07f, 0.8f, -2.6f), 0.18f, 1.2f, Quaternion.Euler(-40, 0, 0), f.accent, 5);
                    k.Cone(new Vector3(-2.3f, 0.2f, 3.2f), 0.25f, 1.4f, Quaternion.Euler(70, -20, 0), f.accent, 5);
                    k.Cone(new Vector3(2.3f, 0.2f, 3.2f), 0.25f, 1.4f, Quaternion.Euler(70, 20, 0), f.accent, 5);
                    break;
                case Body.Machine:
                    k.Box(Vector3.zero, new Vector3(4.6f, 1.5f, 7.2f), a);
                    k.Box(new Vector3(0, 0.1f, 3.8f), new Vector3(4.2f, 1.1f, 0.6f), dk);
                    k.Box(new Vector3(-2.4f, 0, 0), new Vector3(0.3f, 1.2f, 6.6f), b);
                    k.Box(new Vector3(2.4f, 0, 0), new Vector3(0.3f, 1.2f, 6.6f), b);
                    break;
                case Body.Slender:
                    k.Taper(Vector3.zero, new Vector2(4f, 7.6f), new Vector2(2.6f, 5.6f), 1.2f, Quaternion.identity, a);
                    k.Wedge(new Vector3(-2.6f, 0, -1), new Vector3(1.2f, 0.6f, 5f), Quaternion.Euler(0, 0, 90), b);
                    k.Wedge(new Vector3(2.6f, 0, -1), new Vector3(1.2f, 0.6f, 5f), Quaternion.Euler(0, 0, -90), b);
                    k.Nose(new Vector3(0, -0.1f, 4.4f), new Vector3(2.6f, 0.9f, 1.8f), Quaternion.identity, a);
                    break;
                default:
                    k.Taper(Vector3.zero, new Vector2(4.6f, 7.4f), new Vector2(3.8f, 6.2f), 1.4f, Quaternion.identity, a);
                    k.Wedge(new Vector3(0, -0.1f, 4.1f), new Vector3(4.2f, 1.2f, 1.2f), Quaternion.Euler(90, 0, 0), a);
                    k.Box(new Vector3(0, 0.72f, -1.2f), new Vector3(3.2f, 0.1f, 2.2f), b);
                    k.Box(new Vector3(-2.35f, 0.1f, 0), new Vector3(0.2f, 0.6f, 6.4f), b);
                    k.Box(new Vector3(2.35f, 0.1f, 0), new Vector3(0.2f, 0.6f, 6.4f), b);
                    break;
            }
            Repulsors(k, 4.4f, 7f, glow);
            k.Box(new Vector3(-1.3f, 0.2f, -3.75f), new Vector3(1f, 0.6f, 0.2f), G(new Color(1f, 0.55f, 0.2f)));
            k.Box(new Vector3(1.3f, 0.2f, -3.75f), new Vector3(1f, 0.6f, 0.2f), G(new Color(1f, 0.55f, 0.2f)));
            m.body = k.Build("tank_" + f.id);
            k.Clear();
            // turret
            if (f.body == Body.Machine) k.Box(new Vector3(0, 0.5f, 0), new Vector3(2.8f, 1.1f, 3.2f), D(a, 0.85f));
            else if (f.body == Body.Slender) k.Sphere(new Vector3(0, 0.45f, 0), new Vector3(2.8f, 1.2f, 3.4f), Quaternion.identity, D(a, 0.9f), 8, 4);
            else k.Taper(new Vector3(0, 0.5f, 0), new Vector2(2.8f, 3.4f), new Vector2(2.2f, 2.6f), 1f, Quaternion.identity, D(a, 0.9f));
            k.Box(new Vector3(0, 1.05f, -0.6f), new Vector3(0.7f, 0.2f, 0.7f), b);
            m.turret = k.Build("tank_turret_" + f.id);
            k.Clear();
            k.Cyl(new Vector3(0, 0, 1.9f), 0.2f, 3.6f, Quaternion.Euler(90, 0, 0), dk, 8);
            k.Cyl(new Vector3(0, 0, 3.7f), 0.28f, 0.4f, Quaternion.Euler(90, 0, 0), D(b, 0.7f), 8);
            k.Box(new Vector3(0, 0, 0.3f), new Vector3(0.8f, 0.6f, 0.8f), D(a, 0.7f));
            m.gun = k.Build("tank_gun_" + f.id);
            k.Clear();
            k.Box(new Vector3(0, 0.2f, 0), new Vector3(0.4f, 0.4f, 0.5f), dk);
            k.Cyl(new Vector3(0, 0.2f, 0.6f), 0.06f, 0.9f, Quaternion.Euler(90, 0, 0), dk, 6);
            m.gunnerTurret = k.Build("tank_mg_" + f.id);
            m.turretPos = new Vector3(0, 0.7f, -0.3f);
            m.gunPos = new Vector3(0, 0.55f, 0.9f);
            m.muzzles = new[] { new Vector3(0, 0, 3.9f) };
            m.gunnerPos = new Vector3(0.7f, 1.6f, -1.1f);
            m.gunnerMuzzles = new[] { new Vector3(0, 0.2f, 1.1f) };
            m.engines = new[] { new Vector3(-1.3f, 0.2f, -3.9f), new Vector3(1.3f, 0.2f, -3.9f) };
            m.hover = 1.3f; m.camDist = 11; m.camHeight = 4f;
        }

        static void Transport(VModel m, FactionDef f)
        {
            var k = new MeshKit();
            Color a = f.main, b = f.second, dk = D(f.main, 0.5f), glow = f.bolt;
            k.Taper(new Vector3(0, 0, -0.5f), new Vector2(3.8f, 8.6f), new Vector2(3.2f, 7.4f), 1.8f, Quaternion.identity, a);
            k.Nose(new Vector3(0, -0.2f, 4.5f), new Vector3(3.4f, 1.4f, 2.2f), Quaternion.identity, f.body == Body.Reptile ? b : a, -0.2f);
            k.Box(new Vector3(0, 0.75f, 3.1f), new Vector3(2.2f, 0.45f, 1.0f), G(D(glow, 0.6f)));
            k.Box(new Vector3(-1.95f, 0.1f, -0.5f), new Vector3(0.12f, 1.0f, 6f), b);
            k.Box(new Vector3(1.95f, 0.1f, -0.5f), new Vector3(0.12f, 1.0f, 6f), b);
            if (f.body == Body.Slender) { k.Wedge(new Vector3(0, 1.3f, -2.5f), new Vector3(0.3f, 1.6f, 3f), Quaternion.identity, b); }
            if (f.body == Body.Reptile) for (int i = 0; i < 3; i++) k.Cone(new Vector3(0, 1.1f, -3.5f + i * 1.4f), 0.2f, 0.9f, Quaternion.Euler(-30, 0, 0), f.accent, 5);
            Repulsors(k, 3.6f, 8f, glow);
            k.Box(new Vector3(0, 0, -4.85f), new Vector3(2.4f, 0.8f, 0.2f), G(new Color(1f, 0.55f, 0.2f)));
            m.body = k.Build("apc_" + f.id);
            k.Clear();
            k.Box(new Vector3(0, 0.12f, 0), new Vector3(0.5f, 0.3f, 0.6f), dk);
            k.Cyl(new Vector3(-0.2f, 0.1f, 0.7f), 0.06f, 1.0f, Quaternion.Euler(90, 0, 0), dk, 6);
            k.Cyl(new Vector3(0.2f, 0.1f, 0.7f), 0.06f, 1.0f, Quaternion.Euler(90, 0, 0), dk, 6);
            m.gun = k.Build("apc_gun_" + f.id);
            k.Clear();
            k.Box(new Vector3(0, 0.35f, 0), new Vector3(1.2f, 0.7f, 1.4f), D(a, 0.8f));
            k.Box(new Vector3(-0.5f, 0.45f, 0.5f), new Vector3(0.4f, 0.4f, 1.0f), dk);
            k.Box(new Vector3(0.5f, 0.45f, 0.5f), new Vector3(0.4f, 0.4f, 1.0f), dk);
            m.gunnerTurret = k.Build("apc_pod_" + f.id);
            m.gunPos = new Vector3(0, -0.4f, 5.2f);
            m.muzzles = new[] { new Vector3(-0.2f, 0.1f, 1.2f), new Vector3(0.2f, 0.1f, 1.2f) };
            m.gunnerPos = new Vector3(0, 0.95f, -2.6f);
            m.gunnerMuzzles = new[] { new Vector3(-0.5f, 0.45f, 1.0f), new Vector3(0.5f, 0.45f, 1.0f) };
            m.engines = new[] { new Vector3(0, 0, -5f) };
            m.hover = 1.4f; m.camDist = 12; m.camHeight = 4.5f;
        }

        static void Wing(MeshKit k, Vector3 root, float span, float chord, float sweep, float thick, float dihedral, Color c, bool right)
        {
            float s = right ? 1 : -1;
            Vector3 r0 = root + new Vector3(0, 0, chord * 0.5f), r1 = root + new Vector3(0, 0, -chord * 0.5f);
            Vector3 tip = root + new Vector3(s * span, span * dihedral, -sweep);
            Vector3 t0 = tip + new Vector3(0, 0, chord * 0.2f), t1 = tip + new Vector3(0, 0, -chord * 0.2f);
            Vector3 up = Vector3.up * thick * 0.5f;
            if (right)
            {
                k.Quad(r0 + up, t0 + up, t1 + up, r1 + up, c);
                k.Quad(r1 - up, t1 - up, t0 - up, r0 - up, D(c, 0.8f));
            }
            else
            {
                k.Quad(r1 + up, t1 + up, t0 + up, r0 + up, c);
                k.Quad(r0 - up, t0 - up, t1 - up, r1 - up, D(c, 0.8f));
            }
            k.Quad(t0 - up, t0 + up, r0 + up, r0 - up, c);
            k.Quad(r1 - up, r1 + up, t1 + up, t1 - up, c);
        }

        static void Gunship(VModel m, FactionDef f)
        {
            var k = new MeshKit();
            Color a = f.main, b = f.second, dk = D(f.main, 0.5f), glow = f.bolt;
            k.Taper(Vector3.zero, new Vector2(2.6f, 8f), new Vector2(2.0f, 6.6f), 1.8f, Quaternion.Euler(0, 0, 0), a);
            k.Nose(new Vector3(0, -0.1f, 5.2f), new Vector3(2.2f, 1.6f, 2.6f), Quaternion.identity, a, -0.2f);
            k.Box(new Vector3(0, 0.55f, 3.8f), new Vector3(1.6f, 0.6f, 1.6f), G(D(glow, 0.7f)));
            Wing(k, new Vector3(1.1f, 0.4f, -0.5f), 4.6f, 3f, f.body == Body.Slender ? 2.4f : 1.2f, 0.25f, f.body == Body.Reptile ? -0.15f : 0.05f, b, true);
            Wing(k, new Vector3(-1.1f, 0.4f, -0.5f), 4.6f, 3f, f.body == Body.Slender ? 2.4f : 1.2f, 0.25f, f.body == Body.Reptile ? -0.15f : 0.05f, b, false);
            k.Cyl(new Vector3(-4.6f, 0.5f, -1f), 0.55f, 2.6f, Quaternion.Euler(90, 0, 0), dk, 8);
            k.Cyl(new Vector3(4.6f, 0.5f, -1f), 0.55f, 2.6f, Quaternion.Euler(90, 0, 0), dk, 8);
            k.Cyl(new Vector3(-4.6f, 0.5f, -2.35f), 0.45f, 0.1f, Quaternion.Euler(90, 0, 0), G(new Color(1f, 0.55f, 0.2f)), 8);
            k.Cyl(new Vector3(4.6f, 0.5f, -2.35f), 0.45f, 0.1f, Quaternion.Euler(90, 0, 0), G(new Color(1f, 0.55f, 0.2f)), 8);
            k.Wedge(new Vector3(0, 1.4f, -3.2f), new Vector3(0.25f, 1.6f, 2.2f), Quaternion.identity, b);
            m.body = k.Build("gunship_" + f.id);
            k.Clear();
            k.Sphere(Vector3.zero, new Vector3(0.7f, 0.6f, 0.7f), Quaternion.identity, dk, 6, 4);
            k.Cyl(new Vector3(0, 0, 0.6f), 0.07f, 1.0f, Quaternion.Euler(90, 0, 0), dk, 6);
            m.gunnerTurret = k.Build("gunship_chin_" + f.id);
            m.muzzles = new[] { new Vector3(-4.6f, 0.2f, 0.6f), new Vector3(4.6f, 0.2f, 0.6f) };
            m.gunnerPos = new Vector3(0, -1.05f, 4.4f);
            m.gunnerMuzzles = new[] { new Vector3(0, 0, 1.1f) };
            m.engines = new[] { new Vector3(-4.6f, 0.5f, -2.5f), new Vector3(4.6f, 0.5f, -2.5f) };
            m.hover = 0; m.camDist = 14; m.camHeight = 4.5f;
        }

        static void Fighter(VModel m, FactionDef f)
        {
            var k = new MeshKit();
            Color a = f.main, b = f.second, dk = D(f.main, 0.5f), glow = f.bolt, eng = new Color(1f, 0.6f, 0.3f);
            if (f.body == Body.Machine) eng = new Color(1f, 0.75f, 0.25f);
            if (f.body == Body.Slender) eng = new Color(0.5f, 1f, 0.85f);
            switch (f.body)
            {
                case Body.Human: // forward-swept wings with wingtip cannons
                    k.Taper(Vector3.zero, new Vector2(1.6f, 6f), new Vector2(1.2f, 4.8f), 1.2f, Quaternion.identity, a);
                    k.Nose(new Vector3(0, 0, 4f), new Vector3(1.4f, 1.1f, 2.2f), Quaternion.identity, a);
                    k.Box(new Vector3(0, 0.6f, 1.6f), new Vector3(0.9f, 0.4f, 1.4f), G(D(glow, 0.6f)));
                    Wing(k, new Vector3(0.7f, 0, -1.4f), 4.2f, 2.2f, -1.6f, 0.2f, 0, b, true);
                    Wing(k, new Vector3(-0.7f, 0, -1.4f), 4.2f, 2.2f, -1.6f, 0.2f, 0, b, false);
                    k.Cyl(new Vector3(4.9f, 0, 0.6f), 0.12f, 2.2f, Quaternion.Euler(90, 0, 0), dk, 6);
                    k.Cyl(new Vector3(-4.9f, 0, 0.6f), 0.12f, 2.2f, Quaternion.Euler(90, 0, 0), dk, 6);
                    m.muzzles = new[] { new Vector3(-4.9f, 0, 1.8f), new Vector3(4.9f, 0, 1.8f) };
                    break;
                case Body.Reptile: // three claw wings
                    k.Taper(Vector3.zero, new Vector2(1.5f, 5.6f), new Vector2(1.2f, 4.4f), 1.4f, Quaternion.identity, a);
                    k.Nose(new Vector3(0, 0, 3.9f), new Vector3(1.4f, 1.2f, 2.4f), Quaternion.identity, b, -0.4f);
                    k.Box(new Vector3(0, 0.7f, 1.4f), new Vector3(0.9f, 0.3f, 1.2f), G(new Color(1f, 0.85f, 0.2f) * 0.6f));
                    for (int i = 0; i < 3; i++)
                    {
                        var q = Quaternion.Euler(0, 0, i * 120 + 90);
                        k.Box(q * new Vector3(0, 2.2f, -0.8f), new Vector3(0.2f, 3.4f, 2.4f), q, b);
                        k.Cone(q * new Vector3(0, 4.1f, 0.2f), 0.18f, 1.4f, q * Quaternion.Euler(80, 0, 0), f.accent, 5);
                    }
                    m.muzzles = new[] { Quaternion.Euler(0, 0, 210) * new Vector3(0, 3.6f, 0.6f), Quaternion.Euler(0, 0, -30) * new Vector3(0, 3.6f, 0.6f) };
                    break;
                case Body.Machine: // spindle core with ring wing
                    k.Cyl(Vector3.zero, 0.9f, 6f, Quaternion.Euler(90, 0, 0), a, 8);
                    k.Cone(new Vector3(0, 0, 4.2f), 0.9f, 2.4f, Quaternion.Euler(90, 0, 0), a, 8);
                    k.Cyl(new Vector3(0, 0, 3.05f), 0.6f, 0.2f, Quaternion.Euler(90, 0, 0), G(eng), 8);
                    for (int i = 0; i < 8; i++)
                    {
                        float an = i * 45;
                        var q = Quaternion.Euler(0, 0, an);
                        k.Box(q * new Vector3(0, 3.2f, -1.2f), new Vector3(2.3f, 0.3f, 1.6f), q, b);
                    }
                    k.Box(new Vector3(0, 1.7f, -1.2f), new Vector3(0.25f, 2.6f, 1.2f), dk);
                    k.Box(new Vector3(0, -1.7f, -1.2f), new Vector3(0.25f, 2.6f, 1.2f), dk);
                    k.Box(new Vector3(1.7f, 0, -1.2f), new Vector3(2.6f, 0.25f, 1.2f), dk);
                    k.Box(new Vector3(-1.7f, 0, -1.2f), new Vector3(2.6f, 0.25f, 1.2f), dk);
                    m.muzzles = new[] { new Vector3(-3.2f, 0, 0.2f), new Vector3(3.2f, 0, 0.2f) };
                    break;
                default: // crescent
                    k.Sphere(Vector3.zero, new Vector3(1.6f, 1.2f, 5.6f), Quaternion.identity, a, 8, 5);
                    k.Box(new Vector3(0, 0.45f, 1.2f), new Vector3(0.7f, 0.3f, 1.4f), G(D(glow, 0.6f)));
                    for (int i = -4; i <= 4; i++)
                    {
                        float t = i / 4f;
                        var p = new Vector3(t * 5f, 0, 1.6f * (1 - t * t) - 1.6f);
                        k.Box(p, new Vector3(1.3f, 0.18f, 1.2f + (1 - Mathf.Abs(t))), Quaternion.Euler(0, -t * 40, 0), b);
                    }
                    m.muzzles = new[] { new Vector3(-5f, 0, 0.2f), new Vector3(5f, 0, 0.2f) };
                    break;
            }
            k.Cyl(new Vector3(-0.45f, 0, -3.05f), 0.38f, 0.15f, Quaternion.Euler(90, 0, 0), G(eng), 8);
            k.Cyl(new Vector3(0.45f, 0, -3.05f), 0.38f, 0.15f, Quaternion.Euler(90, 0, 0), G(eng), 8);
            m.body = k.Build("fighter_" + f.id);
            m.engines = new[] { new Vector3(-0.45f, 0, -3.2f), new Vector3(0.45f, 0, -3.2f) };
            m.camDist = 15; m.camHeight = 4f;
        }

        static void Bomber(VModel m, FactionDef f)
        {
            var k = new MeshKit();
            Color a = f.main, b = f.second, dk = D(f.main, 0.5f), glow = f.bolt, eng = new Color(1f, 0.6f, 0.3f);
            k.Taper(Vector3.zero, new Vector2(3.6f, 9f), new Vector2(3.0f, 7.6f), 2.2f, Quaternion.identity, a);
            k.Nose(new Vector3(0, -0.2f, 5.6f), new Vector3(3.2f, 1.8f, 2.4f), Quaternion.identity, a, -0.2f);
            k.Box(new Vector3(0, 0.95f, 3.8f), new Vector3(1.6f, 0.5f, 1.4f), G(D(glow, 0.6f)));
            float sweep = f.body == Body.Slender ? 3f : f.body == Body.Reptile ? 2f : 1f;
            Wing(k, new Vector3(1.6f, 0, -1), 6f, 4f, sweep, 0.35f, f.body == Body.Reptile ? -0.12f : 0, b, true);
            Wing(k, new Vector3(-1.6f, 0, -1), 6f, 4f, sweep, 0.35f, f.body == Body.Reptile ? -0.12f : 0, b, false);
            k.Cyl(new Vector3(-3.2f, -0.2f, -1.5f), 0.9f, 5f, Quaternion.Euler(90, 0, 0), dk, 8);
            k.Cyl(new Vector3(3.2f, -0.2f, -1.5f), 0.9f, 5f, Quaternion.Euler(90, 0, 0), dk, 8);
            k.Cyl(new Vector3(-3.2f, -0.2f, -4.05f), 0.75f, 0.12f, Quaternion.Euler(90, 0, 0), G(eng), 8);
            k.Cyl(new Vector3(3.2f, -0.2f, -4.05f), 0.75f, 0.12f, Quaternion.Euler(90, 0, 0), G(eng), 8);
            k.Box(new Vector3(0, -1.2f, 0), new Vector3(1.6f, 0.5f, 4f), dk);
            if (f.body == Body.Machine) k.Box(new Vector3(0, 1.3f, -1), new Vector3(1.2f, 0.6f, 5f), b);
            m.body = k.Build("bomber_" + f.id);
            k.Clear();
            k.Sphere(Vector3.zero, new Vector3(0.9f, 0.8f, 0.9f), Quaternion.identity, dk, 6, 4);
            k.Cyl(new Vector3(-0.15f, 0, 0.6f), 0.06f, 1.0f, Quaternion.Euler(90, 0, 0), dk, 6);
            k.Cyl(new Vector3(0.15f, 0, 0.6f), 0.06f, 1.0f, Quaternion.Euler(90, 0, 0), dk, 6);
            m.gunnerTurret = k.Build("bomber_tail_" + f.id);
            m.muzzles = new[] { new Vector3(-0.9f, -0.6f, 6.4f), new Vector3(0.9f, -0.6f, 6.4f) };
            m.gunnerPos = new Vector3(0, 1.3f, -3.6f);
            m.gunnerMuzzles = new[] { new Vector3(0, 0, 1.1f) };
            m.engines = new[] { new Vector3(-3.2f, -0.2f, -4.2f), new Vector3(3.2f, -0.2f, -4.2f) };
            m.camDist = 21; m.camHeight = 6f;
        }

        static void Interceptor(VModel m, FactionDef f)
        {
            // a long dart: needle nose, low delta wings, twin canted tail fins, one big engine
            var k = new MeshKit();
            Color a = f.main, b = f.second, dk = D(f.main, 0.5f), glow = f.bolt, eng = new Color(1f, 0.6f, 0.3f);
            if (f.body == Body.Machine) eng = new Color(1f, 0.75f, 0.25f);
            if (f.body == Body.Slender) eng = new Color(0.5f, 1f, 0.85f);
            k.Taper(new Vector3(0, 0, -0.6f), new Vector2(1.1f, 4.2f), new Vector2(0.7f, 3.4f), 0.8f, Quaternion.identity, a);
            k.Nose(new Vector3(0, 0, 2.9f), new Vector3(1.0f, 0.75f, 3.2f), Quaternion.identity, a, f.body == Body.Reptile ? -0.5f : 0);
            k.Box(new Vector3(0, 0.42f, 0.9f), new Vector3(0.55f, 0.22f, 1.2f), G(D(glow, 0.6f)));
            float sweep = f.body == Body.Slender ? 3.2f : f.body == Body.Reptile ? 2.6f : 2.2f;
            float dih = f.body == Body.Reptile ? -0.18f : f.body == Body.Machine ? 0 : 0.06f;
            Wing(k, new Vector3(0.45f, -0.15f, -0.2f), 3.0f, 2.6f, sweep, 0.14f, dih, b, true);
            Wing(k, new Vector3(-0.45f, -0.15f, -0.2f), 3.0f, 2.6f, sweep, 0.14f, dih, b, false);
            k.Wedge(new Vector3(0.45f, 0.7f, -2.3f), new Vector3(0.12f, 1.2f, 1.4f), Quaternion.Euler(0, 0, -18), b);
            k.Wedge(new Vector3(-0.45f, 0.7f, -2.3f), new Vector3(0.12f, 1.2f, 1.4f), Quaternion.Euler(0, 0, 18), b);
            if (f.body == Body.Machine) { k.Box(new Vector3(0, -0.5f, 0.5f), new Vector3(0.2f, 0.4f, 3f), dk); }
            if (f.body == Body.Reptile) { k.Cone(new Vector3(2.6f, -0.6f, -1.6f), 0.12f, 1.2f, Quaternion.Euler(80, 0, 0), f.accent, 5); k.Cone(new Vector3(-2.6f, -0.6f, -1.6f), 0.12f, 1.2f, Quaternion.Euler(80, 0, 0), f.accent, 5); }
            k.Cyl(new Vector3(0, 0, -2.8f), 0.45f, 0.6f, Quaternion.Euler(90, 0, 0), dk, 8);
            k.Cyl(new Vector3(0, 0, -3.12f), 0.36f, 0.06f, Quaternion.Euler(90, 0, 0), G(eng), 8);
            m.body = k.Build("interceptor_" + f.id);
            m.muzzles = new[] { new Vector3(-1.4f, -0.2f, 0.6f), new Vector3(1.4f, -0.2f, 0.6f) };
            m.engines = new[] { new Vector3(0, 0, -3.2f) };
            m.camDist = 12; m.camHeight = 3.5f;
        }

        /// <summary>The boarding shuttle: a boxy armoured hull with a rear ramp, side doors and a cockpit up front.</summary>
        static void Dropship(VModel m, FactionDef f)
        {
            var k = new MeshKit();
            Color a = f.main, b = f.second, dk = D(f.main, 0.5f), glow = f.bolt, eng = new Color(1f, 0.6f, 0.3f);
            k.Box(new Vector3(0, 0, -0.5f), new Vector3(4.8f, 3.4f, 9f), a);
            k.Nose(new Vector3(0, 0.3f, 5.2f), new Vector3(4.2f, 2.6f, 2.6f), Quaternion.identity, a, -0.2f);
            k.Box(new Vector3(0, 1.1f, 4.9f), new Vector3(2.6f, 0.7f, 1.2f), G(D(glow, 0.6f)));
            k.Box(new Vector3(0, 1.85f, -0.5f), new Vector3(3.6f, 0.3f, 7.6f), b);
            k.Wedge(new Vector3(0, -1.2f, -5.3f), new Vector3(4.2f, 1f, 1.8f), Quaternion.Euler(0, 180, 0), dk);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Box(new Vector3(s * 2.45f, -0.2f, 0.5f), new Vector3(0.1f, 2f, 2.4f), dk);
                k.Box(new Vector3(s * 2.5f, 0.9f, 0.5f), new Vector3(0.05f, 0.2f, 2.2f), G(f.accent));
                k.Box(new Vector3(s * 3.3f, -0.6f, -1.5f), new Vector3(1.6f, 0.5f, 4f), b);
                k.Cyl(new Vector3(s * 3.3f, -0.6f, -3.6f), 0.7f, 1.2f, Quaternion.Euler(90, 0, 0), dk, 8);
                k.Cyl(new Vector3(s * 3.3f, -0.6f, -4.25f), 0.6f, 0.12f, Quaternion.Euler(90, 0, 0), G(eng), 8);
            }
            m.body = k.Build("dropship_" + f.id);
            m.muzzles = new[] { new Vector3(-1.6f, -1.2f, 5f), new Vector3(1.6f, -1.2f, 5f) };
            m.engines = new[] { new Vector3(-3.3f, -0.6f, -4.3f), new Vector3(3.3f, -0.6f, -4.3f) };
            m.camDist = 22; m.camHeight = 7f;
        }

        static void Support(VModel m, FactionDef f)
        {
            var k = new MeshKit();
            Color a = f.main, b = f.second, dk = D(f.main, 0.5f), glow = f.bolt, eng = new Color(1f, 0.6f, 0.3f);
            k.Box(Vector3.zero, new Vector3(3.6f, 2.6f, 8f), a);
            k.Nose(new Vector3(0, 0.2f, 5.2f), new Vector3(3.2f, 2.2f, 2.4f), Quaternion.identity, a);
            k.Box(new Vector3(0, 0.9f, 4.4f), new Vector3(2f, 0.6f, 1.2f), G(D(glow, 0.6f)));
            k.Box(new Vector3(0, -1.5f, -0.5f), new Vector3(2.8f, 0.6f, 5f), b);
            k.Cyl(new Vector3(0, 1.6f, -1.5f), 0.8f, 0.4f, Quaternion.identity, dk, 10, 1.2f);
            k.Box(new Vector3(0, 2.0f, -1.5f), new Vector3(0.3f, 0.3f, 0.3f), G(new Color(0.4f, 1f, 0.5f)));
            Wing(k, new Vector3(1.8f, 0, -2), 3f, 3f, 0.8f, 0.3f, 0.1f, b, true);
            Wing(k, new Vector3(-1.8f, 0, -2), 3f, 3f, 0.8f, 0.3f, 0.1f, b, false);
            k.Cyl(new Vector3(-1.2f, 0, -4.05f), 0.7f, 0.12f, Quaternion.Euler(90, 0, 0), G(eng), 8);
            k.Cyl(new Vector3(1.2f, 0, -4.05f), 0.7f, 0.12f, Quaternion.Euler(90, 0, 0), G(eng), 8);
            m.body = k.Build("support_" + f.id);
            m.muzzles = new[] { new Vector3(-1.7f, -0.6f, 3.5f), new Vector3(1.7f, -0.6f, 3.5f) };
            m.engines = new[] { new Vector3(-1.2f, 0, -4.2f), new Vector3(1.2f, 0, -4.2f) };
            m.camDist = 20; m.camHeight = 6f;
        }
    }
}
