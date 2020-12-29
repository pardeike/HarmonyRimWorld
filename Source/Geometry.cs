using UnityEngine;
using Verse;

namespace HarmonyMod
{
	static class Geometry
	{
		static readonly GUIContent tmpTextGUIContent = new GUIContent();

		internal static Vector2 Size(this string text)
		{
			tmpTextGUIContent.text = text;
			return Text.CurFontStyle.CalcSize(tmpTextGUIContent);
		}

		internal static float Height(this string text, float width)
		{
			tmpTextGUIContent.text = text;
			return Text.CurFontStyle.CalcHeight(tmpTextGUIContent, width);
		}

		internal static Rect Moved(this Rect r, float dx = 0, float dy = 0)
		{
			r.x += dx;
			r.y += dy;
			return r;
		}

		internal static Rect WithSize(this Rect r, float width = 0, float height = 0)
		{
			if (width != 0) r.width = width;
			if (height != 0) r.height = height;
			return r;
		}

		internal static Rect Inset(this Rect r, float xMin = 0, float yMin = 0, float xMax = 0, float yMax = 0)
		{
			r.xMin += xMin;
			r.yMin += yMin;
			r.xMax -= xMax;
			r.yMax -= yMax;
			return r;
		}

		internal static Rect Center(this Rect r, float width = 0, float height = 0)
		{
			if (width != 0)
			{
				var d = (r.width - width) / 2; ;
				r.xMin += d;
				r.xMax -= d;
			}
			if (height != 0)
			{
				var d = (r.height - height) / 2;
				r.yMin += d;
				r.yMax -= d;
			}
			return r;
		}

		internal static Rect Left(this Rect r, float width)
		{
			r.width = width;
			return r;
		}

		internal static Rect Top(this Rect r, float height)
		{
			r.height = height;
			return r;
		}

		internal static Rect Right(this Rect r, float width)
		{
			r.xMin += r.width - width;
			return r;
		}

		internal static Rect Bottom(this Rect r, float height)
		{
			r.yMin += r.height - height;
			return r;
		}
	}
}
