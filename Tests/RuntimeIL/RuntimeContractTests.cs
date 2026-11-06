using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyMod;
using WaylithClr;

internal static class RuntimeContractTests
{
	static readonly ManualResetEventSlim entered = new(false), release = new(false);
	[MethodImpl(MethodImplOptions.NoInlining)] public static int Target() => 7;
	public static int Block() { entered.Set(); release.Wait(); return 23; }
	static MethodInfo Method(string name) => typeof(RuntimeContractTests).GetMethod(name);
	static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
	static byte[] CallBody(int token, bool nullArgument = false)
	{
		var body = new byte[nullArgument ? 8 : 7];
		body[0] = (byte)(((body.Length - 1) << 2) | 2);
		var index = 1;
		if (nullArgument) body[index++] = 0x14;
		body[index++] = 0x28;
		BitConverter.GetBytes(token).CopyTo(body, index);
		body[^1] = 0x2a;
		return body;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	static WeakReference InstallCollectibleSignature(MethodInfo original)
	{
		var dependency = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("SignatureDependency"), AssemblyBuilderAccess.RunAndCollect);
		var dependencyType = dependency.DefineDynamicModule("dependency").DefineType("PrivateDependency", TypeAttributes.Public).CreateType();
		var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("CollectibleSignatureCallback"), AssemblyBuilderAccess.RunAndCollect);
		var type = assembly.DefineDynamicModule("callback").DefineType("SignatureCallback", TypeAttributes.Public);
		var builder = type.DefineMethod("Echo", MethodAttributes.Public | MethodAttributes.Static, typeof(int), [dependencyType]);
		var il = builder.GetILGenerator(); il.Emit(OpCodes.Ldc_I4, 13); il.Emit(OpCodes.Ret);
		var method = type.CreateType().GetMethod("Echo");
		var token = MethodBodyUpdater.GetTokenFor(original.Module, method);
		MethodBodyUpdater.ReplaceMethodBody(original, CallBody(token, true));
		MethodBodyUpdater.PrepareMethodBody(original);
		return new WeakReference(method.DeclaringType);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	static WeakReference InstallDynamic(MethodInfo original)
	{
		var dynamic = new DynamicMethod("NativeTokenCallback", typeof(int), Type.EmptyTypes, typeof(RuntimeContractTests).Module, true);
		var il = dynamic.GetILGenerator(); il.Emit(OpCodes.Ldc_I4, 19); il.Emit(OpCodes.Ret);
		var token = MethodBodyUpdater.GetTokenFor(original.Module, dynamic);
		MethodBodyUpdater.ReplaceMethodBody(original, CallBody(token));
		MethodBodyUpdater.PrepareMethodBody(original);
		return new WeakReference(dynamic);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	static WeakReference InstallKeptBody(MethodInfo original)
	{
		var sentinel = new object();
		var token = MethodBodyUpdater.GetTokenFor(original.Module, Method(nameof(Block)));
		MethodBodyUpdater.ReplaceMethodBody(original, CallBody(token), keepAlive: sentinel);
		MethodBodyUpdater.PrepareMethodBody(original);
		return new WeakReference(sentinel);
	}

	static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }

	internal static void Run()
	{
		var original = Method(nameof(Target));
		var stringTokens = new System.Collections.Generic.HashSet<int>();
		var instantiationTokens = new System.Collections.Generic.HashSet<int>();
		for (var index = 0; index < 20; index++)
		{
			stringTokens.Add(MethodBodyUpdater.GetTokenFor(original.Module, new string("Repeated runtime literal".ToCharArray())));
			instantiationTokens.Add(MethodBodyUpdater.GetTokenForInstantiation(original.Module, typeof(System.Collections.Generic.List<>)));
		}
		Console.WriteLine($"module-token-deduplication: equal-strings={stringTokens.Count}/20; identical-instantiations={instantiationTokens.Count}/20");
		var baseline = RuntimeILBody.Read(original).Serialize();
		Check(Target() == 7, "Warm default");
		try
		{
			var collectible = InstallCollectibleSignature(original);
			for (var index = 0; index < 5; index++) { Collect(); Check(Target() == 13, "Imported signature dependency must remain callable after GC"); }
			Console.WriteLine("collectible-cross-assembly-signature=PASS; callback-type-alive=" + collectible.IsAlive);
			MethodBodyUpdater.ReplaceMethodBody(original, baseline);
			Collect();
			Console.WriteLine("collectible-callback-type-alive-after-revert=" + collectible.IsAlive);

			var dynamic = InstallDynamic(original);
			for (var index = 0; index < 5; index++) { Collect(); Check(Target() == 19, "DynamicMethod token must keep its callback callable"); }
			Check(dynamic.IsAlive, "Documented module-lifetime DynamicMethod retention");
			Console.WriteLine("direct-dynamic-token-lifetime=PASS");

			var kept = InstallKeptBody(original);
			var result = 0;
			var thread = new Thread(() => result = Target());
			thread.Start();
			Check(entered.Wait(5000), "Old frame entered");
			try
			{
				MethodBodyUpdater.ReplaceMethodBody(original, baseline);
				Collect();
				Check(kept.IsAlive && Target() == 7, "Retired body keeps its explicit lifetime object while an old frame runs");
			}
			finally { release.Set(); Check(thread.Join(5000), "Old frame returned"); }
			Check(result == 23, "Old frame result");
			for (var index = 0; index < 5 && kept.IsAlive; index++) Collect();
			Console.WriteLine("keepAlive-old-frame=PASS; released-after-return=" + !kept.IsAlive);
		}
		finally { release.Set(); MethodBodyUpdater.ReplaceMethodBody(original, baseline); }
	}
}
