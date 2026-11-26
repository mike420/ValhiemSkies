using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace ValheimSkies;

// Physical Sky (section Physical Sky): a simple atmosphere over the game's sky by day, in the day and sunset colours
// of the settings (unity/PhysicalSky/PhysicalSky.shader). One draw right after the skybox on the main camera, shaded
// only where sky shows; nothing is measured or read back. It gives way to the game's own sky at night, in rain, mist
// and storms (eased over a few seconds as the weather changes), and in crypts and other dark places.
internal static class PhysicalSky
{
    private const string BundleName = "valheim_physical_sky";
    private const string ShaderAsset = "assets/valheimskies/physicalsky/physicalsky.shader";
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
    private static readonly int SkyboxSunDirId = Shader.PropertyToID("_SkyboxSunDir");

    private static readonly Vector3[] Corners = new Vector3[4];
    private static readonly CommandBuffer commands = new CommandBuffer { name = "ValheimSkies physical sky" };
    private static AssetBundle bundle;
    private static Material material;
    private static bool loadFailed;
    private static Camera camera;
    private static float weather = -1f;

    public static void Tick()
    {
        Camera main = Camera.main;
        EnvMan env = EnvMan.instance;
        if (!MainConfig.modEnabled.Value || !MainConfig.physicalSky.Value || main == null || env == null || !Load())
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
        float cover = weather * Mathf.Clamp01(MainConfig.physicalSkyStrength.Value);

        Vector4 sun = Shader.GetGlobalVector(SkyboxSunDirId);
        Vector3 sunDir = sun.sqrMagnitude > 1e-6f ? ((Vector3)sun).normalized : Vector3.up;
        if (cover < 0.001f || sunDir.y < -0.2f)
        {
            Detach();
            return;
        }

        if (main != camera)
        {
            Detach();
            camera = main;
            commands.Clear();
            commands.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
            camera.AddCommandBuffer(Event, commands);
        }

        Transform view = main.transform;
        main.CalculateFrustumCorners(new Rect(0f, 0f, 1f, 1f), 1f, Camera.MonoOrStereoscopicEye.Mono, Corners);
        material.SetVector(RayBLId, view.TransformVector(Corners[0]));
        material.SetVector(RayTLId, view.TransformVector(Corners[1]));
        material.SetVector(RayTRId, view.TransformVector(Corners[2]));
        material.SetVector(RayBRId, view.TransformVector(Corners[3]));
        material.SetColor(DayId, MainConfig.daySky.Value.linear);
        material.SetColor(SunsetId, MainConfig.sunsetSky.Value.linear);
        material.SetVector(SunDirId, sunDir);
        material.SetVector(CoverId, new Vector4(cover, 0f, 0f, 0f));
    }

    public static void Shutdown()
    {
        Detach();
        commands.Release();
        if (material != null)
        {
            Object.Destroy(material);
            material = null;
        }
        if (bundle != null)
        {
            bundle.Unload(true);
            bundle = null;
        }
    }

    private static void Detach()
    {
        if (camera != null)
        {
            camera.RemoveCommandBuffer(Event, commands);
        }
        camera = null;
    }

    private static bool Load()
    {
        if (material != null)
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
        Shader shader = bundle != null ? bundle.LoadAsset<Shader>(ShaderAsset) : null;
        if (shader == null)
        {
            Jotunn.Logger.LogError($"[{ValheimSkiesMod.PluginName}] The Physical Sky shader was not found ({BundleName}). Physical Sky stays off.");
            return false;
        }
        if (!shader.isSupported)
        {
            Jotunn.Logger.LogWarning($"[{ValheimSkiesMod.PluginName}] Physical Sky is not supported with this graphics API ({SystemInfo.graphicsDeviceType}). It stays off.");
            return false;
        }
        material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        loadFailed = false;
        return true;
    }
}
