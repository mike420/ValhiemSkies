using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace ValheimSkies;

// What ValheimSkies draws on the sky right after the skybox on the main camera (so the game's clouds, sun and fog come
// after), from the valheim_physical_sky bundle. Each is one full-screen draw shaded only where sky shows; nothing is
// measured or read back.
//  - Physical Sky (section Physical Sky): a simple atmosphere over the game's sky by day, in the Day Sky and Sunset
//    colours (unity/PhysicalSky/PhysicalSky.shader). It gives way to the game's own sky at night.
//  - Moon (section Moon): MagicMike's moon picture opposite the sun, where the game draws its own plain white moon
//    (which is hidden meanwhile: the skybox's _MoonSize set to almost nothing), fading in at dusk and out at dawn
//    (unity/PhysicalSky/Moon.shader).
// Physical Sky fades out in rain, mist and storms (eased over a few seconds as the weather changes) and in dark
// places; the moon stays in any weather, as the game's own does (the game's clouds pass in front of it).
internal static class PhysicalSky
{
    private const string BundleName = "valheim_physical_sky";
    private const string Folder = "assets/valheimskies/physicalsky/";
    private const CameraEvent Event = CameraEvent.AfterSkybox;
    private const float WeatherSeconds = 4f;

    private static readonly int RayBLId = Shader.PropertyToID("_PSRayBL");
    private static readonly int RayTLId = Shader.PropertyToID("_PSRayTL");
    private static readonly int RayTRId = Shader.PropertyToID("_PSRayTR");
    private static readonly int RayBRId = Shader.PropertyToID("_PSRayBR");
    private static readonly int DayId = Shader.PropertyToID("_PSDay");
    private static readonly int SunsetId = Shader.PropertyToID("_PSSunset");
    private static readonly int SunDirId = Shader.PropertyToID("_PSSunDir");
    private static readonly int CoverId = Shader.PropertyToID("_PSCover");
    private static readonly int MoonDirId = Shader.PropertyToID("_MoonDir");
    private static readonly int MoonShapeId = Shader.PropertyToID("_MoonShape");
    private static readonly int SkyboxSunDirId = Shader.PropertyToID("_SkyboxSunDir");
    private static readonly int GameMoonSizeId = Shader.PropertyToID("_MoonSize");

    private static readonly Vector3[] Corners = new Vector3[4];
    private static readonly CommandBuffer commands = new CommandBuffer { name = "ValheimSkies physical sky and moon" };
    private static AssetBundle bundle;
    private static Material skyMaterial, moonMaterial;
    private static bool loadFailed;
    private static Camera camera;
    private static bool drawingSky, drawingMoon;
    private static float weather = -1f;
    // The game's moon size while ours is on (and the skybox it came from)
    private static Material hiddenMoonOn;
    private static float hiddenMoonSize;

    public static void Tick()
    {
        Camera main = Camera.main;
        EnvMan env = EnvMan.instance;
        bool wantSky = MainConfig.physicalSky.Value;
        bool wantMoon = MainConfig.moon.Value;
        HideGameMoon(MainConfig.modEnabled.Value && wantMoon && env != null);
        if (!MainConfig.modEnabled.Value || (!wantSky && !wantMoon) || main == null || env == null || !Load())
        {
            Detach();
            weather = -1f;
            return;
        }

        // Clear weather 1, rain, mist, storms and dark places 0 (from the game's own values for the weather)
        EnvSetup current = env.GetCurrentEnvironment();
        float clear = 1f;
        if (current != null)
        {
            clear = (1f - Mathf.Clamp01(current.m_rainCloudAlpha / 1.2f)) * (1f - Mathf.Clamp01((current.m_fogDensityDay - 0.008f) / 0.015f));
            if (current.m_alwaysDark)
            {
                clear = 0f;
            }
        }
        weather = weather < 0f ? clear : Mathf.Lerp(weather, clear, 1f - Mathf.Exp(-Time.deltaTime / WeatherSeconds));

        Vector4 sun = Shader.GetGlobalVector(SkyboxSunDirId);
        Vector3 sunDir = sun.sqrMagnitude > 1e-6f ? ((Vector3)sun).normalized : Vector3.up;

        float cover = weather * Mathf.Clamp01(MainConfig.physicalSkyStrength.Value);
        bool sky = wantSky && cover >= 0.001f && sunDir.y >= -0.2f;

        // The moon opposite the sun: shows as the sun sets (fully once it is 6 degrees below the horizon) and while
        // the moon is above the horizon
        Vector3 moonDir = -sunDir;
        float night = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.02f, -0.1f, sunDir.y));
        float risen = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.03f, 0.05f, moonDir.y));
        float moonShows = night * risen;
        bool moon = wantMoon && moonShows >= 0.001f;

        if (!sky && !moon)
        {
            Detach();
            return;
        }
        if (main != camera || sky != drawingSky || moon != drawingMoon)
        {
            Detach();
            camera = main;
            drawingSky = sky;
            drawingMoon = moon;
            if (sky)
            {
                commands.DrawProcedural(Matrix4x4.identity, skyMaterial, 0, MeshTopology.Triangles, 3);
            }
            if (moon)
            {
                commands.DrawProcedural(Matrix4x4.identity, moonMaterial, 0, MeshTopology.Triangles, 3);
            }
            camera.AddCommandBuffer(Event, commands);
        }

        Transform view = main.transform;
        main.CalculateFrustumCorners(new Rect(0f, 0f, 1f, 1f), 1f, Camera.MonoOrStereoscopicEye.Mono, Corners);
        Shader.SetGlobalVector(RayBLId, view.TransformVector(Corners[0]));
        Shader.SetGlobalVector(RayTLId, view.TransformVector(Corners[1]));
        Shader.SetGlobalVector(RayTRId, view.TransformVector(Corners[2]));
        Shader.SetGlobalVector(RayBRId, view.TransformVector(Corners[3]));
        if (sky)
        {
            skyMaterial.SetColor(DayId, MainConfig.daySky.Value.linear);
            skyMaterial.SetColor(SunsetId, MainConfig.sunsetSky.Value.linear);
            skyMaterial.SetVector(SunDirId, sunDir);
            skyMaterial.SetVector(CoverId, new Vector4(cover, 0f, 0f, 0f));
        }
        if (moon)
        {
            float radius = Mathf.Tan(0.5f * Mathf.Clamp(MainConfig.moonSize.Value, 0.5f, 30f) * Mathf.Deg2Rad);
            moonMaterial.SetVector(MoonDirId, moonDir);
            moonMaterial.SetVector(MoonShapeId, new Vector4(radius, Mathf.Max(0f, MainConfig.moonBrightness.Value), Mathf.Clamp01(MainConfig.moonGlow.Value), moonShows));
        }
    }

    public static void Shutdown()
    {
        HideGameMoon(false);
        Detach();
        commands.Release();
        foreach (Material material in new[] { skyMaterial, moonMaterial })
        {
            if (material != null)
            {
                Object.Destroy(material);
            }
        }
        skyMaterial = moonMaterial = null;
        if (bundle != null)
        {
            bundle.Unload(true);
            bundle = null;
        }
    }

    // The game's own moon (drawn by its skybox, plain white) hidden while ours shows, and its size put back after
    private static void HideGameMoon(bool hide)
    {
        Material skybox = RenderSettings.skybox;
        if (hiddenMoonOn != null && (!hide || hiddenMoonOn != skybox))
        {
            hiddenMoonOn.SetFloat(GameMoonSizeId, hiddenMoonSize);
            hiddenMoonOn = null;
        }
        if (hide && hiddenMoonOn == null && skybox != null && skybox.HasProperty(GameMoonSizeId))
        {
            hiddenMoonOn = skybox;
            hiddenMoonSize = skybox.GetFloat(GameMoonSizeId);
            skybox.SetFloat(GameMoonSizeId, 1e-5f);
        }
    }

    private static void Detach()
    {
        if (camera != null)
        {
            camera.RemoveCommandBuffer(Event, commands);
        }
        camera = null;
        commands.Clear();
        drawingSky = drawingMoon = false;
    }

    private static bool Load()
    {
        if (skyMaterial != null)
        {
            return true;
        }
        if (loadFailed)
        {
            return false;
        }
        loadFailed = true;
        string path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), BundleName);
        bundle = File.Exists(path) ? AssetBundle.LoadFromFile(path) : null;
        Shader sky = bundle != null ? bundle.LoadAsset<Shader>(Folder + "physicalsky.shader") : null;
        Shader moon = bundle != null ? bundle.LoadAsset<Shader>(Folder + "moon.shader") : null;
        Texture2D moonPicture = bundle != null ? bundle.LoadAsset<Texture2D>(Folder + "moon.png") : null;
        if (sky == null || moon == null || moonPicture == null)
        {
            Jotunn.Logger.LogError($"[{ValheimSkiesMod.PluginName}] The Physical Sky and Moon were not found ({BundleName}). They stay off.");
            return false;
        }
        if (!sky.isSupported || !moon.isSupported)
        {
            Jotunn.Logger.LogWarning($"[{ValheimSkiesMod.PluginName}] Physical Sky and Moon are not supported with this graphics API ({SystemInfo.graphicsDeviceType}). They stay off.");
            return false;
        }
        skyMaterial = new Material(sky) { hideFlags = HideFlags.HideAndDontSave };
        moonMaterial = new Material(moon) { hideFlags = HideFlags.HideAndDontSave };
        moonMaterial.SetTexture("_MainTex", moonPicture);
        loadFailed = false;
        return true;
    }
}
