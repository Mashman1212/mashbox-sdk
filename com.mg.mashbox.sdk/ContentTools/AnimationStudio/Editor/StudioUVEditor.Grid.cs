using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioUVEditor
    {
        private void DrawGrid(Vector2 origin)
        {
            var lower = ToUV(new Vector2(canvas.xMin, canvas.yMax));
            var upper = ToUV(new Vector2(canvas.xMax, canvas.yMin));
            var color = StudioTheme.Current.Grid;
            // Draw only visible lines. Coarsen the grid as tiles shrink to keep panning
            // unbounded without drawing thousands of subpixel lines when zoomed out.
            float spacing = Mathf.Pow(10, Mathf.Max(0, Mathf.Ceil(Mathf.Log10(12 / Unit))));
            if (Unit >= 120)
            {
                Handles.color = StudioTheme.Alpha(color, .2f);
                DrawGridLines(lower, upper, .1f, origin);
            }
            Handles.color = StudioTheme.Alpha(color, .65f);
            DrawGridLines(lower, upper, spacing, origin);
            // The UV origin remains visible outside the main texture tile.
            Handles.color = color;
            if (lower.x <= 0 && upper.x >= 0)
                Handles.DrawLine(ToScreen(new Vector2(0, lower.y)) - origin, ToScreen(new Vector2(0, upper.y)) - origin);
            if (lower.y <= 0 && upper.y >= 0)
                Handles.DrawLine(ToScreen(new Vector2(lower.x, 0)) - origin, ToScreen(new Vector2(upper.x, 0)) - origin);
            // Retain a clear reference to the original texture tile; UVs can live anywhere.
            Handles.color = StudioTheme.Alpha(StudioTheme.Current.Accent, .7f);
            var a = ToScreen(Vector2.zero) - origin;
            var b = ToScreen(Vector2.right) - origin;
            var c = ToScreen(Vector2.one) - origin;
            var d = ToScreen(Vector2.up) - origin;
            Handles.DrawLine(a, b); Handles.DrawLine(b, c); Handles.DrawLine(c, d); Handles.DrawLine(d, a);
        }

        private void DrawGridLines(Vector2 lower, Vector2 upper, float spacing, Vector2 origin)
        {
            float first = Mathf.Ceil(lower.x / spacing) * spacing;
            int count = Mathf.Clamp(Mathf.FloorToInt((upper.x - first) / spacing) + 1, 0, 2048);
            for (int i = 0; i < count; i++)
            {
                float u = first + i * spacing;
                Handles.DrawLine(ToScreen(new Vector2(u, lower.y)) - origin, ToScreen(new Vector2(u, upper.y)) - origin);
            }
            first = Mathf.Ceil(lower.y / spacing) * spacing;
            count = Mathf.Clamp(Mathf.FloorToInt((upper.y - first) / spacing) + 1, 0, 2048);
            for (int i = 0; i < count; i++)
            {
                float v = first + i * spacing;
                Handles.DrawLine(ToScreen(new Vector2(lower.x, v)) - origin, ToScreen(new Vector2(upper.x, v)) - origin);
            }
        }
    }
}
