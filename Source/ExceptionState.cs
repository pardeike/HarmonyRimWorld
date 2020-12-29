using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Verse;

namespace HarmonyMod
{
	[Serializable]
	public class Configuration
	{
		public bool debugging;
	}

	static class ExceptionState
	{
		static readonly string configurationPath = Path.Combine(GenFilePaths.ConfigFolderPath, "HarmonyModSettings.json");
		internal static Configuration configuration = new Configuration();

		static readonly Dictionary<ExceptionInfo, int> exceptions = new Dictionary<ExceptionInfo, int>(new ExceptionInfo.Comparer());
		static readonly HashSet<ExceptionInfo> bannedExceptions = new HashSet<ExceptionInfo>(new ExceptionInfo.Comparer());
		internal static Dictionary<ExceptionInfo, int> Exceptions => exceptions;

		internal static void Handle(Exception exception)
		{
			var result = Lookup(exception);
			if (result != null && result.Item2 == 1)
				Tab.AddHarmony();
		}

		internal static void Clear()
		{
			exceptions.Clear();
		}

		internal static void Load()
		{
			try
			{
				var json = "{}";
				if (File.Exists(configurationPath))
					json = File.ReadAllText(configurationPath, Encoding.UTF8);
				JsonUtility.FromJsonOverwrite(json, configuration);
			}
			catch (Exception exception)
			{
				Log.Error($"Error loading Harmony Mod state: {exception.Message}");
				configuration = new Configuration();
			}
		}

		internal static void Save()
		{
			try
			{
				var json = JsonUtility.ToJson(configuration);
				File.WriteAllText(configurationPath, json, Encoding.UTF8);
			}
			catch (Exception exception)
			{
				Log.Error($"Error saving Harmony Mod state: {exception.Message}");
			}
		}

		static Tuple<ExceptionInfo, int> Lookup(Exception exception)
		{
			var info = new ExceptionInfo(exception);
			if (bannedExceptions.Contains(info)) return null;
			if (exceptions.TryGetValue(info, out var count) == false)
				count = 0;
			exceptions[info] = ++count;
			return new Tuple<ExceptionInfo, int>(info, count);
		}

		internal static void Remove(ExceptionInfo info)
		{
			_ = exceptions.Remove(info);
		}

		internal static void Ban(ExceptionInfo info)
		{
			_ = bannedExceptions.Add(info);
		}
	}
}
