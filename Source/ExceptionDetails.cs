using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Verse;

namespace HarmonyMod
{
	class ExceptionDetails
	{
		internal class Mod
		{
			internal string version;
			internal ModMetaData meta;
			internal List<MethodBase> methods;

			internal Mod(MethodBase method, ModMetaData metaData = null)
			{
				var declaringType = method.DeclaringType;
				version = declaringType.Assembly.GetName().Version.ToString();
				meta = metaData;
				if (metaData == null)
				{
					var assembly = declaringType.Assembly;
					meta = Mods.GetModMetaData(assembly);
				}
				this.methods = new List<MethodBase> { method };
			}

			internal void OpenSteam()
			{
				if (meta.Source == ContentSource.SteamWorkshop)
					SteamUtility.OpenWorkshopPage(meta.GetPublishedFileId());
			}

			internal void OpenURL()
			{
				if (meta.Url.NullOrEmpty() == false)
					_ = Process.Start(meta.Url);
			}

			internal bool IsUnpatched()
			{
				return Mods.UnpatchedMods.Contains(meta.PackageId);
			}

			internal class Comparer : IEqualityComparer<Mod>
			{
				public bool Equals(Mod x, Mod y) => x.meta.Name == y.meta.Name;
				public int GetHashCode(Mod obj) => obj.meta.Name.GetHashCode();
			}
		}

		internal class Patch
		{
			internal string method;
			internal List<Mod> mods;

			internal Patch(MethodBase method, IEnumerable<MethodInfo> patches)
			{
				var declaringType = method.DeclaringType;
				this.method = method.ShortDescription();
				mods = patches.Select(patch => new Mod(patch)).Distinct(new Mod.Comparer()).ToList();
			}
		}

		internal string exceptionMessage;
		internal string topMethod;
		internal List<Mod> mods;

		internal Mod GetMod(MethodBase method)
		{
			return mods.FirstOrDefault(mod => mod.methods.Contains(method));
		}
	}
}
