using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    public enum LootKind : byte { Weapon, Medkit, Shield, Ammo, Gear, Key, Tag, Revive, Station }   // Tag = a fallen duo mate's ID tag; Revive / Station are channels, never on the ground

    /// <summary>Loot rarity: colour, name and how much better a weapon of that tier is.</summary>
    public static class Tiers
    {
        public const int Count = 5;
        public static readonly string[] Names = { "Common", "Uncommon", "Rare", "Epic", "Legendary" };
        public static readonly Color[] Colors =
        {
            new Color(0.82f, 0.84f, 0.86f), new Color(0.35f, 0.95f, 0.4f), new Color(0.3f, 0.6f, 1f), new Color(0.78f, 0.38f, 1f), new Color(1f, 0.74f, 0.18f),
        };
        public static readonly float[] Damage = { 1f, 1.1f, 1.22f, 1.36f, 1.52f };
        public static Color Of(int t) => Colors[Mathf.Clamp(t, 0, Count - 1)];
    }

    /// <summary>One thing lying on the ground (or in a chest's spill) in a battle royale.</summary>
    public class LootItem
    {
        public int id;
        public LootKind kind;
        public int tier;
        public WeaponDef weapon;
        public int count = 1;
        public Vector3 pos;
        public GameObject go;
        public bool taken;
        public long cell;
        public Unit claimedBy;            // a bot on its way to pick it up
        public Unit droppedBy; public float dropLock;   // duos: dropped for a mate - the dropper doesn't auto-pick it up again for a while
        public float claimUntil;

        public string Name
        {
            get
            {
                switch (kind)
                {
                    case LootKind.Weapon: return weapon.name;
                    case LootKind.Medkit: return count > 1 ? $"{count} Medkits" : "Medkit";
                    case LootKind.Shield: return count > 1 ? $"{count} Shield Cells" : "Shield Cell";
                    case LootKind.Ammo: return "Ammo Pack";
                    case LootKind.Gear: return "Equipment Pack";
                    case LootKind.Tag: return "ID Tag";
                    default: return "Vault Key";
                }
            }
        }
    }

    /// <summary>A supply chest: open it (E) and loot spills out, better than what lies on the ground.</summary>
    public class LootChest
    {
        public Vector3 pos;
        public Quaternion rot;
        public GameObject go;
        public bool opened;
        public int bonus;                  // tier bonus of what spills out
        public Unit claimedBy; public float claimUntil;
    }

    /// <summary>What can be found: weapons by tier (copies of the four factions' real guns, made stronger by rarity) and supplies.</summary>
    public static class LootTable
    {
        // (which gun, lowest tier, highest tier, weight)
        static readonly (System.Func<FactionDef, WeaponDef> get, int lo, int hi, float w)[] Guns =
        {
            (f => Primary(f, Role.Assault), 0, 2, 3f),
            (f => Secondary(f), 0, 1, 2.2f),
            (f => Primary(f, Role.Specialist), 0, 2, 2.4f),
            (f => Primary(f, Role.Engineer), 0, 2, 2f),
            (f => Primary(f, Role.Marksman), 1, 3, 1.4f),
            (f => Primary(f, Role.Heavy), 1, 3, 1.1f),
            (f => Award(f, Role.Assault), 2, 4, 2f),
            (f => Award(f, Role.Specialist), 2, 4, 1.6f),
            (f => Award(f, Role.Engineer), 2, 4, 1.4f),
            (f => Award(f, Role.Marksman), 3, 4, 1f),
            (f => Award(f, Role.Heavy), 3, 4, 0.8f),
            (f => HeroGun(f, HeroKind.Juggernaut), 3, 4, 0.8f),
            (f => HeroGun(f, HeroKind.Gunslinger), 3, 4, 0.8f),
            (f => HeroGun(f, HeroKind.Mystic), 4, 4, 0.5f),
        };

        static WeaponDef Primary(FactionDef f, Role r) => f.classes[(int)r].primary;
        static WeaponDef Award(FactionDef f, Role r) => f.classes[(int)r].award;
        public static WeaponDef Secondary(FactionDef f) { foreach (var c in f.classes) if (c.secondary != null && c.secondary.kind == WKind.Pistol) return c.secondary; return f.classes[0].secondary; }
        static WeaponDef HeroGun(FactionDef f, HeroKind k) { foreach (var h in f.heroes) if (h.kind == k) return h.weapon; return f.classes[0].award; }

        /// <summary>A gun of this tier: a copy of a faction weapon, stronger and cooler-running the rarer it is.</summary>
        public static WeaponDef Weapon(int tier, System.Random r)
        {
            float total = 0;
            foreach (var g in Guns) if (tier >= g.lo && tier <= g.hi) total += g.w;
            float pick = (float)r.NextDouble() * total;
            int gun = 0;
            for (int i = 0; i < Guns.Length; i++)
            {
                var g = Guns[i];
                if (tier < g.lo || tier > g.hi) continue;
                pick -= g.w; gun = i;
                if (pick <= 0) break;
            }
            return Make(gun, r.Next(Factions.All.Length), tier);
        }

        /// <summary>Loot gun number `gun` of faction `faction` at a tier (the same on both Wi-Fi devices).</summary>
        public static WeaponDef Make(int gun, int faction, int tier)
        {
            gun = Mathf.Clamp(gun, 0, Guns.Length - 1);
            var w = Tiered(Guns[gun].get(Factions.Get(faction)), Mathf.Clamp(tier, 0, Tiers.Count - 1));
            w.lootGun = gun; w.lootFaction = faction;
            return w;
        }

        public static WeaponDef Tiered(WeaponDef src, int tier)
        {
            var w = src.Copy();
            w.tier = tier;
            w.damage *= Tiers.Damage[tier];
            w.splashDmg *= Tiers.Damage[tier];
            if (w.heatPerShot > 0) w.heatPerShot *= Mathf.Pow(0.93f, tier);
            if (w.clip > 0) { w.ammo = Mathf.RoundToInt(w.ammo * (1 + tier * 0.15f)); w.reload *= Mathf.Pow(0.94f, tier); }
            w.spread *= Mathf.Pow(0.92f, tier);
            if (w.kind == WKind.Rocket)
            {
                // a launcher you found should feel like one: faster rockets, a wider blast, more of them per reload as it gets rarer
                w.speed *= 1.7f;
                w.splash = Mathf.Max(w.splash, 6f) + tier * 0.35f;
                w.clip = tier >= 4 ? 3 : tier >= 2 ? 2 : 1;
                w.rate = Mathf.Max(w.rate, 1.1f);
                w.proximity = 2.2f;      // bursts beside a soldier it would otherwise fly past
            }
            return w;
        }

        /// <summary>Rarity roll: base odds, pushed up by `bonus` steps (chests, compounds, the vault).</summary>
        public static int RollTier(System.Random r, int bonus = 0)
        {
            float x = (float)r.NextDouble();
            int t = x < 0.44f ? 0 : x < 0.73f ? 1 : x < 0.89f ? 2 : x < 0.975f ? 3 : 4;
            return Mathf.Clamp(t + bonus, 0, Tiers.Count - 1);
        }

        public static LootKind RollKind(System.Random r)
        {
            float x = (float)r.NextDouble();
            return x < 0.52f ? LootKind.Weapon : x < 0.66f ? LootKind.Medkit : x < 0.8f ? LootKind.Shield : x < 0.91f ? LootKind.Ammo : LootKind.Gear;
        }

        /// <summary>How much a bot (or the HUD) values a gun: damage per second at its usual range, by tier.</summary>
        public static float Value(WeaponDef w)
        {
            if (w == null) return 0;
            float dps = w.damage * w.pellets * w.rate + w.splashDmg * w.rate * 0.4f;
            if (w.kind == WKind.Sniper) dps *= 2.2f;
            if (w.kind == WKind.Rocket) dps *= 0.55f;
            if (w.kind == WKind.Repair) return 0;
            return dps;
        }
    }

    /// <summary>Meshes for loot on the ground: a gun shape, a crate or a vial, with a coloured beam for the rarity.</summary>
    public static class LootModels
    {
        static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

        public static Mesh For(LootItem it)
        {
            if (it.kind == LootKind.Tag) return Tag();
            string k = it.kind == LootKind.Weapon ? $"w{it.weapon.kind}_{it.tier}_{it.weapon.lootFaction}_{(it.weapon.splash > 0 ? 1 : 0)}" : $"{it.kind}_{it.tier}";
            if (cache.TryGetValue(k, out var m) && m) return m;
            var kit = new MeshKit();
            Color tc = Tiers.Of(it.tier), glow = MeshKit.Glow(tc * 0.85f);
            Color metal = new Color(0.24f, 0.25f, 0.28f), trim = Color.Lerp(metal, tc, 0.45f);
            switch (it.kind)
            {
                case LootKind.Weapon:
                    var q = Quaternion.Euler(0, 0, 90);   // lying on its side
                    // the real gun, lying on its side at 1.4x so it reads from a distance, accent = its rarity
                    if (GunModels.Add(kit, it.weapon, it.weapon.lootFaction, tc, new Vector3(0, 0.14f, -0.25f), Quaternion.Euler(0, 0, 80), 1.4f) > 0) break;
                    switch (it.weapon.kind)
                    {
                        case WKind.Sniper: kit.Box(new Vector3(0, 0.08f, 0), new Vector3(0.1f, 0.16f, 1.4f), metal); kit.Cyl(new Vector3(0, 0.2f, 0.1f), 0.06f, 0.5f, Quaternion.Euler(90, 0, 0), trim, 6); break;
                        case WKind.Rocket: kit.Cyl(new Vector3(0, 0.15f, 0), 0.15f, 1.6f, Quaternion.Euler(90, 0, 0), trim, 8); break;
                        case WKind.Shotgun: kit.Box(new Vector3(0, 0.1f, 0), new Vector3(0.18f, 0.18f, 1f), metal); kit.Box(new Vector3(0, 0.2f, 0.05f), new Vector3(0.2f, 0.06f, 0.8f), trim); break;
                        case WKind.Pistol: kit.Box(new Vector3(0, 0.08f, 0), new Vector3(0.1f, 0.16f, 0.5f), metal); kit.Box(new Vector3(0, 0.02f, -0.15f), new Vector3(0.08f, 0.2f, 0.12f), trim); break;
                        case WKind.Beam: kit.Sphere(new Vector3(0, 0.25f, 0), Vector3.one * 0.4f, Quaternion.identity, glow, 8, 5); kit.Box(new Vector3(0, 0.08f, 0), new Vector3(0.14f, 0.16f, 0.8f), metal); break;
                        default: kit.Box(new Vector3(0, 0.09f, 0), new Vector3(0.14f, 0.18f, 1.1f), metal); kit.Box(new Vector3(0, 0.2f, 0.15f), new Vector3(0.06f, 0.05f, 0.5f), trim); kit.Box(new Vector3(0, -0.02f, -0.2f), new Vector3(0.08f, 0.18f, 0.12f), trim); break;
                    }
                    kit.Box(new Vector3(0, 0.12f, 0.35f), new Vector3(0.05f, 0.05f, 0.12f), glow);
                    break;
                case LootKind.Medkit:
                    kit.Box(new Vector3(0, 0.2f, 0), new Vector3(0.6f, 0.4f, 0.45f), new Color(0.92f, 0.92f, 0.9f));
                    kit.Box(new Vector3(0, 0.41f, 0), new Vector3(0.36f, 0.03f, 0.1f), new Color(0.9f, 0.15f, 0.15f));
                    kit.Box(new Vector3(0, 0.41f, 0), new Vector3(0.1f, 0.03f, 0.3f), new Color(0.9f, 0.15f, 0.15f));
                    break;
                case LootKind.Shield:
                    kit.Cyl(new Vector3(0, 0.3f, 0), 0.16f, 0.6f, Quaternion.identity, metal, 8);
                    kit.Cyl(new Vector3(0, 0.3f, 0), 0.13f, 0.5f, Quaternion.identity, MeshKit.Glow(new Color(0.3f, 0.7f, 1f)), 8);
                    break;
                case LootKind.Ammo:
                    kit.Box(new Vector3(0, 0.17f, 0), new Vector3(0.6f, 0.34f, 0.38f), new Color(0.36f, 0.42f, 0.28f));
                    kit.Box(new Vector3(0, 0.35f, 0), new Vector3(0.62f, 0.04f, 0.4f), new Color(0.85f, 0.7f, 0.2f));
                    break;
                case LootKind.Gear:
                    kit.Sphere(new Vector3(-0.15f, 0.15f, 0), Vector3.one * 0.28f, Quaternion.identity, new Color(0.3f, 0.36f, 0.24f), 8, 5);
                    kit.Sphere(new Vector3(0.15f, 0.15f, 0.05f), Vector3.one * 0.28f, Quaternion.identity, new Color(0.3f, 0.36f, 0.24f), 8, 5);
                    kit.Box(new Vector3(0, 0.32f, 0), new Vector3(0.1f, 0.08f, 0.1f), glow);
                    break;
                case LootKind.Key:
                    kit.Cyl(new Vector3(0, 0.35f, 0), 0.22f, 0.06f, Quaternion.Euler(90, 0, 0), MeshKit.Glow(new Color(1f, 0.8f, 0.3f)), 12);
                    kit.Box(new Vector3(0, 0.35f, 0.4f), new Vector3(0.08f, 0.08f, 0.6f), MeshKit.Glow(new Color(1f, 0.8f, 0.3f)));
                    kit.Box(new Vector3(0, 0.28f, 0.62f), new Vector3(0.08f, 0.14f, 0.06f), MeshKit.Glow(new Color(1f, 0.8f, 0.3f)));
                    break;
            }
            // a ring on the ground and a beam of light whose height grows with the rarity
            float bh = 0.5f + it.tier * 0.55f + (it.kind == LootKind.Key ? 2.5f : 0);
            kit.Ring(new Vector3(0, 0.03f, 0), 0.55f, 0.68f, glow, 16);
            kit.Box(new Vector3(0, bh / 2, 0), new Vector3(0.05f, bh, 0.05f), MeshKit.Glow(tc * 0.55f));
            m = kit.Build("loot_" + k);
            cache[k] = m;
            return m;
        }

        static Mesh tag;
        /// <summary>A fallen duo mate's ID tag: two glowing plates on a chain, under a green beam.</summary>
        static Mesh Tag()
        {
            if (tag) return tag;
            var k = new MeshKit();
            Color g = MeshKit.Glow(new Color(0.4f, 1f, 0.55f)), plate = new Color(0.78f, 0.8f, 0.82f);
            k.Box(new Vector3(-0.08f, 0.32f, 0), new Vector3(0.3f, 0.42f, 0.03f), plate);
            k.Box(new Vector3(0.1f, 0.3f, 0.05f), new Vector3(0.3f, 0.42f, 0.03f), plate);
            k.Box(new Vector3(-0.08f, 0.32f, -0.02f), new Vector3(0.18f, 0.06f, 0.02f), g);
            k.Box(new Vector3(0.1f, 0.3f, 0.07f), new Vector3(0.18f, 0.06f, 0.02f), g);
            k.Ring(new Vector3(0, 0.03f, 0), 0.6f, 0.75f, g, 16);
            k.Box(new Vector3(0, 2.2f, 0), new Vector3(0.07f, 4.4f, 0.07f), MeshKit.Glow(new Color(0.4f, 1f, 0.55f) * 0.6f));
            tag = k.Build("loot_tag");
            return tag;
        }

        static Mesh station, stationUsed;
        /// <summary>A revive station: a round pad, a pylon at the back with a glowing emitter and a green cross, and (while
        /// unused) a tall beam that shows from far away. Used: dark, no beam.</summary>
        public static Mesh Station(bool used)
        {
            if (used ? stationUsed : station) return used ? stationUsed : station;
            var k = new MeshKit();
            Color metal = new Color(0.26f, 0.28f, 0.3f), light = new Color(0.62f, 0.66f, 0.7f);
            Color g = used ? new Color(0.16f, 0.2f, 0.18f) : MeshKit.Glow(new Color(0.35f, 1f, 0.5f));
            k.Cyl(new Vector3(0, 0.12f, 0), 1.7f, 0.24f, Quaternion.identity, metal, 16);
            k.Ring(new Vector3(0, 0.25f, 0), 1.2f, 1.45f, g, 20);
            k.Cyl(new Vector3(0, 0.26f, 0), 0.5f, 0.04f, Quaternion.identity, g, 12);
            k.Box(new Vector3(0, 1.3f, -1.05f), new Vector3(0.6f, 2.6f, 0.4f), metal);
            k.Box(new Vector3(0, 2.7f, -1.05f), new Vector3(0.9f, 0.22f, 0.55f), light);
            k.Box(new Vector3(0, 1.75f, -0.83f), new Vector3(0.42f, 0.12f, 0.04f), g);
            k.Box(new Vector3(0, 1.75f, -0.83f), new Vector3(0.12f, 0.42f, 0.04f), g);
            k.Box(new Vector3(0, 2.45f, -0.6f), new Vector3(0.3f, 0.14f, 0.5f), metal);
            k.Sphere(new Vector3(0, 2.4f, -0.32f), Vector3.one * 0.22f, Quaternion.identity, g, 8, 5);
            if (!used) k.Box(new Vector3(0, 18f, 0), new Vector3(0.08f, 36f, 0.08f), MeshKit.Glow(new Color(0.35f, 1f, 0.5f) * 0.5f));
            var m = k.Build(used ? "station_used" : "station");
            if (used) stationUsed = m; else station = m;
            return m;
        }

        static Mesh chest, chestOpen;
        public static Mesh Chest(bool open)
        {
            if (open ? chestOpen : chest) return open ? chestOpen : chest;
            var k = new MeshKit();
            Color body = new Color(0.32f, 0.36f, 0.42f), band = new Color(0.85f, 0.65f, 0.18f);
            k.Box(new Vector3(0, 0.38f, 0), new Vector3(1.3f, 0.76f, 0.8f), body);
            k.Box(new Vector3(0, 0.4f, 0), new Vector3(1.34f, 0.1f, 0.84f), band);
            k.Box(new Vector3(-0.45f, 0.38f, 0), new Vector3(0.08f, 0.8f, 0.84f), band);
            k.Box(new Vector3(0.45f, 0.38f, 0), new Vector3(0.08f, 0.8f, 0.84f), band);
            if (!open)
            {
                k.Box(new Vector3(0, 0.85f, 0), new Vector3(1.3f, 0.18f, 0.8f), body);
                k.Box(new Vector3(0, 0.6f, 0.41f), new Vector3(0.18f, 0.18f, 0.04f), MeshKit.Glow(new Color(0.4f, 0.9f, 1f)));
                k.Box(new Vector3(0, 1.2f, 0), new Vector3(0.05f, 0.7f, 0.05f), MeshKit.Glow(new Color(0.4f, 0.9f, 1f) * 0.6f));
            }
            else k.Box(new Vector3(0, 0.9f, -0.5f), new Vector3(1.3f, 0.6f, 0.12f), body);
            var m = k.Build(open ? "chestOpen" : "chest");
            if (open) chestOpen = m; else chest = m;
            return m;
        }
    }
}
