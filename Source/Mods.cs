using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.Sound;

namespace HarmonyMod
{
	struct ModInfo
	{
		internal MethodBase method;
		internal ModMetaData metaData;
	}

	static class Mods
	{
		internal static readonly Dictionary<Assembly, string> ActiveHarmonyIDs = new Dictionary<Assembly, string>();
		internal static readonly HashSet<string> UnpatchedMods = new HashSet<string>();
		static readonly Dictionary<Assembly, ModMetaData> MetaDataCache = new Dictionary<Assembly, ModMetaData>();

		internal static ModMetaData GetMetadataIfMod(MethodBase method)
		{
			if (method == null) return null;
			var assembly = method.DeclaringType?.Assembly;
			if (assembly == null) return null;
			var references = assembly.GetReferencedAssemblies();
			if (references.Any(assemblyName => assemblyName.Name == Tools.RimworldAssemblyName) == false) return null;
			var metaData = GetModMetaData(assembly);
			if (metaData == null || metaData.IsCoreMod) return null;
			return metaData;
		}

		internal static ModMetaData GetModMetaData(Assembly assembly)
		{
			if (MetaDataCache.TryGetValue(assembly, out var metaData) == false)
			{
				var contentPack = LoadedModManager.RunningMods
					.FirstOrDefault(m => m.IsCoreMod == false && m.assemblies.loadedAssemblies.Contains(assembly));
				if (contentPack != null)
					metaData = ModsConfig.ActiveModsInLoadOrder.FirstOrDefault(meta => meta.PackageId == contentPack.PackageId);
				MetaDataCache.Add(assembly, metaData);
			}
			return metaData;
		}

		internal static Patches FindPatches(MethodBase method)
		{
			if (method is MethodInfo replacement)
			{
				var original = Harmony.GetOriginalMethod(replacement);
				if (original == null) return null;
				return Harmony.GetPatchInfo(original);
			}
			return null;
		}

		internal static void ToggleActive(this ExceptionDetails.Mod mod)
		{
			mod.meta.Active = !mod.meta.Active;
			ModsConfig.Save();
		}

		internal static void Unpatch(this ExceptionDetails.Mod mod)
		{
			var contentPack = LoadedModManager.RunningMods.FirstOrDefault(cp => cp.PackageId == mod.meta.PackageId);
			contentPack.assemblies.loadedAssemblies
				.Where(assembly => assembly.GetName().Name != "0Harmony")
				.Select(assembly => ActiveHarmonyIDs.GetValueSafe(assembly))
				.OfType<string>().Distinct()
				.Do(id => { new Harmony(id).UnpatchAll(id); SoundDefOf.TabOpen.PlayOneShotOnCamera(null); });
			_ = UnpatchedMods.Add(mod.meta.PackageId);
		}

		internal static IEnumerable<ModInfo> GetPrefixes(Patches info)
		{
			if (info == null) return new List<ModInfo>().AsEnumerable();
			return AddMetadata(info.Prefixes.OrderBy(t => t.priority).Select(t => t.PatchMethod));
		}

		internal static IEnumerable<ModInfo> GetPostfixes(Patches info)
		{
			if (info == null) return new List<ModInfo>().AsEnumerable();
			return AddMetadata(info.Postfixes.OrderBy(t => t.priority).Select(t => t.PatchMethod));
		}

		internal static IEnumerable<ModInfo> GetTranspilers(Patches info)
		{
			if (info == null) return new List<ModInfo>().AsEnumerable();
			return AddMetadata(info.Transpilers.OrderBy(t => t.priority).Select(t => t.PatchMethod));
		}

		internal static IEnumerable<ModInfo> GetFinalizers(Patches info)
		{
			if (info == null) return new List<ModInfo>().AsEnumerable();
			return AddMetadata(info.Finalizers.OrderBy(t => t.priority).Select(t => t.PatchMethod));
		}

		static IEnumerable<ModInfo> AddMetadata(IEnumerable<MethodInfo> methods)
		{
			return methods.Select(method =>
			{
				var assembly = method.DeclaringType.Assembly;
				var metaData = GetModMetaData(assembly);
				return new ModInfo { method = method, metaData = metaData };
			});
		}
	}
}
