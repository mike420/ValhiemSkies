using UnityEngine;
using UnityEngine.Rendering;

namespace ValheimSkies;

// A rainbow after the rain (section Rainbow): once it has rained for a while and the rain stops, a rainbow comes out
// opposite the sun if the sun is low enough to make one (4 to 40 degrees up, by day), waiting a few minutes for the
// sun to get there if it is not. It is a ring of coloured bands around the point opposite the sun (red outside at about
// 42 degrees, violet inside), fading toward the horizon, drawn on a mesh that follows the camera a few hundred metres
// out so hills and trees stand in front of it. It fades in, stays a few minutes and fades out, sooner if the rain comes
// back, night falls or the sun moves out of place.
internal static class Rainbow
{
    private const float MinRainSeconds = 45f;   // rain that short is not worth a rainbow
    private const float WaitSeconds = 8f * 60f; // how long to wait for the sun to get in place after the rain
    private const float ShowSeconds = 3f * 60f;
    private const float FadeInSeconds = 14f;
    private const float FadeOutSeconds = 20f;
    private const float MinSun = 4f, MaxSun = 40f;
    private const float InnerAngle = 38f, OuterAngle = 44f; // the band's edges, degrees from the point opposite the sun
    private const int Around = 200, Across = 24;
    private const float SoftEdge = 0.12f;   // the part of the band at each side that fades into the sky

    private static readonly Color[] Spectrum =
    {
        new Color(0.6f, 0.25f, 0.85f),  // violet (inside)
        new Color(0.35f, 0.3f, 0.95f),
        new Color(0.2f, 0.6f, 1f),
        new Color(0.3f, 0.85f, 0.3f),
        new Color(1f, 0.9f, 0.2f),
        new Color(1f, 0.5f, 0.05f),
        new Color(1f, 0.08f, 0.05f),    // red (outside)
    };

    private static GameObject root;
    private static Mesh mesh;
    private static Material material;
    private static Vector3[] points;    // the ring on a unit sphere, around +Z
    private static Color[] hues, colours;
    private static float[] weights;

    private static float wetSeconds, waitLeft, shownSeconds, fade, drawnFade = -1f;
    private static Vector3 antiSun = Vector3.forward, drawnAntiSun;
    private static bool showing;

    public static void Tick()
    {
        Player player = Player.m_localPlayer;
        EnvMan env = EnvMan.instance;
        if (!MainConfig.modEnabled.Value || !MainConfig.rainbows.Value || player == null || env == null)
        {
            Hide();
            wetSeconds = waitLeft = 0f;
            return;
        }

        float dt = Time.deltaTime;
        EnvSetup weather = env.GetCurrentEnvironment();
        bool wet = weather != null && weather.m_isWet;
        if (wet)
        {
            wetSeconds += dt;
            waitLeft = 0f;
        }
        else
        {
            if (wetSeconds >= MinRainSeconds && !showing)
            {
                waitLeft = WaitSeconds;
            }
            wetSeconds = 0f;
        }

        Vector3 sun = env.m_dirLight != null ? -env.m_dirLight.transform.forward : Vector3.up;
        float sunHeight = Mathf.Asin(Mathf.Clamp(sun.y, -1f, 1f)) * Mathf.Rad2Deg;
        bool day = EnvMan.IsDay();

        if (waitLeft > 0f)
        {
            waitLeft -= dt;
            if (!wet && day && sunHeight >= MinSun && sunHeight <= MaxSun && !player.InInterior())
            {
                waitLeft = 0f;
                Show();
            }
        }
        if (!showing)
        {
            return;
        }

        // Faded in and out over time, and eased out as the sun nears either end of where it makes a rainbow
        shownSeconds += dt;
        float timeFade = Mathf.Clamp01(shownSeconds / FadeInSeconds) * Mathf.Clamp01((ShowSeconds - shownSeconds) / FadeOutSeconds);
        float sunFade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2f, 7f, sunHeight)) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(42f, 36f, sunHeight));
        float target = wet || !day ? 0f : timeFade * sunFade;
        fade = Mathf.MoveTowards(fade, target, dt / (target < fade ? 4f : 8f));
        antiSun = -sun;

        if (shownSeconds > ShowSeconds || (fade <= 0.001f && shownSeconds > FadeInSeconds + 6f))
        {
            Hide();
            return;
        }
        Draw();
    }

    // Kept centred on the camera (after it has moved this frame), far enough out to be behind nearby things
    public static void Follow()
    {
        Camera main = Camera.main;
        if (!showing || root == null || main == null)
        {
            return;
        }
        root.transform.position = main.transform.position;
        root.transform.localScale = Vector3.one * Mathf.Clamp(main.farClipPlane * 0.7f, 200f, 450f);
    }

    public static void Shutdown()
    {
        Hide();
        if (root != null)
        {
            Object.Destroy(root);
        }
        if (mesh != null)
        {
            Object.Destroy(mesh);
        }
        if (material != null)
        {
            Object.Destroy(material);
        }
        root = null;
        mesh = null;
        material = null;
    }

    private static void Show()
    {
        if (!Build())
        {
            return;
        }
        showing = true;
        shownSeconds = 0f;
        fade = 0f;
        drawnFade = -1f;
        root.SetActive(true);
    }

    private static void Hide()
    {
        showing = false;
        fade = 0f;
        if (root != null)
        {
            root.SetActive(false);
        }
    }

    private static bool Build()
    {
        if (root != null)
        {
            return true;
        }
        Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        if (shader == null)
        {
            Jotunn.Logger.LogWarning($"[{ValheimSkiesMod.PluginName}] No shader found for the rainbow. Rainbows stay off.");
            return false;
        }

        int rows = Across + 1;
        points = new Vector3[rows * Around];
        hues = new Color[points.Length];
        colours = new Color[points.Length];
        weights = new float[points.Length];
        int[] triangles = new int[Across * Around * 6];
        for (int row = 0; row < rows; row++)
        {
            float t = row / (float)Across;  // 0 inside (violet), 1 outside (red)
            float angle = Mathf.Lerp(InnerAngle, OuterAngle, t) * Mathf.Deg2Rad;
            // Full strength across the band except a thin soft edge at each side, which keeps the colour next to it
            // (violet inside, red outside), so red and violet show as strongly as the rest instead of fading out
            Color hue = SpectrumAt(Mathf.InverseLerp(SoftEdge, 1f - SoftEdge, t));
            float weight = Mathf.SmoothStep(0f, 1f, t / SoftEdge) * Mathf.SmoothStep(0f, 1f, (1f - t) / SoftEdge);
            for (int i = 0; i < Around; i++)
            {
                float around = i / (float)Around * Mathf.PI * 2f;
                int v = row * Around + i;
                points[v] = new Vector3(Mathf.Sin(angle) * Mathf.Cos(around), Mathf.Sin(angle) * Mathf.Sin(around), Mathf.Cos(angle));
                hues[v] = hue;
                weights[v] = weight;
            }
        }
        int n = 0;
        for (int row = 0; row < Across; row++)
        {
            for (int i = 0; i < Around; i++)
            {
                int a = row * Around + i, b = row * Around + (i + 1) % Around;
                triangles[n++] = a; triangles[n++] = a + Around; triangles[n++] = b;
                triangles[n++] = b; triangles[n++] = a + Around; triangles[n++] = b + Around;
            }
        }

        mesh = new Mesh { name = "ValheimSkies Rainbow", hideFlags = HideFlags.HideAndDontSave };
        mesh.vertices = points;
        mesh.triangles = triangles;
        mesh.colors = colours;
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
        mesh.MarkDynamic();
        material = new Material(shader) { name = "ValheimSkies Rainbow", renderQueue = 3100, hideFlags = HideFlags.HideAndDontSave };

        root = new GameObject("ValheimSkies Rainbow");
        Object.DontDestroyOnLoad(root);
        root.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = root.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        root.SetActive(false);
        return true;
    }

    private static Color SpectrumAt(float t)
    {
        float x = Mathf.Clamp01(t) * (Spectrum.Length - 1);
        int i = Mathf.Min((int)x, Spectrum.Length - 2);
        return Color.Lerp(Spectrum[i], Spectrum[i + 1], x - i);
    }

    // Turned to face away from the sun; the colours (and the fade toward the horizon) set again only when they change
    private static void Draw()
    {
        Quaternion rotation = Quaternion.FromToRotation(Vector3.forward, antiSun);
        root.transform.rotation = rotation;
        float strength = fade * 0.9f * Mathf.Clamp(MainConfig.rainbowBrightness.Value, 0f, 2f);
        if (drawnFade >= 0f && Mathf.Abs(strength - drawnFade) < 0.003f && Vector3.Angle(antiSun, drawnAntiSun) < 0.05f)
        {
            return;
        }
        drawnFade = strength;
        drawnAntiSun = antiSun;
        Vector3 up = Quaternion.Inverse(rotation) * Vector3.up;
        for (int v = 0; v < points.Length; v++)
        {
            float aboveHorizon = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.16f, Vector3.Dot(up, points[v])));
            Color colour = hues[v];
            colour.a = Mathf.Clamp01(weights[v] * aboveHorizon * strength);
            colours[v] = colour;
        }
        mesh.colors = colours;
    }
}
