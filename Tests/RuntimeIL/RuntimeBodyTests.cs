using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using HarmonyMod;
using WaylithClr;

internal static class RuntimeBodyTests
{
    public static void Postfix() { }

    internal static void Run()
    {
        var game = Assembly.LoadFrom(System.IO.Path.Combine(AppContext.BaseDirectory, "Assembly-CSharp.dll"));
        var method = game.GetType("Verse.Map").GetMethod("MapPostTick");
        var reflected = method.GetMethodBody();
        foreach (var capacity in new[] { 0, 16, 64 })
        {
            var clauses = new MethodBodyClause[capacity];
            var success = MethodBodyUpdater.TryGetDefaultMethodBody(method, clauses, out var view);
            Console.WriteLine($"capacity={capacity}; success={success}; reported={view.ClauseCount}");
        }
        var baseline = RuntimeILBody.Read(method);
        Console.WriteLine($"reflection clauses={reflected.ExceptionHandlingClauses.Count}; adapter clauses={baseline.ExceptionHandlingClauses.Count}");
        if (baseline.ExceptionHandlingClauses.Count != reflected.ExceptionHandlingClauses.Count)
            throw new InvalidOperationException("Adapter dropped exception clauses");
        for (var index = 0; index < reflected.ExceptionHandlingClauses.Count; index++)
        {
            var expected = reflected.ExceptionHandlingClauses[index];
            var actual = baseline.ExceptionHandlingClauses[index];
            if (actual.Flags != expected.Flags || actual.TryOffset != expected.TryOffset || actual.TryLength != expected.TryLength
                || actual.HandlerOffset != expected.HandlerOffset || actual.HandlerLength != expected.HandlerLength
                || actual.Flags == ExceptionHandlingClauseOptions.Clause && actual.CatchType != expected.CatchType)
                throw new InvalidOperationException("Adapter changed exception clause " + index);
        }
        MethodBodyUpdater.ReplaceMethodBody(method, baseline.Serialize());
        MethodBodyUpdater.PrepareMethodBody(method);
        if (!RuntimeILBody.ActiveIL(method).SequenceEqual(reflected.GetILAsByteArray()))
            throw new InvalidOperationException("Serialized recovery changed baseline IL");
        RuntimeILPatching.Register(target => target == method);
        var harmony = new Harmony("runtime.body.probe");
        harmony.Patch(method, postfix: new HarmonyMethod(typeof(RuntimeBodyTests).GetMethod(nameof(Postfix))));
        MethodBodyUpdater.PrepareMethodBody(method);
        harmony.UnpatchAll(harmony.Id);
        MethodBodyUpdater.PrepareMethodBody(method);
        var activeClauses = new MethodBodyClause[64];
        if (!MethodBodyUpdater.TryGetCurrentMethodBody(method, activeClauses, out var active)
            || active.ClauseCount != reflected.ExceptionHandlingClauses.Count)
            throw new InvalidOperationException("Final unpatch dropped exception clauses");
    }
}
