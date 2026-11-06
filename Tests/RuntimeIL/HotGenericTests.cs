using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using HarmonyMod;

internal static class HotGenericTests
{
	[MethodImpl(MethodImplOptions.NoInlining)] public static int Target<T>(int value) => value + 1;
	public static class Box<T>
	{
		[MethodImpl(MethodImplOptions.NoInlining)] public static int Read(int value) => value + 1;
	}
	static void Postfix(ref int __result) => __result += 100;
	static void Exercise(int offset)
	{
		for (var index = 0; index < 500000; index++)
			if (Target<string>(index) != index + offset || Target<object>(index) != index + offset
				 || Target<int>(index) != index + offset || Box<string>.Read(index) != index + offset
				 || Box<object>.Read(index) != index + offset || Box<int>.Read(index) != index + offset)
				throw new InvalidOperationException("Hot generic target lost its expected patch state");
	}
	internal static void Run()
	{
		RuntimeILPatching.Register(method => method.DeclaringType == typeof(HotGenericTests) || method.DeclaringType == typeof(Box<>));
		Exercise(1);
		Thread.Sleep(200);
		Exercise(1);
		var harmony = new Harmony("runtime.il.hot-generics");
		var postfix = new HarmonyMethod(typeof(HotGenericTests).GetMethod(nameof(Postfix), BindingFlags.Static | BindingFlags.NonPublic));
		try
		{
			harmony.Patch(typeof(HotGenericTests).GetMethod(nameof(Target)), postfix: postfix);
			harmony.Patch(typeof(Box<>).GetMethod(nameof(Box<int>.Read)), postfix: postfix);
			for (var round = 0; round < 20; round++) { Exercise(101); Thread.Sleep(50); }
			if (Target<decimal>(5) != 106 || Box<decimal>.Read(5) != 106)
				throw new InvalidOperationException("Late generic instantiation lost its patch");
		}
		finally { harmony.UnpatchAll(harmony.Id); }
		Exercise(1);
		Thread.Sleep(200);
		Exercise(1);
		Console.WriteLine("hot-generic-definitions=PASS; 60-million-patched-calls; generic-method-and-class; warmed-and-late; unpatch");
	}
}
