using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace ValheimSkies;

// Draws volumetric clouds into the game's main camera: a port of HDRP's volumetric clouds to the built-in render
// pipeline (the shader is Hidden/ValheimSkies/VolumetricClouds in the valheim_clouds asset bundle). Each frame,
// just before the transparent objects, a command buffer on the camera:
//   1. traces the clouds at a lower resolution, stopping at the ground (from the camera's depth texture),
//   2. blends that with last frame's clouds, moved to where they now are, which averages out the ray noise,
//   3. lays the clouds over the camera image, upscaled along the edges of the ground.
// Before the camera draws, the clouds' shadows are traced into a cookie for the game's sun (UpdateShadowCookie).
// The clouds' look follows the weather (CloudWeather), and they are lit by the game's own sun or moon and ambient light.
internal static class VolumetricClouds
{
    public enum Quality
    {
        Low,
        Medium,
        High,
        Ultra
    }

    private const string BundleName = "valheim_clouds";
    private const string ShaderAsset = "assets/valheimskies/clouds/volumetricclouds.shader";
    private const string WorleyAsset = "assets/valheimskies/clouds/worleynoise128rgba.png";
    private const string ErosionAsset = "assets/valheimskies/clouds/perlinnoise32rgb.png";
    private const string ErosionWorleyAsset = "assets/valheimskies/clouds/worleynoise32rgb.png";
    private const string HighMapAsset = "assets/valheimskies/clouds/defaultcloudmap.png";
    private const string AtmosphereAsset = "assets/valheimskies/clouds/atmosphere.shader";

    // HDRP's planet: the Earth, with the sea at the game's water level
    private const float PlanetRadius = 6360000f;
    private const float SeaLevel = 30f;

    // In a dungeon (the game puts them high above the world) there is no sky to see
    private const float DungeonHeight = 4000f;

    // A camera that moves further than this in one frame has been teleported: last frame's clouds are no use
    private const float TeleportDistance = 100f;

    private const float HistoryWeight = 0.9f;


    // The width of the cloud shadow cookie around the camera (HDRP's default shadow distance is 8 km; wider here,
    // so shadows show on the land seen from hills)
    private const float CookieSize = 10000f;

    // Cloud shadow strength by moonlight, and under a full rain cover, as shares of the Cloud Shadows setting
    private const float NightShadows = 0.3f;
    private const float WetShadows = 0.4f;

    // The cloud map: its width around the camera (the clouds fade out by 50 km) and its size in texels, and how fast
    // its noise changes (texture units per second of game time; the noise repeats every unit)
    private const float MapWidth = 100000f;
    private const int MapSize = 512;
    private const float MapEvolutionRate = 0.0002f;

    // The longest step of the ray march (HDRP uses an eighth of the layer's depth, too coarse for storm towers),
    // and the fade of the clouds right in front of the camera (only seen from inside a cloud)
    private const float MaxStepSize = 400f;
    private const float FadeInStart = 100f;
    private const float FadeInDistance = 300f;

    // How much the captured sky colours the clouds' ambient light and their haze
    private const float SkyAmbientShare = 1f;
    private const float SkyHazeShare = 1f;

    // The cirrus sheet's height above the sea (m)
    private const float CirrusAltitude = 9000f;

    // The light kept under the darkest (stormy) clouds
    private const float RainLight = 0.45f;

    // The least of the fog colour kept in far clouds at night, whatever Night Brightness is
    private const float NightFogFloor = 0.08f;
    private const float NightScale = 0.25f;

    // Clouds further away than this fade out (metres along the view)
    private const float FadeStart = 80000f;
    private const float FadeEnd = 120000f;

    private const CameraEvent Event = CameraEvent.BeforeImageEffectsOpaque;

    private static readonly int RayBLId = Shader.PropertyToID("_CloudRayBL");
    private static readonly int RayBRId = Shader.PropertyToID("_CloudRayBR");
    private static readonly int RayTLId = Shader.PropertyToID("_CloudRayTL");
    private static readonly int RayTRId = Shader.PropertyToID("_CloudRayTR");
    private static readonly int CameraId = Shader.PropertyToID("_CloudCamera");
    private static readonly int AltitudesId = Shader.PropertyToID("_CloudAltitudes");
    private static readonly int StepsId = Shader.PropertyToID("_CloudSteps");
    private static readonly int WindId = Shader.PropertyToID("_CloudWind");
    private static readonly int WindSpeedsId = Shader.PropertyToID("_CloudWindSpeeds");
    private static readonly int ShapeId = Shader.PropertyToID("_CloudShape");
    private static readonly int Shape2Id = Shader.PropertyToID("_CloudShape2");
    private static readonly int LightId = Shader.PropertyToID("_CloudLight");
    private static readonly int CoverId = Shader.PropertyToID("_CloudCover");
    private static readonly int FogColorId = Shader.PropertyToID("_CloudFogColor");
    private static readonly int FadeId = Shader.PropertyToID("_CloudFade");
    private static readonly int ShadowOriginId = Shader.PropertyToID("_CloudShadowOrigin");
    private static readonly int ShadowRightId = Shader.PropertyToID("_CloudShadowRight");
    private static readonly int ShadowUpId = Shader.PropertyToID("_CloudShadowUp");
    private static readonly int ShadowSunId = Shader.PropertyToID("_CloudShadowSun");
    private static readonly int ZParamsId = Shader.PropertyToID("_CloudZParams");
    private static readonly int ShadeId = Shader.PropertyToID("_CloudShade");
    private static readonly int TintId = Shader.PropertyToID("_CloudTint");
    private static readonly int GlowId = Shader.PropertyToID("_CloudGlow");
    private static readonly int MapExtraId = Shader.PropertyToID("_CloudMapExtra");
    private static readonly int MicroId = Shader.PropertyToID("_CloudMicro");
    private static readonly int SizesId = Shader.PropertyToID("_CloudSizes");
    private static readonly int HistoryId = Shader.PropertyToID("_CloudHistory");
    private static readonly int PrevViewProjId = Shader.PropertyToID("_CloudPrevViewProj");
    private static readonly int WorleyId = Shader.PropertyToID("_CloudWorley128");
    private static readonly int ErosionId = Shader.PropertyToID("_CloudErosion");
    private static readonly int ErosionWorleyId = Shader.PropertyToID("_CloudErosionWorley");
    private static readonly int KindsId = Shader.PropertyToID("_CloudKinds");
    private static readonly int MidId = Shader.PropertyToID("_CloudMid");
    private static readonly int BaseId = Shader.PropertyToID("_CloudBase");
    private static readonly int DeckId = Shader.PropertyToID("_CloudDeck");
    private static readonly int ReliefId = Shader.PropertyToID("_CloudRelief");
    private static readonly int HighId = Shader.PropertyToID("_CloudHigh");
    private static readonly int HighTurnId = Shader.PropertyToID("_CloudHighTurn");
    private static readonly int HighMapId = Shader.PropertyToID("_CloudHighMap");
    private static readonly int SkyCubeId = Shader.PropertyToID("_CloudSkyCube");
    private static readonly int SkyId = Shader.PropertyToID("_CloudSky");
    private static readonly int MapId = Shader.PropertyToID("_CloudMap");
    private static readonly int MapAreaId = Shader.PropertyToID("_CloudMapArea");
    private static readonly int MapAmountsId = Shader.PropertyToID("_CloudMapAmounts");
    private static readonly int TraceColorId = Shader.PropertyToID("_CloudTraceColor");
    private static readonly int TraceDepthId = Shader.PropertyToID("_CloudTraceDepth");
    private static readonly int HistoryTexId = Shader.PropertyToID("_CloudHistoryTex");
    private static readonly int AccumulatedId = Shader.PropertyToID("_CloudAccumulated");

    private static AssetBundle bundle;
    private static bool loadFailed;
    private static Material material;
    private static Texture worley;
    private static Texture erosion;

    private static CloudWeather weather;
    private static readonly CommandBuffer commands = new CommandBuffer { name = "ValheimSkies volumetric clouds" };

    // The camera the command buffer is on, and whether its depth texture was switched on here
    private static Camera camera;
    private static bool addedDepth;

    private static RenderTexture traceColor;
    private static RenderTexture traceDepth;

    // The cloud shadow cookie, the sun it is on, and the cookie that sun had before
    private static RenderTexture shadowCookie;

    // The cloud map (where the clouds are, and of what kind), redrawn each frame before the shadows and the clouds
    private static RenderTexture cloudMap;
    private static readonly CommandBuffer mapCommands = new CommandBuffer { name = "ValheimSkies cloud map" };
    private static float mapEvolution;
    private static Light cookieLight;
    private static Texture originalCookie;
    private static float originalCookieSize;
    private static readonly CommandBuffer cookieCommands = new CommandBuffer { name = "ValheimSkies cloud shadow cookie" };
    private static readonly RenderTexture[] history = new RenderTexture[2];
    private static readonly RenderTargetIdentifier[] traceTargets = new RenderTargetIdentifier[2];
    private static int current;
    private static bool historyValid;
    private static Matrix4x4 previousViewProjection;
    private static Vector3 previousCameraPosition;
    private static int frame;

    private static Vector2 windOffset;

    // The game's sky, rendered small into a cube now and then: the clouds take their ambient light and their haze
    // from it, so they turn the sky's colours (blue shadows by day, pink at sunset) whatever sky is showing
    // The physically based atmosphere (HDRP's): its lookup tables, rebuilt as the sun moves and the air changes
    private static Material atmosphereMaterial;
    private static RenderTexture multiScattering;
    private static RenderTexture skyView;
    private static readonly CommandBuffer atmosphereCommands = new CommandBuffer { name = "ValheimSkies atmosphere" };
    private static Vector4 builtAerosol = new Vector4(-1f, 0f, 0f, 0f);
    private static float atmosphereDay;
    // The atmosphere's horizon colour, read back from the GPU, that the game's distance fog leans towards by day
    private static RenderTexture horizon;
    private static Color horizonColor;
    private static bool haveHorizon;
    private static bool readingHorizon;
    private static float nextHorizonRead;
    // The game's own fog colour this frame, before the horizon is mixed into it
    private static Color gameFogColor;
    // How far the game's fog leans towards the sky's horizon by day (WeatherOverhaul's fog sky: half)
    private const float FogSkyShare = 0.5f;
    private const float TwilightExposure = 0.75f;

    private static Camera skyCamera;
    private static RenderTexture skyCube;
    private static float nextSkyCapture;
    private const float SkyCaptureInterval = 0.25f;
    private static CloudWeather.Regime loggedRegime = (CloudWeather.Regime)(-1);
    private static MainConfig.HighClouds loggedHigh = (MainConfig.HighClouds)(-1);
    // How far the wind has carried the cirrus (m), and the turn of their sky map (radians a game day)
    private static float cirrusTravel;
    private const float CirrusTurnRate = 0.6f;
    private static bool active;
    private static bool subscribed;

    // The game's own flat cloud layers, hidden while the volumetric clouds show
    private static readonly List<Renderer> gameClouds = new List<Renderer>();
    private static readonly HashSet<Renderer> hiddenClouds = new HashSet<Renderer>();
    private static EnvMan searchedIn;

    // Called every frame by the plugin: switches the clouds on or off and moves the weather and wind along
    public static void Tick()
    {
        bool wanted = MainConfig.modEnabled.Value && MainConfig.volumetricClouds.Value && EnvMan.instance != null && Camera.main != null && !loadFailed;
        if (wanted && !Load())
        {
            wanted = false;
        }

        if (wanted)
        {
            Camera main = Camera.main;
            if (main.transform.position.y > DungeonHeight)
            {
                wanted = false;
            }
            else if (main != camera)
            {
                Detach();
                Attach(main);
            }
        }

        if (!wanted)
        {
            if (active)
            {
                Detach();
                RestoreGameClouds();
                RestoreSunCookie();
                active = false;
            }
            return;
        }

        if (!active)
        {
            active = true;
            weather.Snap();
        }

        int seed = WorldGenerator.instance != null ? WorldGenerator.instance.GetSeed() : 0;
        weather.Update(EnvMan.instance.GetCurrentEnvironment(), EnvMan.instance.GetCurrentBiome(), seed, EnvMan.instance.GetDay(), Time.deltaTime);
        if (weather.Now != loggedRegime || MainConfig.highClouds.Value != loggedHigh)
        {
            loggedRegime = weather.Now;
            loggedHigh = MainConfig.highClouds.Value;
            Jotunn.Logger.LogInfo($"[{ValheimSkiesMod.PluginName}] Clouds: {weather.Now} (day {EnvMan.instance.GetDay()}, high clouds {loggedHigh}; cirrus and altocumulus ease towards {weather.TargetCirrus:0.00} and {weather.TargetAltocumulus:0.00})");
        }

        // The game's wind (0 to 1) as 15 to 90 km/h at cloud height. The noise is offset against the wind, so the clouds go with it
        Vector3 windDirection = EnvMan.instance.GetWindDir();
        Vector2 direction = new Vector2(windDirection.x, windDirection.z);
        direction = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector2.right;
        // The regime's wind at cloud height, more or less as the game's wind (0 to 1) is weak or strong
        float speed = weather.WindKmh * (0.6f + 0.8f * Mathf.Clamp01(EnvMan.instance.GetWindIntensity())) / 3.6f;
        windOffset -= direction * (speed * Time.deltaTime);
        if (windOffset.sqrMagnitude > 1e10f)
        {
            windOffset = Vector2.zero;
        }
        material.SetVector(WindId, new Vector4(windOffset.x, windOffset.y, -direction.x, -direction.y));

        // The cirrus: their sky map turns slowly over the days (never with the wind, which swings about), and the wind
        // carries them along in the turned map
        cirrusTravel = Mathf.Repeat(cirrusTravel + speed * Time.deltaTime, 2f * CirrusAltitude * 1000f);
        float turn = (seed % 1000) * 0.37f + (EnvMan.instance.GetDay() + EnvMan.instance.GetDayFraction()) * CirrusTurnRate;
        float cos = Mathf.Cos(turn), sin = Mathf.Sin(turn);
        material.SetVector(HighTurnId, new Vector4(cos, sin, -(direction.x * cos - direction.y * sin), -(direction.x * sin + direction.y * cos)));

        CaptureSky(Camera.main.transform.position);

        if (MainConfig.hideGameClouds.Value)
        {
            HideGameClouds();
        }
        else
        {
            RestoreGameClouds();
        }
    }

    // A world was left or entered: start over
    public static void OnWorldLoaded()
    {
        historyValid = false;
        RestoreGameClouds();
        RestoreSunCookie();
        gameClouds.Clear();
        searchedIn = null;
        weather?.Snap();
    }

    public static void OnSettingChanged()
    {
        historyValid = false;
        if (!MainConfig.hideGameClouds.Value || !MainConfig.volumetricClouds.Value)
        {
            RestoreGameClouds();
        }
        if (!MainConfig.volumetricClouds.Value || MainConfig.cloudShadows.Value <= 0f)
        {
            RestoreSunCookie();
        }
    }

    public static void Shutdown()
    {
        Detach();
        RestoreGameClouds();
        if (subscribed)
        {
            Camera.onPreCull -= OnPreCull;
            subscribed = false;
        }
        ReleaseTargets();
        ReleaseCookie();
        commands.Release();
        cookieCommands.Release();
        mapCommands.Release();
        if (cloudMap != null)
        {
            cloudMap.Release();
            Object.Destroy(cloudMap);
            cloudMap = null;
        }
        ReleaseSky();
        if (material != null)
        {
            Object.Destroy(material);
        }
        active = false;
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
        if (!File.Exists(path))
        {
            Jotunn.Logger.LogError($"[{ValheimSkiesMod.PluginName}] The volumetric clouds need the asset bundle {BundleName}, which is not at {path}. The clouds stay off.");
            return false;
        }
        bundle = AssetBundle.LoadFromFile(path);
        if (bundle == null)
        {
            Jotunn.Logger.LogError($"[{ValheimSkiesMod.PluginName}] Failed to load the asset bundle {BundleName}. The clouds stay off.");
            return false;
        }

        Shader shader = bundle.LoadAsset<Shader>(ShaderAsset);
        worley = bundle.LoadAsset<Texture3D>(WorleyAsset);
        erosion = bundle.LoadAsset<Texture3D>(ErosionAsset);
        Texture2D highMap = bundle.LoadAsset<Texture2D>(HighMapAsset);
        Texture3D erosionWorley = bundle.LoadAsset<Texture3D>(ErosionWorleyAsset);
        Shader atmosphereShader = bundle.LoadAsset<Shader>(AtmosphereAsset);
        if (shader == null || worley == null || erosion == null || erosionWorley == null || highMap == null || atmosphereShader == null)
        {
            Jotunn.Logger.LogError($"[{ValheimSkiesMod.PluginName}] The asset bundle {BundleName} is missing the clouds' shader or noise textures. The clouds stay off.");
            return false;
        }
        if (!shader.isSupported || !SystemInfo.supports3DTextures || SystemInfo.supportedRenderTargetCount < 2
            || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf) || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGFloat))
        {
            Jotunn.Logger.LogWarning($"[{ValheimSkiesMod.PluginName}] The volumetric clouds are not supported with this graphics API ({SystemInfo.graphicsDeviceType}); they work with DirectX 11 and Vulkan. The clouds stay off.");
            return false;
        }

        material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        atmosphereMaterial = new Material(atmosphereShader) { hideFlags = HideFlags.HideAndDontSave };
        weather = new CloudWeather();
        material.SetTexture(WorleyId, worley);
        material.SetTexture(ErosionId, erosion);
        material.SetTexture(ErosionWorleyId, erosionWorley);
        material.SetTexture(HighMapId, highMap);

        Camera.onPreCull += OnPreCull;
        subscribed = true;
        loadFailed = false;
        Jotunn.Logger.LogInfo($"[{ValheimSkiesMod.PluginName}] Volumetric clouds loaded ({SystemInfo.graphicsDeviceType}).");
        return true;
    }

    private static void Attach(Camera main)
    {
        camera = main;
        addedDepth = (main.depthTextureMode & DepthTextureMode.Depth) == 0;
        main.depthTextureMode |= DepthTextureMode.Depth;
        main.AddCommandBuffer(Event, commands);
        historyValid = false;
    }

    private static void Detach()
    {
        if (camera != null)
        {
            camera.RemoveCommandBuffer(Event, commands);
            if (addedDepth)
            {
                camera.depthTextureMode &= ~DepthTextureMode.Depth;
            }
        }
        camera = null;
        addedDepth = false;
        commands.Clear();
        historyValid = false;
    }

    // Fills the command buffer for this frame, just before the camera renders
    private static void OnPreCull(Camera cam)
    {
        if (!active || cam == null || cam != camera || material == null)
        {
            return;
        }

        gameFogColor = RenderSettings.fogColor;
        GetQuality(MainConfig.cloudQuality.Value, out int divider, out int primarySteps, out int lightSteps);
        int fullWidth = Mathf.Max(1, cam.pixelWidth);
        int fullHeight = Mathf.Max(1, cam.pixelHeight);
        int width = Mathf.Max(1, (fullWidth + divider - 1) / divider);
        int height = Mathf.Max(1, (fullHeight + divider - 1) / divider);
        if (traceColor == null || traceColor.width != width || traceColor.height != height)
        {
            Allocate(width, height);
        }

        Transform transform = cam.transform;
        Vector3 position = transform.position;
        if (Vector3.Distance(position, previousCameraPosition) > TeleportDistance)
        {
            historyValid = false;
        }

        // Rays to the frustum's corners at view depth 1 (bottom left, top left, top right, bottom right)
        Vector3[] corners = Corners;
        cam.CalculateFrustumCorners(new Rect(0f, 0f, 1f, 1f), 1f, Camera.MonoOrStereoscopicEye.Mono, corners);
        material.SetVector(RayBLId, transform.TransformVector(corners[0]));
        material.SetVector(RayTLId, transform.TransformVector(corners[1]));
        material.SetVector(RayTRId, transform.TransformVector(corners[2]));
        material.SetVector(RayBRId, transform.TransformVector(corners[3]));
        Shader.SetGlobalVector("_AtmoRayBL", transform.TransformVector(corners[0]));
        Shader.SetGlobalVector("_AtmoRayTL", transform.TransformVector(corners[1]));
        Shader.SetGlobalVector("_AtmoRayTR", transform.TransformVector(corners[2]));
        Shader.SetGlobalVector("_AtmoRayBR", transform.TransformVector(corners[3]));

        float altitude = Mathf.Max(1f, position.y - SeaLevel);
        material.SetVector(CameraId, new Vector4(position.x, PlanetRadius + altitude, position.z, 0f));

        float bottom = weather.BottomAltitude;
        float range = weather.AltitudeRange;
        material.SetVector(AltitudesId, new Vector4(PlanetRadius + bottom, PlanetRadius + bottom + range, PlanetRadius, MaxStepSize));
        material.SetVector(StepsId, new Vector4(primarySteps, lightSteps, Mathf.Max(FadeInStart, cam.nearClipPlane), FadeInDistance));
        // Cloud map, shape and erosion speeds, and the altitude distortion (HDRP's defaults)
        material.SetVector(WindSpeedsId, new Vector4(0.5f, 1f, 0.25f, 0.25f * 0.25f));
        float night = Night();
        // The weather's gloom eases off at night, where Night Brightness already darkens the clouds
        float gloom = Mathf.Lerp(weather.Gloom, 1f, night * 0.6f);
        bool micro = MainConfig.cloudQuality.Value >= Quality.High;
        // HDRP squares the density multiplier; the erosion compensation is for the Perlin erosion noise
        material.SetVector(ShapeId, new Vector4(weather.DensityMultiplier * weather.DensityMultiplier * 2f, weather.ShapeFactor, weather.ShapeScale, weather.ErosionFactor));
        material.SetVector(Shape2Id, new Vector4(weather.ErosionScale, 0.1f, 1f, Mathf.Lerp(1f, RainLight, weather.Rain) * gloom));
        material.SetVector(ShadeId, new Vector4(weather.Darkness, weather.BaseLight, 0f, 0f));
        material.SetVector(TintId, (Vector4)weather.Tint);
        material.SetVector(GlowId, (Vector4)weather.Glow);
        // z: how much the cloud cover varies over the map (WeatherOverhaul's variety, 0.67 on the clear day measured)
        material.SetVector(MapExtraId, new Vector4(weather.TowerHeight, weather.TowerRain, MapVariety, 0f));
        material.SetVector(KindsId, new Vector4(weather.CumulusDepth, weather.DeckDepth, 0f, 0f));
        material.SetVector(BaseId, weather.DeckBase);
        material.SetVector(DeckId, weather.DeckModel);
        material.SetVector(ReliefId, weather.DeckRelief);
        // x: how much of the clouds' ambient light takes the sky's colour, y: how much of their haze is the sky behind them
        // w: twilight, when the clouds take the low sun's deeper colour
        material.SetVector(SkyId, new Vector4(SkyAmbientShare, SkyHazeShare, 1f, Twilight() * (1f - Night())));
        // Altocumulus: cells about 400 to 500 m across (scale 35: the shape noise repeats every 2.9 km)
        material.SetVector(MidId, new Vector4(weather.AltocumulusAbove, CloudWeather.AltocumulusDepth, Mathf.Min(1f, weather.Altocumulus * MainConfig.cloudAmount.Value), 35f));
        // Cirrus: a sheet 9 km above the sea
        material.SetVector(HighId, new Vector4(PlanetRadius + CirrusAltitude, Mathf.Min(1f, weather.Cirrus * MainConfig.cloudAmount.Value), 0.35f, cirrusTravel));
        // HDRP's micro details on High and Ultra: a finer erosion pass, and the erosion its micro detail presets use
        material.SetVector(MicroId, micro ? new Vector4(0.5f, 200f, 0f, 0f) : Vector4.zero);

        // HDRP's phase functions are normalised over the sphere (1 / 4 pi); the game's sun colour is not a radiance,
        // so 4 pi brings a sunlit cloud to about the sun's colour
        // The game lights its nights brightly (a strong moon) for play; the clouds take the Night Brightness share of it
        // (at night on a much darker scale than by day: WeatherOverhaul's night clouds are near silhouettes)
        float brightness = MainConfig.cloudBrightness.Value * Mathf.Lerp(1f, MainConfig.cloudNightBrightness.Value * NightScale, night);
        // HDRP's lighting in the game's terms (checked against HDRP's own clouds rendered with the same values): its phase
        // functions are normalised over the sphere and the game lights a surface by albedo x (sun x N.L + ambient), so the
        // sun weighs pi and the sky 1; both times WeatherOverhaul's measured cloud gain
        material.SetVector(LightId, new Vector4(1f - 0.5f * 0.95f, 0.25f, Mathf.PI * CloudGain * brightness * WetSunRestore(), CloudGain * brightness));
        // The fog mixed into far clouds dims at night too, but not all the way, or they would show black against
        // the game's own fog-lit horizon
        // Storms darken the fog in the clouds too, or it lifts their bellies back to the game's pale storm fog
        float fogLight = Mathf.Lerp(1f, Mathf.Max(MainConfig.cloudNightBrightness.Value, NightFogFloor), night) * Mathf.Min(gloom, 1f);
        material.SetVector(FadeId, new Vector4(FadeStart, 1f / (FadeEnd - FadeStart), 1f - weather.WetShare, fogLight));

        // y: the haze's scale height (m, WeatherOverhaul's), z: its density at sea level (1/m)
        // (WeatherOverhaul's haze values, at a tenth: at their face value a rain deck vanishes into the haze, while its
        // own rain shows a textured deck overhead; a tenth matches its shots)
        material.SetVector(CoverId, new Vector4(1f, HazeScaleHeight, AirHaze() * HazeScale, Mathf.Repeat(frame * 0.618034f, 1f)));
        material.SetColor(FogColorId, gameFogColor);

        material.SetVector(SizesId, new Vector4(width, height, fullWidth, fullHeight));
        material.SetVector(ZParamsId, ZBufferParams(cam));
        UpdateAtmosphere();
        UpdateCloudMap(position);
        UpdateShadowCookie();
        material.SetVector(HistoryId, new Vector4(HistoryWeight, historyValid ? 1f : 0f, 0f, 0f));
        material.SetMatrix(PrevViewProjId, previousViewProjection * Matrix4x4.Translate(position));

        RenderTexture previous = history[current];
        RenderTexture next = history[1 - current];

        commands.Clear();
        // The atmosphere's sky over the game's, before the clouds go over both
        if (atmosphereDay > 0f)
        {
            commands.Blit(Texture2D.blackTexture, BuiltinRenderTextureType.CameraTarget, atmosphereMaterial, 2);
        }
        commands.SetGlobalTexture(HistoryTexId, previous);

        commands.SetRenderTarget(traceTargets, traceTargets[0]);
        commands.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);

        commands.SetGlobalTexture(TraceColorId, traceColor);
        commands.SetGlobalTexture(TraceDepthId, traceDepth);
        commands.SetRenderTarget(next);
        commands.DrawProcedural(Matrix4x4.identity, material, 1, MeshTopology.Triangles, 3);

        commands.SetGlobalTexture(AccumulatedId, next);
        commands.Blit(next, BuiltinRenderTextureType.CameraTarget, material, 2);
        // Hand the camera's own target (with its depth) back for the water and other transparent things drawn next
        commands.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);

        current = 1 - current;
        historyValid = true;
        previousViewProjection = cam.projectionMatrix * cam.worldToCameraMatrix;
        previousCameraPosition = position;
        frame++;
    }

    private static readonly Vector3[] Corners = new Vector3[4];

    // The camera's _ZBufferParams, as Unity works them out. The shader needs them in the passes drawn with Blit,
    // which sets its own while it draws
    public static Vector4 ZBufferParams(Camera cam)
    {
        float near = cam.nearClipPlane;
        float far = cam.farClipPlane;
        float x = SystemInfo.usesReversedZBuffer ? -1f + far / near : 1f - far / near;
        float y = SystemInfo.usesReversedZBuffer ? 1f : far / near;
        return new Vector4(x, y, x / far, y / far);
    }

    // The cloud map, centred on the camera (snapped to its texels, so it does not shimmer as the camera moves). The
    // weather's amounts are scaled by the Cloud Amount setting; the noise slowly changes as game time passes.
    private static void UpdateCloudMap(Vector3 cameraPosition)
    {
        if (cloudMap == null)
        {
            cloudMap = Create(MapSize, MapSize, RenderTextureFormat.ARGB32, FilterMode.Bilinear, "map");
            material.SetTexture(MapId, cloudMap);
        }

        mapEvolution = Mathf.Repeat(mapEvolution + Time.deltaTime * MapEvolutionRate, 1000f);
        float texel = MapWidth / MapSize;
        float centreX = Mathf.Round(cameraPosition.x / texel) * texel;
        float centreZ = Mathf.Round(cameraPosition.z / texel) * texel;
        material.SetVector(MapAreaId, new Vector4(centreX, centreZ, 1f / MapWidth, mapEvolution));
        material.SetVector(MapAmountsId, Vector4.Min(weather.Amounts * MainConfig.cloudAmount.Value, Vector4.one));

        mapCommands.Clear();
        mapCommands.SetRenderTarget(cloudMap);
        mapCommands.DrawProcedural(Matrix4x4.identity, material, 4, MeshTopology.Triangles, 3);
        RenderTexture active = RenderTexture.active;
        Graphics.ExecuteCommandBuffer(mapCommands);
        RenderTexture.active = active;

    }

    // The cloud shadows: a cookie on the game's sun, as HDRP and WeatherOverhaul do it, so only direct sunlight is
    // shaded. It is rendered here, before the camera draws: the game lights the world before the clouds' command
    // buffer runs, so drawing it there would always be a frame late.
    private static void UpdateShadowCookie()
    {
        Light sun = EnvMan.instance.m_dirLight;
        float strength = sun != null ? ShadowStrength(sun, out Vector3 toSun) : 0f;
        if (strength <= 0.001f)
        {
            RestoreSunCookie();
            return;
        }
        toSun = -sun.transform.forward;
        if (sun != cookieLight)
        {
            RestoreSunCookie();
            cookieLight = sun;
            originalCookie = sun.cookie;
            originalCookieSize = sun.cookieSize;
        }

        int size = MainConfig.cloudQuality.Value >= Quality.High ? 512 : 256;
        if (shadowCookie == null || shadowCookie.width != size)
        {
            ReleaseCookie();
            shadowCookie = Create(size, size, RenderTextureFormat.ARGB32, FilterMode.Bilinear, "shadow cookie");
        }

        // The cookie is centred on the light, which EnvMan keeps 3 km from the camera towards the sun. The light is
        // moved to whole cookie texels across its own axes, so the shadows do not shimmer as the camera moves (for
        // a directional light the position only places the cookie)
        Transform transform = sun.transform;
        Vector3 right = transform.right;
        Vector3 up = transform.up;
        Vector3 forward = transform.forward;
        Vector3 position = transform.position;
        float texel = CookieSize / size;
        position = right * (Mathf.Round(Vector3.Dot(position, right) / texel) * texel)
            + up * (Mathf.Round(Vector3.Dot(position, up) / texel) * texel)
            + forward * Vector3.Dot(position, forward);
        transform.position = position;

        material.SetVector(ShadowOriginId, new Vector4(position.x, position.y, position.z, CookieSize));
        material.SetVector(ShadowRightId, right);
        material.SetVector(ShadowUpId, new Vector4(up.x, up.y, up.z, strength));
        material.SetVector(ShadowSunId, new Vector4(toSun.x, toSun.y, toSun.z, SeaLevel));

        cookieCommands.Clear();
        cookieCommands.SetRenderTarget(shadowCookie);
        cookieCommands.DrawProcedural(Matrix4x4.identity, material, 3, MeshTopology.Triangles, 3);
        RenderTexture active = RenderTexture.active;
        Graphics.ExecuteCommandBuffer(cookieCommands);
        RenderTexture.active = active;

        sun.cookie = shadowCookie;
        sun.cookieSize = CookieSize;
    }

    // Gives the sun back the cookie it had (none, in the game as it is)
    private static void RestoreSunCookie()
    {
        if (cookieLight != null && cookieLight.cookie == shadowCookie)
        {
            cookieLight.cookie = originalCookie;
            cookieLight.cookieSize = originalCookieSize;
        }
        cookieLight = null;
        originalCookie = null;
    }

    // The air's haze between the camera and the clouds (1/m) and its aerosols (1/km), as WeatherOverhaul's measured:
    // clear days 0.0006 with aerosol 0.0055, twilight 0.0014 / 0.0070, night 0.0017 / 0.0078, rain and storms 0.0037 / 0.0199
    private static float Twilight()
    {
        Light sun = EnvMan.instance != null ? EnvMan.instance.m_dirLight : null;
        float elevation = sun != null ? -sun.transform.forward.y : 1f;
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.4f, elevation));
    }

    private const float HazeScaleHeight = 271.529f;
    private const float HazeScale = 0.1f;
    private const float MapVariety = 0.67f;
    // WeatherOverhaul's measured cloud brightness gain (clear skies)
    private const float CloudGain = 1.55f;
    private const float RainGameShare = 0.9f;

    private static float AirHaze()
    {
        float dry = Mathf.Lerp(Mathf.Lerp(0.0006f, 0.0014f, Twilight()), 0.0017f, Night());
        return Mathf.Lerp(dry, 0.0037f, weather.AirWetness);
    }

    private static float AirAerosol()
    {
        // (clear days hazier than WeatherOverhaul's measured 0.0055, for a pale, milky horizon)
        float dry = Mathf.Lerp(Mathf.Lerp(0.012f, 0.0070f, Twilight()), 0.0078f, Night());
        return Mathf.Lerp(dry, 0.0199f, weather.AirWetness);
    }

    // Sets the atmosphere for both shaders and rebuilds its lookup tables: the multiple scattering one only when the
    // air changes, the sky view one every frame (it follows the sun)
    private static void UpdateAtmosphere()
    {
        if (multiScattering == null)
        {
            multiScattering = Create(32, 32, RenderTextureFormat.ARGBHalf, FilterMode.Bilinear, "atmosphere multiple scattering");
            skyView = Create(256, 144, RenderTextureFormat.ARGBHalf, FilterMode.Bilinear, "atmosphere sky view");
            multiScattering.wrapMode = TextureWrapMode.Clamp;
            skyView.wrapModeU = TextureWrapMode.Repeat;
            skyView.wrapModeV = TextureWrapMode.Clamp;
            Shader.SetGlobalTexture("_AtmoMultiScattering", multiScattering);
            Shader.SetGlobalTexture("_AtmoSkyView", skyView);
            builtAerosol = new Vector4(-1f, 0f, 0f, 0f);
        }

        float aerosol = AirAerosol();
        // HDRP's Earth, with the planet, scale heights, ozone and ground of WeatherOverhaul's (km)
        Shader.SetGlobalVector("_AtmoRadii", new Vector4(6360f, 6460f, 1f / 8f, 1f / 1.3973f));
        Shader.SetGlobalVector("_AtmoAirScattering", new Vector4(0.0058f, 0.01356f, 0.0331f, 0f));
        Shader.SetGlobalVector("_AtmoAerosolScattering", new Vector4(aerosol, aerosol, aerosol, 0.8f));
        Shader.SetGlobalVector("_AtmoAerosolExtinction", new Vector4(aerosol + 0.00061f, aerosol + 0.00071f, aerosol + 0.00093f, 0f));
        Shader.SetGlobalVector("_AtmoOzone", new Vector4(0.00085f, 0.00245f, 0.00011f, 0f));
        Shader.SetGlobalVector("_AtmoGround", new Vector4(0.3f, 0.3f, 0.3f, 0f));
        // The real sun (the game turns its light round to the moon at night; its sky keeps the sun in _SkyboxSunDir)
        Vector3 sunDirection = Shader.GetGlobalVector("_SkyboxSunDir");
        if (sunDirection.sqrMagnitude < 1e-6f)
        {
            Light light = EnvMan.instance.m_dirLight;
            sunDirection = light != null ? -light.transform.forward : Vector3.up;
        }
        sunDirection.Normalize();
        Shader.SetGlobalVector("_AtmoSun", sunDirection);
        // The atmosphere is drawn by day and fades out through dusk (the sun 7 degrees below the horizon), leaving the
        // game's sky (the chosen ValheimSkies sky) at night
        atmosphereDay = MainConfig.atmosphereSky.Value ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.12f, 0.03f, sunDirection.y)) : 0f;

        // In rain and storms the sky and the air take the game's grey fog colour (WeatherOverhaul anchors its look to the
        // game's there, 0.9 in rain): the clear atmosphere would show blue around and through the deck
        // w: the sky's exposure at twilight. The exposure follows the game's sky, which stays bright at sunset; measured
        // against WeatherOverhaul's clear sunset (cloudless: our zenith 12/86/138 against its 0/44/110, 30 degrees up
        // 80/147/193 against 0/62/156) it needs less light; 0.2 crushed it to 3/19/28 under the game's colour grading, so 0.75
        float twilightExposure = Mathf.Lerp(1f, TwilightExposure, Mathf.Clamp01(Twilight() / 0.86f) * (1f - weather.AirWetness));
        Shader.SetGlobalVector("_AtmoSky", new Vector4(0f, atmosphereDay, RainGameShare * weather.AirWetness, twilightExposure));
        float grey = gameFogColor.grayscale;
        Shader.SetGlobalColor("_AtmoFogColor", Color.Lerp(new Color(grey, grey, grey), gameFogColor, 0.15f));
        Shader.SetGlobalColor("_AtmoSunColor", Shader.GetGlobalColor("_SunColor"));

        atmosphereCommands.Clear();
        if (Mathf.Abs(aerosol - builtAerosol.x) > 0.0002f)
        {
            builtAerosol.x = aerosol;
            atmosphereCommands.SetRenderTarget(multiScattering);
            atmosphereCommands.DrawProcedural(Matrix4x4.identity, atmosphereMaterial, 0, MeshTopology.Triangles, 3);
        }
        atmosphereCommands.SetRenderTarget(skyView);
        atmosphereCommands.DrawProcedural(Matrix4x4.identity, atmosphereMaterial, 1, MeshTopology.Triangles, 3);
        if (horizon == null)
        {
            horizon = Create(1, 1, RenderTextureFormat.ARGBHalf, FilterMode.Point, "atmosphere horizon");
        }
        atmosphereCommands.SetRenderTarget(horizon);
        atmosphereCommands.DrawProcedural(Matrix4x4.identity, atmosphereMaterial, 3, MeshTopology.Triangles, 3);
        RenderTexture active = RenderTexture.active;
        Graphics.ExecuteCommandBuffer(atmosphereCommands);
        RenderTexture.active = active;

        if (!readingHorizon && Time.unscaledTime >= nextHorizonRead && SystemInfo.supportsAsyncGPUReadback)
        {
            readingHorizon = true;
            nextHorizonRead = Time.unscaledTime + 0.25f;
            AsyncGPUReadback.Request(horizon, 0, TextureFormat.RGBAFloat, request =>
            {
                readingHorizon = false;
                if (!request.hasError)
                {
                    Color linear = request.GetData<Color>()[0];
                    horizonColor = QualitySettings.activeColorSpace == ColorSpace.Linear ? linear.gamma : linear;
                    haveHorizon = true;
                }
            });
        }

        // The game's distance fog leans towards the sky's horizon by day, so far land fades into the sky (not in wet
        // weather, where the sky is the game's grey)
        // (only with the sun well up: a low sun's bright horizon would wash the land out at dusk)
        float fogSky = haveHorizon ? FogSkyShare * atmosphereDay * (1f - Twilight()) * (1f - RainGameShare * weather.AirWetness) : 0f;
        Color fog = gameFogColor;
        if (fogSky > 0f)
        {
            fog = Color.Lerp(gameFogColor, horizonColor, fogSky);
        }
        // In rain and storms WeatherOverhaul still gives the game's fog its sky's horizon colour, mostly greyed (80%) as
        // the sky is overcast, at the game's fog brightness; so does this
        float wet = weather.AirWetness;
        if (wet > 0f)
        {
            fog = Color.Lerp(fog, Overcast(gameFogColor, haveHorizon && atmosphereDay > 0f ? horizonColor : gameFogColor), wet);
        }
        fog.a = gameFogColor.a;
        RenderSettings.fogColor = fog;
    }

    // The game dims its sun in rain and storms (light intensity 0.5 against 1.7 when clear), which leaves the decks dark
    // and lets its colour grading turn them teal; WeatherOverhaul lights its clouds by the sun's real strength in every
    // weather (its illuminance follows the sun's height only) and darkens them by its shade and gain instead. Measured
    // zenith in game: its rain 160/170/177 and storm 125/138/146 against our 97/120/135 and 49/79/88. So in wet
    // weather the clouds' sunlight is brought back to the clear sky's strength
    private static float WetSunRestore()
    {
        EnvSetup current = EnvMan.instance != null ? EnvMan.instance.GetCurrentEnvironment() : null;
        if (current == null || weather.AirWetness <= 0f)
        {
            return 1f;
        }
        if (clearSunLuma <= 0f)
        {
            EnvSetup clear = EnvMan.instance.m_environments.Find(e => e.m_name == "Clear");
            clearSunLuma = clear != null ? clear.m_sunColorDay.grayscale * clear.m_lightIntensityDay : 1f;
        }
        float currentLuma = current.m_sunColorDay.grayscale * current.m_lightIntensityDay;
        float restore = currentLuma > 0.01f ? Mathf.Clamp(clearSunLuma / currentLuma, 1f, 4f) : 1f;
        return Mathf.Lerp(1f, restore, weather.AirWetness);
    }

    private static float clearSunLuma;

    // A colour at the brightness of the game's, in a hue mostly greyed (80%) from the sky's
    private static Color Overcast(Color game, Color sky)
    {
        float skyGrey = sky.grayscale;
        Color greyed = Color.Lerp(sky, new Color(skyGrey, skyGrey, skyGrey), 0.8f);
        Color result = greyed * (game.grayscale / Mathf.Max(greyed.grayscale, 1e-4f));
        result.a = game.a;
        return result;
    }

    private static void CaptureSky(Vector3 position)
    {
        if (Time.unscaledTime < nextSkyCapture && skyCube != null)
        {
            return;
        }
        nextSkyCapture = Time.unscaledTime + SkyCaptureInterval;
        if (skyCube == null)
        {
            skyCube = new RenderTexture(64, 64, 16, RenderTextureFormat.ARGBHalf)
            {
                dimension = TextureDimension.Cube,
                useMipMap = true,
                autoGenerateMips = true,
                name = "ValheimSkies sky",
                hideFlags = HideFlags.HideAndDontSave
            };
            skyCube.Create();
            material.SetTexture(SkyCubeId, skyCube);
            Shader.SetGlobalTexture("_AtmoVanillaSky", skyCube);
        }
        if (skyCamera == null)
        {
            GameObject holder = new GameObject("ValheimSkies sky capture") { hideFlags = HideFlags.HideAndDontSave };
            skyCamera = holder.AddComponent<Camera>();
            skyCamera.enabled = false;
            skyCamera.cullingMask = 0;
            skyCamera.clearFlags = CameraClearFlags.Skybox;
            skyCamera.renderingPath = RenderingPath.Forward;
            skyCamera.allowHDR = true;
            skyCamera.allowMSAA = false;
            skyCamera.nearClipPlane = 0.1f;
            skyCamera.farClipPlane = 10f;
        }
        skyCamera.transform.position = position;
        skyCamera.RenderToCubemap(skyCube);
    }

    private static void ReleaseSky()
    {
        foreach (RenderTexture table in new[] { multiScattering, skyView })
        {
            if (table != null)
            {
                table.Release();
                Object.Destroy(table);
            }
        }
        multiScattering = null;
        skyView = null;
        if (horizon != null)
        {
            horizon.Release();
            Object.Destroy(horizon);
            horizon = null;
        }
        haveHorizon = false;
        if (atmosphereMaterial != null)
        {
            Object.Destroy(atmosphereMaterial);
            atmosphereMaterial = null;
        }
        if (skyCamera != null)
        {
            Object.Destroy(skyCamera.gameObject);
            skyCamera = null;
        }
        if (skyCube != null)
        {
            skyCube.Release();
            Object.Destroy(skyCube);
            skyCube = null;
        }
    }

    private static void ReleaseCookie()
    {
        RestoreSunCookie();
        if (shadowCookie != null)
        {
            shadowCookie.Release();
            Object.Destroy(shadowCookie);
            shadowCookie = null;
        }
    }

    // How dark the cloud shadows are this frame (0 for none)
    private static float ShadowStrength(Light sun, out Vector3 toSun)
    {
        toSun = -sun.transform.forward;
        float strength = MainConfig.cloudShadows.Value;
        if (strength <= 0f || toSun.y <= 0f)
        {
            return 0f;
        }
        // Faint by moonlight; gone as the sun reaches the horizon, where the shadows would stretch for kilometres
        strength *= Mathf.Lerp(1f, NightShadows, Night());
        strength *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.08f, 0.3f, toSun.y));
        // Under a full rain cover the game already dims its light; the shadows only add the patchiness
        strength *= Mathf.Lerp(1f, WetShadows, weather.WetShare);
        return strength;
    }

    // How much it is night (0 to 1)
    private static float Night()
    {
        // The game turns its sun into the moon at 06:00 and 18:00 (day fractions 0.25 and 0.75); night is whole
        // from then on, eased in over about half an hour of game time either side
        float f = EnvMan.instance != null ? EnvMan.instance.GetDayFraction() : 0.5f;
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Min(f - 0.24f, 0.76f - f) / 0.02f));
    }

    private static void GetQuality(Quality quality, out int divider, out int primarySteps, out int lightSteps)
    {
        switch (quality)
        {
            case Quality.Low:
                divider = 2; primarySteps = 32; lightSteps = 4;
                break;
            case Quality.High:
                divider = 2; primarySteps = 64; lightSteps = 6;
                break;
            case Quality.Ultra:
                divider = 1; primarySteps = 96; lightSteps = 8;
                break;
            default:
                divider = 2; primarySteps = 48; lightSteps = 6;
                break;
        }
    }

    private static void Allocate(int width, int height)
    {
        ReleaseTargets();
        traceColor = Create(width, height, RenderTextureFormat.ARGBHalf, FilterMode.Point, "trace");
        traceDepth = Create(width, height, RenderTextureFormat.RGFloat, FilterMode.Point, "trace depth");
        history[0] = Create(width, height, RenderTextureFormat.ARGBHalf, FilterMode.Bilinear, "history 0");
        history[1] = Create(width, height, RenderTextureFormat.ARGBHalf, FilterMode.Bilinear, "history 1");
        traceTargets[0] = traceColor;
        traceTargets[1] = traceDepth;
        historyValid = false;
    }

    private static RenderTexture Create(int width, int height, RenderTextureFormat format, FilterMode filter, string name)
    {
        RenderTexture texture = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear)
        {
            name = "ValheimSkies clouds " + name,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = filter,
            useMipMap = false,
            hideFlags = HideFlags.HideAndDontSave
        };
        texture.Create();
        return texture;
    }

    private static void ReleaseTargets()
    {
        foreach (RenderTexture texture in new[] { traceColor, traceDepth, history[0], history[1] })
        {
            if (texture != null)
            {
                texture.Release();
                Object.Destroy(texture);
            }
        }
        traceColor = traceDepth = history[0] = history[1] = null;
    }

    // The game's cloud layers: the three EnvMan keeps, and the distant cloud sky objects found by their material
    private static void HideGameClouds()
    {
        EnvMan env = EnvMan.instance;
        if (env != searchedIn)
        {
            RestoreGameClouds();
            gameClouds.Clear();
            searchedIn = env;
            foreach (Renderer renderer in new Renderer[] { env.m_clouds, env.m_rainClouds, env.m_rainCloudsDownside })
            {
                if (renderer != null)
                {
                    gameClouds.Add(renderer);
                }
            }
            foreach (Renderer renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Material shared = renderer.sharedMaterial;
                if (shared != null && !gameClouds.Contains(renderer)
                    && (shared.name.StartsWith("distantclouds") || shared.shader != null && shared.shader.name == "Custom/Clouds"))
                {
                    gameClouds.Add(renderer);
                }
            }
            Jotunn.Logger.LogDebug($"[{ValheimSkiesMod.PluginName}] Found {gameClouds.Count} of the game's cloud layers to hide.");
        }
        foreach (Renderer renderer in gameClouds)
        {
            if (renderer != null && renderer.enabled)
            {
                renderer.enabled = false;
                hiddenClouds.Add(renderer);
            }
        }
    }

    // Shows again only the layers hidden here (the game may have others switched off itself)
    private static void RestoreGameClouds()
    {
        foreach (Renderer renderer in hiddenClouds)
        {
            if (renderer != null)
            {
                renderer.enabled = true;
            }
        }
        hiddenClouds.Clear();
    }
}
