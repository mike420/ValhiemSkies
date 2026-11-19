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

// The tree is looked for once the player exists, when the whole scene is there
[HarmonyPatch(typeof(Player), nameof(Player.Awake))]
internal static class PlayerAwakePatch
{
    private static void Postfix()
    {
        SkyChanger.OnPlayerSpawned();
    }
}
