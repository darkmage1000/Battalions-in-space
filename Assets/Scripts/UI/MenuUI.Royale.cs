using UnityEngine;

namespace BIS
{
    /// <summary>Battle royale setup: race, classes on / off (and which class), how many players, bot skill.</summary>
    public partial class MenuUI
    {
        int brFaction, brClass;
        bool brClasses = true;
        int brPlayers = 100;
        public bool brHost;              // hosting a Wi-Fi battle royale
        bool brDuo = true;

        void RoyaleSetup()
        {
            Title(brHost ? "Host a Wi-Fi battle royale" : "Battle royale");
            float w = UI.W;
            Rect r = new Rect(w / 2 - 820, 190, 1640, 800);
            UI.Box(r);
            float x = r.x + 30, y = r.y + 24;
            var map = Maps.Royale;
            UI.Text(new Rect(x, y, 500, 30), map.name.ToUpper(), 24, UI.Gold); y += 38;
            UI.Text(new Rect(x, y, 500, 120), map.blurb, 19, UI.Dim, TextAnchor.UpperLeft, true); y += 110;
            UI.Text(new Rect(x, y, 500, 240),
                "• Pick where you land, glide down, grab what you find.\n• Weapons come in five rarities: grey, green, blue, purple, gold.\n• Medkits heal, shield cells give armour (H to use).\n• Blue chests hold better loot. The zone shrinks on a timer.\n• A Dreadnought Warlord lands a few minutes in: kill it for gold loot.\n• Vaults open with a key - one in ten gold finds has one.",
                18, Color.white, TextAnchor.UpperLeft, true); y += 250;
            UI.Text(new Rect(x, y, 500, 30), "PLAYERS", 22, UI.Gold); y += 40;
            brPlayers = Mathf.RoundToInt(UI.Slider(new Rect(x, y, 500, 40), "Players", brPlayers, 20, 100, brPlayers.ToString()) / 5f) * 5; y += 50;
            int d = Mathf.RoundToInt(UI.Slider(new Rect(x, y, 500, 40), "Bot skill", Settings.Difficulty, 0, 2, Settings.DifficultyNames[Settings.Difficulty]));
            if (d != Settings.Difficulty) { Settings.Difficulty = d; Settings.Save(); }
            y += 60;
            if (Application.isMobilePlatform && brPlayers > 70) UI.Text(new Rect(x, y, 500, 50), "On a phone, fewer players runs smoother.", 16, UI.Dim, TextAnchor.UpperLeft, true);

            // race
            float fx = r.x + 560; y = r.y + 24;
            UI.Text(new Rect(fx, y, 500, 30), "YOUR RACE", 22, UI.Gold); y += 36;
            for (int i = 0; i < 4; i++)
            {
                var f = Factions.All[i];
                if (UI.Button(new Rect(fx, y, 500, 50), f.name, brFaction == i, true, 24, f.second)) brFaction = i;
                y += 56;
            }
            var mf = Factions.All[brFaction];
            UI.Text(new Rect(fx, y, 500, 90), mf.blurb, 17, UI.Dim, TextAnchor.UpperLeft, true); y += 90;
            UI.Text(new Rect(fx, y, 500, 30), "CLASSES", 22, UI.Gold); y += 36;
            if (UI.Button(new Rect(fx, y, 245, 50), "ON", brClasses)) brClasses = true;
            if (UI.Button(new Rect(fx + 255, y, 245, 50), "OFF", !brClasses)) brClasses = false;
            y += 60;
            UI.Text(new Rect(fx, y, 500, 80), brClasses
                ? "Everyone picks a class: its health, speed, ability and equipment come with you. Weapons are still all found."
                : "Everyone lands the same: a sidearm and nothing else. Grenades and everything else are found.", 17, Color.white, TextAnchor.UpperLeft, true);

            // class
            float sx = r.x + 1100; y = r.y + 24;
            if (brClasses)
            {
                UI.Text(new Rect(sx, y, 500, 30), "YOUR CLASS", 22, UI.Gold); y += 36;
                for (int i = 0; i < 5; i++)
                {
                    var c = mf.classes[i];
                    if (UI.Button(new Rect(sx, y, 510, 50), c.name, brClass == i, true, 22)) brClass = i;
                    y += 56;
                }
                var cc = mf.classes[brClass];
                string sp = cc.special != Special.None ? cc.special.ToString() : "none";
                UI.Text(new Rect(sx, y, 510, 140), $"{cc.blurb}\nHealth {cc.hp:0}   Speed {cc.speed:0.0}\nAbility: {sp}   Equipment: {HUD.EquipName(cc.equip)}", 17, UI.Dim, TextAnchor.UpperLeft, true);
                y += 130;
            }
            if (brHost) RoyaleHostPanel(sx, y);

            bool guestHere = Net.Session.peerName != null && Net.Session.link != null && !Net.Session.link.Closed;
            if (UI.Button(new Rect(r.x + 30, r.yMax + 20, 260, 64), "BACK", false, true, 26))
            {
                if (brHost) { Net.Session.Shutdown(); brHost = false; screen = Screen.Multi; }
                else screen = Screen.Main;
            }
            if (UI.Button(new Rect(r.xMax - 360, r.yMax + 20, 360, 64), "DROP IN", false, !brHost || guestHere, 30, new Color(0.2f, 0.6f, 0.3f)) || (Ctl.Confirm && (!brHost || guestHere)))
            {
                var cfg = new BattleConfig
                {
                    land = Maps.Royale, royale = true, royalePlayers = brPlayers, royaleClasses = brClasses, royaleClass = brClass, royaleFaction = brFaction,
                    factions = new[] { brFaction, (brFaction + 1) % 4 }, difficulty = Settings.Difficulty, tickets = 1, botsPerTeam = 1,
                };
                bool hosted = brHost;
                if (hosted) RoyaleHostConfig(cfg);
                Game.I.StartBattle(cfg, r2 =>
                {
                    lastResult = r2.quit ? null : $"Last battle royale: {r2.reason}";
                    Game.I.ShowMenu();
                    if (hosted) { Net.NetHost.Reset(); brHost = Net.Session.IsHost; screen = brHost ? Screen.Royale : Screen.Multi; enabled = true; } else screen = Screen.Royale;
                });
            }
        }

        /// <summary>Wi-Fi battle royale: the guest as your duo partner, or one more player to beat.</summary>
        void RoyaleHostPanel(float x, float y)
        {
            UI.Text(new Rect(x, y, 500, 30), "WI-FI GAME", 22, UI.Gold); y += 36;
            if (UI.Button(new Rect(x, y, 245, 46), "DUO (TOGETHER)", brDuo, true, 18)) brDuo = true;
            if (UI.Button(new Rect(x + 255, y, 245, 46), "AGAINST", !brDuo, true, 18)) brDuo = false;
            y += 56;
            bool guestHere = Net.Session.peerName != null && Net.Session.link != null && !Net.Session.link.Closed;
            UI.Text(new Rect(x, y, 510, 60), guestHere ? $"{Net.Session.peerName} has joined." : $"Waiting for the other player... (your address: {Net.NetLink.LocalIp()})", 18, guestHere ? UI.Good : UI.Dim, TextAnchor.UpperLeft, true);
        }

        void RoyaleHostConfig(BattleConfig cfg)
        {
            cfg.netHost = true;
            cfg.royaleDuo = brDuo;
            cfg.remoteName = Net.Session.peerName ?? "Guest";
            cfg.title = "Wi-Fi battle royale with " + cfg.remoteName;
        }
    }
}
