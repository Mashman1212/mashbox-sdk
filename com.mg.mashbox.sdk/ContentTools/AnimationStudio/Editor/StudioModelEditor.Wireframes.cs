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
            internal int[] indices, canonical;
            internal Dictionary<(int,int),int> edgeLookup;
            internal Vector3[] lines;
            internal int[] orderedIndices, stripEnds;
            internal Vector3[] worldVertices, strokePoints;
            internal Mesh baked;
            internal readonly List<Vector3> vertices = new List<Vector3>();
            internal GraphicsBuffer positionBuffer, edgeBuffer;
            internal GraphicsBuffer adjacentBuffer;
            internal int[] adjacent;
            internal bool batchPositionsDirty = true, linesDirty;
            internal int vertexUpdateStart;
        }
        private readonly Vector3[] outlineSegment = new Vector3[2];
        private Texture2D componentEdgeStroke;
        private Texture2D ComponentEdgeStroke
        {
            get
            {
                if(componentEdgeStroke)return componentEdgeStroke;
                const int samples=64;
                componentEdgeStroke=new Texture2D(1,samples,TextureFormat.RGBA32,false,true)
                {hideFlags=HideFlags.HideAndDontSave,wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
                // Three-pixel stroke: two-pixel opaque core, half-pixel AA borders.
                var pixels=new Color[samples];
                for(int i=0;i<samples;i++)
                {
                    float t=(float)i/(samples-1);
                    pixels[i]=new Color(1,1,1,Mathf.Clamp01(Mathf.Min(t,1-t)*6));
                }
                componentEdgeStroke.SetPixels(pixels);componentEdgeStroke.Apply(false,true);
                return componentEdgeStroke;
            }
        }
        private Camera outlineCamera;
        private Vector3 outlineCameraPosition, outlineCameraForward;
        private bool outlineOrthographic;
        private float outlineNearClip;
#if MASHBOX_STUDIO_TESTS
        internal int ObjectWireDrawCalls { get; private set; }
#endif
        #if MASHBOX_STUDIO_TESTS
        internal bool UseBatchedWireframes { get; set; } = true;
#else
        private const bool UseBatchedWireframes = true;
#endif
        #if MASHBOX_STUDIO_TESTS
        internal bool SelectionOutlineEnabled { get; set; } = true;
#else
        private const bool SelectionOutlineEnabled = true;
#endif
        private readonly Dictionary<StudioModelDocument.Node, SelectionWireframe> selectionWireframes = new Dictionary<StudioModelDocument.Node, SelectionWireframe>();
        private void ClearSelectionWireframes()
        {
            ClearAllComponentDrawCaches();
            ReleaseComponentWireSubsets();
            foreach(var wire in selectionWireframes.Values) { if(wire.baked) UnityEngine.Object.DestroyImmediate(wire.baked); ReleaseWireBatch(wire); }
            selectionWireframes.Clear();
        }
        private void PositionsChanged(bool deferStatistics = false, IList<Vector3> markerPositions = null, int[] markerIndices = null)
        {
            var mesh=ActiveMesh;DirtySubdivision(mesh);
            RefreshIsolatedPositions(markerPositions);
            // Separating UV/normal seams can change position-based edge deduplication.
            // Rebuild only that mesh in this case, never every object in the scene.
            if(!weld&&Document)
                foreach(var node in Document.nodes)
                    if(node.mesh==mesh&&selectionWireframes.TryGetValue(node,out var oldWire))
                    {
                        if(oldWire.baked)UnityEngine.Object.DestroyImmediate(oldWire.baked);
                        ReleaseWireBatch(oldWire);
                        selectionWireframes.Remove(node);
                    }
            // A deformation keeps polygon/edge indices intact. Update only their
            // position buffers, including other nodes sharing this mesh.
            foreach(var pair in selectionWireframes)
            {
                var wire=pair.Value;
                if(pair.Key.mesh!=mesh||wire.baked)continue;
                int count=wire.vertices.Count;
                mesh.GetVertices(wire.vertices);
                wire.batchPositionsDirty = true; wire.vertexUpdateStart = 0;
                if(wire.vertices.Count!=count){ClearSelectionWireframes();break;}
                for(int i=0;i<wire.lines.Length;i++)wire.lines[i]=wire.vertices[wire.indices[i]];
            }
            RefreshComponentPositions(0, markerPositions, markerIndices);
            CaptureUndoMeshVersions();
            componentFrameValid=false;pivotTargetCache.Clear();pivotHover=null;
            if(Document)
            {
                InvalidatePositionStatistics(mesh, deferStatistics);
                EditorUtility.SetDirty(Document);
            }
            repaint();
        }
        internal IEnumerable<int> SelectedWireframeNodes()
        { foreach(int i in VisibleWireframeNodes()) if(objects.Contains(i)||HasSelectedParent(i))yield return i; }
        internal IEnumerable<int> VisibleWireframeNodes()
        {
            if(!Document)yield break;
            for(int i=0;i<Transforms.Length && i<Document.nodes.Count;i++)
            {
                if(!Transforms[i]||!Document.nodes[i].mesh||!Transforms[i].gameObject.activeInHierarchy)continue;
                var renderer=Transforms[i].GetComponent<Renderer>();if(renderer&&renderer.enabled&&!DisplayRendererHidden(renderer))yield return i;
            }
        }
        private void DrawSceneWireframes(Camera camera)
        {
            if(Event.current.type!=EventType.Repaint)return;
            DrawVisibleObjectWireframes(camera);
        }
        private static Color wireContrastBackground = new Color(-1,-1,-1,1);
        private static readonly Dictionary<(Color,float,bool),Color> wireContrastColors = new Dictionary<(Color,float,bool),Color>();
        internal static Color ContrastingWireColor(Color preferred,float minimum=3f,bool preferDark=false)
        {
            Color background=StudioSettings.Current.sceneBackground;
            if(background!=wireContrastBackground){wireContrastBackground=background;wireContrastColors.Clear();}
            var key=(preferred,minimum,preferDark);
            if(wireContrastColors.TryGetValue(key,out var cached))return cached;
            Color result=preferred;result.a=1;
            if(StudioTheme.TextContrast(result,background)<minimum)
            {
                Color target=preferDark && StudioTheme.TextContrast(Color.black,background)>=minimum ? Color.black :
                    StudioTheme.TextContrast(Color.white,background)>StudioTheme.TextContrast(Color.black,background)?Color.white:Color.black;
                float low=0,high=1;
                for(int i=0;i<16;i++)
                {float middle=(low+high)*.5f;if(StudioTheme.TextContrast(Color.Lerp(result,target,middle),background)>=minimum+.025f)high=middle;else low=middle;}
                result=Color.Lerp(result,target,high);
            }
            wireContrastColors[key]=result;return result;
        }
        internal static Color BackgroundWireColor
        {
            get {return ContrastingWireColor(new Color(.12f,.15f,.18f,1),1.5f,true);}
        }
        private void DrawVisibleObjectWireframes(Camera camera)
        {
            outlineCamera=camera;
            if(camera){outlineCameraPosition=camera.transform.position;outlineCameraForward=camera.transform.forward;outlineOrthographic=camera.orthographic;outlineNearClip=camera.nearClipPlane;}
#if MASHBOX_STUDIO_TESTS
                ObjectWireDrawCalls=0;
#endif
            var depth=Handles.zTest;bool lighting=Handles.lighting;
            try
            {
                Handles.zTest=displayXray?UnityEngine.Rendering.CompareFunction.Always:UnityEngine.Rendering.CompareFunction.LessEqual;
                Handles.lighting=false;
                foreach(int i in VisibleWireframeNodes())
                {
                    // node.mesh already contains the evaluated modifier result.
                    // Draw its real edges normally; subdivision must not force an
                    // extra cage overlay or bypass the user's depth/X-ray mode.
                    bool selected=objects.Contains(i)||HasSelectedParent(i);
                    bool highlight=selected&&SelectionOutlineEnabled&&mode==ComponentMode.Object;
                    Handles.zTest=displayXray?UnityEngine.Rendering.CompareFunction.Always:UnityEngine.Rendering.CompareFunction.LessEqual;
                    bool drawWire=wireframeOnly || !StudioSettings.Current.hideObjectWireframe || (mode!=ComponentMode.Object&&i==Active);
                    if(!drawWire&&!highlight)continue;
                    SelectionWireframeLines(i, false);
                    var wire=selectionWireframes[Document.nodes[i]];
                    var color=highlight?ContrastingWireColor(new Color(1,.65f,.15f,1)):selected?ContrastingWireColor(new Color(.2f,.9f,.75f,1)):BackgroundWireColor;
                    float width=highlight?1.2f:1.25f;
                    using(new Handles.DrawingScope(color,Matrix4x4.identity))
                    {
                        if (UseBatchedWireframes && DrawWireBatch(wire,Transforms[i].localToWorldMatrix,color,width,camera)) {
#if MASHBOX_STUDIO_TESTS
                ObjectWireDrawCalls++;
#endif
                        } else {
#if MASHBOX_STUDIO_TESTS
                                                        ObjectWireDrawCalls+=
#endif
                            DrawNativeWireframe(wire,Transforms[i].localToWorldMatrix,width);
                        }
                    }
                }
            }
            finally {Handles.zTest=depth;Handles.lighting=lighting;}
        }
        private static void BuildWireframeStrokes(SelectionWireframe wire,int[] canonical)
        {
            int edgeCount=wire.indices.Length/2;
            var adjacency=new List<int>[canonical.Length];
            for(int e=0;e<edgeCount;e++)
            {
                int a=canonical[wire.indices[e*2]],b=canonical[wire.indices[e*2+1]];
                if(adjacency[a]==null)adjacency[a]=new List<int>();
                if(adjacency[b]==null)adjacency[b]=new List<int>();
                adjacency[a].Add(e);adjacency[b].Add(e);
            }
            var starts=new Queue<int>();
            for(int v=0;v<adjacency.Length;v++)if(adjacency[v]!=null&&(adjacency[v].Count&1)!=0)starts.Enqueue(v);
            for(int v=0;v<adjacency.Length;v++)if(adjacency[v]!=null)starts.Enqueue(v);
            var used=new bool[edgeCount];var next=new int[canonical.Length];
            var ordered=new List<int>(wire.indices.Length);var ends=new List<int>();int maxPoints=2;
            bool Take(int vertex,out int edge)
            {
                var adjacent=adjacency[vertex];
                while(next[vertex]<adjacent.Count)
                {
                    edge=adjacent[next[vertex]++];
                    if(!used[edge])return true;
                }
                edge=-1;return false;
            }
            // Build connected trails once. No added connector edges and no
            // per-frame graph traversal, lists or arrays during drawing.
            while(starts.Count>0)
            {
                int vertex=starts.Dequeue(),begin=ordered.Count;
                while(Take(vertex,out int edge))
                {
                    used[edge]=true;
                    int a=wire.indices[edge*2],b=wire.indices[edge*2+1];
                    if(canonical[a]!=vertex){int swap=a;a=b;b=swap;}
                    ordered.Add(a);ordered.Add(b);vertex=canonical[b];
                }
                if(ordered.Count==begin)continue;
                ends.Add(ordered.Count/2);maxPoints=Mathf.Max(maxPoints,(ordered.Count-begin)/2+1);
            }
            wire.orderedIndices=ordered.ToArray();wire.stripEnds=ends.ToArray();
            wire.worldVertices=new Vector3[wire.vertices.Count];wire.strokePoints=new Vector3[Mathf.Min(maxPoints,4096)];
        }
        private int DrawNativeWireframe(SelectionWireframe wire,Matrix4x4 matrix,float width)
        {
            for(int i=0;i<wire.vertices.Count;i++)wire.worldVertices[i]=OffsetOutlineDepth(matrix.MultiplyPoint3x4(wire.vertices[i]));
            int begin=0,calls=0;
            foreach(int end in wire.stripEnds)
            {
                int count=0;
                for(int e=begin;e<end;e++)
                {
                    var a=wire.worldVertices[wire.orderedIndices[e*2]];var b=wire.worldVertices[wire.orderedIndices[e*2+1]];
                    // A deformed/skinned seam can split a previously shared point.
                    if(count>0&&!wire.strokePoints[count-1].Equals(a))
                    {Handles.DrawAAPolyLine(width,count,wire.strokePoints);calls++;count=0;}
                    if(count==0)wire.strokePoints[count++]=a;
                    if(count==wire.strokePoints.Length)
                    {
                        Handles.DrawAAPolyLine(width,count,wire.strokePoints);calls++;
                        wire.strokePoints[0]=wire.strokePoints[count-1];count=1;
                    }
                    wire.strokePoints[count++]=b;
                }
                if(count>1){Handles.DrawAAPolyLine(width,count,wire.strokePoints);calls++;}
                begin=end;
            }
            return calls;
        }
        private void DrawNativeEdge(Vector3 a,Vector3 b,float width,Texture2D stroke=null)
        {
            outlineSegment[0]=OffsetOutlineDepth(a);outlineSegment[1]=OffsetOutlineDepth(b);
            if(stroke)Handles.DrawAAPolyLine(stroke,width,outlineSegment);
            else Handles.DrawAAPolyLine(width,outlineSegment);
        }
        private Vector3 OffsetOutlineDepth(Vector3 point)
        {
            if(!outlineCamera || displayXray)return point;
            // Move along the viewing ray: screen position stays identical, while the
            // normal editor line no longer competes with its own surface depth.
            const float fraction=.00001f;
            if(!outlineOrthographic)return Vector3.LerpUnclamped(point,outlineCameraPosition,fraction);
            float distance=Mathf.Max(outlineNearClip,Vector3.Dot(point-outlineCameraPosition,outlineCameraForward));
            return point-outlineCameraForward*(distance*fraction);
        }
        private void DrawComponentEdges(IEnumerable<int> selected,Color color,float width)
        {
            color=ContrastingWireColor(color);
            if(DrawComponentWireSubset(selected,color,width))return;
            var depth=Handles.zTest;bool lighting=Handles.lighting;
            try
            {
                Handles.zTest=displayXray?UnityEngine.Rendering.CompareFunction.Always:UnityEngine.Rendering.CompareFunction.LessEqual;
                Handles.lighting=false;var edges=Edges();
                using(new Handles.DrawingScope(color,Matrix4x4.identity))
                    foreach(int id in selected)if(id>=0&&id<edges.Count&&IsolationAllowsComponent(id))
                    {var edge=edges[id];DrawNativeEdge(Transforms[Active].TransformPoint(componentPositions[edge.a]),Transforms[Active].TransformPoint(componentPositions[edge.b]),width,ComponentEdgeStroke);}
            }
            finally {Handles.zTest=depth;Handles.lighting=lighting;}
        }
        internal Vector3[] VisibleWireframeLines(int index,Camera camera)=>SelectionWireframeLines(index);
        internal Vector3[] SelectionWireframeLines(int index, bool updateLines = true)
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
                var edges = new Dictionary<(int, int),int>(); var indices = new List<int>(); var adjacent = new List<int>();
                void Edge(int a, int b, int opposite = -1)
                {
                    if (!IsolationAllowsEdge(index,a,b)) return;
                    int x = canonical[a], y = canonical[b];
                    if(x==y)return;
                    var key=(Math.Min(x,y),Math.Max(x,y));
                    if(edges.TryGetValue(key,out int existing))
                    {
                        if(opposite>=0 && adjacent[existing*2+1]==0)
                            adjacent[existing*2+1]=(canonical[indices[existing*2]]==x?1:-1)*(opposite+1);
                        return;
                    }
                    edges.Add(key,indices.Count/2);
                    indices.Add(a); indices.Add(b);
                    adjacent.Add(opposite+1); adjacent.Add(0);
                }
                bool triangles = true;
                for (int sub = 0; sub < mesh.subMeshCount; sub++) if (mesh.GetTopology(sub) != MeshTopology.Triangles) triangles = false;
                if (triangles)
                {
                    var topology = new StudioMeshCuts(node);
                    var renderTriangles = topology.BevelSourceTriangles(mesh);
                    var faceEdges = new Dictionary<(int,int),int>();
                    for (int f = 0; f < topology.Faces.Count; f++)
                    {
                        faceEdges.Clear();
                        var trianglesForFace = renderTriangles[f];
                        for (int t = 0; t < trianglesForFace.Count; t += 3)
                            for (int e = 0; e < 3; e++)
                            {
                                int a = trianglesForFace[t+e], b = trianglesForFace[t+(e+1)%3];
                                faceEdges[(Math.Min(a,b),Math.Max(a,b))] = trianglesForFace[t+(e+2)%3];
                            }
                        var face = topology.Faces[f].Vertices;
                        for (int e = 0; e < face.Count; e++)
                        {
                            int a = face[e], b = face[(e+1)%face.Count];
                            int third = faceEdges.TryGetValue((Math.Min(a,b),Math.Max(a,b)),out int opposite) ? opposite : -1;
                            Edge(a,b,third);
                        }
                    }
                }
                else for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var topology = mesh.GetTopology(sub); var list = mesh.GetIndices(sub);
                    if (topology == MeshTopology.Triangles || topology == MeshTopology.Quads)
                    {
                        int size = topology == MeshTopology.Quads ? 4 : 3;
                        for (int i = 0; i < list.Length; i += size) for (int j = 0; j < size; j++) Edge(list[i + j], list[i + (j + 1) % size],list[i+(size == 4 ? (j == 0 || j == 3 ? 2 : 0) : (j+2)%size)]);
                    }
                    else if (topology == MeshTopology.Lines) for (int i = 0; i + 1 < list.Length; i += 2) Edge(list[i], list[i + 1]);
                    else if (topology == MeshTopology.LineStrip) for (int i = 0; i + 1 < list.Length; i++) Edge(list[i], list[i + 1]);
                }
                wire.canonical=canonical;wire.edgeLookup=edges;
                wire.indices = indices.ToArray(); wire.lines = new Vector3[indices.Count];
                wire.adjacent=adjacent.ToArray();
                BuildWireframeStrokes(wire,canonical);

                if (node.skinned) wire.baked = new Mesh { name = "MashBox Studio selection preview", hideFlags = HideFlags.HideAndDontSave };
                else for (int i = 0; i < wire.lines.Length; i++) wire.lines[i] = wire.vertices[wire.indices[i]];
                selectionWireframes.Add(node, wire);
            }
            if (wire.baked)
            {
                Transforms[index].GetComponent<SkinnedMeshRenderer>().BakeMesh(wire.baked);
                wire.baked.GetVertices(wire.vertices);
                wire.batchPositionsDirty = true; wire.vertexUpdateStart = 0;
                for (int i = 0; i < wire.lines.Length; i++) wire.lines[i] = wire.vertices[wire.indices[i]];

            }
            if (updateLines && wire.linesDirty)
            { for (int i = 0; i < wire.lines.Length; i++) wire.lines[i] = wire.vertices[wire.indices[i]]; wire.linesDirty = false; }
            return wire.lines;
        }

    }
}
