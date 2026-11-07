using UnityEngine;

namespace BIS
{
    /// <summary>Weapon templates. Faction tables copy and rename them.</summary>
    public static class W
    {
        public static WeaponDef Rifle(string n) => new WeaponDef { name = n, kind = WKind.Rifle, damage = 26, rate = 5f, spread = 1.1f, speed = 150, heatPerShot = 0.06f, range = 180, sound = "blaster" };
        public static WeaponDef RifleAward(string n) => new WeaponDef { name = n, kind = WKind.Rifle, damage = 33, rate = 5.6f, spread = 0.6f, speed = 170, heatPerShot = 0.042f, range = 200, sound = "blaster", pitch = 0.9f };
        public static WeaponDef Rocket(string n) => new WeaponDef { name = n, kind = WKind.Rocket, damage = 240, rate = 0.6f, spread = 0.2f, speed = 62, heatPerShot = 0, clip = 1, ammo = 6, reload = 2.0f, splash = 4.5f, splashDmg = 170, vsVehicle = 1f, lockOn = true, range = 250, sound = "rocket", boltLen = 1.2f, boltWidth = 0.35f, auto = false };
        public static WeaponDef RocketAward(string n) { var w = Rocket(n); w.guided = true; w.damage = 300; w.ammo = 9; w.speed = 70; return w; }
        public static WeaponDef Sniper(string n) => new WeaponDef { name = n, kind = WKind.Sniper, damage = 190, rate = 0.8f, spread = 0, speed = 650, heatPerShot = 0, clip = 4, ammo = 16, reload = 3f, zoomFov = 16, range = 500, sound = "sniper", boltLen = 6, boltWidth = 0.12f, auto = false };
        public static WeaponDef SniperAward(string n) { var w = Sniper(n); w.damage = 305; w.rate = 1.15f; w.clip = 5; w.ammo = 20; return w; }
        public static WeaponDef Shotgun(string n) => new WeaponDef { name = n, kind = WKind.Shotgun, damage = 21, pellets = 8, rate = 1.2f, spread = 5.5f, speed = 160, heatPerShot = 0, clip = 6, ammo = 36, reload = 2.2f, range = 40, sound = "shotgun", boltLen = 1.4f, auto = false, vsVehicle = 0.2f };
        public static WeaponDef ShotgunAward(string n) { var w = Shotgun(n); w.damage = 27; w.pellets = 9; w.rate = 1.7f; w.spread = 4.5f; w.clip = 8; w.ammo = 48; return w; }
        public static WeaponDef Repeater(string n) => new WeaponDef { name = n, kind = WKind.Repeater, damage = 15, rate = 10f, spread = 2.4f, speed = 150, heatPerShot = 0.035f, range = 120, sound = "repeater", boltLen = 1.8f, boltWidth = 0.1f };
        public static WeaponDef RepeaterAward(string n) { var w = Repeater(n); w.damage = 19; w.splash = 1.6f; w.splashDmg = 14; w.heatPerShot = 0.028f; return w; }
        public static WeaponDef Pistol(string n) => new WeaponDef { name = n, kind = WKind.Pistol, damage = 42, rate = 3.2f, spread = 0.6f, speed = 150, heatPerShot = 0.11f, range = 120, sound = "pistol", auto = false };
        public static WeaponDef Repair(string n) => new WeaponDef { name = n, kind = WKind.Repair, damage = 70, rate = 5, range = 6, heatPerShot = 0, sound = "repair" };
        public static WeaponDef Blade(string n) => new WeaponDef { name = n, kind = WKind.Blade, damage = 105, rate = 2.4f, range = 3.4f, heatPerShot = 0, sound = "blade", vsVehicle = 0.6f };
        public static WeaponDef TwinGuns(string n) => new WeaponDef { name = n, kind = WKind.Pistol, damage = 50, rate = 6.5f, spread = 0.5f, speed = 190, heatPerShot = 0.03f, range = 160, sound = "pistol", pitch = 0.9f, vsVehicle = 0.3f };
        public static WeaponDef HeavyGun(string n) => new WeaponDef { name = n, kind = WKind.Repeater, damage = 32, rate = 9, spread = 1.6f, speed = 160, heatPerShot = 0.022f, splash = 1.5f, splashDmg = 22, range = 170, sound = "heavy", boltWidth = 0.22f, vsVehicle = 0.4f };
        public static WeaponDef Beam(string n) => new WeaponDef { name = n, kind = WKind.Beam, damage = 190, rate = 10, range = 18, heatPerShot = 0.012f, sound = "zap", vsVehicle = 0.5f };

        // vehicles
        public static WeaponDef TankCannon(string n) => new WeaponDef { name = n, kind = WKind.Cannon, damage = 300, rate = 0.75f, spread = 0.3f, speed = 120, heatPerShot = 0.3f, splash = 6, splashDmg = 210, vsVehicle = 1, range = 300, sound = "cannon", boltLen = 3.5f, boltWidth = 0.4f, auto = true };
        public static WeaponDef Mg(string n) => new WeaponDef { name = n, kind = WKind.Repeater, damage = 26, rate = 8, spread = 1.4f, speed = 170, heatPerShot = 0.035f, vsVehicle = 0.3f, range = 200, sound = "repeater" };
        public static WeaponDef TwinBlaster(string n) => new WeaponDef { name = n, kind = WKind.Rifle, damage = 38, rate = 5.5f, spread = 0.8f, speed = 170, heatPerShot = 0.045f, vsVehicle = 0.45f, range = 220, sound = "blaster2", boltWidth = 0.2f };
        public static WeaponDef RocketPod(string n) => new WeaponDef { name = n, kind = WKind.Rocket, damage = 190, rate = 1.2f, spread = 0.8f, speed = 75, heatPerShot = 0.24f, splash = 4, splashDmg = 140, vsVehicle = 1, range = 250, sound = "rocket", boltLen = 1.2f, boltWidth = 0.3f };
        public static WeaponDef AirLaser(string n) => new WeaponDef { name = n, kind = WKind.ShipLaser, damage = 42, rate = 7, spread = 0.6f, speed = 260, heatPerShot = 0.032f, vsVehicle = 0.6f, range = 350, sound = "laser", boltLen = 4, boltWidth = 0.2f };
        public static WeaponDef AirMissile(string n) => new WeaponDef { name = n, kind = WKind.Missile, damage = 300, rate = 0.8f, spread = 0, speed = 90, heatPerShot = 0.38f, splash = 5, splashDmg = 200, vsVehicle = 1, lockOn = true, range = 400, sound = "rocket", boltLen = 1.4f, boltWidth = 0.35f };

        // ships
        public static WeaponDef ShipLaser(string n, float dmg = 45, float rate = 6) => new WeaponDef { name = n, kind = WKind.ShipLaser, damage = dmg, rate = rate, spread = 0.35f, speed = 320, heatPerShot = 0.03f, vsVehicle = 1, vsInfantry = 1, range = 650, sound = "laser", boltLen = 6, boltWidth = 0.28f };
        public static WeaponDef ShipMissile(string n) => new WeaponDef { name = n, kind = WKind.Missile, damage = 260, rate = 0.7f, speed = 150, heatPerShot = 0.34f, splash = 6, splashDmg = 150, vsVehicle = 1, lockOn = true, range = 700, sound = "rocket", boltLen = 1.6f, boltWidth = 0.45f };
        public static WeaponDef Torpedo(string n) => new WeaponDef { name = n, kind = WKind.Torpedo, damage = 950, rate = 0.45f, speed = 120, heatPerShot = 0.5f, splash = 8, splashDmg = 400, vsVehicle = 1, range = 700, sound = "cannon", boltLen = 2.5f, boltWidth = 1.1f, pitch = 0.7f };
        public static WeaponDef Flechette(string n) => new WeaponDef { name = n, kind = WKind.Shotgun, damage = 22, pellets = 7, rate = 1.3f, spread = 2.5f, speed = 340, heatPerShot = 0.3f, vsVehicle = 1, range = 400, sound = "repeater", boltLen = 3, boltWidth = 0.2f };
        public static WeaponDef RepairPulse(string n) => new WeaponDef { name = n, kind = WKind.Repair, damage = 150, rate = 0.33f, range = 70, heatPerShot = 0.9f, sound = "repair" };
        public static WeaponDef CapTurret(string n) => new WeaponDef { name = n, kind = WKind.Turret, damage = 22, rate = 1.3f, spread = 1.2f, speed = 240, heatPerShot = 0, vsVehicle = 1, range = 650, sound = "heavy", boltLen = 9, boltWidth = 0.6f };
    }

    /// <summary>The four original factions of Battalions in Space.</summary>
    public static class Factions
    {
        public static FactionDef[] All;
        public static FactionDef Get(int i) => All[Mathf.Clamp(i, 0, All.Length - 1)];

        static Factions() { Build(); }

        static ClassDef Cls(string name, Role role, WeaponDef p, WeaponDef s, WeaponDef award, Equip eq, int n, Special sp, string blurb, float hp = 300, float speed = 6.2f)
            => new ClassDef { name = name, role = role, primary = p, secondary = s, award = award, awardName = award.name, equip = eq, equipCount = n, special = sp, blurb = blurb, hp = hp, speed = speed };

        static HeroDef Hero(string name, string title, HeroKind k, WeaponDef w, string ability, string blurb, Color main, Color accent, Color glow)
        {
            var h = new HeroDef { name = name, title = title, kind = k, weapon = w, abilityName = ability, blurb = blurb, main = main, accent = accent, glow = glow };
            switch (k)
            {
                case HeroKind.Blade: h.hp = 1300; h.speed = 7.6f; break;
                case HeroKind.Gunslinger: h.hp = 1000; h.speed = 7.4f; break;
                case HeroKind.Juggernaut: h.hp = 1900; h.speed = 5.6f; h.scale = 1.3f; break;
                case HeroKind.Mystic: h.hp = 1100; h.speed = 6.8f; break;
            }
            return h;
        }

        static VehicleDef Veh(string name, VKind k, string blurb, string p, string s, string g = null)
        {
            var v = new VehicleDef { name = name, kind = k, blurb = blurb };
            switch (k)
            {
                case VKind.Tank: v.hp = 2400; v.speed = 13; v.turn = 55; v.seats = 2; v.primary = W.TankCannon(p); v.gunner = W.Mg(g ?? "Coaxial Repeater"); v.respawn = 40; break;
                case VKind.Transport: v.hp = 1900; v.speed = 17; v.turn = 70; v.seats = 5; v.primary = W.TwinBlaster(p); v.gunner = W.RocketPod(g ?? "Rocket Turret"); v.respawn = 30; break;
                case VKind.Air: v.hp = 1500; v.speed = 38; v.turn = 75; v.seats = 3; v.primary = W.AirLaser(p); v.secondary = W.AirMissile(s); v.gunner = W.Mg(g ?? "Chin Turret"); v.respawn = 45; break;
                case VKind.Fighter: v.hp = 650; v.speed = 72; v.turn = 95; v.seats = 1; v.primary = W.ShipLaser(p); v.secondary = W.ShipMissile(s); v.respawn = 6; break;
                case VKind.Bomber: v.hp = 1250; v.speed = 52; v.turn = 65; v.seats = 2; v.primary = W.ShipLaser(p, 34, 4); v.secondary = W.Torpedo(s); v.gunner = W.ShipLaser(g ?? "Tail Gun", 28, 6); v.respawn = 10; break;
                case VKind.Interceptor: v.hp = 420; v.speed = 94; v.turn = 125; v.seats = 1; v.primary = W.ShipLaser(p, 32, 11); v.secondary = W.Flechette(s); v.respawn = 6; break;
                case VKind.Support: v.hp = 950; v.speed = 62; v.turn = 80; v.seats = 3; v.primary = W.ShipLaser(p, 36, 5); v.secondary = W.RepairPulse(s); v.respawn = 10; break;
                case VKind.Dropship: v.hp = 3200; v.speed = 58; v.turn = 60; v.seats = 6; v.primary = W.ShipLaser(p, 26, 5); v.secondary = W.Flechette(s); v.respawn = 12; break;
            }
            if (v.IsShip)
            {
                // calmer space fights: slower craft, a little tougher, softer guns (was hp x1.45 / guns x0.62 - too tanky;
                // now a fighter dies in about 60% of the time it took)
                v.speed *= 0.75f; v.turn *= 0.85f; v.hp *= 1.15f;
                if (v.primary != null) v.primary.damage *= 0.8f;
                if (v.secondary != null && v.secondary.kind != WKind.Repair) { v.secondary.damage *= 0.8f; v.secondary.splashDmg *= 0.8f; }
                if (v.gunner != null) v.gunner.damage *= 0.8f;
            }
            return v;
        }

        static void Tune(FactionDef f)
        {
            void T(WeaponDef w)
            {
                if (w == null) return;
                w.damage *= f.dmgMul; w.splashDmg *= f.dmgMul; w.rate *= f.rateMul;
                if (f.id == 3) w.spread *= 0.8f;
            }
            foreach (var c in f.classes) { T(c.primary); T(c.secondary); T(c.award); c.hp *= f.hpMul; c.speed *= f.speedMul; }
            foreach (var c in new[] { f.pilot, f.marine }) { T(c.primary); T(c.secondary); T(c.award); c.hp *= f.hpMul; c.speed *= f.speedMul; }
            foreach (var h in f.heroes) { T(h.weapon); h.hp *= f.hpMul; }
        }

        static void Build()
        {
            // ── 0. Terran Concord ─────────────────────────────────────────────
            var concord = new FactionDef
            {
                id = 0, name = "Terran Concord", shortName = "Concord", adjective = "Concord",
                blurb = "A union of human colony worlds. Disciplined, well equipped, fond of jump-packs and long speeches.",
                motto = "Hold the line together.",
                capitalName = "CNS Resolute",
                main = new Color(0.85f, 0.88f, 0.92f), second = new Color(0.18f, 0.36f, 0.72f), accent = new Color(0.95f, 0.75f, 0.2f),
                bolt = new Color(0.35f, 0.65f, 1f), skin = new Color(0.86f, 0.68f, 0.55f), body = Body.Human,
                names = new[] { "Hale", "Ortega", "Brandt", "Nakamura", "Vance", "Kowal", "Reyes", "Sato", "Mercer", "Odum", "Petrov", "Laine", "Duarte", "Fisk", "Kerr", "Ames", "Holt", "Ibarra", "Quill", "Ross", "Tamsin", "Wade", "Yorke", "Zell", "Cho", "Daley", "Ekker", "Farrow", "Grieve", "Ivers", "Juno", "Kade" },
            };
            concord.classes = new[]
            {
                Cls("Concord Rifleman", Role.Assault, W.Rifle("CR-7 Pulse Rifle"), W.Pistol("Sidearm"), W.RifleAward("CR-9 Honor Rifle"), Equip.Grenade, 4, Special.None, "Front-line trooper with a reliable pulse rifle and frag grenades."),
                Cls("Breacher", Role.Heavy, W.Rocket("Breaker Launcher"), W.Pistol("Sidearm"), W.RocketAward("Seeker Launcher"), Equip.Mine, 3, Special.None, "Anti-armour specialist. Rockets lock on to vehicles; lays mines.", 320, 5.8f),
                Cls("Longshot", Role.Marksman, W.Sniper("Longshot Rifle"), W.Pistol("Sidearm"), W.SniperAward("Marksman's Medal Rifle"), Equip.Grenade, 2, Special.None, "Long-range marksman. Hold aim to zoom.", 260),
                Cls("Field Engineer", Role.Engineer, W.Shotgun("Scattergun"), W.Repair("Fusion Cutter"), W.ShotgunAward("Breach Scattergun"), Equip.Detpack, 2, Special.None, "Repairs vehicles and ship systems, plants detpacks, close-quarters scattergun."),
                Cls("Vanguard", Role.Specialist, W.Repeater("Strike Carbine"), W.Pistol("Sidearm"), W.RepeaterAward("Arc Carbine"), Equip.Supply, 2, Special.Jetpack, "Jump-pack trooper. Hold JUMP in the air to fly. Drops supply crates.", 280),
            };
            concord.pilot = Cls("Concord Pilot", Role.Engineer, W.Pistol("Pilot's Sidearm"), W.Repair("Fusion Cutter"), W.TwinGuns("Ace Sidearms"), Equip.Detpack, 2, Special.None, "Flies better, repairs ship systems, carries detpacks for sabotage.", 260);
            concord.pilot.space = true; concord.pilot.pilot = true;
            concord.marine = Cls("Concord Marine", Role.Assault, W.Rifle("CR-7 Pulse Rifle"), W.Rocket("Breaker Launcher"), W.RifleAward("CR-9 Honor Rifle"), Equip.Grenade, 3, Special.None, "Boarding and defence trooper with rifle and rockets.");
            concord.marine.space = true;
            concord.heroes = new[]
            {
                Hero("Commander Elena Voss", "Gunslinger", HeroKind.Gunslinger, W.TwinGuns("Twin Hand-Cannons"), "Fan Fire", "A legendary duelist who never misses twice.", new Color(0.2f, 0.25f, 0.35f), new Color(0.95f, 0.75f, 0.2f), new Color(0.35f, 0.65f, 1f)),
                Hero("Captain Darius Rook", "Juggernaut", HeroKind.Juggernaut, W.HeavyGun("Siege Repeater"), "Rocket Barrage", "Walks into fire wearing a tank's worth of armour.", new Color(0.55f, 0.58f, 0.62f), new Color(0.2f, 0.4f, 0.85f), new Color(1f, 0.6f, 0.2f)),
                Hero("Warden Ilya Mareth", "Bladesworn", HeroKind.Blade, W.Blade("Arc Saber"), "Dash Strike", "Sworn guardian of the Concord with an arc saber.", new Color(0.9f, 0.9f, 0.95f), new Color(0.2f, 0.4f, 0.8f), new Color(0.4f, 0.8f, 1f)),
                Hero("Oracle Sena Kai", "Mystic", HeroKind.Mystic, W.Beam("Storm Grasp"), "Kinetic Wave", "A seer who bends lightning with a thought.", new Color(0.25f, 0.2f, 0.4f), new Color(0.85f, 0.85f, 1f), new Color(0.6f, 0.75f, 1f)),
            };
            concord.tank = Veh("Bastion Hover Tank", VKind.Tank, "Heavy hover tank with a plasma cannon and a gunner seat.", "Plasma Cannon", null);
            concord.transport = Veh("Mule APC", VKind.Transport, "Fast armoured carrier for five, with a rocket turret.", "Twin Blasters", null);
            concord.air = Veh("Kestrel Gunship", VKind.Air, "Low-flying gunship with lasers, missiles and a chin turret.", "Kestrel Lasers", "Hawk Missiles");
            concord.ships = new[]
            {
                Veh("Lancer Fighter", VKind.Fighter, "All-round fighter with lock-on missiles.", "Lancer Cannons", "Lance Missiles"),
                Veh("Hammerhead Bomber", VKind.Bomber, "Heavy bomber; torpedoes wreck capital ship systems.", "Nose Guns", "Hammer Torpedoes"),
                Veh("Swiftwing Interceptor", VKind.Interceptor, "Very fast and fragile; rapid cannons and a flechette burst.", "Rapid Cannons", "Flechette Burst"),
                Veh("Aegis Support Craft", VKind.Support, "Repairs nearby friendly craft and carries two boarders.", "Point Guns", "Repair Pulse"),
                Veh("Bulwark Boarding Shuttle", VKind.Dropship, "Armoured shuttle for five boarders: land in the enemy hangar and storm their ship.", "Shuttle Guns", "Breach Flechettes"),
            };

            // ── 1. Vorrkai Dominion ───────────────────────────────────────────
            var vorr = new FactionDef
            {
                id = 1, name = "Vorrkai Dominion", shortName = "Dominion", adjective = "Vorrkai",
                blurb = "A reptilian warrior empire spreading by conquest. Heavy weapons, heavy hides, slow to fall.",
                motto = "The strong inherit the stars.",
                capitalName = "Dominion Ship Gorrath's Fury",
                main = new Color(0.32f, 0.12f, 0.1f), second = new Color(0.78f, 0.18f, 0.12f), accent = new Color(0.86f, 0.8f, 0.62f),
                bolt = new Color(1f, 0.28f, 0.18f), skin = new Color(0.35f, 0.5f, 0.3f), body = Body.Reptile,
                dmgMul = 1.12f, rateMul = 0.92f, hpMul = 1.08f, speedMul = 0.97f,
                names = new[] { "Skarrik", "Thossa", "Vreln", "Gazhuk", "Orrisk", "Kethra", "Zhaal", "Murrog", "Ssevrin", "Tukkar", "Ralgoth", "Issk", "Drevka", "Haldor", "Yussak", "Brakka", "Sorrn", "Velgra", "Krull", "Asska", "Ghorr", "Nyssa", "Tarrok", "Xelka", "Uzzra", "Kriss", "Vashk", "Rekkan", "Oskra", "Felk", "Grath", "Zuun" },
            };
            vorr.classes = new[]
            {
                Cls("Clawguard", Role.Assault, W.Rifle("Scorch Rifle"), W.Pistol("Fang Pistol"), W.RifleAward("Bloodscale Rifle"), Equip.Grenade, 4, Special.None, "Dominion line warrior with a hard-hitting scorch rifle."),
                Cls("Siegebreaker", Role.Heavy, W.Rocket("Gorehorn Launcher"), W.Pistol("Fang Pistol"), W.RocketAward("Hunter's Gorehorn"), Equip.Mine, 3, Special.None, "Rocket-armed tank killer who seeds the ground with mines.", 320, 5.8f),
                Cls("Fang Stalker", Role.Marksman, W.Sniper("Spine Rifle"), W.Pistol("Fang Pistol"), W.SniperAward("Matriarch's Spine"), Equip.Grenade, 2, Special.None, "Patient sniper. Hold aim to zoom.", 260),
                Cls("Forgeling", Role.Engineer, W.Shotgun("Maw Scatterer"), W.Repair("Forge Torch"), W.ShotgunAward("Great Maw"), Equip.Detpack, 2, Special.None, "Keeps war-beasts and machines running; plants detpacks."),
                Cls("Ravager", Role.Specialist, W.Repeater("Rend Repeater"), W.Pistol("Fang Pistol"), W.RepeaterAward("Firebrand Repeater"), Equip.Supply, 2, Special.Frenzy, "Berserker. FRENZY: run faster and hit harder for a few seconds.", 300),
            };
            vorr.pilot = Cls("Dominion Pilot", Role.Engineer, W.Pistol("Fang Pistol"), W.Repair("Forge Torch"), W.TwinGuns("Twin Fangs"), Equip.Detpack, 2, Special.None, "Repairs ship systems and sabotages with detpacks.", 260);
            vorr.pilot.space = true; vorr.pilot.pilot = true;
            vorr.marine = Cls("Dominion Boarder", Role.Assault, W.Rifle("Scorch Rifle"), W.Rocket("Gorehorn Launcher"), W.RifleAward("Bloodscale Rifle"), Equip.Grenade, 3, Special.None, "Boarding warrior with rifle and rockets.");
            vorr.marine.space = true;
            vorr.heroes = new[]
            {
                Hero("Warlord Skarr", "Juggernaut", HeroKind.Juggernaut, W.HeavyGun("Skullbreaker Cannon"), "Rocket Barrage", "Ruler of a hundred burned worlds.", new Color(0.25f, 0.08f, 0.06f), new Color(0.9f, 0.75f, 0.3f), new Color(1f, 0.4f, 0.1f)),
                Hero("Huntmistress Vyssa", "Gunslinger", HeroKind.Gunslinger, W.TwinGuns("Twin Stingers"), "Fan Fire", "She has never lost a hunt.", new Color(0.15f, 0.25f, 0.15f), new Color(0.85f, 0.2f, 0.15f), new Color(1f, 0.35f, 0.2f)),
                Hero("Bladefang Ruuk", "Bladesworn", HeroKind.Blade, W.Blade("Ember Glaive"), "Dash Strike", "A duelist whose glaive burns like a forge.", new Color(0.45f, 0.1f, 0.08f), new Color(0.2f, 0.2f, 0.2f), new Color(1f, 0.3f, 0.1f)),
                Hero("Shaman Ossk", "Mystic", HeroKind.Mystic, W.Beam("Bonefire"), "Kinetic Wave", "Old as the Dominion, and twice as cruel.", new Color(0.4f, 0.38f, 0.3f), new Color(0.6f, 0.15f, 0.5f), new Color(0.8f, 0.3f, 1f)),
            };
            vorr.tank = Veh("Crusher Assault Tank", VKind.Tank, "A beast of a tank with a scorch cannon.", "Scorch Cannon", null);
            vorr.transport = Veh("Drake Carrier", VKind.Transport, "Armoured war-sled with a rocket turret.", "Drake Blasters", null);
            vorr.air = Veh("Wyrm Gunship", VKind.Air, "Predatory gunship with talon missiles.", "Wyrm Lasers", "Talon Missiles");
            vorr.ships = new[]
            {
                Veh("Talon Fighter", VKind.Fighter, "Aggressive fighter with lock-on missiles.", "Talon Guns", "Talon Missiles"),
                Veh("Maw Bomber", VKind.Bomber, "Armoured bomber hauling fusion torpedoes.", "Maw Guns", "Fusion Torpedoes"),
                Veh("Viper Interceptor", VKind.Interceptor, "Fast striker that tears fighters apart.", "Viper Cannons", "Venom Burst"),
                Veh("Brood Tender", VKind.Support, "Mends nearby craft and carries boarders.", "Tender Guns", "Mending Pulse"),
                Veh("Gorefang Assault Barge", VKind.Dropship, "Carries five warriors into the enemy hangar.", "Barge Guns", "Spine Flechettes"),
            };

            // ── 2. Iron Synod ─────────────────────────────────────────────────
            var synod = new FactionDef
            {
                id = 2, name = "Iron Synod", shortName = "Synod", adjective = "Synod",
                blurb = "A machine collective that rewrites worlds into factories. Many, fast, precise, and never afraid.",
                motto = "All will be optimised.",
                capitalName = "Synod Ark Calculus",
                main = new Color(0.32f, 0.34f, 0.36f), second = new Color(0.92f, 0.62f, 0.14f), accent = new Color(0.15f, 0.15f, 0.16f),
                bolt = new Color(1f, 0.75f, 0.2f), skin = new Color(0.5f, 0.52f, 0.55f), body = Body.Machine,
                dmgMul = 0.92f, rateMul = 1.12f, hpMul = 0.95f, speedMul = 1f,
                names = new[] { "K-11", "R-04", "V-27", "M-13", "S-88", "T-62", "X-09", "B-31", "Q-77", "Z-15", "H-42", "N-56", "C-03", "J-19", "L-71", "P-24", "D-38", "F-66", "G-12", "W-83", "Y-45", "A-90", "E-07", "U-29", "O-51", "I-36", "K-72", "R-18", "V-95", "M-61", "S-22", "T-40" },
            };
            synod.classes = new[]
            {
                Cls("Unit-K Trooper", Role.Assault, W.Rifle("Lattice Rifle"), W.Pistol("Pulse Pistol"), W.RifleAward("Optimised Lattice"), Equip.Grenade, 4, Special.None, "Mass-produced infantry frame."),
                Cls("Breaker Frame", Role.Heavy, W.Rocket("Breaker Launcher"), W.Pistol("Pulse Pistol"), W.RocketAward("Tracking Breaker"), Equip.Mine, 3, Special.None, "Armour-cracking frame with lock-on rockets and mines.", 320, 5.8f),
                Cls("Optic Sentinel", Role.Marksman, W.Sniper("Optic Lance"), W.Pistol("Pulse Pistol"), W.SniperAward("Perfect Lance"), Equip.Grenade, 2, Special.None, "Long-range frame with a ten-power optic.", 260),
                Cls("Fabricator", Role.Engineer, W.Shotgun("Scrap Cannon"), W.Repair("Weld Arm"), W.ShotgunAward("Smelter Cannon"), Equip.Detpack, 2, Special.None, "Repairs anything metal; plants detpacks."),
                Cls("Phantom Frame", Role.Specialist, W.Repeater("Needle Repeater"), W.Pistol("Pulse Pistol"), W.RepeaterAward("Shard Repeater"), Equip.Supply, 2, Special.Cloak, "Infiltrator. CLOAK: turn invisible to the enemy for a few seconds.", 270),
            };
            synod.pilot = Cls("Synod Pilot Frame", Role.Engineer, W.Pistol("Pulse Pistol"), W.Repair("Weld Arm"), W.TwinGuns("Paired Pulse Pistols"), Equip.Detpack, 2, Special.None, "Repairs ship systems; detpacks for sabotage.", 260);
            synod.pilot.space = true; synod.pilot.pilot = true;
            synod.marine = Cls("Synod Boarding Frame", Role.Assault, W.Rifle("Lattice Rifle"), W.Rocket("Breaker Launcher"), W.RifleAward("Optimised Lattice"), Equip.Grenade, 3, Special.None, "Boarding frame with rifle and rockets.");
            synod.marine.space = true;
            synod.heroes = new[]
            {
                Hero("Prime Axiom", "Juggernaut", HeroKind.Juggernaut, W.HeavyGun("Axiom Cannon"), "Rocket Barrage", "The Synod's first and largest war-frame.", new Color(0.2f, 0.2f, 0.22f), new Color(0.95f, 0.6f, 0.1f), new Color(1f, 0.6f, 0.1f)),
                Hero("Executor Vex-9", "Gunslinger", HeroKind.Gunslinger, W.TwinGuns("Twin Executors"), "Fan Fire", "Calculates every shot before it is fired.", new Color(0.7f, 0.72f, 0.75f), new Color(0.15f, 0.15f, 0.15f), new Color(1f, 0.25f, 0.2f)),
                Hero("Shard Duelist", "Bladesworn", HeroKind.Blade, W.Blade("Monofilament Edge"), "Dash Strike", "Four arms, one purpose.", new Color(0.12f, 0.12f, 0.14f), new Color(0.9f, 0.9f, 0.9f), new Color(0.3f, 1f, 0.6f)),
                Hero("The Choir", "Mystic", HeroKind.Mystic, W.Beam("Resonance"), "Kinetic Wave", "A thousand minds singing lightning.", new Color(0.85f, 0.8f, 0.6f), new Color(0.3f, 0.3f, 0.35f), new Color(1f, 0.85f, 0.3f)),
            };
            synod.tank = Veh("Monolith Tank", VKind.Tank, "Slab-sided hover tank with a lattice cannon.", "Lattice Cannon", null);
            synod.transport = Veh("Hive Hauler", VKind.Transport, "Frame transport with a rocket turret.", "Hauler Guns", null);
            synod.air = Veh("Wasp Drone Gunship", VKind.Air, "Drone gunship with lasers and missiles.", "Wasp Lasers", "Stinger Missiles");
            synod.ships = new[]
            {
                Veh("Spindle Fighter", VKind.Fighter, "Balanced drone fighter.", "Spindle Guns", "Seeker Missiles"),
                Veh("Anvil Bomber", VKind.Bomber, "Heavy drone bomber with breach torpedoes.", "Anvil Guns", "Breach Torpedoes"),
                Veh("Needle Interceptor", VKind.Interceptor, "Fastest craft in the sky.", "Needle Cannons", "Shard Burst"),
                Veh("Mender Drone", VKind.Support, "Repairs nearby drones and carries boarders.", "Mender Guns", "Repair Swarm"),
                Veh("Lattice Insertion Carrier", VKind.Dropship, "Delivers five frames into the enemy hangar.", "Carrier Guns", "Shard Flechettes"),
            };

            // ── 3. Kessari Veil ───────────────────────────────────────────────
            var kess = new FactionDef
            {
                id = 3, name = "Kessari Veil", shortName = "Veil", adjective = "Kessari",
                blurb = "Tall, ancient psionic beings who guard the old star-roads. Precise weapons and fast feet.",
                motto = "The Veil endures.",
                capitalName = "Veilship Everdusk",
                main = new Color(0.16f, 0.4f, 0.42f), second = new Color(0.55f, 0.3f, 0.75f), accent = new Color(0.85f, 0.92f, 0.88f),
                bolt = new Color(0.4f, 1f, 0.75f), skin = new Color(0.55f, 0.7f, 0.85f), body = Body.Slender,
                dmgMul = 1f, rateMul = 1f, hpMul = 0.92f, speedMul = 1.07f,
                names = new[] { "Ithre", "Saelo", "Varun", "Mireth", "Ouvan", "Celiss", "Theron", "Aloe", "Yrsa", "Keth", "Lunara", "Davri", "Essa", "Quoren", "Nimue", "Tiriel", "Ashen", "Velis", "Orun", "Seph", "Iolan", "Rhys", "Uma", "Feyr", "Halcy", "Zeroth", "Pell", "Wyn", "Imra", "Joren", "Kael", "Lisse" },
            };
            kess.classes = new[]
            {
                Cls("Veil Warden", Role.Assault, W.Rifle("Lumen Rifle"), W.Pistol("Glass Pistol"), W.RifleAward("Starlit Lumen"), Equip.Grenade, 4, Special.None, "Swift guardian with a precise lumen rifle."),
                Cls("Lancebearer", Role.Heavy, W.Rocket("Lance Projector"), W.Pistol("Glass Pistol"), W.RocketAward("Seeking Lance"), Equip.Mine, 3, Special.None, "Carries a lance projector that locks on to vehicles; lays mines.", 320, 5.8f),
                Cls("Seer", Role.Marksman, W.Sniper("Farsight Bow"), W.Pistol("Glass Pistol"), W.SniperAward("Eye of Dusk"), Equip.Grenade, 2, Special.None, "Psionic marksman. Hold aim to zoom.", 260),
                Cls("Weaver", Role.Engineer, W.Shotgun("Prism Scatter"), W.Repair("Mending Loom"), W.ShotgunAward("Great Prism"), Equip.Detpack, 2, Special.None, "Mends craft and ship systems; plants detpacks."),
                Cls("Shade", Role.Specialist, W.Repeater("Whisper Repeater"), W.Pistol("Glass Pistol"), W.RepeaterAward("Moonfire Repeater"), Equip.Supply, 2, Special.Rally, "Battle-singer. RALLY: heal nearby allies and boost their damage.", 280),
            };
            kess.pilot = Cls("Veil Pilot", Role.Engineer, W.Pistol("Glass Pistol"), W.Repair("Mending Loom"), W.TwinGuns("Glass Twins"), Equip.Detpack, 2, Special.None, "Repairs ship systems; detpacks for sabotage.", 260);
            kess.pilot.space = true; kess.pilot.pilot = true;
            kess.marine = Cls("Veil Boarder", Role.Assault, W.Rifle("Lumen Rifle"), W.Rocket("Lance Projector"), W.RifleAward("Starlit Lumen"), Equip.Grenade, 3, Special.None, "Boarding guardian with rifle and lance projector.");
            kess.marine.space = true;
            kess.heroes = new[]
            {
                Hero("Matriarch Ysolde", "Mystic", HeroKind.Mystic, W.Beam("Veilstorm"), "Kinetic Wave", "Eldest of the Veil; storms answer her.", new Color(0.9f, 0.9f, 0.95f), new Color(0.55f, 0.3f, 0.75f), new Color(0.5f, 1f, 0.85f)),
                Hero("Duelist Kaeth", "Bladesworn", HeroKind.Blade, W.Blade("Twilight Edge"), "Dash Strike", "The fastest blade on the star-roads.", new Color(0.1f, 0.12f, 0.2f), new Color(0.4f, 0.9f, 0.8f), new Color(0.6f, 0.4f, 1f)),
                Hero("Starcaller Iru", "Gunslinger", HeroKind.Gunslinger, W.TwinGuns("Twin Starfire"), "Fan Fire", "Fires faster than the eye can follow.", new Color(0.2f, 0.45f, 0.5f), new Color(0.9f, 0.85f, 0.5f), new Color(0.4f, 1f, 0.75f)),
                Hero("Ironveil Thane", "Juggernaut", HeroKind.Juggernaut, W.HeavyGun("Moonhammer Cannon"), "Rocket Barrage", "Walking fortress of the old Veil.", new Color(0.35f, 0.3f, 0.45f), new Color(0.85f, 0.92f, 0.88f), new Color(0.6f, 0.4f, 1f)),
            };
            kess.tank = Veh("Pillar Hover Tank", VKind.Tank, "Graceful but deadly lumen tank.", "Lumen Cannon", null);
            kess.transport = Veh("Reliquary Skiff", VKind.Transport, "Fast skiff with a lance turret.", "Skiff Blasters", null);
            kess.air = Veh("Heron Gunship", VKind.Air, "Elegant gunship with seeker darts.", "Heron Lasers", "Seeker Darts");
            kess.ships = new[]
            {
                Veh("Glaive Fighter", VKind.Fighter, "Agile fighter with seeker missiles.", "Glaive Guns", "Seeker Missiles"),
                Veh("Bloom Bomber", VKind.Bomber, "Heavy bomber with starfire torpedoes.", "Bloom Guns", "Starfire Torpedoes"),
                Veh("Mote Interceptor", VKind.Interceptor, "Tiny, fast and furious.", "Mote Cannons", "Mote Burst"),
                Veh("Lantern Support", VKind.Support, "Heals nearby craft; carries boarders.", "Lantern Guns", "Healing Light"),
                Veh("Veilglass Boarding Skiff", VKind.Dropship, "Slips five boarders into the enemy hangar.", "Skiff Guns", "Glass Flechettes"),
            };

            All = new[] { concord, vorr, synod, kess };
            foreach (var f in All) Tune(f);
        }
    }
}
