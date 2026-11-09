using UnityEngine;

namespace BIS
{
    public enum WKind { Rifle, Shotgun, Sniper, Rocket, Pistol, Repair, Blade, Beam, Repeater, Cannon, ShipLaser, Missile, Torpedo, Turret, Grenade }
    public enum Role { Assault, Heavy, Marksman, Engineer, Specialist }
    public enum Equip { None, Grenade, Mine, Detpack, Supply }
    public enum Special { None, Jetpack, Frenzy, Cloak, Rally }
    public enum HeroKind { Blade, Gunslinger, Juggernaut, Mystic }
    public enum VKind { Tank, Transport, Air, Fighter, Bomber, Interceptor, Support, Dropship }
    public enum Body { Human, Reptile, Machine, Slender }
    public enum DmgType { Bolt, Explosive, Melee, Psi, Crush, Environment }

    public class WeaponDef
    {
        public string name;
        public WKind kind;
        public float damage = 30, rate = 4, spread = 1f, speed = 140, range = 160;
        public float heatPerShot = 0.07f;       // 0 = uses clip/ammo instead of heat
        public int clip, ammo;
        public float reload = 2f;
        public float splash, splashDmg;
        public int pellets = 1;
        public float zoomFov;
        public bool lockOn, guided;
        public float gravity;
        public float vsVehicle = 0.15f;          // multiplier against vehicles and ship systems
        public float vsInfantry = 1f;
        public float boltLen = 2.4f, boltWidth = 0.13f;
        public string sound = "blaster";
        public float pitch = 1f;
        public bool auto = true;                 // hold to fire
        public int tier = -1;                    // battle royale loot rarity (0 common .. 4 legendary); -1 = issued, not found
        public int lootGun = -1, lootFaction;
        public float proximity;                  // rockets: burst when passing this close to an enemy soldier (royale launchers)    // battle royale: which loot gun of which faction (so a Wi-Fi guest can rebuild it)

        public WeaponDef Copy() => (WeaponDef)MemberwiseClone();

        /// <summary>The distance the gun is good at: aim assist and auto-fire stop beyond it.</summary>
        public float EffectiveRange
        {
            get
            {
                switch (kind)
                {
                    case WKind.Rifle: return 90;
                    case WKind.Repeater: return splash > 0 ? 80 : 60;
                    case WKind.Pistol: return 55;
                    case WKind.Shotgun: return 22;
                    case WKind.Sniper: return 320;
                    case WKind.Rocket: return 160;
                    case WKind.Blade: return 5;
                    case WKind.Beam: return range;
                    case WKind.Cannon: return 170;
                    case WKind.ShipLaser: return Mathf.Min(range, 420);
                    case WKind.Missile: return Mathf.Min(range, 600);
                    case WKind.Torpedo: return 500;
                    case WKind.Repair: return 0;
                    default: return Mathf.Min(range, 120);
                }
            }
        }
        public bool UsesHeat => heatPerShot > 0;

        /// <summary>What kind of gun this is, in plain words (shown on pickups and in the weapon panel).</summary>
        public string TypeName
        {
            get
            {
                switch (kind)
                {
                    case WKind.Rifle: return "Assault rifle";
                    case WKind.Repeater: return splash > 0 ? "Heavy repeater" : "Carbine";
                    case WKind.Pistol: return "Pistol";
                    case WKind.Shotgun: return "Shotgun";
                    case WKind.Sniper: return "Sniper rifle";
                    case WKind.Rocket: return guided ? "Guided launcher" : "Rocket launcher";
                    case WKind.Beam: return "Beam projector";
                    case WKind.Blade: return "Blade";
                    case WKind.Repair: return "Repair tool";
                    case WKind.Grenade: return "Grenade";
                    default: return kind.ToString();
                }
            }
        }
        public string RangeWord { get { float r = EffectiveRange; return r <= 25 ? "close" : r <= 65 ? "short" : r <= 110 ? "medium" : r <= 200 ? "long" : "very long"; } }
        /// <summary>"Assault rifle - medium range - 26 dmg - 5 shots/s"</summary>
        public string InfoLine
        {
            get
            {
                string dmg = pellets > 1 ? $"{damage:0} x{pellets} pellets" : $"{damage:0} dmg";
                string r = rate >= 1.5f ? $"{rate:0.#} shots/s" : $"a shot every {1f / Mathf.Max(0.01f, rate):0.#} s";
                return kind == WKind.Repair ? "Repair tool - fixes vehicles" : $"{TypeName} - {RangeWord} range - {dmg} - {r}" + (splash > 0 ? " - blast" : "");
            }
        }
        /// <summary>Hand-held guns hit instantly along the aim line (a tracer is drawn); rockets, grenades and vehicle weapons fly.</summary>
        public bool Hitscan => kind == WKind.Rifle || kind == WKind.Repeater || kind == WKind.Pistol || kind == WKind.Shotgun || kind == WKind.Sniper;
        public float Dps => damage * pellets * rate;
    }

    public class ClassDef
    {
        public string name, blurb;
        public Role role;
        public float hp = 300, speed = 6.2f;
        public WeaponDef primary, secondary, award;
        public string awardName;
        public Equip equip;
        public int equipCount;
        public Special special;
        public bool space;                       // pilot / marine
        public bool pilot;
    }

    public class HeroDef
    {
        public string name, title, blurb;
        public HeroKind kind;
        public float hp = 1200, speed = 7f;
        public WeaponDef weapon;
        public string abilityName;
        public Color main, accent, glow;
        public float scale = 1.12f;
    }

    public class VehicleDef
    {
        public string name, blurb;
        public VKind kind;
        public float hp = 2000, speed = 14, turn = 70, boost = 1.6f;
        public WeaponDef primary, secondary, gunner;
        public int seats = 1;
        public float respawn = 35;
        public bool IsShip => kind >= VKind.Fighter;
        public bool IsGround => kind == VKind.Tank || kind == VKind.Transport;
        public bool Flies => kind >= VKind.Air;
    }

    public class FactionDef
    {
        public int id;
        public string name, shortName, adjective, blurb, capitalName, motto;
        public Color main, second, accent, bolt, skin;
        public Body body;
        public ClassDef[] classes;               // 5 land classes in Role order
        public ClassDef pilot, marine;           // space on-foot classes
        public HeroDef[] heroes;                 // 4
        public VehicleDef tank, transport, air;
        public VehicleDef[] ships;               // fighter, bomber, interceptor, support
        public string[] names;                   // bot call-signs
        public float dmgMul = 1, rateMul = 1, hpMul = 1, speedMul = 1;

        public VehicleDef Vehicle(VKind k)
        {
            switch (k)
            {
                case VKind.Tank: return tank;
                case VKind.Transport: return transport;
                case VKind.Air: return air;
                default: return ships[(int)k - (int)VKind.Fighter];
            }
        }
    }
}
