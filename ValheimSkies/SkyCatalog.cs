using System;
using System.Linq;

namespace ValheimSkies;

// One sky: the name shown in the settings, and the name of its cubemap inside the valheim_skies asset bundle
internal sealed class SkyInfo
{
    public readonly string Name;
    public readonly string Asset;

    public SkyInfo(string name, string asset)
    {
        Name = name;
        Asset = asset;
    }

    // The key in the translation files for this sky's name
    public string TranslationKey => "valheimskies_" + Asset;
}

// Keep in step with SkyBundleBuilder in the Unity project (the asset names are made there)
internal static class SkyCatalog
{
    public const string Vanilla = "Vanilla";

    public static readonly SkyInfo[] Skies =
    {
        new SkyInfo("Edge-On Galaxy", "sky_edge_on_galaxy"),
        new SkyInfo("Spiral Galaxy", "sky_spiral_galaxy"),
        new SkyInfo("Blue Horizon", "sky_blue_horizon"),
        new SkyInfo("Galaxy Field", "sky_galaxy_field"),
        new SkyInfo("Deep Field", "sky_deep_field"),
        new SkyInfo("Nebula Sunrise", "sky_nebula_sunrise"),
        new SkyInfo("Spiral Void", "sky_spiral_void"),
        new SkyInfo("Milky Way Rift", "sky_milky_way_rift"),
        new SkyInfo("Ember Nebula", "sky_ember_nebula"),
        new SkyInfo("Dust Lane", "sky_dust_lane"),
        new SkyInfo("Cosmic Clouds", "sky_cosmic_clouds"),
        new SkyInfo("Cosmos 1", "sky_cosmos_1"),
        new SkyInfo("Cosmos 4", "sky_cosmos_4"),
        new SkyInfo("Nebula 30", "sky_nebula_30"),
        new SkyInfo("Nebula 39", "sky_nebula_39"),
        new SkyInfo("Nebula 64", "sky_nebula_64"),
    };

    // What the Sky setting can be: the game's own sky first, then every sky from the bundle
    public static string[] Choices => new[] { Vanilla }.Concat(Skies.Select(s => s.Name)).ToArray();

    public static SkyInfo Find(string name)
    {
        return Skies.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    // Any sky but the one given, so a random pick is never the sky already showing
    public static SkyInfo PickRandom(SkyInfo not)
    {
        SkyInfo[] others = Skies.Where(s => s != not).ToArray();
        return others[UnityEngine.Random.Range(0, others.Length)];
    }
}
