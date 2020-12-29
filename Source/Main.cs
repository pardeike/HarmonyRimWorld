using HarmonyLib;
using Verse;

namespace HarmonyMod
{
	[StaticConstructorOnStartup]
	class HarmonyMain : Mod
	{
		static HarmonyMain()
		{
			var harmony = new Harmony("net.pardeike.rimworld.lib.harmony");
			_ = new PatchClassProcessor(harmony, typeof(ShowHarmonyVersionOnMainScreen)).Patch();

			ExceptionState.Load();
			if (ExceptionState.configuration.debugging)
				Patcher.Apply(harmony);
		}

		public HarmonyMain(ModContentPack content) : base(content) { }
	}
}
