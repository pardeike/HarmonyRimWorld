using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
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

	[HarmonyPatch(typeof(VersionControl), nameof(VersionControl.DrawInfoInCorner))]
	public static class VersionControl_DrawInfoInCorner_Patch
	{
		public static void Postfix()
		{
			Text.Font = GameFont.Small;
			GUI.color = new Color(1f, 1f, 1f, 0.5f);
			var rect = new Rect(10f, 58f, 330f, 20f);
			Widgets.Label(rect, $"Harmony v{HarmonyMain.harmonyVersion}");
			GUI.color = Color.white;
		}
	}

	[HarmonyPriority(Priority.Last)]
	public static class Log_Patch
	{
		public static IEnumerable<MethodBase> TargetMethods()
		{
			yield return SymbolExtensions.GetMethodInfo(() => Log.Message(""));
			yield return SymbolExtensions.GetMethodInfo(() => Log.Warning(""));
			yield return SymbolExtensions.GetMethodInfo(() => Log.Error(""));
		}

		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			return instructions.MethodReplacer(
				SymbolExtensions.GetMethodInfo(() => StackTraceUtility.ExtractStackTrace()),
				SymbolExtensions.GetMethodInfo(() => ExceptionTools.ExtractHarmonyEnhancedStackTrace())
			);
		}
	}

	[HarmonyPatch(typeof(Environment), "GetStackTrace")]
	public static class Environment_GetStackTrace_Patch
	{
		public static bool Prefix(Exception e, bool needFileInfo, ref string __result)
		{
			try
			{
				var stackTrace = e == null ? new StackTrace(needFileInfo) : new StackTrace(e, needFileInfo);
				__result = ExceptionTools.ExtractHarmonyEnhancedStackTrace(stackTrace);
				return false;
			}
			catch (Exception)
			{
				return true;
			}
		}
	}
}