using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace HarmonyMod
{
	public class Tab : MainButtonDef
	{
		internal static Tab instance = new Tab();
		internal static readonly AccessTools.FieldRef<MainButtonsRoot, List<MainButtonDef>> allButtonsInOrderRef = AccessTools.FieldRefAccess<MainButtonsRoot, List<MainButtonDef>>("allButtonsInOrder");

		public Tab()
		{
			tabWindowClass = typeof(ExceptionInspector);
			defName = "harmony";
			description = "Harmony";
			order = -99999;
			validWithoutMap = true;
			minimized = true;
			iconPath = "HarmonyTab";
		}

		internal static void AddHarmony()
		{
			var root = Find.UIRoot;

			if (root is UIRoot_Play rootPlay)
			{
				var allTabs = allButtonsInOrderRef(rootPlay.mainButtonsRoot);
				if (allTabs.Contains(instance) == false)
				{
					allTabs.Insert(0, instance);
					Tools.PlayErrorSound(Assets.error);
				}
				return;
			}

			if (root is UIRoot_Entry rootEntry)
			{
				if (Find.WindowStack.IsOpen<ExceptionInspector>() == false)
				{
					Find.WindowStack.Add(new ExceptionInspector
					{
						def = instance,
						customPosition = new Vector2(10, 10)
					});
					Tools.PlayErrorSound(Assets.error);
				}
			}
		}
	}
}
