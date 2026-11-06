using HarmonyLib;
using HarmonyMod;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;

internal static class ReentryTests
{
    [MethodImpl(MethodImplOptions.NoInlining)] public static int First() => 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int Second() => 2;
    public static void Postfix(ref int __result) => __result += 100;

    internal static void Run()
    {
        var first = typeof(ReentryTests).GetMethod(nameof(First));
        var second = typeof(ReentryTests).GetMethod(nameof(Second));
        var postfix = new HarmonyMethod(typeof(ReentryTests).GetMethod(nameof(Postfix)));
        var outer = new Harmony("runtime.il.outer");
        var inner = new Harmony("runtime.il.inner");
        var nested = false;
        var nativeDepth = 0;
        var install = (Action<MethodBase, MethodInfo, Func<Func<object, int>, byte[]>>)typeof(RuntimeILPatching)
            .GetMethod("Install", BindingFlags.Static | BindingFlags.NonPublic)
            .CreateDelegate(typeof(Action<MethodBase, MethodInfo, Func<Func<object, int>, byte[]>>));
        MethodBodyPatching.SetEligibility(method => method == first || method == second);
        MethodBodyPatching.SetBodySource(RuntimeILBody.Read);
        MethodBodyPatching.SetInstaller((method, replacement, writeBody) =>
        {
            if (method == first && !nested) { nested = true; inner.Patch(second, postfix: postfix); }
            if (++nativeDepth != 1) throw new InvalidOperationException("Runtime update calls overlapped");
            try { install(method, replacement, writeBody); }
            finally { nativeDepth--; }
        });
        outer.Patch(first, postfix: postfix);
        if (First() != 101 || Second() != 102) throw new InvalidOperationException("Nested target patch was lost");
        outer.UnpatchAll(outer.Id);
        if (First() != 1 || Second() != 102) throw new InvalidOperationException("Unpatch changed the other target");
        inner.UnpatchAll(inner.Id);
        if (Second() != 2) throw new InvalidOperationException("Nested target unpatch");
        Console.WriteLine("different-target-nesting=PASS; runtime-updates-serial=True");
    }
}
