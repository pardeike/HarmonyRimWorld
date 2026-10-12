using HarmonyLib;
using System;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Verse;
using Waylith.Runtime;

namespace HarmonyMod;

internal static class LogLocation
{
    // This mod is startup code hosted by RimWorld, so early module setup is intentional.
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Initialize()
    {
        // Run before any mod constructor or Harmony patching in this assembly.
        // Preserve explicit developer destinations and Harmony's suppression policy.
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HARMONY_LOG_FILE"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HARMONY_NO_LOG")))
            return;

        try
        {
            var dataPath = WaylithRuntime.Current?.App.LogDataPath;
            if (string.IsNullOrEmpty(dataPath))
                return;

            var directory = Path.Combine(dataPath, Waylith.Logging.FileLog.LogSubfolder);
            Directory.CreateDirectory(directory);
            Environment.SetEnvironmentVariable("HARMONY_LOG_FILE", Path.Combine(directory, "harmony.log.txt"));
        }
        catch (Exception error)
        {
            // A log-directory failure must not prevent mods from loading.
            Console.Error.WriteLine($"Harmony Mod could not configure its log directory: {error.Message}");
        }
    }
}

[StaticConstructorOnStartup]
public class Main : Mod
{
    public static Settings settings;

    public static Version loadedHarmonyVersion = default;
    public static string loadedHarmonyInformationalVersion;
    public static string loadingError;

    public static string modVersion = BuildVersion(Assembly.GetExecutingAssembly());
    public static string versionSummary => $"Harmony Mod v{modVersion.Split('+')[0]}; Harmony library v{(loadedHarmonyInformationalVersion ?? loadedHarmonyVersion?.ToString() ?? "unknown").Split('+')[0]}";
    public static string versionTooltip => $"Harmony Mod v{modVersion.Split('+')[0]}\nHarmony library v{(loadedHarmonyInformationalVersion ?? loadedHarmonyVersion?.ToString() ?? "unknown").Split('+')[0]}";

    static Main()
    {
        string[] HarmonyNames = ["0Harmony", "Lib.Harmony", "HarmonyLib"];
        var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => HarmonyNames.Contains(a.GetName().Name));
        if (loaded != null)
        {
            loadedHarmonyVersion = loaded.GetName().Version ?? new Version(0, 0, 0, 0);
            loadedHarmonyInformationalVersion = BuildVersion(loaded);

            var loadedPath = SafeLocation(loaded);
            var ourPath = SafeLocation(Assembly.GetExecutingAssembly());
            var expectedPath = ourPath[..(ourPath.LastIndexOfAny(['\\', '/']) + 1)] + "0Harmony.dll";

            var ourHarmonyVersion = new Version(0, 0, 0, 0);
            try
            {
                if (System.IO.File.Exists(expectedPath))
                {
                    var assemblyName = AssemblyName.GetAssemblyName(expectedPath);
                    ourHarmonyVersion = assemblyName.Version ?? new Version(0, 0, 0, 0);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Could not read version of our 0Harmony.dll from disk: {ex.Message}");
            }

            if (expectedPath != loadedPath && ourHarmonyVersion > loadedHarmonyVersion)
            {
                loadingError = "HARMONY LOADING PROBLEM\n\nAnother Harmony library was loaded before the Harmony mod could.\n\n"
                    + $"Their version: {loadedHarmonyVersion}\nOur version: {ourHarmonyVersion}\n\n"
                    + $"This means that your Harmony version is now downgraded to {loadedHarmonyVersion} regardless of what the Harmony mod provides. "
                    + $"You need to update or remove that other loader/mod. The other Harmony was loaded from: {loadedPath}";
                if (Regex.IsMatch(loadedPath, @"data-[0-9A-F]{16}"))
                    loadingError += "\n\nThe path looks like Harmony was loaded from memory and not via a file path. This often hints to preloaders like Doorstop or similar.";
                Log.Error(loadingError);
            }
        }

        try
        {
            var harmony = new Harmony("net.pardeike.rimworld.lib.harmony");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Log.Message(versionSummary);
        }
        catch (Exception ex)
        {
            Log.Error($"Lib.Harmony could not be initialized: {ex.Message}");
        }
    }

    public Main(ModContentPack content) : base(content) => settings = GetSettings<Settings>();

    static string BuildVersion(Assembly assembly) => assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? assembly.GetName().Version?.ToString() ?? "unknown";

    static string SafeLocation(Assembly a)
    {
        try
        {
            if (!string.IsNullOrEmpty(a.Location))
                return a.Location;
        }
        catch { }

        return string.Empty;
    }
}
