using HarmonyLib;

namespace ValheimSkies;

// The world's sky belongs to the scene that holds EnvMan, so the chosen sky is put on each time that scene starts
[HarmonyPatch(typeof(EnvMan), nameof(EnvMan.Awake))]
internal static class EnvManAwakePatch
{
    private static void Postfix()
    {
        SkyChanger.OnWorldLoaded();
    }
}
