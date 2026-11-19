using BepInEx.Configuration;
using Jotunn.Extensions;

namespace ValheimSkies;

internal static class MainConfig
{
    public static ConfigEntry<bool> modEnabled;
    public static ConfigEntry<string> sky;
    public static ConfigEntry<bool> randomSky;
    public static ConfigEntry<bool> showMessage;

    public static void Init(ConfigFile config)
    {
        modEnabled = config.BindConfigInOrder("General Settings", "Enable Plugin", defaultValue: true, "Enable the ValheimSkies plugin.\n(Restart required. It is read before the game starts, so it is a local setting).\n", synced: false, sectionOrder: true, settingOrder: true);

        sky = config.BindConfigInOrder("Sky", "Sky", defaultValue: "Nebula Sunrise", "The sky to show instead of the game's own. Vanilla leaves the game's sky alone. The sky changes straight away if you are in a world. The skies are most visible at night, when the stars come out.", synced: false, sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueList<string>(SkyCatalog.Choices));
        randomSky = config.BindConfigInOrder("Sky", "Random Sky", defaultValue: false, "If On, a different sky is picked each time you enter a world, and the Sky setting is ignored.", synced: false, sectionOrder: true, settingOrder: true);
        showMessage = config.BindConfigInOrder("Sky", "Show Message", defaultValue: true, "If On, a short message in the top-left corner tells you which sky is showing when it changes.", synced: false, sectionOrder: true, settingOrder: true);

        sky.SettingChanged += (_, _) => SkyChanger.OnSettingChanged(newPick: false);
        randomSky.SettingChanged += (_, _) => SkyChanger.OnSettingChanged(newPick: true);
    }
}
