using HarmonyLib;
using RimWorld;
using System;
using UnityEngine;
using Verse;

namespace HarmonyMod
{
	[StaticConstructorOnStartup]
	public class HarmonyMain(ModContentPack content) : Mod(content)
	{
		public static Version harmonyVersion = default;

		static HarmonyMain()
		{
			_ = Harmony.VersionInfo(out harmonyVersion);
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
			Text.Font = GameFont.Small;
			GUI.color = new Color(1f, 0.2f, 0.2f);
			var rect = new Rect(10f, 58f, 330f, 20f);
			Widgets.Label(rect, $"Harmony {HarmonyMain.harmonyVersion}");
			GUI.color = Color.white;
		}
	}
}