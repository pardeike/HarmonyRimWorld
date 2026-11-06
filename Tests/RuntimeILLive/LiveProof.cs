using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimBridgeServer.Sdk;
using Waylith.Profiling.Instrumentation;

public sealed class HarmonyRuntimePipelineLiveProof
{
	static int calls;
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int Target()
	{
		var values = new int[7];
		GC.KeepAlive(values);
		return values.Length;
	}
	public static void Postfix(ref int __result) { calls++; __result += 100; }
	static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
	static string Status() => (string)AppDomain.CurrentDomain.GetAssemblies()
		 .Single(assembly => assembly.GetName().Name == "HarmonyMod.RuntimeIL")
		 .GetType("HarmonyMod.RuntimeILProvider").GetMethod("GetStatus").Invoke(null, null);

	[Tool("harmony/prototype_profiler_coexistence")]
	public static object VerifyProfilerCoexistence()
	{
		var original = typeof(HarmonyRuntimePipelineLiveProof).GetMethod(nameof(Target));
		var postfix = new HarmonyMethod(typeof(HarmonyRuntimePipelineLiveProof).GetMethod(nameof(Postfix)));
		var first = new Harmony("Harmony.RuntimePipeline.LiveProof.First");
		var second = new Harmony("Harmony.RuntimePipeline.LiveProof.Second");
		var oldTargets = ProfilerInstrumentation.Targets.ToArray();
		var oldEnabled = ProfilerInstrumentation.IsEnabled;
		var statusBefore = Status();
		var instrumented = 0;
		calls = 0;
		try
		{
			ProfilerInstrumentation.SetEnabled(false);
			ProfilerInstrumentation.SetTargets(["Harmony.RuntimePipeline.LiveProof!HarmonyRuntimePipelineLiveProof::Target"]);
			first.Patch(original, postfix: postfix);
			Check(Target() == 107 && calls == 1, "Initial Harmony patch");
			ProfilerInstrumentation.SetEnabled(true);
			instrumented = ProfilerInstrumentation.InstrumentedMethodCount;
			var firstProfiled = Target();
			Check(instrumented == 1 && firstProfiled == 107 && calls == 2,
				 $"Native instrumentation: instrumented={instrumented}, matched={ProfilerInstrumentation.MatchedMethodCount}, value={firstProfiled}, calls={calls}");
			second.Patch(original, postfix: postfix);
			Check(Target() == 207 && calls == 4, "Harmony rebuild under active native instrumentation");
			ProfilerInstrumentation.SetEnabled(false);
			Check(Target() == 207 && calls == 6, "Disabling native instrumentation must preserve both owners");
			ProfilerInstrumentation.SetEnabled(true);
			Check(ProfilerInstrumentation.InstrumentedMethodCount == 1 && Target() == 207 && calls == 8, "Re-enabling native instrumentation");
			second.UnpatchAll(second.Id);
			Check(Target() == 107 && calls == 9, "Partial unpatch under native instrumentation");
			first.UnpatchAll(first.Id);
			Check(Target() == 7 && calls == 9, "Final unpatch under native instrumentation");
			return new
			{
				statusBefore,
				statusAfter = Status(),
				instrumented,
				callbackCalls = calls,
				collectibleTarget = original.Module.Assembly.IsCollectible,
				success = true
			};
		}
		finally
		{
			ProfilerInstrumentation.SetEnabled(false);
			second.UnpatchAll(second.Id);
			first.UnpatchAll(first.Id);
			ProfilerInstrumentation.SetTargets(oldTargets);
			ProfilerInstrumentation.SetEnabled(oldEnabled);
		}
	}

	[Tool("harmony/prototype_provider_status")]
	public static string ProviderStatus() => Status();
}
