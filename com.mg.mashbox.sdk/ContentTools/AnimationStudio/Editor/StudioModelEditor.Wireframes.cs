using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private sealed class SelectionWireframe
        {
            internal int[] indices;
            internal Vector3[] lines;
            internal Mesh baked;
            internal readonly List<Vector3> vertices = new List<Vector3>();
        }
        private readonly Vector3[] outlineSegment = new Vector3[2];
        private Camera outlineCamera;
        private readonly Dictionary<StudioModelDocument.Node, SelectionWireframe> selectionWireframes = new Dictionary<StudioModelDocument.Node, SelectionWireframe>();
        private void ClearSelectionWireframes()
        {
            ClearComponentDrawing();
            foreach(var wire in selectionWireframes.Values) if(wire.baked) UnityEngine.Object.DestroyImmediate(wire.baked);
            selectionWireframes.Clear();
        }
        internal IEnumerable<int> SelectedWireframeNodes()
        { foreach(int i in VisibleWireframeNodes()) if(objects.Contains(i)||HasSelectedParent(i))yield return i; }
        internal IEnumerable<int> VisibleWireframeNodes()
        {
            if(!Document)yield break;
            for(int i=0;i<Transforms.Length;i++)
            {
                if(!Document.nodes[i].mesh||!Transforms[i].gameObject.activeInHierarchy)continue;
                var renderer=Transforms[i].GetComponent<Renderer>();if(renderer&&renderer.enabled)yield return i;
            }
        }
        private void DrawSceneWireframes(Camera camera)
        {
            if(Event.current.type!=EventType.Repaint)return;
            using(ObjectWireMarker.Auto()) DrawVisibleObjectWireframes(camera);
        }
        private void DrawVisibleObjectWireframes(Camera camera)
        {
            outlineCamera=camera;
            var depth=Handles.zTest;bool lighting=Handles.lighting;
            try
            {
                Handles.zTest=displayXray?UnityEngine.Rendering.CompareFunction.Always:UnityEngine.Rendering.CompareFunction.LessEqual;
                Handles.lighting=false;
                foreach(int i in VisibleWireframeNodes())
                {
                    if(StudioSettings.Current.hideObjectWireframe&&(mode==ComponentMode.Object||i!=Active))continue;
                    var lines=SelectionWireframeLines(i);
                    var color=objects.Contains(i)||HasSelectedParent(i)?new Color(.2f,.9f,.75f,1):new Color(.12f,.15f,.18f,1);
                    using(new Handles.DrawingScope(color,Matrix4x4.identity))
                        for(int e=0;e+1<lines.Length;e+=2) DrawNativeEdge(Transforms[i].TransformPoint(lines[e]),Transforms[i].TransformPoint(lines[e+1]),2);
                }
            }
            finally {Handles.zTest=depth;Handles.lighting=lighting;}
        }
        private void DrawNativeEdge(Vector3 a,Vector3 b,float width)
        {
            outlineSegment[0]=OffsetOutlineDepth(a);outlineSegment[1]=OffsetOutlineDepth(b);
            Handles.DrawAAPolyLine(width,outlineSegment);
        }
        private Vector3 OffsetOutlineDepth(Vector3 point)
        {
            if(!outlineCamera || displayXray)return point;
            // Move along the viewing ray: screen position stays identical, while the
            // normal editor line no longer competes with its own surface depth.
            const float fraction=.001f;
            if(!outlineCamera.orthographic)return Vector3.LerpUnclamped(point,outlineCamera.transform.position,fraction);
            float distance=Mathf.Max(outlineCamera.nearClipPlane,Vector3.Dot(point-outlineCamera.transform.position,outlineCamera.transform.forward));
            return point-outlineCamera.transform.forward*(distance*fraction);
        }
        private void DrawNativeComponentEdges(IEnumerable<int> selected,Color color,float width)
        {
            var depth=Handles.zTest;bool lighting=Handles.lighting;
            try
            {
                Handles.zTest=displayXray?UnityEngine.Rendering.CompareFunction.Always:UnityEngine.Rendering.CompareFunction.LessEqual;
                Handles.lighting=false;var edges=Edges();
                using(new Handles.DrawingScope(color,Matrix4x4.identity))
                    foreach(int id in selected)if(id>=0&&id<edges.Count)
                    {var edge=edges[id];DrawNativeEdge(Transforms[Active].TransformPoint(componentPositions[edge.a]),Transforms[Active].TransformPoint(componentPositions[edge.b]),width);}
            }
            finally {Handles.zTest=depth;Handles.lighting=lighting;}
        }
        internal Vector3[] VisibleWireframeLines(int index,Camera camera)=>SelectionWireframeLines(index);
        internal Vector3[] SelectionWireframeLines(int index)
        {
            var node = Document.nodes[index]; var mesh = node.mesh;
            if (!mesh) return Array.Empty<Vector3>();
            if (!selectionWireframes.TryGetValue(node, out var wire))
            {
                wire = new SelectionWireframe(); mesh.GetVertices(wire.vertices);
                var canonical = new int[wire.vertices.Count]; var positions = new Dictionary<Vector3, int>();
                for (int i = 0; i < canonical.Length; i++)
                {
                    if (node.skinned) { canonical[i] = i; continue; }
                    if (!positions.TryGetValue(wire.vertices[i], out int id)) positions[wire.vertices[i]] = id = positions.Count;
                    canonical[i] = id;
                }
                var edges = new HashSet<(int, int)>(); var indices = new List<int>();
                void Edge(int a, int b)
                {
                    int x = canonical[a], y = canonical[b];
                    if (x == y || !edges.Add((Math.Min(x, y), Math.Max(x, y)))) return;
                    indices.Add(a); indices.Add(b);
                }
                bool triangles = true;
                for (int sub = 0; sub < mesh.subMeshCount; sub++) if (mesh.GetTopology(sub) != MeshTopology.Triangles) triangles = false;
                if (triangles)
                {
                    foreach (var face in new StudioMeshCuts(node).Faces)
                        for (int e = 0; e < face.Vertices.Count; e++) Edge(face.Vertices[e], face.Vertices[(e + 1) % face.Vertices.Count]);
                }
                else for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var topology = mesh.GetTopology(sub); var list = mesh.GetIndices(sub);
                    if (topology == MeshTopology.Triangles || topology == MeshTopology.Quads)
                    {
                        int size = topology == MeshTopology.Quads ? 4 : 3;
                        for (int i = 0; i < list.Length; i += size) for (int j = 0; j < size; j++) Edge(list[i + j], list[i + (j + 1) % size]);
                    }
                    else if (topology == MeshTopology.Lines) for (int i = 0; i + 1 < list.Length; i += 2) Edge(list[i], list[i + 1]);
                    else if (topology == MeshTopology.LineStrip) for (int i = 0; i + 1 < list.Length; i++) Edge(list[i], list[i + 1]);
                }
                wire.indices = indices.ToArray(); wire.lines = new Vector3[indices.Count];

                if (node.skinned) wire.baked = new Mesh { name = "MashBox Studio selection preview", hideFlags = HideFlags.HideAndDontSave };
                else for (int i = 0; i < wire.lines.Length; i++) wire.lines[i] = wire.vertices[wire.indices[i]];
                selectionWireframes.Add(node, wire);
            }
            if (wire.baked)
            {
                Transforms[index].GetComponent<SkinnedMeshRenderer>().BakeMesh(wire.baked);
                wire.baked.GetVertices(wire.vertices);
                for (int i = 0; i < wire.lines.Length; i++) wire.lines[i] = wire.vertices[wire.indices[i]];

            }
            return wire.lines;
        }

    }
}
