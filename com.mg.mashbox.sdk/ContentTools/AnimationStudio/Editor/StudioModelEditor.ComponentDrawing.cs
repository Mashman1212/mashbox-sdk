using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioFaceSelectionStyle
    {
        internal static readonly Color Selected = new Color(1f,.72f,.18f,1f);
        internal static readonly Color Hover = new Color(.55f,1f,.95f,1f);
        internal const float SelectedOpacity=.28f, HoverOpacity=.4f;
        internal static Color Fill(Color tint,float opacity) { tint.a=opacity;return tint; }
    }
    internal sealed partial class StudioModelEditor
    {
        private Mesh componentSource, componentLines, componentDots, componentHighlight, componentHover;
        private Vector3[] componentPositions;
        private List<Vector3> componentPositionBuffer = new List<Vector3>();
        private sealed class OverlayGeometry
        {
            internal Mesh mesh;
            internal Vector3[] positions, endpoints;
            internal int[] points, ends;
        }
        private List<OverlayGeometry> componentGeometry = new List<OverlayGeometry>();
        private HashSet<int> drawnComponents = new HashSet<int>();
        private int hoveredComponent = -1, drawnHover = -1;
        private ComponentMode hoverMode;
        private ComponentMode drawnMode;
        private Material componentMaterial;
        private static readonly int OverlayColor = Shader.PropertyToID("_Color"), OverlayMode = Shader.PropertyToID("_Mode"), OverlaySize = Shader.PropertyToID("_Size"), OverlayScreen = Shader.PropertyToID("_Viewport"), OverlayDepth = Shader.PropertyToID("_ZTest");
#if MASHBOX_STUDIO_TESTS
        internal int ComponentOverlayBuilds { get; private set; }
#endif
#if MASHBOX_STUDIO_TESTS
        internal int ComponentOverlayDrawCalls { get; private set; }
#endif
        private void ClearComponentDrawing()
        {
            paintPickIndex = null;
            cachedSelectedStatistics=null; ReleaseVertexMarkers(); InvalidateHandleComponentFrame();
            foreach (var mesh in new[]{componentLines,componentDots,componentHighlight,componentHover}) if(mesh) UnityEngine.Object.DestroyImmediate(mesh);
            componentLines=componentDots=componentHighlight=componentHover=componentSource=null;drawnHover=-1;
            componentPositions=null;drawnComponents.Clear();
            componentGeometry.Clear();componentPositionBuffer.Clear();
            componentFaceUseSource=null; componentFaceOffsets=componentFaceUses=null;
        }
        private void DisposeComponentDrawing()
        {
            ClearAllComponentDrawCaches();if(componentMaterial)UnityEngine.Object.DestroyImmediate(componentMaterial);
            if(markerMaterial)UnityEngine.Object.DestroyImmediate(markerMaterial);
        }
        private Mesh OverlayMesh(string name,Vector3[] positions,int[] indices,MeshTopology topology,Vector2[] corners=null,Vector3[] endpoints=null,int[] sourcePoints=null,int[] sourceEnds=null)
        {
            var mesh=new Mesh { name="MashBox Studio "+name,hideFlags=HideFlags.HideAndDontSave,indexFormat=positions.Length>65535?IndexFormat.UInt32:IndexFormat.UInt16 };
            mesh.vertices=positions;
            if(corners!=null)mesh.uv=corners;
            if(endpoints!=null)mesh.normals=endpoints; // A second endpoint, not a lighting normal.
            mesh.SetIndices(indices,topology,0);mesh.UploadMeshData(false);
            componentGeometry.Add(new OverlayGeometry{mesh=mesh,positions=positions,endpoints=endpoints,points=sourcePoints,ends=sourceEnds});
            return mesh;
        }
        private void DestroyComponentOverlay(Mesh mesh)
        {
            if(!mesh)return;
            for(int i=componentGeometry.Count-1;i>=0;i--)if(ReferenceEquals(componentGeometry[i].mesh,mesh))componentGeometry.RemoveAt(i);
            UnityEngine.Object.DestroyImmediate(mesh);
        }
        private void RefreshComponentPositions(int start = 0, IList<Vector3> positions = null, int[] changedPoints = null)
        {
            paintPickIndex = null; InvalidateHandleComponentFrame();
            if(componentSource!=ActiveMesh||componentPositions==null)return;
            if (positions == null || positions.Count != componentPositions.Length)
            {
                ActiveMesh.GetVertices(componentPositionBuffer);
                if(componentPositionBuffer.Count!=componentPositions.Length){ClearComponentDrawing();return;}
                positions = componentPositionBuffer;
            }
            if (changedPoints != null)
            {
                foreach (int point in changedPoints) componentPositions[point] = positions[point];
                UploadChangedMarkerPoints(changedPoints);
            }
            else
            {
                int first = -1, last = -1;
                for (int i = start; i < componentPositions.Length; i++)
                    if (!componentPositions[i].Equals(positions[i]))
                    { componentPositions[i] = positions[i]; if (first < 0) first = i; last = i; }
                if (first < 0) return;
                UploadMarkerRange(first, last - first + 1);
            }
            foreach(var geometry in componentGeometry)
            {
                if(!geometry.mesh)continue;
                if(geometry.points!=null)
                    for(int i=0;i<geometry.points.Length;i++)
                        for(int corner=0;corner<4;corner++)
                        {
                            geometry.positions[i*4+corner]=componentPositions[geometry.points[i]];
                            if(geometry.ends!=null)geometry.endpoints[i*4+corner]=componentPositions[geometry.ends[i]];
                        }
                geometry.mesh.vertices=geometry.positions;
                if(geometry.endpoints!=null)geometry.mesh.normals=geometry.endpoints;
            }
        }
        private Mesh PointBatch(IEnumerable<int> points)
        {
            var positions=new List<Vector3>();var corners=new List<Vector2>();var indices=new List<int>();var sourcePoints=new List<int>();
            foreach(int point in points)
            {
                if(point<0||point>=componentPositions.Length)continue;
                sourcePoints.Add(point);
                int start=positions.Count;
                for(int i=0;i<4;i++)positions.Add(componentPositions[point]);
                corners.Add(new Vector2(-.5f,-.5f));corners.Add(new Vector2(-.5f,.5f));corners.Add(new Vector2(.5f,.5f));corners.Add(new Vector2(.5f,-.5f));
                indices.Add(start);indices.Add(start+1);indices.Add(start+2);indices.Add(start);indices.Add(start+2);indices.Add(start+3);
            }
            return OverlayMesh("vertex dots",positions.ToArray(),indices.ToArray(),MeshTopology.Triangles,corners.ToArray(),sourcePoints:sourcePoints.ToArray());
        }
        private void EnsureComponentDrawing()
        {
            if(componentSource!=ActiveMesh)
            {
                {
                    ClearComponentDrawing();componentSource=ActiveMesh;componentPositions=ActiveMesh.vertices;
                    // Regular polygon edges already use the GPU wire batch.
                    // Do not allocate/upload an unused second whole-mesh line mesh.
                    drawnMode=(ComponentMode)(-1);
#if MASHBOX_STUDIO_TESTS
                ComponentOverlayBuilds++;
#endif
                }
            }
            if(mode==ComponentMode.Vertex&&UseGpuVertexMarkers) EnsureVertexMarkers();
            if(mode==ComponentMode.Vertex&&!UseGpuVertexMarkers&&!componentDots)
            {
                { var all=new int[componentPositions.Length];for(int i=0;i<all.Length;i++)all[i]=i;componentDots=PointBatch(all);
#if MASHBOX_STUDIO_TESTS
                ComponentOverlayBuilds++;
#endif
 }
            }
            if(mode==drawnMode&&drawnComponents.SetEquals(Components))return;
            {
                DestroyComponentOverlay(componentHighlight);
                componentHighlight=null;drawnMode=mode;drawnComponents.Clear();drawnComponents.UnionWith(Components);
                if(mode==ComponentMode.Vertex&&UseGpuVertexMarkers){UpdateVertexMarkerSelection();return;}
                if(Components.Count==0)return;
                componentHighlight=ComponentHighlight(Components);
#if MASHBOX_STUDIO_TESTS
                ComponentOverlayBuilds++;
#endif
            }
        }
        private Mesh ComponentHighlight(IEnumerable<int> components)
        {
            Mesh highlight=null;
            if(mode==ComponentMode.Vertex)highlight=PointBatch(components);
            else if(mode==ComponentMode.Face)
            {
                Faces();var indices=new List<int>();
                foreach(int face in components)if(face>=0&&face<cachedFaceTriangles.Length)indices.AddRange(cachedFaceTriangles[face]);
                highlight=OverlayMesh("selected faces",componentPositions,indices.ToArray(),MeshTopology.Triangles);
            }
            else if(mode==ComponentMode.Edge)
            {
                var edges=Edges();var positions=new List<Vector3>();var ends=new List<Vector3>();var corners=new List<Vector2>();var indices=new List<int>();
                var sourcePoints=new List<int>();var sourceEnds=new List<int>();
                foreach(int edge in components)
                {
                    if(edge<0||edge>=edges.Count)continue;
                    sourcePoints.Add(edges[edge].a);sourceEnds.Add(edges[edge].b);
                    int start=positions.Count;var a=componentPositions[edges[edge].a];var b=componentPositions[edges[edge].b];

                    for(int i=0;i<4;i++){positions.Add(a);ends.Add(b);}
                    corners.Add(new Vector2(0,-.5f));corners.Add(new Vector2(0,.5f));corners.Add(new Vector2(1,.5f));corners.Add(new Vector2(1,-.5f));
                    indices.Add(start);indices.Add(start+1);indices.Add(start+2);indices.Add(start);indices.Add(start+2);indices.Add(start+3);
                }
                highlight=OverlayMesh("selected edges",positions.ToArray(),indices.ToArray(),MeshTopology.Triangles,corners.ToArray(),ends.ToArray(),sourcePoints.ToArray(),sourceEnds.ToArray());
            }
            return highlight;
        }
        private void EnsureComponentHover()
        {
            int next = mode==ComponentMode.Face || PaintHoverVisible ? hoveredComponent : Components.Contains(hoveredComponent) ? -1 : hoveredComponent;
            if(next==drawnHover && mode==hoverMode)return;
            DestroyComponentOverlay(componentHover);
            componentHover=null;drawnHover=next;hoverMode=mode;
            if(next>=0 && !(mode==ComponentMode.Vertex&&UseGpuVertexMarkers))componentHover=ComponentHighlight(new[]{next});
        }
        private void DrawComponentBatches(SceneView view)
        {
            {
#if MASHBOX_STUDIO_TESTS
                ComponentOverlayDrawCalls=0;
#endif
EnsureComponentDrawing();EnsureComponentHover();
                if(mode==ComponentMode.Vertex&&UseGpuVertexMarkers){DrawVertexMarkers(view.camera,Transforms[Active].localToWorldMatrix);return;}
                if(!componentMaterial)componentMaterial=new Material(Shader.Find("Hidden/MashBox/ModelerComponents")) { hideFlags=HideFlags.HideAndDontSave };
                componentMaterial.SetVector(OverlayScreen,new Vector4(view.camera.pixelWidth,view.camera.pixelHeight,0,0));
                componentMaterial.SetFloat(OverlayDepth,(float)(displayXray?CompareFunction.Always:CompareFunction.LessEqual));
                var matrix=Transforms[Active].localToWorldMatrix;
                void Draw(Mesh mesh,Color color,int shaderMode,float size)
                {
                    if(!mesh||mesh.vertexCount==0||mesh.GetIndexCount(0)==0)return;
                    componentMaterial.SetColor(OverlayColor,color);componentMaterial.SetFloat(OverlayMode,shaderMode);componentMaterial.SetFloat(OverlaySize,size*EditorGUIUtility.pixelsPerPoint);
                    if(componentMaterial.SetPass(0)){Graphics.DrawMeshNow(mesh,matrix);
#if MASHBOX_STUDIO_TESTS
                ComponentOverlayDrawCalls++;
#endif
}
                }
                if(mode==ComponentMode.Face)
                {
                    Draw(componentHighlight,StudioFaceSelectionStyle.Fill(StudioFaceSelectionStyle.Selected,StudioFaceSelectionStyle.SelectedOpacity),0,0);
                    var tint=Components.Contains(drawnHover)?StudioFaceSelectionStyle.Selected:StudioFaceSelectionStyle.Hover;
                    Draw(componentHover,StudioFaceSelectionStyle.Fill(tint,StudioFaceSelectionStyle.HoverOpacity),0,0);
                    return;
                }
                if(mode==ComponentMode.Vertex)Draw(componentDots,new Color(.35f,.9f,.9f,1),1,4);
                if(mode==ComponentMode.Edge)
                {
                    if(!PaintHoverVisible && drawnHover>=0)DrawNativeComponentEdges(new[]{drawnHover},new Color(1,.95f,.5f,1),3);
                    DrawNativeComponentEdges(Components,new Color(1,.65f,.15f,1),3);
                    if(PaintHoverVisible && drawnHover>=0)DrawNativeComponentEdges(new[]{drawnHover},new Color(1,.95f,.5f,1),3);
                }
                else
                {
                    if(!PaintHoverVisible)Draw(componentHover,new Color(1,.95f,.5f,displayXray ? 1 : .8f),mode==ComponentMode.Vertex?1:0,mode==ComponentMode.Vertex?10:5);
                    Draw(componentHighlight,new Color(1,.65f,.15f,displayXray ? 1 : .9f),mode==ComponentMode.Vertex?1:0,mode==ComponentMode.Vertex?5:4);
                    if(PaintHoverVisible)Draw(componentHover,new Color(1,.95f,.5f,displayXray ? 1 : .8f),mode==ComponentMode.Vertex?1:0,mode==ComponentMode.Vertex?10:5);

                }
            }
        }
        private void DrawHoveredFaceBorder(int index,float width)
        {
            var faces=Faces();
            if(index>=faces.Count)return;
            var vertices=faces[index].Vertices;
            var depth=Handles.zTest;bool lighting=Handles.lighting;
            try
            {
                Handles.zTest=displayXray?CompareFunction.Always:CompareFunction.LessEqual;
                Handles.lighting=false;
                using(new Handles.DrawingScope(Components.Contains(index)?StudioFaceSelectionStyle.Selected:StudioFaceSelectionStyle.Hover,Matrix4x4.identity))
                    for(int i=0;i<vertices.Count;i++)
                        DrawNativeEdge(Transforms[Active].TransformPoint(componentPositions[vertices[i]]),
                            Transforms[Active].TransformPoint(componentPositions[vertices[(i+1)%vertices.Count]]),width);
            }
            finally {Handles.zTest=depth;Handles.lighting=lighting;}
        }
    }
}
