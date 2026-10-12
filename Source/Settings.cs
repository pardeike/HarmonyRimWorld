using LudeonTK;
using Verse;

namespace HarmonyMod;

public class Settings : ModSettings
{
	// Preserve the saved setting name: the cache now stores reference IDs only,
	// never permission to suppress a trace that may not have reached any log.
	[TweakValue("Harmony")] public static bool noStacktraceCaching;
	[TweakValue("Harmony")] public static bool noStacktraceEnhancing;

#pragma warning disable IDE1006
	static void noStacktraceCaching_Changed() => Main.settings.Write();
	static void noStacktraceEnhancing_Changed() => Main.settings.Write();
#pragma warning restore IDE1006

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Values.Look(ref noStacktraceCaching, "noStacktraceCaching", false);
		Scribe_Values.Look(ref noStacktraceEnhancing, "noStacktraceEnhancing", false);
	}
}
