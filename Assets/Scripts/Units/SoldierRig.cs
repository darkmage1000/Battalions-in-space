using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    /// <summary>
    /// Part-based soldier model (hips, torso, head, arms, legs, gun) built from MeshKit shapes per faction
    /// body style, class and hero, animated procedurally. Meshes are cached by key.
    /// </summary>
    public class SoldierRig : MonoBehaviour
    {
        public Transform hips, torso, head, armL, armR, legL, legR, gun, muzzle;
        Renderer[] rends;
        float phase, deadT = -1, swing;
        Vector3 deadAxis;
        public float height = 1.8f;
        public bool cloaked;
        static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();
        FactionDef f;
        Color main, second, accent, skin, glow;
        Body body;
        string key;

        static Mesh Cached(string k, System.Action<MeshKit> build)
        {
            if (cache.TryGetValue(k, out var m) && m) return m;
            var kit = new MeshKit();
            build(kit);
            m = kit.Build(k);
            cache[k] = m;
            return m;
        }

        Transform Part(string name, Transform parent, Vector3 local, Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            if (mesh)
            {
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = Mats.Lit;
            }
            return go.transform;
        }

        public static SoldierRig Build(Transform parent, FactionDef f, ClassDef c, HeroDef h, WeaponDef weapon)
        {
            var go = new GameObject("rig");
            go.transform.SetParent(parent, false);
            var r = go.AddComponent<SoldierRig>();
            r.Make(f, c, h, weapon);
            return r;
        }

        void Make(FactionDef fac, ClassDef c, HeroDef h, WeaponDef weapon)
        {
            f = fac; body = f.body;
            main = h != null ? h.main : f.main;
            second = h != null ? h.accent : f.second;
            accent = h != null ? h.glow : f.accent;
            skin = f.skin;
            glow = h != null ? h.glow : f.bolt;
            int role = c != null ? (int)c.role : 10 + (int)h.kind;
            key = $"{f.id}_{role}_{(h != null ? h.name : c.name)}";
            float s = h != null ? h.scale : 1f;
            transform.localScale = Vector3.one * s;

            float hipY = body == Body.Slender ? 1.05f : body == Body.Machine ? 0.98f : 0.92f;
            float legLen = hipY;
            height = (body == Body.Slender ? 2.05f : 1.8f) * s;

            hips = Part("hips", transform, new Vector3(0, hipY, 0), Cached(key + "_hips", k => BuildHips(k, c, h)));
            torso = Part("torso", hips, Vector3.zero, Cached(key + "_torso", k => BuildTorso(k, c, h)));
            float neck = body == Body.Slender ? 0.68f : body == Body.Machine ? 0.6f : 0.62f;
            head = Part("head", torso, new Vector3(0, neck, body == Body.Reptile ? 0.08f : 0), Cached(key + "_head", k => BuildHead(k, c, h)));
            float sh = body == Body.Machine ? 0.22f : body == Body.Reptile ? 0.3f : 0.26f;
            float shY = neck - 0.08f;
            var armMesh = Cached(key + "_arm", k => BuildArm(k, c, h));
            armL = Part("armL", torso, new Vector3(-sh, shY, 0), armMesh);
            armR = Part("armR", torso, new Vector3(sh, shY, 0), armMesh);
            var legMesh = Cached(key + "_leg_" + legLen.ToString("0.00"), k => BuildLeg(k, legLen));
            legL = Part("legL", hips, new Vector3(-0.12f, 0, 0), legMesh);
            legR = Part("legR", hips, new Vector3(0.12f, 0, 0), legMesh);
            gun = Part("gun", torso, new Vector3(sh * 0.55f, shY - 0.18f, 0.32f), null);
            SetWeapon(weapon);
            rends = GetComponentsInChildren<Renderer>();
        }

        public void SetWeapon(WeaponDef w)
        {
            if (gun == null) return;
            var mf = gun.GetComponent<MeshFilter>();
            if (!mf) { mf = gun.gameObject.AddComponent<MeshFilter>(); gun.gameObject.AddComponent<MeshRenderer>().sharedMaterial = Mats.Lit; }
            if (w == null) { mf.sharedMesh = null; return; }
            string k = $"gun_{w.kind}_{f.id}_{(int)(glow.r * 9)}{(int)(glow.g * 9)}{(int)(glow.b * 9)}_{w.name}";
            mf.sharedMesh = Cached(k, kit => BuildGun(kit, w));
            if (!muzzle) { muzzle = new GameObject("muzzle").transform; muzzle.SetParent(gun, false); }
            muzzle.localPosition = new Vector3(0, 0.03f, MuzzleZ(w));
            gun.localPosition = new Vector3(w.kind == WKind.Rocket ? 0.16f : gun.localPosition.x, gun.localPosition.y, w.kind == WKind.Blade ? 0.38f : 0.32f);
            rends = GetComponentsInChildren<Renderer>();
        }

        static float MuzzleZ(WeaponDef w)
        {
            switch (w.kind)
            {
                case WKind.Sniper: return 1.15f;
                case WKind.Rocket: return 0.75f;
                case WKind.Pistol: return 0.3f;
                case WKind.Shotgun: return 0.62f;
                case WKind.Blade: return 1.3f;
                case WKind.Beam: return 0.25f;
                case WKind.Repair: return 0.45f;
                default: return 0.72f;
            }
        }

        // ── part builders ────────────────────────────────────────────────
        Color Dark(Color c, float k = 0.55f) { var r = c * k; r.a = 1; return r; }
        Color G(Color c) { c.a = 0; return c; }

        void BuildHips(MeshKit k, ClassDef c, HeroDef h)
        {
            switch (body)
            {
                case Body.Reptile:
                    k.Box(new Vector3(0, -0.02f, 0), new Vector3(0.42f, 0.26f, 0.3f), Dark(main, 0.8f));
                    // tail
                    for (int i = 0; i < 4; i++)
                    {
                        float t = i / 4f;
                        k.Box(new Vector3(0, -0.06f - t * 0.32f, -0.22f - i * 0.22f), new Vector3(0.22f - t * 0.14f, 0.18f - t * 0.1f, 0.26f), Quaternion.Euler(-18 - i * 6, 0, 0), skin);
                    }
                    break;
                case Body.Machine:
                    k.Box(new Vector3(0, 0, 0), new Vector3(0.32f, 0.16f, 0.2f), Dark(main, 0.7f));
                    k.Cyl(new Vector3(0, 0.12f, 0), 0.05f, 0.25f, Quaternion.identity, Dark(main, 0.5f), 6);
                    break;
                case Body.Slender:
                    k.Taper(new Vector3(0, -0.12f, 0), new Vector2(0.44f, 0.28f), new Vector2(0.32f, 0.22f), 0.36f, Quaternion.identity, second);
                    break;
                default:
                    k.Box(new Vector3(0, 0, 0), new Vector3(0.4f, 0.2f, 0.26f), Dark(main, 0.75f));
                    k.Box(new Vector3(0, 0.06f, 0), new Vector3(0.42f, 0.06f, 0.28f), Dark(second, 0.6f));
                    break;
            }
        }

        void BuildTorso(MeshKit k, ClassDef c, HeroDef h)
        {
            Role role = c != null ? c.role : Role.Assault;
            bool heavy = (c != null && c.role == Role.Heavy) || (h != null && h.kind == HeroKind.Juggernaut);
            switch (body)
            {
                case Body.Reptile:
                    k.Taper(new Vector3(0, 0.3f, 0.04f), new Vector2(0.44f, 0.3f), new Vector2(0.6f, 0.34f), 0.6f, Quaternion.Euler(12, 0, 0), main);
                    k.Box(new Vector3(0, 0.36f, 0.2f), new Vector3(0.4f, 0.4f, 0.06f), Quaternion.Euler(12, 0, 0), accent);
                    for (int i = 0; i < 3; i++) k.Cone(new Vector3(0, 0.25f + i * 0.15f, -0.18f), 0.06f, 0.18f, Quaternion.Euler(-70, 0, 0), accent, 5);
                    break;
                case Body.Machine:
                    k.Box(new Vector3(0, 0.38f, 0), new Vector3(0.36f, 0.34f, 0.22f), main);
                    k.Cyl(new Vector3(0, 0.12f, 0), 0.06f, 0.24f, Quaternion.identity, Dark(main, 0.5f), 6);
                    k.Box(new Vector3(0, 0.4f, 0.115f), new Vector3(0.18f, 0.06f, 0.02f), G(second));
                    k.Box(new Vector3(0, 0.38f, -0.14f), new Vector3(0.22f, 0.26f, 0.08f), Dark(main, 0.6f));
                    break;
                case Body.Slender:
                    k.Taper(new Vector3(0, 0.33f, 0), new Vector2(0.3f, 0.2f), new Vector2(0.42f, 0.24f), 0.62f, Quaternion.identity, main);
                    k.Box(new Vector3(0, 0.36f, 0.115f), new Vector3(0.1f, 0.5f, 0.02f), second);
                    k.Box(new Vector3(0, 0.55f, 0.12f), new Vector3(0.08f, 0.08f, 0.02f), G(glow));
                    k.Box(new Vector3(-0.1f, 0.3f, 0.02f), new Vector3(0.06f, 0.6f, 0.26f), Quaternion.Euler(0, 0, 30), second);
                    break;
                default:
                    k.Taper(new Vector3(0, 0.3f, 0), new Vector2(0.4f, 0.24f), new Vector2(0.5f, 0.28f), 0.6f, Quaternion.identity, Dark(main, 0.85f));
                    k.Box(new Vector3(0, 0.38f, 0.13f), new Vector3(0.42f, 0.34f, 0.06f), main);
                    k.Box(new Vector3(0, 0.36f, 0.165f), new Vector3(0.12f, 0.08f, 0.02f), second);
                    k.Box(new Vector3(0, 0.36f, -0.15f), new Vector3(0.36f, 0.4f, 0.08f), main);
                    break;
            }
            if (heavy)
            {
                k.Box(new Vector3(-0.3f, 0.56f, 0), new Vector3(0.22f, 0.12f, 0.3f), Quaternion.Euler(0, 0, 18), second);
                k.Box(new Vector3(0.3f, 0.56f, 0), new Vector3(0.22f, 0.12f, 0.3f), Quaternion.Euler(0, 0, -18), second);
            }
            if (c != null)
            {
                switch (role)
                {
                    case Role.Engineer:
                        k.Box(new Vector3(0, 0.35f, -0.25f), new Vector3(0.34f, 0.4f, 0.18f), Dark(second, 0.7f));
                        k.Cyl(new Vector3(0.12f, 0.62f, -0.25f), 0.03f, 0.3f, Quaternion.identity, Dark(main, 0.4f), 5);
                        k.Box(new Vector3(-0.1f, 0.5f, -0.33f), new Vector3(0.1f, 0.08f, 0.02f), G(accent));
                        break;
                    case Role.Marksman:
                        k.Box(new Vector3(0, 0.25f, -0.2f), new Vector3(0.46f, 0.7f, 0.04f), Quaternion.Euler(-8, 0, 0), Dark(second, 0.6f));
                        break;
                    case Role.Specialist:
                        if (c.special == Special.Jetpack)
                        {
                            k.Box(new Vector3(0, 0.36f, -0.24f), new Vector3(0.36f, 0.42f, 0.16f), Dark(main, 0.6f));
                            k.Cyl(new Vector3(-0.12f, 0.12f, -0.27f), 0.07f, 0.2f, Quaternion.identity, Dark(main, 0.4f), 6);
                            k.Cyl(new Vector3(0.12f, 0.12f, -0.27f), 0.07f, 0.2f, Quaternion.identity, Dark(main, 0.4f), 6);
                            k.Cyl(new Vector3(-0.12f, 0.01f, -0.27f), 0.05f, 0.03f, Quaternion.identity, G(new Color(1, 0.6f, 0.2f)), 6);
                            k.Cyl(new Vector3(0.12f, 0.01f, -0.27f), 0.05f, 0.03f, Quaternion.identity, G(new Color(1, 0.6f, 0.2f)), 6);
                        }
                        else if (c.special == Special.Frenzy)
                            for (int i = 0; i < 4; i++) k.Cone(new Vector3(-0.2f + i * 0.13f, 0.62f, -0.12f), 0.05f, 0.28f, Quaternion.Euler(-30, 0, 0), accent, 5);
                        else if (c.special == Special.Cloak)
                        {
                            k.Cyl(new Vector3(0.14f, 0.75f, -0.12f), 0.015f, 0.5f, Quaternion.identity, Dark(main, 0.4f), 4);
                            k.Box(new Vector3(0.14f, 1.0f, -0.12f), new Vector3(0.05f, 0.05f, 0.05f), G(second));
                        }
                        else
                            k.Box(new Vector3(0.08f, 0.3f, 0.14f), new Vector3(0.1f, 0.62f, 0.03f), Quaternion.Euler(0, 0, -35), G(glow));
                        break;
                }
            }
            if (h != null)
            {
                // cape for every hero, coloured accent
                k.Taper(new Vector3(0, 0.12f, -0.19f), new Vector2(0.44f, 0.03f), new Vector2(0.36f, 0.03f), 0.8f, Quaternion.Euler(-10, 0, 0), h.accent);
                k.Box(new Vector3(0, 0.6f, 0.06f), new Vector3(0.5f, 0.08f, 0.32f), Dark(h.accent, 0.7f));
            }
        }

        void BuildHead(MeshKit k, ClassDef c, HeroDef h)
        {
            switch (body)
            {
                case Body.Reptile:
                    k.Box(new Vector3(0, 0.14f, 0.02f), new Vector3(0.26f, 0.24f, 0.28f), skin);
                    k.Taper(new Vector3(0, 0.1f, 0.22f), new Vector2(0.2f, 0.16f), new Vector2(0.14f, 0.1f), 0.24f, Quaternion.Euler(90, 0, 0), skin);
                    k.Box(new Vector3(-0.09f, 0.2f, 0.14f), new Vector3(0.05f, 0.04f, 0.04f), G(new Color(1f, 0.85f, 0.1f)));
                    k.Box(new Vector3(0.09f, 0.2f, 0.14f), new Vector3(0.05f, 0.04f, 0.04f), G(new Color(1f, 0.85f, 0.1f)));
                    k.Wedge(new Vector3(0, 0.3f, -0.06f), new Vector3(0.06f, 0.18f, 0.3f), Quaternion.identity, accent);
                    k.Box(new Vector3(0, 0.25f, 0), new Vector3(0.3f, 0.07f, 0.3f), main);
                    break;
                case Body.Machine:
                    k.Box(new Vector3(0, 0.12f, 0), new Vector3(0.2f, 0.2f, 0.24f), main);
                    k.Box(new Vector3(0, 0.14f, 0.125f), new Vector3(0.16f, 0.04f, 0.01f), G(second));
                    k.Cyl(new Vector3(0.11f, 0.12f, 0), 0.04f, 0.06f, Quaternion.Euler(0, 0, 90), Dark(main, 0.5f), 6);
                    k.Cyl(new Vector3(-0.11f, 0.12f, 0), 0.04f, 0.06f, Quaternion.Euler(0, 0, 90), Dark(main, 0.5f), 6);
                    k.Cyl(new Vector3(0.06f, 0.3f, -0.05f), 0.01f, 0.18f, Quaternion.identity, Dark(main, 0.5f), 4);
                    break;
                case Body.Slender:
                    k.Taper(new Vector3(0, 0.16f, 0), new Vector2(0.16f, 0.2f), new Vector2(0.12f, 0.16f), 0.32f, Quaternion.identity, skin);
                    k.Wedge(new Vector3(0, 0.3f, -0.12f), new Vector3(0.05f, 0.14f, 0.4f), Quaternion.Euler(-20, 0, 0), second);
                    k.Box(new Vector3(-0.045f, 0.2f, 0.1f), new Vector3(0.05f, 0.025f, 0.02f), G(glow));
                    k.Box(new Vector3(0.045f, 0.2f, 0.1f), new Vector3(0.05f, 0.025f, 0.02f), G(glow));
                    break;
                default:
                    k.Sphere(new Vector3(0, 0.13f, 0), new Vector3(0.22f, 0.26f, 0.24f), Quaternion.identity, skin, 7, 5);
                    if (h == null || h.kind != HeroKind.Gunslinger)
                    {
                        k.Box(new Vector3(0, 0.2f, -0.01f), new Vector3(0.28f, 0.2f, 0.29f), main);
                        k.Box(new Vector3(0, 0.15f, 0.13f), new Vector3(0.22f, 0.06f, 0.04f), G(Dark(second, 0.9f)));
                        k.Box(new Vector3(0, 0.31f, 0), new Vector3(0.06f, 0.03f, 0.27f), second);
                    }
                    else
                    {
                        k.Box(new Vector3(0, 0.25f, 0), new Vector3(0.24f, 0.08f, 0.26f), Dark(main, 0.6f));
                        k.Box(new Vector3(0, 0.29f, 0), new Vector3(0.38f, 0.02f, 0.38f), Dark(main, 0.6f));
                    }
                    break;
            }
            if (h != null && h.kind == HeroKind.Mystic)
                k.Box(new Vector3(0, 0.36f, 0), new Vector3(0.36f, 0.04f, 0.04f), G(h.glow));
        }

        void BuildArm(MeshKit k, ClassDef c, HeroDef h)
        {
            float w = body == Body.Machine ? 0.08f : body == Body.Reptile ? 0.15f : 0.12f;
            if (body == Body.Machine)
            {
                k.Cyl(new Vector3(0, -0.28f, 0), w * 0.5f, 0.56f, Quaternion.identity, Dark(main, 0.7f), 6);
                k.Sphere(Vector3.zero, Vector3.one * 0.11f, Quaternion.identity, main, 6, 4);
                k.Box(new Vector3(0, -0.58f, 0), new Vector3(0.08f, 0.08f, 0.08f), Dark(main, 0.5f));
            }
            else
            {
                k.Box(new Vector3(0, -0.15f, 0), new Vector3(w + 0.03f, 0.3f, w + 0.03f), main);
                k.Box(new Vector3(0, -0.43f, 0), new Vector3(w, 0.28f, w), body == Body.Reptile ? skin : Dark(main, 0.75f));
                k.Box(new Vector3(0, -0.6f, 0), new Vector3(w * 0.9f, 0.08f, w), body == Body.Human ? Dark(second, 0.5f) : skin);
            }
        }

        void BuildLeg(MeshKit k, float len)
        {
            float w = body == Body.Machine ? 0.09f : body == Body.Reptile ? 0.17f : 0.15f;
            if (body == Body.Machine)
            {
                k.Cyl(new Vector3(0, -len * 0.5f, 0), w * 0.5f, len, Quaternion.identity, Dark(main, 0.7f), 6);
                k.Sphere(new Vector3(0, -len * 0.5f, 0), Vector3.one * 0.1f, Quaternion.identity, main, 6, 4);
                k.Box(new Vector3(0, -len + 0.03f, 0.05f), new Vector3(0.12f, 0.06f, 0.22f), Dark(main, 0.5f));
            }
            else if (body == Body.Reptile)
            {
                k.Box(new Vector3(0, -len * 0.25f, 0.04f), new Vector3(w, len * 0.5f, w + 0.03f), Quaternion.Euler(-14, 0, 0), main);
                k.Box(new Vector3(0, -len * 0.72f, 0.02f), new Vector3(w * 0.8f, len * 0.5f, w * 0.8f), Quaternion.Euler(16, 0, 0), skin);
                k.Box(new Vector3(0, -len + 0.03f, 0.08f), new Vector3(0.16f, 0.06f, 0.26f), skin);
            }
            else
            {
                k.Box(new Vector3(0, -len * 0.27f, 0), new Vector3(w, len * 0.54f, w + 0.02f), body == Body.Slender ? second : Dark(main, 0.85f));
                k.Box(new Vector3(0, -len * 0.73f, 0), new Vector3(w * 0.9f, len * 0.46f, w), body == Body.Slender ? Dark(second, 0.7f) : main);
                k.Box(new Vector3(0, -len + 0.04f, 0.04f), new Vector3(w + 0.02f, 0.08f, 0.26f), Dark(second, 0.4f));
            }
        }

        float BuildGun(MeshKit k, WeaponDef w)
        {
            Color metal = new Color(0.22f, 0.22f, 0.25f), trim = Dark(second, 0.8f);
            switch (w.kind)
            {
                case WKind.Sniper:
                    k.Box(new Vector3(0, 0, 0.35f), new Vector3(0.06f, 0.1f, 0.7f), metal);
                    k.Cyl(new Vector3(0, 0.02f, 0.9f), 0.02f, 0.5f, Quaternion.Euler(90, 0, 0), metal, 6);
                    k.Cyl(new Vector3(0, 0.1f, 0.35f), 0.035f, 0.3f, Quaternion.Euler(90, 0, 0), trim, 6);
                    k.Box(new Vector3(0, 0.1f, 0.2f), new Vector3(0.03f, 0.03f, 0.02f), G(glow));
                    k.Box(new Vector3(0, -0.08f, -0.05f), new Vector3(0.05f, 0.12f, 0.2f), trim);
                    return 1.15f;
                case WKind.Rocket:
                    k.Cyl(new Vector3(0, 0.08f, 0.2f), 0.09f, 1.1f, Quaternion.Euler(90, 0, 0), trim, 8);
                    k.Cyl(new Vector3(0, 0.08f, 0.76f), 0.1f, 0.06f, Quaternion.Euler(90, 0, 0), metal, 8);
                    k.Box(new Vector3(0, -0.05f, 0.1f), new Vector3(0.05f, 0.14f, 0.08f), metal);
                    k.Box(new Vector3(-0.1f, 0.14f, 0.3f), new Vector3(0.06f, 0.06f, 0.1f), G(glow));
                    return 0.75f;
                case WKind.Pistol:
                    k.Box(new Vector3(0, 0, 0.12f), new Vector3(0.05f, 0.08f, 0.28f), metal);
                    k.Box(new Vector3(0, -0.07f, 0.02f), new Vector3(0.04f, 0.1f, 0.06f), trim);
                    k.Box(new Vector3(0, 0.045f, 0.2f), new Vector3(0.02f, 0.01f, 0.08f), G(glow));
                    return 0.3f;
                case WKind.Shotgun:
                    k.Box(new Vector3(0, 0, 0.3f), new Vector3(0.1f, 0.1f, 0.6f), metal);
                    k.Box(new Vector3(0, 0.06f, 0.35f), new Vector3(0.12f, 0.04f, 0.5f), trim);
                    k.Box(new Vector3(0, -0.07f, 0.05f), new Vector3(0.05f, 0.12f, 0.08f), trim);
                    return 0.62f;
                case WKind.Repeater:
                    k.Box(new Vector3(0, 0, 0.3f), new Vector3(0.08f, 0.1f, 0.62f), metal);
                    k.Cyl(new Vector3(0, -0.08f, 0.25f), 0.06f, 0.08f, Quaternion.Euler(0, 0, 90), trim, 8);
                    k.Box(new Vector3(0, 0.07f, 0.4f), new Vector3(0.03f, 0.03f, 0.3f), G(glow));
                    if (w.splash > 0) k.Cyl(new Vector3(0, 0, 0.66f), 0.05f, 0.1f, Quaternion.Euler(90, 0, 0), trim, 6);
                    return 0.72f;
                case WKind.Repair:
                    k.Box(new Vector3(0, 0, 0.2f), new Vector3(0.1f, 0.1f, 0.4f), new Color(0.6f, 0.55f, 0.2f));
                    k.Cone(new Vector3(0, 0, 0.43f), 0.05f, 0.08f, Quaternion.Euler(90, 0, 0), metal, 6);
                    k.Box(new Vector3(0, 0.07f, 0.15f), new Vector3(0.04f, 0.04f, 0.1f), G(new Color(0.4f, 1f, 0.5f)));
                    return 0.45f;
                case WKind.Blade:
                    k.Cyl(new Vector3(0, 0, 0.0f), 0.025f, 0.24f, Quaternion.Euler(90, 0, 0), metal, 6);
                    k.Box(new Vector3(0, 0, 0.13f), new Vector3(0.08f, 0.02f, 0.03f), trim);
                    k.Box(new Vector3(0, 0, 0.72f), new Vector3(0.04f, 0.06f, 1.1f), G(glow));
                    return 1.3f;
                case WKind.Beam:
                    k.Box(new Vector3(0, 0, 0.05f), new Vector3(0.14f, 0.12f, 0.2f), trim);
                    k.Sphere(new Vector3(0, 0, 0.2f), Vector3.one * 0.12f, Quaternion.identity, G(glow), 6, 4);
                    return 0.25f;
                default:
                    bool heavyGun = w.splash > 0 && w.kind == WKind.Repeater;
                    k.Box(new Vector3(0, 0, 0.28f), new Vector3(0.07f, 0.11f, 0.56f), metal);
                    k.Cyl(new Vector3(0, 0.01f, 0.62f), 0.025f, 0.2f, Quaternion.Euler(90, 0, 0), metal, 6);
                    k.Box(new Vector3(0, -0.08f, 0.32f), new Vector3(0.04f, 0.1f, 0.06f), trim);
                    k.Box(new Vector3(0, -0.06f, 0.05f), new Vector3(0.05f, 0.12f, 0.12f), trim);
                    k.Box(new Vector3(0, 0.065f, 0.3f), new Vector3(0.025f, 0.02f, 0.22f), G(glow));
                    if (heavyGun)
                    {
                        k.Cyl(new Vector3(0, 0, 0.45f), 0.1f, 0.7f, Quaternion.Euler(90, 0, 0), metal, 8);
                        k.Box(new Vector3(0, -0.15f, 0.2f), new Vector3(0.18f, 0.16f, 0.2f), trim);
                    }
                    return 0.72f;
            }
        }

        // ── animation ────────────────────────────────────────────────────
        public void Animate(float speed, bool grounded, bool crouch, float pitch, float dt, bool swingBlade)
        {
            if (deadT >= 0)
            {
                deadT += dt;
                float t = Mathf.Clamp01(deadT / 0.6f);
                transform.localRotation = Quaternion.AngleAxis(t * t * 88f, deadAxis);
                transform.localPosition = new Vector3(0, -t * 0.15f, 0);
                return;
            }
            float sp = Mathf.Clamp01(speed / 7f);
            phase += dt * (3.5f + speed * 1.15f);
            float sw = Mathf.Sin(phase) * 38f * sp;
            float bob = Mathf.Abs(Mathf.Sin(phase)) * 0.05f * sp;
            float hipY = (body == Body.Slender ? 1.05f : body == Body.Machine ? 0.98f : 0.92f);
            if (!grounded)
            {
                legL.localRotation = Quaternion.Euler(-30, 0, 0);
                legR.localRotation = Quaternion.Euler(20, 0, 0);
                hips.localPosition = new Vector3(0, hipY, 0);
            }
            else if (crouch)
            {
                legL.localRotation = Quaternion.Euler(-70 + sw * 0.3f, 0, 0);
                legR.localRotation = Quaternion.Euler(-10 - sw * 0.3f, 0, 0);
                hips.localPosition = new Vector3(0, hipY * 0.62f, 0);
            }
            else
            {
                legL.localRotation = Quaternion.Euler(sw, 0, 0);
                legR.localRotation = Quaternion.Euler(-sw, 0, 0);
                hips.localPosition = new Vector3(0, hipY + bob, 0);
            }
            float lean = crouch ? 18 : sp * 6;
            torso.localRotation = Quaternion.Euler(lean + pitch * 0.25f, 0, 0);
            head.localRotation = Quaternion.Euler(pitch * 0.5f, 0, 0);
            if (swingBlade) swing = 1;
            swing = Mathf.MoveTowards(swing, 0, dt * 3f);
            float aimP = pitch * 0.75f;
            // arms hold the weapon forward along the aim
            armR.localRotation = Quaternion.Euler(-80 + aimP - swing * 70, 0, swing * 40);
            armL.localRotation = Quaternion.Euler(-70 + aimP, 30, 0);
            gun.localRotation = Quaternion.Euler(aimP - swing * 60, -swing * 50, 0);
        }

        public void Die(Vector3 dir)
        {
            if (deadT >= 0) return;
            deadT = 0;
            Vector3 local = transform.parent ? transform.parent.InverseTransformDirection(dir) : dir;
            local.y = 0;
            if (local.sqrMagnitude < 0.01f) local = Vector3.back;
            deadAxis = Vector3.Cross(Vector3.up, local.normalized);
            if (deadAxis.sqrMagnitude < 0.01f) deadAxis = Vector3.right;
        }

        public void SetCloak(bool on)
        {
            if (cloaked == on) return;
            cloaked = on;
            if (rends == null) rends = GetComponentsInChildren<Renderer>();
            foreach (var r in rends)
                if (r) r.sharedMaterial = on ? Mats.Alpha(new Color(0.6f, 0.8f, 1f, 0.12f)) : Mats.Lit;
        }

        public void SetVisible(bool v)
        {
            if (rends == null) rends = GetComponentsInChildren<Renderer>();
            foreach (var r in rends) if (r) r.enabled = v;
        }
    }
}
