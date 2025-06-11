using HarmonyLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using Verse;

namespace HarmonyMod
{
	static class ExceptionTools
	{
		static bool hasDelegateError = false;
		static T GetDelegate<T>(Type type, string name) where T : Delegate
		{
			try
			{
				var method = AccessTools.Method(type, name);
				return AccessTools.MethodDelegate<T>(method);
			}
			catch (Exception ex)
			{
				Log.Error($"Harmony: Failed to create delegate for {type}.{name}: {ex.Message}");
				hasDelegateError = true;
				return null;
			}
		}

		static readonly AccessTools.FieldRef<StackTrace, StackTrace[]> captured_traces = AccessTools.FieldRefAccess<StackTrace, StackTrace[]>("captured_traces");
		static readonly AccessTools.FieldRef<StackFrame, string> internalMethodName = AccessTools.FieldRefAccess<StackFrame, string>("internalMethodName");
		static readonly AccessTools.FieldRef<StackFrame, long> methodAddress = AccessTools.FieldRefAccess<StackFrame, long>("methodAddress");

		delegate void GetFullNameForStackTraceDelegate(StackTrace instance, StringBuilder sb, MethodBase mi, bool needsNewLine, out bool skipped, out bool isAsync);
		static readonly GetFullNameForStackTraceDelegate GetFullNameForStackTrace = GetDelegate<GetFullNameForStackTraceDelegate>(typeof(StackTrace), "GetFullNameForStackTrace");

		delegate uint GetMethodIndexDelegate(StackFrame instance);
		static readonly GetMethodIndexDelegate GetMethodIndex = GetDelegate<GetMethodIndexDelegate>(typeof(StackFrame), "GetMethodIndex");

		delegate string GetSecureFileNameDelegate(StackFrame instance);
		static readonly GetSecureFileNameDelegate GetSecureFileName = GetDelegate<GetSecureFileNameDelegate>(typeof(StackFrame), "GetSecureFileName");

		delegate string GetAotIdDelegate();
		static readonly GetAotIdDelegate GetAotId = GetDelegate<GetAotIdDelegate>(typeof(StackTrace), "GetAotId");

		internal static readonly ConcurrentDictionary<int, int> seenStacktraces = [];

		internal static string ExtractHarmonyEnhancedStackTrace(StackTrace trace, bool forceRefresh, out int hash)
		{
			var sb = new StringBuilder();
			var traces = captured_traces(trace);
			if (traces != null)
				for (int i = 0; i < traces.Length; i++)
					if (sb.AddHarmonyFrames(traces[i]))
						_ = sb.Append("\n--- End of stack trace from previous location where exception was thrown ---\n");
			_ = sb.AddHarmonyFrames(trace);
			var stacktrace = sb.ToString();
			hash = stacktrace.GetHashCode();
			if (HarmonyMain.noStacktraceCaching)
				return stacktrace;
			var hashRef = $"[Ref {hash:X}]";
			if (forceRefresh)
				return $"{hashRef}\n{stacktrace}";
			var count = seenStacktraces.AddOrUpdate(hash, 1, (k, v) => v + 1);
			if (count > 1)
				return $"{hashRef} Duplicate stacktrace, see ref for original";
			return $"{hashRef}\n{stacktrace}";
		}

		static bool AddHarmonyFrames(this StringBuilder sb, StackTrace trace)
		{
			if (trace.FrameCount == 0)
				return false;

			for (var i = 0; i < trace.FrameCount; i++)
			{
				var frame = trace.GetFrame(i);
				if (i > 0)
					_ = sb.Append('\n');
				//_ = sb.Append(" at ");

				var method = Harmony.GetOriginalMethodFromStackframe(frame);
				if (method == null || hasDelegateError)
				{
					var internalMethodName = ExceptionTools.internalMethodName(frame);
					if (internalMethodName != null)
						_ = sb.Append(internalMethodName);
					else
						_ = sb.AppendFormat("<0x{0:x5} + 0x{1:x5}> <unknown method>", methodAddress(frame), frame.GetNativeOffset());
				}
				else
				{
					GetFullNameForStackTrace(trace, sb, method, false, out _, out _);
					if (frame.GetILOffset() == -1)
					{
						_ = sb.AppendFormat(" <0x{0:x5} + 0x{1:x5}>", methodAddress(frame), frame.GetNativeOffset());
						if (GetMethodIndex(frame) != 16777215U)
							_ = sb.AppendFormat(" {0}", GetMethodIndex(frame));
					}
					else
						_ = sb.AppendFormat(" [0x{0:x5}]", frame.GetILOffset());

					var fileName = GetSecureFileName(frame);
					if (fileName[0] == '<')
					{
						var versionId = method.Module.ModuleVersionId.ToString("N");
						var aotId = GetAotId();
						if (frame.GetILOffset() != -1 || aotId == null)
							fileName = string.Format("<{0}>", versionId);
						else
							fileName = string.Format("<{0}#{1}>", versionId, aotId);
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
			return true;
		}

		static void AppendPatch(this StringBuilder sb, MethodBase method, IEnumerable<Patch> fixes, string name)
		{
			foreach (var patch in PatchProcessor.GetSortedPatchMethods(method, [.. fixes]))
			{
				var owner = fixes.First(p => p.PatchMethod == patch).owner;
				var parameters = patch.GetParameters().Join(p => $"{p.ParameterType.Name} {p.Name}");
				_ = sb.AppendFormat("\n    - {0} {1}: {2} {3}:{4}({5})", name, owner, patch.ReturnType.Name, patch.DeclaringType.FullName, patch.Name, parameters);
			}
		}
	}
}