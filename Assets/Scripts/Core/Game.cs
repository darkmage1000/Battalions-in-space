using UnityEngine;

namespace BIS
{
    /// <summary>
    /// Bootstrap and top-level flow. Created automatically after any scene loads (the Main scene is empty).
    /// Owns the camera, sun, audio, effects and the menus; battles are built and torn down in code.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class Game : MonoBehaviour
    {
        public static Game I;
        public static Camera Cam;
        public Light sun;
        public MenuUI menu;
        public Battle battle;
        GameObject backdrop;
        float backdropT;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I) return;
            var go = new GameObject("Game");
            go.AddComponent<Game>();
        }

        void Awake()
        {
            I = this;
            DontDestroyOnLoad(gameObject);
            Application.targetFrameRate = Application.isMobilePlatform ? 60 : 144;
            Settings.Load();
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) if (c.gameObject.scene.IsValid()) Destroy(c.gameObject);
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) Destroy(l.gameObject);
            var camGo = new GameObject("MainCamera") { tag = "MainCamera" };
            camGo.transform.SetParent(transform, false);
            Cam = camGo.AddComponent<Camera>();
            Cam.nearClipPlane = 0.15f; Cam.farClipPlane = 3000; Cam.fieldOfView = Settings.Fov;
            camGo.AddComponent<AudioListener>();
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(transform, false);
            sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional; sun.shadows = LightShadows.Soft; sun.intensity = 1.1f;
            sun.shadowBias = 0.06f; sun.shadowNormalBias = 0.5f;
            Sfx.Init(transform);
            Fx.Init(transform);
            menu = gameObject.AddComponent<MenuUI>();
            ShowMenu();
            AutoTest.MaybeStart();
        }

        void Update()
        {
            Ctl.Update();
            if (backdrop)
            {
                backdropT += Time.deltaTime;
                Cam.transform.position = new Vector3(Mathf.Sin(backdropT * 0.03f) * 40, 12 + Mathf.Sin(backdropT * 0.05f) * 4, -120);
                Cam.transform.rotation = Quaternion.Euler(4, 8 + Mathf.Sin(backdropT * 0.03f) * 6, 0);
                foreach (Transform t in backdrop.transform)
                    if (t.name.StartsWith("fighter")) t.position += t.forward * Time.deltaTime * 22;
                    else if (t.name == "planet") t.Rotate(0, Time.deltaTime * 0.6f, 0);
            }
        }

        public void ShowMenu()
        {
            menu.Show(MenuUI.Screen.Main);
            BuildBackdrop();
            if (!Sfx.Music.isPlaying) Sfx.Music.Play();
            Ctl.LockCursor(false);
        }

        public void StartBattle(BattleConfig cfg, System.Action<BattleResult> onEnd)
        {
            if (backdrop) Destroy(backdrop);
            foreach (var g in FindObjectsByType<GalaxyView>(FindObjectsSortMode.None)) Destroy(g.gameObject);
            menu.Hide();
            Time.timeScale = 1;
            battle = Battle.Create(cfg, r => { battle = null; onEnd?.Invoke(r); });
        }

        public void ClearBackdrop() { if (backdrop) Destroy(backdrop); backdrop = null; }

        /// <summary>A quiet space scene behind the menus.</summary>
        public void BuildBackdrop()
        {
            if (backdrop) Destroy(backdrop);
            backdrop = new GameObject("Backdrop");
            var sky = Mats.Sky(true);
            sky.SetColor("_Neb1", new Color(0.35f, 0.15f, 0.45f)); sky.SetColor("_Neb2", new Color(0.1f, 0.25f, 0.5f));
            sky.SetColor("_SunCol", new Color(1f, 0.9f, 0.75f)); sky.SetFloat("_NebAmt", 0.9f); sky.SetFloat("_Seed", 5);
            sky.SetVector("_SunDir", new Vector3(-0.6f, 0.3f, 0.7f));
            RenderSettings.skybox = sky;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.3f, 0.3f, 0.36f);
            sun.transform.rotation = Quaternion.LookRotation(-new Vector3(-0.6f, 0.3f, 0.7f));
            sun.color = new Color(1f, 0.92f, 0.8f); sun.intensity = 1.3f; sun.shadows = LightShadows.None;
            Cam.clearFlags = CameraClearFlags.Skybox; Cam.farClipPlane = 9000;
            var k = new MeshKit();
            k.Sphere(Vector3.zero, Vector3.one, Quaternion.identity, new Color(0.45f, 0.62f, 0.75f), 48, 24);
            var p = k.Make("planet", backdrop.transform, false);
            p.transform.position = new Vector3(260, -620, 900); p.transform.localScale = Vector3.one * 1400;
            var a = new MeshKit();
            a.Sphere(Vector3.zero, Vector3.one, Quaternion.identity, Color.white, 48, 24);
            var ag = a.Make("atmo", backdrop.transform, false, Mats.Additive(new Color(0.5f, 0.75f, 1f, 0.12f), false));
            ag.transform.position = p.transform.position; ag.transform.localScale = Vector3.one * 1450;
            var rng = new System.Random(4);
            for (int i = 0; i < 12; i++)
            {
                var f = Factions.All[i % 4];
                var def = f.ships[i % 3 == 2 ? 1 : 0];
                var m = VehicleModels.Get(def, f);
                var go = new GameObject("fighter" + i);
                go.transform.SetParent(backdrop.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = m.body;
                go.AddComponent<MeshRenderer>().sharedMaterial = Mats.Lit;
                go.transform.position = new Vector3(-260 + (float)rng.NextDouble() * 120 + (i % 4) * 30, -10 + (float)rng.NextDouble() * 60, 40 + (float)rng.NextDouble() * 200);
                go.transform.rotation = Quaternion.Euler(-4, 80, (float)rng.NextDouble() * 20 - 10);
            }
            backdropT = 0;
        }
    }
}
