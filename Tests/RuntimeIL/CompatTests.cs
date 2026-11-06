using HarmonyLib;
using HarmonyMod;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using WaylithClr;

// Compatibility probe: what the provider does with patterns mods use, next to the detour baseline of the same Harmony core.
internal static class CompatTests
{
    [MethodImpl(MethodImplOptions.NoInlining)] public static int Open<T>(int value) => value + 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int ClassConstrained<T>(int value) where T : Stream => value + 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int InterfaceConstrained<T>(int value) where T : IDisposable => value + 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int NewConstrained<T>(int value) where T : class, new() => value + 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int StructConstrained<T>(int value) where T : struct => value + 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int AbstractConstrained<T>(int value) where T : TextReader => value + 1;
    public static class Box<T> { [MethodImpl(MethodImplOptions.NoInlining)] public static int Read(int value) => value + 1; }
    public static class Constrained<T> where T : Stream { [MethodImpl(MethodImplOptions.NoInlining)] public static int Read(int value) => value + 1; }
    [MethodImpl(MethodImplOptions.NoInlining)] public static int FreeFirst(int value) => value + 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int FreeStaged(int value) => value + 1;
    // The shape of PrepatcherCompat's FreePatchStage on build 5000: a priority -2000 stage that writes the free patch body.
    sealed class FreeStage() : MethodBodyTransformer(-2000)
    {
        static readonly System.Collections.Generic.Dictionary<nint, byte[]> bodies = [];
        static readonly FreeStage instance = Create();
        static FreeStage Create() { var stage = new FreeStage(); stage.Register(); return stage; }
        internal static void Apply(MethodBase method, byte[] body)
        {
            var handle = MethodBodyUpdater.GetDefinitionHandle(method);
            bodies[handle.Value] = body;
            var failure = instance.Rebuild(handle);
            if (failure is not null) throw new InvalidOperationException("free stage rebuild failed", failure);
        }
        protected override bool CanTransform(RuntimeMethodHandle method) => bodies.ContainsKey(method.Value);
        protected override bool TryTransform(RuntimeMethodHandle method, in MethodBodyView body, ReadOnlySpan<MethodBodyClause> clauses, MethodBodyBuilder result)
        {
            if (!bodies.TryGetValue(method.Value, out var bytes)) return false;
            result.Write(bytes);
            return true;
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)] public static int Warm<T>(int value) => value + 1;
    static void Postfix(ref int __result) => __result += 100;

    static MethodInfo Method(string name) => typeof(CompatTests).GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

    internal static void Run(bool provider)
    {
        if (provider) RuntimeILPatching.Register(method => method.DeclaringType == typeof(CompatTests) || method.DeclaringType?.DeclaringType == typeof(CompatTests),
            reportError: message => Console.WriteLine("report: " + message));
        var harmony = new Harmony("runtime.il.compat");
        var postfix = new HarmonyMethod(Method(nameof(Postfix)));
        string Try(string label, MethodBase target, Func<string> observe)
        {
            try
            {
                harmony.Patch(target, postfix: postfix);
                var result = observe();
                harmony.UnpatchAll(harmony.Id);
                return label + ": patched -> " + result + " | after unpatch -> " + observe();
            }
            catch (Exception error)
            {
                try { harmony.UnpatchAll(harmony.Id); } catch (Exception cleanup) { return label + ": THROWS " + error.GetType().Name + " and unpatch THROWS " + cleanup.GetType().Name; }
                var text = error.ToString().Replace('\n', ' ');
                return label + ": THROWS " + error.GetType().Name + ": " + text[..Math.Min(text.Length, 260)];
            }
        }
        Console.WriteLine("mode=" + (provider ? "provider" : "detour"));
        // Warmed instantiations before patching, like a method the game already called.
        Console.WriteLine("warm: " + Warm<string>(1) + " " + Warm<object>(1) + " " + Warm<int>(1));
        Console.WriteLine(Try("warm-open", Method(nameof(Warm)), () => $"string={Warm<string>(1)} object={Warm<object>(1)} int={Warm<int>(1)} long={Warm<long>(1)}"));
        Console.WriteLine(Try("open-unconstrained", Method(nameof(Open)), () => $"string={Open<string>(1)} object={Open<object>(1)} int={Open<int>(1)}"));
        Console.WriteLine(Try("open-class-constrained(Stream)", Method(nameof(ClassConstrained)), () => $"MemoryStream={ClassConstrained<MemoryStream>(1)} FileStream={ClassConstrained<FileStream>(1)}"));
        Console.WriteLine(Try("open-abstract-constrained(TextReader)", Method(nameof(AbstractConstrained)), () => $"StringReader={AbstractConstrained<StringReader>(1)}"));
        Console.WriteLine(Try("open-interface-constrained(IDisposable)", Method(nameof(InterfaceConstrained)), () => $"MemoryStream={InterfaceConstrained<MemoryStream>(1)}"));
        Console.WriteLine(Try("open-new-constrained(class,new)", Method(nameof(NewConstrained)), () => $"object={NewConstrained<object>(1)} MemoryStream={NewConstrained<MemoryStream>(1)}"));
        Console.WriteLine(Try("open-struct-constrained", Method(nameof(StructConstrained)), () => $"int={StructConstrained<int>(1)} long={StructConstrained<long>(1)}"));
        Console.WriteLine(Try("open-type Box<>.Read", typeof(Box<>).GetMethod("Read"), () => $"string={Box<string>.Read(1)} object={Box<object>.Read(1)} int={Box<int>.Read(1)}"));
        Console.WriteLine(Try("open-constrained-type Constrained<Stream>.Read", typeof(Constrained<>).GetMethod("Read"), () => $"MemoryStream={Constrained<MemoryStream>.Read(1)}"));
        Console.WriteLine(Try("closed-ref Open<string>", Method(nameof(Open)).MakeGenericMethod(typeof(string)), () => $"string={Open<string>(1)} object={Open<object>(1)} int={Open<int>(1)}"));
        Console.WriteLine(Try("closed-value Open<int>", Method(nameof(Open)).MakeGenericMethod(typeof(int)), () => $"string={Open<string>(1)} object={Open<object>(1)} int={Open<int>(1)} long={Open<long>(1)}"));
        Console.WriteLine(Try("closed-ref-type Box<string>.Read", typeof(Box<string>).GetMethod("Read"), () => $"string={Box<string>.Read(1)} object={Box<object>.Read(1)} int={Box<int>.Read(1)}"));
        Console.WriteLine(Try("closed-value-type Box<int>.Read", typeof(Box<int>).GetMethod("Read"), () => $"string={Box<string>.Read(1)} object={Box<object>.Read(1)} int={Box<int>.Read(1)}"));
        Console.WriteLine(Try("closed-constrained Constrained<MemoryStream>.Read", typeof(Constrained<MemoryStream>).GetMethod("Read"), () => $"MemoryStream={Constrained<MemoryStream>.Read(1)} FileStream={Constrained<FileStream>.Read(1)}"));
        // A free patch replaces a body directly before any Harmony patch exists, like PrepatcherCompat at RunAfterAssembliesLoaded.
        var free = Method(nameof(FreeFirst));
        MethodBodyUpdater.ReplaceMethodBody(free, [0x0e, 0x1f, 77, 0x2a]);
        Console.WriteLine("free-first: direct write -> " + FreeFirst(1) + " (expect 77)");
        try
        {
            harmony.Patch(free, postfix: postfix);
            Console.WriteLine("free-first: then Harmony postfix -> " + FreeFirst(1) + " (177 keeps the direct write, 102 drops it)");
            harmony.UnpatchAll(harmony.Id);
            Console.WriteLine("free-first: after unpatch -> " + FreeFirst(1) + " (77 keeps the direct write, 2 drops it)");
        }
        catch (Exception error) { Console.WriteLine("free-first: THROWS " + error); }
        var staged = Method(nameof(FreeStaged));
        try
        {
            FreeStage.Apply(staged, [0x0e, 0x1f, 77, 0x2a]);
            Console.WriteLine("free-stage: stage write -> " + FreeStaged(1) + " (expect 77)");
            harmony.Patch(staged, postfix: postfix);
            Console.WriteLine("free-stage: then Harmony postfix -> " + FreeStaged(1) + " (177 keeps the free patch, 102 drops it)");
            harmony.UnpatchAll(harmony.Id);
            Console.WriteLine("free-stage: after unpatch -> " + FreeStaged(1) + " (77 keeps the free patch, 2 drops it)");
        }
        catch (Exception error) { Console.WriteLine("free-stage: THROWS " + error); }
    }
}
