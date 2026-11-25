using System.Linq;

namespace ValheimSkies;

// The Weather setting: holds the game on one of its weathers (its environments), the way the game's own "env" console
// command does, through EnvMan.m_debugEnv. The game still blends into the new weather over a few seconds.
internal static class WeatherPicker
{
    public const string Natural = "Natural";

    // The game's environments. Those of the Mistlands, the Ashlands and the Deep North come with their locations, so a
    // name that is not in the world is reported in the log, with the names that are
    public static readonly string[] Choices =
    {
        Natural,
        "Clear", "Heath clear", "Misty", "LightRain", "Rain", "ThunderStorm",
        "DeepForest Mist", "Darklands_dark", "SwampRain",
        "Snow", "SnowStorm",
        "Mistlands_clear", "Mistlands_rain", "Mistlands_thunder",
        "Ashlands_ashrain", "Ashlands_misty", "Ashlands_CinderRain", "Ashlands_storm",
        "Twilight_Clear", "Twilight_Snow", "Twilight_SnowStorm",
        "Eikthyr", "GDKing", "Bonemass", "Moder", "GoblinKing", "Queen", "Fader",
    };

    // The Time of Day setting: holds the sun where the game's own debug time does (EnvMan.m_debugTimeOfDay). The day
    // fraction: the sun rises at about 0.25 and sets at about 0.75
    public static readonly string[] Times = { Natural, "Dawn", "Morning", "Noon", "Afternoon", "Sunset", "Dusk", "Night" };
    private static readonly float[] TimeFractions = { -1f, 0.26f, 0.35f, 0.5f, 0.63f, 0.72f, 0.76f, 0.95f };
    private static bool timeHeld;

    // What this setting last put into the game, so turning it back to Natural clears only our own choice
    private static string applied = "";
    private static string reported = "";

    public static void Tick()
    {
        EnvMan env = EnvMan.instance;
        if (env == null)
        {
            applied = "";
            return;
        }

        HoldTime(env);

        string pick = MainConfig.weather.Value;
        if (string.IsNullOrEmpty(pick) || pick == Natural)
        {
            if (applied.Length > 0 && env.m_debugEnv == applied)
            {
                env.m_debugEnv = "";
            }
            applied = "";
            return;
        }

        if (!env.m_environments.Any(e => e.m_name == pick))
        {
            if (reported != pick)
            {
                reported = pick;
                Jotunn.Logger.LogWarning($"[{ValheimSkiesMod.PluginName}] The weather '{pick}' is not in this world. Its weathers are: "
                    + string.Join(", ", env.m_environments.Select(e => e.m_name)));
            }
            return;
        }

        if (env.m_debugEnv != pick)
        {
            env.m_debugEnv = pick;
        }
        applied = pick;
    }

    private static void HoldTime(EnvMan env)
    {
        int index = System.Array.IndexOf(Times, MainConfig.timeOfDay.Value);
        if (index <= 0)
        {
            if (timeHeld)
            {
                env.m_debugTimeOfDay = false;
                timeHeld = false;
            }
            return;
        }
        env.m_debugTimeOfDay = true;
        env.m_debugTime = TimeFractions[index];
        timeHeld = true;
    }
}
