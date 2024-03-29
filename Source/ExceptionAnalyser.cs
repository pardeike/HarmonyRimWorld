using HarmonyLib;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace HarmonyMod
{
	static class ExceptionTools
	{
		static readonly AccessTools.FieldRef<StackTrace, StackTrace[]> captured_traces_ref = AccessTools.FieldRefAccess<StackTrace, StackTrace[]>("captured_traces");
		static readonly AccessTools.FieldRef<StackFrame, string> internalMethodName_ref = AccessTools.FieldRefAccess<StackFrame, string>("internalMethodName");
		static readonly AccessTools.FieldRef<StackFrame, long> methodAddress_ref = AccessTools.FieldRefAccess<StackFrame, long>("methodAddress");

		delegate void GetFullNameForStackTrace(StackTrace instance, StringBuilder sb, MethodBase mi);
		static readonly MethodInfo m_GetFullNameForStackTrace = AccessTools.Method(typeof(StackTrace), "GetFullNameForStackTrace");
		static readonly GetFullNameForStackTrace getFullNameForStackTrace = AccessTools.MethodDelegate<GetFullNameForStackTrace>(m_GetFullNameForStackTrace);

		delegate uint GetMethodIndex(StackFrame instance);
		static readonly MethodInfo m_GetMethodIndex = AccessTools.Method(typeof(StackFrame), "GetMethodIndex");
		static readonly GetMethodIndex getMethodIndex = AccessTools.MethodDelegate<GetMethodIndex>(m_GetMethodIndex);

		delegate string GetSecureFileName(StackFrame instance);
		static readonly MethodInfo m_GetSecureFileName = AccessTools.Method(typeof(StackFrame), "GetSecureFileName");
		static readonly GetSecureFileName getSecureFileName = AccessTools.MethodDelegate<GetSecureFileName>(m_GetSecureFileName);

		delegate string GetAotId();
		static readonly MethodInfo m_GetAotId = AccessTools.Method(typeof(StackTrace), "GetAotId");
		static readonly GetAotId getAotId = AccessTools.MethodDelegate<GetAotId>(m_GetAotId);

		public static readonly ConcurrentBag<int> seenStacktraces = [];

		public static string ExtractHarmonyEnhancedStackTrace()
		{
			try
			{
				return ExtractHarmonyEnhancedStackTrace(new StackTrace(3, true));
			}
			catch (System.Exception)
			{
				return StackTraceUtility.ExtractStackTrace();
			}
		}

		public static string ExtractHarmonyEnhancedStackTrace(StackTrace trace)
		{
			var sb = new StringBuilder();
			if (captured_traces_ref(trace) != null)
			{
				var traces = captured_traces_ref(trace);
				for (int i = 0; i < traces.Length; i++)
				{
					if (sb.AddHarmonyFrames(traces[i]))
						_ = sb.Append("\n--- End of stack trace from previous location where exception was thrown ---\n");
				}
			}
			_ = sb.AddHarmonyFrames(trace);
			var stacktrace = sb.ToString();
			var hash = stacktrace.GetHashCode();
			if (seenStacktraces.Contains(hash))
				return $"[Ref {hash:X}] Duplicate stacktrace, see ref for original";
			seenStacktraces.Add(hash);
			return $"[Ref {hash:X}]\n{stacktrace}";
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
				_ = sb.Append(" at ");

				var method = Harmony.GetOriginalMethodFromStackframe(frame);
				if (method == null)
				{
					var internalMethodName = internalMethodName_ref(frame);
					if (internalMethodName != null)
						_ = sb.Append(internalMethodName);
					else
						_ = sb.AppendFormat("<0x{0:x5} + 0x{1:x5}> <unknown method>", methodAddress_ref(frame), frame.GetNativeOffset());
				}
				else
				{
					getFullNameForStackTrace(trace, sb, method);
					if (frame.GetILOffset() == -1)
					{
						_ = sb.AppendFormat(" <0x{0:x5} + 0x{1:x5}>", methodAddress_ref(frame), frame.GetNativeOffset());
						if (getMethodIndex(frame) != 16777215U)
							_ = sb.AppendFormat(" {0}", getMethodIndex(frame));
					}
					else
						_ = sb.AppendFormat(" [0x{0:x5}]", frame.GetILOffset());

					var fileName = getSecureFileName(frame);
					if (fileName[0] == '<')
					{
						var versionId = method.Module.ModuleVersionId.ToString("N");
						var aotId = getAotId();
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
			foreach (var patch in PatchProcessor.GetSortedPatchMethods(method, fixes.ToArray()))
			{
				var owner = fixes.First(p => p.PatchMethod == patch).owner;
				var parameters = patch.GetParameters().Join(p => $"{p.ParameterType.Name} {p.Name}");
				_ = sb.AppendFormat("\n     - {0} {1}: {2} {3}:{4}({5})", name, owner, patch.ReturnType.Name, patch.DeclaringType.FullName, patch.Name, parameters);
			}
		}
	}
}
