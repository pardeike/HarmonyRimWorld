using HarmonyLib;
using RimWorld;
using System;
using System.Reflection;
using UnityEngine;
using Verse;

namespace HarmonyMod
{
	public class HarmonyMain : Mod
	{
		public static Version harmonyVersion = default;
		public static string modVersion = ((AssemblyFileVersionAttribute)Attribute.GetCustomAttribute(
			 Assembly.GetExecutingAssembly(),
			 typeof(AssemblyFileVersionAttribute), false)
		).Version;

		public HarmonyMain(ModContentPack content) : base(content)
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
			GUI.color = new Color(1f, 1f, 1f, 0.5f);
			var rect = new Rect(10f, 58f, 330f, 20f);
			Widgets.Label(rect, $"Harmony: Lib v{HarmonyMain.harmonyVersion}, Mod v{HarmonyMain.modVersion}");
			GUI.color = Color.white;
		}
	}
}