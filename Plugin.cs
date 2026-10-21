using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;

namespace FFVBlueMageHelper;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class BlueMageHelperPlugin : BasePlugin
{
    public const string PluginGuid = "com.lukahn.ffv.bluemagehelper";
    public const string PluginName = "FFV Blue Mage Helper";
    public const string PluginVersion = "1.0.0";

    internal static ManualLogSource LogSource;
    internal static ConfigEntry<float> ShowDuration;
    internal static ConfigEntry<int> MaxLines;
    internal static ConfigEntry<bool> Verbose;
    internal static ConfigEntry<bool> ShowTestBanner;

    public override void Load()
    {
        LogSource = Log;

        ShowDuration = Config.Bind(
            "Display",
            "ShowDurationSeconds",
            8f,
            "How long the blue magic banner stays on screen.");

        MaxLines = Config.Bind(
            "Display",
            "MaxLines",
            6,
            "Maximum number of enemy/spell lines shown at once. Extra lines are " +
            "replaced with a \"+N more\" note.");

        Verbose = Config.Bind(
            "Debug",
            "VerboseLogging",
            false,
            "Log detected enemies, learned blue magic IDs and the game's blue " +
            "magic mapping table.");

        ShowTestBanner = Config.Bind(
            "Debug",
            "ShowTestBannerOnStartup",
            false,
            "Shows a sample banner when the game starts. Useful for checking that " +
            "the blue box renders correctly.");

        Banner.EnsureTypeRegistered();

        var harmony = new Harmony(PluginGuid);
        harmony.PatchAll(Assembly.GetExecutingAssembly());

        LogSource.LogInfo(
            $"{PluginName} {PluginVersion} loaded (duration={ShowDuration.Value}s, " +
            $"maxLines={MaxLines.Value}, verbose={Verbose.Value}).");
    }
}

internal static class Banner
{
    private static BannerBehaviour _instance;

    public static BannerBehaviour Instance => _instance;

    public static void EnsureTypeRegistered()
    {
        try
        {
            if (!ClassInjector.IsTypeRegisteredInIl2Cpp<BannerBehaviour>())
                ClassInjector.RegisterTypeInIl2Cpp<BannerBehaviour>();
        }
        catch (Exception e)
        {
            BlueMageHelperPlugin.LogSource.LogError($"Could not register banner behaviour: {e}");
        }
    }

    public static void EnsureCreated()
    {
        if (_instance != null)
            return;

        try
        {
            var go = new UnityEngine.GameObject("FFV Blue Magic Banner");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<BannerBehaviour>();
            if (BlueMageHelperPlugin.Verbose.Value)
                BlueMageHelperPlugin.LogSource.LogInfo("Banner object created.");
        }
        catch (Exception e)
        {
            BlueMageHelperPlugin.LogSource.LogError($"Could not create banner: {e}");
        }
    }

    public static void Hide()
    {
        if (_instance != null)
            _instance.Hide();
    }
}
