using HarmonyLib;
using RimWorld;
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace HarmonyMod;

[HarmonyPatch(typeof(VersionControl), nameof(VersionControl.DrawInfoInCorner))]
static class VersionControl_DrawInfoInCorner_Patch
{
    public static void Postfix()
    {
        var str = $"Harmony v{Main.modVersion}";
        Text.Font = GameFont.Small;
        GUI.color = Color.white.ToTransparent(0.5f);
        var size = Text.CalcSize(str);
        var rect = new Rect(10f, 58f, size.x, size.y);
        Widgets.Label(rect, str);
        GUI.color = Color.white;
        if (Mouse.IsOver(rect))
        {
            var tipSignal = new TipSignal(Main.versionTooltip);
            TooltipHandler.TipRegion(rect, tipSignal);
            Widgets.DrawHighlight(rect);
        }

        if (Main.loadingError != null)
        {
            Find.WindowStack.Add(new Dialog_MessageBox(Main.loadingError, "OK"));
            Main.loadingError = null;
        }
    }
}

[HarmonyPatch]
static class StackTrace_ToString_Patch
{
    static MethodBase TargetMethod() => AccessTools.GetDeclaredMethods(typeof(StackTrace)).Single(method =>
        method.Name == nameof(StackTrace.ToString) && method.ReturnType == typeof(string) && method.GetParameters().Length == 1);

    public static void Postfix(StackTrace __instance, ref string __result)
    {
        if (Settings.noStacktraceEnhancing)
            return;

        try
        {
            __result = ExceptionTools.EnhanceStackTrace(__instance, __result);
        }
        catch (Exception)
        {
            // Keep the runtime's original trace if enhancement fails.
        }
    }
}

[HarmonyPatch(typeof(Log), nameof(Log.ResetMessageCount))]
static class Log_ResetMessageCount_Patch
{
    public static void Postfix() => ExceptionTools.seenStacktraces.Clear();
}
