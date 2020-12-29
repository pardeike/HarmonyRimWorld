using HarmonyLib;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Verse;

namespace HarmonyMod
{
	static class PatchPersistence
	{
		static readonly string configurationPath = Path.Combine(GenFilePaths.ConfigFolderPath, "HarmonyModPatches.txt");
		static readonly Assembly assembly = typeof(Pawn).Assembly;
		static readonly Module module = assembly.Modules.First();
		static readonly string version = assembly.GetName().Version.ToString();

		internal static IEnumerable<MethodBase> Methods
		{
			get
			{
				if (File.Exists(configurationPath) == false)
					return new List<MethodBase>();
				var lines = File.ReadAllLines(configurationPath, Encoding.UTF8);
				if (lines.Length != 2 || lines[0] != version)
					return new List<MethodBase>();
				return lines[1].Split(',').Select(num => int.Parse(num)).Select(token => module.ResolveMethod(token)).ToList();
			}
			set
			{
				File.WriteAllLines(configurationPath, new[]
				{
					version,
					value.Join(method => method.MetadataToken.ToString(), ",")
				}, Encoding.UTF8);
			}
		}
	}
}
