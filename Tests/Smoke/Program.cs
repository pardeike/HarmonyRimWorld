using HarmonyLib;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;

var gameDir = Environment.GetEnvironmentVariable("RIMWORLD_ASSEMBLY_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Application Support/Steam/steamapps/common/RimWorld/RimWorld.app/Contents/MacOS");
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var path = Path.Combine(gameDir, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
var loggingMode = args.SingleOrDefault() ?? "default";
var logPath = PrepareLogging(loggingMode);
CheckLogging(logPath, loggingMode);
if (args.Length == 0)
    Run();

[MethodImpl(MethodImplOptions.NoInlining)]
static string PrepareLogging(string mode)
{
    var fixture = Path.Combine(AppContext.BaseDirectory, "log-smoke", Guid.NewGuid().ToString("N"));
    // Keep log data separate from save data, as it is in the macOS game.
    var runtime = new Waylith.Runtime.WaylithRuntime();
    runtime.ConfigurePaths(fixture, Path.Combine(fixture, "save-data"), Path.Combine(fixture, "cache"), Path.Combine(fixture, "log-data"));
    Environment.SetEnvironmentVariable("HARMONY_DEBUG", null);
    Environment.SetEnvironmentVariable("HARMONY_LOG_FILE", null);
    Environment.SetEnvironmentVariable("HARMONY_NO_LOG", null);
    var expected = Path.Combine(runtime.App.LogDataPath, "Logs", "harmony.log.txt");
    if (mode == "custom")
    {
        expected = Path.Combine(fixture, "custom-harmony.log.txt");
        Environment.SetEnvironmentVariable("HARMONY_LOG_FILE", expected);
    }
    else if (mode == "disabled")
        Environment.SetEnvironmentVariable("HARMONY_NO_LOG", "1");
    else if (mode != "default")
        throw new ArgumentException("Unknown logging smoke mode");
    return expected;
}

[MethodImpl(MethodImplOptions.NoInlining)]
static void CheckLogging(string expected, string mode)
{
    // Let the CLR invoke the module initializer on first managed access, before
    // Main's constructor or any Harmony write. Do not force module initialization
    // or reset FileLog's cached path in this process.
    var mod = Assembly.Load("HarmonyMod");
    _ = mod.GetType("HarmonyMod.Settings").GetField("noStacktraceCaching").GetValue(null);
    var disabled = mode == "disabled";
    if (FileLog.LogPath != (disabled ? null : expected))
        throw new InvalidOperationException("Unexpected initial Harmony log path");
    Console.WriteLine($"PASS: early module initializer configures Harmony log path ({mode})");
    Harmony.DEBUG = true;
    try
    {
        _ = new Harmony("harmony.logging.smoke");
        FileLog.LogBuffered("Buffered logging smoke marker");
        FileLog.FlushBuffer();
        if (disabled)
        {
            if (File.Exists(expected)) throw new InvalidOperationException("Suppressed Harmony log was created");
            FileLog.GetBuffer(true);
        }
        else
        {
            var text = File.ReadAllText(expected);
            if (!text.Contains("harmony.logging.smoke") || !text.Contains("Buffered logging smoke marker"))
                throw new InvalidOperationException("Early debug output or buffered output was lost");
        }
        Console.WriteLine($"PASS: first debug write and buffered output follow Harmony log policy ({mode})");
    }
    finally { Harmony.DEBUG = false; }
}

[MethodImpl(MethodImplOptions.NoInlining)]
static void Run()
{
    Console.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; core library: {typeof(object).Assembly.Location}");
    var harmony = new Harmony("harmony3.smoke");
    var mod = typeof(HarmonyMod.Settings).Assembly;
    var settings = typeof(HarmonyMod.Settings);
    var enhance = settings.GetField("noStacktraceEnhancing");
    var cache = settings.GetField("noStacktraceCaching");
    var cacheStore = mod.GetType("HarmonyMod.ExceptionTools").GetField("seenStacktraces", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    void ClearCache() => cacheStore.GetType().GetMethod("Clear").Invoke(cacheStore, null);
    void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + label);
        Console.WriteLine("PASS: " + label);
    }
    try
    {
        var bundledVersion = AssemblyName.GetAssemblyName(Path.Combine(AppContext.BaseDirectory, "0Harmony.dll")).Version;
        var expectedModVersion = Version.Parse(Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().Single(attribute => attribute.Key == "ModVersion").Value);
        Check(bundledVersion.Major == 3 && typeof(Harmony).Assembly.GetName().Version == bundledVersion, "loaded Harmony matches the bundled Harmony 3 assembly identity");
        Check(mod.GetName().Version == expectedModVersion, "mod assembly identity matches the configured release version");
        harmony.Patch(AccessTools.Method(typeof(Target), nameof(Target.Value)), postfix: new HarmonyMethod(typeof(Target), nameof(Target.Postfix)));
        Check(Target.Value() == 42, "Harmony postfix detour on .NET 10");
        harmony.Patch(AccessTools.Method(typeof(Verse.Log), nameof(Verse.Log.Message), [typeof(string)]), prefix: new HarmonyMethod(typeof(Target), nameof(Target.CaptureLog)));
        RuntimeHelpers.RunClassConstructor(typeof(HarmonyMod.Main).TypeHandle);
        Check(HarmonyMod.Main.loadedHarmonyVersion == bundledVersion && HarmonyMod.Main.loadingError == null, "actual mod initializer accepts the bundled Harmony");
        var modInfo = mod.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;
        var libraryInfo = typeof(Harmony).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;
        Check(modInfo == mod.GetName().Version.ToString() && HarmonyMod.Main.modVersion == modInfo, "mod reports its normal release version without temporary timestamps");
        Check(HarmonyMod.Main.loadedHarmonyInformationalVersion == libraryInfo, "mod reports the actual loaded library informational version");
        Check(HarmonyMod.Main.versionSummary == $"Harmony Mod v{expectedModVersion}; Harmony library v{libraryInfo.Split('+')[0]}", "startup version summary retains release and prerelease versions without source build metadata");
        Check(Target.InitializationMessages.Contains(HarmonyMod.Main.versionSummary), "mod initializer logs the concise release versions");
        Check(HarmonyMod.Main.versionTooltip == $"Harmony Mod v{expectedModVersion}\nHarmony library v{libraryInfo.Split('+')[0]}", "player tooltip retains release and prerelease versions without source build metadata");
        var targets = Harmony.GetAllPatchedMethods().Where(method => Harmony.GetPatchInfo(method).Owners.Contains("net.pardeike.rimworld.lib.harmony")).ToArray();
        Check(targets.Any(method => method.DeclaringType == typeof(RimWorld.VersionControl) && method.Name == "DrawInfoInCorner"), "version-display patch installed");
        Check(targets.Any(method => method.DeclaringType == typeof(Verse.Log) && method.Name == "ResetMessageCount"), "log-reset patch installed");
        Check(targets.Any(method => method.DeclaringType == typeof(StackTrace) && method.Name == "ToString"), ".NET 10 stacktrace patch installed");
        Exception captured;
        try { Target.Throw(); throw new Exception("Expected target to throw"); }
        catch (InvalidOperationException ex) { captured = ex; }
        Exception rethrown;
        try { ExceptionDispatchInfo.Capture(captured).Throw(); throw new Exception("Expected rethrow"); }
        catch (InvalidOperationException ex) { rethrown = ex; }
        var trace = new StackTrace(rethrown, true);
        ClearCache();
        cache.SetValue(null, true);
        enhance.SetValue(null, true);
        var native = trace.ToString();
        enhance.SetValue(null, false);
        var enhanced = trace.ToString();
        Check(enhanced.StartsWith(native), "native stacktrace and rethrow formatting preserved");
        Check(!enhanced.Contains("[Ref "), "cache disabled preserves full trace");
        harmony.Patch(AccessTools.Method(typeof(Target), nameof(Target.Throw)), prefix: new HarmonyMethod(typeof(Target), nameof(Target.ThrowPrefix)));
        var secondOwner = new Harmony("harmony3.smoke.second-owner");
        secondOwner.Patch(AccessTools.Method(typeof(Target), nameof(Target.Throw)), prefix: new HarmonyMethod(typeof(Target), nameof(Target.ThrowPrefix)));
        try { Target.Throw(); }
        catch (InvalidOperationException ex)
        {
            var annotated = new StackTrace(ex, true).ToString();
            Check(annotated.Contains("harmony3.smoke") && annotated.Contains("PREFIX"), "stacktrace includes patch owner and kind");
            Check(annotated.Contains("PREFIX harmony3.smoke:") && annotated.Contains("PREFIX harmony3.smoke.second-owner:"), "same patch method retains both registered owners");
        }
        cache.SetValue(null, false);
        ClearCache();
        var first = trace.ToString();
        var second = trace.ToString();
        Check(first.StartsWith("[Ref ") && !first.Contains("Duplicate stacktrace"), "first cached trace remains complete");
        Check(second == first && second.Contains(native), "discarded formatting cannot turn a subsequent trace into a bare reference");
        Check(rethrown.ToString().Contains(native), "exception formatting retains the complete trace after prior formatting");
        var repeated = Enumerable.Range(0, 32).AsParallel().Select(_ => trace.ToString()).ToArray();
        Check(repeated.All(text => text == first), "concurrent repeated formatting always retains the full trace");
        Verse.Log.ResetMessageCount();
        Check((int)cacheStore.GetType().GetProperty("Count").GetValue(cacheStore) == 0, "actual game log reset clears cached reference IDs");
        enhance.SetValue(null, true);
        Check(trace.ToString() == native, "enhancement disabled restores runtime formatting");
        Console.WriteLine("Runtime smoke checks passed; this is not an in-game acceptance test.");
    }
    finally
    {
        enhance.SetValue(null, false);
        cache.SetValue(null, false);
        harmony.UnpatchAll(harmony.Id);
        harmony.UnpatchAll("harmony3.smoke.second-owner");
        harmony.UnpatchAll("net.pardeike.rimworld.lib.harmony");
    }
}

static class Target
{
    public static readonly List<string> InitializationMessages = [];
    public static bool CaptureLog(string text) { InitializationMessages.Add(text); return false; }
    [MethodImpl(MethodImplOptions.NoInlining)] public static int Value() => 1;
    public static void Postfix(ref int __result) => __result = 42;
    [MethodImpl(MethodImplOptions.NoInlining)] public static void Throw() => throw new InvalidOperationException("smoke fixture");
    public static void ThrowPrefix() { }
}
