using HarmonyLib;
using LudeonTK;
using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Verse;

namespace HarmonyMod;

[StaticConstructorOnStartup]
public class HarmonyMain(ModContentPack content) : Mod(content)
{
	[TweakValue("Harmony")] public static bool noStacktraceCaching;
	[TweakValue("Harmony")] public static bool noStacktraceEnhancing;

	public static Version loadedHarmonyVersion = default;
	public static string loadingError;

	public static string modVersion = ((AssemblyFileVersionAttribute)Attribute.GetCustomAttribute(
		 Assembly.GetExecutingAssembly(),
		 typeof(AssemblyFileVersionAttribute), false)
	).Version;

	static HarmonyMain()
	{
		string[] HarmonyNames = ["0Harmony", "Lib.Harmony", "HarmonyLib"];
		var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => HarmonyNames.Contains(a.GetName().Name));
		if (loaded != null)
		{
			loadedHarmonyVersion = loaded.GetName().Version ?? new Version(0, 0, 0, 0);

			var loadedPath = SafeLocation(loaded);
			var ourPath = SafeLocation(Assembly.GetExecutingAssembly());
			var expectedPath = ourPath[..(ourPath.LastIndexOfAny(['\\', '/']) + 1)] + "0Harmony.dll";

			var ourHarmonyVersion = new Version(0, 0, 0, 0);
			try
			{
				if (System.IO.File.Exists(expectedPath))
				{
					var assemblyName = AssemblyName.GetAssemblyName(expectedPath);
					ourHarmonyVersion = assemblyName.Version ?? new Version(0, 0, 0, 0);
				}
			}
			catch (Exception ex)
			{
				Log.Warning($"Could not read version of our 0Harmony.dll from disk: {ex.Message}");
			}

			if (expectedPath != loadedPath && ourHarmonyVersion > loadedHarmonyVersion)
			{
				loadingError = "HARMONY LOADING PROBLEM\n\nAnother Harmony library was loaded before the Harmony mod could.\n\n" 
					+ $"Their version: {loadedHarmonyVersion}\nOur version: {ourHarmonyVersion}\n\n"
					+ $"This means that your Harmony version is now downgraded to {loadedHarmonyVersion} regardless of what the Harmony mod provides. " 
					+ $"You need to update or remove that other loader/mod. The other Harmony was loaded from: {loadedPath}";
				if (Regex.IsMatch(loadedPath, @"data-[0-9A-F]{16}"))
					loadingError += "\n\nThe path looks like Harmony was loaded from memory and not via a file path. This often hints to preloaders like Doorstop or similar.";
				Log.Error(loadingError);
			}
		}

		try
		{
			var harmony = new Harmony("net.pardeike.rimworld.lib.harmony");
			harmony.PatchAll();
		}
		catch (Exception ex)
		{
			Log.Error($"Lib.Harmony could not be initialized: {ex.Message}");
		}
	}

	static string SafeLocation(Assembly a)
	{
		try
		{
			if (!string.IsNullOrEmpty(a.Location))
				return a.Location;
		}
		catch { }

		try
		{
			var cb = a.GetName().CodeBase;
			if (!string.IsNullOrEmpty(cb) && Uri.TryCreate(cb, UriKind.Absolute, out var uri) && uri.IsFile)
				return uri.LocalPath;
		}
		catch { }

		return string.Empty;
	}
}