using BepInEx;
using HarmonyLib;
using Jotunn.Utils;
using System.Reflection;

namespace ValheimSkies;

[BepInPlugin(PluginGUID, PluginName, PluginVersion)]
[BepInDependency("com.jotunn.jotunn", BepInDependency.DependencyFlags.HardDependency)]
[NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.None)]
public class ValheimSkiesMod : BaseUnityPlugin
{
    public const string PluginName = "ValheimSkies";
    public const string PluginVersion = "1.0.5";
    public const string PluginAuthor = "MagicMike";
    internal const string PluginGUID = "MagicMike.ValheimSkies";
    internal const string PluginCopy = "Copyright © MagicMike 2026";
    public const string PluginLink = "https://discord.gg/mnr36G6J4x";

    private Harmony harmony;

    public void Awake()
    {
        MainConfig.Init(Config);
        MagicMike.ModSettings.ModSettings.Init(this);

        if (!MainConfig.modEnabled.Value)
        {
            return;
        }

        Translations.Load(this);
        SkyChanger.Init(this);
        harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), PluginGUID);
    }

    public void Update()
    {
        SkyChanger.Tick();
    }

    public void OnDestroy()
    {
        harmony?.UnpatchSelf();
        SkyChanger.Shutdown();
    }
}
