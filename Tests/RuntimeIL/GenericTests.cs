using HarmonyLib;
using HarmonyMod;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

internal static class GenericTests
{
    [MethodImpl(MethodImplOptions.NoInlining)] public static int Target<T>(int value) => value + 1;
    public static class Box<T>
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public static int Target(int value) => value + 1;
        [MethodImpl(MethodImplOptions.NoInlining)] public static Type Owner() => typeof(Box<T>);
        [MethodImpl(MethodImplOptions.NoInlining)] public static Type Definition() => typeof(Box<>);
        [MethodImpl(MethodImplOptions.NoInlining)] public static Type Collection() => typeof(List<T>);
        [MethodImpl(MethodImplOptions.NoInlining)] public static string Mixed<U>() => typeof(T).Name + ":" + typeof(U).Name;
    }
    public class Container<T>
    {
        public T Value;
        [MethodImpl(MethodImplOptions.NoInlining)] T Sibling() => Value;
        [MethodImpl(MethodImplOptions.NoInlining)] public T Read()
        {
            Container<T> local = this;
            try { var array = new Container<T>[1]; array[0] = (Container<T>)(object)local; return array[0].Sibling(); }
            finally { GC.KeepAlive(local); }
        }
    }
    public struct ValueContainer<T>
    {
        public T Value;
        [MethodImpl(MethodImplOptions.NoInlining)] public T Read() => Value;
    }
    [MethodImpl(MethodImplOptions.NoInlining)] public static T Echo<T>(T value) { T local = value; return local; }
    [MethodImpl(MethodImplOptions.NoInlining)] public static int ReverseEcho(int value) => -1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int ReverseTranspiled(int value) => -1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int Transpiled<T>(int value) => value + 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int Recursive<T>(int value) => value == 0 ? 0 : Recursive<T>(value - 1) + 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static T RefOut<T>(ref T value, out T output) { output = value; return value; }
    [MethodImpl(MethodImplOptions.NoInlining)] public static int Inner(int value) => value + 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int Outer<T>(int value) => 10 + Inner(value);
    [MethodImpl(MethodImplOptions.NoInlining)] public static T InvalidTarget<T>(T value) => value;
    [MethodImpl(MethodImplOptions.NoInlining)] public static T Constrained<T>(T value) where T : struct, IComparable<T> => value.CompareTo(value) == 0 ? value : default;
    [MethodImpl(MethodImplOptions.NoInlining)] public static T Disposable<T>(T value) where T : IDisposable => value;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int MixedConstraints<T, U>() where T : class where U : struct => 1;
    public class OwnMembers<T>
    {
        public T Value;
        public OwnMembers(T value) => Value = value;
        static T StaticSibling(T value) => value;
        T InstanceSibling() => Value;
        [MethodImpl(MethodImplOptions.NoInlining)] public static object[] Target<U>(T value, U other)
        {
            var own = new OwnMembers<T>(StaticSibling(value));
            return [Identity(own.InstanceSibling()), other, typeof(T), typeof(U)];
        }
    }
#pragma warning disable CS0693
    public class Shadowed<T>
    {
        public T Value;
        [MethodImpl(MethodImplOptions.NoInlining)] public object Read<T>(T ignored) => Value;
        [MethodImpl(MethodImplOptions.NoInlining)] public object Structured<T>(T ignored) => Identity(Value);
    }
#pragma warning restore CS0693
    [MethodImpl(MethodImplOptions.NoInlining)] public static object Identity(object value) => value;
    // Closed instantiations install through their definition under a type guard. Callbacks keep their closed signatures.
    [MethodImpl(MethodImplOptions.NoInlining)] public static int Closed<T>(int value) => value + 1;
    public class Bag<T>
    {
        public T Item;
        [MethodImpl(MethodImplOptions.NoInlining)] public T Take(T fallback) => Item;
    }
    [MethodImpl(MethodImplOptions.NoInlining)] public static T Pass<T>(T value) => value;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int States<T>() => 1;
    static void IntStatePrefix(out int __state) => __state = 100;
    static void IntStatePostfix(int __state, ref int __result) => __result += __state;
    static void StringStatePrefix(out string __state) => __state = "1000";
    static void StringStatePostfix(string __state, ref int __result) => __result += int.Parse(__state);
    static void ClosedStringPostfix(ref int __result) => __result += 1000;
    static void ClosedIntPostfix(ref int __result) => __result += 10;
    static void BagIntPostfix(Bag<int> __instance, int fallback, ref int __result, int ___Item) => __result = __instance.Item + fallback + ___Item + 100;
    static void BagStringPostfix(Bag<string> __instance, ref string __result, MethodBase __originalMethod)
        => __result = __instance.Item + __originalMethod.DeclaringType.GetGenericArguments()[0].Name;
    static bool PassIntPrefix(int value, ref int __result) { __result = value * 2; return false; }
    static string PassStringPassthrough(string result) => result + "?";
    static int passFinalized;
    static Exception PassIntFinalizer(Exception __exception) { passFinalized++; return __exception; }
    static HarmonyMethod Callback(string name) => new(typeof(GenericTests).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic));
    static bool Eligible(MethodBase target) => (bool)typeof(RuntimeILPatching).GetMethod("Eligible", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, [target]);
    static void Postfix(ref int __result) => __result += 100;
    static void Guard<T>(ref int __result) { if (typeof(T) == typeof(string)) __result += 100; }
    static void Reset<T>(ref T __result) => __result = default;
    static void Observe() { }
    static object observed;
    static void ObserveObject(object value, object __result) => observed = __result;
    static void ObserveInstance<T>(Container<T> __instance) => observed = __instance;
    static void ResetInstance<T>(ref ValueContainer<T> __instance) => __instance.Value = default;
    static void MixedGuard<T, U>(ref string __result) { if (typeof(T) == typeof(string) && typeof(U) == typeof(int)) __result += ":patched"; }
    static Exception Finalizer<T>(Exception __exception) => __exception;
    static Exception InfixFinalizer(Exception __exception) => __exception;
    static void ObserveArgs(object[] __args) => observed = __args[0];
    static void ObserveRef<T>(T value) => observed = value;
    static MethodBase originalMethod;
    static void ObserveMethod(MethodBase __originalMethod) => originalMethod = __originalMethod;
    static IEnumerable<CodeInstruction> Invalid(IEnumerable<CodeInstruction> instructions)
    {
        yield return new(System.Reflection.Emit.OpCodes.Ldc_I4_1);
        yield return new(System.Reflection.Emit.OpCodes.Ldc_I4_8);
        yield return new(System.Reflection.Emit.OpCodes.Conv_U);
        yield return new(System.Reflection.Emit.OpCodes.Localloc);
        yield return new(System.Reflection.Emit.OpCodes.Pop);
        yield return new(System.Reflection.Emit.OpCodes.Pop);
        foreach (var instruction in instructions) yield return instruction;
    }
    static IEnumerable<CodeInstruction> Clone(IEnumerable<CodeInstruction> instructions) => instructions.Select(code => new CodeInstruction(code));
    // The shape of GUILocator's transpiler on Widgets.TextFieldNumeric<int>: a call with the first argument, inserted at the start of the body.
    static int observedFirst;
    static MethodBase observedCaller;
    static void ObserveFirst(int value) { observedFirst = value; observedCaller = Harmony.GetOriginalMethodFromStackframe(new System.Diagnostics.StackFrame(1)); }
    static IEnumerable<CodeInstruction> CallObserveFirst(IEnumerable<CodeInstruction> instructions)
    {
        yield return new(System.Reflection.Emit.OpCodes.Ldarg_0);
        yield return new(System.Reflection.Emit.OpCodes.Call, typeof(GenericTests).GetMethod(nameof(ObserveFirst), BindingFlags.Static | BindingFlags.NonPublic));
        foreach (var instruction in instructions) yield return instruction;
    }
    static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
        => instructions.Select(code => code.opcode == System.Reflection.Emit.OpCodes.Ldc_I4_1 ? new CodeInstruction(System.Reflection.Emit.OpCodes.Ldc_I4_7) : code);

    internal static void Run()
    {
        var provideWitness = false;
        RuntimeILPatching.Register(method => method.DeclaringType == typeof(GenericTests) || method.DeclaringType?.DeclaringType == typeof(GenericTests),
            method => method.Name == nameof(Disposable) ? provideWitness ? [((MethodInfo)method).MakeGenericMethod(typeof(MemoryStream))] : [] : null);
        var harmony = new Harmony("runtime.il.generics");
        var second = new Harmony("runtime.il.generics.second");
        var postfix = new HarmonyMethod(typeof(GenericTests).GetMethod(nameof(Postfix), BindingFlags.Static | BindingFlags.NonPublic));
        foreach (var target in new[] { typeof(GenericTests).GetMethod(nameof(Target)), typeof(Box<>).GetMethod(nameof(Box<object>.Target)) })
        {
            var eligible = (bool)typeof(RuntimeILPatching).GetMethod("Eligible", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, [target]);
            Check(eligible, "Explicit generic definitions must be eligible for runtime IL replacement");
            Check(Call(target, typeof(string)) == 6 && Call(target, typeof(object)) == 6 && Call(target, typeof(int)) == 6, "Warm controls");
            var replacement = harmony.Patch(target, postfix: postfix);
            Check(Call(target, typeof(string)) == 106 && Call(target, typeof(object)) == 106 && Call(target, typeof(int)) == 106,
                "Definition-wide installation must update previously compiled reference and value cases");
            Check(Call(target, typeof(double)) == 106, "New instantiation after installation");
            second.Patch(target, postfix: postfix);
            Check(Call(target, typeof(string)) == 206 && Call(target, typeof(int)) == 206, "Second owner");
            second.UnpatchAll(second.Id);
            Check(Call(target, typeof(string)) == 106 && Call(target, typeof(int)) == 106, "Partial unpatch");
            harmony.UnpatchAll(harmony.Id);
            Check(Call(target, typeof(string)) == 6 && Call(target, typeof(int)) == 6, "Final unpatch");
            Check(replacement == target, "A definition-wide target returns its definition. It has no callable copy");
        }
        var method = typeof(GenericTests).GetMethod(nameof(Target));
        harmony.Patch(method, postfix: new HarmonyMethod(typeof(GenericTests).GetMethod(nameof(Guard), BindingFlags.Static | BindingFlags.NonPublic)));
        Check(Target<string>(5) == 106 && Target<object>(5) == 6 && Target<int>(5) == 6, "Generic patch callback type guard");
        harmony.UnpatchAll(harmony.Id);
        harmony.Patch(typeof(GenericTests).GetMethod(nameof(Echo)), postfix: new HarmonyMethod(typeof(GenericTests).GetMethod(nameof(Reset), BindingFlags.Static | BindingFlags.NonPublic)));
        Check(Echo(42) == 0 && Echo("value") is null, "Typed generic result and local");
        var closedEcho = typeof(GenericTests).GetMethod(nameof(Echo)).MakeGenericMethod(typeof(int));
        harmony.CreateReversePatcher(closedEcho, new HarmonyMethod(typeof(GenericTests).GetMethod(nameof(ReverseEcho)))).Patch();
        Check(ReverseEcho(42) == 42, "Constructed reverse patch must read the generic definition's baseline");
        harmony.UnpatchAll(harmony.Id);
        harmony.Patch(typeof(GenericTests).GetMethod(nameof(Echo)), postfix: new HarmonyMethod(typeof(GenericTests).GetMethod(nameof(ObserveObject), BindingFlags.Static | BindingFlags.NonPublic)));
        Check(Echo(42) == 42 && Equals(observed, 42), "Generic boxing to object");
        Check(Echo("value") == "value" && Equals(observed, "value"), "Generic reference to object");
        harmony.UnpatchAll(harmony.Id);
        Check(Recursive<int>(2) == 2 && Recursive<string>(2) == 2, "Warm generic recursion");
        harmony.Patch(typeof(GenericTests).GetMethod(nameof(Recursive)), postfix: postfix);
        Check(Recursive<int>(2) == 302 && Recursive<string>(2) == 302, "Generic recursive MethodSpec");
        harmony.UnpatchAll(harmony.Id);
        foreach (var name in new[] { nameof(Box<object>.Owner), nameof(Box<object>.Definition), nameof(Box<object>.Collection) })
        {
            var target = typeof(Box<>).GetMethod(name);
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(GenericTests).GetMethod(nameof(Observe), BindingFlags.Static | BindingFlags.NonPublic)),
                transpiler: new HarmonyMethod(typeof(GenericTests).GetMethod(nameof(Clone), BindingFlags.Static | BindingFlags.NonPublic)));
            foreach (var type in new[] { typeof(string), typeof(int) })
            {
                var actual = (Type)typeof(Box<>).MakeGenericType(type).GetMethod(name).Invoke(null, null);
                var expected = name == nameof(Box<object>.Definition) ? typeof(Box<>)
                    : (name == nameof(Box<object>.Owner) ? typeof(Box<>) : typeof(List<>)).MakeGenericType(type);
                Check(actual == expected, "Generic type token meaning: " + name + "; expected=" + expected + "; actual=" + actual);
            }
            harmony.UnpatchAll(harmony.Id);
        }
        var mixed = typeof(Box<>).GetMethod(nameof(Box<object>.Mixed));
        harmony.Patch(mixed, postfix: new HarmonyMethod(typeof(GenericTests).GetMethod(nameof(MixedGuard), BindingFlags.Static | BindingFlags.NonPublic)));
        Check((string)RuntimeILPatching.Close(mixed, [typeof(string), typeof(int)]).Invoke(null, null) == "String:Int32:patched", "Combined class/method arguments");
        Check((string)RuntimeILPatching.Close(mixed, [typeof(int), typeof(string)]).Invoke(null, null) == "Int32:String", "Combined type guard preserves other cases");
        harmony.UnpatchAll(harmony.Id);
        var reference = new Container<int> { Value = 42 };
        var text = new Container<string> { Value = "value" };
        Check(reference.Read() == 42 && text.Read() == "value", "Warm instance controls");
        harmony.Patch(typeof(Container<>).GetMethod(nameof(Container<object>.Read)), postfix: new HarmonyMethod(typeof(GenericTests).GetMethod(nameof(ObserveInstance), BindingFlags.Static | BindingFlags.NonPublic)));
        Check(reference.Read() == 42 && ReferenceEquals(observed, reference), "Generic local, array, cast, sibling call, field, EH and receiver");
        Check(text.Read() == "value" && ReferenceEquals(observed, text), "Reference instance case");
        harmony.UnpatchAll(harmony.Id);
        var value = new ValueContainer<int> { Value = 42 };
        harmony.Patch(typeof(ValueContainer<>).GetMethod(nameof(ValueContainer<object>.Read)), prefix: new HarmonyMethod(typeof(GenericTests).GetMethod(nameof(ResetInstance), BindingFlags.Static | BindingFlags.NonPublic)));
        Check(value.Read() == 0 && value.Value == 0, "Generic struct receiver by reference");
        harmony.UnpatchAll(harmony.Id);
        var referenceMethod = typeof(GenericTests).GetMethod(nameof(RefOut));
        harmony.Patch(referenceMethod, prefix: new HarmonyMethod(typeof(GenericTests).GetMethod(nameof(ObserveArgs), BindingFlags.Static | BindingFlags.NonPublic)),
            postfix: new HarmonyMethod(typeof(GenericTests).GetMethod(nameof(ObserveRef), BindingFlags.Static | BindingFlags.NonPublic)));
        var number = 42;
        Check(RefOut(ref number, out var result) == 42 && result == 42 && Equals(observed, 42), "Generic ref/out and argument arrays");
        var stringValue = "value";
        Check(RefOut(ref stringValue, out var stringResult) == "value" && stringResult == "value" && Equals(observed, "value"), "Reference ref/out and arrays");
        harmony.UnpatchAll(harmony.Id);
        harmony.Patch(typeof(GenericTests).GetMethod(nameof(Constrained)), postfix: new HarmonyMethod(typeof(GenericTests).GetMethod(nameof(Reset), BindingFlags.Static | BindingFlags.NonPublic)));
        Check(Constrained(42) == 0, "Constrained generic body and callback");
        harmony.UnpatchAll(harmony.Id);
        var outer = typeof(GenericTests).GetMethod(nameof(Outer));
        harmony.CreateProcessor(outer).AddInnerPostfix(new HarmonyMethod(postfix.method)
            { innerMethod = new InnerMethod(typeof(GenericTests).GetMethod(nameof(Inner))) })
            .AddInnerFinalizer(new HarmonyMethod(typeof(GenericTests).GetMethod(nameof(InfixFinalizer), BindingFlags.Static | BindingFlags.NonPublic))
            { innerMethod = new InnerMethod(typeof(GenericTests).GetMethod(nameof(Inner))) }).Patch();
        Check(Outer<string>(5) == 116 && Outer<int>(5) == 116, "Generic structured Infix helper with ambient stack");
        harmony.UnpatchAll(harmony.Id);
        harmony.Patch(typeof(Container<>).GetMethod(nameof(Container<object>.Read)), prefix: new HarmonyMethod(typeof(GenericTests).GetMethod(nameof(ObserveMethod), BindingFlags.Static | BindingFlags.NonPublic)));
        Check(reference.Read() == 42 && originalMethod.Name == nameof(Container<object>.Read), "Generic __originalMethod binding");
        Console.WriteLine("original-method-context=" + originalMethod + "; owner=" + originalMethod.DeclaringType);
        harmony.UnpatchAll(harmony.Id);
        var invalid = typeof(GenericTests).GetMethod(nameof(InvalidTarget));
        var objectValue = new object();
        Check(InvalidTarget(42) == 42 && ReferenceEquals(InvalidTarget<object>(objectValue), objectValue) && InvalidTarget("value") == "value", "Warm both validation witnesses before rollback");
        try { harmony.Patch(invalid, transpiler: new HarmonyMethod(typeof(GenericTests).GetMethod(nameof(Invalid), BindingFlags.Static | BindingFlags.NonPublic))); throw new InvalidOperationException("Invalid generic IL was accepted"); }
        catch (InvalidProgramException error) { Console.WriteLine("generic-rejection=" + error); }
        Check(InvalidTarget(42) == 42 && ReferenceEquals(InvalidTarget<object>(objectValue), objectValue)
            && InvalidTarget("value") == "value" && Harmony.GetPatchInfo(invalid) is null, "Generic witness failure must roll back all tested cases and metadata");
        harmony.Patch(invalid, postfix: new HarmonyMethod(typeof(GenericTests).GetMethod(nameof(Reset), BindingFlags.Static | BindingFlags.NonPublic)));
        Check(InvalidTarget(42) == 0 && InvalidTarget("value") is null, "Warm patched rollback controls");
        try { second.Patch(invalid, transpiler: new HarmonyMethod(typeof(GenericTests).GetMethod(nameof(Invalid), BindingFlags.Static | BindingFlags.NonPublic))); throw new InvalidOperationException("Invalid generic rebuild was accepted"); }
        catch (InvalidProgramException error) { Console.WriteLine("generic-rebuild-rejection=" + error); }
        Check(InvalidTarget(42) == 0 && InvalidTarget("value") is null && Harmony.GetPatchInfo(invalid).Transpilers.Count == 0,
            "Generic rebuild failure must preserve the previous installation and metadata");
        harmony.UnpatchAll(harmony.Id);
        var disposable = typeof(GenericTests).GetMethod(nameof(Disposable));
        var previous = RuntimeILBody.ActiveIL(disposable);
        harmony.Patch(disposable, postfix: new HarmonyMethod(typeof(GenericTests).GetMethod(nameof(Reset), BindingFlags.Static | BindingFlags.NonPublic)));
        Check(Disposable(new MemoryStream()) is null && !RuntimeILBody.ActiveIL(disposable).SequenceEqual(previous), "A constrained definition installs without witnesses; the runtime compiles its known variants");
        harmony.UnpatchAll(harmony.Id);
        provideWitness = true;
        harmony.Patch(disposable, postfix: new HarmonyMethod(typeof(GenericTests).GetMethod(nameof(Reset), BindingFlags.Static | BindingFlags.NonPublic)));
        Check(Disposable(new MemoryStream()) is null, "Explicit witness for constrained generic definition");
        harmony.UnpatchAll(harmony.Id);
        provideWitness = false;
        harmony.Patch(disposable.MakeGenericMethod(typeof(MemoryStream)), postfix: new HarmonyMethod(typeof(GenericTests).GetMethod(nameof(Reset), BindingFlags.Static | BindingFlags.NonPublic)));
        Check(Disposable(new MemoryStream()) is null, "A requested closed member of a constrained definition is its own witness");
        harmony.UnpatchAll(harmony.Id);
        var states = typeof(GenericTests).GetMethod(nameof(States));
        harmony.Patch(states.MakeGenericMethod(typeof(int)), prefix: Callback(nameof(IntStatePrefix)), postfix: Callback(nameof(IntStatePostfix)));
        harmony.Patch(states.MakeGenericMethod(typeof(string)), prefix: Callback(nameof(StringStatePrefix)), postfix: Callback(nameof(StringStatePostfix)));
        Check(States<int>() == 101 && States<string>() == 1001 && States<object>() == 1, "Instantiations keep separate __state in one patch class");
        harmony.UnpatchAll(harmony.Id);
        harmony.Patch(typeof(GenericTests).GetMethod(nameof(MixedConstraints)), postfix: postfix);
        Check(MixedConstraints<string, long>() == 101, "Reference/value constraint witnesses");
        harmony.UnpatchAll(harmony.Id);
        var genericMethod = typeof(GenericTests).GetMethod(nameof(Target));
        harmony.Patch(genericMethod, prefix: new HarmonyMethod(typeof(GenericTests).GetMethod(nameof(ObserveMethod), BindingFlags.Static | BindingFlags.NonPublic)));
        Check(Target<int>(5) == 6 && ((MethodInfo)originalMethod).GetGenericMethodDefinition() == genericMethod,
            "Generic method metadata identifies the executing method family");
        Console.WriteLine("original-generic-method-context=" + originalMethod + "; arguments=" + string.Join(",", originalMethod.GetGenericArguments().Select(type => type.ToString())));
        harmony.UnpatchAll(harmony.Id);
        var transpiled = typeof(GenericTests).GetMethod(nameof(Transpiled));
        harmony.Patch(transpiled, transpiler: new HarmonyMethod(typeof(GenericTests).GetMethod(nameof(Transpile), BindingFlags.Static | BindingFlags.NonPublic)));
        var closedTranspiled = transpiled.MakeGenericMethod(typeof(int));
        Check(Transpiled<int>(5) == 12, "Definition transpiler");
        Check(PatchProcessor.GetOriginalInstructions(closedTranspiled).Any(code => code.opcode == System.Reflection.Emit.OpCodes.Ldc_I4_1), "Constructed baseline inspection");
        Check(PatchProcessor.GetCurrentInstructions(closedTranspiled).Any(code => code.opcode == System.Reflection.Emit.OpCodes.Ldc_I4_7), "Constructed current instruction inspection");
        harmony.CreateReversePatcher(closedTranspiled, new HarmonyMethod(typeof(GenericTests).GetMethod(nameof(ReverseTranspiled)))).Patch(HarmonyReversePatchType.Snapshot);
        Check(ReverseTranspiled(5) == 12, "Constructed reverse snapshot includes definition transpilers");
        harmony.UnpatchAll(harmony.Id);
        foreach (var name in new[] { nameof(Shadowed<object>.Read), nameof(Shadowed<object>.Structured) })
        {
            var target = typeof(Shadowed<>).GetMethod(name);
            var processor = harmony.CreateProcessor(target);
            if (name == nameof(Shadowed<object>.Structured)) processor.AddInnerFinalizer(new HarmonyMethod(typeof(GenericTests).GetMethod(nameof(InfixFinalizer), BindingFlags.Static | BindingFlags.NonPublic))
                { innerMethod = new InnerMethod(typeof(GenericTests).GetMethod(nameof(Identity))) });
            var replacement = processor.Patch();
            var instance = new Shadowed<string> { Value = "kept" };
            Check((string)typeof(Shadowed<string>).GetMethod(name).MakeGenericMethod(typeof(int)).Invoke(instance, [7]) == "kept", "Installed shadowed generic context: " + name);
            Check(replacement == target, "Shadowed definition returned without a callable copy: " + name);
            harmony.UnpatchAll(harmony.Id);
        }
        var ownTarget = typeof(OwnMembers<>).GetMethod(nameof(OwnMembers<object>.Target));
        var ownReplacement = harmony.CreateProcessor(ownTarget).AddInnerFinalizer(new HarmonyMethod(typeof(GenericTests).GetMethod(nameof(InfixFinalizer), BindingFlags.Static | BindingFlags.NonPublic))
            { innerMethod = new InnerMethod(typeof(GenericTests).GetMethod(nameof(Identity))) }).Patch();
        var expectedOwn = new object[] { "kept", 7, typeof(string), typeof(int) };
        Check(OwnMembers<string>.Target("kept", 7).SequenceEqual(expectedOwn), "Installed own-type calls and constructor");
        Check(ownReplacement == ownTarget, "Own-type definition returned without a callable copy");
        harmony.UnpatchAll(harmony.Id);
        Console.WriteLine("review-generic-gates=PASS; shadowed-parameters; own-type-members; helpers; no-definition-snapshot");
        var closedDefinition = typeof(GenericTests).GetMethod(nameof(Closed));
        var closedString = closedDefinition.MakeGenericMethod(typeof(string));
        var closedInt = closedDefinition.MakeGenericMethod(typeof(int));
        Check(Eligible(closedString) && !Eligible(typeof(Box<>).GetMethod(nameof(Box<object>.Mixed)).MakeGenericMethod(typeof(int))),
            "Closed instantiations are eligible through their definition; partially open methods are not");
        Check(Closed<string>(5) == 6 && Closed<object>(5) == 6 && Closed<int>(5) == 6, "Warm closed controls");
        var closedReplacement = harmony.Patch(closedString, postfix: Callback(nameof(ClosedStringPostfix)));
        Check(closedReplacement == closedString, "An instantiation installed through its definition returns itself");
        Check(Closed<string>(5) == 1006 && Closed<object>(5) == 6 && Closed<int>(5) == 6 && Closed<double>(5) == 6,
            "Only the targeted instantiation is patched, including the shared reference-type code");
        second.Patch(closedInt, postfix: Callback(nameof(ClosedIntPostfix)));
        Check(Closed<string>(5) == 1006 && Closed<int>(5) == 16 && Closed<object>(5) == 6, "Two instantiations of one family");
        harmony.Patch(closedDefinition, postfix: postfix);
        Check(Closed<string>(5) == 1106 && Closed<int>(5) == 116 && Closed<object>(5) == 106, "Definition patch composes with instantiation patches");
        second.UnpatchAll(second.Id);
        Check(Closed<string>(5) == 1106 && Closed<int>(5) == 106, "Instantiation unpatch rebuilds the family");
        Check(Harmony.GetPatchInfo(closedString).Postfixes.Count == 1 && Harmony.GetPatchInfo(closedDefinition).Postfixes.Count == 1
            && Harmony.GetPatchInfo(closedInt).Postfixes.Count == 0, "Instantiation patch identity");
        harmony.Patch(closedInt, transpiler: Callback(nameof(CallObserveFirst)));
        observedFirst = 0;
        Check(Closed<int>(5) == 106 && observedFirst == 5 && observedCaller == closedInt && Harmony.GetPatchInfo(closedInt).Transpilers.Count == 1,
            "An instantiation transpiler runs in a closed body that keeps the definition's postfix");
        observedFirst = 0;
        Check(Closed<string>(5) == 1106 && Closed<object>(5) == 106 && Closed<double>(5) == 106 && observedFirst == 0, "Siblings keep the guarded family body");
        second.Patch(closedInt, postfix: Callback(nameof(ClosedIntPostfix)));
        harmony.Patch(closedInt, transpiler: Callback(nameof(Transpile)));
        Check(Closed<int>(5) == 122 && observedFirst == 5, "Two transpilers and a second owner's postfix compose in the closed body");
        Check(PatchProcessor.GetCurrentInstructions(closedInt).Any(code => code.opcode == System.Reflection.Emit.OpCodes.Ldc_I4_7)
            && !PatchProcessor.GetCurrentInstructions(closedString).Any(code => code.opcode == System.Reflection.Emit.OpCodes.Ldc_I4_7), "The body view of an instantiation includes its transpilers");
        second.UnpatchAll(second.Id);
        harmony.Unpatch(closedInt, HarmonyPatchType.Transpiler, harmony.Id);
        observedFirst = 0;
        Check(Closed<int>(5) == 106 && observedFirst == 0 && Harmony.GetPatchInfo(closedInt).Transpilers.Count == 0, "Removing the transpilers returns the instantiation to the guarded family body");
        harmony.UnpatchAll(harmony.Id);
        Check(Closed<string>(5) == 6 && Closed<int>(5) == 6 && Closed<object>(5) == 6, "Family unpatch");
        var bagInt = new Bag<int> { Item = 5 };
        var bagString = new Bag<string> { Item = "v" };
        Check(bagInt.Take(7) == 5 && bagString.Take("f") == "v", "Warm bag controls");
        harmony.Patch(typeof(Bag<int>).GetMethod(nameof(Bag<int>.Take)), postfix: Callback(nameof(BagIntPostfix)));
        harmony.Patch(typeof(Bag<string>).GetMethod(nameof(Bag<string>.Take)), postfix: Callback(nameof(BagStringPostfix)));
        Check(bagInt.Take(7) == 117 && bagString.Take("f") == "vString" && new Bag<long> { Item = 3 }.Take(1) == 3,
            "Bridged instance, argument, result, field and __originalMethod on instantiations of a generic class");
        harmony.UnpatchAll(harmony.Id);
        var pass = typeof(GenericTests).GetMethod(nameof(Pass));
        harmony.Patch(pass.MakeGenericMethod(typeof(int)), prefix: Callback(nameof(PassIntPrefix)), finalizer: Callback(nameof(PassIntFinalizer)));
        harmony.Patch(pass.MakeGenericMethod(typeof(string)), postfix: Callback(nameof(PassStringPassthrough)));
        Check(Pass(21) == 42 && passFinalized == 1 && Pass("value") == "value?" && Pass(7L) == 7L && passFinalized == 1,
            "Instantiation prefix skip, passthrough postfix and finalizer with a generic result");
        harmony.UnpatchAll(harmony.Id);
        Console.WriteLine("generic-instantiations=PASS; guarded-family; shared-code-distinguished; bridged-types; prefix-passthrough-finalizer; instantiation-transpiler-closed-body");
        Console.WriteLine("generic-definitions=PASS; generic-method-and-class; precompiled-and-new; owners-and-unpatch; snapshot; closed-witness; instantiation-state");
    }

    static int Call(MethodInfo method, Type argument)
    {
        if (method.DeclaringType.ContainsGenericParameters)
            method = method.DeclaringType.MakeGenericType(argument).GetMethod(method.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (method.IsGenericMethodDefinition) method = method.MakeGenericMethod(argument);
        return (int)method.Invoke(null, [5]);
    }
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
