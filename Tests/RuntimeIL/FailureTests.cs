using HarmonyLib;
using HarmonyMod;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;

internal static class FailureTests
{
    [MethodImpl(MethodImplOptions.NoInlining)] public static int Target() => 1;
    static int finalized;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int TargetWithEH()
    {
        try { var value = Environment.TickCount; if (value == int.MinValue) throw new ArgumentException(); return 1; }
        catch (ArgumentException) { return 9; }
        finally { finalized++; }
    }
    static void Postfix(ref int __result) => __result += 100;

    internal static void Run()
    {
        var install = (Action<MethodBase, MethodInfo, Func<Func<object, int>, byte[]>>)typeof(RuntimeILPatching)
            .GetMethod("Install", BindingFlags.NonPublic | BindingFlags.Static)
            .CreateDelegate(typeof(Action<MethodBase, MethodInfo, Func<Func<object, int>, byte[]>>));
        var corrupt = true;
        MethodBodyPatching.SetEligibility(method => method.DeclaringType == typeof(FailureTests) && method.Name.StartsWith("Target"));
        MethodBodyPatching.SetBodySource(RuntimeILBody.Read);
        MethodBodyPatching.SetInstaller((method, replacement, writeBody) =>
            install(method, replacement, import =>
            {
                var body = writeBody(import);
                if (!corrupt) return body;
                // A valid header with an invalid int-return body. The separate executable replacement remains valid.
                return [0x03, 0x30, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0x2a];
            }));
        var harmony = new Harmony("runtime.il.failure");
        var postfix = new HarmonyMethod(typeof(FailureTests).GetMethod(nameof(Postfix), BindingFlags.NonPublic | BindingFlags.Static));
        foreach (var name in new[] { nameof(Target), nameof(TargetWithEH) })
        {
            var original = typeof(FailureTests).GetMethod(name);
            var call = (Func<int>)original.CreateDelegate(typeof(Func<int>));
            var baseline = original.GetMethodBody();
            corrupt = true;
            if (call() != 1) throw new InvalidOperationException("Warm control");
            Fails(() => harmony.Patch(original, postfix: postfix));
            if (call() != 1 || Harmony.GetPatchInfo(original) is not null) throw new InvalidOperationException("Initial rollback failed");
            var restored = original.GetMethodBody();
            if (restored.InitLocals != baseline.InitLocals || restored.LocalSignatureMetadataToken != baseline.LocalSignatureMetadataToken)
                throw new InvalidOperationException("Rollback changed local initialization or signature");
            corrupt = false;
            harmony.Patch(original, postfix: postfix);
            if (call() != 101) throw new InvalidOperationException("Valid install after recovery");
            corrupt = true;
            Fails(() => harmony.CreateProcessor(original).Patch());
            if (call() != 101 || Harmony.GetPatchInfo(original).Postfixes.Count != 1) throw new InvalidOperationException("Rebuild rollback failed");
            corrupt = false;
            harmony.UnpatchAll(harmony.Id);
            if (call() != 1) throw new InvalidOperationException("Unpatch after recovery");
        }
        Console.WriteLine("target-compilation-and-rollback=PASS; initial-and-existing; locals-and-EH");
    }

    static void Fails(Action action)
    {
        try { action(); }
        catch (InvalidProgramException) { return; }
        throw new InvalidOperationException("Actual target compilation must reject the corrupted body");
    }
}
