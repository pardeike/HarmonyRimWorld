using System.IO;
using System.Linq;
using UnityEngine;
using Verse;

namespace HarmonyMod
{
	[StaticConstructorOnStartup]
	static class Assets
	{
		internal static readonly Texture2D cancel = ContentFinder<Texture2D>.Get("UI/Designators/Cancel", true);

		internal static readonly Texture2D[] debugToggle = LoadTextures("Debug0", "Debug1");
		internal static readonly Texture2D restart = LoadTexture("Restart");
		internal static readonly Texture2D exception = LoadTexture("Exception");
		internal static readonly Texture2D location = LoadTexture("Location");
		internal static readonly Texture2D mod = LoadTexture("Mod");
		internal static readonly Texture2D bubble = LoadTexture("Bubble");
		internal static readonly Texture2D copy = LoadTexture("Copy");

		internal static readonly Texture2D modMenu = LoadTexture("ModMenu");
		internal static readonly Texture2D enableMenu = LoadTexture("EnableMenu");
		internal static readonly Texture2D disableMenu = LoadTexture("DisableMenu");
		internal static readonly Texture2D unpatchMenu = LoadTexture("UnpatchMenu");

		internal static readonly AudioClip error = LoadSound("Error");
		internal static readonly Texture2D highlight = SolidColorMaterials.NewSolidColorTexture(new Color(1, 1, 1, 0.05f));

		static Texture2D LoadTexture(string path, bool makeReadonly = true)
		{
			var fullPath = Path.Combine(Tools.GetModRootDirectory(), "Textures", $"{path}.png");
			var data = File.ReadAllBytes(fullPath);
			if (data == null || data.Length == 0) return new Texture2D(1, 1);
			var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
			if (tex.LoadImage(data) == false) return new Texture2D(1, 1);
			tex.Compress(true);
			tex.wrapMode = TextureWrapMode.Clamp;
			tex.filterMode = FilterMode.Trilinear;
			tex.Apply(true, makeReadonly);
			return tex;
		}

		static Texture2D[] LoadTextures(params string[] paths)
		{
			return paths.Select(path => LoadTexture(path)).ToArray();
		}

		static AudioClip LoadSound(string path)
		{
			var fullPath = Path.Combine(Tools.GetModRootDirectory(), "Sounds", $"{path}.wav");
			return RuntimeAudioClipLoader.Manager.Load(fullPath);
		}
	}
}
