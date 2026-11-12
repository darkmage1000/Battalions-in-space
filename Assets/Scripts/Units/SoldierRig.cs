using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    /// <summary>
    /// Soldier model. On Medium / High graphics: the Quaternius Universal Animation Library mannequin (CC0) in the
    /// faction's colours, animated by Resources/Models/Characters/Soldier.controller (built by
    /// Battalions > Build Soldier Animator), with the faction helmet on its head bone, the code-built gun in its
    /// right hand and the chest bent toward the aim. On Low (or if the files are missing): the original part-based
    /// model built from MeshKit shapes and animated procedurally. Meshes are cached by key.
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
        // animated mannequin
        Animator anim;
        Transform chest, handR, bodyRoot;
        float pitchNow;
        HeroDef heroDef;
        Quaternion chestAnim, chestSet, headAnim, headSet;
        static GameObject manPrefab;
        static RuntimeAnimatorController manCtl;
        static bool manTried;
        static readonly Dictionary<string, Mesh> manMeshes = new Dictionary<string, Mesh>();
        public bool Animated => anim != null;

        public static bool MannequinAvailable
        {
            get
            {
                if (!manTried)
                {
                    manTried = true;
                    manPrefab = Resources.Load<GameObject>("Models/Characters/UAL_Mannequin");
                    manCtl = Resources.Load<RuntimeAnimatorController>("Models/Characters/Soldier");
                }
                return manPrefab && manCtl;
            }
        }
        public static bool UseMannequin => Settings.Quality > 0 && MannequinAvailable;
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
            heroDef = h;
            if (UseMannequin && MakeMannequin(c, h, weapon)) return;

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

        /// <summary>The faction-coloured copy of the mannequin mesh: body in the main colour, joints in the second.</summary>
        static Mesh ColouredMannequin(SkinnedMeshRenderer smr, Color body, Color joints)
        {
            string k = ColorUtility.ToHtmlStringRGB(body) + ColorUtility.ToHtmlStringRGB(joints);
            if (manMeshes.TryGetValue(k, out var m) && m) return m;
            var src = smr.sharedMesh;
            m = Object.Instantiate(src);
            m.name = "mannequin_" + k;
            var mats = smr.sharedMaterials;
            // the joint material is the purple one (more blue than red); everything else is the body
            int jointSub = -1; float best = -9;
            for (int i = 0; i < mats.Length && i < src.subMeshCount; i++)
                if (mats[i] && mats[i].HasProperty("_Color")) { var c = mats[i].color; if (c.b - c.r > best) { best = c.b - c.r; jointSub = i; } }
            var cols = new Color[src.vertexCount];
            for (int i = 0; i < cols.Length; i++) cols[i] = body;
            if (jointSub >= 0 && src.subMeshCount > 1)
                foreach (int t in src.GetTriangles(jointSub)) cols[t] = joints;
            m.colors = cols;
            manMeshes[k] = m;
            return m;
        }

        bool MakeMannequin(ClassDef c, HeroDef h, WeaponDef weapon)
        {
            var inst = Object.Instantiate(manPrefab, transform);
            inst.name = "body";
            bodyRoot = inst.transform;
            anim = inst.GetComponent<Animator>();
            var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>();
            if (!anim || !smr) { Object.Destroy(inst); anim = null; return false; }
            float k = height / transform.localScale.y / 1.94f;
            // uniform scale only: a squashed humanoid skeleton bends its arms wrongly
            Vector3 sc = Vector3.one * k;
            bodyRoot.localPosition = Vector3.zero; bodyRoot.localRotation = Quaternion.identity; bodyRoot.localScale = sc;
            anim.runtimeAnimatorController = manCtl;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            // the mannequin is the undersuit (dark, or the skin for scaled / alien bodies); the armour pieces carry the look
            Color bodyCol = body == Body.Reptile || body == Body.Slender ? Color.Lerp(skin, Dark(main, 0.6f), 0.35f) : body == Body.Machine ? Dark(main, 0.55f) : Dark(Color.Lerp(main, second, 0.5f), 0.42f);
            bodyCol.a = 1;
            Color jointCol = Dark(second, 0.35f);
            smr.sharedMesh = ColouredMannequin(smr, bodyCol, jointCol);
            var ms = new Material[smr.sharedMesh.subMeshCount]; for (int i = 0; i < ms.Length; i++) ms[i] = Mats.Lit;
            smr.sharedMaterials = ms;
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            chest = anim.GetBoneTransform(HumanBodyBones.Chest) ?? anim.GetBoneTransform(HumanBodyBones.Spine);
            torso = chest;
            head = anim.GetBoneTransform(HumanBodyBones.Head);
            handR = anim.GetBoneTransform(HumanBodyBones.RightHand);
            armL = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm); armR = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            legL = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg); legR = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            if (!chest || !head || !handR) { Object.Destroy(inst); anim = null; return false; }
            // the faction helmet / head piece, upright in the bind pose and then carried by the head bone
            var hm = Cached(key + "_head", kk => BuildHead(kk, c, h));
            var helmet = new GameObject("helmet").transform;
            helmet.SetParent(head, false);
            helmet.rotation = transform.rotation;
            helmet.position = head.position - transform.up * 0.13f * k;
            helmet.localScale = Vector3.one * (k * 1.12f / Mathf.Max(0.0001f, head.lossyScale.x)) * transform.localScale.x;
            helmet.gameObject.AddComponent<MeshFilter>().sharedMesh = hm;
            helmet.gameObject.AddComponent<MeshRenderer>().sharedMaterial = Mats.Lit;
            AttachArmour(c, h);
            gun = Part("gun", transform, Vector3.zero, null);
            SetWeapon(weapon);
            anim.Play(h != null && h.kind == HeroKind.Blade ? "Sword" : h != null && h.kind == HeroKind.Mystic ? "Spell" : "Rifle", 1, Random.value);
            anim.Play("Move", 0, Random.value);
            rends = GetComponentsInChildren<Renderer>();
            return true;
        }

        /// <summary>
        /// The faction and class look on top of the animated body: the code-built torso (armour, packs, cloaks,
        /// spikes, hero capes), hips (the reptile tail) and limb armour in two pieces per limb so elbows and knees
        /// still bend. Each piece is placed in the bind pose and then carried by its bone.
        /// </summary>
        void AttachArmour(ClassDef c, HeroDef h)
        {
            Transform B(HumanBodyBones hb) => anim.GetBoneTransform(hb);
            float ws = transform.lossyScale.x;
            void Put(Transform bone, Mesh m, Vector3 at, Vector3 down, string n)
            {
                if (!bone || !m) return;
                var t = new GameObject(n).transform;
                t.SetParent(bone, false);
                t.position = at;
                t.rotation = down.sqrMagnitude > 0.001f ? Quaternion.FromToRotation(-transform.up, down.normalized) * transform.rotation : transform.rotation;
                t.localScale = Vector3.one * (ws / Mathf.Max(0.0001f, bone.lossyScale.x));
                t.gameObject.AddComponent<MeshFilter>().sharedMesh = m;
                var r = t.gameObject.AddComponent<MeshRenderer>(); r.sharedMaterial = Mats.Lit;
            }
            Vector3 Dir(HumanBodyBones a, HumanBodyBones b) { var x = B(a); var y = B(b); return x && y ? y.position - x.position : -transform.up; }
            var hipsB = B(HumanBodyBones.Hips);
            Vector3 hipAt = hipsB.position;
            Put(chest, Cached(key + "_torso", kk => BuildTorso(kk, c, h)), hipAt, Vector3.zero, "torsoArmour");
            Put(hipsB, Cached(key + "_hips", kk => BuildHips(kk, c, h)), hipAt, Vector3.zero, "hipArmour");
            var up = Cached(key + "_uarm", kk => BuildArmPart(kk, c, h, true));
            var lo = Cached(key + "_larm", kk => BuildArmPart(kk, c, h, false));
            var th = Cached(key + "_thigh", kk => BuildLegPart(kk, true));
            var sh = Cached(key + "_shin", kk => BuildLegPart(kk, false));
            foreach (var side in new[] { 0, 1 })
            {
                var ua = side == 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
                var la = side == 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm;
                var hd = side == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
                var ul = side == 0 ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg;
                var ll = side == 0 ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg;
                var ft = side == 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot;
                if (B(ua)) Put(B(ua), up, B(ua).position, Dir(ua, la), "upperArm");
                if (B(la)) Put(B(la), lo, B(la).position, Dir(la, hd), "forearm");
                if (B(ul)) Put(B(ul), th, B(ul).position, Dir(ul, ll), "thigh");
                if (B(ll)) Put(B(ll), sh, B(ll).position, Dir(ll, ft), "shin");
            }
        }

        /// <summary>Upper arm (shoulder to elbow) or forearm (elbow to hand) armour, hanging along -y from the joint.</summary>
        void BuildArmPart(MeshKit k, ClassDef c, HeroDef h, bool upper)
        {
            float w = body == Body.Machine ? 0.1f : body == Body.Reptile ? 0.15f : 0.12f;
            bool heavy = (c != null && c.role == Role.Heavy) || (h != null && h.kind == HeroKind.Juggernaut);
            if (upper)
            {
                if (body == Body.Machine) { k.Sphere(Vector3.zero, Vector3.one * 0.13f, Quaternion.identity, main, 6, 4); k.Cyl(new Vector3(0, -0.14f, 0), w * 0.5f, 0.24f, Quaternion.identity, Dark(main, 0.7f), 6); }
                else k.Box(new Vector3(0, -0.13f, 0), new Vector3(w + 0.035f, 0.24f, w + 0.035f), main);
                // shoulder plate
                k.Box(new Vector3(0, 0.02f, 0), new Vector3(w + (heavy ? 0.12f : 0.06f), 0.1f, w + (heavy ? 0.1f : 0.05f)), heavy ? second : Dark(main, 0.8f));
            }
            else
            {
                Color fc = body == Body.Reptile ? skin : body == Body.Slender ? second : Dark(main, 0.75f);
                if (body == Body.Machine) k.Cyl(new Vector3(0, -0.12f, 0), w * 0.45f, 0.22f, Quaternion.identity, Dark(main, 0.6f), 6);
                else k.Box(new Vector3(0, -0.12f, 0), new Vector3(w + 0.02f, 0.2f, w + 0.02f), fc);
                k.Box(new Vector3(0, -0.2f, 0), new Vector3(w + 0.03f, 0.05f, w + 0.03f), body == Body.Human ? Dark(second, 0.6f) : accent);
            }
        }

        /// <summary>Thigh (hip to knee) or shin (knee to ankle, with the boot) armour, along -y from the joint.</summary>
        void BuildLegPart(MeshKit k, bool upper)
        {
            float w = body == Body.Machine ? 0.11f : body == Body.Reptile ? 0.17f : 0.15f;
            if (upper)
            {
                Color tc = body == Body.Slender ? second : Dark(main, 0.85f);
                if (body == Body.Machine) k.Cyl(new Vector3(0, -0.2f, 0), w * 0.5f, 0.38f, Quaternion.identity, Dark(main, 0.7f), 6);
                else k.Box(new Vector3(0, -0.21f, 0), new Vector3(w + 0.02f, 0.36f, w + 0.03f), body == Body.Reptile ? main : tc);
            }
            else
            {
                Color sc2 = body == Body.Reptile ? skin : body == Body.Slender ? Dark(second, 0.7f) : main;
                if (body == Body.Machine) { k.Sphere(Vector3.zero, Vector3.one * 0.1f, Quaternion.identity, main, 6, 4); k.Cyl(new Vector3(0, -0.2f, 0), w * 0.45f, 0.36f, Quaternion.identity, Dark(main, 0.7f), 6); }
                else k.Box(new Vector3(0, -0.19f, 0.01f), new Vector3(w, 0.34f, w + 0.02f), sc2);
                // boot / foot, toes forward
                k.Box(new Vector3(0, -0.4f, 0.05f), new Vector3(w + 0.03f, 0.09f, 0.26f), body == Body.Reptile ? skin : Dark(second, 0.4f));
            }
        }

        void LateUpdate()
        {
            if (!anim || deadT >= 0) return;
            // lean the chest (and a little of the head) toward where the soldier aims. When the animator skipped a
            // frame (off screen) the bones still hold last frame's bend: start from the animated pose, never stack.
            if (chest.localRotation == chestSet) chest.localRotation = chestAnim;
            if (head.localRotation == headSet) head.localRotation = headAnim;
            chestAnim = chest.localRotation; headAnim = head.localRotation;
            chest.rotation = Quaternion.AngleAxis(pitchNow * 0.65f, transform.right) * chest.rotation;
            head.rotation = Quaternion.AngleAxis(pitchNow * 0.3f, transform.right) * head.rotation;
            chestSet = chest.localRotation; headSet = head.localRotation;
            if (gun)
            {
                bool blade = heroDef != null && heroDef.kind == HeroKind.Blade;
                gun.position = handR.position + transform.rotation * new Vector3(0.02f, 0.02f, blade ? 0.02f : -0.06f);
                gun.rotation = transform.rotation * Quaternion.Euler(pitchNow, 0, 0) * (blade ? Quaternion.Euler(-35 - swing * 80, 0, 0) : Quaternion.identity);
            }
        }

        public void SetWeapon(WeaponDef w)
        {
            if (gun == null) return;
            var mf = gun.GetComponent<MeshFilter>();
            if (!mf) { mf = gun.gameObject.AddComponent<MeshFilter>(); gun.gameObject.AddComponent<MeshRenderer>().sharedMaterial = Mats.Lit; }
            if (w == null) { mf.sharedMesh = null; return; }
            string k = $"gun_{w.kind}_{f.id}_{(int)(glow.r * 9)}{(int)(glow.g * 9)}{(int)(glow.b * 9)}_{w.name}_{w.tier}_{w.lootFaction}";
            mf.sharedMesh = Cached(k, kit => BuildGun(kit, w));
            if (!muzzle) { muzzle = new GameObject("muzzle").transform; muzzle.SetParent(gun, false); }
            float modelMuzzle = GunModels.MuzzleZ(w, GunFaction(w), GunScale);
            muzzle.localPosition = new Vector3(0, 0.03f, modelMuzzle > 0 ? modelMuzzle : MuzzleZ(w));
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

        /// <summary>Whose gun this is: a looted gun keeps the look of the faction that made it.</summary>
        int GunFaction(WeaponDef w) => w.tier >= 0 ? w.lootFaction : f.id;
        float GunScale => heroDef != null ? Mathf.Max(1f, heroDef.scale * 0.9f) : 1f;

        float BuildGun(MeshKit k, WeaponDef w)
        {
            // a hand-made model where the kit has one: this faction's shape for the kind, accent in the faction's colour
            // (a looted gun: its rarity's colour)
            float mz = GunModels.Add(k, w, GunFaction(w), w.tier >= 0 ? Tiers.Of(w.tier) : second, Vector3.zero, Quaternion.identity, GunScale);
            if (mz > 0) return mz;
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
            if (anim)
            {
                if (deadT >= 0) return;
                anim.SetFloat("Speed", speed, 0.1f, dt);
                anim.SetBool("Crouch", crouch);
                anim.SetBool("Grounded", grounded);
                pitchNow = Mathf.Clamp(pitch, -60, 60);
                if (swingBlade)
                {
                    swing = 1;
                    anim.Play(swingKind == 1 && hasCombo ? "Swing2" : swingKind == 2 && hasCombo ? "Spin" : "Swing", 1, 0);
                    if (swingKind == 2) spinT = SpinTime;
                }
                swing = Mathf.MoveTowards(swing, 0, dt * 3f);
                TickSpin(dt);
                return;
            }
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
            if (swingBlade) { swing = 1; if (swingKind == 2) spinT = SpinTime; }
            swing = Mathf.MoveTowards(swing, 0, dt * 3f);
            TickSpin(dt);
            float aimP = pitch * 0.75f;
            // arms hold the weapon forward along the aim
            armR.localRotation = Quaternion.Euler(-80 + aimP - swing * 70, 0, swing * 40);
            armL.localRotation = Quaternion.Euler(-70 + aimP, 30, 0);
            gun.localRotation = Quaternion.Euler(aimP - swing * 60, -swing * 50, 0);
        }

        // ── Bladesworn combo: which swing (0 cut, 1 mirrored cut, 2 spin) and the spin itself ──
        public int swingKind;
        float spinT;
        const float SpinTime = 0.42f;
        bool comboChecked, hasCombo;

        void TickSpin(float dt)
        {
            if (anim && !comboChecked) { comboChecked = true; hasCombo = anim.HasState(1, Animator.StringToHash("Spin")); }
            Transform t = anim ? bodyRoot : transform;
            if (t == null || downW > 0) return;
            if (spinT > 0)
            {
                spinT -= dt;
                float k = Mathf.SmoothStep(0, 1, 1 - Mathf.Clamp01(spinT / SpinTime));
                t.localRotation = Quaternion.Euler(0, 360f * k, 0);
            }
            else if (t.localRotation != Quaternion.identity) t.localRotation = Quaternion.identity;
        }

        // ── royale duos: down (crawling) and kneeling over a downed mate ──
        float downW, armsW = 1;
        bool downChecked, hasDown;
        Vector3 rootBase; bool rootBaseSet;
        public const float DownDrop = -0.55f;          // the swim pose floats at swimming depth: put it on the ground
        const float CrawlFull = 1.4f;                  // crawl speed at which the stroke plays at full rate

        public void SetDown(bool down, bool reviving, float speed, float dt)
        {
            downW = Mathf.MoveTowards(downW, down ? 1 : 0, dt * 4);
            if (anim)
            {
                if (deadT >= 0) return;
                if (!downChecked) { downChecked = true; foreach (var p in anim.parameters) if (p.name == "Knocked") hasDown = true; }
                if (!hasDown) return;
                anim.SetBool("Knocked", down);
                anim.SetBool("Revive", reviving && !down);
                anim.SetFloat("CrawlRate", Mathf.Lerp(0.08f, 1f, Mathf.Clamp01(speed / CrawlFull)));
                armsW = Mathf.MoveTowards(armsW, down || reviving ? 0 : 1, dt * 6);
                anim.SetLayerWeight(1, armsW);
                if (down) pitchNow = 0;
                if (bodyRoot)
                {
                    if (!rootBaseSet) { rootBaseSet = true; rootBase = bodyRoot.localPosition; }
                    bodyRoot.localPosition = rootBase + Vector3.up * DownDrop * downW;
                }
                return;
            }
            if (deadT >= 0) return;
            // the code-built body: tip it forward onto the ground, centred over where the soldier is
            if (downW <= 0) { if (spinT <= 0 && transform.localRotation != Quaternion.identity) { transform.localRotation = Quaternion.identity; transform.localPosition = Vector3.zero; } return; }
            float k = Mathf.SmoothStep(0, 1, downW);
            transform.localRotation = Quaternion.Euler(84 * k, 0, 0);
            transform.localPosition = new Vector3(0, 0.12f * k, -height * 0.5f * k);
            if (down && speed > 0.2f)
            {
                float s = Mathf.Sin(Time.time * 6) * 30;
                armR.localRotation = Quaternion.Euler(-150 + s, 0, 0); armL.localRotation = Quaternion.Euler(-150 - s, 0, 0);
                legL.localRotation = Quaternion.Euler(s * 0.5f, 0, 0); legR.localRotation = Quaternion.Euler(-s * 0.5f, 0, 0);
            }
        }

        public void Die(Vector3 dir)
        {
            if (deadT >= 0) return;
            deadT = 0;
            if (anim && hasDown) { anim.SetBool("Knocked", false); anim.SetBool("Revive", false); anim.SetLayerWeight(1, 1); }
            if (anim) { anim.SetTrigger("Die"); return; }
            if (downW > 0) { transform.localRotation = Quaternion.identity; transform.localPosition = Vector3.zero; }
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
            var m = on ? Mats.Alpha(new Color(0.6f, 0.8f, 1f, 0.12f)) : Mats.Lit;
            foreach (var r in rends)
            {
                if (!r) continue;
                if (r is SkinnedMeshRenderer sk) { var arr = new Material[sk.sharedMesh ? sk.sharedMesh.subMeshCount : 1]; for (int i = 0; i < arr.Length; i++) arr[i] = m; sk.sharedMaterials = arr; }
                else r.sharedMaterial = m;
            }
        }

        public void SetVisible(bool v)
        {
            if (rends == null) rends = GetComponentsInChildren<Renderer>();
            foreach (var r in rends) if (r) r.enabled = v;
        }
    }
}
