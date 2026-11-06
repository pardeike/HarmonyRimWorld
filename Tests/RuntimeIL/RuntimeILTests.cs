using HarmonyLib;
using HarmonyMod;
using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using WaylithClr;

internal static class RuntimeILTests
{
    static readonly ManualResetEventSlim entered = new(false), release = new(false);
    static int finalized;
    static DynamicMethod dynamicCallback;
    static FieldInfo foreignField;
    public static MethodInfo DynamicFactory(MethodBase original) => dynamicCallback;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int ILTarget(int value) => value + 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int ILBody(int value)
    {
        try { if (value < 0) throw new ArgumentException(); var local = value.ToString(); return local.Length + 4; }
        catch (ArgumentException) { return 9; }
        finally { finalized++; }
    }
    [MethodImpl(MethodImplOptions.NoInlining)] public static int ILRef(ref int value, out string text) { value++; text = "ok"; return value; }
    [MethodImpl(MethodImplOptions.NoInlining)] public static int ILBlock(int value) { entered.Set(); release.Wait(); return value; }
    [MethodImpl(MethodImplOptions.NoInlining)] public static int ILBlockingOuter(int value) => ILBlock(value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int ILInline(int value) => value + 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int InlineCaller(int value) => ILInline(value);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static int NativeInline(int value) => value + 1;
	[MethodImpl(MethodImplOptions.NoInlining)] public static int NativeInlineCaller(int value) => NativeInline(value);
    [MethodImpl(MethodImplOptions.NoInlining)] public static int ILPerf(int value) => value + 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int ILSnapshot(int value) => value + 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int NativePerf(int value) => value + 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int PlainPerf(int value) => value + 101;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int ILSpan(Span<int> values) { values[0]++; return values[0]; }
    readonly struct PrivateValue(int value) { internal readonly int Value = value; }
    [MethodImpl(MethodImplOptions.NoInlining)] static int ILPrivate(PrivateValue value) => value.Value;
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.Synchronized)] public static int ILMonitor(int value) => value + 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int ILInner(int value) => value + 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int ILOuter(int value) => ILInner(value) + 3;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int ILHelperOuter(int value) => ILInner(value) + 3;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int NativeHelperOuter(int value) => ILInner(value) + 3;
    public static Exception Finalizer(Exception __exception) => __exception;
    public static void HelperPrivateField(int ___finalized) { if (___finalized < 0) throw new InvalidOperationException("Private static field binding"); }
    [MethodImpl(MethodImplOptions.NoInlining)] public static int ILExternal(int value) => value + 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int ILForeign() => 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int NativeForeign() => 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int ILForeignField() => 0;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int NativeForeignField() => 0;
    public static IEnumerable<CodeInstruction> ReadForeignField(IEnumerable<CodeInstruction> _) =>
        [new(OpCodes.Ldsfld, foreignField), new(OpCodes.Ret)];
    public static IEnumerable<CodeInstruction> InvalidAllocation(IEnumerable<CodeInstruction> instructions)
    {
        yield return new(OpCodes.Ldc_I4_1); yield return new(OpCodes.Ldc_I4_8); yield return new(OpCodes.Conv_U);
        yield return new(OpCodes.Localloc); yield return new(OpCodes.Pop); yield return new(OpCodes.Pop);
        foreach (var instruction in instructions) yield return instruction;
    }
    [MethodImpl(MethodImplOptions.NoInlining)] public static int Reverse(int value) => -1;
    public static void Postfix(ref int __result) => __result += 100;
    public struct Large { public long A, B, C; }
    [MethodImpl(MethodImplOptions.NoInlining)] public static Large ILLarge(int value) => new() { A = value, B = 2, C = 3 };
    public static void LargePostfix(ref Large __result) => __result.C += 100;
    public struct Receiver
    {
        public int Value;
        [MethodImpl(MethodImplOptions.NoInlining)] public int ILMutate(int amount) { Value += amount; return Value; }
    }
    public sealed class ILConstructed
    {
        public int Value;
        [MethodImpl(MethodImplOptions.NoInlining)] public ILConstructed(int value) => Value = value;
    }
    public static void ConstructedPostfix(ILConstructed __instance) => __instance.Value += 100;
    public static class ILInitialized
    {
        public static int Value;
        static ILInitialized() { Value = 5; initializerRan = true; }
    }
    static bool initializerRan;
    public static void InitializerPostfix() => ILInitialized.Value += 100;
    // Signature kinds that carry required custom modifiers or function pointer types. The installed body keeps the target's signature.
    // C# emits modreq(InAttribute) for in parameters and ref readonly returns on virtual and interface members. Static ILIn stays as a control.
    static int readonlyStore = 5, pointerCalls;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int ILIn(in int value) => value + 1;
    public static void ReadonlyRefPostfix() => readonlyStore += 100;
    public class ILSignatures
    {
        int stored;
        public int Value { get => stored; init => stored = value; }
        [MethodImpl(MethodImplOptions.NoInlining)] public virtual int In(in int value) => value + 1;
        [MethodImpl(MethodImplOptions.NoInlining)] public virtual ref readonly int ReadonlyRef() => ref readonlyStore;
    }
    public static void InitPostfix(ref int ___stored) => ___stored += 100;
    static int Identity(int value) => value;
    [MethodImpl(MethodImplOptions.NoInlining)] public static unsafe int ILPointer(delegate*<int, int> pointer, int value) => value + 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static unsafe delegate*<int, int> ILPointerProvider() => &Identity;
    public static void CountPrefix() => pointerCalls++;

    static MethodInfo Method(string name) => typeof(RuntimeILTests).GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
    static bool Eligible(MethodBase target) => (bool)typeof(RuntimeILPatching).GetMethod("Eligible", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, [target]);
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Run()
    {
        Console.WriteLine("runtime=" + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
        RuntimeILPatching.Register(method => method.Name.StartsWith("IL", StringComparison.Ordinal) || method.DeclaringType == typeof(ILConstructed)
            || method.DeclaringType == typeof(ILInitialized) || method.DeclaringType == typeof(ILSignatures));
        var harmony = new Harmony("runtime.il.probe");
        var second = new Harmony("runtime.il.second");
        var postfix = new HarmonyMethod(Method(nameof(Postfix)));
        var original = Method(nameof(ILTarget));
        var callSiteSignature = new byte[] { 0x00, 0x01, 0x08, 0x08 }; // managed int(int), not a locals signature
        var signatureToken = MethodBodyUpdater.GetTokenForSignature(original.Module, callSiteSignature);
        Check(original.Module.ResolveSignature(signatureToken).SequenceEqual(callSiteSignature), "Runtime importer accepts a call-site signature");
        Console.WriteLine("call-site-signature-import=PASS");
		for (var i = 0; i < 30000; i++) Check(InlineCaller(i) == i + 1 && NativeInlineCaller(i) == i + 1, "inliner warmup");
        harmony.Patch(Method(nameof(ILInline)), postfix: postfix);
        Check(InlineCaller(5) == 106, "already compiled inliner must observe the patch");
		harmony.Patch(Method(nameof(NativeInline)), postfix: postfix);
		Console.WriteLine("compiled-inliner native=" + NativeInlineCaller(5) + " runtime-il=" + InlineCaller(5));
        var baseline = original.GetMethodBody().GetILAsByteArray();
        Check(ILTarget(5) == 6, "baseline");
        var replacement = harmony.Patch(original, postfix: postfix);
        var deferredSnapshot = harmony.Patch(Method(nameof(ILSnapshot)), postfix: postfix);
        Check(ILTarget(5) == 106, "patch");
        var directCall = new byte[5];
        directCall[0] = 0x28;
        BitConverter.GetBytes(MethodBodyUpdater.GetTokenFor(original.Module, Method(nameof(Postfix)))).CopyTo(directCall, 1);
        var active = RuntimeILBody.ActiveIL(original);
        Check(Enumerable.Range(0, Math.Max(0, active.Length - directCall.Length + 1))
            .Any(offset => active.AsSpan(offset, directCall.Length).SequenceEqual(directCall)),
            "Installed body must contain the patch callback directly, without replacement dispatch");
        Check(Harmony.GetOriginalMethod(replacement) == original, "replacement identity");
        Check((int)replacement.Invoke(null, [5]) == 106, "returned replacement remains callable");
        for (var i = 0; i < 10; i++) harmony.CreateProcessor(original).Patch();
        Check(ILTarget(5) == 106, "repatch must not duplicate patches");
		Check(RuntimeILBody.Read(original).GetILAsByteArray().SequenceEqual(baseline), "baseline after replacement");
        second.Patch(original, postfix: postfix);
        Check(ILTarget(5) == 206, "second owner");
        second.UnpatchAll(second.Id);
        Check(ILTarget(5) == 106, "partial unpatch");
        harmony.CreateReversePatcher(original, new HarmonyMethod(Method(nameof(Reverse)))).Patch();
        Check(Reverse(5) == 6, "reverse baseline");

        harmony.Patch(Method(nameof(ILBody)), postfix: postfix);
        Check(ILBody(123) == 107 && ILBody(-1) == 109 && finalized == 2, "locals/catch/finally");
        harmony.Patch(Method(nameof(ILRef)), postfix: postfix);
        var number = 3;
        Check(ILRef(ref number, out var text) == 104 && number == 4 && text == "ok", "ref/out");
        harmony.Patch(typeof(Receiver).GetMethod(nameof(Receiver.ILMutate)), postfix: postfix);
        var receiver = new Receiver { Value = 2 };
        Check(receiver.ILMutate(3) == 105 && receiver.Value == 5, "struct receiver");
        harmony.Patch(Method(nameof(ILLarge)), postfix: new HarmonyMethod(Method(nameof(LargePostfix))));
        var large = ILLarge(7);
        Check(large.A == 7 && large.B == 2 && large.C == 103, "large struct return");
        var constructor = typeof(ILConstructed).GetConstructor([typeof(int)]);
        harmony.Patch(constructor, postfix: new HarmonyMethod(Method(nameof(ConstructedPostfix))));
        Check(new ILConstructed(5).Value == 105, "instance constructor");
        var constructorCall = new byte[5];
        constructorCall[0] = 0x28;
        BitConverter.GetBytes(MethodBodyUpdater.GetTokenFor(constructor.Module, Method(nameof(ConstructedPostfix)))).CopyTo(constructorCall, 1);
        var constructorActive = RuntimeILBody.ActiveIL(constructor);
        Check(Enumerable.Range(0, Math.Max(0, constructorActive.Length - constructorCall.Length + 1))
            .Any(offset => constructorActive.AsSpan(offset, constructorCall.Length).SequenceEqual(constructorCall)),
            "Installed constructor body must contain the patch callback directly");
        var initializer = typeof(ILInitialized).TypeInitializer;
        var initializerBaseline = RuntimeILBody.ActiveIL(initializer);
        harmony.Patch(initializer, postfix: new HarmonyMethod(Method(nameof(InitializerPostfix))));
        Check(!initializerRan, "Patching a type initializer must not run it");
        Check(!RuntimeILBody.ActiveIL(initializer).SequenceEqual(initializerBaseline), "Installed type initializer body");
        Check(ILInitialized.Value == 105 && initializerRan, "type initializer");
        Signatures(harmony, postfix);

        var dynamic = new DynamicMethod("dynamicPostfix", typeof(void), [typeof(int).MakeByRefType()], typeof(RuntimeILTests).Module, true);
        dynamic.DefineParameter(1, ParameterAttributes.None, "__result");
        var il = dynamic.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldind_I4); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stind_I4); il.Emit(OpCodes.Ret);
        dynamicCallback = dynamic;
        harmony.Patch(original, postfix: new HarmonyMethod(Method(nameof(DynamicFactory))));
        Check(ILTarget(5) == 107, "dynamic callback");
        var callbackToken = MethodBodyUpdater.GetTokenFor(original.Module, dynamicCallback);
        var emptyLocalsToken = MethodBodyUpdater.GetTokenForSignature(original.Module, [0x07, 0x00]);
        for (var i = 0; i < 5; i++) harmony.CreateProcessor(original).Patch();
        Check(MethodBodyUpdater.GetTokenFor(original.Module, dynamicCallback) == callbackToken
            && MethodBodyUpdater.GetTokenForSignature(original.Module, [0x07, 0x00]) == emptyLocalsToken,
            "The runtime must reuse imported member and signature tokens without a host cache");
        Exception invalid = null;
        try { second.Patch(original, transpiler: new HarmonyMethod(Method(nameof(InvalidAllocation)))); }
        catch (Exception error) { invalid = error; }
        Check(invalid is not null && Chain(invalid).Any(error => error is InvalidProgramException), "Invalid IL must fail before publication");
        Console.WriteLine("invalid-IL-rejection=" + Chain(invalid).First(error => error is InvalidProgramException));
        Check(ILTarget(5) == 107 && Harmony.GetPatchInfo(original).Transpilers.Count == 0, "Failed update must preserve code and metadata");
        harmony.Patch(Method(nameof(ILSpan)), postfix: postfix);
        Span<int> span = stackalloc int[1]; span[0] = 3;
        Check(ILSpan(span) == 104 && span[0] == 4, "byref-like parameter");
        harmony.Patch(Method(nameof(ILPrivate)), postfix: postfix);
        Check(ILPrivate(new PrivateValue(4)) == 104, "private signature type");
        var monitorBody = Method(nameof(ILMonitor)).GetMethodBody().GetILAsByteArray();
        harmony.Patch(Method(nameof(ILMonitor)), postfix: postfix);
        Check(RuntimeILBody.ActiveIL(Method(nameof(ILMonitor))).SequenceEqual(monitorBody), "Synchronized target must keep the default path");
        harmony.CreateProcessor(Method(nameof(ILOuter))).AddInnerPostfix(new HarmonyMethod(Method(nameof(Postfix)))
            { innerMethod = new InnerMethod(Method(nameof(ILInner))) }).Patch();
        Check(ILOuter(5) == 109, "Infix must retain the ordinary construction path");
        harmony.CreateProcessor(Method(nameof(ILHelperOuter))).AddInnerPostfix(new HarmonyMethod(Method(nameof(Postfix)))
            { innerMethod = new InnerMethod(Method(nameof(ILInner))) })
            .AddInnerPrefix(new HarmonyMethod(Method(nameof(HelperPrivateField))) { innerMethod = new InnerMethod(Method(nameof(ILInner))) })
            .AddInnerFinalizer(new HarmonyMethod(Method(nameof(Finalizer))) { innerMethod = new InnerMethod(Method(nameof(ILInner))) }).Patch();
        Check(ILHelperOuter(5) == 109, "Structured Infix helper must retain exact EH and direct metadata calls");
        harmony.CreateProcessor(Method(nameof(NativeHelperOuter))).AddInnerPostfix(new HarmonyMethod(Method(nameof(Postfix)))
            { innerMethod = new InnerMethod(Method(nameof(ILInner))) })
            .AddInnerFinalizer(new HarmonyMethod(Method(nameof(Finalizer))) { innerMethod = new InnerMethod(Method(nameof(ILInner))) }).Patch();
        Check(NativeHelperOuter(5) == 109, "Native structured-helper control");
        HelperChurn(harmony);
        ForeignAccess(harmony);
        CollectibleTarget(harmony, postfix);
        ActiveFrame(harmony, second, new HarmonyMethod(Method(nameof(DynamicFactory))), postfix);
        harmony.Unpatch(Method(nameof(ILBlock)), HarmonyPatchType.All, harmony.Id);
        ActiveFrame(harmony, second, new HarmonyMethod(Method(nameof(DynamicFactory))), postfix, true);
        Performance(harmony, postfix);
        harmony.UnpatchAll(harmony.Id);
        Check(InlineCaller(5) == 6, "An updated inliner must observe final unpatch");
        Check(ILTarget(5) == 6 && ILBody(-1) == 9 && receiver.ILMutate(1) == 6 && ILIn(5) == 6 && new ILSignatures { Value = 5 }.In(5) == 6, "unpatch");
        Check(ILSnapshot(5) == 6 && (int)deferredSnapshot.Invoke(null, [5]) == 106,
            "An uncompiled returned replacement must preserve its patch snapshot after unpatch");
        UncoordinatedWriteContract(harmony, second, postfix);
        Console.WriteLine("checks=baseline,repatch,owners,reverse,EH,ref,out,receiver,large-result,dynamic-callback,invalid-IL,span,private-type,inliner,Infix,active-frame,unpatch,pipeline-ownership,signatures");
    }

    static unsafe void Signatures(Harmony harmony, HarmonyMethod postfix)
    {
        var staticIn = Method(nameof(ILIn));
        var inTarget = typeof(ILSignatures).GetMethod(nameof(ILSignatures.In));
        var readonlyTarget = typeof(ILSignatures).GetMethod(nameof(ILSignatures.ReadonlyRef));
        var initTarget = typeof(ILSignatures).GetProperty(nameof(ILSignatures.Value)).GetSetMethod();
        var pointerTarget = Method(nameof(ILPointer));
        var providerTarget = Method(nameof(ILPointerProvider));
        var flags = new[] { inTarget.GetParameters()[0].GetRequiredCustomModifiers().Length == 1, readonlyTarget.ReturnParameter.GetRequiredCustomModifiers().Length == 1,
            initTarget.ReturnParameter.GetRequiredCustomModifiers().Length == 1, pointerTarget.GetParameters()[0].ParameterType.IsFunctionPointer, providerTarget.ReturnType.IsFunctionPointer };
        Console.WriteLine("signature-modifiers: virtual-in=" + flags[0] + " virtual-ref-readonly=" + flags[1] + " init=" + flags[2] + " pointer-parameter=" + flags[3]
            + " pointer-return=" + flags[4] + " static-in-modreq-count=" + staticIn.GetParameters()[0].GetRequiredCustomModifiers().Length);
        Check(flags.All(flag => flag), "Signature probes must carry required modifiers or function pointer types");
        var targets = new[] { staticIn, inTarget, readonlyTarget, initTarget, pointerTarget, providerTarget };
        foreach (var target in targets) Check(Eligible(target), "Signature kind must stay on the runtime path: " + target.Name);
        var baselines = targets.Select(RuntimeILBody.ActiveIL).ToArray();
        harmony.Patch(staticIn, postfix: postfix);
        harmony.Patch(inTarget, postfix: postfix);
        harmony.Patch(readonlyTarget, postfix: new HarmonyMethod(Method(nameof(ReadonlyRefPostfix))));
        harmony.Patch(initTarget, postfix: new HarmonyMethod(Method(nameof(InitPostfix))));
        harmony.Patch(pointerTarget, postfix: postfix);
        harmony.Patch(providerTarget, prefix: new HarmonyMethod(Method(nameof(CountPrefix))));
        var five = 5;
        var instance = new ILSignatures();
        Check(ILIn(in five) == 106 && instance.In(in five) == 106, "in parameter");
        Check(instance.ReadonlyRef() == 105, "ref readonly return");
        Check(new ILSignatures { Value = 5 }.Value == 105, "init setter");
        Check(ILPointer(&Identity, 5) == 106, "function pointer parameter");
        Check(ILPointerProvider()(7) == 7 && pointerCalls == 1, "function pointer return");
        Check(!targets.Select(RuntimeILBody.ActiveIL).Zip(baselines, (active, baseline) => active.SequenceEqual(baseline)).Any(same => same),
            "Signature kinds must install through the runtime stage");
        Console.WriteLine("signature-kinds=PASS; in,virtual-in,virtual-ref-readonly,init,function-pointer-parameter,function-pointer-return");
    }

    static IEnumerable<Exception> Chain(Exception error) { for (; error is not null; error = error.InnerException) yield return error; }

    static void HelperChurn(Harmony harmony)
    {
        foreach (var native in new[] { true, false })
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var before = AppDomain.CurrentDomain.GetAssemblies().Length;
            for (var i = 0; i < 10; i++)
            {
                harmony.CreateProcessor(Method(native ? nameof(NativeHelperOuter) : nameof(ILHelperOuter))).Patch();
                Check((native ? NativeHelperOuter(5) : ILHelperOuter(5)) == 109, "Helper churn behavior");
            }
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Console.WriteLine("helper-rebuilds=10; retained-assembly-delta " + (native ? "native=" : "runtime-il=")
                + (AppDomain.CurrentDomain.GetAssemblies().Length - before));
        }
    }

    static void ForeignAccess(Harmony harmony)
    {
        foreach (var collectible in new[] { false, true })
        {
            var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("PrivateCallbacks" + collectible),
                collectible ? AssemblyBuilderAccess.RunAndCollect : AssemblyBuilderAccess.Run);
            var builder = assembly.DefineDynamicModule("callbacks").DefineType("PrivateCallbacks", TypeAttributes.Public);
            var field = builder.DefineField("Value", typeof(int), FieldAttributes.Private | FieldAttributes.Static);
            var postfix = builder.DefineMethod("Postfix", MethodAttributes.Private | MethodAttributes.Static, typeof(void), [typeof(int).MakeByRefType()]);
            postfix.DefineParameter(1, ParameterAttributes.None, "__result");
            var il = postfix.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldind_I4); il.Emit(OpCodes.Ldsfld, field);
            il.Emit(OpCodes.Add); il.Emit(OpCodes.Stind_I4); il.Emit(OpCodes.Ret);
            var type = builder.CreateType();
            foreignField = type.GetField(field.Name, BindingFlags.NonPublic | BindingFlags.Static);
            foreignField.SetValue(null, 7);
            var callback = new HarmonyMethod(type.GetMethod(postfix.Name, BindingFlags.NonPublic | BindingFlags.Static));
            harmony.Patch(Method(nameof(ILForeign)), postfix: callback);
            harmony.Patch(Method(nameof(NativeForeign)), postfix: callback);
            var transpiler = new HarmonyMethod(Method(nameof(ReadForeignField)));
            harmony.Patch(Method(nameof(ILForeignField)), transpiler: transpiler);
            harmony.Patch(Method(nameof(NativeForeignField)), transpiler: transpiler);
            Check(ILForeign() == 8 && NativeForeign() == 8 && ILForeignField() == 7 && NativeForeignField() == 7,
                "Foreign private callbacks and fields must match native behavior; collectible=" + collectible);
            foreach (var name in new[] { nameof(ILForeign), nameof(NativeForeign), nameof(ILForeignField), nameof(NativeForeignField) })
                harmony.Unpatch(Method(name), HarmonyPatchType.All, harmony.Id);
        }
        Console.WriteLine("foreign-private-method-and-field=PASS; collectible-and-noncollectible");
    }

    static void CollectibleTarget(Harmony harmony, HarmonyMethod postfix)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("CollectibleTarget"), AssemblyBuilderAccess.RunAndCollect);
        var builder = assembly.DefineDynamicModule("target").DefineType("Target", TypeAttributes.Public);
        var method = builder.DefineMethod("ILCollectibleTarget", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
        var il = method.GetILGenerator(); il.Emit(OpCodes.Ldc_I4_5); il.Emit(OpCodes.Ret);
        var target = builder.CreateType().GetMethod(method.Name);
        var eligible = (bool)typeof(RuntimeILPatching).GetMethod("Eligible", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, [target]);
        Check(!eligible, "Collectible targets must be declined before runtime installation");
        var baseline = target.GetMethodBody().GetILAsByteArray();
        harmony.Patch(target, postfix: postfix);
        Check((int)target.Invoke(null, null) == 105 && target.GetMethodBody().GetILAsByteArray().SequenceEqual(baseline),
            "Collectible targets must retain the native detour path");
        harmony.Unpatch(target, HarmonyPatchType.All, harmony.Id);
        Check((int)target.Invoke(null, null) == 5, "Collectible target unpatch");
        Console.WriteLine("collectible-target-default-path=PASS");
    }

    static void UncoordinatedWriteContract(Harmony harmony, Harmony second, HarmonyMethod postfix)
    {
        var original = Method(nameof(ILExternal));
        harmony.Patch(original, postfix: postfix);
        Check(ILExternal(5) == 106, "collision control");
        MethodBodyUpdater.ReplaceMethodBody(original, [0x0e, 0x1f, 77, 0x2a]);
        Check(ILExternal(5) == 77, "A direct runtime write is observable before rebuilding");
        second.Patch(original, postfix: postfix);
        Check(ILExternal(5) == 206 && Harmony.GetPatchInfo(original).Owners.Contains(second.Id),
            "The runtime pipeline deliberately supersedes direct writes that bypass its stages");
        Console.WriteLine("external-direct-write=unsupported-on-managed-target; next-pipeline-rebuild-supersedes-it");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference StartOld(Harmony harmony, HarmonyMethod postfix, bool helper)
    {
        var replacement = helper ? harmony.CreateProcessor(Method(nameof(ILBlockingOuter)))
            .AddInnerPostfix(new HarmonyMethod(Method(nameof(Postfix))) { innerMethod = new InnerMethod(Method(nameof(ILBlock))) })
            .AddInnerFinalizer(new HarmonyMethod(Method(nameof(Finalizer))) { innerMethod = new InnerMethod(Method(nameof(ILBlock))) }).Patch()
            : harmony.Patch(Method(nameof(ILBlock)), postfix: postfix);
        return new WeakReference(replacement);
    }

    static void ActiveFrame(Harmony harmony, Harmony second, HarmonyMethod callback, HarmonyMethod postfix, bool helper = false)
    {
        entered.Reset(); release.Reset();
        var weak = StartOld(harmony, callback, helper);
        var result = 0;
        var thread = new Thread(() => result = helper ? ILBlockingOuter(3) : ILBlock(3));
        thread.Start();
        if (!entered.Wait(5000)) throw new InvalidOperationException("Old frame did not enter");
        try
        {
            for (var i = 0; i < 5; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
            Check(!weak.IsAlive, "An active runtime body must not require its separate returned replacement");
            second.Patch(Method(helper ? nameof(ILBlockingOuter) : nameof(ILBlock)), postfix: postfix);
            for (var i = 0; i < 5; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        }
        finally { release.Set(); thread.Join(5000); }
        Check(!thread.IsAlive && result == (helper ? 103 : 4), "active old frame must retain its callback after replacement and GC");
        second.UnpatchAll(second.Id);
        Console.WriteLine("active-replacement-collected=True; old-frame-completed=True; structured-helper=" + helper);
    }

    static void Performance(Harmony harmony, HarmonyMethod postfix)
    {
        var native = Method(nameof(NativePerf));
        var runtime = Method(nameof(ILPerf));
        harmony.Patch(native, postfix: postfix);
        harmony.Patch(runtime, postfix: postfix);
        long nativeTicks = 0, ilTicks = 0;
        for (var round = 0; round < 20; round++)
        {
            var start = Stopwatch.GetTimestamp();
            harmony.CreateProcessor(native).Patch();
            Check(NativePerf(5) == 106, "native rebuild");
            nativeTicks += Stopwatch.GetTimestamp() - start;
            start = Stopwatch.GetTimestamp();
            harmony.CreateProcessor(runtime).Patch();
            Check(ILPerf(5) == 106, "direct rebuild and first call");
            ilTicks += Stopwatch.GetTimestamp() - start;
        }
        var factor = 1_000_000.0 / Stopwatch.Frequency / 20;
        Console.WriteLine("repatch-and-call-us native=" + nativeTicks * factor + " runtime-il=" + ilTicks * factor);
        const int rounds = 9, calls = 10_000_000;
        var samples = new[] { new double[rounds], new double[rounds], new double[rounds] };
        for (var mode = 0; mode < 3; mode++) Measure(mode, calls);
        for (var round = 0; round < rounds; round++)
        {
            for (var order = 0; order < 3; order++)
            {
                var mode = (round + order) % 3;
                samples[mode][round] = Measure(mode, calls);
            }
        }
        for (var mode = 0; mode < 3; mode++)
        {
            var name = new[] { "native", "runtime-il", "plain" }[mode];
            Console.WriteLine("invocation-ns-rounds " + name + "=" + string.Join(";", samples[mode]));
            Array.Sort(samples[mode]);
            Console.WriteLine("invocation-ns-median " + name + "=" + samples[mode][rounds / 2]);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static double Measure(int mode, int calls)
    {
        var sum = 0;
        var start = Stopwatch.GetTimestamp();
        if (mode == 0) for (var i = 0; i < calls; i++) sum = unchecked(sum + NativePerf(i));
        else if (mode == 1) for (var i = 0; i < calls; i++) sum = unchecked(sum + ILPerf(i));
        else for (var i = 0; i < calls; i++) sum = unchecked(sum + PlainPerf(i));
        var elapsed = Stopwatch.GetTimestamp() - start;
        Check(sum == unchecked((int)((long)calls * (calls - 1) / 2 + (long)101 * calls)), "Timing controls must do the same work");
        return elapsed * 1_000_000_000.0 / Stopwatch.Frequency / calls;
    }
}
