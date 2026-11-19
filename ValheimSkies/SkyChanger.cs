using System.Collections;
using System.IO;
using System.Reflection;
using BepInEx;
using UnityEngine;

namespace ValheimSkies;

// Puts the chosen sky's cubemap on the game's sky material, in place of the star field texture it draws, and takes it
// off again for Vanilla. Only the sky showing is kept in memory: the others stay in the asset bundle, on disk.
internal static class SkyChanger
{
    // The texture slot of the game's sky material that holds the cubemap of the stars and space
    private const string TextureProperty = "_StarFieldTex";

    private const string BundleName = "valheim_skies";

    private const float CheckInterval = 1f;

    private static BaseUnityPlugin plugin;
    private static AssetBundle bundle;
    private static bool bundleFailed;

    // The sky material being changed, and the texture it had before this mod touched it
    private static Material material;
    private static Texture vanillaTexture;

    // The cubemap now on the material (null while it has the game's own texture) and its name in the bundle
    private static Cubemap shown;
    private static string shownAsset;

    // The sky wanted by the latest request; an earlier request that finishes loading after it gives way
    private static int request;
    private static string wantedAsset;

    // The sky picked by Random Sky, kept until the next world is entered
    private static SkyInfo randomPick;

    // The tree in the sky, found by name (a hidden object cannot be found again, so it is kept)
    private const string YggdrasilName = "YggdrasilBranch";
    private static GameObject yggdrasil;

    private static float nextCheck;
    private static bool warnedMissingProperty;

    public static void Init(BaseUnityPlugin owner)
    {
        plugin = owner;
    }

    // Each time a world's scene starts: a new random pick, and no message, as the sky is not changing before the player's eyes
    public static void OnWorldLoaded()
    {
        Apply(newPick: true, announce: false);
    }

    public static void OnPlayerSpawned()
    {
        yggdrasil = GameObject.Find(YggdrasilName);
        if (yggdrasil == null)
        {
            Jotunn.Logger.LogDebug($"[{ValheimSkiesMod.PluginName}] The tree in the sky ('{YggdrasilName}') was not found.");
        }
        ApplyYggdrasil();
    }

    // Hides or shows the tree in the sky as the setting says
    public static void ApplyYggdrasil()
    {
        if (yggdrasil != null)
        {
            yggdrasil.SetActive(!MainConfig.hideYggdrasil.Value);
        }
    }

    public static void OnSettingChanged(bool newPick)
    {
        if (EnvMan.instance != null)
        {
            Apply(newPick, announce: true);
        }
    }

    // While a sky is showing, put it back if the game has put its own texture back on the material
    public static void Tick()
    {
        if (shown == null || Time.unscaledTime < nextCheck)
        {
            return;
        }
        nextCheck = Time.unscaledTime + CheckInterval;

        Material skybox = RenderSettings.skybox;
        if (skybox == null || EnvMan.instance == null || !skybox.HasProperty(TextureProperty))
        {
            return;
        }
        if (skybox != material)
        {
            Track(skybox);
        }
        if (skybox.GetTexture(TextureProperty) != shown)
        {
            skybox.SetTexture(TextureProperty, shown);
        }
    }

    public static void Shutdown()
    {
        request++;
        if (yggdrasil != null)
        {
            yggdrasil.SetActive(true);
        }
        if (material != null && vanillaTexture != null)
        {
            material.SetTexture(TextureProperty, vanillaTexture);
        }
        shown = null;
    }

    private static void Apply(bool newPick, bool announce)
    {
        Material skybox = RenderSettings.skybox;
        if (skybox == null)
        {
            return;
        }
        if (!skybox.HasProperty(TextureProperty))
        {
            if (!warnedMissingProperty)
            {
                warnedMissingProperty = true;
                Jotunn.Logger.LogWarning($"[{ValheimSkiesMod.PluginName}] The game's sky material '{skybox.name}' (shader '{skybox.shader.name}') has no '{TextureProperty}' texture, so the sky cannot be changed.");
            }
            return;
        }
        if (skybox != material)
        {
            Track(skybox);
        }

        SkyInfo target = Choose(newPick);
        int mine = ++request;
        wantedAsset = target?.Asset;

        if (target == null)
        {
            Show(null, null);
            if (announce)
            {
                Say(Translations.T("valheimskies_sky_vanilla"));
            }
        }
        else if (shown != null && shownAsset == target.Asset)
        {
            skybox.SetTexture(TextureProperty, shown);
            if (announce)
            {
                Say(Translations.T(target.TranslationKey));
            }
        }
        else
        {
            plugin.StartCoroutine(Load(target, mine, announce));
        }
    }

    // The sky wanted by the settings, or null for the game's own
    private static SkyInfo Choose(bool newPick)
    {
        if (MainConfig.randomSky.Value)
        {
            if (newPick || randomPick == null)
            {
                randomPick = SkyCatalog.PickRandom(randomPick);
            }
            return randomPick;
        }
        return SkyCatalog.Find(MainConfig.sky.Value);
    }

    private static void Track(Material skybox)
    {
        material = skybox;
        Texture current = skybox.GetTexture(TextureProperty);
        if (current != shown)
        {
            vanillaTexture = current;
        }
    }

    private static IEnumerator Load(SkyInfo target, int mine, bool announce)
    {
        AssetBundle skies = GetBundle();
        if (skies == null)
        {
            yield break;
        }
        AssetBundleRequest loading = skies.LoadAssetAsync<Cubemap>(target.Asset);
        yield return loading;

        Cubemap cubemap = loading.asset as Cubemap;
        if (cubemap == null)
        {
            Jotunn.Logger.LogError($"[{ValheimSkiesMod.PluginName}] The sky '{target.Asset}' is not in the asset bundle {BundleName}.");
            yield break;
        }

        // A newer request came in while this one loaded: leave the showing to it, and let go of this cubemap if it is not wanted
        if (mine != request)
        {
            if (wantedAsset != target.Asset && cubemap != shown)
            {
                Resources.UnloadAsset(cubemap);
            }
            yield break;
        }

        Material skybox = RenderSettings.skybox;
        if (skybox == null || !skybox.HasProperty(TextureProperty))
        {
            if (cubemap != shown)
            {
                Resources.UnloadAsset(cubemap);
            }
            yield break;
        }
        if (skybox != material)
        {
            Track(skybox);
        }

        Show(cubemap, target.Asset);
        if (announce)
        {
            Say(Translations.T(target.TranslationKey));
        }
    }

    // Puts the cubemap (or the game's own texture, for null) on the material and lets go of the one it replaced
    private static void Show(Cubemap cubemap, string asset)
    {
        Cubemap old = shown;
        shown = cubemap;
        shownAsset = asset;
        if (material != null)
        {
            material.SetTexture(TextureProperty, cubemap != null ? cubemap : vanillaTexture);
        }
        if (old != null && old != cubemap)
        {
            Resources.UnloadAsset(old);
        }
    }

    private static void Say(string skyName)
    {
        if (MainConfig.showMessage.Value && Player.m_localPlayer != null)
        {
            Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, Translations.T("valheimskies_sky_changed", skyName));
        }
    }

    private static AssetBundle GetBundle()
    {
        if (bundle != null || bundleFailed)
        {
            return bundle;
        }
        string path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), BundleName);
        bundle = File.Exists(path) ? AssetBundle.LoadFromFile(path) : null;
        if (bundle == null)
        {
            bundleFailed = true;
            Jotunn.Logger.LogError($"[{ValheimSkiesMod.PluginName}] Failed to load the asset bundle {BundleName} (expected at {path}).");
        }
        return bundle;
    }
}
