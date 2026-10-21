using System;
using HarmonyLib;
using Last.Battle;

namespace FFVBlueMageHelper;

[HarmonyPatch(typeof(SceneSplash), nameof(SceneSplash.Start))]
internal static class SplashStartPatch
{
    private static void Postfix()
    {
        try
        {
            Banner.EnsureCreated();
            if (BlueMageHelperPlugin.ShowTestBanner.Value)
                Banner.Instance?.ShowTest();
            if (BlueMageHelperPlugin.Verbose.Value)
                GameState.LogSelfTest();
        }
        catch (Exception e)
        {
            BlueMageHelperPlugin.LogSource.LogWarning($"Could not initialise banner: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(Last.Scene.SceneTitleScreen), nameof(Last.Scene.SceneTitleScreen.CreateTitleWindow))]
internal static class TitleScreenPatch
{
    private static void Postfix()
    {
        try
        {
            if (BlueMageHelperPlugin.Verbose.Value)
                GameState.LogSelfTest();
        }
        catch (Exception e)
        {
            BlueMageHelperPlugin.LogSource.LogWarning($"Title self-test failed: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(BattleController), nameof(BattleController.StartInBattle))]
internal static class BattleStartPatch
{
    private static void Postfix(BattleController __instance)
    {
        BlueMagicNotifier.OnBattleStart(__instance);
    }
}

[HarmonyPatch(typeof(BattleController), nameof(BattleController.Exit))]
internal static class BattleExitPatch
{
    private static void Postfix()
    {
        try
        {
            Banner.Hide();
        }
        catch (Exception e)
        {
            BlueMageHelperPlugin.LogSource.LogWarning($"Could not hide banner: {e.Message}");
        }
    }
}
