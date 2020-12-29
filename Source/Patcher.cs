using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Verse;

namespace HarmonyMod
{
	static class Patcher
	{
		internal static bool patchesApplied = false;

		internal static void Apply(Harmony harmony)
		{
			_ = new PatchClassProcessor(harmony, typeof(AddHarmonyTabWhenNecessary)).Patch();
			_ = new PatchClassProcessor(harmony, typeof(RememberHarmonyIDs)).Patch();
			_ = new PatchClassProcessor(harmony, typeof(RunloopExceptionHandler)).Patch();
			patchesApplied = true;
		}
	}

	// adds exception handlers
	//
	[HarmonyPatch]
	static class RunloopExceptionHandler
	{
		static readonly MethodInfo Handle = SymbolExtensions.GetMethodInfo(() => ExceptionState.Handle(null));

		internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var list = instructions.ToList();
			var idx = 0;
			var found = false;
			while (true)
			{
				idx = list.FindIndex(idx, IsCatchException);
				if (idx < 0) break;
				var code = list[idx];
				if (code.opcode == OpCodes.Pop)
				{
					idx += 1;
					continue;
				}
				list.InsertRange(idx, new[]
				{
						new CodeInstruction(OpCodes.Dup) { blocks = code.blocks, labels = code.labels },
						new CodeInstruction(OpCodes.Call, Handle),
					});
				code.labels = new List<Label>();
				code.blocks = new List<ExceptionBlock>();
				found = true;
				idx += 3;
			}
			if (found == false) return null;
			return list.AsEnumerable();
		}

		internal static IEnumerable<MethodBase> TargetMethods()
		{
			var methods = PatchPersistence.Methods;
			if (methods.Any()) return methods;
			methods = typeof(Pawn).Assembly.GetTypes()
				.Where(t => t.IsGenericType == false && (t.FullName.StartsWith("Verse.") || t.FullName.StartsWith("RimWorld.") || t.FullName.StartsWith("RuntimeAudioClipLoader.")))
				.SelectMany(t => AccessTools.GetDeclaredMethods(t))
				.Where(m => m.IsGenericMethod == false && HasCatch(m));
			PatchPersistence.Methods = methods;
			return methods;
		}

		static bool IsCatchException(CodeInstruction code)
		{
			return code.blocks.Any(block => block.blockType == ExceptionBlockType.BeginCatchBlock && block.catchType == typeof(Exception));
		}

		static bool HasCatch(MethodBase method)
		{
			try
			{
				var result = PatchProcessor.GetOriginalInstructions(method).Any(IsCatchException);
				return result;
			}
			catch
			{
				return false;
			}
		}
	}

	// draw the harmony lib version at the start screen
	//
	[HarmonyPatch(typeof(VersionControl))]
	[HarmonyPatch(nameof(VersionControl.DrawInfoInCorner))]
	static class ShowHarmonyVersionOnMainScreen
	{
		static readonly string modVersion = (Attribute.GetCustomAttribute(
			 Assembly.GetExecutingAssembly(),
			 typeof(AssemblyFileVersionAttribute),
			 false) as AssemblyFileVersionAttribute)?.Version ?? "???";

		static readonly Version harmonyVersion;
		static ShowHarmonyVersionOnMainScreen()
		{
			_ = Harmony.VersionInfo(out harmonyVersion);
		}

		internal static void Postfix()
		{
			Tools.DrawInfoSection(harmonyVersion?.ToString() ?? "???", modVersion);
		}
	}

	// adds harmony tab to the leftmost position on the bottom of the screen
	// in case there is something to report
	//
	[HarmonyPatch(typeof(UIRoot_Play))]
	[HarmonyPatch(nameof(UIRoot_Play.Init))]
	static class AddHarmonyTabWhenNecessary
	{
		[HarmonyPriority(int.MinValue)]
		internal static void Postfix()
		{
			if (ExceptionState.Exceptions.Count > 0)
				Tab.AddHarmony();
		}
	}

	// adds harmony tab to the leftmost position on the bottom of the screen
	// in case there is something to report
	//
	[HarmonyPatch(typeof(Harmony))]
	[HarmonyPatch(MethodType.Constructor)]
	[HarmonyPatch(new[] { typeof(string) })]
	static class RememberHarmonyIDs
	{
		internal static void Postfix(string id)
		{
			var assembly = new StackTrace(false).GetFrames()
				.Where(f => f.GetMethod().IsConstructor)
				.Select(f => f.GetMethod().DeclaringType.Assembly)
				.FirstOrDefault();
			if (assembly != null)
				Mods.ActiveHarmonyIDs[assembly] = id;
		}
	}
}
