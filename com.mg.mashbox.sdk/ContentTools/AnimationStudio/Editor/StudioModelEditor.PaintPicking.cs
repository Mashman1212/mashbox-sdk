using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        // A cached screen-space broad phase keeps a paint stroke from scanning and
        // projecting every component for every interpolated pointer sample.
        internal sealed class PaintBoundsIndex
        {
            internal struct Entry { internal Vector2 Min, Max; internal int Component; }
            private struct Node { internal Vector2 Min, Max; internal int Start, Count, Left, Right; }
            private readonly Entry[] entries;
            private readonly Node[] nodes;
            private int used;
            private readonly int root;
            private static readonly IComparer<Entry> ByX = Comparer<Entry>.Create((a,b) => (a.Min.x+a.Max.x).CompareTo(b.Min.x+b.Max.x));
            private static readonly IComparer<Entry> ByY = Comparer<Entry>.Create((a,b) => (a.Min.y+a.Max.y).CompareTo(b.Min.y+b.Max.y));
            internal PaintBoundsIndex(Entry[] values)
            { entries = values; nodes = new Node[values.Length*2]; root = values.Length == 0 ? -1 : Build(0, values.Length); }
            private int Build(int start, int count)
            {
                int id = used++; var min = entries[start].Min; var max = entries[start].Max;
                for (int i = start+1; i < start+count; i++) { min = Vector2.Min(min,entries[i].Min); max = Vector2.Max(max,entries[i].Max); }
                var node = new Node { Min = min, Max = max, Start = start, Count = count };
                if (count > 8)
                {
                    Array.Sort(entries,start,count,max.x-min.x >= max.y-min.y ? ByX : ByY);
                    int half = count/2; node.Count = 0; node.Left = Build(start,half); node.Right = Build(start+half,count-half);
                }
                nodes[id] = node; return id;
            }
            internal void Query(Vector2 point, List<int> results)
            { results.Clear(); if (root >= 0) Query(root,point,results); results.Sort(); }
            private static bool Overlaps(Vector2 min, Vector2 max, Vector2 p) => min.x <= p.x+ComponentPickRadius && max.x >= p.x-ComponentPickRadius && min.y <= p.y+ComponentPickRadius && max.y >= p.y-ComponentPickRadius;
            private void Query(int id, Vector2 point, List<int> results)
            {
                var node = nodes[id]; if (!Overlaps(node.Min,node.Max,point)) return;
                if (node.Count == 0) { Query(node.Left,point,results); Query(node.Right,point,results); return; }
                for (int i = node.Start; i < node.Start+node.Count; i++)
                    if (Overlaps(entries[i].Min,entries[i].Max,point)) results.Add(entries[i].Component);
            }
        }
        private sealed class PaintPickFrame
        {
            internal Mesh Mesh; internal ComponentMode Mode; internal int Version;
            internal Matrix4x4 View, Projection, Transform;
            internal Vector2 Origin, Size; internal float Dpi;
            internal Vector3[] Vertices; internal PaintBoundsIndex Bounds;
            internal Vector2 LastPoint; internal int LastHit; internal bool LastValid, Xray;
        }
        private static Vector2 PaintWorldToGUI(Camera camera, Vector3 world)
        {
            var pixel = camera.WorldToScreenPoint(world);
            return new Vector2(pixel.x, camera.pixelHeight - pixel.y) / EditorGUIUtility.pixelsPerPoint;
        }
        internal static Ray PaintPointerRay(Camera camera, Vector2 point) => camera.ScreenPointToRay(
            new Vector3(point.x * EditorGUIUtility.pixelsPerPoint, camera.pixelHeight - point.y * EditorGUIUtility.pixelsPerPoint, 0));
        private bool PaintOccluded(SceneView view, Vector3 world, ref OcclusionMesh[] occluders)
        {
            var ray = view.camera.ViewportPointToRay(view.camera.WorldToViewportPoint(world));
            if (occluders == null) occluders = CaptureOccluders();
            return Occluded(ray, Vector3.Dot(world-ray.origin,ray.direction)-.001f, occluders);
        }
#if MASHBOX_STUDIO_TESTS
        internal int ReferencePaintHit(SceneView view, Vector2 pointer)
        { OcclusionMesh[] occluders = null; return FindComponentAt(view,pointer,ActiveMesh.vertices,ref occluders,null,PaintPointerRay(view.camera,pointer)); }
#endif
        private PaintPickFrame paintPickIndex;
        private readonly List<int> paintPickCandidates = new List<int>();
#if MASHBOX_STUDIO_TESTS
        internal int PaintPickIndexBuilds { get; private set; }
        internal int PaintPickCandidateCount => paintPickCandidates.Count;
#endif
        private int FindSelectionPaintComponent(SceneView view, Vector2 pointer)
        { OcclusionMesh[] occluders = null; return FindSelectionPaintComponent(view,pointer,ref occluders); }
        private int FindSelectionPaintComponent(SceneView view, Vector2 pointer, ref OcclusionMesh[] occluders)
        {
            if (!ActiveMesh || mode == ComponentMode.Object || Active < 0 || !Transforms[Active].gameObject.activeInHierarchy) return -1;
            var camera = view.camera; var matrix = Transforms[Active].localToWorldMatrix;
            var origin = view is MashBoxStudioViewport studio ? studio.CanvasInputOrigin : Vector2.zero; var size = new Vector2(camera.pixelWidth,camera.pixelHeight);
            int version = EditorUtility.GetDirtyCount(ActiveMesh); var frame = paintPickIndex;
            if (frame == null || frame.Mesh != ActiveMesh || frame.Mode != mode || frame.Version != version ||
                frame.View != camera.worldToCameraMatrix || frame.Projection != camera.projectionMatrix || frame.Transform != matrix ||
                frame.Origin != origin || frame.Size != size || frame.Dpi != EditorGUIUtility.pixelsPerPoint)
            {
                var vertices = selectionPaintBefore != null ? selectionPaintVertices : ActiveMesh.vertices;
                var screen = new Vector2[vertices.Length]; var near = new bool[vertices.Length];
                for (int v = 0; v < vertices.Length; v++)
                {
                    var world = matrix.MultiplyPoint3x4(vertices[v]); screen[v] = PaintWorldToGUI(camera, world);
                    near[v] = camera.WorldToViewportPoint(world).z <= camera.nearClipPlane || float.IsNaN(screen[v].sqrMagnitude) || float.IsInfinity(screen[v].sqrMagnitude);
                }
                var edges = Edges(); var faces = mode == ComponentMode.Face ? Faces() : null;
                var entries = new PaintBoundsIndex.Entry[ComponentCount()];
                for (int component = 0; component < entries.Length; component++)
                {
                    var min = new Vector2(float.MaxValue,float.MaxValue); var max = new Vector2(float.MinValue,float.MinValue); bool broad = false;
                    void Add(int vertex) { min = Vector2.Min(min,screen[vertex]); max = Vector2.Max(max,screen[vertex]); broad |= near[vertex]; }
                    if (mode == ComponentMode.Vertex) Add(component);
                    else if (mode == ComponentMode.Edge) { Add(edges[component].a); Add(edges[component].b); }
                    else foreach (int vertex in faces[component].Vertices) Add(vertex);
                    if (broad) { min = Vector2.one * -1e20f; max = Vector2.one * 1e20f; }
                    entries[component] = new PaintBoundsIndex.Entry { Min = min, Max = max, Component = component };
                }
                paintPickIndex = frame = new PaintPickFrame { Mesh = ActiveMesh, Mode = mode, Version = version, View = camera.worldToCameraMatrix,
                    Projection = camera.projectionMatrix, Transform = matrix, Origin = origin, Size = size, Dpi = EditorGUIUtility.pixelsPerPoint,
                    Vertices = vertices, Bounds = new PaintBoundsIndex(entries) };
#if MASHBOX_STUDIO_TESTS
                PaintPickIndexBuilds++;
#endif
            }
            if (frame.LastValid && frame.LastPoint == pointer && frame.Xray == xray) return frame.LastHit;
            frame.Bounds.Query(pointer,paintPickCandidates);
            frame.LastHit = FindComponentAt(view,pointer,frame.Vertices,ref occluders,paintPickCandidates,PaintPointerRay(camera,pointer));
            frame.LastPoint = pointer; frame.Xray = xray; frame.LastValid = true;
            return frame.LastHit;
        }
    }
}
