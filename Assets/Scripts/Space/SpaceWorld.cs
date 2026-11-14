using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    /// <summary>Space battlefield: star sky, sun, background planet / nebula / asteroid field and two capital ships.</summary>
    public class SpaceWorld : MonoBehaviour
    {
        public SpaceMapDef def;
        public readonly CapitalShip[] ships = new CapitalShip[2];
        public float boundRadius = 1500;
        public const float ShipGap = 650;

        public void Build(SpaceMapDef d, Battle b)
        {
            def = d;
            var rng = new System.Random(d.seed);
            // capital ships face each other: local +z (the hangar side) points at the enemy
            for (int t = 0; t < 2; t++)
            {
                var go = new GameObject("capital_" + t);
                go.transform.SetParent(transform, false);
                var cs = go.AddComponent<CapitalShip>();
                Vector3 pos = new Vector3(t == 0 ? -40 : 40, t == 0 ? 0 : 25, t == 0 ? -ShipGap : ShipGap);
                cs.Build(b, t, b.Faction(t), pos, Quaternion.Euler(0, t == 0 ? 0 : 180, 0));
                ships[t] = cs;
            }

            var sky = Mats.Sky(true);
            sky.SetColor("_Neb1", d.neb1); sky.SetColor("_Neb2", d.neb2); sky.SetColor("_SunCol", d.sun);
            sky.SetFloat("_NebAmt", d.bg == SpaceBg.Nebula ? 1.4f : 0.55f);
            sky.SetFloat("_Seed", d.seed);
            Quaternion sunRot = Quaternion.Euler(28, 60 + d.seed * 7, 0);
            sky.SetVector("_SunDir", -(sunRot * Vector3.forward));
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = d.bg == SpaceBg.Nebula ? new Color(0.36f, 0.28f, 0.42f) : new Color(0.3f, 0.31f, 0.36f);
            RenderSettings.fog = d.bg == SpaceBg.Nebula;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = d.neb1 * 0.5f;
            RenderSettings.fogDensity = 0.0006f;
            Game.I.sun.transform.rotation = sunRot;
            Game.I.sun.color = d.sun;
            Game.I.sun.intensity = 1.25f;
            Game.I.sun.shadows = LightShadows.None;
            Shader.SetGlobalColor("_BIS_Rim", Color.Lerp(d.neb1, new Color(0.5f, 0.6f, 0.8f), 0.5f) * 0.5f);   // ships stand out from the dark
            Game.Cam.clearFlags = CameraClearFlags.Skybox;
            Game.Cam.farClipPlane = 9000;

            if (d.bg == SpaceBg.Planet)
            {
                var k = new MeshKit();
                k.Sphere(Vector3.zero, Vector3.one, Quaternion.identity, d.planet, 48, 24);
                var p = k.Make("planet", transform, false);
                p.transform.position = new Vector3(400, -4200, 1500);
                p.transform.localScale = Vector3.one * 6000;
                // atmosphere rim
                var a = new MeshKit();
                a.Sphere(Vector3.zero, Vector3.one, Quaternion.identity, Color.white, 48, 24);
                var ag = a.Make("atmo", transform, false, Mats.Additive(new Color(0.95f, 0.75f, 0.5f, 0.12f), false));
                ag.transform.position = p.transform.position; ag.transform.localScale = Vector3.one * 6200;
            }
            if (d.bg == SpaceBg.Nebula)
            {
                // drifting gas clouds you can fly through
                for (int i = 0; i < 26; i++)
                {
                    Vector3 c = new Vector3(R(rng, -900, 900), R(rng, -300, 300), R(rng, -400, 400));
                    var k = new MeshKit();
                    k.Sphere(Vector3.zero, Vector3.one, Quaternion.identity, Color.white, 10, 6);
                    Color col = Color.Lerp(d.neb1, d.neb2, (float)rng.NextDouble()); col.a = 0.05f;
                    var go = k.Make("cloud", transform, false, Mats.Alpha(col));
                    go.transform.position = c; go.transform.localScale = new Vector3(R(rng, 120, 260), R(rng, 60, 140), R(rng, 120, 260));
                }
            }
            if (d.bg == SpaceBg.Asteroids)
            {
                for (int i = 0; i < 140; i++)
                {
                    Vector3 c = new Vector3(R(rng, -1100, 1100), R(rng, -350, 350), R(rng, -520, 520));
                    if (Mathf.Abs(c.z) > 380 && Mathf.Abs(c.x) < 280) continue;   // keep clear of the capital ships
                    float s = R(rng, 8, 55);
                    var k = new MeshKit();
                    Color col = Color.Lerp(new Color(0.32f, 0.3f, 0.28f), new Color(0.48f, 0.42f, 0.36f), (float)rng.NextDouble());
                    k.Rock(Vector3.zero, new Vector3(s * R(rng, 0.8f, 1.4f), s * R(rng, 0.6f, 1f), s), Quaternion.identity, col, rng.Next(), 0.35f);
                    var go = k.Make("asteroid", transform, false);
                    go.transform.position = c; go.transform.rotation = Quaternion.Euler(R(rng, 0, 360), R(rng, 0, 360), R(rng, 0, 360));
                    go.AddComponent<SphereCollider>().radius = s * 0.42f;
                }
            }
            // distant fleet silhouettes for scale
            for (int t = 0; t < 2; t++)
                for (int i = 0; i < 3; i++)
                {
                    var k = new MeshKit();
                    Color c = new Color(0.25f, 0.26f, 0.3f);
                    k.Box(Vector3.zero, new Vector3(260, 30, 50), c);
                    k.Nose(new Vector3(160, 0, 0), new Vector3(50, 28, 60), Quaternion.Euler(0, 90, 0), c);
                    k.Box(new Vector3(-80, 22, 0), new Vector3(40, 18, 24), c);
                    var go = k.Make("far_ship", transform, false);
                    float z = (t == 0 ? -1 : 1) * R(rng, 1900, 2600);
                    go.transform.position = new Vector3(R(rng, -1500, 1500), R(rng, -400, 400), z);
                    go.transform.rotation = Quaternion.Euler(0, t == 0 ? 0 : 180, 0);
                }
        }

        static float R(System.Random r, float a, float b) => a + (b - a) * (float)r.NextDouble();

        public Hangar HangarAt(Vector3 p)
        {
            foreach (var s in ships) if (s && s.hangar.Contains(p)) return s.hangar;
            return null;
        }

        public CapitalShip ShipAt(Vector3 p)
        {
            foreach (var s in ships)
            {
                if (!s) continue;
                var l = s.transform.InverseTransformPoint(p);
                if (Mathf.Abs(l.x) < 60 && l.y > -2 && l.y < 22 && l.z > -48 && l.z < 34) return s;
            }
            return null;
        }

        /// <summary>Floor height under a point inside a ship (hangar and interior decks are at the ship's origin height).</summary>
        public float FloorY(Vector3 p)
        {
            var s = ShipAt(p);
            return s ? s.transform.position.y : p.y - 1000;
        }
    }
}
