using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace HarmonyMod
{
	static class Columns
	{
		static readonly Dictionary<string, string> truncatedModNamesCache = new Dictionary<string, string>();

		internal const float spacing = 8;
		const float iconDim = 16;
		const float sliceDim = 8;

		internal static void Row(float viewWidth, ref float viewHeight, float spaceBefore, float spaceAfter, float[] widths, Action rowClick, params ColumnDrawer[] columns)
		{
			viewHeight += spaceBefore;
			widths = widths.Select(w => w != 0 ? w : viewWidth - columns.Length * spacing - widths.Sum()).ToArray();
			var rowRect = new Rect(0, viewHeight, 0, 0);
			rowRect.height = Mathf.Max(columns.Select((column, i) => column(rowRect.WithSize(width: widths[i])).dim).ToArray());
			var rect = rowRect;
			rowRect.width = viewWidth;
			if (rowClick != null)
			{
				if (Mouse.IsOver(rowRect))
					GUI.DrawTexture(rowRect, Assets.highlight);
				if (Widgets.ButtonInvisible(rowRect))
					rowClick();
			}
			columns.Iterate((column, i) =>
			{
				rect.x = widths.Take(i).Sum() + spacing * i;
				rect.width = widths[i];
				if (rect.width > 0)
					column(rect).draw();
			});
			viewHeight += rect.height;
			viewHeight += spaceAfter;
		}

		internal static float MaxWidth(this IEnumerable<ColumnDrawer> drawers)
		{
			if (drawers.Any())
				return drawers.Max(drawer => drawer(Rect.zero).dim);
			return 0;
		}

		//

		internal static ColumnDrawer Column(this string str)
		{
			return rect =>
			{
				if (rect.width == 0)
					return new ColumnInfo() { dim = str.Size().x };
				if (rect.height == 0)
					return new ColumnInfo() { dim = str.Height(rect.width) };
				return new ColumnInfo() { draw = () => Widgets.Label(rect, str) };
			};
		}

		internal static ColumnDrawer Column(this List<string> strings)
		{
			return rect =>
			{
				if (rect.width == 0)
					return new ColumnInfo() { dim = strings.Join(null, "\n").Size().x };
				if (rect.height == 0)
				{
					var h = strings.Select(str => str.Height(rect.width)).Sum();
					return new ColumnInfo() { dim = h + spacing / 2 * (strings.Count - 1) };
				}
				return new ColumnInfo()
				{
					draw = () =>
					{
						foreach (var str in strings)
						{
							Widgets.Label(rect, str);
							rect = rect.Moved(dy: str.Height(rect.width) + spacing / 2);
						}
					}
				};
			};
		}

		internal static ColumnDrawer IconColumn(this string str, Texture2D icon)
		{
			return rect =>
			{
				if (rect.width == 0)
					return new ColumnInfo() { dim = str.Size().x + iconDim + spacing };
				if (rect.height == 0)
				{
					var strHeight = str.Height(rect.width - (iconDim + spacing));
					return new ColumnInfo() { dim = Mathf.Max(iconDim, strHeight) };
				}
				return new ColumnInfo()
				{
					draw = () =>
					{
						GUI.DrawTexture(rect.WithSize(iconDim, iconDim), icon);
						Widgets.Label(rect.Inset(xMin: iconDim + spacing), str);
					}
				};
			};
		}

		internal static ColumnDrawer ExceptionColumn(this ExceptionInfo info, Func<string> exception, int count)
		{
			var countLen = count.ToString().Size().x - 1;
			var extraLen = iconDim + spacing + sliceDim + countLen + sliceDim + spacing;
			var message = info.GetReport().exceptionMessage;
			return rect =>
			{
				if (rect.width == 0)
					return new ColumnInfo() { dim = message.Size().x + extraLen };
				if (rect.height == 0)
				{
					var strHeight = message.Height(rect.width - extraLen - iconDim);
					return new ColumnInfo() { dim = Mathf.Max(iconDim, strHeight) };
				}
				return new ColumnInfo()
				{
					draw = () =>
					{
						var r = rect.WithSize(iconDim, iconDim);
						var tex = Mouse.IsOver(r) ? Widgets.CheckboxOffTex : Assets.exception;
						Tools.Button(tex, r, "CommandHideZoneLabel", false, () => info.Remove());

						var r2 = r.Moved(dx: r.width + spacing).WithSize(width: sliceDim + countLen + sliceDim);
						if (Mouse.IsOver(r2))
						{
							GUI.DrawTexture(r2.Center(width: r2.height), Assets.cancel);
							Tools.Button(null, r2, "Ignore", false, () => info.Ban());
						}
						else
						{
							r = r.Moved(dx: r.width + spacing).WithSize(width: sliceDim);
							Widgets.DrawTexturePart(r, new Rect(0, 0, 0.25f, 1), Assets.bubble);
							r = r.Moved(dx: r.width).WithSize(width: countLen);
							Widgets.DrawTexturePart(r, new Rect(0.25f, 0, 0.5f, 1), Assets.bubble);
							r = r.Moved(dx: r.width).WithSize(width: sliceDim);
							Widgets.DrawTexturePart(r, new Rect(0.75f, 0, 0.25f, 1), Assets.bubble);
							Widgets.Label(rect.Inset(xMin: iconDim + spacing + sliceDim), count.ToString());
						}

						GUI.color = Color.yellow;
						Widgets.Label(rect.Inset(xMin: extraLen, xMax: iconDim), message);
						GUI.color = Color.white;

						r = rect.Right(iconDim).Center(height: iconDim);
						Tools.Button(Assets.copy, r, "Copy", true, () =>
						{
							GUIUtility.systemCopyBuffer = exception().ToString();
							SoundDefOf.Tick_Low.PlayOneShotOnCamera(null);
						});
					}
				};
			};
		}

		internal static ColumnDrawer Column(this int index, bool unpatched)
		{
			return rect =>
			{
				var str = $"{index}.";
				if (rect.width == 0)
					return new ColumnInfo() { dim = str.Size().x - 1 + spacing };
				if (rect.height == 0)
					return new ColumnInfo() { dim = str.Size().y };
				return new ColumnInfo()
				{
					draw = () =>
					{
						var f = 2f / (index + 1);
						Widgets.DrawBoxSolid(rect, new Color(f / 2, (1 - f) / 2, 0));
						if (unpatched)
							GUI.DrawTexture(rect.Bottom(rect.width).Center(width: 16, height: 16), Assets.unpatchMenu);
						var oldColor = GUI.color;
						GUI.color = Color.white;
						Text.Anchor = TextAnchor.UpperLeft;
						Widgets.Label(rect.Inset(xMin: spacing / 2), str);
						GUI.color = oldColor;
					}
				};
			};
		}

		internal static ColumnDrawer Column(this ExceptionDetails.Mod mod)
		{
			var name = mod.meta.Name.Truncate(200, truncatedModNamesCache);
			return rect =>
			{
				var author = "© " + mod.meta.Author;
				if (rect.width == 0)
				{
					var w1 = name.Size().x + iconDim + spacing;
					var w2 = author.Size().x;
					return new ColumnInfo() { dim = Mathf.Max(w1, w2) };
				}
				if (rect.height == 0)
				{
					var h1 = Mathf.Max(iconDim, name.Height(rect.width - (iconDim + spacing)));
					var h2 = author.Height(rect.width);
					return new ColumnInfo() { dim = h1 + 2 + h2 };
				}
				return new ColumnInfo()
				{
					draw = () =>
					{
						GUI.color = mod.meta.Active ? Color.white : new Color(1, 1, 1, 0.4f);
						GUI.DrawTexture(rect.WithSize(iconDim, iconDim), Assets.mod);
						Widgets.Label(rect.Inset(xMin: iconDim + spacing), name);
						var h = Mathf.Max(iconDim, name.Height(rect.width - (iconDim + spacing)));
						Widgets.Label(rect.Inset(yMin: h + 2), author);
						GUI.color = Color.white;
					}
				};
			};
		}

		internal static void Line(float viewWidth, ref float viewHeight, float thickness = 1)
		{
			Widgets.DrawLine(new Vector2(-spacing, viewHeight), new Vector2(viewWidth + spacing + 16, viewHeight), Color.gray, thickness);
			viewHeight += thickness;
		}
	}
}
