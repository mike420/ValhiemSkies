using System;
using UnityEngine;

namespace ValheimSkies;

// What the volumetric clouds look like. The weather (the game's environment), the biome and the day pick a regime:
// fair weather, broken cloud, rain, storm, snow, blizzard, fog, the Mistlands' low cover, the Ashlands' ash deck and
// its storms, or the clear polar sky of the Deep North. Each sets HDRP's cloud model values, how much of the sky the
// cloud map fills with each kind of cloud (see pass 4 of the shader), how dark the clouds' bellies get, their colour
// and the wind. Fair weather also changes from day to day (a draw from the world's seed and the day): some days have
// few small cumulus, some more, some grow towers, some have a high sheet of cloud over them.
// Everything eases towards a new look over about half a minute, so a change of weather rolls in.
internal sealed class CloudWeather
{
    public enum Regime
    {
        Fair,
        Broken,
        LightRain,
        Rain,
        Storm,
        Snow,
        Blizzard,
        Fog,
        Mist,
        Ash,
        AshStorm,
        PolarClear
    }

    // One look: the values that ease together
    private struct Look
    {
        public float DensityMultiplier, ShapeFactor, ErosionFactor, BottomAltitude, Rain, Darkness, BaseLight, WindKmh;
        // How tall the cumulus grow and how deep the altostratus deck is (m)
        public float CumulusDepth, DeckDepth;
        // How much of the light is kept in this weather, the fog mixed into the clouds included (1 for all)
        public float Gloom;
        // How much of the sky the cirrus and the altocumulus fill (0 for none)
        public float Cirrus, Altocumulus;
        // How wet the air is (0 dry, 1 rain): the haze and aerosols of the atmosphere the clouds are seen through
        public float AirWetness;
        // The deck's base band (m), its shape and noise scale, and the relief of its base (share of the layer's depth)
        public float BaseBand, BaseShape, BaseScale, Relief;
        // The deck's top: how far it dips over a few kilometres (m, so it is thinner in places and lets more light
        // through), and how soft its base is (m over which its density fades in, so the noise's peaks hang lowest)
        public float ReliefTop, SoftBase;
        // The altostratus deck's own shape, erosion and density (WeatherOverhaul's deck look; below 0: the clouds' own)
        public float DeckShape, DeckErosion, DeckDensity;
        // Where the deck starts above the layer's base (m): a veil above the cumulus, a rain deck from the base
        public float DeckBottom;
        // How much of the sky the cloud map fills with cumulus, altostratus and cumulonimbus, and the rain under the
        // altostratus; and how tall the cumulonimbus grow and how much rain they carry
        public Vector4 Amounts;
        public float TowerHeight, TowerRain;
        public Color Tint, Glow;

        public static Look Lerp(in Look a, in Look b, float t)
        {
            return new Look
            {
                DensityMultiplier = Mathf.Lerp(a.DensityMultiplier, b.DensityMultiplier, t),
                ShapeFactor = Mathf.Lerp(a.ShapeFactor, b.ShapeFactor, t),
                ErosionFactor = Mathf.Lerp(a.ErosionFactor, b.ErosionFactor, t),
                BottomAltitude = Mathf.Lerp(a.BottomAltitude, b.BottomAltitude, t),
                Rain = Mathf.Lerp(a.Rain, b.Rain, t),
                Darkness = Mathf.Lerp(a.Darkness, b.Darkness, t),
                BaseLight = Mathf.Lerp(a.BaseLight, b.BaseLight, t),
                WindKmh = Mathf.Lerp(a.WindKmh, b.WindKmh, t),
                CumulusDepth = Mathf.Lerp(a.CumulusDepth, b.CumulusDepth, t),
                DeckDepth = Mathf.Lerp(a.DeckDepth, b.DeckDepth, t),
                Gloom = Mathf.Lerp(a.Gloom, b.Gloom, t),
                Cirrus = Mathf.Lerp(a.Cirrus, b.Cirrus, t),
                Altocumulus = Mathf.Lerp(a.Altocumulus, b.Altocumulus, t),
                AirWetness = Mathf.Lerp(a.AirWetness, b.AirWetness, t),
                BaseBand = Mathf.Lerp(a.BaseBand, b.BaseBand, t),
                BaseShape = Mathf.Lerp(a.BaseShape, b.BaseShape, t),
                BaseScale = Mathf.Lerp(a.BaseScale, b.BaseScale, t),
                Relief = Mathf.Lerp(a.Relief, b.Relief, t),
                ReliefTop = Mathf.Lerp(a.ReliefTop, b.ReliefTop, t),
                SoftBase = Mathf.Lerp(a.SoftBase, b.SoftBase, t),
                DeckShape = Mathf.Lerp(a.DeckShape, b.DeckShape, t),
                DeckErosion = Mathf.Lerp(a.DeckErosion, b.DeckErosion, t),
                DeckDensity = Mathf.Lerp(a.DeckDensity, b.DeckDensity, t),
                DeckBottom = Mathf.Lerp(a.DeckBottom, b.DeckBottom, t),
                Amounts = Vector4.Lerp(a.Amounts, b.Amounts, t),
                TowerHeight = Mathf.Lerp(a.TowerHeight, b.TowerHeight, t),
                TowerRain = Mathf.Lerp(a.TowerRain, b.TowerRain, t),
                Tint = Color.Lerp(a.Tint, b.Tint, t),
                Glow = Color.Lerp(a.Glow, b.Glow, t),
            };
        }
    }

    // How tall the tallest cumulonimbus grow (m; the shader's TOWER_DEPTH)
    private const float TowerDepth = 10000f;

    // The altocumulus layer: its lowest base above the sea (it stays 200 m clear of the cumulus tops) and its depth (m)
    public const float AltocumulusBase = 3200f;
    public const float AltocumulusDepth = 500f;

    // Seconds for most of the way to a new look
    private const float EaseTime = 15f;

    // The game's bosses bring their own stormy skies
    private static readonly string[] BossEnvironments = { "eikthyr", "gdking", "bonemass", "moder", "goblinking", "queen", "fader" };

    private Look current;
    private bool snapped;

    // Today's fair weather: how cloudy (0 to 1) and what kind (0: towers, to 1: a high sheet over the cumulus)
    private int dayDrawn = int.MinValue;
    private float dayCover;
    private float dayType;
    // Today's high and middle cloud (0 to 1: under about half, none)
    private float dayHigh;
    private float dayMiddle;
    // How wet today's rain is (WeatherOverhaul's precipitation scale, about 0.4 to 1.6)
    private float dayPrecipitation = 1f;

    public Regime Now { get; private set; }
    public float TargetCirrus { get; private set; }
    public float TargetAltocumulus { get; private set; }

    public float DensityMultiplier => current.DensityMultiplier;
    public float ShapeFactor => current.ShapeFactor;
    // HDRP's and WeatherOverhaul's shape scale (the shape noise repeats every 20 km)
    public float ShapeScale => 5f;
    public float ErosionFactor => current.ErosionFactor;
    public float ErosionScale => 107f;
    public float BottomAltitude => current.BottomAltitude;
    public float CumulusDepth => current.CumulusDepth;
    public float DeckDepth => current.DeckDepth;
    public float Gloom => current.Gloom;
    public float Cirrus => current.Cirrus;
    public float Altocumulus => current.Altocumulus;
    public float AirWetness => current.AirWetness;
    public Vector4 DeckModel => new Vector4(current.DeckShape, current.DeckErosion, current.DeckDensity, current.DeckBottom);
    public Vector4 DeckBase => new Vector4(current.BaseBand, current.BaseShape, current.BaseScale, current.Relief);
    public Vector4 DeckRelief => new Vector4(current.ReliefTop, current.SoftBase, 0f, 0f);

    // Whether the weather has room for high clouds at all (none under a deck, in fog or in rain), for the High Clouds setting
    private static bool HasHighClouds(Regime regime) => regime == Regime.Fair || regime == Regime.Broken || regime == Regime.LightRain || regime == Regime.PolarClear;
    // Where the altocumulus sit above the layer's base (m)
    public float AltocumulusAbove => Mathf.Max(AltocumulusBase - current.BottomAltitude, current.CumulusDepth + 200f);
    // The layer is as deep as its tallest kind of cloud: a couple of kilometres, more where towers grow
    public float AltitudeRange => Mathf.Max(Mathf.Max(Mathf.Max(current.CumulusDepth, current.DeckBottom + current.DeckDepth), current.TowerHeight * TowerDepth * Mathf.Clamp01(current.Amounts.z * 20f)),
        (AltocumulusAbove + AltocumulusDepth) * Mathf.Clamp01(current.Altocumulus * 20f));
    public float Rain => current.Rain;
    public float Darkness => current.Darkness;
    public float BaseLight => current.BaseLight;
    public float WindKmh => current.WindKmh;
    public Vector4 Amounts => current.Amounts;
    public float TowerHeight => current.TowerHeight;
    public float TowerRain => current.TowerRain;
    public Color Tint => current.Tint;
    public Color Glow => current.Glow;

    // How much of the sky is under a wet, grey deck (0 to 1): far clouds fade into fog rather than to the clear sky,
    // and the cloud shadows soften
    public float WetShare => Mathf.Clamp01((current.Amounts.y - 0.5f) / 0.4f) * Mathf.Clamp01(current.Rain * 2f + 0.3f);

    // Jumps straight to the weather's clouds next update (a world was entered, or the clouds were switched on)
    public void Snap()
    {
        snapped = false;
    }

    public void Update(EnvSetup environment, Heightmap.Biome biome, int worldSeed, int day, float deltaTime)
    {
        DrawDay(worldSeed, day);
        Now = Classify(environment, biome);
        Look target = For(Now);
        TargetCirrus = target.Cirrus;
        TargetAltocumulus = target.Altocumulus;

        float t = snapped ? 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / EaseTime) : 1f;
        snapped = true;
        current = Look.Lerp(current, target, t);
    }

    private void DrawDay(int worldSeed, int day)
    {
        if (day == dayDrawn)
        {
            return;
        }
        dayDrawn = day;
        System.Random random = new System.Random(unchecked(worldSeed * 397 ^ day * 7919 + 1013));
        dayCover = (float)random.NextDouble();
        dayType = (float)random.NextDouble();
        dayHigh = (float)random.NextDouble();
        dayMiddle = (float)random.NextDouble();
        dayPrecipitation = 0.4f + 1.2f * (float)random.NextDouble();
    }

    private Regime Classify(EnvSetup environment, Heightmap.Biome biome)
    {
        string name = environment?.m_name?.ToLowerInvariant() ?? "";
        bool snow = Contains(name, "snow") || environment != null && environment.m_isFreezing && environment.m_isWet;
        bool storm = Contains(name, "storm") || Contains(name, "thunder");
        bool wet = Contains(name, "rain") || environment != null && environment.m_isWet;

        if (Array.Exists(BossEnvironments, boss => Contains(name, boss)))
        {
            return biome == Heightmap.Biome.AshLands ? Regime.AshStorm : Regime.Storm;
        }
        if (biome == Heightmap.Biome.AshLands)
        {
            return storm ? Regime.AshStorm : Regime.Ash;
        }
        if (snow)
        {
            return storm ? Regime.Blizzard : Regime.Snow;
        }
        if (storm)
        {
            return Regime.Storm;
        }
        if (wet)
        {
            return Contains(name, "light") ? Regime.LightRain : Regime.Rain;
        }
        if (biome == Heightmap.Biome.Mistlands)
        {
            return Regime.Mist;
        }
        if (Contains(name, "mist") || Contains(name, "fog") || Contains(name, "dark"))
        {
            return Regime.Fog;
        }
        if (biome == Heightmap.Biome.DeepNorth)
        {
            return Regime.PolarClear;
        }
        // As WeatherOverhaul: half the dry days are fair, half broken (its BrokenShare 0.5)
        return dayType >= 0.5f ? Regime.Broken : Regime.Fair;
    }

    // WeatherOverhaul's cloud regimes (CloudRegimes.For), with the values it was measured rendering in rain and storms
    // where they differ (its day looks adjust them), in HDRP's cloud map terms: an amount is coverage everywhere.
    // "cover" is the day's cloudiness (0 to 1) and "precipitation" how wet its rain is. The light rain and Mistlands
    // regimes are ValheimSkies' own
    private Look For(Regime regime)
    {
        Look look = Base();
        float cover = Mathf.Clamp01((dayCover - 0.25f) / 0.5f);
        float precipitation = Mathf.Clamp01((dayPrecipitation - 0.7f) / 0.6f);
        switch (regime)
        {
            case Regime.Fair:
            {
                float type = Mathf.Clamp01(dayType * 0.5f / 0.5f);
                // Plenty of cumulus on every fair day (WeatherOverhaul's table leaves many fair days empty)
                look.Amounts = new Vector4(Mathf.Lerp(0.35f, 0.75f, cover), 0.1f + 0.15f * cover, 0f, 0f);
                look.Cirrus = 0.2f + 0.5f * Mathf.Clamp01(type * 2f);
                // Towering cumulus on the days of few, small clouds
                float towers = Mathf.Clamp01((0.5f - type) / 0.2f) * (1f - Mathf.Clamp01((cover - 0.3f) / 0.2f));
                look.Amounts.z = 0.15f * towers;
                look.TowerHeight = 0.45f;
                look.WindKmh = 36f;
                break;
            }
            case Regime.Broken:
                // (its table's 0.55 to 0.8 cumulus, raised to 0.6 to 0.9: the day measured rendered 0.88)
                look.Amounts = new Vector4(0.7f + 0.25f * cover, 0.2f + 0.3f * cover, 0f, 0f);
                // (its table has cirrus 0.3, but it rendered none on the broken day measured)
                look.Cirrus = 0f;
                look.Altocumulus = 0.3f + 0.1f * cover;
                look.DensityMultiplier = 0.45f;
                look.ShapeFactor = 0.85f;
                look.WindKmh = 43f;
                break;
            case Regime.LightRain:
                look.DeckBottom = 0f;
                look.DeckShape = -1f;
                look.AirWetness = 0.5f;
                look.Amounts = new Vector4(0.65f, 0.5f, 0f, 0.15f);
                look.DensityMultiplier = 0.45f;
                look.ShapeFactor = 0.6f;
                look.ErosionFactor = 0.7f;
                look.Rain = 0.3f;
                look.Darkness = 2f;
                look.BaseLight = 0.45f;
                look.Cirrus = 0.25f;
                look.Altocumulus = 0.35f;
                look.WindKmh = 43f;
                break;
            case Regime.Rain:
                look.DeckBottom = 0f;
                // Measured (its rendered layer, _WO_CloudRadii): 1457 m up, 2792 m deep; shape 0.52, deck erosion 0.06, shade 1.22
                look.AirWetness = 1f;
                // Measured: cumulus 0.33 under an altostratus deck of 0.69 (with a veil over all)
                look.Amounts = new Vector4(0.33f, 0.69f, 0f, 0.5f + 0.4f * precipitation);
                look.DensityMultiplier = 0.5f;
                look.ShapeFactor = 0.52f;
                look.ErosionFactor = 0.06f;
                look.BottomAltitude = 1457f;
                look.CumulusDepth = 2792f;
                look.DeckDepth = 2792f;
                look.Rain = 0.5f + 0.4f * precipitation;
                look.Darkness = 1.22f;
                look.DeckShape = 0.52f;
                look.DeckErosion = 0.06f;
                look.DeckDensity = 0.74f;
                // Its stratocumulus look: base band 500 m of shape 0.90 on noise 0.42 times the scale, relief at variety 0.6
                look.BaseBand = 500f;
                look.BaseShape = 0.9f;
                look.BaseScale = 0.42f;
                // Its deck relief (_WO_DeckRelief 0.108, 0.241, 1/3000 m, 0.215 of the layer): the base dips 300 m and
                // the top 670 m over 3 km, the base fades in over 600 m
                look.Relief = 300f;
                look.ReliefTop = 670f;
                look.SoftBase = 600f;
                // No darkening of the deck's base (WeatherOverhaul darkens by its shade only). Its rain gain is 0.94 against
                // 1.55 clear, but with its thinner layer and relief our deck passes more light: set by the measured zenith
                // (ours 197 at 0.61 of clear against its 160, so 0.38; the rain light, 0.45 at full rain, takes part of it)
                look.BaseLight = 1f;
                look.Gloom = 0.38f / Mathf.Lerp(1f, 0.45f, look.Rain);
                look.WindKmh = 54f;
                break;
            case Regime.Storm:
                look.DeckBottom = 0f;
                // Measured (its rendered layer): 1407 m up, 2598 m deep; shape 0.37, deck erosion 0.21, shade 1.39; cells up to 10 km
                look.AirWetness = 1f;
                // Measured: cumulus 0.43 under a full deck
                look.Amounts = new Vector4(0.43f, 1f, 0.4f + 0.4f * cover, 0.7f + 0.3f * precipitation);
                look.TowerHeight = 1f;
                look.TowerRain = 1f;
                look.DensityMultiplier = 0.5f;
                look.ShapeFactor = 0.37f;
                look.ErosionFactor = 0.21f;
                look.BottomAltitude = 1407f;
                look.CumulusDepth = 2598f;
                look.DeckDepth = 2598f;
                look.Rain = 0.7f + 0.3f * precipitation;
                look.Darkness = 1.39f;
                look.DeckShape = 0.37f;
                look.DeckErosion = 0.21f;
                look.DeckDensity = 0.8f;
                // Its nimbostratus look: base band 300 m of shape 0.70 on noise 0.44 times the scale
                look.BaseBand = 300f;
                look.BaseShape = 0.7f;
                look.BaseScale = 0.44f;
                // Its deck relief (0.123, 0.255, 1/3000 m, 0.231 of the layer): base 320 m, top 660 m, soft base 600 m
                look.Relief = 320f;
                look.ReliefTop = 660f;
                look.SoftBase = 600f;
                look.BaseLight = 1f;
                // Its storm gain is 1.78 (1.15 times its clear 1.55), but our deck passes more of the light: set by the
                // measured zenith instead (ours 146 at 0.48 of clear against its 125, so 0.34)
                look.Gloom = 0.34f / Mathf.Lerp(1f, 0.45f, look.Rain);
                look.WindKmh = 79f;
                break;
            case Regime.Snow:
                look.DeckBottom = 0f;
                look.DeckShape = -1f;
                look.AirWetness = 0.7f;
                look.Amounts = new Vector4(0.55f + 0.15f * cover, 0.35f + 0.2f * cover, 0f, 0.2f);
                look.DensityMultiplier = 0.45f;
                look.ShapeFactor = 0.6f;
                look.BottomAltitude = 600f;
                look.CumulusDepth = 2000f;
                look.DeckDepth = 2000f;
                look.Rain = 0.2f;
                look.Darkness = 1.4f;
                look.WindKmh = 50f;
                break;
            case Regime.Blizzard:
                look.DeckBottom = 0f;
                look.DeckShape = -1f;
                look.AirWetness = 1f;
                look.Amounts = new Vector4(0.7f, 0.8f + 0.1f * cover, 0f, 0.55f);
                look.DensityMultiplier = 0.5f;
                look.ShapeFactor = 0.45f;
                look.BottomAltitude = 500f;
                look.CumulusDepth = 2200f;
                look.DeckDepth = 2200f;
                look.Rain = 0.55f;
                look.Darkness = 1.8f;
                look.BaseLight = 0.35f;
                look.Gloom = 0.75f;
                look.WindKmh = 65f;
                break;
            case Regime.Fog:
                look.DeckBottom = 0f;
                look.DeckShape = -1f;
                look.AirWetness = 1f;
                look.Amounts = new Vector4(0.2f, 0.6f + 0.3f * cover, 0f, 0f);
                look.DensityMultiplier = 0.35f;
                look.ShapeFactor = 0.6f;
                look.BottomAltitude = 400f;
                look.CumulusDepth = 1200f;
                look.DeckDepth = 1200f;
                look.WindKmh = 22f;
                break;
            case Regime.Mist:
                look.DeckBottom = 0f;
                look.DeckShape = -1f;
                look.AirWetness = 1f;
                look.Amounts = new Vector4(0.2f, 0.6f + 0.3f * cover, 0f, 0f);
                look.DensityMultiplier = 0.35f;
                look.ShapeFactor = 0.6f;
                look.BottomAltitude = 400f;
                look.CumulusDepth = 1200f;
                look.DeckDepth = 1500f;
                look.Darkness = 1.5f;
                look.Tint = new Color(0.95f, 0.9f, 1f);
                look.WindKmh = 22f;
                break;
            case Regime.Ash:
                look.DeckBottom = 0f;
                look.DeckShape = -1f;
                look.AirWetness = 1f;
                look.Amounts = new Vector4(0.45f, 1f, 0f, 0.3f);
                look.DensityMultiplier = 0.5f;
                look.ShapeFactor = 0.4f;
                look.BottomAltitude = 1000f;
                look.CumulusDepth = 3000f;
                look.DeckDepth = 3000f;
                look.Rain = 0.3f;
                look.Darkness = 5f;
                look.BaseLight = 0.3f;
                look.Tint = new Color(0.62f, 0.56f, 0.52f);
                look.Glow = new Color(0.2f, 0.05f, 0.012f);
                look.WindKmh = 43f;
                break;
            case Regime.AshStorm:
                look.DeckBottom = 0f;
                look.DeckShape = -1f;
                look.AirWetness = 1f;
                look.Amounts = new Vector4(0.5f, 1f, 0.6f, 0.6f);
                look.TowerHeight = 1f;
                look.TowerRain = 0.8f;
                look.DensityMultiplier = 0.5f;
                look.ShapeFactor = 0.6f;
                look.BottomAltitude = 900f;
                look.CumulusDepth = 3500f;
                look.DeckDepth = 3500f;
                look.Rain = 0.6f;
                look.Darkness = 10f;
                look.BaseLight = 0.15f;
                look.Gloom = 0.5f;
                look.Tint = new Color(0.62f, 0.56f, 0.52f);
                look.Glow = new Color(0.25f, 0.06f, 0.015f);
                look.WindKmh = 79f;
                break;
            case Regime.PolarClear:
                look.DeckBottom = 0f;
                look.DeckShape = -1f;
                look.Amounts = new Vector4(0.05f, 0.3f + 0.2f * cover, 0f, 0f);
                look.Cirrus = 0.6f;
                look.DensityMultiplier = 0.25f;
                look.BottomAltitude = 3000f;
                look.CumulusDepth = 1000f;
                look.DeckDepth = 1000f;
                look.WindKmh = 29f;
                break;
        }
        // (below 0: the deck takes the clouds' own shape and erosion, at full density)
        if (look.DeckShape < 0f)
        {
            look.DeckShape = look.ShapeFactor;
            look.DeckErosion = look.ErosionFactor;
            look.DeckDensity = 1f;
        }
        // The altocumulus are off for now: drawn with our noise they come out as long streaks, not WeatherOverhaul's puffs
        // (which use its own textures)
        look.Altocumulus = 0f;
        switch (MainConfig.highClouds.Value)
        {
            case MainConfig.HighClouds.Off:
                look.Cirrus = 0f;
                look.Altocumulus = 0f;
                break;
            case MainConfig.HighClouds.Always when HasHighClouds(regime):
                look.Cirrus = Mathf.Max(look.Cirrus, 0.5f);
                break;
        }
        return look;
    }

    // Fair weather cumulus; the regimes change what they need from it
    private static Look Base()
    {
        return new Look
        {
            DensityMultiplier = 0.4f,
            ShapeFactor = 0.9f,
            ErosionFactor = 0.8f,
            BottomAltitude = 1200f,
            CumulusDepth = 2000f,
            DeckDepth = 1500f,
            Gloom = 1f,
            Rain = 0f,
            Darkness = 1f,
            // (no darkening under the clouds in fair weather: HDRP has none)
            BaseLight = 1f,
            WindKmh = 36f,
            Amounts = Vector4.zero,
            BaseShape = 0.9f,
            // Its deck on the clear (broken) day measured: shape 0.24, erosion 0.20, density 0.87, a soft veil
            DeckShape = 0.08f,
            DeckErosion = 0.1f,
            // thin and see-through: at full density 2 km of it becomes an opaque sheet
            DeckDensity = 0.25f,
            // above the cumulus (whose tops are about 1.6 km over the base), 1.5 km deep
            DeckBottom = 1600f,
            BaseScale = 1f,
            TowerHeight = 0.45f,
            TowerRain = 0f,
            Tint = Color.white,
            Glow = Color.black,
        };
    }

    private static bool Contains(string text, string part) => text.IndexOf(part, StringComparison.Ordinal) >= 0;
}
