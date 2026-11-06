using UnityEngine;
using BIS.Net;

namespace BIS
{
    /// <summary>The Wi-Fi game screens: host (the Quickplay setup plus who you play with) or join (games found on this network, or an address).</summary>
    public partial class MenuUI
    {
        bool qpHost;                 // the Quickplay screen is setting up a hosted Wi-Fi game
        bool joinTyping;
        string joinIp = "";

        /// <summary>Back to the Wi-Fi screens after a battle (still connected if the other player is).</summary>
        public void ShowMulti()
        {
            if (Session.IsHost) { qpHost = true; screen = Screen.Quick; Session.status = Session.peerName != null ? Session.peerName + " is here - start when ready" : "Waiting for someone to join..."; }
            else { qpHost = false; screen = Screen.Multi; }
            enabled = true;
        }

        void Update()
        {
            if (screen == Screen.Multi || (screen == Screen.Quick && qpHost))
            {
                if (Session.IsHost) Session.HostLobbyUpdate();
                else if (Session.IsClient) Session.ClientLobbyUpdate();
                Session.disco.Update();
            }
        }

        string LobbyInfo() => (qpMode == 1 ? Maps.Space[qpSpace].name : Maps.Land[qpLand].name) + ", " + Factions.All[qpFaction].shortName + " v " + Factions.All[qpEnemy].shortName;

        void Multi()
        {
            Title("Play on Wi-Fi");
            float w = UI.W;
            Rect r = new Rect(w / 2 - 560, 190, 1120, 760);
            UI.Box(r);
            float x = r.x + 40, y = r.y + 26, rw = r.width - 80;
            UI.Text(new Rect(x, y, rw, 30), $"This device: {Session.localName}   ({NetLink.LocalIp()})", 20, UI.Dim); y += 44;

            if (Session.IsClient && Session.link != null && !Session.link.Closed)
            {
                UI.Text(new Rect(x, y, rw, 40), Session.status, 26, UI.Gold); y += 54;
                UI.Text(new Rect(x, y, rw, 120), "The host picks the map and the sides, then starts the battle. You'll be taken in as soon as they do.", 20, Color.white, TextAnchor.UpperLeft, true);
                if (UI.Button(new Rect(r.center.x - 160, r.yMax + 20, 320, 64), "LEAVE", false, true, 26)) { Session.Shutdown(); }
                return;
            }

            if (Session.role == Session.Role.None && !Session.disco.Listening)
            {
                UI.Text(new Rect(x, y, rw, 90), "Both devices must be on the same Wi-Fi network. One of you hosts (picks the map and plays on it); the other joins.", 21, Color.white, TextAnchor.UpperLeft, true); y += 90;
                if (UI.Button(new Rect(x, y, 500, 80), "HOST A GAME", false, true, 30, new Color(0.2f, 0.6f, 0.3f)))
                {
                    qpHost = true;
                    Session.HostLobby(LobbyInfo);
                    screen = Screen.Quick;
                }
                if (UI.Button(new Rect(x + 540, y, 500, 80), "JOIN A GAME", false, true, 30)) { Session.StartBrowsing(); joinTyping = false; }
                y += 110;
                if (!string.IsNullOrEmpty(Session.status)) UI.Text(new Rect(x, y, rw, 60), Session.status, 20, UI.Bad, TextAnchor.UpperLeft, true);
                if (UI.Button(new Rect(r.center.x - 160, r.yMax + 20, 320, 64), "BACK", false, true, 26)) { Session.Shutdown(); screen = Screen.Main; }
                return;
            }

            // joining: games heard on this network, or type the host's address
            UI.Text(new Rect(x, y, rw, 30), "GAMES ON THIS WI-FI", 22, UI.Gold); y += 40;
            int n = 0;
            foreach (var kv in Session.disco.found)
            {
                string label = $"{kv.Value.name}  -  {kv.Value.info}";
                if (UI.Button(new Rect(x, y, rw, 60), label, false, !kv.Value.info.EndsWith("(full)"), 22)) Session.Join(kv.Key);
                y += 68; n++;
                if (n >= 4) break;
            }
            if (n == 0) { UI.Text(new Rect(x, y, rw, 60), "Looking... (the other device should be on HOST A GAME)", 20, UI.Dim); y += 60; }
            y += 10;
            UI.Text(new Rect(x, y, rw, 30), "OR TYPE THE HOST'S ADDRESS (shown on their screen)", 20, UI.Gold); y += 36;
            Rect field = new Rect(x, y, 420, 56);
            UI.Box(field, UI.PanelLight);
            UI.Text(field, joinIp.Length > 0 ? joinIp : "192.168...", 26, joinIp.Length > 0 ? Color.white : UI.Dim, TextAnchor.MiddleCenter);
            if (UI.Button(new Rect(x + 440, y, 200, 56), "CONNECT", false, joinIp.Length >= 7, 22)) Session.Join(joinIp.Trim());
            if (UI.Button(new Rect(x + 660, y, 200, 56), joinTyping ? "HIDE KEYS" : "KEYPAD", joinTyping, true, 20)) joinTyping = !joinTyping;
            y += 70;
            if (joinTyping)
            {
                // a keypad: phones don't always bring up a keyboard for IMGUI
                string keys = "123456789.0<";
                for (int i = 0; i < keys.Length; i++)
                {
                    char c = keys[i];
                    var kr = new Rect(x + (i % 6) * 110, y + (i / 6) * 66, 100, 58);
                    if (UI.Button(kr, c == '<' ? "DEL" : c.ToString(), false, true, 26))
                    {
                        if (c == '<') { if (joinIp.Length > 0) joinIp = joinIp.Substring(0, joinIp.Length - 1); }
                        else if (joinIp.Length < 15) joinIp += c;
                    }
                }
                y += 140;
            }
            else
            {
                // keyboard typing on PC
                var e = Event.current;
                if (e != null && e.type == EventType.KeyDown)
                {
                    char c = e.character;
                    if ((char.IsDigit(c) || c == '.') && joinIp.Length < 15) { joinIp += c; e.Use(); }
                    else if (e.keyCode == KeyCode.Backspace && joinIp.Length > 0) { joinIp = joinIp.Substring(0, joinIp.Length - 1); e.Use(); }
                    else if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && joinIp.Length >= 7) { Session.Join(joinIp.Trim()); e.Use(); }
                }
            }
            if (!string.IsNullOrEmpty(Session.status)) UI.Text(new Rect(x, r.yMax - 50, rw, 40), Session.status, 20, Session.status.StartsWith("Could") ? UI.Bad : UI.Dim);
            if (UI.Button(new Rect(r.center.x - 160, r.yMax + 20, 320, 64), "BACK", false, true, 26)) { Session.Shutdown(); Session.status = ""; }
        }

        /// <summary>The hosting part of the Quickplay screen: who you play with and whether they're here.</summary>
        void HostPanel(float sx, float y)
        {
            UI.Text(new Rect(sx, y, 520, 30), "WI-FI GAME", 22, UI.Gold); y += 38;
            if (UI.Button(new Rect(sx, y, 255, 50), "TOGETHER", Session.together, true, 22)) Session.together = true;
            if (UI.Button(new Rect(sx + 265, y, 255, 50), "AGAINST", !Session.together, true, 22)) Session.together = false;
            y += 58;
            UI.Text(new Rect(sx, y, 520, 50), Session.together ? $"You both fight for the {Factions.All[qpFaction].name}." : $"They fight for the {Factions.All[qpEnemy].name}.", 18, Color.white, TextAnchor.UpperLeft, true); y += 44;
            UI.Text(new Rect(sx, y, 520, 30), $"Your address: {NetLink.LocalIp()}", 22, Color.white); y += 34;
            bool here = Session.peerName != null && Session.link != null && !Session.link.Closed;
            UI.Text(new Rect(sx, y, 520, 60), here ? $"{Session.peerName} has joined - start when ready" : "Waiting for the other device to join (same Wi-Fi, PLAY ON WI-FI > JOIN A GAME)...", 19, here ? UI.Good : UI.Dim, TextAnchor.UpperLeft, true);
        }

        /// <summary>The hosted battle's settings, ready for Game.StartBattle.</summary>
        BattleConfig HostConfig(BattleConfig cfg)
        {
            cfg.netHost = true;
            cfg.remoteTeam = Session.together ? 0 : 1;
            cfg.remoteName = Session.peerName ?? "Guest";
            cfg.botsPerTeam = Mathf.Max(cfg.botsPerTeam, 2);
            cfg.title = "Wi-Fi battle with " + cfg.remoteName;
            return cfg;
        }
    }
}
