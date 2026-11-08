using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    /// <summary>
    /// On-screen controls for phones and tablets. Left: floating move stick (push it all the way to run) with a
    /// second FIRE button above it, so the right thumb can aim while the left thumb shoots. Right: drag anywhere
    /// to look; the big FIRE button also aims while held. Only the buttons that make sense right now are shown
    /// (on foot / driving / gunner / piloting). Auto-fire (Options) shoots when the crosshair is on an enemy.
    /// </summary>
    public static class TouchInput
    {
        public static bool Active => Settings.Current == Settings.InputDevice.Touch;
        public class Btn
        {
            public string id, label; public Rect r; public bool held, down, visible; public int finger = -1; public bool big;
            public bool hot;   // flashes red (EVADE while something is locked on)
        }
        public static readonly List<Btn> Buttons = new List<Btn>();
        public static Vector2 StickOrigin, StickPos;
        public static int stickFinger = -1, lookFinger = -1;
        public static Vector2 Move, Look;
        /// <summary>Scoped guns: ZOOM is a switch (tap in, tap out), so both thumbs stay free to look and fire.</summary>
        public static bool zoomOn;
        static bool built;

        static Btn B(string id, string label, float x, float y, float s, bool big = false)
        {
            var b = new Btn { id = id, label = label, r = new Rect(x, y, s, s), big = big };
            Buttons.Add(b);
            return b;
        }

        static void Build()
        {
            built = true;
            // positions in virtual UI units; negative = measured from the right / bottom edge
            B("fire", "FIRE", -300, -330, 200, true);
            B("jump", "JUMP", -150, -470, 120);
            B("aim", "ZOOM", -470, -230, 110);
            B("swap", "SWAP", -470, -360, 100);
            B("equip", "GEAR", -600, -230, 100);
            B("ability", "SKILL", -300, -480, 110);
            B("use", "ENTER", -150, -620, 120);
            B("hero", "HERO", -440, -480, 110);
            B("fire2", "FIRE", 60, -560, 130);
            B("boost", "BOOST", -150, -470, 120);
            B("evade", "EVADE", -630, -370, 130);
            B("alt", "MSL", -470, -230, 110);
            B("up", "UP", -620, -360, 100);
            B("down", "DOWN", -620, -230, 100);
            B("pause", "II", -110, 20, 90);
            B("score", "TAB", -220, 20, 90);
            B("map", "MAP", -330, 20, 90);
        }

        public static Rect ScreenRect(Btn b)
        {
            float x = b.r.x < 0 ? UI.W + b.r.x : b.r.x, y = b.r.y < 0 ? UI.H + b.r.y : b.r.y;
            return new Rect(x, y, b.r.width, b.r.height);
        }

        public static Btn Get(string id) { foreach (var b in Buttons) if (b.id == id) return b; return null; }

        /// <summary>Which buttons show, and their labels, for what the player is doing right now.</summary>
        static void Layout()
        {
            var pc = Battle.I != null ? Battle.I.control : null;
            var me = pc != null ? pc.Me : null;
            foreach (var b in Buttons) b.visible = false;
            bool zoomGun = false;
            void Show(string id, string label = null) { var b = Get(id); b.visible = true; if (label != null) b.label = label; }
            Show("pause"); Show("score");
            if (Battle.I != null && Battle.I.land != null) Show("map");
            if (me == null) { zoomOn = false; return; }
            if (!me.InVehicle && me.knocked) { zoomOn = false; return; }      // down: crawl with the stick, nothing else
            if (!me.InVehicle)
            {
                Show("fire", "FIRE"); Show("fire2"); Show("jump"); Show("equip", "GEAR");
                if (me.weapons[1] != null) Show("swap", "SWAP");
                if (me.Weapon.def.zoomFov > 0)
                {
                    zoomGun = true;
                    // the clip ran dry: the scope comes down by itself so the rifle reloads
                    if (zoomOn && me.Weapon.clip == 0 && me.Weapon.ammo > 0) zoomOn = false;
                    Show("aim", zoomOn ? "UNZOOM" : "ZOOM");
                }
                else if (me.IsHero && me.hero.kind == HeroKind.Blade) Show("aim", "BLOCK");
                if (me.AbilityName != null) Show("ability", me.AbilityName.Length > 6 ? "SKILL" : me.AbilityName);
                if (Battle.I.player.heroReady && !me.IsHero) Show("hero", "HERO");
                if (pc.NearbyVehicle()) Show("use", "ENTER");
                if (!zoomGun) zoomOn = false;
                if (Battle.I.royale)
                {
                    if (me.medkits + me.shields > 0) Show("hero", me.Using ? "..." : "HEAL");
                    string rp = pc.royalePrompt;
                    if (rp != null && rp.StartsWith("[E]")) Show("use", rp.Contains("REVIVE") ? "REVIVE" : rp.Contains("RESPAWN") ? "RESPAWN" : rp.Contains("VAULT") || rp.Contains("CHEST") ? "OPEN" : "TAKE");
                }
                return;
            }
            zoomOn = false;
            var v = me.vehicle;
            if (v.CanExitHere()) Show("use", "EXIT");
            if (v.riders.Length > 1) Show("swap", "SEAT");
            if (me.seat == 0)
            {
                Show("fire", "FIRE"); Show("fire2"); Show("boost", "BOOST");
                if (v.sec != null) Show("alt", v.sec.def.kind == WKind.Repair ? "HEAL" : v.sec.def.kind == WKind.Torpedo ? "TORP" : "MSL");
                if (v.Flies) Show("ability", pc.flyLock != null ? "FREE" : "LOCK");
                if (v.Flies && !v.IsShip) { Show("up"); Show("down"); }
                if (v.CanEvadeKind) { float cd = v.EvadeReadyIn; Show("evade", cd > 0 ? $"{Mathf.CeilToInt(cd)}s" : "EVADE"); Get("evade").hot = cd <= 0 && HUD.ThreatLevel > 0; }
            }
            else if (me.seat == 1 && v.gunnerT) { Show("fire", "FIRE"); Show("fire2"); }
        }

        public static void Update()
        {
            if (!built) Build();
            foreach (var b in Buttons) b.down = false;
            Look = Vector2.zero;
            bool playing = Battle.I != null && Battle.I.control.mode == PlayerControl.Mode.Play && !Battle.I.control.paused;
            if (!playing) { Move = Vector2.zero; stickFinger = lookFinger = -1; zoomOn = false; foreach (var b in Buttons) { b.held = false; b.finger = -1; } return; }
            Layout();
            float s = UI.S;
            float look = 0.4f * Settings.TouchSens;
            for (int i = 0; i < Input.touchCount; i++)
            {
                var t = Input.GetTouch(i);
                Vector2 p = new Vector2(t.position.x / s, (Screen.height - t.position.y) / s);
                Vector2 delta = new Vector2(t.deltaPosition.x, t.deltaPosition.y) / s;
                if (t.phase == TouchPhase.Began)
                {
                    Btn hit = null;
                    foreach (var b in Buttons) if (b.visible && ScreenRect(b).Contains(p)) { hit = b; break; }
                    if (hit != null) { hit.held = true; hit.down = true; hit.finger = t.fingerId; }
                    else if (p.x < UI.W * 0.4f && stickFinger < 0) { stickFinger = t.fingerId; StickOrigin = StickPos = p; }
                    else if (lookFinger < 0) lookFinger = t.fingerId;
                }
                else if (t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary)
                {
                    if (t.fingerId == stickFinger)
                    {
                        StickPos = p;
                        // the stick base follows a finger that drifts far, so it never runs out of travel
                        Vector2 off = StickPos - StickOrigin;
                        if (off.magnitude > 150) StickOrigin = StickPos - off.normalized * 150;
                    }
                    else if (t.fingerId == lookFinger) Look += delta * look;
                    else foreach (var b in Buttons) if (b.finger == t.fingerId && b.id == "fire") Look += delta * look;   // aim while holding FIRE
                }
                else
                {
                    if (t.fingerId == stickFinger) stickFinger = -1;
                    if (t.fingerId == lookFinger) lookFinger = -1;
                    foreach (var b in Buttons) if (b.finger == t.fingerId) { b.held = false; b.finger = -1; }
                }
            }
            // buttons that disappeared release their fingers
            foreach (var b in Buttons) if (!b.visible && b.held) { b.held = false; b.finger = -1; }
            Move = stickFinger >= 0 ? Vector2.ClampMagnitude((StickPos - StickOrigin) / 110f, 1) : Vector2.zero;
            Move.y = -Move.y;
        }

        public static bool Held(string id) { var b = Get(id); return b != null && b.visible && b.held; }
        public static bool Down(string id) { var b = Get(id); return b != null && b.visible && b.down; }
        public static bool Running => Move.magnitude > 0.92f;

        /// <summary>Drawn by the HUD during play.</summary>
        public static void Draw()
        {
            if (!Active) return;
            if (stickFinger >= 0)
            {
                UI.Dot(StickOrigin, 110, new Color(1, 1, 1, 0.08f));
                UI.Dot(StickOrigin + Vector2.ClampMagnitude(StickPos - StickOrigin, 110), 46, Running ? new Color(1f, 0.85f, 0.4f, 0.4f) : new Color(1, 1, 1, 0.3f));
            }
            else UI.Text(new Rect(40, UI.H - 200, 460, 30), "drag here to move - push all the way to run", 18, new Color(1, 1, 1, 0.35f));
            foreach (var b in Buttons)
            {
                if (!b.visible) continue;
                var r = ScreenRect(b);
                bool flash = b.hot && Mathf.Repeat(Time.unscaledTime * 4, 1) < 0.5f;
                UI.Fill(r, b.held || (b.id == "aim" && zoomOn) ? new Color(1f, 0.8f, 0.3f, 0.45f) : flash ? new Color(0.9f, 0.15f, 0.1f, 0.65f) : b.big ? new Color(0.5f, 0.12f, 0.1f, 0.45f) : new Color(0.1f, 0.12f, 0.16f, 0.45f));
                UI.Frame(r, new Color(1, 1, 1, 0.35f), 2);
                UI.Text(r, b.label, b.big ? 34 : 20, Color.white, TextAnchor.MiddleCenter);
            }
            if (Settings.AutoFire) UI.Text(new Rect(UI.W - 330, UI.H - 120, 320, 24), "auto-fire on", 15, new Color(1, 1, 1, 0.4f), TextAnchor.MiddleRight);
        }
    }
}
