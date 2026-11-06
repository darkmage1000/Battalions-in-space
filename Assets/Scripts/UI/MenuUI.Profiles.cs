using UnityEngine;

namespace BIS
{
    /// <summary>"Who's playing?": pick, make or delete an account. Nothing else opens until one is chosen.</summary>
    public partial class MenuUI
    {
        string newName = "";
        bool naming;
        string nameError;
        Profile confirmDelete;
        TouchScreenKeyboard kb;

        void ProfilesScreen()
        {
            bool none = Profiles.All.Count == 0;
            Title(none ? "Make your account to start" : "Who's playing?");
            float w = UI.W;
            Rect r = new Rect(w / 2 - 560, 190, 1120, confirmDelete != null ? 300 : none ? 340 : 200 + Mathf.Min(5, Profiles.All.Count) * 76 + (naming ? 260 : 90));
            UI.Box(r);
            float x = r.x + 40, y = r.y + 26, rw = r.width - 80;

            if (confirmDelete != null)
            {
                UI.Text(new Rect(x, y, rw, 90), $"Delete the account \"{confirmDelete.name}\"? Its record and its Galactic Conquest save go with it. This can't be undone.", 24, Color.white, TextAnchor.UpperLeft, true); y += 110;
                if (UI.Button(new Rect(x, y, 300, 70), "DELETE IT", false, true, 26, new Color(0.65f, 0.2f, 0.2f))) { Profiles.Delete(confirmDelete); confirmDelete = null; }
                if (UI.Button(new Rect(x + 320, y, 300, 70), "KEEP IT", false, true, 26)) confirmDelete = null;
                return;
            }

            if (!none)
            {
                UI.Text(new Rect(x, y, rw, 30), "ACCOUNTS ON THIS DEVICE", 22, UI.Gold); y += 40;
                int shown = 0;
                foreach (var p in Profiles.All.ToArray())
                {
                    if (shown++ >= 5) break;
                    bool cur = Profiles.Current == p;
                    string rec = p.battles > 0 ? $"{p.battles} battles, {p.wins} won, {p.kills} kills" : "no battles yet";
                    if (UI.Button(new Rect(x, y, rw - 190, 66), $"{p.name}   <size=17>{rec}</size>", cur, true, 26)) { Profiles.Select(p); naming = false; screen = Screen.Main; }
                    if (UI.Button(new Rect(x + rw - 175, y, 175, 66), "DELETE", false, true, 20)) confirmDelete = p;
                    y += 76;
                }
                y += 10;
            }

            if (none || naming)
            {
                UI.Text(new Rect(x, y, rw, 30), "USERNAME (your name in battles and in Wi-Fi games)", 22, UI.Gold); y += 38;
                Rect field = new Rect(x, y, 560, 64);
                UI.Box(field, UI.PanelLight);
                bool blink = (int)(Time.unscaledTime * 2) % 2 == 0;
                UI.Text(field, newName.Length > 0 ? newName + (blink ? "|" : " ") : "tap here and type a name", 28, newName.Length > 0 ? Color.white : UI.Dim, TextAnchor.MiddleCenter);
                // phones: the system keyboard; PCs: type straight in
                if (Event.current.type == EventType.MouseUp && field.Contains(UI.Mouse) && TouchScreenKeyboard.isSupported)
                    kb = TouchScreenKeyboard.Open(newName, TouchScreenKeyboardType.Default, false, false, false, false, "Username", Profiles.MaxName);
                if (kb != null)
                {
                    newName = kb.text.Length > Profiles.MaxName ? kb.text.Substring(0, Profiles.MaxName) : kb.text;
                    if (kb.status == TouchScreenKeyboard.Status.Done) { kb = null; TryCreate(); }
                    else if (kb.status != TouchScreenKeyboard.Status.Visible) kb = null;
                }
                var e = Event.current;
                if (e.type == EventType.KeyDown)
                {
                    char c = e.character;
                    if (e.keyCode == KeyCode.Backspace) { if (newName.Length > 0) newName = newName.Substring(0, newName.Length - 1); e.Use(); }
                    else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { TryCreate(); e.Use(); }
                    else if (c != 0 && (char.IsLetterOrDigit(c) || c == ' ' || c == '_' || c == '-') && newName.Length < Profiles.MaxName) { newName += c; e.Use(); }
                }
                if (UI.Button(new Rect(x + 580, y, 300, 64), "CREATE", false, newName.Trim().Length >= 2, 26, new Color(0.2f, 0.6f, 0.3f))) TryCreate();
                y += 76;
                if (nameError != null) UI.Text(new Rect(x, y, rw, 30), nameError, 20, UI.Bad);
                y += 40;
                UI.Text(new Rect(x, y, rw, 60), "Each account keeps its own Galactic Conquest campaign and battle record. Accounts stay on this device.", 18, UI.Dim, TextAnchor.UpperLeft, true);
            }
            else if (UI.Button(new Rect(x, y, 460, 66), "+ NEW ACCOUNT", false, Profiles.All.Count < 5, 24)) { naming = true; newName = ""; nameError = null; }

            if (Profiles.Current != null && UI.Button(new Rect(r.center.x - 160, r.yMax + 20, 320, 64), "BACK", false, true, 26)) { naming = false; kb = null; screen = Screen.Main; }
        }

        void TryCreate()
        {
            nameError = Profiles.Problem(newName);
            if (nameError != null) return;
            Profiles.Create(newName);
            newName = ""; naming = false; kb = null;
            Sfx.Play2D("ui2");
            screen = Screen.Main;
        }
    }
}
