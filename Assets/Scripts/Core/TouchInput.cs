using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    /// <summary>
    /// On-screen controls for phones and tablets: a floating move stick on the left, drag-to-look on the
    /// right, and buttons. Feeds Ctl; drawn by HUD while playing. Menus use IMGUI, which takes touches as clicks.
    /// </summary>
    public static class TouchInput
    {
        public static bool Active => Application.isMobilePlatform || Settings.ForceTouch;
        public class Btn { public string id, label; public Rect r; public bool held, down; public int finger = -1; }
        public static readonly List<Btn> Buttons = new List<Btn>();
        public static Vector2 StickOrigin, StickPos;
        public static int stickFinger = -1, lookFinger = -1;
        public static Vector2 Move, Look;
        static bool built;

        static void Build()
        {
            built = true;
            void B(string id, string label, float x, float y, float s) => Buttons.Add(new Btn { id = id, label = label, r = new Rect(x, y, s, s) });
            // positions are from the bottom-right corner in virtual UI units (negative = from right / bottom)
            B("fire", "FIRE", -250, -280, 170);
            B("aim", "AIM", -420, -200, 110);
            B("jump", "JUMP", -150, -470, 120);
            B("crouch", "DUCK", -290, -470, 110);
            B("reload", "R", -420, -330, 90);
            B("swap", "SWAP", -540, -170, 100);
            B("equip", "GEAR", -540, -300, 100);
            B("ability", "SKILL", -420, -450, 100);
            B("use", "USE", -150, -620, 110);
            B("sprint", "RUN", 40, -470, 110);
            B("hero", "HERO", -290, -620, 100);
            B("boost", "BOOST", 170, -470, 110);
            B("pause", "II", -110, 20, 90);
            B("score", "TAB", -220, 20, 90);
        }

        public static Rect ScreenRect(Btn b)
        {
            float x = b.r.x < 0 ? UI.W + b.r.x : b.r.x, y = b.r.y < 0 ? UI.H + b.r.y : b.r.y;
            return new Rect(x, y, b.r.width, b.r.height);
        }

        public static Btn Get(string id) { foreach (var b in Buttons) if (b.id == id) return b; return null; }

        public static void Update()
        {
            if (!built) Build();
            foreach (var b in Buttons) b.down = false;
            Look = Vector2.zero;
            bool playing = Battle.I != null && Battle.I.control.mode == PlayerControl.Mode.Play && !Battle.I.control.paused;
            if (!playing) { Move = Vector2.zero; stickFinger = lookFinger = -1; foreach (var b in Buttons) { b.held = false; b.finger = -1; } return; }
            float s = UI.S;
            for (int i = 0; i < Input.touchCount; i++)
            {
                var t = Input.GetTouch(i);
                Vector2 p = new Vector2(t.position.x / s, (Screen.height - t.position.y) / s);
                if (t.phase == TouchPhase.Began)
                {
                    Btn hit = null;
                    foreach (var b in Buttons) if (ScreenRect(b).Contains(p)) { hit = b; break; }
                    if (hit != null) { hit.held = true; hit.down = true; hit.finger = t.fingerId; }
                    else if (p.x < UI.W * 0.4f && stickFinger < 0) { stickFinger = t.fingerId; StickOrigin = StickPos = p; }
                    else if (lookFinger < 0) lookFinger = t.fingerId;
                }
                else if (t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary)
                {
                    if (t.fingerId == stickFinger) StickPos = p;
                    else if (t.fingerId == lookFinger) Look += new Vector2(t.deltaPosition.x, t.deltaPosition.y) / s * 0.18f * Settings.MouseSens;
                    else foreach (var b in Buttons) if (b.finger == t.fingerId && b.id == "fire") Look += new Vector2(t.deltaPosition.x, t.deltaPosition.y) / s * 0.12f * Settings.MouseSens;
                }
                else
                {
                    if (t.fingerId == stickFinger) stickFinger = -1;
                    if (t.fingerId == lookFinger) lookFinger = -1;
                    foreach (var b in Buttons) if (b.finger == t.fingerId) { b.held = false; b.finger = -1; }
                }
            }
            Move = stickFinger >= 0 ? Vector2.ClampMagnitude((StickPos - StickOrigin) / 110f, 1) : Vector2.zero;
            Move.y = -Move.y;
        }

        public static bool Held(string id) { var b = Get(id); return b != null && b.held; }
        public static bool Down(string id) { var b = Get(id); return b != null && b.down; }

        /// <summary>Drawn by the HUD during play.</summary>
        public static void Draw()
        {
            if (!Active) return;
            if (stickFinger >= 0)
            {
                UI.Dot(StickOrigin, 110, new Color(1, 1, 1, 0.08f));
                UI.Dot(StickOrigin + Vector2.ClampMagnitude(StickPos - StickOrigin, 110), 46, new Color(1, 1, 1, 0.3f));
            }
            else UI.Text(new Rect(40, UI.H - 200, 400, 30), "drag here to move", 18, new Color(1, 1, 1, 0.35f));
            foreach (var b in Buttons)
            {
                var r = ScreenRect(b);
                UI.Fill(r, b.held ? new Color(1f, 0.8f, 0.3f, 0.45f) : new Color(0.1f, 0.12f, 0.16f, 0.45f));
                UI.Frame(r, new Color(1, 1, 1, 0.35f), 2);
                UI.Text(r, b.label, b.id == "fire" ? 30 : 20, Color.white, TextAnchor.MiddleCenter);
            }
        }
    }
}
