using System.Text;
using UnityEngine;

namespace ValheimSkies;

// Dev tool, not built into the mod: logs the shader globals WeatherOverhaul sets, for side-by-side measurements.
// To use: add it to ValheimSkies.csproj and call WeatherOverhaulProbe.Tick() from ValheimSkiesMod.Update.
internal static class WeatherOverhaulProbe
{
    private static readonly string[] Names = { "_WO_AirCamera", "_WO_AirCamForward", "_WO_AirFade", "_WO_AirFar", "_WO_AirFogColor", "_WO_AirGameSky", "_WO_AirGlow", "_WO_AirLayers", "_WO_AirNear", "_WO_AirRay00", "_WO_AirRay01", "_WO_AirRay10", "_WO_AirRay11", "_WO_AirSea", "_WO_AirShafts", "_WO_AirShaftsSize", "_WO_AirSun", "_WO_AirSunFogColor", "_WO_AirTerrain", "_WO_AirVol", "_WO_AirVolDecode", "_WO_AirVolEncode", "_WO_AirWaterAfter", "_WO_Altocumulus", "_WO_Altocumulus2", "_WO_BlueNoise", "_WO_BoltColor", "_WO_BoltIntensity", "_WO_BoltLayer", "_WO_BoltShape", "_WO_Cirrus", "_WO_CirrusMap", "_WO_CloudAir", "_WO_CloudAmbient", "_WO_CloudCamera", "_WO_CloudCamForward", "_WO_CloudDataLatest", "_WO_CloudDataRT", "_WO_CloudDebug", "_WO_CloudDome", "_WO_CloudDomeRest", "_WO_CloudFrame", "_WO_CloudFull", "_WO_CloudGain", "_WO_CloudGlow", "_WO_CloudGroundSun", "_WO_CloudHistory", "_WO_CloudHistoryData", "_WO_CloudHistoryLight", "_WO_CloudLight", "_WO_CloudLightDir", "_WO_CloudLightRT", "_WO_CloudLook", "_WO_CloudLook2", "_WO_CloudLut", "_WO_CloudMap", "_WO_CloudModel", "_WO_CloudModel2", "_WO_CloudMotion", "_WO_CloudPreset", "_WO_CloudPresetLut", "_WO_CloudPrevViewProj", "_WO_CloudRadii", "_WO_CloudRay00", "_WO_CloudRay01", "_WO_CloudRay10", "_WO_CloudRay11", "_WO_CloudShade", "_WO_CloudSizes", "_WO_CloudSkyTint", "_WO_CloudSkyTintAway", "_WO_CloudSteps", "_WO_CloudTint", "_WO_CloudTrace", "_WO_CloudVertical", "_WO_CloudViewProj", "_WO_CloudWind", "_WO_CloudWindSpeeds", "_WO_Congestus", "_WO_CongestusDrift", "_WO_DeckRelief", "_WO_DistanceWeights", "_WO_DomeBand", "_WO_DomeSteps", "_WO_Erosion32", "_WO_ExtraCloudDay", "_WO_ExtraCloudNoise", "_WO_ExtraCloudSwirley", "_WO_FlakeClamp", "_WO_Flash", "_WO_FlashInfo", "_WO_FlashLight", "_WO_FogSky", "_WO_FogSkyDome", "_WO_FogSkyParams", "_WO_FogSkyRing", "_WO_GroundAlbedo", "_WO_HighMapA", "_WO_HighMapB", "_WO_HighMaps", "_WO_HighTurn", "_WO_Illuminance", "_WO_LightDir", "_WO_MapA", "_WO_MapB", "_WO_MapCover", "_WO_MapDeck", "_WO_MapFront", "_WO_MapPlayer", "_WO_MapVariety", "_WO_MieAbsorption", "_WO_MieScattering", "_WO_Moon", "_WO_MoonAxis", "_WO_MoonColor", "_WO_MultiScatLut", "_WO_NightLayer", "_WO_Ozone", "_WO_Perlin32", "_WO_Radii", "_WO_Rayleigh", "_WO_Samples", "_WO_Shadow", "_WO_ShadowCenter", "_WO_ShadowDebug", "_WO_ShadowForward", "_WO_ShadowOrigin", "_WO_ShadowOut", "_WO_ShadowRight", "_WO_ShadowUp", "_WO_ShaftCloudsOn", "_WO_ShaftDebug", "_WO_ShaftFootprint", "_WO_ShaftGlare", "_WO_ShaftParams", "_WO_ShaftRay00", "_WO_ShaftRay01", "_WO_ShaftRay10", "_WO_ShaftRay11", "_WO_Shafts", "_WO_ShaftsIn", "_WO_ShaftSize", "_WO_ShaftsOut", "_WO_ShaftStrength", "_WO_ShaftSun", "_WO_SkyTint", "_WO_SkyTintAway", "_WO_SkyTintDir", "_WO_SkyViewMoon", "_WO_SkyViewSun", "_WO_StrikeGlow", "_WO_StrikeGlowParams", "_WO_SunDir", "_WO_SunGlare", "_WO_SunShadowMap", "_WO_SunShadowValid", "_WO_TransmittanceLut", "_WO_TreeAir", "_WO_TreeMask", "_WO_TreeMaskOn", "_WO_Veil", "_WO_VolCamera", "_WO_VolCookie", "_WO_VolCookieMatrix", "_WO_VolCookieParams", "_WO_VolDecode", "_WO_VolEncode", "_WO_VolFade", "_WO_VolFog", "_WO_VolForward", "_WO_VolGameLight", "_WO_VolGlow", "_WO_VolLayers", "_WO_VolLight", "_WO_VolLighting", "_WO_VolNear", "_WO_VolNoise", "_WO_VolNoiseParams", "_WO_VolRay00", "_WO_VolRay01", "_WO_VolRay10", "_WO_VolRay11", "_WO_VolScreen", "_WO_VolSea", "_WO_VolSize", "_WO_VolSunFog", "_WO_VolZ", "_WO_Worley128" };
    private static float next;

    public static void Tick()
    {
        if (Time.unscaledTime < next || EnvMan.instance == null)
        {
            return;
        }
        next = Time.unscaledTime + 10f;
        StringBuilder text = new StringBuilder("[ValheimSkies] WO globals:");
        int found = 0;
        foreach (string name in Names)
        {
            Vector4 v = Shader.GetGlobalVector(name);
            if (v != Vector4.zero)
            {
                text.Append(' ').Append(name).Append('=').Append(v.ToString("0.#####"));
                found++;
            }
        }
        if (found > 0)
        {
            Jotunn.Logger.LogInfo(text.ToString());
        }
    }
}
