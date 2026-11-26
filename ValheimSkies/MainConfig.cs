using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using Jotunn.Extensions;
using UnityEngine;

namespace ValheimSkies;

internal static class MainConfig
{
    public static ConfigEntry<bool> modEnabled;
    public static ConfigEntry<string> sky;
    public static ConfigEntry<bool> randomSky;
    public static ConfigEntry<bool> showMessage;
    public static ConfigEntry<bool> hideYggdrasil;
    public static ConfigEntry<bool> physicalSky;
    public static ConfigEntry<float> physicalSkyStrength;
    public static ColourSetting daySky, sunsetSky;

    public static void Init(ConfigFile config)
    {
        modEnabled = config.BindConfigInOrder("General Settings", "Enable Plugin", defaultValue: true, "Enable the ValheimSkies plugin.\n(Restart required. It is read before the game starts, so it is a local setting).\n", synced: false, sectionOrder: true, settingOrder: true);

        sky = config.BindConfigInOrder("Sky", "Sky", defaultValue: "Nebula Sunrise", "The sky to show instead of the game's own. Vanilla leaves the game's sky alone. The sky changes straight away if you are in a world. The skies are most visible at night, when the stars come out.", synced: false, sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueList<string>(SkyCatalog.Choices));
        randomSky = config.BindConfigInOrder("Sky", "Random Sky", defaultValue: false, "If On, a different sky is picked each time you enter a world, and the Sky setting is ignored.", synced: false, sectionOrder: true, settingOrder: true);
        showMessage = config.BindConfigInOrder("Sky", "Show Message", defaultValue: true, "If On, a short message in the top-left corner tells you which sky is showing when it changes.", synced: false, sectionOrder: true, settingOrder: true);
        hideYggdrasil = config.BindConfigInOrder("Sky", "Hide Yggdrasil", defaultValue: false, "If On, the big tree (Yggdrasil) that grows up into the sky is hidden. It changes straight away if you are in a world.", synced: false, sectionOrder: true, settingOrder: true);

        physicalSky = config.BindConfigInOrder("Physical Sky", "Physical Sky", defaultValue: false, "If On, a simple atmosphere colours the sky by day: the Day Sky colour overhead, paler toward the horizon, a soft glow around the sun, and the Sunset colour along the horizon at sunrise and sunset. It gives way to the game's own sky at night (so the stars and your chosen sky show) and in rain, mist and storms. Changes show straight away.", synced: false, sectionOrder: true, settingOrder: true);
        physicalSkyStrength = config.BindConfigInOrder("Physical Sky", "Strength", defaultValue: 0.5f, "How much the atmosphere covers the game's own sky by day: 0 not at all, 1 fully.", synced: false, sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueRange<float>(0f, 1f));
        daySky = new ColourSetting(config, "Physical Sky", "Day Sky", "the sky overhead by day (paler toward the horizon)", 0.6f, 0.6f, 0.9f);
        sunsetSky = new ColourSetting(config, "Physical Sky", "Sunset", "the glow along the horizon at sunrise and sunset (with the game's time of day, tod, about 0.2 to 0.3 and 0.7 to 0.8)", 0.05f, 0.75f, 1f);

        CarryOverOldSections(config);

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

// A colour setting as three sliders, as EinherjarBodies' eye colour: hue, saturation and brightness, each 0 to 1
internal sealed class ColourSetting
{
    private const string HueNote = " Around the colour wheel: 0 red, 0.08 orange, 0.17 yellow, 0.33 green, 0.5 cyan, 0.67 blue, 0.75 purple, 0.83 magenta, 0.92 pink, 1 red again.";

    private readonly ConfigEntry<float> hue, saturation, brightness;

    public ColourSetting(ConfigFile config, string section, string name, string what, float defaultHue, float defaultSaturation, float defaultBrightness)
    {
        hue = config.BindConfigInOrder(section, name + " Hue", defaultValue: defaultHue, "The colour of " + what + "." + HueNote, synced: false, sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueRange<float>(0f, 1f));
        saturation = config.BindConfigInOrder(section, name + " Saturation", defaultValue: defaultSaturation, "How strong the colour of " + what + " is: 0 grey, 1 the full colour.", synced: false, sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueRange<float>(0f, 1f));
        brightness = config.BindConfigInOrder(section, name + " Brightness", defaultValue: defaultBrightness, "How bright " + what + " is: 0 black, 1 the brightest.", synced: false, sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueRange<float>(0f, 1f));
    }

    public Color Value => Color.HSVToRGB(Mathf.Clamp01(hue.Value), Mathf.Clamp01(saturation.Value), Mathf.Clamp01(brightness.Value));
}
