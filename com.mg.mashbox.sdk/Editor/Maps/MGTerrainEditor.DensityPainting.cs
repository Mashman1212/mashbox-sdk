#if UNITY_EDITOR
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    public sealed partial class MGTerrainEditor
    {
        // R16 stores whole instances. Preserve sub-instance progress across dabs
        // and strokes, including temporary world/Multi editors, without changing
        // the runtime density-map format. Weak keys do not retain unused maps.
        static ConditionalWeakTable<Texture2D, Dictionary<int, float>> DensityPaintProgress = new();

        [InitializeOnLoadMethod]
        static void RegisterDensityPaintUndo()
        {
            Undo.undoRedoPerformed += ResetDensityPaintProgress;
        }

        internal static void ResetDensityPaintProgress()
        {
            DensityPaintProgress = new ConditionalWeakTable<Texture2D, Dictionary<int, float>>();
        }

        internal static ushort AccumulateDensityPaint(Texture2D map, int index, ushort stored, int goal, float influence)
        {
            if (influence <= 0f) return stored;
            var progress = DensityPaintProgress.GetOrCreateValue(map);
            // Resync after an external edit; Undo also clears hidden progress even
            // when the restored integer happens to equal the last written value.
            float previous = progress.TryGetValue(index, out float pending)
                && Mathf.RoundToInt(pending) == stored ? pending : stored;
            float next = Mathf.Lerp(previous, goal, Mathf.Clamp01(influence));
            ushort value = (ushort)Mathf.Clamp(Mathf.RoundToInt(next), 0, ushort.MaxValue);
            // At the displayed target there is no useful residual to retain.
            if (value == goal || next == value) progress.Remove(index);
            else progress[index] = next;
            return value;
        }
    }
}
#endif
