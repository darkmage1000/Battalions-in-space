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

    public class LandMapDef
    {
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
                name = "Harrak Dunes", planet = "Harrak", theme = Theme.Desert, seed = 1041, hills = 0.8f, props = 0.9f,
                blurb = "Wind-carved dunes and sandstone arches on a sun-baked mining world. Long sight lines - bring a marksman.",
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
                name = "Elos Highwood", planet = "Elos", theme = Theme.Forest, seed = 2203, hills = 1.1f, props = 1.4f,
                blurb = "Giant trees on a green moon. Close fighting in the woods, open meadows between.",
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
                name = "Frostfall Ridge", planet = "Frostfall", theme = Theme.Snow, seed = 3371, hills = 1.35f, props = 0.8f,
                blurb = "A frozen valley under a jagged ridge. Whoever holds the ridge holds the battle.",
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
                name = "Cinder Reach", planet = "Cinder", theme = Theme.Volcanic, seed = 4489, hills = 1.15f, props = 1f,
                blurb = "Black glass plains between smoking vents and lava rivers on a world still being born.",
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
