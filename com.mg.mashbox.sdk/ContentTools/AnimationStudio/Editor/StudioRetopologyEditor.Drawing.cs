using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        private Material topologyOverlayMaterial;
        private Material topologyWireMaterial;
        private TopologyWireBatch workingWireBatch, selectedWireBatch;
        private sealed class TopologyWireBatch : System.IDisposable
        {
            internal GraphicsBuffer positions,edges,adjacent;
            internal int count;
            internal TopologyWireBatch(Vector3[] points,int[] indices)
            {
                count=indices.Length/2;
                positions=new GraphicsBuffer(GraphicsBuffer.Target.Structured,points.Length,12);positions.SetData(points);
                edges=new GraphicsBuffer(GraphicsBuffer.Target.Structured,indices.Length,4);edges.SetData(indices);
                adjacent=new GraphicsBuffer(GraphicsBuffer.Target.Structured,indices.Length,4);adjacent.SetData(new int[indices.Length]);
            }
            public void Dispose(){positions?.Dispose();edges?.Dispose();adjacent?.Dispose();}
        }
        private bool DrawTopologyWire(ref TopologyWireBatch batch,Mesh mesh,Vector3[] lines,Color color,float width,CompareFunction depth)
        {
            if(SystemInfo.graphicsShaderLevel<45)return false;
            if(!topologyWireMaterial)
            {
                var shader=Shader.Find("Hidden/MashBox/ModelerWireBatch");
                if(!shader || !shader.isSupported)return false;
                topologyWireMaterial=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
            }
            if(batch==null)
            {
                var points=mesh?mesh.vertices:lines;
                var indices=mesh?mesh.GetIndices(0):Enumerable.Range(0,lines.Length).ToArray();
                if(points.Length==0||indices.Length==0)return true;
                batch=new TopologyWireBatch(points,indices);
            }
            var camera=view.camera;var position=camera.transform.position;var forward=camera.transform.forward;
            topologyWireMaterial.SetBuffer("_WirePositions",batch.positions);topologyWireMaterial.SetBuffer("_WireEdges",batch.edges);topologyWireMaterial.SetBuffer("_WireAdjacent",batch.adjacent);
            topologyWireMaterial.SetMatrix("_WireObjectToWorld",Matrix4x4.identity);
            topologyWireMaterial.SetVector("_WireViewport",new Vector4(camera.pixelWidth,camera.pixelHeight,0,0));
            topologyWireMaterial.SetVector("_WireCamera",new Vector4(position.x,position.y,position.z,camera.orthographic?1:0));
            topologyWireMaterial.SetVector("_WireForward",new Vector4(forward.x,forward.y,forward.z,camera.nearClipPlane));
            topologyWireMaterial.SetColor("_WireColor",color);topologyWireMaterial.SetFloat("_WireWidth",width*EditorGUIUtility.pixelsPerPoint);
            topologyWireMaterial.SetFloat("_ZTest",(float)depth);topologyWireMaterial.SetFloat("_WireXRay",depth==CompareFunction.Always?1:0);
            if(!topologyWireMaterial.SetPass(0))return false;
            Graphics.DrawProceduralNow(MeshTopology.Triangles,6,batch.count);return true;
        }
        private void DrawTopologyWorkingWire()
        {
            if(overlayPart!=Active){ClearTopologyDrawing();overlayPart=Active;}
            var mesh=xrayLines[Document.selected];var color=new Color(.35f,1,.8f,.85f);
            if(!DrawTopologyWire(ref workingWireBatch,mesh,null,color,1.4f,CompareFunction.LessEqual))
                DrawTopologyBatch(mesh,color,false,1,CompareFunction.LessEqual);
        }
        private StudioRetopologyDocument.Part overlayPart;
        private Mesh topologyDots, selectedDots, topologyFaceSelection;
        private Vector3[] topologySelectedLines;
        private readonly HashSet<(int,int)> overlayEdges=new HashSet<(int,int)>();
        private readonly HashSet<int> overlaySelection = new HashSet<int>();
        private SelectionMode overlaySelectionMode;
        private void ClearTopologyDrawing()
        {
            workingWireBatch?.Dispose();selectedWireBatch?.Dispose();workingWireBatch=selectedWireBatch=null;
            foreach (var mesh in new[] { topologyDots, selectedDots, topologyFaceSelection })
                if (mesh) Object.DestroyImmediate(mesh);
            topologyDots=selectedDots=null; overlayPart=null; overlaySelection.Clear();
            topologyFaceSelection=null;topologySelectedLines=null;overlayEdges.Clear();
        }
        private Mesh TopologyPointBatch(IEnumerable<int> ids)
        {
            var positions=new List<Vector3>();var corners=new List<Vector2>();var indices=new List<int>();
            foreach(int id in ids)
            {
                if(id<0 || id>=Active.vertices.Count)continue;
                int start=positions.Count;
                for(int i=0;i<4;i++)positions.Add(Active.vertices[id]);
                corners.Add(new Vector2(-.5f,-.5f));corners.Add(new Vector2(-.5f,.5f));
                corners.Add(new Vector2(.5f,.5f));corners.Add(new Vector2(.5f,-.5f));
                indices.Add(start);indices.Add(start+1);indices.Add(start+2);indices.Add(start);indices.Add(start+2);indices.Add(start+3);
            }
            var mesh=new Mesh{name="Retopology vertex batch",hideFlags=HideFlags.HideAndDontSave,indexFormat=IndexFormat.UInt32};
            mesh.SetVertices(positions);mesh.SetUVs(0,corners);mesh.SetTriangles(indices,0);return mesh;
        }
        private bool DrawTopologyBatch(Mesh mesh,Color color,bool points,float size,CompareFunction depth)
        {
            if(!topologyOverlayMaterial)
            {
                var shader=Shader.Find("Hidden/MashBox/ModelerComponents");
                if(!shader || !shader.isSupported)return false;
                topologyOverlayMaterial=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
            }
            if(!mesh || mesh.vertexCount==0)return true;
            topologyOverlayMaterial.SetVector("_Viewport",new Vector4(view.camera.pixelWidth,view.camera.pixelHeight,0,0));
            topologyOverlayMaterial.SetColor("_Color",color);topologyOverlayMaterial.SetFloat("_Mode",points?1:0);
            topologyOverlayMaterial.SetFloat("_Size",size*EditorGUIUtility.pixelsPerPoint);
            topologyOverlayMaterial.SetFloat("_ZTest",(float)depth);
            if(!topologyOverlayMaterial.SetPass(0))return false;
            Graphics.DrawMeshNow(mesh,Matrix4x4.identity);return true;
        }
        private Mesh EnsureTopologyVertexBatch(bool highlighted)
        {
            if(overlayPart!=Active){ClearTopologyDrawing();overlayPart=Active;}
            if(!topologyDots)topologyDots=TopologyPointBatch(Enumerable.Range(0,Active.vertices.Count));
            if(highlighted && (overlaySelectionMode!=SelectionMode.Vertices || !overlaySelection.SetEquals(selected) || !selectedDots))
            {
                if(selectedDots)Object.DestroyImmediate(selectedDots);
                selectedDots=TopologyPointBatch(selected);overlaySelection.Clear();overlaySelection.UnionWith(selected);overlaySelectionMode=SelectionMode.Vertices;
            }
            return highlighted?selectedDots:topologyDots;
        }
        private bool DrawTopologyVertices(bool highlighted,Color tint,float size,CompareFunction depth)
        {
            var mesh=EnsureTopologyVertexBatch(highlighted);
            return DrawTopologyBatch(mesh,new Color(.02f,.03f,.03f,1),true,size+2,depth) && DrawTopologyBatch(mesh,tint,true,size,depth);
        }
        private bool DrawTopologySelectedSurfaces(Color tint,CompareFunction depth)
        {
            if(overlayPart!=Active){ClearTopologyDrawing();overlayPart=Active;}
            bool faces=selectionMode==SelectionMode.Faces;
            if(topologySelectedLines==null || overlaySelectionMode!=selectionMode ||
                (faces ? !overlaySelection.SetEquals(selectedFaces) : !overlayEdges.SetEquals(selectedEdges)))
            {
                if(topologyFaceSelection)Object.DestroyImmediate(topologyFaceSelection);
                topologyFaceSelection=null;
                selectedWireBatch?.Dispose();selectedWireBatch=null;
                var edges=new HashSet<(int,int)>();var triangles=new List<int>();
                if(faces)
                {
                    foreach(int face in selectedFaces)
                    {
                        if(face<0||face>=Active.faces.Count)continue;
                        var ids=Active.faces[face].vertices;
                        for(int i=1;i+1<ids.Length;i++){triangles.Add(ids[0]);triangles.Add(ids[i]);triangles.Add(ids[i+1]);}
                        for(int i=0;i<ids.Length;i++)edges.Add(StudioRetopologyOperations.Edge(ids[i],ids[(i+1)%ids.Length]));
                    }
                    topologyFaceSelection=new Mesh{name="Retopology selected faces",hideFlags=HideFlags.HideAndDontSave,indexFormat=IndexFormat.UInt32};
                    topologyFaceSelection.SetVertices(Active.vertices);topologyFaceSelection.SetTriangles(triangles,0);
                }
                else edges.UnionWith(selectedEdges);
                topologySelectedLines=edges.Where(e=>e.Item1>=0&&e.Item2<Active.vertices.Count)
                    .SelectMany(e=>new[]{Active.vertices[e.Item1],Active.vertices[e.Item2]}).ToArray();
                overlaySelection.Clear();overlaySelection.UnionWith(selectedFaces);overlayEdges.Clear();overlayEdges.UnionWith(selectedEdges);overlaySelectionMode=selectionMode;
            }
            if(faces && !DrawTopologyBatch(topologyFaceSelection,new Color(tint.r,tint.g,tint.b,StudioFaceSelectionStyle.SelectedOpacity),false,0,depth))return false;
            if(!DrawTopologyWire(ref selectedWireBatch,null,topologySelectedLines,tint,faces?2.5f:3.5f,depth))
            { Handles.color=tint;Handles.DrawLines(topologySelectedLines); }
            return true;
        }
    }
}
