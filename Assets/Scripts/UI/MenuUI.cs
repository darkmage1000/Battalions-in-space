using UnityEngine;

namespace BIS
{
    /// <summary>Main menu, Quickplay setup, options, how-to-play and credits. Conquest screens live in ConquestUI.</summary>
    public partial class MenuUI : MonoBehaviour
    {
        public enum Screen { None, Main, Quick, Options, Help, Credits, ConquestSetup, Conquest }
        public Screen screen = Screen.Main;
        // quickplay choices
        int qpMode;         // 0 land, 1 space
        int qpLand, qpSpace;
        int qpFaction = 0, qpEnemy = 1;
        int qpTickets = 200;
        int qpBots = 16;
        string lastResult;

        public void Show(Screen s) { screen = s; enabled = true; }
        public void Hide() { screen = Screen.None; }

        void OnGUI()
        {
            if (screen == Screen.None) return;
            UI.Begin();
            switch (screen)
            {
                case Screen.Main: Main(); break;
                case Screen.Quick: Quick(); break;
                case Screen.Options: Options(); break;
                case Screen.Help: Help(); break;
                case Screen.Credits: Credits(); break;
                case Screen.ConquestSetup: ConquestSetup(); break;
                case Screen.Conquest: ConquestMapScreen(); break;
            }
        }

        void Title(string sub = null)
        {
            float w = UI.W;
            UI.Text(new Rect(0, 40, w, 90), "BATTALIONS IN SPACE", 72, UI.Gold, TextAnchor.MiddleCenter);
            if (sub != null) UI.Text(new Rect(0, 128, w, 40), sub, 26, UI.Dim, TextAnchor.MiddleCenter);
        }

        void Main()
        {
            Title("Command posts, starfighters, capital ships and a galaxy to conquer");
            float w = UI.W;
            float x = w / 2 - 230, y = 260;
            if (UI.Button(new Rect(x, y, 460, 70), "QUICKPLAY", false, true, 30)) screen = Screen.Quick; y += 86;
            if (UI.Button(new Rect(x, y, 460, 70), "GALACTIC CONQUEST", false, true, 30)) screen = Screen.ConquestSetup; y += 86;
            if (UI.Button(new Rect(x, y, 460, 70), "OPTIONS", false, true, 30)) screen = Screen.Options; y += 86;
            if (UI.Button(new Rect(x, y, 460, 70), "HOW TO PLAY", false, true, 30)) screen = Screen.Help; y += 86;
            if (UI.Button(new Rect(x, y, 460, 70), "CREDITS", false, true, 30)) screen = Screen.Credits; y += 86;
            if (UI.Button(new Rect(x, y, 460, 70), "QUIT", false, true, 30)) Application.Quit();
            if (lastResult != null) UI.Text(new Rect(0, UI.H - 80, w, 40), lastResult, 22, UI.Dim, TextAnchor.MiddleCenter);
            UI.Text(new Rect(20, UI.H - 40, 600, 30), "v0.9 - an original game; all names, art and sound made for it", 16, UI.Dim);
        }

        void Quick()
        {
            Title("Quickplay");
            float w = UI.W;
            Rect r = new Rect(w / 2 - 820, 190, 1640, 800);
            UI.Box(r);
            float x = r.x + 30, y = r.y + 24;
            UI.Text(new Rect(x, y, 400, 30), "BATTLE", 22, UI.Gold); y += 36;
            if (UI.Button(new Rect(x, y, 240, 50), "LAND", qpMode == 0)) qpMode = 0;
            if (UI.Button(new Rect(x + 250, y, 240, 50), "SPACE", qpMode == 1)) qpMode = 1;
            y += 70;
            UI.Text(new Rect(x, y, 400, 30), "MAP", 22, UI.Gold); y += 36;
            if (qpMode == 0)
                for (int i = 0; i < Maps.Land.Length; i++) { if (UI.Button(new Rect(x, y, 490, 50), Maps.Land[i].name, qpLand == i)) qpLand = i; y += 56; }
            else
                for (int i = 0; i < Maps.Space.Length; i++) { if (UI.Button(new Rect(x, y, 490, 50), Maps.Space[i].name, qpSpace == i)) qpSpace = i; y += 56; }
            y += 10;
            string blurb = qpMode == 0 ? Maps.Land[qpLand].blurb : Maps.Space[qpSpace].blurb;
            UI.Text(new Rect(x, y, 490, 120), blurb, 19, UI.Dim, TextAnchor.UpperLeft, true);

            // factions
            float fx = r.x + 560; y = r.y + 24;
            UI.Text(new Rect(fx, y, 500, 30), "YOUR FACTION", 22, UI.Gold); y += 36;
            for (int i = 0; i < 4; i++)
            {
                var f = Factions.All[i];
                if (UI.Button(new Rect(fx, y, 500, 50), f.name, qpFaction == i, true, 24, f.second)) { qpFaction = i; if (qpEnemy == i) qpEnemy = (i + 1) % 4; }
                y += 56;
            }
            y += 6;
            UI.Text(new Rect(fx, y, 500, 30), "OPPONENT", 22, UI.Gold); y += 36;
            for (int i = 0; i < 4; i++)
            {
                var f = Factions.All[i];
                if (UI.Button(new Rect(fx, y, 500, 50), f.name, qpEnemy == i, i != qpFaction, 24, f.second)) qpEnemy = i;
                y += 56;
            }

            // settings + faction info
            float sx = r.x + 1100; y = r.y + 24;
            UI.Text(new Rect(sx, y, 500, 30), "SETTINGS", 22, UI.Gold); y += 40;
            qpTickets = Mathf.RoundToInt(UI.Slider(new Rect(sx, y, 520, 40), "Tickets", qpTickets, 50, 500, qpTickets.ToString()) / 10f) * 10; y += 50;
            qpBots = Mathf.RoundToInt(UI.Slider(new Rect(sx, y, 520, 40), "Per side", qpBots, 4, 32, qpBots.ToString())); y += 50;
            int d = Mathf.RoundToInt(UI.Slider(new Rect(sx, y, 520, 40), "Bot skill", Settings.Difficulty, 0, 2, Settings.DifficultyNames[Settings.Difficulty]));
            if (d != Settings.Difficulty) { Settings.Difficulty = d; Settings.Save(); }
            y += 70;
            var mf = Factions.All[qpFaction];
            UI.Text(new Rect(sx, y, 500, 30), mf.name, 24, mf.bolt); y += 32;
            UI.Text(new Rect(sx, y, 500, 80), mf.blurb, 18, UI.Dim, TextAnchor.UpperLeft, true); y += 70;
            string cls = ""; foreach (var c in mf.classes) cls += c.name + ", ";
            UI.Text(new Rect(sx, y, 500, 60), "Classes: " + cls.TrimEnd(',', ' '), 17, Color.white, TextAnchor.UpperLeft, true); y += 52;
            string hs = ""; foreach (var h in mf.heroes) hs += h.name + ", ";
            UI.Text(new Rect(sx, y, 500, 60), "Heroes: " + hs.TrimEnd(',', ' '), 17, Color.white, TextAnchor.UpperLeft, true); y += 52;
            UI.Text(new Rect(sx, y, 500, 60), $"Vehicles: {mf.tank.name}, {mf.transport.name}, {mf.air.name}", 17, Color.white, TextAnchor.UpperLeft, true); y += 52;
            string ss = ""; foreach (var s in mf.ships) ss += s.name + ", ";
            UI.Text(new Rect(sx, y, 500, 60), "Starships: " + ss.TrimEnd(',', ' '), 17, Color.white, TextAnchor.UpperLeft, true);

            if (UI.Button(new Rect(r.x + 30, r.yMax + 20, 260, 64), "BACK", false, true, 26)) screen = Screen.Main;
            if (UI.Button(new Rect(r.xMax - 360, r.yMax + 20, 360, 64), "START BATTLE", false, true, 30, new Color(0.2f, 0.6f, 0.3f)) || Ctl.Confirm)
            {
                var cfg = new BattleConfig
                {
                    space = qpMode == 1,
                    land = Maps.Land[qpLand],
                    spaceMap = Maps.Space[qpSpace],
                    factions = new[] { qpFaction, qpEnemy },
                    playerTeam = 0, tickets = qpTickets, botsPerTeam = qpBots, difficulty = Settings.Difficulty,
                };
                Game.I.StartBattle(cfg, r2 =>
                {
                    lastResult = r2.quit ? null : (r2.winner == 0 ? "Last battle: VICTORY - " : "Last battle: defeat - ") + r2.reason;
                    Game.I.ShowMenu();
                    screen = Screen.Quick;
                });
            }
        }

        void Options()
        {
            Title("Options");
            float w = UI.W;
            Rect r = new Rect(w / 2 - 420, 180, 840, 760);
            UI.Box(r);
            float x = r.x + 40, y = r.y + 24, rw = r.width - 80;
            Settings.MouseSens = UI.Slider(new Rect(x, y, rw, 44), "Mouse sensitivity", Settings.MouseSens, 0.3f, 6f, Settings.MouseSens.ToString("0.0")); y += 54;
            Settings.PadSens = UI.Slider(new Rect(x, y, rw, 44), "Gamepad sensitivity", Settings.PadSens, 0.5f, 8f, Settings.PadSens.ToString("0.0")); y += 54;
            Settings.Fov = UI.Slider(new Rect(x, y, rw, 44), "Field of view", Settings.Fov, 50, 95, Settings.Fov.ToString("0")); y += 54;
            Settings.Volume = UI.Slider(new Rect(x, y, rw, 44), "Volume", Settings.Volume, 0, 1, Mathf.RoundToInt(Settings.Volume * 100) + "%"); y += 54;
            Settings.MusicVolume = UI.Slider(new Rect(x, y, rw, 44), "Menu music", Settings.MusicVolume, 0, 1, Mathf.RoundToInt(Settings.MusicVolume * 100) + "%"); y += 54;
            Sfx.Music.volume = Settings.MusicVolume * 0.5f;
            Settings.Quality = Mathf.RoundToInt(UI.Slider(new Rect(x, y, rw, 44), "Graphics", Settings.Quality, 0, 2, new[] { "Low", "Medium", "High" }[Settings.Quality])); y += 54;
            Settings.InvertY = UI.Toggle(new Rect(x, y, rw, 50), "Invert mouse Y", Settings.InvertY); y += 56;
            Settings.InvertPad = UI.Toggle(new Rect(x, y, rw, 50), "Invert gamepad Y", Settings.InvertPad); y += 56;
            Settings.ShowFps = UI.Toggle(new Rect(x, y, rw, 50), "Show frame rate", Settings.ShowFps); y += 56;
            if (!Application.isMobilePlatform) { Settings.ForceTouch = UI.Toggle(new Rect(x, y, rw, 50), "Touch controls (for touch screens)", Settings.ForceTouch); y += 56; }
            bool full = UI.Toggle(new UnityEngine.Rect(x, y, rw, 50), "Fullscreen", UnityEngine.Screen.fullScreen);
            if (full != UnityEngine.Screen.fullScreen) UnityEngine.Screen.fullScreen = full;
            if (UI.Button(new Rect(r.center.x - 160, r.yMax + 20, 320, 64), "SAVE AND BACK", false, true, 26)) { Settings.Save(); screen = Screen.Main; }
        }

        void Help()
        {
            Title("How to play");
            float w = UI.W;
            Rect r = new Rect(w / 2 - 860, 180, 860, 760);
            UI.Box(r);
            string s =
                "<b>LAND BATTLES</b>\nBoth sides start with reinforcement tickets. Every soldier lost costs one. Stand inside a command post's ring with no enemies to neutralise it, then capture it. Holding more posts than the enemy makes their tickets bleed away. A side loses at 0 tickets, or when it cannot hold a single post.\n\n" +
                "Posts are spawn points, and the bigger ones spawn tanks, transports and gunships.\n\n" +
                "<b>HEROES</b>\nEarn points (kills 2, captures 4, vehicles 3) to unlock a hero. Press H to change into one on the spot, or pick one at your next spawn. Heroes slowly lose health; kills restore it.\n\n" +
                "<b>AWARDS</b>\nGet 6 kills with one class, or 4 in a single life, and that class carries its award weapon for the rest of the battle.\n\n" +
                "<b>SPACE BATTLES</b>\nLaunch from your hangar in a fighter, bomber, interceptor or support craft. Bombers' torpedoes wreck the enemy capital ship's systems once its shield generator is down. Fly into the enemy hangar, slow down and press E to land, then sabotage Life Support and the Reactor on foot. Win on tickets, by destroying the hull, or by knocking out every critical system.\n\n" +
                "<b>GALACTIC CONQUEST</b>\nMove one fleet a turn along the hyperlanes. Fleet against fleet is a space battle; invading a planet is a land battle. Planets pay credits each turn; spend them on fleets, class unlocks and battle bonuses.";
            UI.Text(new Rect(r.x + 26, r.y + 22, r.width - 52, r.height - 40), s, 19, Color.white, TextAnchor.UpperLeft, true);
            HUD.Controls(new Rect(r.xMax + 20, 180, 840, 760));
            if (UI.Button(new Rect(w / 2 - 160, 960, 320, 64), "BACK", false, true, 26)) screen = Screen.Main;
        }

        void Credits()
        {
            Title("Credits");
            float w = UI.W;
            Rect r = new Rect(w / 2 - 520, 200, 1040, 640);
            UI.Box(r);
            string s =
                "<b>Battalions in Space</b>\nAn original large-scale team shooter.\n\n" +
                "Design, code, models, sound and music: made from scratch for this game.\n" +
                "Every model is built in code from simple shapes; every sound and the march are synthesised at start-up.\n" +
                "No third-party art, audio or fonts beyond the font that ships with the Unity engine.\n\n" +
                "Built with Unity 6 (Unity Technologies).\n\n" +
                "Factions, characters, planets, ships and story are all original.\n" +
                "Mechanically inspired by the classic team-based battlefront shooters of the mid-2000s.\n\n" +
                "See CREDITS.md in the project for the full list.";
            UI.Text(new Rect(r.x + 30, r.y + 26, r.width - 60, r.height - 40), s, 21, Color.white, TextAnchor.UpperLeft, true);
            if (UI.Button(new Rect(w / 2 - 160, r.yMax + 20, 320, 64), "BACK", false, true, 26)) screen = Screen.Main;
        }
    }
}
