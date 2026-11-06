using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    /// <summary>The 3D galaxy map: planets, hyperlanes, owner rings and fleet markers.</summary>
    public class GalaxyView : MonoBehaviour
    {
        public const float Scale = 10f;
        readonly List<Transform> planets = new List<Transform>();
        readonly List<Renderer> rings = new List<Renderer>();
        readonly List<Renderer> marks = new List<Renderer>();
        readonly Dictionary<int, Transform> fleetObjs = new Dictionary<int, Transform>();
        Transform fleetRoot;
        float t;
        Vector3 camTarget;
        public float zoom = 1;
        Vector3 pan;

        public static Vector3 World(int p) { var v = Galaxy.Planets[p].pos; return new Vector3(v.x * Scale, 0, v.y * Scale); }

        public static GalaxyView Create()
        {
            var go = new GameObject("GalaxyView");
            var g = go.AddComponent<GalaxyView>();
            g.Build();
            return g;
        }

        void Build()
        {
            var sky = Mats.Sky(true);
            sky.SetColor("_Neb1", new Color(0.4f, 0.18f, 0.5f)); sky.SetColor("_Neb2", new Color(0.12f, 0.3f, 0.55f));
            sky.SetColor("_SunCol", new Color(1f, 0.9f, 0.8f)); sky.SetFloat("_NebAmt", 1.2f); sky.SetFloat("_Seed", 9);
            sky.SetVector("_SunDir", new Vector3(0.2f, -1, 0.3f));
            RenderSettings.skybox = sky; RenderSettings.fog = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.35f, 0.35f, 0.4f);
            Game.I.sun.transform.rotation = Quaternion.Euler(50, -30, 0);
            Game.I.sun.intensity = 1.2f; Game.I.sun.color = new Color(1f, 0.95f, 0.9f); Game.I.sun.shadows = LightShadows.None;
            Game.Cam.clearFlags = CameraClearFlags.Skybox; Game.Cam.farClipPlane = 4000; Game.Cam.fieldOfView = 50;

            // galactic core glow
            var core = new MeshKit();
            core.Quad(new Vector3(-60, -6, -60), new Vector3(-60, -6, 60), new Vector3(60, -6, 60), new Vector3(60, -6, -60), Color.white);
            var cg = core.Make("core", transform, false, Mats.Additive(new Color(0.55f, 0.4f, 0.7f, 0.25f)));
            _ = cg;

            for (int i = 0; i < Galaxy.Planets.Length; i++)
            {
                var pd = Galaxy.Planets[i];
                var k = new MeshKit();
                float r = 1.3f + (pd.income - 90) / 70f * 0.6f;
                k.Sphere(Vector3.zero, Vector3.one * r * 2, Quaternion.identity, pd.color, 20, 10);
                var go = k.Make("planet_" + pd.name, transform, false);
                go.transform.position = World(i);
                go.AddComponent<SphereCollider>().radius = r * 1.3f;
                planets.Add(go.transform);
                var rk = new MeshKit(); rk.Ring(Vector3.zero, r * 1.45f, r * 1.75f, Color.white, 40);
                var rg = rk.Make("ring", go.transform, false, Mats.Additive(Color.white, false));
                rings.Add(rg.GetComponent<Renderer>());
                var mk = new MeshKit(); mk.Ring(Vector3.zero, r * 1.95f, r * 2.15f, Color.white, 40);
                var mg = mk.Make("mark", go.transform, false, Mats.Additive(UI.Gold, false));
                mg.transform.localPosition = Vector3.up * 0.05f;
                marks.Add(mg.GetComponent<Renderer>());
            }
            foreach (var l in Galaxy.Lanes)
            {
                Vector3 a = World(l[0]), b = World(l[1]);
                var k = new MeshKit();
                Vector3 d = b - a; float len = d.magnitude;
                k.Box((a + b) / 2, new Vector3(0.12f, 0.02f, len), Quaternion.LookRotation(d), Color.white);
                k.Make("lane", transform, false, Mats.Additive(new Color(0.5f, 0.75f, 1f, 0.35f), false));
            }
            fleetRoot = new GameObject("fleets").transform; fleetRoot.SetParent(transform, false);
            camTarget = Vector3.zero;
        }

        public void Refresh(Campaign c, int selected, List<int> moveTargets)
        {
            for (int i = 0; i < planets.Count; i++)
            {
                int o = c.owner[i];
                Color col = o >= 0 ? Factions.Get(o).bolt : Color.gray;
                rings[i].sharedMaterial = Mats.Additive(new Color(col.r, col.g, col.b, 0.85f), false);
                bool mt = moveTargets != null && moveTargets.Contains(i);
                marks[i].enabled = i == selected || mt;
                marks[i].sharedMaterial = Mats.Additive(mt ? new Color(0.4f, 1f, 0.5f, 0.9f) : new Color(1f, 0.82f, 0.35f, 0.9f), false);
            }
            var alive = new HashSet<int>();
            foreach (var f in c.fleets)
            {
                alive.Add(f.id);
                if (!fleetObjs.TryGetValue(f.id, out var tr) || !tr)
                {
                    var fd = Factions.Get(f.faction);
                    var m = VehicleModels.Get(fd.ships[0], fd);
                    var go = new GameObject("fleet" + f.id);
                    go.transform.SetParent(fleetRoot, false);
                    for (int s = 0; s < 3; s++)
                    {
                        var sh = new GameObject("s");
                        sh.transform.SetParent(go.transform, false);
                        sh.AddComponent<MeshFilter>().sharedMesh = m.body;
                        sh.AddComponent<MeshRenderer>().sharedMaterial = Mats.Lit;
                        sh.transform.localPosition = new Vector3((s - 1) * 1.1f, s == 1 ? 0.3f : 0, s == 1 ? 0.6f : 0);
                        sh.transform.localScale = Vector3.one * 0.09f;
                    }
                    var cap = new MeshKit(); cap.Ring(Vector3.zero, 1.6f, 1.8f, Color.white, 24);
                    cap.Make("tag", go.transform, false, Mats.Additive(new Color(fd.bolt.r, fd.bolt.g, fd.bolt.b, 0.8f), false));
                    fleetObjs[f.id] = go.transform;
                    tr = go.transform;
                    tr.position = World(f.planet) + new Vector3(0, 2.6f, -2.4f);
                }
            }
            var dead = new List<int>();
            foreach (var kv in fleetObjs) if (!alive.Contains(kv.Key)) { if (kv.Value) Destroy(kv.Value.gameObject); dead.Add(kv.Key); }
            foreach (var d in dead) fleetObjs.Remove(d);
            lastCamp = c;
        }
        Campaign lastCamp;

        public int Pick(Vector2 screen)
        {
            var ray = Game.Cam.ScreenPointToRay(screen);
            if (Physics.Raycast(ray, out var h, 3000))
                for (int i = 0; i < planets.Count; i++) if (h.collider.transform == planets[i]) return i;
            return -1;
        }

        public void Focus(int p) { if (p >= 0) camTarget = World(p) * 0.5f; }

        void Update()
        {
            t += Time.deltaTime;
            foreach (var p in planets) p.Rotate(0, Time.deltaTime * 8, 0);
            if (lastCamp != null)
                foreach (var f in lastCamp.fleets)
                    if (fleetObjs.TryGetValue(f.id, out var tr) && tr)
                    {
                        Vector3 want = World(f.planet) + new Vector3(0, 2.6f + Mathf.Sin(t * 2 + f.id) * 0.2f, -2.4f);
                        Vector3 d = want - tr.position;
                        if (d.sqrMagnitude > 0.01f) tr.rotation = Quaternion.Slerp(tr.rotation, Quaternion.LookRotation(new Vector3(d.x, 0, d.z).sqrMagnitude > 0.01f ? new Vector3(d.x, 0, d.z) : Vector3.forward), Time.deltaTime * 5);
                        tr.position = Vector3.MoveTowards(tr.position, want, Time.deltaTime * 12);
                    }
            // camera: wheel zoom, WASD / right-drag pan
            if (Battle.I == null)
            {
                zoom = Mathf.Clamp(zoom - Input.mouseScrollDelta.y * 0.08f, 0.45f, 1.4f);
                Vector3 mv = new Vector3(Ctl.Move.x, 0, Ctl.Move.y) * Time.deltaTime * 30 * zoom;
                if (Input.GetMouseButton(1)) mv += new Vector3(-Input.GetAxis("Mouse X"), 0, -Input.GetAxis("Mouse Y")) * 1.2f * zoom;
                pan = Vector3.ClampMagnitude(pan + mv, 30);
                Vector3 focus = Vector3.Lerp(camTarget, Vector3.zero, 0.3f) + pan;
                Vector3 pos = focus + new Vector3(0, 52, -40) * zoom;
                var cam = Game.Cam;
                cam.transform.position = Vector3.Lerp(cam.transform.position, pos, Time.deltaTime * 4);
                cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, Quaternion.LookRotation(focus - pos), Time.deltaTime * 4);
            }
        }

        public Vector2 ScreenOf(int p, float yOff = 0)
        {
            Vector3 sp = Game.Cam.WorldToScreenPoint(World(p) + Vector3.up * yOff);
            if (sp.z < 0) return new Vector2(-999, -999);
            return new Vector2(sp.x / UI.S, (Screen.height - sp.y) / UI.S);
        }
    }
}
