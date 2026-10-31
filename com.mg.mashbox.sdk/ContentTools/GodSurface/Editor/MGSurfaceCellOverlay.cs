using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MG.GodSurface.Editor
{
    [InitializeOnLoad]
    static class MGSurfaceCellOverlay
    {
        static readonly Unity.Profiling.ProfilerMarker overlayMarker = new Unity.Profiling.ProfilerMarker("MGSurface.CellOverlay");
        static readonly Color[] colours = { new Color(.15f,.8f,1,.85f), new Color(1,.65f,.1f,.95f), new Color(1,.2f,.2f,.95f), new Color(.55f,.55f,.55f,.5f) };
        static readonly Dictionary<int, Dictionary<MGSurfaceWorld, Lines>> views = new Dictionary<int, Dictionary<MGSurfaceWorld, Lines>>();
        sealed class Lines
        {
            int version = -1;
            MGSurfaceWorld.CellDebugState[] previous = Array.Empty<MGSurfaceWorld.CellDebugState>();
            readonly List<Vector3>[] staging = { new List<Vector3>(), new List<Vector3>(), new List<Vector3>(), new List<Vector3>() };
            readonly Vector3[][] vertices = { Array.Empty<Vector3>(), Array.Empty<Vector3>(), Array.Empty<Vector3>(), Array.Empty<Vector3>() };
            public void Draw(MGSurfaceWorld world, MGSurfaceWorld.CellDebugState[] states)
            {
                bool changed = version != world.CellBoundsVersion || previous.Length != states.Length;
                if (!changed) for (int i = 0; i < states.Length; i++) if (previous[i] != states[i]) { changed = true; break; }
                if (changed)
                {
                    if (previous.Length != states.Length) previous = new MGSurfaceWorld.CellDebugState[states.Length];
                    Array.Copy(states, previous, states.Length); version = world.CellBoundsVersion;
                    foreach (var list in staging) list.Clear();
                    for (int i = 0; i < states.Length; i++)
                    {
                        int group = states[i] == MGSurfaceWorld.CellDebugState.Visible ? 0
                            : states[i] == MGSurfaceWorld.CellDebugState.PartiallyDistanceCulled ? 1
                            : states[i] == MGSurfaceWorld.CellDebugState.DistanceCulled ? 2 : 3;
                        AddBox(staging[group], world.CellBounds[i]);
                    }
                    for (int group = 0; group < 4; group++)
                    {
                        if (vertices[group].Length != staging[group].Count) vertices[group] = new Vector3[staging[group].Count];
                        staging[group].CopyTo(vertices[group]);
                    }
                }
                for (int group = 0; group < 4; group++) if (vertices[group].Length > 0)
                { Handles.color = colours[group]; Handles.DrawLines(vertices[group]); }
            }
            static void AddBox(List<Vector3> lines, Bounds b)
            {
                // Emit every edge once: 8 corners, connecting only each corner's unset axis bits.
                for (int corner = 0; corner < 8; corner++)
                {
                    var a = new Vector3((corner & 1) == 0 ? b.min.x : b.max.x, (corner & 2) == 0 ? b.min.y : b.max.y, (corner & 4) == 0 ? b.min.z : b.max.z);
                    if ((corner & 1) == 0) { lines.Add(a); lines.Add(new Vector3(b.max.x, a.y, a.z)); }
                    if ((corner & 2) == 0) { lines.Add(a); lines.Add(new Vector3(a.x, b.max.y, a.z)); }
                    if ((corner & 4) == 0) { lines.Add(a); lines.Add(new Vector3(a.x, a.y, b.max.z)); }
                }
            }
        }
        static MGSurfaceCellOverlay()
        {
            SceneView.beforeSceneGui += Prepare; SceneView.duringSceneGui += Draw;
            EditorApplication.hierarchyChanged += () => views.Clear();
        }
        static void Prepare(SceneView view)
        {
            if (Event.current.type != EventType.Repaint || !MashBoxSDK.Maps.MBGameplayGizmoVisibility.PropCellsVisible) return;
            using var sample = overlayMarker.Auto();
            var worlds = MGSurfaceWorld.ActiveEditorWorlds;
            for (int i = 0; i < worlds.Count; i++)
                if (worlds[i] && worlds[i].drawCellBounds) worlds[i].GetCellDebugStates(view.camera.GetInstanceID());
        }
        static void Draw(SceneView view)
        {
            if (Event.current.type != EventType.Repaint || !MashBoxSDK.Maps.MBGameplayGizmoVisibility.PropCellsVisible) return;
            using var sample = overlayMarker.Auto();
            var oldDepth = Handles.zTest;
            try
            {
                Handles.zTest = CompareFunction.LessEqual;
                using (new Handles.DrawingScope(Color.white, Matrix4x4.identity))
                {
                    var worlds = MGSurfaceWorld.ActiveEditorWorlds; int id = view.camera.GetInstanceID();
                    for (int i = 0; i < worlds.Count; i++)
                    {
                        var world = worlds[i]; if (!world || !world.drawCellBounds) continue;
                        if (!views.TryGetValue(id, out var cache))
                        {
                            if (views.Count >= 16) views.Clear();
                            cache = new Dictionary<MGSurfaceWorld, Lines>(); views.Add(id, cache);
                        }
                        if (!cache.TryGetValue(world, out var lines)) { lines = new Lines(); cache.Add(world, lines); }
                        lines.Draw(world, world.GetCellDebugStates(id));
                    }
                }
            }
            finally { Handles.zTest = oldDepth; }
        }
    }
}