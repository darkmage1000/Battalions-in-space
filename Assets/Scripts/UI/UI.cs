using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    /// <summary>
    /// IMGUI helpers. Everything is laid out in a virtual 1080-pixel-high space (UI.W wide) and scaled
    /// with GUI.matrix, so menus and HUD look the same at any resolution.
    /// </summary>
    public static class UI
    {
        public static float S => Screen.height / 1080f;
        public static float W => Screen.width / S;
        public const float H = 1080;
        static readonly Dictionary<int, Texture2D> texes = new Dictionary<int, Texture2D>();
        static GUIStyle label, button, box, field;
        static Font font;
        public static readonly Color Panel = new Color(0.05f, 0.07f, 0.1f, 0.82f);
        public static readonly Color PanelLight = new Color(0.12f, 0.15f, 0.2f, 0.9f);
        public static readonly Color Gold = new Color(1f, 0.82f, 0.35f);
        public static readonly Color Dim = new Color(0.65f, 0.7f, 0.75f);
        public static readonly Color Bad = new Color(1f, 0.45f, 0.35f);
        public static readonly Color Good = new Color(0.45f, 1f, 0.55f);
        public static Vector2 Mouse { get { var m = Event.current != null ? Event.current.mousePosition : (Vector2)Input.mousePosition; return m; } }

        public static void Begin()
        {
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(S, S, 1));
            if (label == null) Init();
        }

        static void Init()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label = new GUIStyle { font = font, fontSize = 22, richText = true, wordWrap = false };
            label.normal.textColor = Color.white;
            button = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = 24 };
            box = new GUIStyle();
            field = new GUIStyle(label) { alignment = TextAnchor.MiddleLeft };
        }

        public static Texture2D Tex(Color c)
        {
            int k = ((int)(c.r * 255) << 24) | ((int)(c.g * 255) << 16) | ((int)(c.b * 255) << 8) | (int)(c.a * 255);
            if (texes.TryGetValue(k, out var t) && t) return t;
            t = new Texture2D(1, 1) { hideFlags = HideFlags.DontSave };
            t.SetPixel(0, 0, c); t.Apply();
            texes[k] = t;
            return t;
        }

        public static void Fill(Rect r, Color c) => GUI.DrawTexture(r, Tex(c));

        public static void Box(Rect r, Color? c = null)
        {
            Fill(r, c ?? Panel);
            var edge = new Color(1, 1, 1, 0.08f);
            Fill(new Rect(r.x, r.y, r.width, 1), edge);
            Fill(new Rect(r.x, r.yMax - 1, r.width, 1), edge);
        }

        public static void Frame(Rect r, Color c, float w = 2)
        {
            Fill(new Rect(r.x, r.y, r.width, w), c);
            Fill(new Rect(r.x, r.yMax - w, r.width, w), c);
            Fill(new Rect(r.x, r.y, w, r.height), c);
            Fill(new Rect(r.xMax - w, r.y, w, r.height), c);
        }

        public static void Text(Rect r, string s, int size = 22, Color? col = null, TextAnchor a = TextAnchor.UpperLeft, bool wrap = false, bool shadow = true)
        {
            label.fontSize = size; label.alignment = a; label.wordWrap = wrap;
            if (shadow)
            {
                label.normal.textColor = new Color(0, 0, 0, 0.75f);
                GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), s, label);
            }
            label.normal.textColor = col ?? Color.white;
            GUI.Label(r, s, label);
        }

        public static float TextHeight(string s, int size, float width)
        {
            label.fontSize = size; label.wordWrap = true;
            return label.CalcHeight(new GUIContent(s), width);
        }

        public static void Bar(Rect r, float f, Color c, Color? back = null)
        {
            Fill(r, back ?? new Color(0, 0, 0, 0.55f));
            Fill(new Rect(r.x, r.y, r.width * Mathf.Clamp01(f), r.height), c);
        }

        /// <summary>A flat button. Returns true when clicked.</summary>
        public static bool Button(Rect r, string s, bool selected = false, bool enabled = true, int size = 24, Color? tint = null)
        {
            bool hover = enabled && r.Contains(Mouse);
            Color bg = !enabled ? new Color(0.1f, 0.1f, 0.12f, 0.7f) : selected ? new Color(0.85f, 0.62f, 0.18f, 0.95f) : hover ? new Color(0.25f, 0.33f, 0.45f, 0.95f) : new Color(0.13f, 0.17f, 0.24f, 0.92f);
            if (tint.HasValue && enabled && !selected) bg = Color.Lerp(bg, tint.Value, hover ? 0.55f : 0.35f);
            Fill(r, bg);
            Frame(r, selected ? Gold : new Color(1, 1, 1, hover ? 0.35f : 0.12f), 1);
            button.fontSize = size;
            button.normal.textColor = enabled ? (selected ? Color.black : Color.white) : new Color(1, 1, 1, 0.35f);
            GUI.Label(r, s, button);
            if (enabled && GUI.Button(r, GUIContent.none, box))
            {
                Sfx.Play2D("ui", 0.6f);
                return true;
            }
            return false;
        }

        public static float Slider(Rect r, string label, float v, float min, float max, string shown)
        {
            Text(new Rect(r.x, r.y, r.width * 0.42f, r.height), label, 22, null, TextAnchor.MiddleLeft);
            Rect bar = new Rect(r.x + r.width * 0.44f, r.y + r.height * 0.35f, r.width * 0.4f, r.height * 0.3f);
            Fill(bar, new Color(0, 0, 0, 0.6f));
            float f = Mathf.InverseLerp(min, max, v);
            Fill(new Rect(bar.x, bar.y, bar.width * f, bar.height), Gold);
            Fill(new Rect(bar.x + bar.width * f - 5, bar.y - 6, 10, bar.height + 12), Color.white);
            Text(new Rect(bar.xMax + 12, r.y, r.width * 0.16f, r.height), shown, 22, Gold, TextAnchor.MiddleLeft);
            Rect hit = new Rect(bar.x - 8, r.y, bar.width + 16, r.height);
            var e = Event.current;
            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && hit.Contains(e.mousePosition))
            {
                v = Mathf.Lerp(min, max, Mathf.Clamp01((e.mousePosition.x - bar.x) / bar.width));
                e.Use();
            }
            return v;
        }

        public static bool Toggle(Rect r, string s, bool v)
        {
            Text(new Rect(r.x, r.y, r.width - 140, r.height), s, 22, null, TextAnchor.MiddleLeft);
            if (Button(new Rect(r.xMax - 130, r.y + 4, 130, r.height - 8), v ? "ON" : "OFF", v, true, 20)) v = !v;
            return v;
        }

        public static void Line(Vector2 a, Vector2 b, Color c, float w)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.5f) return;
            var m = GUI.matrix;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            GUIUtility.RotateAroundPivot(ang, a);
            Fill(new Rect(a.x, a.y - w / 2, len, w), c);
            GUI.matrix = m;
        }

        public static void Dot(Vector2 p, float r, Color c)
        {
            Fill(new Rect(p.x - r, p.y - r, r * 2, r * 2), c);
        }

        public static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
    }
}
