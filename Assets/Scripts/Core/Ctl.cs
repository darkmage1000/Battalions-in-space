using UnityEngine;

namespace BIS
{
    /// <summary>
    /// The one place that reads the keyboard, mouse and gamepad (legacy Input Manager; the pad axes
    /// PadLX..PadDY are added to InputManager.asset by Editor/ProjectSetup). Gameplay reads only Ctl.
    /// </summary>
    public static class Ctl
    {
        public static Vector2 Move, Look;
        public static bool Fire, FireDown, Aim, AimDown, Jump, JumpDown, Sprint, Crouch, CrouchDown;
        public static bool Reload, Use, Swap, Equip, Ability, Pause, Score, ScoreHeld, Map, Hero, Cam;
        public static float Throttle, Roll, Climb;
        public static bool Boost, Alt, AltDown;
        public static bool PadActive;
        public static bool Confirm, Back;
        static bool padOk = true, ltWas, rtWas;

        static float Axis(string n)
        {
            if (!padOk) return 0;
            try { return Input.GetAxisRaw(n); } catch { padOk = false; return 0; }
        }

        public static void Update()
        {
            // keyboard + mouse
            Vector2 mv = new Vector2((Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0),
                                     (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0));
            float sens = Settings.MouseSens;
            Vector2 lk = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * sens;
            bool fire = Input.GetMouseButton(0), aim = Input.GetMouseButton(1);
            bool fireD = Input.GetMouseButtonDown(0), aimD = Input.GetMouseButtonDown(1);
            bool jump = Input.GetKey(KeyCode.Space), jumpD = Input.GetKeyDown(KeyCode.Space);
            bool sprint = Input.GetKey(KeyCode.LeftShift);
            bool crouch = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C);
            bool crouchD = Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.C);
            Reload = Input.GetKeyDown(KeyCode.R);
            Use = Input.GetKeyDown(KeyCode.E);
            Swap = Input.GetKeyDown(KeyCode.Q) || Input.mouseScrollDelta.y != 0;
            Equip = Input.GetKeyDown(KeyCode.G);
            Ability = Input.GetKeyDown(KeyCode.F);
            Pause = Input.GetKeyDown(KeyCode.Escape);
            Score = Input.GetKeyDown(KeyCode.Tab);
            ScoreHeld = Input.GetKey(KeyCode.Tab);
            Map = Input.GetKeyDown(KeyCode.M);
            Hero = Input.GetKeyDown(KeyCode.H);
            Cam = Input.GetKeyDown(KeyCode.V);
            Confirm = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            Back = Pause;
            float throttle = (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0);
            float roll = (Input.GetKey(KeyCode.A) ? 1 : 0) - (Input.GetKey(KeyCode.D) ? 1 : 0);
            float climb = (Input.GetKey(KeyCode.Space) ? 1 : 0) - (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C) ? 1 : 0);
            bool boost = sprint;
            bool alt = aim, altD = aimD;

            // gamepad (Xbox layout under Windows legacy input)
            Vector2 pl = new Vector2(Axis("PadLX"), -Axis("PadLY"));
            Vector2 pr = new Vector2(Axis("PadRX"), -Axis("PadRY"));
            float lt = Axis("PadLT"), rt = Axis("PadRT");
            if (pl.magnitude < 0.18f) pl = Vector2.zero;
            if (pr.magnitude < 0.15f) pr = Vector2.zero;
            bool padUsed = pl != Vector2.zero || pr != Vector2.zero || lt > 0.3f || rt > 0.3f;
            for (int b = 0; b < 10 && !padUsed; b++) if (Input.GetKey(KeyCode.JoystickButton0 + b)) padUsed = true;
            if (padUsed) PadActive = true;
            if (Input.anyKeyDown && !padUsed && !Input.GetKeyDown(KeyCode.JoystickButton0)) PadActive = false;
            if (lk.sqrMagnitude > 0.01f) PadActive = false;

            if (pl != Vector2.zero) { mv = pl; throttle = pl.y; roll = -pl.x * 0.8f; }
            if (pr != Vector2.zero) lk = new Vector2(pr.x, pr.y * (Settings.InvertPad ? -1 : 1)) * Settings.PadSens * Time.unscaledDeltaTime * 60f;
            bool rtOn = rt > 0.35f, ltOn = lt > 0.35f;
            if (rtOn) { fire = true; if (!rtWas) fireD = true; }
            if (ltOn) { aim = true; alt = true; if (!ltWas) { aimD = true; altD = true; } }
            rtWas = rtOn; ltWas = ltOn;
            if (Input.GetKey(KeyCode.JoystickButton0)) { jump = true; climb = 1; }
            if (Input.GetKeyDown(KeyCode.JoystickButton0)) { jumpD = true; }
            if (Input.GetKey(KeyCode.JoystickButton1)) { crouch = true; climb = -1; }
            if (Input.GetKeyDown(KeyCode.JoystickButton1)) crouchD = true;
            if (Input.GetKeyDown(KeyCode.JoystickButton2)) Reload = true;
            if (Input.GetKeyDown(KeyCode.JoystickButton3)) Use = true;
            if (Input.GetKeyDown(KeyCode.JoystickButton4)) Equip = true;
            if (Input.GetKeyDown(KeyCode.JoystickButton5)) Swap = true;
            if (Input.GetKey(KeyCode.JoystickButton8)) { sprint = true; boost = true; }
            if (Input.GetKeyDown(KeyCode.JoystickButton9)) Ability = true;
            if (Input.GetKeyDown(KeyCode.JoystickButton7)) Pause = true;
            if (Input.GetKeyDown(KeyCode.JoystickButton6)) Score = true;
            if (Input.GetKey(KeyCode.JoystickButton6)) ScoreHeld = true;
            if (Input.GetKeyDown(KeyCode.JoystickButton0)) Confirm = true;
            if (Input.GetKeyDown(KeyCode.JoystickButton1)) Back = true;
            float dy = Axis("PadDY");
            if (dy > 0.5f) Hero = true;

            Move = Vector2.ClampMagnitude(mv, 1);
            Look = lk;
            if (Settings.InvertY && !PadActive) Look.y = -Look.y;
            Fire = fire; FireDown = fireD; Aim = aim; AimDown = aimD;
            Jump = jump; JumpDown = jumpD; Sprint = sprint; Crouch = crouch; CrouchDown = crouchD;
            Throttle = throttle; Roll = roll; Climb = climb; Boost = boost; Alt = alt; AltDown = altD;
        }

        public static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }

    /// <summary>Device settings in PlayerPrefs.</summary>
    public static class Settings
    {
        public static float MouseSens = 2f, PadSens = 3f, Volume = 0.8f, MusicVolume = 0.5f, Fov = 65f;
        public static bool InvertY, InvertPad, ShowFps;
        public static int BotsPerTeam = 16, Difficulty = 1, Quality = 2;

        public static void Load()
        {
            MouseSens = PlayerPrefs.GetFloat("sens", 2f);
            PadSens = PlayerPrefs.GetFloat("padsens", 3f);
            Volume = PlayerPrefs.GetFloat("vol", 0.8f);
            MusicVolume = PlayerPrefs.GetFloat("music", 0.5f);
            Fov = PlayerPrefs.GetFloat("fov", 65f);
            InvertY = PlayerPrefs.GetInt("invy", 0) == 1;
            InvertPad = PlayerPrefs.GetInt("invpad", 0) == 1;
            ShowFps = PlayerPrefs.GetInt("fps", 0) == 1;
            BotsPerTeam = PlayerPrefs.GetInt("bots", 16);
            Difficulty = PlayerPrefs.GetInt("diff", 1);
            Quality = PlayerPrefs.GetInt("quality", Application.isMobilePlatform ? 0 : 2);
            Apply();
        }

        public static void Save()
        {
            PlayerPrefs.SetFloat("sens", MouseSens);
            PlayerPrefs.SetFloat("padsens", PadSens);
            PlayerPrefs.SetFloat("vol", Volume);
            PlayerPrefs.SetFloat("music", MusicVolume);
            PlayerPrefs.SetFloat("fov", Fov);
            PlayerPrefs.SetInt("invy", InvertY ? 1 : 0);
            PlayerPrefs.SetInt("invpad", InvertPad ? 1 : 0);
            PlayerPrefs.SetInt("fps", ShowFps ? 1 : 0);
            PlayerPrefs.SetInt("bots", BotsPerTeam);
            PlayerPrefs.SetInt("diff", Difficulty);
            PlayerPrefs.SetInt("quality", Quality);
            PlayerPrefs.Save();
            Apply();
        }

        public static void Apply()
        {
            AudioListener.volume = Volume;
            QualitySettings.shadows = Quality == 0 ? ShadowQuality.Disable : ShadowQuality.All;
            QualitySettings.shadowDistance = Quality >= 2 ? 140 : 70;
            QualitySettings.shadowResolution = Quality >= 2 ? ShadowResolution.High : ShadowResolution.Medium;
            QualitySettings.antiAliasing = Quality >= 2 ? 4 : 0;
            QualitySettings.vSyncCount = 1;
        }

        public static readonly string[] DifficultyNames = { "Recruit", "Veteran", "Elite" };
    }
}
