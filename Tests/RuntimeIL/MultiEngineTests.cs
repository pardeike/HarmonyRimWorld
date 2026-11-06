using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyMod;

// Reflection-only so neither Harmony is loaded in the default context before the two isolated engines.
internal static class MultiEngineTests
{
    [MethodImpl(MethodImplOptions.NoInlining)] public static int Target() => 1;
    public static int Source() => 23;
    public static void Postfix(ref int __result) => __result++;
    sealed class Context(string name, string path) : AssemblyLoadContext(name, false)
    {
        protected override Assembly Load(AssemblyName name) => name.Name == "0Harmony" ? LoadFromAssemblyPath(path) : null;
    }

    internal static void Run()
    {
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == "0Harmony"), "Default context must have no Harmony");
        var path = Path.Combine(Path.GetDirectoryName(typeof(MultiEngineTests).Assembly.Location), "0Harmony.dll");
        var aContext = new Context("engine-a", path);
        var bContext = new Context("engine-b", path);
        var a = aContext.LoadFromAssemblyPath(path);
        var b = bContext.LoadFromAssemblyPath(path);
        Check(a != b, "Engine identities must be distinct");
        var original = typeof(MultiEngineTests).GetMethod(nameof(Target));
        var source = typeof(MultiEngineTests).GetMethod(nameof(Source));
        var postfix = typeof(MultiEngineTests).GetMethod(nameof(Postfix));
        var baseline = RuntimeILBody.ActiveIL(original);
        var installs = 0;
        var installBody = (Action<MethodBase, MethodInfo, Func<Func<object, int>, byte[]>>)typeof(RuntimeILPatching)
            .GetMethod("Install", BindingFlags.Static | BindingFlags.NonPublic)
            .CreateDelegate(typeof(Action<MethodBase, MethodInfo, Func<Func<object, int>, byte[]>>));
        Action<MethodBase, MethodInfo, Func<Func<object, int>, byte[]>> install = (method, replacement, writeBody) =>
        {
            installBody(method, replacement, writeBody);
            installs++;
        };
        var patching = a.GetType("HarmonyLib.MethodBodyPatching");
        patching.GetMethod("SetEligibility").Invoke(null, [new Func<MethodBase, bool>(method => method == original)]);
        patching.GetMethod("SetBodySource").Invoke(null, [new Func<MethodBase, MethodBody>(_ => source.GetMethodBody())]);
        patching.GetMethod("SetInstaller").Invoke(null, [install]);
        var aEngine = Activator.CreateInstance(a.GetType("HarmonyLib.Harmony"), "engine.a");
        var bEngine = Activator.CreateInstance(b.GetType("HarmonyLib.Harmony"), "engine.b");
        Apply(aContext, a, aEngine, original, postfix);
        Check(Target() == 24, "First engine must read the custom baseline");
        Check(!RuntimeILBody.ActiveIL(original).SequenceEqual(baseline), "The shared provider must install the full runtime body");
        Apply(bContext, b, bEngine, original, postfix);
        Check(Target() == 25 && installs == 2, "Second engine must use the same source and installer");
        using (bContext.EnterContextualReflection()) bEngine.GetType().GetMethod("UnpatchAll").Invoke(bEngine, ["engine.b"]);
        Check(Target() == 24, "Second engine removal must preserve the first owner");
        using (aContext.EnterContextualReflection()) aEngine.GetType().GetMethod("UnpatchAll").Invoke(aEngine, ["engine.a"]);
        Check(Target() == 23 && installs == 4, "Both removals must use the shared provider");
        Console.WriteLine("two-engines=PASS; direct-body-installs=" + installs);
    }

    static void Apply(Context context, Assembly assembly, object engine, MethodBase original, MethodInfo postfix)
    {
        using var scope = context.EnterContextualReflection();
        var harmonyMethod = Activator.CreateInstance(assembly.GetType("HarmonyLib.HarmonyMethod"), postfix);
        var patch = engine.GetType().GetMethods().Single(method => method.Name == "Patch" && method.GetParameters().Length == 5);
        patch.Invoke(engine, [original, null, harmonyMethod, null, null]);
    }
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
