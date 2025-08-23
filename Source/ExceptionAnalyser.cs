using HarmonyLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using Verse;

namespace HarmonyMod;

static class ExceptionTools
{
	static T GetDelegate<T>(Type type, string name) where T : Delegate
	{
		try
		{
			var method = AccessTools.Method(type, name) ?? throw new MissingMethodException(type.FullName, name);
			return AccessTools.MethodDelegate<T>(method);
		}
		catch (Exception ex)
		{
			Log.Error($"Harmony: Failed to create delegate for {type}.{name}: {ex.Message}");
			return null;
		}
	}

	static AccessTools.FieldRef<TInst, TField> SafeFieldRefAccess<TInst, TField>(string fieldName)
	{
		try
		{
			return AccessTools.FieldRefAccess<TInst, TField>(fieldName);
		}
		catch (Exception ex)
		{
			Log.Warning($"Harmony: Unable to access field {typeof(TInst).FullName}.{fieldName}: {ex.Message}");
			return null;
		}
	}

	static readonly AccessTools.FieldRef<StackTrace, StackTrace[]> captured_traces = SafeFieldRefAccess<StackTrace, StackTrace[]>("captured_traces");
	static readonly AccessTools.FieldRef<StackFrame, string> internalMethodName = SafeFieldRefAccess<StackFrame, string>("internalMethodName");
	static readonly AccessTools.FieldRef<StackFrame, long> methodAddress = SafeFieldRefAccess<StackFrame, long>("methodAddress");

	delegate void GetFullNameForStackTraceDelegate(StackTrace instance, StringBuilder sb, MethodBase mi, bool needsNewLine, out bool skipped, out bool isAsync);
	static readonly GetFullNameForStackTraceDelegate GetFullNameForStackTrace = GetDelegate<GetFullNameForStackTraceDelegate>(typeof(StackTrace), "GetFullNameForStackTrace");

	delegate uint GetMethodIndexDelegate(StackFrame instance);
	static readonly GetMethodIndexDelegate GetMethodIndex = GetDelegate<GetMethodIndexDelegate>(typeof(StackFrame), "GetMethodIndex");

	delegate string GetSecureFileNameDelegate(StackFrame instance);
	static readonly GetSecureFileNameDelegate GetSecureFileName = GetDelegate<GetSecureFileNameDelegate>(typeof(StackFrame), "GetSecureFileName");

	delegate string GetAotIdDelegate();
	static readonly GetAotIdDelegate GetAotId = GetDelegate<GetAotIdDelegate>(typeof(StackTrace), "GetAotId");

	internal static readonly ConcurrentDictionary<int, int> seenStacktraces = new();
	const int MaxSeenStacktraces = 4096;

	static int ComputeStableHash(string s)
	{
		unchecked
		{
			const uint fnvOffset = 2166136261;
			const uint fnvPrime = 16777619;
			uint h = fnvOffset;
			for (int i = 0; i < s.Length; i++)
			{
				h ^= s[i];
				h *= fnvPrime;
			}
			return (int)h;
		}
	}

	internal static string ExtractHarmonyEnhancedStackTrace(StackTrace trace, bool forceRefresh, out int hash)
	{
		var sb = new StringBuilder();

		var traces = captured_traces != null ? captured_traces(trace) : null;
		if (traces != null)
		{
			for (int i = 0; i < traces.Length; i++)
				if (sb.AddHarmonyFrames(traces[i]))
					_ = sb.Append("\n--- End of stack trace from previous location where exception was thrown ---\n");
		}

		_ = sb.AddHarmonyFrames(trace);
		var stacktrace = sb.ToString();

		hash = ComputeStableHash(stacktrace);

		if (HarmonyMain.noStacktraceCaching)
			return stacktrace;

		var hashRef = $"[Ref {hash:X}]";
		if (forceRefresh)
			return $"{hashRef}\n{stacktrace}";

		if (seenStacktraces.Count > MaxSeenStacktraces)
		{
			Log.Warning("Harmony: Clearing stacktrace cache to preserve memory");
			seenStacktraces.Clear();
		}

		var count = seenStacktraces.AddOrUpdate(hash, 1, (k, v) => v + 1);
		if (count > 1) return $"{hashRef} Duplicate stacktrace, see ref for original";
		return $"{hashRef}\n{stacktrace}";
	}

	static bool AddHarmonyFrames(this StringBuilder sb, StackTrace trace)
	{
		if (trace == null || trace.FrameCount == 0)
			return false;

		for (var i = 0; i < trace.FrameCount; i++)
		{
			try
			{
				var frame = trace.GetFrame(i);
				if (i > 0)
					_ = sb.Append('\n');
				//_ = sb.Append(" at ");

				var method = Harmony.GetOriginalMethodFromStackframe(frame);

				if (method == null)
				{
					var internalName = internalMethodName != null ? internalMethodName(frame) : null;
					if (!string.IsNullOrEmpty(internalName))
						_ = sb.Append(internalName);
					else
					{
						var m = frame?.GetMethod();
						if (m != null)
							AppendMethodSignature(sb, m);
						else
							_ = sb.AppendFormat("<0x{0:x5} + 0x{1:x5}> <unknown method>", SafeMethodAddress(frame), frame.GetNativeOffset()); // CHANGED: guarded address
					}
				}
				else
				{
					if (GetFullNameForStackTrace != null)
						GetFullNameForStackTrace(trace, sb, method, false, out _, out _);
					else
						AppendMethodSignature(sb, method); // CHANGED: fallback if delegate missing

					if (frame.GetILOffset() == -1)
					{
						_ = sb.AppendFormat(" <0x{0:x5} + 0x{1:x5}>", SafeMethodAddress(frame), frame.GetNativeOffset()); // CHANGED: guarded address

						if (GetMethodIndex != null)
						{
							var idx = GetMethodIndex(frame);
							if (idx != 16777215U)
								_ = sb.AppendFormat(" {0}", idx);
						}
					}
					else
						_ = sb.AppendFormat(" [0x{0:x5}]", frame.GetILOffset());

					var fileName = GetSecureFileName != null ? GetSecureFileName(frame) : frame.GetFileName();
					if (string.IsNullOrEmpty(fileName))
						fileName = "<unknown>";

					if (fileName.Length > 0 && fileName[0] == '<')
					{
						var versionId = method.Module.ModuleVersionId.ToString("N");
						var aotId = GetAotId != null ? GetAotId() : null;
						if (frame.GetILOffset() != -1 || aotId == null)
							fileName = $"<{versionId}>";
						else
							fileName = $"<{versionId}#{aotId}>";
					}
					_ = sb.AppendFormat(" in {0}:{1} ", fileName, frame.GetFileLineNumber());

					var patches = Harmony.GetPatchInfo(method);
					if (patches != null)
					{
						sb.AppendPatch(method, patches.Transpilers, "TRANSPILER");
						sb.AppendPatch(method, patches.Prefixes, "PREFIX");
						sb.AppendPatch(method, patches.Postfixes, "POSTFIX");
						sb.AppendPatch(method, patches.Finalizers, "FINALIZER");
					}
				}
			}
			catch (Exception ex) // never throw while formatting a stack trace
			{
				_ = sb.Append($"[Harmony: failed to render frame: {ex.GetType().Name}]");
			}
		}
		return true;
	}

	static long SafeMethodAddress(StackFrame frame) => methodAddress != null ? methodAddress(frame) : 0L;

	static void AppendMethodSignature(StringBuilder sb, MethodBase mi)
	{
		var dt = mi.DeclaringType;
		if (dt != null)
			_ = sb.Append(dt.FullName).Append('.');
		_ = sb.Append(mi.Name);

		if (mi is MethodInfo mInfo && mInfo.IsGenericMethod)
		{
			var args = mInfo.GetGenericArguments();
			if (args.Length > 0)
				_ = sb.Append('<').Append(string.Join(", ", args.Select(a => a.Name))).Append('>');
		}

		var ps = mi.GetParameters();
		_ = sb.Append('(')
			  .Append(string.Join(", ", ps.Select(p => $"{p.ParameterType.Name} {p.Name}")))
			  .Append(')');
	}

	static void AppendPatch(this StringBuilder sb, MethodBase method, IEnumerable<Patch> fixes, string name)
	{
		if (fixes == null)
			return;

		var fixList = fixes as IList<Patch> ?? [.. fixes];
		if (fixList.Count == 0)
			return;

		var ownerByMethod = new Dictionary<MethodInfo, string>(fixList.Count);
		foreach (var f in fixList)
		{
			if (f?.PatchMethod != null && !ownerByMethod.ContainsKey(f.PatchMethod))
				ownerByMethod[f.PatchMethod] = f.owner ?? "<unknown>";
		}

		var sorted = PatchProcessor.GetSortedPatchMethods(method, [.. fixList]);
		foreach (var patchMethod in sorted)
		{
			ownerByMethod.TryGetValue(patchMethod, out var owner);
			owner ??= patchMethod.DeclaringType?.Assembly?.GetName().Name ?? "<unknown>";

			var parameters = patchMethod.GetParameters();
			var paramList = string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));

			_ = sb.AppendFormat("\n    - {0} {1}: {2} {3}:{4}({5})",
				name,
				owner,
				patchMethod.ReturnType?.Name ?? "void",
				patchMethod.DeclaringType?.FullName ?? "<UnknownType>",
				patchMethod.Name,
				paramList);
		}
	}
}
