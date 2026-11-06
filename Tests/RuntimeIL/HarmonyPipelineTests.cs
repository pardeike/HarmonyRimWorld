using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using HarmonyMod;
using Waylith.Injection;
using WaylithClr;

internal static class HarmonyPipelineTests
{
    [MethodImpl(MethodImplOptions.NoInlining)] public static int Target() => 3;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int Reverse() => -1;
    public static void Postfix(ref int __result) => __result += 100;
    static bool fail;
    public static IEnumerable<CodeInstruction> Switchable(IEnumerable<CodeInstruction> codes)
    {
        if (fail) throw new InvalidOperationException("Probe: rejected reconstruction");
        return codes;
    }
    static int tracked;
    public static void Track() => Interlocked.Increment(ref tracked);
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    internal sealed class TrackingStage(RuntimeMethodHandle target) : MethodBodyTransformer(1000)
    {
        internal bool Enabled = true;
        internal int Calls;
        protected override bool StaysAttached => true;
        protected override bool CanTransform(RuntimeMethodHandle method) => method.Equals(target);
        protected override bool TryTransform(RuntimeMethodHandle method, in MethodBodyView body,
            ReadOnlySpan<MethodBodyClause> clauses, MethodBodyBuilder result)
        {
            Calls++;
            if (!Enabled) return false;
            var bytes = new BlobBuilder();
            var encoded = new MethodBodyStreamEncoder(bytes).AddMethodBody(body.IL.Length + 5, body.MaxStackSize, clauses.Length, false,
                MetadataTokens.StandaloneSignatureHandle(body.LocalSignatureToken & 0xffffff),
                body.InitLocals ? MethodBodyAttributes.InitLocals : MethodBodyAttributes.None);
            var code = new BlobWriter(encoded.Instructions);
            code.WriteByte(0x28);
            code.WriteInt32(result.GetTokenFor(typeof(HarmonyPipelineTests).GetMethod(nameof(Track))));
            code.WriteBytes(body.IL.ToArray());
            var regions = encoded.ExceptionRegions;
            foreach (var clause in clauses)
                regions = regions.Add((ExceptionRegionKind)clause.Flags, clause.TryOffset + 5, clause.TryLength,
                    clause.HandlerOffset + 5, clause.HandlerLength,
                    clause.Flags == 0 ? MetadataTokens.EntityHandle(clause.ClassTokenOrFilterOffset) : default,
                    clause.Flags == ExceptionHandlingClauseOptions.Filter ? clause.ClassTokenOrFilterOffset + 5 : 0);
            result.Write(bytes.ToArray());
            return true;
        }
    }

    sealed class UpstreamStage(RuntimeMethodHandle target) : MethodBodyTransformer(-1000)
    {
        protected override bool CanTransform(RuntimeMethodHandle method) => method.Equals(target);
        protected override bool TryTransform(RuntimeMethodHandle method, in MethodBodyView body,
            ReadOnlySpan<MethodBodyClause> clauses, MethodBodyBuilder result)
        {
            result.Write([0x0a, 0x1d, 0x2a]); // tiny body: ldc.i4.7; ret
            return true;
        }
    }

    internal static void Run()
    {
        var original = typeof(HarmonyPipelineTests).GetMethod(nameof(Target));
        var handle = MethodBodyUpdater.GetDefinitionHandle(original);
        var errors = new ConcurrentQueue<string>();
        RuntimeILPatching.Register(method => method == original, reportError: errors.Enqueue);
        var harmony = new Harmony("runtime.pipeline.harmony");
        var other = new Harmony("runtime.pipeline.other");
        var postfix = new HarmonyMethod(typeof(HarmonyPipelineTests).GetMethod(nameof(Postfix)));
        var tracking = new TrackingStage(handle);
        var upstream = new UpstreamStage(handle);
        tracking.Register();
        try
        {
            harmony.Patch(original, postfix: postfix);
            Check(tracking.Rebuild(handle) is null && Target() == 103 && tracked == 1, "Profiler after Harmony");
            tracking.Enabled = false;
            Check(tracking.Rebuild(handle) is null && Target() == 103 && tracked == 1, "Profiler disable preserves Harmony");
            tracking.Enabled = true;
            Check(tracking.Rebuild(handle) is null && Target() == 103 && tracked == 2, "Profiler re-enable preserves Harmony");
            other.Patch(original, postfix: postfix);
            Check(Target() == 203 && tracked == 3, "Harmony update preserves instrumentation");
            other.UnpatchAll(other.Id);
            Check(Target() == 103 && tracked == 4, "Partial unpatch preserves instrumentation");

            upstream.Register();
            var calls = tracking.Calls;
            var failure = upstream.Rebuild(handle);
            Check(failure is null && Target() == 7 && tracking.Calls > calls, "A detached Harmony stage must not abort other pipeline callbacks");
            Check(RuntimeILPatching.Status.Contains("detached=1") && RuntimeILPatching.Status.Contains("applied=0"), "Detach state");
            ExpectDetached(() => PatchProcessor.GetOriginalInstructions(original));
            ExpectDetached(() => harmony.CreateReversePatcher(original, new HarmonyMethod(typeof(HarmonyPipelineTests).GetMethod(nameof(Reverse)))).Patch());
            Check(RuntimeILPatching.Status.Contains("detached=1"), "Reads must not clear a detached fault");
            Check(Injection.HarmonyRegenerate is { } regenerate && regenerate(original), "The host regenerate hook accepts a provider target");
            Check(Target() == 107 && RuntimeILPatching.Status.Contains("detached=0"), "Regeneration uses the new upstream body");
            Check(!Injection.HarmonyRegenerate(typeof(HarmonyPipelineTests).GetMethod(nameof(Track))), "The hook declines methods it does not manage");
            Check(Injection.HarmonyRegenerate(original) && Target() == 107, "A forced regeneration of an attached stage keeps its patches");
            harmony.Patch(original, transpiler: new HarmonyMethod(typeof(HarmonyPipelineTests).GetMethod(nameof(Switchable))));
            Check(Target() == 107, "A pass-through transpiler keeps the behavior");
            fail = true;
            try { Injection.HarmonyRegenerate(original); throw new InvalidOperationException("A rejected reconstruction must throw"); }
            catch (Exception error) when (error.ToString().Contains("rejected")) { }
            fail = false;
            Check(Target() == 107 && RuntimeILPatching.Status.Contains("detached=0") && RuntimeILPatching.Status.Contains("applied=1"),
                "A failed reconstruction keeps the working generation while the default body is unchanged");
            Thread.Sleep(1500);
            Check(errors.IsEmpty, "An expected detach that is regenerated in time must not report an error");

            upstream.Unregister();
            Check(Target() == 3 && RuntimeILPatching.Status.Contains("detached=1"), "Upstream removal detaches stale bytes");
            Check(SpinWait.SpinUntil(() => !errors.IsEmpty, 3000), "A detach nobody regenerates must report a visible host error outside callbacks");
            fail = true;
            try { RuntimeILPatching.Rebase(original); throw new InvalidOperationException("A rejected reconstruction must throw"); }
            catch (Exception error) when (error.ToString().Contains("rejected")) { }
            fail = false;
            Check(Target() == 3 && RuntimeILPatching.Status.Contains("detached=1"), "A failed reconstruction after a changed default body stays detached");
            RuntimeILPatching.Rebase(original);
            Check(Target() == 103, "Explicit rebase after upstream removal");
            harmony.UnpatchAll(harmony.Id);
            Check(Target() == 3, "Final unpatch preserves the profiler's stage");
            tracking.Unregister();
            var before = tracked;
            Check(Target() == 3 && tracked == before, "Profiler removal preserves the unpatched body");
        }
        finally
        {
            fail = false;
            if (upstream.IsRegistered) upstream.Unregister();
            if (RuntimeILPatching.Status.Contains("detached=1")) RuntimeILPatching.Rebase(original);
            other.UnpatchAll(other.Id); harmony.UnpatchAll(harmony.Id);
            if (tracking.IsRegistered) tracking.Unregister();
        }
        Console.WriteLine("pipeline-coexistence=PASS; owners, instrumentation, detach notification, read preservation, failed reconstruction, explicit rebase");
    }

    static void ExpectDetached(Action action)
    {
        try { action(); } catch (InvalidOperationException error) when (error.Message.Contains("detached")) { return; }
        throw new InvalidOperationException("A detached body reader must fail without altering ownership");
    }
}
