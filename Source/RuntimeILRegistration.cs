#if HARMONY_RUNTIME_IL_HOST
using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace HarmonyMod;

internal static class RuntimeILRegistration
{
	static Func<string> status;
#pragma warning disable CA2255
	[ModuleInitializer]
#pragma warning restore CA2255
	internal static void Initialize()
	{
		if (typeof(object).Assembly.GetType("WaylithClr.MethodBodyTransformer") is null) return;
		// Escape hatch for players and testers: every patch then installs through Harmony's ordinary detour.
		if (Environment.GetEnvironmentVariable("HARMONY_DETOURS") == "1" || Array.IndexOf(Environment.GetCommandLineArgs(), "-harmony-detours") >= 0)
		{
			status = () => "disabled by -harmony-detours or HARMONY_DETOURS=1; patches use the detour";
			Console.WriteLine("Harmony Mod direct method-body provider disabled by switch");
			return;
		}
		var path = Path.Combine(Path.GetDirectoryName(typeof(RuntimeILRegistration).Assembly.Location), "HarmonyMod.RuntimeIL.dll");
		var provider = Assembly.LoadFrom(path).GetType("HarmonyMod.RuntimeILProvider", true);
		provider.GetMethod("Register", [typeof(Action<string>)]).CreateDelegate<Action<Action<string>>>()(message => Verse.Log.Error(message));
		status = provider.GetMethod("GetStatus", Type.EmptyTypes).CreateDelegate<Func<string>>();
		Console.WriteLine("Harmony Mod experimental direct method-body provider registered");
	}

	internal static void Report() { if (status is not null) Verse.Log.Message("Harmony runtime-body provider: " + status()); }
}
#endif
