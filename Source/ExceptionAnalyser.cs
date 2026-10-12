using HarmonyLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;

namespace HarmonyMod;

static class ExceptionTools
{
	internal static readonly ConcurrentDictionary<string, string> seenStacktraces = new();
	const int MaxSeenStacktraces = 4096;

	internal static string EnhanceStackTrace(StackTrace trace, string original)
	{
		// Preserve .NET's formatting, including async and rethrow boundaries.
		var sb = new StringBuilder(original);
		var visited = new HashSet<MethodBase>();
		foreach (var frame in trace.GetFrames() ?? [])
		{
			var method = Harmony.GetOriginalMethodFromStackframe(frame);
			if (method == null || !visited.Add(method))
				continue;

			var patches = Harmony.GetPatchInfo(method);
			if (patches == null)
				continue;

			AppendPatches(sb, method, patches.Transpilers, "TRANSPILER");
			AppendPatches(sb, method, patches.Prefixes, "PREFIX");
			AppendPatches(sb, method, patches.Postfixes, "POSTFIX");
			AppendPatches(sb, method, patches.Finalizers, "FINALIZER");
			AppendPatches(sb, method, patches.InnerPrefixes, "INNER PREFIX");
			AppendPatches(sb, method, patches.InnerPostfixes, "INNER POSTFIX");
			AppendPatches(sb, method, patches.InnerFinalizers, "INNER FINALIZER");
		}

		var result = sb.ToString();
		if (Settings.noStacktraceCaching || string.IsNullOrEmpty(result))
			return result;

		if (seenStacktraces.Count > MaxSeenStacktraces)
			seenStacktraces.Clear();
		var reference = seenStacktraces.GetOrAdd(result, text => $"[Ref {ComputeStableHash(text):X8}]");
		// Formatting does not prove that a trace was logged. Every occurrence
		// must be self-contained, even if an earlier caller discarded its text.
		return $"{reference}\n{result}";
	}

	static int ComputeStableHash(string text)
	{
		unchecked
		{
			uint hash = 2166136261;
			foreach (var character in text)
				hash = (hash ^ character) * 16777619;
			return (int)hash;
		}
	}

	static void AppendPatches(StringBuilder sb, MethodBase original, IEnumerable<Patch> patches, string kind)
	{
		var fixes = patches.ToArray();
		if (fixes.Length == 0)
			return;
		foreach (var method in PatchProcessor.GetSortedPatchMethods(original, fixes).Distinct())
		{
			var parameters = string.Join(", ", method.GetParameters().Select(parameter => $"{parameter.ParameterType.Name} {parameter.Name}"));
			foreach (var patch in fixes.Where(patch => patch.GetMethod(original) == method))
				sb.Append($"\n    - {kind} {patch.owner}: {original.DeclaringType?.FullName}.{original.Name} <- {method.ReturnType.Name} {method.DeclaringType?.FullName}:{method.Name}({parameters})");
		}
	}
}
