using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using Jotunn.Extensions;

namespace ValheimSkies;

internal static class MainConfig
{
    public static ConfigEntry<bool> modEnabled;
    public static ConfigEntry<string> sky;
    public static ConfigEntry<bool> randomSky;
    public static ConfigEntry<bool> showMessage;
    public static ConfigEntry<bool> hideYggdrasil;
    public static ConfigEntry<bool> volumetricClouds;
    public static ConfigEntry<VolumetricClouds.Quality> cloudQuality;
    public static ConfigEntry<float> cloudAmount;
    public static ConfigEntry<float> cloudBrightness;
    public static ConfigEntry<float> cloudNightBrightness;
    public static ConfigEntry<float> cloudShadows;
    public static ConfigEntry<bool> hideGameClouds;
    public static ConfigEntry<HighClouds> highClouds;
    public static ConfigEntry<bool> atmosphereSky;

    public enum HighClouds
    {
        Off,
        Weather,
        Always
    }
    public static ConfigEntry<string> weather;
    public static ConfigEntry<string> timeOfDay;

    public static void Init(ConfigFile config)
    {
        modEnabled = config.BindConfigInOrder("General Settings", "Enable Plugin", defaultValue: true, "Enable the ValheimSkies plugin.\n(Restart required. It is read before the game starts, so it is a local setting).\n", synced: false, sectionOrder: true, settingOrder: true);

        sky = config.BindConfigInOrder("Sky", "Sky", defaultValue: "Nebula Sunrise", "The sky to show instead of the game's own. Vanilla leaves the game's sky alone. The sky changes straight away if you are in a world. The skies are most visible at night, when the stars come out.", synced: false, sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueList<string>(SkyCatalog.Choices));
        randomSky = config.BindConfigInOrder("Sky", "Random Sky", defaultValue: false, "If On, a different sky is picked each time you enter a world, and the Sky setting is ignored.", synced: false, sectionOrder: true, settingOrder: true);
        showMessage = config.BindConfigInOrder("Sky", "Show Message", defaultValue: true, "If On, a short message in the top-left corner tells you which sky is showing when it changes.", synced: false, sectionOrder: true, settingOrder: true);
        hideYggdrasil = config.BindConfigInOrder("Sky", "Hide Yggdrasil", defaultValue: false, "If On, the big tree (Yggdrasil) that grows up into the sky is hidden. It changes straight away if you are in a world.", synced: false, sectionOrder: true, settingOrder: true);

        volumetricClouds = config.BindConfigInOrder("Clouds", "Volumetric Clouds", defaultValue: true, "If On, the sky gets 3D volumetric clouds that follow the weather: scattered clouds when it is clear, a grey cover in rain and snow, and towering dark clouds in storms. They take GPU time; lower the Cloud Quality if the game slows down. Windows only (DirectX 11 or Vulkan). It changes straight away if you are in a world.", synced: false, sectionOrder: true, settingOrder: true);
        cloudQuality = config.BindConfigInOrder("Clouds", "Cloud Quality", defaultValue: VolumetricClouds.Quality.Medium, "How carefully the clouds are drawn. Low is the quickest; Ultra draws them at full resolution and is much slower.", synced: false, sectionOrder: true, settingOrder: true);
        cloudAmount = config.BindConfigInOrder("Clouds", "Cloud Amount", defaultValue: 1f, "How much of the sky the clouds cover, on top of what the weather brings. 0 is no clouds.", synced: false, sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueRange<float>(0f, 1.1f));
        cloudBrightness = config.BindConfigInOrder("Clouds", "Cloud Brightness", defaultValue: 1f, "How brightly the sun, the moon and the sky light the clouds.", synced: false, sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueRange<float>(0.25f, 3f));
        cloudNightBrightness = config.BindConfigInOrder("Clouds", "Night Brightness", defaultValue: 0.3f, "How brightly the clouds are lit at night. At the default they are dark shapes against the stars; at 1 they are faintly moonlit.", synced: false, sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueRange<float>(0f, 1f));
        cloudShadows = config.BindConfigInOrder("Clouds", "Cloud Shadows", defaultValue: 0.6f, "How dark the shadows the clouds cast on the ground are. 0 is no shadows. They fade out as the sun gets low, are faint by moonlight and softer under rain clouds.", synced: false, sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueRange<float>(0f, 1f));
        highClouds = config.BindConfigInOrder("Clouds", "High Clouds", defaultValue: HighClouds.Weather, "The thin layers above the main clouds: wispy cirrus streaks high up, and small puffy altocumulus a little above the cumulus. Weather: some fair days have them, some do not, and a few other weathers have them too. Always: every dry day has both. Off: never.", synced: false, sectionOrder: true, settingOrder: true);
        atmosphereSky = config.BindConfigInOrder("Clouds", "Atmosphere Sky", defaultValue: true, "If On (with Volumetric Clouds), the daytime sky is a physically based atmosphere that the clouds are lit by and fade into: pale towards the horizon, pink and gold at sunrise and sunset. It fades out through dusk, so your chosen sky shows at night. If Off, the game's (or your chosen) sky shows by day too.", synced: false, sectionOrder: true, settingOrder: true);
        hideGameClouds = config.BindConfigInOrder("Clouds", "Hide Game Clouds", defaultValue: true, "If On, the game's own flat cloud layers are hidden while the volumetric clouds show.", synced: false, sectionOrder: true, settingOrder: true);

        weather = config.BindConfigInOrder("Weather", "Weather", defaultValue: WeatherPicker.Natural, "Holds the game on one weather, wherever you are (as the game's env console command does). Natural leaves the weather to the game. The game blends into the new weather over a few seconds. The weathers of the Mistlands, the Ashlands and the Deep North only exist once the world has them; if one is missing, the log lists the world's weathers.", synced: false, sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueList<string>(WeatherPicker.Choices));

        timeOfDay = config.BindConfigInOrder("Weather", "Time of Day", defaultValue: WeatherPicker.Natural, "Holds the sun at one time of day (as the game's tod console command does). Natural lets the day pass as usual.", synced: false, sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueList<string>(WeatherPicker.Times));

        CarryOverOldSections(config);

        volumetricClouds.SettingChanged += (_, _) => VolumetricClouds.OnSettingChanged();
        hideGameClouds.SettingChanged += (_, _) => VolumetricClouds.OnSettingChanged();
        cloudShadows.SettingChanged += (_, _) => VolumetricClouds.OnSettingChanged();
        cloudQuality.SettingChanged += (_, _) => VolumetricClouds.OnSettingChanged();
        sky.SettingChanged += (_, _) => SkyChanger.OnSettingChanged(newPick: false);
        hideYggdrasil.SettingChanged += (_, _) => SkyChanger.ApplyYggdrasil();
        randomSky.SettingChanged += (_, _) => SkyChanger.OnSettingChanged(newPick: true);
    }

    private static readonly Dictionary<string, string[]> OldSections = new Dictionary<string, string[]>
    {
    };

    private static void CarryOverOldSections(ConfigFile config)
    {
        Dictionary<ConfigDefinition, string> orphans;
        try
        {
            PropertyInfo property = typeof(ConfigFile).GetProperty("OrphanedEntries", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            orphans = property?.GetValue(config) as Dictionary<ConfigDefinition, string>;
        }
        catch (System.Exception e)
        {
            Jotunn.Logger.LogWarning("[ValheimSkies] Could not look for settings under old section names: " + e.Message);
            return;
        }
        if (orphans == null || orphans.Count == 0)
        {
            return;
        }

        int carried = 0, dropped = 0;
        HashSet<ConfigEntryBase> handled = new HashSet<ConfigEntryBase>();
        bool saveOnSet = config.SaveOnConfigSet;
        config.SaveOnConfigSet = false;
        try
        {
            // An earlier renumbering can leave the same setting under several old numbers; the highest is the latest
            foreach (ConfigDefinition old in orphans.Keys.OrderByDescending(d => SectionNumber(d.Section)).ToList())
            {
                string oldName = SectionName(old.Section);
                ConfigEntryBase entry = ((IDictionary<ConfigDefinition, ConfigEntryBase>)config).Values.FirstOrDefault(e => e.Definition.Key == old.Key && e.Definition.Section != old.Section
                    && (SectionName(e.Definition.Section) == oldName || OldSections.TryGetValue(SectionName(e.Definition.Section), out string[] before) && before.Contains(oldName)));
                if (entry == null)
                {
                    continue;   // (not one of ours any more: left as it is)
                }
                if (handled.Add(entry) && Equals(entry.BoxedValue, entry.DefaultValue))
                {
                    try
                    {
                        entry.SetSerializedValue(orphans[old]);
                        carried++;
                    }
                    catch (System.Exception e)
                    {
                        Jotunn.Logger.LogWarning("[ValheimSkies] Could not carry over " + old.Section + " / " + old.Key + ": " + e.Message);
                    }
                }
                orphans.Remove(old);
                dropped++;
            }
        }
        finally
        {
            config.SaveOnConfigSet = saveOnSet;
        }
        if (dropped > 0)
        {
            config.Save();
            Jotunn.Logger.LogInfo("[ValheimSkies] Carried " + carried + " setting(s) over from old section names in the config file.");
        }
    }

    // A section's name without the number in front ("4 - Fortified Ballista": Fortified Ballista)
    private static string SectionName(string section) => Regex.Replace(section ?? "", @"^\d+\s*-\s*", "");

    private static int SectionNumber(string section)
    {
        Match match = Regex.Match(section ?? "", @"^(\d+)\s*-");
        return match.Success ? int.Parse(match.Groups[1].Value) : -1;
    }
}
