using HarmonyLib;
using RimWorld;
using System;
using System.Diagnostics;
using UnityEngine;
using Verse;

namespace HarmonyMod;

[HarmonyPatch(typeof(VersionControl), nameof(VersionControl.DrawInfoInCorner))]
static class VersionControl_DrawInfoInCorner_Patch
{
	public static void Postfix()
	{
		var str = $"Harmony v{HarmonyMain.loadedHarmonyVersion}";
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

		if (HarmonyMain.loadingError != null)
		{
			Find.WindowStack.Add(new Dialog_MessageBox(HarmonyMain.loadingError, "OK"));
			HarmonyMain.loadingError = null;
		}
	}
}

[HarmonyPatch(typeof(Environment), "GetStackTrace")]
static class Environment_GetStackTrace_Patch
{
	public static bool Prefix(Exception e, bool needFileInfo, ref string __result)
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
	public static void Postfix() => ExceptionTools.seenStacktraces.Clear();
}