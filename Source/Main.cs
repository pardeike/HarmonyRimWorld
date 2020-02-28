using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace HarmonyMod
{
	public class HarmonyMain : Mod
	{
		public HarmonyMain(ModContentPack content) : base(content)
		{
			var harmony = new Harmony("net.pardeike.rimworld.lib.harmony");
			harmony.PatchAll();
		}
	}

	[HarmonyPatch(typeof(VersionControl))]
	[HarmonyPatch(nameof(VersionControl.DrawInfoInCorner))]
	public static class VersionControl_DrawInfoInCorner_Patch
	{
		public static void Postfix()
		{
			_ = Harmony.VersionInfo(out var version);
			var harmonyVersion = $"Harmony v{version}";

			Text.Font = GameFont.Small;
			GUI.color = new Color(1f, 1f, 1f, 0.5f);
			var rect = new Rect(10f, 59f, 330f, 20f);
			Widgets.Label(rect, harmonyVersion);
			GUI.color = Color.white;
		}
	}
}