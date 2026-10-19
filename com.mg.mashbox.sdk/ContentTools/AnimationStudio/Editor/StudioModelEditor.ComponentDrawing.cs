using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private static readonly ProfilerMarker ComponentDrawMarker = new ProfilerMarker("MashBox Studio.Modeler.Component overlays");
        private static readonly ProfilerMarker ComponentBuildMarker = new ProfilerMarker("MashBox Studio.Modeler.Build overlay cache");
        private static readonly ProfilerMarker ObjectWireMarker = new ProfilerMarker("MashBox Studio.Modeler.Object outlines");
        private Mesh componentSource, componentLines, componentDots, componentHighlight, componentHover;
        private Vector3[] componentPositions;
        private readonly HashSet<int> drawnComponents = new HashSet<int>();
        private int hoveredComponent = -1, drawnHover = -1;
        private ComponentMode hoverMode;
        private ComponentMode drawnMode;
        private Material componentMaterial;
        private static readonly int OverlayColor = Shader.PropertyToID("_Color"), OverlayMode = Shader.PropertyToID("_Mode"), OverlaySize = Shader.PropertyToID("_Size"), OverlayScreen = Shader.PropertyToID("_Viewport"), OverlayDepth = Shader.PropertyToID("_ZTest");
        internal int ComponentOverlayBuilds { get; private set; }
        internal int ComponentOverlayDrawCalls { get; private set; }

        private void ClearComponentDrawing()
        {
            foreach (var mesh in new[]{componentLines,componentDots,componentHighlight,componentHover}) if(mesh) UnityEngine.Object.DestroyImmediate(mesh);
            componentLines=componentDots=componentHighlight=componentHover=componentSource=null;drawnHover=-1;
            componentPositions=null;drawnComponents.Clear();
        }
        private void DisposeComponentDrawing()
        {
            ClearComponentDrawing();if(componentMaterial)UnityEngine.Object.DestroyImmediate(componentMaterial);
        }
        private static Mesh OverlayMesh(string name,Vector3[] positions,int[] indices,MeshTopology topology,Vector2[] corners=null,Vector3[] endpoints=null)
        {
            var mesh=new Mesh { name="MashBox Studio "+name,hideFlags=HideFlags.HideAndDontSave,indexFormat=positions.Length>65535?IndexFormat.UInt32:IndexFormat.UInt16 };
            mesh.vertices=positions;
            if(corners!=null)mesh.uv=corners;
            if(endpoints!=null)mesh.normals=endpoints; // A second endpoint, not a lighting normal.
            mesh.SetIndices(indices,topology,0);mesh.UploadMeshData(false);return mesh;
        }
        private Mesh PointBatch(IEnumerable<int> points)
        {
            var positions=new List<Vector3>();var corners=new List<Vector2>();var indices=new List<int>();
            foreach(int point in points)
            {
                if(point<0||point>=componentPositions.Length)continue;
                int start=positions.Count;
                for(int i=0;i<4;i++)positions.Add(componentPositions[point]);
                corners.Add(new Vector2(-.5f,-.5f));corners.Add(new Vector2(-.5f,.5f));corners.Add(new Vector2(.5f,.5f));corners.Add(new Vector2(.5f,-.5f));
                indices.Add(start);indices.Add(start+1);indices.Add(start+2);indices.Add(start);indices.Add(start+2);indices.Add(start+3);
            }
            return OverlayMesh("vertex dots",positions.ToArray(),indices.ToArray(),MeshTopology.Triangles,corners.ToArray());
        }
        private void EnsureComponentDrawing()
        {
            if(componentSource!=ActiveMesh)
            {
                using(ComponentBuildMarker.Auto())
                {
                    ClearComponentDrawing();componentSource=ActiveMesh;componentPositions=ActiveMesh.vertices;
                    var edges=Edges();var indices=new int[edges.Count*2];
                    for(int i=0;i<edges.Count;i++){indices[i*2]=edges[i].a;indices[i*2+1]=edges[i].b;}
                    componentLines=OverlayMesh("component edges",componentPositions,indices,MeshTopology.Lines);
                    drawnMode=(ComponentMode)(-1);ComponentOverlayBuilds++;
                }
            }
            if(mode==ComponentMode.Vertex&&!componentDots)
            {
                using(ComponentBuildMarker.Auto())
                { var all=new int[componentPositions.Length];for(int i=0;i<all.Length;i++)all[i]=i;componentDots=PointBatch(all);ComponentOverlayBuilds++; }
            }
            if(mode==drawnMode&&drawnComponents.SetEquals(Components))return;
            using(ComponentBuildMarker.Auto())
            {
                if(componentHighlight)UnityEngine.Object.DestroyImmediate(componentHighlight);
                componentHighlight=null;drawnMode=mode;drawnComponents.Clear();drawnComponents.UnionWith(Components);
                if(Components.Count==0)return;
                componentHighlight=ComponentHighlight(Components);
                ComponentOverlayBuilds++;
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
                foreach(int edge in components)
                {
                    if(edge<0||edge>=edges.Count)continue;
                    int start=positions.Count;var a=componentPositions[edges[edge].a];var b=componentPositions[edges[edge].b];

                    for(int i=0;i<4;i++){positions.Add(a);ends.Add(b);}
                    corners.Add(new Vector2(0,-.5f));corners.Add(new Vector2(0,.5f));corners.Add(new Vector2(1,.5f));corners.Add(new Vector2(1,-.5f));
                    indices.Add(start);indices.Add(start+1);indices.Add(start+2);indices.Add(start);indices.Add(start+2);indices.Add(start+3);
                }
                highlight=OverlayMesh("selected edges",positions.ToArray(),indices.ToArray(),MeshTopology.Triangles,corners.ToArray(),ends.ToArray());
            }
            return highlight;
        }
        private void EnsureComponentHover()
        {
            int next = Components.Contains(hoveredComponent) ? -1 : hoveredComponent;
            if(next==drawnHover && mode==hoverMode)return;
            if(componentHover)UnityEngine.Object.DestroyImmediate(componentHover);
            componentHover=null;drawnHover=next;hoverMode=mode;
            if(next>=0)componentHover=ComponentHighlight(new[]{next});
        }
        private void DrawComponentBatches(SceneView view)
        {
            using(ComponentDrawMarker.Auto())
            {
                ComponentOverlayDrawCalls=0;EnsureComponentDrawing();EnsureComponentHover();
                if(!componentMaterial)componentMaterial=new Material(Shader.Find("Hidden/MashBox/ModelerComponents")) { hideFlags=HideFlags.HideAndDontSave };
                componentMaterial.SetVector(OverlayScreen,new Vector4(view.camera.pixelWidth,view.camera.pixelHeight,0,0));
                componentMaterial.SetFloat(OverlayDepth,(float)(displayXray?CompareFunction.Always:CompareFunction.LessEqual));
                var matrix=Transforms[Active].localToWorldMatrix;
                void Draw(Mesh mesh,Color color,int shaderMode,float size)
                {
                    if(!mesh||mesh.vertexCount==0||mesh.GetIndexCount(0)==0)return;
                    componentMaterial.SetColor(OverlayColor,color);componentMaterial.SetFloat(OverlayMode,shaderMode);componentMaterial.SetFloat(OverlaySize,size*EditorGUIUtility.pixelsPerPoint);
                    if(componentMaterial.SetPass(0)){Graphics.DrawMeshNow(mesh,matrix);ComponentOverlayDrawCalls++;}
                }
                if(mode==ComponentMode.Vertex)Draw(componentDots,new Color(.35f,.9f,.9f,1),1,4);
                if(mode==ComponentMode.Edge)
                {
                    if(drawnHover>=0)DrawNativeComponentEdges(new[]{drawnHover},new Color(1,.95f,.5f,1),5);
                    DrawNativeComponentEdges(Components,new Color(1,.65f,.15f,1),4);
                }
                else
                {
                    Draw(componentHover,new Color(1,.95f,.5f,displayXray ? 1 : .8f),mode==ComponentMode.Vertex?1:0,mode==ComponentMode.Vertex?7:5);
                    Draw(componentHighlight,new Color(1,.65f,.15f,displayXray ? 1 : .9f),mode==ComponentMode.Vertex?1:0,mode==ComponentMode.Vertex?5:4);
                }
            }
        }
    }
}
