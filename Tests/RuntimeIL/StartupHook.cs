using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

public static class StartupHook
{
	public static void Initialize()
	{
		if (Environment.GetEnvironmentVariable("HARMONY_RUNTIME_BRIDGE_TEST") is not string mode) return;
		try
		{
			if (mode == "multi") MultiEngineTests.Run();
			else
			{
				if (mode == "protocol") AppDomain.CurrentDomain.SetData("Harmony.MethodBodyPatching.v1",
					 new object[] { null, new Dictionary<MethodBase, bool>(), new List<Func<MethodBase, bool>>(), new object() });
				var directory = Path.GetDirectoryName(typeof(StartupHook).Assembly.Location);
				AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(directory, "0Harmony.dll"));
				if (mode == "hot-generics") HotGenericTests.Run();
				else if (mode == "runtime-contracts") RuntimeContractTests.Run();
				else if (mode == "coexist") HarmonyPipelineTests.Run();
				else if (mode == "pipeline") PipelineTests.Run();
				else if (mode == "readbody") RuntimeBodyTests.Run();
				else if (mode == "failure") FailureTests.Run();
				else if (mode == "reentry") ReentryTests.Run();
				else if (mode == "generics") GenericTests.Run();
				else if (mode == "compat") CompatTests.Run(true);
				else if (mode == "compat-detour") CompatTests.Run(false);
				else RuntimeILTests.Run();
			}
			Console.WriteLine("PASS runtime-" + mode);
			Environment.Exit(0);
		}
		catch (Exception error) { Console.Error.WriteLine(error); Environment.Exit(1); }
	}
}
