namespace HarmonyMod;

// Loaded only after the wrapper detects the optional API in the running CoreLib.
public static class RuntimeILProvider
{
	public static void Register(System.Action<string> reportError) => RuntimeILPatching.Register(_ => true, reportError: reportError);
	public static string GetStatus() => RuntimeILPatching.Status;
	public static void Rebuild(System.Reflection.MethodBase method) => RuntimeILPatching.Rebase(method, force: true);
}
