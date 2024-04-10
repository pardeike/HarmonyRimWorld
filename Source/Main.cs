using HarmonyLib;
using LudeonTK;
using RimWorld;
using System;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;
using Verse;

namespace HarmonyMod
{
	[StaticConstructorOnStartup]
	public class HarmonyMain(ModContentPack content) : Mod(content)
	{
		[TweakValue("Harmony")]
		public static bool noStacktraceCaching;
		[TweakValue("Harmony")]
		public static bool noStacktraceEnhancing;

		public static Version harmonyVersion = default;

		public static string modVersion = ((AssemblyFileVersionAttribute)Attribute.GetCustomAttribute(
			 Assembly.GetExecutingAssembly(),
			 typeof(AssemblyFileVersionAttribute), false)
		).Version;

		static HarmonyMain()
		{
			_ = Harmony.VersionInfo(out harmonyVersion);
			var harmony = new Harmony("net.pardeike.rimworld.lib.harmony");
			harmony.PatchAll();
		}
	}

	[HarmonyPatch(typeof(VersionControl), nameof(VersionControl.DrawInfoInCorner))]
	static class VersionControl_DrawInfoInCorner_Patch
	{
		static void Postfix()
		{
			var str = $"Harmony v{HarmonyMain.harmonyVersion}";
			Text.Font = GameFont.Small;
			GUI.color = Color.white.ToTransparent(0.5f);
			var size = Text.CalcSize(str);
			var rect = new Rect(10f, 58f, size.x, size.y);
			Widgets.Label(rect, str);
			GUI.color = Color.white;
			if (Mouse.IsOver(rect))
			{
				var tipSignal = new TipSignal($"Harmony Mod v{HarmonyMain.modVersion}");
				TooltipHandler.TipRegion(rect, tipSignal);
				Widgets.DrawHighlight(rect);
			}
		}
	}

	[HarmonyPatch(typeof(Environment), "GetStackTrace")]
	static class Environment_GetStackTrace_Patch
	{
		static bool Prefix(Exception e, bool needFileInfo, ref string __result)
		{
			if (HarmonyMain.noStacktraceEnhancing)
				return true;

			try
			{
				var stackTrace = e == null ? new StackTrace(needFileInfo) : new StackTrace(e, needFileInfo);
				__result = ExceptionTools.ExtractHarmonyEnhancedStackTrace(stackTrace, false, out _);
				return false;
			}
			catch (Exception)
			{
				return true;
			}
		}
	}

	[HarmonyPatch(typeof(Log), nameof(Log.ResetMessageCount))]
	static class Log_ResetMessageCount_Patch
	{
		static void Postfix() => ExceptionTools.seenStacktraces.Clear();
	}
}