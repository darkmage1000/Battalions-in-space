using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BIS
{
    /// <summary>One account on this device: its username (your name in battles and in Wi-Fi games), its own Conquest save and its record.</summary>
    [System.Serializable]
    public class Profile
    {
        public string id, name;
        public long created, lastPlayed;
        public int battles, wins, kills, deaths, captures, points;
    }

    /// <summary>
    /// Accounts are local to the device: each lives in persistentDataPath/profiles/&lt;id&gt;/ (profile.json + conquest.json).
    /// You make one before you can play; the last one used is picked again at start. Options stay per device.
    /// </summary>
    public static class Profiles
    {
        public const int MaxName = 16;
        public static readonly List<Profile> All = new List<Profile>();
        public static Profile Current { get; private set; }
        static string Root => Path.Combine(Application.persistentDataPath, "profiles");
        public static string Dir(Profile p) => Path.Combine(Root, p.id);
        public static string Name => Current != null ? Current.name : "You";

        public static void Load()
        {
            All.Clear();
            try
            {
                if (Directory.Exists(Root))
                    foreach (var d in Directory.GetDirectories(Root))
                    {
                        string f = Path.Combine(d, "profile.json");
                        if (!File.Exists(f)) continue;
                        var p = JsonUtility.FromJson<Profile>(File.ReadAllText(f));
                        if (p != null && !string.IsNullOrEmpty(p.name)) { p.id = Path.GetFileName(d); All.Add(p); }
                    }
            }
            catch (System.Exception e) { Debug.LogWarning("profiles: " + e.Message); }
            All.Sort((a, b) => b.lastPlayed.CompareTo(a.lastPlayed));
            string last = PlayerPrefs.GetString("bis_profile", "");
            Current = All.Find(p => p.id == last);
            if (Current != null) Net.Session.localName = Current.name;
        }

        /// <summary>Why this name can't be used, or null if it can.</summary>
        public static string Problem(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length < 2) return "Pick a name of at least 2 letters";
            if (name.Length > MaxName) return $"At most {MaxName} letters";
            foreach (char c in name) if (!(char.IsLetterOrDigit(c) || c == ' ' || c == '_' || c == '-')) return "Letters, numbers, spaces, _ and - only";
            if (All.Exists(p => string.Equals(p.name, name, System.StringComparison.OrdinalIgnoreCase))) return "There's already an account called that";
            return null;
        }

        public static Profile Create(string name)
        {
            name = (name ?? "").Trim();
            if (Problem(name) != null) return null;
            var p = new Profile { id = System.DateTime.UtcNow.Ticks.ToString(), name = name, created = System.DateTime.UtcNow.Ticks };
            Directory.CreateDirectory(Dir(p));
            // the very first account takes over a Conquest campaign saved before accounts existed
            string legacy = Path.Combine(Application.persistentDataPath, "conquest.json");
            if (All.Count == 0 && File.Exists(legacy)) { try { File.Move(legacy, Path.Combine(Dir(p), "conquest.json")); } catch { } }
            All.Insert(0, p);
            Select(p);
            return p;
        }

        public static void Select(Profile p)
        {
            Current = p;
            if (p == null) return;
            p.lastPlayed = System.DateTime.UtcNow.Ticks;
            Save(p);
            PlayerPrefs.SetString("bis_profile", p.id); PlayerPrefs.Save();
            Net.Session.localName = p.name;
        }

        public static void Delete(Profile p)
        {
            try { if (Directory.Exists(Dir(p))) Directory.Delete(Dir(p), true); } catch (System.Exception e) { Debug.LogWarning("delete profile: " + e.Message); }
            All.Remove(p);
            if (Current == p) { Current = null; PlayerPrefs.DeleteKey("bis_profile"); }
        }

        public static void Save(Profile p)
        {
            try { Directory.CreateDirectory(Dir(p)); File.WriteAllText(Path.Combine(Dir(p), "profile.json"), JsonUtility.ToJson(p, true)); }
            catch (System.Exception e) { Debug.LogWarning("save profile: " + e.Message); }
        }

        /// <summary>A battle that was played to the end goes on the account's record.</summary>
        public static void Record(Battle b, bool won)
        {
            var p = Current;
            if (p == null || b == null || b.player == null) return;
            p.battles++; if (won) p.wins++;
            p.kills += b.player.kills; p.deaths += b.player.deaths; p.captures += b.player.captures; p.points += b.player.points;
            p.lastPlayed = System.DateTime.UtcNow.Ticks;
            Save(p);
        }
    }
}
