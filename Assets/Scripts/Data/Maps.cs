using UnityEngine;

namespace BIS
{
    public enum Theme { Desert, Forest, Snow, Volcanic, Urban, Sky }
    public enum SpaceBg { Planet, Nebula, Asteroids }

    public class PostDef
    {
        public string name;
        public Vector2 pos;          // -1..1 of the playable half size
        public int owner;            // 0 = team A, 1 = team B, -1 = neutral
        public VKind[] vehicles;
        public bool home;            // a team's main base even with fewer than 3 vehicle pads
        public PostDef(string n, float x, float z, int o, params VKind[] v) { name = n; pos = new Vector2(x, z); owner = o; vehicles = v; }
        public PostDef Home() { home = true; return this; }
        public bool IsHome => home || vehicles.Length >= 3;
    }

    public enum FeatureKind { Ridge, River, Lava, Wadi, Road, Forest, Town, Boulders, Lake, Crater, Ford, City, Isle, Span, Wall, Tower, Compound, Vault, Wreck }

    /// <summary>A named part of a map with its own biome (battle royale: one map holds several). Centre in map units.</summary>
    public class RegionDef
    {
        public string name; public Theme theme; public Vector2 pos;
        public RegionDef(string n, Theme t, float x, float z) { name = n; theme = t; pos = new Vector2(x, z); }
    }

    /// <summary>
    /// One piece of a land map's layout, in the same -1..1 space as posts (x 0.72 of the half size).
    /// Ridges are walls you can't climb; a road that crosses a ridge cuts a pass through it, and a road that
    /// crosses a river or lava gets a bridge. Forests, towns and boulder fields are areas (pts[0] + width as radius).
    /// </summary>
    public class MapFeature
    {
        public FeatureKind kind;
        public Vector2[] pts;
        public float width, height, density = 1;
        static Vector2[] P(float[] xy) { var v = new Vector2[xy.Length / 2]; for (int i = 0; i < v.Length; i++) v[i] = new Vector2(xy[i * 2], xy[i * 2 + 1]); return v; }
        /// <summary>A mountain wall `width` metres thick at the base and `height` metres high along a line.</summary>
        public static MapFeature Ridge(float width, float height, params float[] xy) => new MapFeature { kind = FeatureKind.Ridge, width = width, height = height, pts = P(xy) };
        public static MapFeature River(float width, float depth, params float[] xy) => new MapFeature { kind = FeatureKind.River, width = width, height = depth, pts = P(xy) };
        public static MapFeature Lava(float width, params float[] xy) => new MapFeature { kind = FeatureKind.Lava, width = width, height = 4, pts = P(xy) };
        /// <summary>A dry gulch you can walk along, with steep banks.</summary>
        public static MapFeature Wadi(float width, float depth, params float[] xy) => new MapFeature { kind = FeatureKind.Wadi, width = width, height = depth, pts = P(xy) };
        public static MapFeature Road(float width, params float[] xy) => new MapFeature { kind = FeatureKind.Road, width = width, pts = P(xy) };
        public static MapFeature Forest(float x, float z, float radius, float density = 1) => new MapFeature { kind = FeatureKind.Forest, width = radius, density = density, pts = P(new[] { x, z }) };
        public static MapFeature Town(float x, float z, float radius, int buildings) => new MapFeature { kind = FeatureKind.Town, width = radius, density = buildings, pts = P(new[] { x, z }) };
        public static MapFeature Boulders(float x, float z, float radius, int count) => new MapFeature { kind = FeatureKind.Boulders, width = radius, density = count, pts = P(new[] { x, z }) };
        /// <summary>A flat lake bed (frozen in snow, so it can be walked on).</summary>
        public static MapFeature Lake(float x, float z, float radius) => new MapFeature { kind = FeatureKind.Lake, width = radius, pts = P(new[] { x, z }) };
        /// <summary>A shallow stretch of a river that can be waded across (radius in map units).</summary>
        public static MapFeature Ford(float x, float z, float radius) => new MapFeature { kind = FeatureKind.Ford, width = radius, pts = P(new[] { x, z }) };
        /// <summary>A ring-shaped rim `height` high round a point; roads cut gaps in it.</summary>
        public static MapFeature Crater(float x, float z, float radius, float height) => new MapFeature { kind = FeatureKind.Crater, width = radius, height = height, pts = P(new[] { x, z }) };
        /// <summary>A dense village: like a town, but most buildings can be walked into.</summary>
        public static MapFeature Village(float x, float z, float radius, int buildings) => new MapFeature { kind = FeatureKind.Town, width = radius, height = 0.85f, density = buildings, pts = P(new[] { x, z }) };
        /// <summary>A city: a grid of streets (roads) round blocks of towers, buildings you can enter, parks and rubble lots.
        /// halfX / halfZ in map units; block = metres between street centres.</summary>
        public static MapFeature City(float x, float z, float halfX, float halfZ, float block) => new MapFeature { kind = FeatureKind.City, width = halfX, height = halfZ, density = block, pts = P(new[] { x, z }) };
        /// <summary>Floating maps: an island whose flat top stands `level` metres up (radius in map units).</summary>
        public static MapFeature Isle(float x, float z, float radius, float level) => new MapFeature { kind = FeatureKind.Isle, width = radius, height = level, pts = P(new[] { x, z }) };
        /// <summary>Floating maps: a walkway from one island to another (points inside each); a flight of stairs takes it up or down.</summary>
        public static MapFeature Span(float width, float x0, float z0, float x1, float z1) => new MapFeature { kind = FeatureKind.Span, width = width, pts = P(new[] { x0, z0, x1, z1 }) };
        /// <summary>A low wall, hedge or barrier line with gaps to walk through (cover).</summary>
        public static MapFeature Wall(float height, params float[] xy) => new MapFeature { kind = FeatureKind.Wall, height = height, width = 1, pts = P(xy) };
        /// <summary>A landmark tower (clock tower, radio mast, spire).</summary>
        public static MapFeature Tower(float x, float z, float height) => new MapFeature { kind = FeatureKind.Tower, height = height, width = 0.03f, pts = P(new[] { x, z }) };
        /// <summary>A walled compound of large buildings you can enter (military base, research station, spaceport hangars).
        /// style: 0 barracks, 1 hangars + landing pads, 2 research labs.</summary>
        public static MapFeature Compound(float x, float z, float radius, int buildings, int style) => new MapFeature { kind = FeatureKind.Compound, width = radius, density = buildings, height = style, pts = P(new[] { x, z }) };
        /// <summary>A sealed vault: its door opens only with a vault key (battle royale); the best loot is inside.</summary>
        public static MapFeature Vault(float x, float z, float yaw) => new MapFeature { kind = FeatureKind.Vault, height = yaw, width = 0.03f, pts = P(new[] { x, z }) };
        /// <summary>A crashed warship section you can walk into through its breach.</summary>
        public static MapFeature Wreck(float x, float z, float yaw) => new MapFeature { kind = FeatureKind.Wreck, height = yaw, width = 0.06f, pts = P(new[] { x, z }) };
    }

    public class LandMapDef
    {
        public MapFeature[] features = new MapFeature[0];
        public string name, planet, blurb;
        public Theme theme;
        public int seed;
        public float size = 640, hills = 1, roughness = 1, props = 1;
        public float cell = 2.5f;          // terrain grid (metres); the big royale map uses a coarser one
        public PostDef[] posts;
        public RegionDef[] regions;        // several biomes on one map (null = the whole map is `theme`)
        public bool royale;                // the battle royale map: no posts; loot, buildings and the zone
        /// <summary>Islands over a void: falling off kills, the ground below is never drawn.</summary>
        public bool Floating => theme == Theme.Sky;
        public LandMapDef Variant(string planetName, int newSeed)
        {
            var m = (LandMapDef)MemberwiseClone();
            m.planet = planetName; m.seed = newSeed; m.name = planetName;
            return m;
        }
    }

    public class SpaceMapDef
    {
        public string name, blurb;
        public SpaceBg bg;
        public int seed;
        public Color neb1, neb2, sun, planet;
    }

    public class ThemeInfo
    {
        public Color low, mid, high, steep, patch, path;
        public Color skyTop, skyHorizon, skyBottom, sun, ambientSky, ambientGround, fog;
        public float fogDensity, sunPitch, sunYaw;
        public static ThemeInfo Of(Theme t)
        {
            switch (t)
            {
                case Theme.Desert: return new ThemeInfo
                {
                    low = new Color(0.86f, 0.7f, 0.46f), mid = new Color(0.8f, 0.62f, 0.4f), high = new Color(0.72f, 0.52f, 0.36f), steep = new Color(0.62f, 0.42f, 0.3f),
                    patch = new Color(0.9f, 0.78f, 0.56f), path = new Color(0.7f, 0.58f, 0.42f),
                    skyTop = new Color(0.3f, 0.52f, 0.82f), skyHorizon = new Color(0.95f, 0.85f, 0.7f), skyBottom = new Color(0.8f, 0.68f, 0.5f), sun = new Color(1f, 0.93f, 0.8f),
                    ambientSky = new Color(0.62f, 0.66f, 0.75f), ambientGround = new Color(0.55f, 0.45f, 0.35f), fog = new Color(0.9f, 0.82f, 0.68f), fogDensity = 0.0022f, sunPitch = 52, sunYaw = 35
                };
                case Theme.Forest: return new ThemeInfo
                {
                    low = new Color(0.3f, 0.48f, 0.22f), mid = new Color(0.26f, 0.42f, 0.2f), high = new Color(0.4f, 0.44f, 0.3f), steep = new Color(0.4f, 0.36f, 0.3f),
                    patch = new Color(0.38f, 0.55f, 0.24f), path = new Color(0.45f, 0.38f, 0.26f),
                    skyTop = new Color(0.32f, 0.5f, 0.78f), skyHorizon = new Color(0.78f, 0.86f, 0.88f), skyBottom = new Color(0.4f, 0.5f, 0.4f), sun = new Color(1f, 0.96f, 0.86f),
                    ambientSky = new Color(0.55f, 0.62f, 0.72f), ambientGround = new Color(0.3f, 0.36f, 0.26f), fog = new Color(0.66f, 0.74f, 0.74f), fogDensity = 0.0032f, sunPitch = 46, sunYaw = 140
                };
                case Theme.Snow: return new ThemeInfo
                {
                    low = new Color(0.9f, 0.93f, 0.97f), mid = new Color(0.86f, 0.9f, 0.96f), high = new Color(0.95f, 0.97f, 1f), steep = new Color(0.5f, 0.54f, 0.6f),
                    patch = new Color(0.78f, 0.84f, 0.92f), path = new Color(0.72f, 0.74f, 0.78f),
                    skyTop = new Color(0.45f, 0.58f, 0.75f), skyHorizon = new Color(0.86f, 0.9f, 0.95f), skyBottom = new Color(0.8f, 0.84f, 0.9f), sun = new Color(0.95f, 0.97f, 1f),
                    ambientSky = new Color(0.68f, 0.74f, 0.85f), ambientGround = new Color(0.6f, 0.62f, 0.68f), fog = new Color(0.82f, 0.86f, 0.92f), fogDensity = 0.0045f, sunPitch = 28, sunYaw = 210
                };
                case Theme.Urban: return new ThemeInfo
                {
                    // concrete and asphalt under a dusk sky
                    low = new Color(0.52f, 0.51f, 0.5f), mid = new Color(0.46f, 0.46f, 0.47f), high = new Color(0.56f, 0.54f, 0.52f), steep = new Color(0.4f, 0.37f, 0.35f),
                    patch = new Color(0.44f, 0.47f, 0.38f), path = new Color(0.2f, 0.2f, 0.22f),
                    skyTop = new Color(0.22f, 0.28f, 0.48f), skyHorizon = new Color(0.98f, 0.66f, 0.44f), skyBottom = new Color(0.42f, 0.36f, 0.36f), sun = new Color(1f, 0.82f, 0.62f),
                    ambientSky = new Color(0.55f, 0.55f, 0.66f), ambientGround = new Color(0.4f, 0.36f, 0.34f), fog = new Color(0.66f, 0.56f, 0.54f), fogDensity = 0.0024f, sunPitch = 24, sunYaw = 250
                };
                case Theme.Sky: return new ThemeInfo
                {
                    // grassy island tops and pale rock, high above a sea of cloud
                    low = new Color(0.42f, 0.63f, 0.3f), mid = new Color(0.36f, 0.57f, 0.27f), high = new Color(0.5f, 0.68f, 0.38f), steep = new Color(0.62f, 0.56f, 0.5f),
                    patch = new Color(0.62f, 0.72f, 0.36f), path = new Color(0.74f, 0.71f, 0.64f),
                    skyTop = new Color(0.22f, 0.46f, 0.88f), skyHorizon = new Color(0.86f, 0.91f, 0.99f), skyBottom = new Color(0.93f, 0.95f, 0.99f), sun = new Color(1f, 0.96f, 0.86f),
                    ambientSky = new Color(0.62f, 0.7f, 0.85f), ambientGround = new Color(0.6f, 0.62f, 0.66f), fog = new Color(0.82f, 0.88f, 0.96f), fogDensity = 0.0016f, sunPitch = 42, sunYaw = 60
                };
                default: return new ThemeInfo
                {
                    low = new Color(0.2f, 0.17f, 0.16f), mid = new Color(0.26f, 0.22f, 0.2f), high = new Color(0.34f, 0.3f, 0.27f), steep = new Color(0.14f, 0.12f, 0.12f),
                    patch = new Color(0.4f, 0.24f, 0.16f), path = new Color(0.3f, 0.26f, 0.22f),
                    skyTop = new Color(0.18f, 0.12f, 0.14f), skyHorizon = new Color(0.75f, 0.38f, 0.22f), skyBottom = new Color(0.3f, 0.15f, 0.1f), sun = new Color(1f, 0.7f, 0.5f),
                    ambientSky = new Color(0.45f, 0.32f, 0.3f), ambientGround = new Color(0.35f, 0.18f, 0.12f), fog = new Color(0.45f, 0.24f, 0.18f), fogDensity = 0.0042f, sunPitch = 22, sunYaw = 300
                };
            }
        }
    }

    public static class Maps
    {
        public static readonly LandMapDef[] Land =
        {
            new LandMapDef
            {
                name = "Harrak Dunes", planet = "Harrak", theme = Theme.Desert, seed = 1041, hills = 0.55f, props = 0.7f,
                blurb = "Sandstone mesas split the dunes into three lanes: canyon passes, a dry gulch with two bridges, and a mining town at the Salt Mine.",
                features = new[]
                {
                    // the long central mesa with two canyon passes (where the roads cut through), and its neighbours
                    MapFeature.Ridge(40, 34, -0.48f, -0.27f, -0.12f, -0.22f, 0.3f, -0.2f, 0.78f, -0.12f),
                    MapFeature.Ridge(44, 30, -0.28f, 0.3f, 0.12f, 0.36f),
                    MapFeature.Ridge(30, 28, -0.78f, 0.16f, -0.7f, 0.34f),
                    MapFeature.Ridge(34, 26, 0.32f, -0.92f, 0.62f, -0.82f),
                    MapFeature.Ridge(30, 30, 0.72f, 0.12f, 0.95f, 0.3f),
                    MapFeature.Wadi(22, 5, 1.05f, -0.4f, 0.7f, -0.44f, 0.36f, -0.42f, 0.02f, -0.44f, -0.3f, -0.5f, -0.56f, -0.47f),
                    MapFeature.Road(9, -0.78f, -0.62f, -0.62f, -0.35f, -0.4f, -0.05f),
                    MapFeature.Road(9, -0.78f, -0.62f, -0.35f, -0.72f, 0.15f, -0.62f),
                    MapFeature.Road(9, -0.4f, -0.05f, -0.18f, 0.06f, 0.02f, 0.08f),
                    MapFeature.Road(8, 0.02f, 0.08f, 0.05f, -0.3f, 0.15f, -0.62f),
                    MapFeature.Road(9, 0.02f, 0.08f, 0.22f, 0.25f, 0.42f, 0.32f),
                    MapFeature.Road(8, 0.15f, -0.62f, 0.5f, -0.38f, 0.56f, 0.02f, 0.42f, 0.32f),
                    MapFeature.Road(9, 0.42f, 0.32f, 0.6f, 0.52f, 0.78f, 0.66f),
                    MapFeature.Road(8, -0.4f, -0.05f, -0.46f, 0.42f, -0.1f, 0.62f, 0.22f, 0.56f, 0.42f, 0.32f),
                    MapFeature.Town(0.3f, -0.74f, 0.16f, 10),
                    MapFeature.Town(-0.52f, -0.22f, 0.1f, 5),
                    MapFeature.Town(0.62f, 0.4f, 0.09f, 4),
                    MapFeature.Boulders(0.0f, 0.32f, 0.12f, 9),
                    MapFeature.Boulders(-0.2f, -0.6f, 0.12f, 8),
                    MapFeature.Boulders(0.55f, -0.62f, 0.1f, 6),
                    MapFeature.Boulders(-0.72f, -0.2f, 0.1f, 6),
                },
                posts = new[]
                {
                    new PostDef("Landing Field", -0.78f, -0.62f, 0, VKind.Tank, VKind.Transport, VKind.Air),
                    new PostDef("Pump Station", -0.4f, -0.05f, 0, VKind.Transport),
                    new PostDef("Old Arch", 0.02f, 0.08f, -1, VKind.Tank),
                    new PostDef("Salt Mine", 0.15f, -0.62f, -1),
                    new PostDef("Sand Fort", 0.42f, 0.32f, 1, VKind.Transport),
                    new PostDef("Crash Site", 0.78f, 0.66f, 1, VKind.Tank, VKind.Transport, VKind.Air),
                }
            },
            new LandMapDef
            {
                name = "Elos Highwood", planet = "Elos", theme = Theme.Forest, seed = 2203, hills = 0.75f, props = 0.7f,
                blurb = "A river splits the green moon's forest in two. Three bridges, a ford you can wade, and deep woods to sneak through.",
                features = new[]
                {
                    MapFeature.River(18, 2.2f, 0.05f, 1.1f, 0.12f, 0.55f, 0.22f, 0.22f, 0.19f, -0.1f, 0.1f, -0.42f, 0.0f, -0.8f, -0.05f, -1.1f),
                    MapFeature.Ford(0.21f, 0.2f, 0.07f),
                    MapFeature.Road(8, -0.75f, 0.55f, -0.5f, 0.35f, -0.3f, 0.2f),
                    MapFeature.Road(8, -0.3f, 0.2f, -0.12f, 0.05f, 0.0f, -0.05f),
                    MapFeature.Road(7, -0.3f, 0.2f, -0.44f, -0.2f, -0.25f, -0.55f),
                    MapFeature.Road(7, 0.0f, -0.05f, -0.1f, -0.32f, -0.25f, -0.55f),
                    MapFeature.Road(8, 0.0f, -0.05f, 0.35f, -0.25f),
                    MapFeature.Road(8, -0.25f, -0.55f, 0.08f, -0.72f, 0.45f, -0.72f, 0.74f, -0.6f),
                    MapFeature.Road(8, 0.35f, -0.25f, 0.55f, -0.45f, 0.74f, -0.6f),
                    MapFeature.Road(7, -0.3f, 0.2f, -0.05f, 0.45f, 0.32f, 0.45f, 0.5f, 0.14f, 0.35f, -0.25f),
                    MapFeature.Forest(-0.62f, -0.12f, 0.24f, 1.5f),
                    MapFeature.Forest(0.55f, 0.38f, 0.26f, 1.5f),
                    MapFeature.Forest(0.42f, -0.98f, 0.2f, 1.3f),
                    MapFeature.Forest(-0.15f, 0.8f, 0.22f, 1.3f),
                    MapFeature.Forest(0.95f, -0.2f, 0.18f, 1.2f),
                    MapFeature.Forest(-0.85f, 0.95f, 0.2f, 1f),
                    MapFeature.Forest(-0.55f, -0.85f, 0.15f, 1.2f),
                    MapFeature.Ridge(30, 26, -0.6f, -0.98f, -0.18f, -1.0f),
                    MapFeature.Ridge(34, 30, 0.62f, 0.92f, 0.98f, 0.62f),
                    MapFeature.Ridge(28, 24, -1.0f, 0.15f, -0.86f, -0.4f),
                    MapFeature.Town(-0.44f, 0.34f, 0.1f, 5),
                    MapFeature.Town(0.5f, -0.1f, 0.08f, 3),
                    MapFeature.Town(0.6f, -0.82f, 0.08f, 3),
                    MapFeature.Boulders(-0.08f, 0.24f, 0.1f, 6),
                    MapFeature.Boulders(-0.05f, -0.6f, 0.08f, 5),
                },
                posts = new[]
                {
                    new PostDef("Ranger Camp", -0.75f, 0.55f, 0, VKind.Tank, VKind.Transport, VKind.Air),
                    new PostDef("Mill Ruins", -0.3f, 0.2f, 0, VKind.Transport),
                    new PostDef("Heartwood", 0.0f, -0.05f, -1),
                    new PostDef("Waterfall", -0.25f, -0.55f, -1, VKind.Transport),
                    new PostDef("Shrine Hill", 0.35f, -0.25f, 1, VKind.Transport),
                    new PostDef("Logging Yard", 0.74f, -0.6f, 1, VKind.Tank, VKind.Transport, VKind.Air),
                }
            },
            new LandMapDef
            {
                name = "Frostfall Ridge", planet = "Frostfall", theme = Theme.Snow, seed = 3371, hills = 0.7f, props = 0.6f,
                blurb = "A great ridge splits the valley: one pass at the Ridge Relay, a frozen lake below and a long flank round the south cliffs.",
                features = new[]
                {
                    MapFeature.Ridge(56, 46, -0.5f, 0.12f, -0.1f, 0.1f, 0.3f, 0.18f),
                    MapFeature.Ridge(44, 40, -0.4f, 0.82f, 0.0f, 0.88f, 0.38f, 0.84f),
                    MapFeature.Ridge(42, 34, 0.18f, -0.78f, 0.5f, -0.62f, 0.76f, -0.45f),
                    MapFeature.Ridge(30, 26, -0.98f, 0.45f, -0.8f, 0.62f),
                    MapFeature.Lake(0.0f, -0.35f, 0.2f),
                    MapFeature.Road(9, -0.8f, 0.0f, -0.62f, 0.22f, -0.42f, 0.38f),
                    MapFeature.Road(9, -0.8f, 0.0f, -0.45f, -0.25f, 0.0f, -0.35f),
                    MapFeature.Road(8, -0.42f, 0.38f, -0.2f, 0.5f, 0.0f, 0.45f),
                    MapFeature.Road(8, 0.0f, 0.45f, -0.04f, 0.12f, 0.0f, -0.35f),
                    MapFeature.Road(9, 0.0f, 0.45f, 0.4f, 0.55f, 0.8f, 0.3f),
                    MapFeature.Road(9, 0.0f, -0.35f, 0.25f, -0.25f, 0.42f, -0.05f),
                    MapFeature.Road(9, 0.42f, -0.05f, 0.62f, 0.1f, 0.8f, 0.3f),
                    MapFeature.Road(7, -0.8f, 0.0f, -0.5f, -0.6f, 0.0f, -0.9f, 0.6f, -0.95f, 0.88f, -0.4f, 0.8f, 0.3f),
                    MapFeature.Forest(-0.5f, -0.58f, 0.18f, 1.3f),
                    MapFeature.Forest(0.62f, -0.22f, 0.12f, 1.2f),
                    MapFeature.Forest(-0.22f, 0.66f, 0.12f, 1.1f),
                    MapFeature.Forest(0.7f, 0.66f, 0.14f, 1.1f),
                    MapFeature.Town(-0.9f, 0.26f, 0.1f, 5),
                    MapFeature.Town(0.92f, 0.04f, 0.09f, 3),
                    MapFeature.Town(0.2f, 0.62f, 0.07f, 3),
                    MapFeature.Boulders(0.2f, -0.05f, 0.1f, 8),
                    MapFeature.Boulders(-0.28f, -0.1f, 0.1f, 6),
                    MapFeature.Boulders(0.55f, 0.42f, 0.1f, 6),
                },
                posts = new[]
                {
                    new PostDef("Echo Base", -0.8f, 0.0f, 0, VKind.Tank, VKind.Transport, VKind.Air),
                    new PostDef("Ice Trench", -0.42f, 0.38f, 0, VKind.Tank),
                    new PostDef("Ridge Relay", 0.0f, 0.45f, -1),
                    new PostDef("Frozen Lake", 0.0f, -0.35f, -1, VKind.Transport),
                    new PostDef("Shield Bunker", 0.42f, -0.05f, 1, VKind.Tank),
                    new PostDef("Glacier Port", 0.8f, 0.3f, 1, VKind.Tank, VKind.Transport, VKind.Air),
                }
            },
            new LandMapDef
            {
                name = "Cinder Reach", planet = "Cinder", theme = Theme.Volcanic, seed = 4489, hills = 0.7f, props = 0.7f,
                blurb = "Two lava rivers cut the black glass plain into thirds. Bridges are the only way across - and the caldera sits between them.",
                features = new[]
                {
                    MapFeature.Lava(16, -0.38f, -1.1f, -0.32f, -0.5f, -0.36f, 0.1f, -0.3f, 0.6f, -0.36f, 1.1f),
                    MapFeature.Lava(16, 0.36f, -1.1f, 0.32f, -0.45f, 0.37f, 0.15f, 0.3f, 0.55f, 0.4f, 1.1f),
                    MapFeature.Crater(0.0f, 0.0f, 0.22f, 14),
                    MapFeature.Ridge(40, 52, 0.08f, 0.94f, 0.24f, 0.98f),
                    MapFeature.Ridge(30, 24, -0.15f, -0.45f, 0.15f, -0.5f),
                    MapFeature.Ridge(30, 28, -0.95f, -0.3f, -0.92f, 0.0f),
                    MapFeature.Ridge(30, 28, 0.95f, 0.35f, 0.92f, 0.05f),
                    MapFeature.Road(9, -0.7f, -0.7f, -0.78f, -0.3f, -0.7f, 0.1f),
                    MapFeature.Road(8, -0.7f, -0.7f, -0.35f, -0.45f, 0.0f, 0.0f),
                    MapFeature.Road(8, -0.7f, 0.1f, -0.35f, 0.05f, 0.0f, 0.0f),
                    MapFeature.Road(8, -0.7f, 0.1f, -0.55f, 0.5f, -0.25f, 0.7f, 0.0f, 0.68f),
                    MapFeature.Road(8, 0.0f, 0.0f, 0.05f, 0.35f, 0.0f, 0.68f),
                    MapFeature.Road(8, 0.0f, 0.0f, 0.35f, -0.05f, 0.7f, -0.1f),
                    MapFeature.Road(8, 0.0f, 0.68f, 0.35f, 0.75f, 0.7f, 0.72f),
                    MapFeature.Road(9, 0.7f, -0.1f, 0.8f, 0.3f, 0.7f, 0.72f),
                    MapFeature.Road(7, -0.7f, -0.7f, -0.2f, -0.85f, 0.35f, -0.8f, 0.62f, -0.45f, 0.7f, -0.1f),
                    MapFeature.Forest(-0.6f, 0.82f, 0.14f, 0.9f),
                    MapFeature.Forest(0.62f, -0.75f, 0.14f, 0.9f),
                    MapFeature.Town(-0.88f, 0.34f, 0.1f, 5),
                    MapFeature.Town(0.88f, -0.32f, 0.1f, 5),
                    MapFeature.Town(-0.5f, -0.88f, 0.1f, 4),
                    MapFeature.Boulders(0.55f, 0.35f, 0.1f, 8),
                    MapFeature.Boulders(-0.55f, -0.25f, 0.1f, 8),
                    MapFeature.Boulders(0.0f, 0.42f, 0.06f, 4),
                },
                posts = new[]
                {
                    new PostDef("Forge Gate", -0.7f, -0.7f, 0, VKind.Tank, VKind.Transport, VKind.Air),
                    new PostDef("Smelter", -0.7f, 0.1f, 0, VKind.Transport),
                    new PostDef("Caldera", 0.0f, 0.0f, -1, VKind.Tank),
                    new PostDef("Obsidian Spire", 0.0f, 0.68f, -1),
                    new PostDef("Ash Works", 0.7f, -0.1f, 1, VKind.Transport),
                    new PostDef("Magma Dock", 0.7f, 0.72f, 1, VKind.Tank, VKind.Transport, VKind.Air),
                }
            },
            new LandMapDef
            {
                name = "Novo Meridian", planet = "Meridian", theme = Theme.Urban, seed = 5107, hills = 0.25f, props = 0.3f,
                blurb = "A city at dusk: a grid of streets between towers, gutted blocks and parks. Buildings you can enter, a central square, and tanks in the avenues.",
                features = new[]
                {
                    // 8 x 8 blocks, streets every 54 m; blocks round a post become open plazas
                    MapFeature.City(0, 0, 0.94f, 0.94f, 54),
                    MapFeature.Boulders(-0.95f, 0.2f, 0.08f, 5),
                    MapFeature.Boulders(0.95f, -0.2f, 0.08f, 5),
                },
                posts = new[]
                {
                    new PostDef("Harbor Gate", -0.822f, -0.822f, 0, VKind.Tank, VKind.Transport, VKind.Air),
                    new PostDef("Metro Station", -0.352f, -0.117f, 0, VKind.Transport),
                    new PostDef("Riverside Market", -0.587f, 0.587f, -1),
                    new PostDef("Central Plaza", 0f, 0f, -1, VKind.Tank),
                    new PostDef("Stadium", 0.587f, -0.587f, -1),
                    new PostDef("Old Cathedral", 0.352f, 0.117f, 1, VKind.Transport),
                    new PostDef("Sky Rail Depot", 0.822f, 0.822f, 1, VKind.Tank, VKind.Transport, VKind.Air),
                }
            },
            new LandMapDef
            {
                name = "Briar Hollow", planet = "Briar", theme = Theme.Forest, seed = 6131, hills = 0.3f, props = 0.45f,
                blurb = "A small farming town: narrow lanes, houses you can clear room by room, stone walls and hedges across the fields, and a clock tower over the square.",
                features = new[]
                {
                    // a ring of lanes round the town, the high street through it and a cross street over the square
                    MapFeature.Road(9, -0.78f, 0.62f, -0.42f, 0.22f, 0f, 0f, 0.42f, -0.22f, 0.78f, -0.62f),
                    MapFeature.Road(7, -0.42f, 0.22f, -0.52f, -0.12f, -0.35f, -0.5f),
                    MapFeature.Road(7, -0.35f, -0.5f, 0.05f, -0.42f, 0.42f, -0.22f),
                    MapFeature.Road(7, 0.42f, -0.22f, 0.52f, 0.12f, 0.35f, 0.5f),
                    MapFeature.Road(7, 0.35f, 0.5f, -0.05f, 0.42f, -0.42f, 0.22f),
                    MapFeature.Road(8, -0.35f, -0.5f, -0.12f, -0.2f, 0f, 0f, 0.12f, 0.2f, 0.35f, 0.5f),
                    MapFeature.Tower(0.16f, 0.1f, 22),
                    MapFeature.Tower(-0.25f, -0.62f, 15),
                    MapFeature.Tower(0.25f, 0.62f, 15),
                    MapFeature.Village(0f, 0f, 0.4f, 50),
                    MapFeature.Village(-0.62f, 0.42f, 0.12f, 7),
                    MapFeature.Village(0.62f, -0.42f, 0.12f, 7),
                    MapFeature.Village(-0.5f, -0.62f, 0.1f, 5),
                    MapFeature.Village(0.5f, 0.62f, 0.1f, 5),
                    MapFeature.Forest(-0.88f, -0.15f, 0.2f, 1.4f),
                    MapFeature.Forest(0.88f, 0.15f, 0.2f, 1.4f),
                    MapFeature.Forest(-0.2f, 0.88f, 0.16f, 1.2f),
                    MapFeature.Forest(0.2f, -0.88f, 0.16f, 1.2f),
                    MapFeature.Forest(0.78f, 0.88f, 0.14f, 1f),
                    MapFeature.Forest(-0.78f, -0.88f, 0.14f, 1f),
                    // field walls and hedges (gaps every few metres)
                    MapFeature.Wall(1.3f, -0.98f, 0.95f, -0.5f, 0.98f),
                    MapFeature.Wall(1.3f, 0.98f, -0.95f, 0.5f, -0.98f),
                    MapFeature.Wall(1.3f, -0.98f, 0.32f, -0.66f, 0.18f, -0.62f, -0.15f),
                    MapFeature.Wall(1.3f, 0.98f, -0.32f, 0.66f, -0.18f, 0.62f, 0.15f),
                    MapFeature.Wall(1.2f, -0.7f, -0.3f, -0.4f, -0.28f, -0.15f, -0.65f),
                    MapFeature.Wall(1.2f, 0.7f, 0.3f, 0.4f, 0.28f, 0.15f, 0.65f),
                    MapFeature.Wall(1.2f, -0.3f, 0.75f, 0.05f, 0.7f),
                    MapFeature.Wall(1.2f, 0.3f, -0.75f, -0.05f, -0.7f),
                    MapFeature.Boulders(-0.2f, 0.62f, 0.07f, 4),
                    MapFeature.Boulders(0.2f, -0.62f, 0.07f, 4),
                },
                posts = new[]
                {
                    new PostDef("Northgate Farm", -0.78f, 0.62f, 0, VKind.Tank, VKind.Transport, VKind.Air),
                    new PostDef("Old Mill", -0.42f, 0.22f, 0, VKind.Transport),
                    new PostDef("Chapel", -0.35f, -0.5f, -1),
                    new PostDef("Town Square", 0f, 0f, -1, VKind.Tank),
                    new PostDef("Orchard", 0.35f, 0.5f, -1),
                    new PostDef("Brewery", 0.42f, -0.22f, 1, VKind.Transport),
                    new PostDef("Southgate Farm", 0.78f, -0.62f, 1, VKind.Tank, VKind.Transport, VKind.Air),
                }
            },
            Skyreach(),
        };

        /// <summary>
        /// The floating map: islands at many heights joined by walkways with flights of stairs. One side is written
        /// out here and mirrored through the middle for the other team (the Sky Shrine on top is shared).
        /// </summary>
        static LandMapDef Skyreach()
        {
            // id, x, z, radius (map units), level (m)
            var isles = new (string id, float x, float z, float r, float lv)[]
            {
                ("base", -0.8f, -0.62f, 0.19f, 24), ("fwd", -0.45f, -0.15f, 0.12f, 36), ("low", 0.15f, -0.66f, 0.14f, 14),
                ("a1", -0.62f, -0.36f, 0.07f, 30), ("a2", -0.4f, -0.78f, 0.08f, 18), ("a3", -0.25f, -0.06f, 0.055f, 44),
                ("a4", -0.08f, -0.4f, 0.07f, 32), ("a5", -0.5f, 0.3f, 0.08f, 24), ("a6", -0.12f, -0.75f, 0.06f, 16), ("a7", -0.33f, 0.5f, 0.05f, 20),
            };
            var links = new (string a, string b)[]
            {
                ("base", "a1"), ("a1", "fwd"), ("base", "a2"), ("a2", "a6"), ("a6", "low"), ("fwd", "a3"), ("a3", "shrine"),
                ("low", "a4"), ("a4", "shrine"), ("fwd", "a5"), ("a5", "a7"), ("a7", "low'"),
            };
            var all = new System.Collections.Generic.Dictionary<string, (float x, float z, float r, float lv)> { ["shrine"] = (0, 0, 0.15f, 52) };
            foreach (var i in isles) { all[i.id] = (i.x, i.z, i.r, i.lv); all[i.id + "'"] = (-i.x, -i.z, i.r, i.lv); }
            var f = new System.Collections.Generic.List<MapFeature>();
            foreach (var kv in all) f.Add(MapFeature.Isle(kv.Value.x, kv.Value.z, kv.Value.r, kv.Value.lv));
            void Link(string a, string b)
            {
                var A = all[a]; var B = all[b];
                Vector2 pa = new Vector2(A.x, A.z), pb = new Vector2(B.x, B.z), d = (pb - pa).normalized;
                Vector2 p0 = pa + d * A.r * 0.78f, p1 = pb - d * B.r * 0.78f;
                f.Add(MapFeature.Span(8, p0.x, p0.y, p1.x, p1.y));
            }
            string Mirror(string id) => id == "shrine" ? id : id.EndsWith("'") ? id.TrimEnd('\'') : id + "'";
            foreach (var l in links) { Link(l.a, l.b); Link(Mirror(l.a), Mirror(l.b)); }
            f.Add(MapFeature.Tower(-0.25f, -0.06f, 14));
            f.Add(MapFeature.Tower(0.25f, 0.06f, 14));
            return new LandMapDef
            {
                name = "Skyreach Isles", planet = "Skyreach", theme = Theme.Sky, seed = 7211, hills = 0, props = 1.1f,
                blurb = "Islands float over a sea of cloud, joined by walkways and long flights of stairs. Fight up to the Sky Shrine on top - and don't fall.",
                features = f.ToArray(),
                posts = new[]
                {
                    new PostDef("Dawn Dock", -0.8f, -0.62f, 0, VKind.Air, VKind.Air).Home(),
                    new PostDef("Wind Terrace", -0.45f, -0.15f, 0),
                    new PostDef("Cloud Garden", 0.15f, -0.66f, -1),
                    new PostDef("Sky Shrine", 0f, 0f, -1),
                    new PostDef("Mist Hollow", -0.15f, 0.66f, -1),
                    new PostDef("Storm Terrace", 0.45f, 0.15f, 1),
                    new PostDef("Dusk Dock", 0.8f, 0.62f, 1, VKind.Air, VKind.Air).Home(),
                }
            };
        }

        /// <summary>
        /// The battle royale map: every land biome but the floating isles, side by side on one 1.6 km world, plus three new
        /// zones (a military base, farmlands with a crashed warship, a desert spaceport). Map units: 1 = 576 m from the middle.
        /// </summary>
        public static readonly LandMapDef Royale = BuildRoyale();

        static LandMapDef BuildRoyale()
        {
            var f = new System.Collections.Generic.List<MapFeature>
            {
                // roads between the nine regions (a grid with four diagonals); bridges appear where they cross water or lava
                MapFeature.Road(10, -0.72f, 0.72f, -0.36f, 0.76f, 0f, 0.74f, 0.36f, 0.75f, 0.72f, 0.72f),
                MapFeature.Road(10, -0.75f, 0f, -0.4f, 0.02f, 0f, 0f, 0.4f, -0.02f, 0.75f, 0f),
                MapFeature.Road(10, -0.72f, -0.72f, -0.36f, -0.74f, 0f, -0.76f, 0.36f, -0.73f, 0.72f, -0.72f),
                MapFeature.Road(10, -0.72f, 0.72f, -0.76f, 0.36f, -0.75f, 0f, -0.74f, -0.36f, -0.72f, -0.72f),
                MapFeature.Road(10, 0f, 0.74f, 0.02f, 0.36f, 0f, 0f, -0.02f, -0.38f, 0f, -0.76f),
                MapFeature.Road(10, 0.72f, 0.72f, 0.76f, 0.36f, 0.75f, 0f, 0.74f, -0.36f, 0.72f, -0.72f),
                MapFeature.Road(9, -0.72f, 0.72f, -0.4f, 0.38f, -0.2f, 0.2f),
                MapFeature.Road(9, 0.72f, 0.72f, 0.4f, 0.4f, 0.2f, 0.2f),
                MapFeature.Road(9, -0.72f, -0.72f, -0.38f, -0.4f, -0.2f, -0.2f),
                MapFeature.Road(9, 0.72f, -0.72f, 0.4f, -0.38f, 0.2f, -0.2f),

                // hamlets at the crossroads between regions: more buildings to clear and loot
                MapFeature.Village(-0.4f, 0.4f, 0.055f, 7),
                MapFeature.Village(0.42f, 0.42f, 0.055f, 7),
                MapFeature.Village(-0.4f, -0.38f, 0.055f, 7),
                MapFeature.Village(0.42f, -0.38f, 0.055f, 7),
                MapFeature.Village(0.03f, 0.42f, 0.05f, 6),
                MapFeature.Village(-0.03f, -0.44f, 0.05f, 6),
                MapFeature.Village(-0.44f, 0.04f, 0.05f, 6),
                MapFeature.Village(0.38f, 0.06f, 0.045f, 5),

                // NW - Frostfall: a long ridge, a frozen lake and the research station
                MapFeature.Ridge(54, 44, -1.05f, 0.5f, -0.86f, 0.48f, -0.66f, 0.5f),
                MapFeature.Ridge(44, 38, -0.5f, 1.02f, -0.42f, 0.9f),
                MapFeature.Lake(-0.52f, 0.6f, 0.11f),
                MapFeature.Compound(-0.84f, 0.84f, 0.11f, 6, 2),
                MapFeature.Town(-0.6f, 0.86f, 0.07f, 4),
                MapFeature.Forest(-0.95f, 0.66f, 0.1f, 1.1f),
                MapFeature.Forest(-0.38f, 0.5f, 0.08f, 1f),
                MapFeature.Boulders(-0.66f, 0.98f, 0.08f, 6),

                // N - Highwood: a river through deep forest, a mill village and its bridges
                MapFeature.River(18, 2.2f, -0.22f, 1.2f, -0.12f, 0.96f, -0.18f, 0.74f, -0.08f, 0.56f, 0.04f, 0.44f, 0.18f, 0.36f),
                MapFeature.Lake(0.22f, 0.33f, 0.06f),
                MapFeature.Ford(-0.15f, 0.84f, 0.05f),
                MapFeature.Village(0.1f, 0.62f, 0.08f, 12),
                MapFeature.Forest(-0.3f, 0.95f, 0.13f, 1.5f),
                MapFeature.Forest(0.26f, 0.92f, 0.12f, 1.4f),
                MapFeature.Forest(0.3f, 0.56f, 0.07f, 1.2f),
                MapFeature.Forest(-0.3f, 0.48f, 0.07f, 1.2f),

                // NE - Harrak: a mesa, a dry gulch and the mining town
                MapFeature.Ridge(46, 40, 0.48f, 0.98f, 0.62f, 1.0f, 0.84f, 0.94f),
                MapFeature.Ridge(40, 34, 1.02f, 0.44f, 0.96f, 0.6f),
                MapFeature.Wadi(20, 5, 0.42f, 0.46f, 0.56f, 0.52f, 0.7f, 0.48f, 0.9f, 0.52f),
                MapFeature.Town(0.78f, 0.62f, 0.12f, 18),
                MapFeature.Boulders(0.56f, 0.84f, 0.1f, 9),
                MapFeature.Boulders(0.95f, 0.8f, 0.08f, 6),

                // W - Briar Hollow: a dense village to clear house by house, hedged fields, the chapel
                MapFeature.Village(-0.76f, 0.02f, 0.13f, 36),
                MapFeature.Tower(-0.76f, 0.02f, 16),
                MapFeature.Tower(-0.62f, -0.14f, 11),
                MapFeature.Forest(-1.0f, 0.2f, 0.09f, 1f),
                MapFeature.Forest(-0.5f, 0.22f, 0.06f, 1f),
                MapFeature.Wall(1.3f, -1.02f, -0.18f, -0.86f, -0.24f, -0.7f, -0.22f),
                MapFeature.Wall(1.3f, -0.98f, 0.28f, -0.82f, 0.32f, -0.66f, 0.26f),
                MapFeature.Wall(1.2f, -0.5f, -0.12f, -0.48f, 0.14f),

                // centre - Novo Meridian: a grid of towers, buildings you can enter, parks and rubble
                MapFeature.City(0, 0, 0.27f, 0.27f, 52),

                // E - Cinder Foundry: a lava river crossed only on bridges, the caldera, a foundry town
                MapFeature.Lava(16, 0.56f, 0.32f, 0.52f, 0.14f, 0.57f, -0.06f, 0.52f, -0.24f, 0.56f, -0.36f),
                MapFeature.Crater(0.86f, 0.12f, 0.09f, 14),
                MapFeature.Town(0.9f, -0.2f, 0.1f, 12),
                MapFeature.Ridge(34, 30, 1.02f, -0.1f, 1.04f, 0.3f),
                MapFeature.Boulders(0.7f, 0.24f, 0.08f, 7),
                MapFeature.Boulders(0.7f, -0.28f, 0.07f, 6),

                // SW - Fort Halvard: a walled military base of barracks, with a vault
                MapFeature.Compound(-0.72f, -0.72f, 0.14f, 9, 0),
                MapFeature.Vault(-0.84f, -0.88f, 30),
                MapFeature.Boulders(-0.98f, -0.5f, 0.08f, 6),
                MapFeature.Forest(-0.44f, -0.96f, 0.08f, 0.8f),

                // S - Greenreach: farms and the crashed warship
                MapFeature.Village(0.16f, -0.66f, 0.08f, 9),
                MapFeature.Wreck(-0.16f, -0.86f, 24),
                MapFeature.Wall(1.2f, -0.2f, -0.5f, 0.02f, -0.46f, 0.24f, -0.5f),
                MapFeature.Wall(1.2f, 0.32f, -0.92f, 0.36f, -0.6f),
                MapFeature.Wall(1.2f, -0.36f, -0.6f, -0.3f, -0.42f),
                MapFeature.Forest(0.34f, -0.98f, 0.08f, 1.1f),
                MapFeature.Forest(-0.34f, -0.98f, 0.06f, 0.9f),

                // SE - Sunscar Spaceport: hangars round the landing pads, a control mast and a vault
                MapFeature.Compound(0.74f, -0.7f, 0.14f, 6, 1),
                MapFeature.Vault(0.93f, -0.91f, -40),
                MapFeature.Tower(0.58f, -0.88f, 22),
                MapFeature.Boulders(0.98f, -0.46f, 0.09f, 8),
                MapFeature.Wadi(18, 4, 0.42f, -0.42f, 0.5f, -0.5f, 0.46f, -0.62f),
            };
            return new LandMapDef
            {
                name = "Kharos Frontier", planet = "Kharos", theme = Theme.Forest, seed = 8807, size = 1600, cell = 3.125f, hills = 0.75f, props = 4.2f, royale = true,
                blurb = "Every land you have fought on, side by side on one world: snowfields, forest, desert, a village, a city, lava, a military base, farms and a spaceport.",
                features = f.ToArray(),
                posts = new PostDef[0],
                regions = new[]
                {
                    new RegionDef("Frostfall Station", Theme.Snow, -0.72f, 0.72f),
                    new RegionDef("Highwood", Theme.Forest, 0f, 0.75f),
                    new RegionDef("Harrak Mines", Theme.Desert, 0.72f, 0.72f),
                    new RegionDef("Briar Hollow", Theme.Forest, -0.75f, 0f),
                    new RegionDef("Novo Meridian", Theme.Urban, 0f, 0f),
                    new RegionDef("Cinder Foundry", Theme.Volcanic, 0.75f, 0f),
                    new RegionDef("Fort Halvard", Theme.Urban, -0.72f, -0.72f),
                    new RegionDef("Greenreach Farms", Theme.Forest, 0f, -0.75f),
                    new RegionDef("Sunscar Spaceport", Theme.Desert, 0.72f, -0.72f),
                },
            };
        }

        public static readonly SpaceMapDef[] Space =
        {
            new SpaceMapDef { name = "Harrak Orbit", bg = SpaceBg.Planet, seed = 11, blurb = "High above the dunes of Harrak, two capital ships trade broadsides.",
                neb1 = new Color(0.25f, 0.15f, 0.1f), neb2 = new Color(0.1f, 0.15f, 0.3f), sun = new Color(1f, 0.92f, 0.75f), planet = new Color(0.85f, 0.62f, 0.4f) },
            new SpaceMapDef { name = "Veil Nebula", bg = SpaceBg.Nebula, seed = 23, blurb = "A violet storm of gas where sensors fail and dogfights turn close.",
                neb1 = new Color(0.55f, 0.18f, 0.6f), neb2 = new Color(0.15f, 0.35f, 0.65f), sun = new Color(0.9f, 0.8f, 1f), planet = new Color(0.4f, 0.3f, 0.6f) },
            new SpaceMapDef { name = "Shattered Belt", bg = SpaceBg.Asteroids, seed = 37, blurb = "The fleets clash inside a field of drifting asteroids. Watch your wingtips.",
                neb1 = new Color(0.2f, 0.18f, 0.16f), neb2 = new Color(0.25f, 0.12f, 0.08f), sun = new Color(1f, 0.85f, 0.7f), planet = new Color(0.5f, 0.45f, 0.4f) },
        };
    }
}
