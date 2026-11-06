using UnityEngine;

namespace BIS
{
    public enum Theme { Desert, Forest, Snow, Volcanic }
    public enum SpaceBg { Planet, Nebula, Asteroids }

    public class PostDef
    {
        public string name;
        public Vector2 pos;          // -1..1 of the playable half size
        public int owner;            // 0 = team A, 1 = team B, -1 = neutral
        public VKind[] vehicles;
        public PostDef(string n, float x, float z, int o, params VKind[] v) { name = n; pos = new Vector2(x, z); owner = o; vehicles = v; }
    }

    public enum FeatureKind { Ridge, River, Lava, Wadi, Road, Forest, Town, Boulders, Lake, Crater, Ford }

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
    }

    public class LandMapDef
    {
        public MapFeature[] features = new MapFeature[0];
        public string name, planet, blurb;
        public Theme theme;
        public int seed;
        public float size = 640, hills = 1, roughness = 1, props = 1;
        public PostDef[] posts;
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
        };

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
