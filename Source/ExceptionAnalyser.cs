using HarmonyLib;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using LudeonTK;
using UnityEngine;
using JetBrains.Annotations;

namespace HarmonyMod
{
	public static class ExceptionTools
	{
		[TweakValue("_Harmony")]
		[UsedImplicitly]
		public static bool DisableStackTraceCaching;

		public static readonly AccessTools.FieldRef<StackTrace, StackTrace[]> captured_traces = AccessTools.FieldRefAccess<StackTrace, StackTrace[]>("captured_traces");
		public static readonly AccessTools.FieldRef<StackFrame, string> internalMethodName = AccessTools.FieldRefAccess<StackFrame, string>("internalMethodName");
		public static readonly AccessTools.FieldRef<StackFrame, long> methodAddress = AccessTools.FieldRefAccess<StackFrame, long>("methodAddress");

		public delegate void GetFullNameForStackTraceDelegate(StackTrace instance, StringBuilder sb, MethodBase mi);
		private static readonly MethodInfo m_GetFullNameForStackTrace = AccessTools.Method(typeof(StackTrace), "GetFullNameForStackTrace");
		public static readonly GetFullNameForStackTraceDelegate GetFullNameForStackTrace = AccessTools.MethodDelegate<GetFullNameForStackTraceDelegate>(m_GetFullNameForStackTrace);

		public delegate uint GetMethodIndexDelegate(StackFrame instance);
		private static readonly MethodInfo m_GetMethodIndex = AccessTools.Method(typeof(StackFrame), "GetMethodIndex");
		public static readonly GetMethodIndexDelegate GetMethodIndex = AccessTools.MethodDelegate<GetMethodIndexDelegate>(m_GetMethodIndex);

		public delegate string GetSecureFileNameDelegate(StackFrame instance);
		private static readonly MethodInfo m_GetSecureFileName = AccessTools.Method(typeof(StackFrame), "GetSecureFileName");
		public static readonly GetSecureFileNameDelegate GetSecureFileName = AccessTools.MethodDelegate<GetSecureFileNameDelegate>(m_GetSecureFileName);

		public delegate string GetAotIdDelegate();
		private static readonly MethodInfo m_GetAotId = AccessTools.Method(typeof(StackTrace), "GetAotId");
		public static readonly GetAotIdDelegate GetAotId = AccessTools.MethodDelegate<GetAotIdDelegate>(m_GetAotId);

		public static readonly ConcurrentDictionary<int, int> seenStacktraces = [];

		public static string ExtractHarmonyEnhancedStackTrace()
		{
			try
			{
				return ExtractHarmonyEnhancedStackTrace(new StackTrace(3, true), false, out _);
			}
			catch (System.Exception)
			{
				return StackTraceUtility.ExtractStackTrace();
			}
		}

		public static string ExtractHarmonyEnhancedStackTrace(StackTrace trace, bool forceRefresh, out int hash)
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
			if (DisableStackTraceCaching)
				return stacktrace;
			var hashRef = $"[Ref {hash:X}]";
			if (forceRefresh)
				return $"{hashRef}\n{stacktrace}";
			var count = seenStacktraces.AddOrUpdate(hash, 1, (k, v) => v + 1);
			if (count > 1)
				return $"{hashRef} Duplicate stacktrace, see ref for original";
			return $"{hashRef}\n{stacktrace}";
		}

		public static bool AddHarmonyFrames(this StringBuilder sb, StackTrace trace)
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
					var internalMethodName = ExceptionTools.internalMethodName(frame);
					if (internalMethodName != null)
						_ = sb.Append(internalMethodName);
					else
						_ = sb.AppendFormat("<0x{0:x5} + 0x{1:x5}> <unknown method>", methodAddress(frame), frame.GetNativeOffset());
				}
				else
				{
					GetFullNameForStackTrace(trace, sb, method);
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

		public static void AppendPatch(this StringBuilder sb, MethodBase method, IEnumerable<Patch> fixes, string name)
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
