using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor
    {
        private bool showSelectedWireframe = true;
        private Mesh wireSource, wireBaked;
        private int[] wireEdges;
        private Vector3[] wireLines;
        private bool wirePoseDirty=true;
        private GameObject wireObject;
        private Matrix4x4[] wireBonePose;
        private readonly List<Vector3> wireVertices=new List<Vector3>();
        private Vector3[] stitchMarkerLines;
        private void DrawWireframeButton(Rect rect)
        {
            bool cullingWire=CullingWireframeOnly;
            bool showWire=cullingWire || showSelectedWireframe;
            if(DrawSkinModeButton(rect,false,cullingWire ? "Culling shows the selected clothing as wireframe" : "Toggle quad-style wireframe on the selected mesh") && !cullingWire)
            { showSelectedWireframe=!showSelectedWireframe; repaint(); }
            if(Event.current.type!=EventType.Repaint) return;
            var color=Handles.color; var matrix=Handles.matrix;
            try
            {
                Handles.matrix=Matrix4x4.identity;
                Handles.color=showWire ? new Color(.5f,1,.87f) : new Color(.75f,.81f,.81f);
                Vector3 P(float x,float y)=>new Vector3(rect.center.x+x,rect.center.y+y,0);
                Handles.DrawAAPolyLine(1.4f,P(-9,-9),P(9,-9),P(9,9),P(-9,9),P(-9,-9));
                Handles.DrawAAPolyLine(1.4f,P(0,-9),P(0,9));
                Handles.DrawAAPolyLine(1.4f,P(-9,0),P(9,0));
                if(showWire) EditorGUI.DrawRect(new Rect(rect.x+5,rect.yMax-3,rect.width-10,2),new Color(.5f,1,.87f));
            }
            finally { Handles.color=color; Handles.matrix=matrix; }
        }
        private void DrawSelectedMeshWireframe()
        {
            if(Event.current.type!=EventType.Repaint || Part==null || !Part.visible || !CanPaintPart(Part) || !Part.mesh) return;
            bool showStitches = !sculptMode && (Tool==Brush.Paint || Tool==Brush.SmoothWeights) && Part.HasStitchWeightLock;
            bool showWire=CullingWireframeOnly || showSelectedWireframe;
            if(!showWire && !showStitches) return;
            if(Document.selected<0 || Document.selected>=objects.Count) return;
            if(wireSource!=Part.mesh)
            {
                wireSource=Part.mesh;
                var diagonals=QuadWireDiagonals(wireSource);
                var edges=new HashSet<ulong>(); var list=new List<int>();
                void Edge(int a,int b)
                {
                    if(a>b) { int t=a;a=b;b=t; }
                    ulong key=((ulong)(uint)a<<32)|(uint)b;
                    if(a==b || diagonals.Contains(key) || !edges.Add(key)) return;
                    list.Add(a);list.Add(b);
                }
                var triangles=wireSource.triangles;
                for(int i=0;i<triangles.Length;i+=3) { Edge(triangles[i],triangles[i+1]);Edge(triangles[i+1],triangles[i+2]);Edge(triangles[i+2],triangles[i]); }
                wireEdges=list.ToArray();wireLines=new Vector3[wireEdges.Length];
                wirePoseDirty=true;
            }
            var go=objects[Document.selected];
            if(wireObject!=go){wireObject=go;wirePoseDirty=true;}
            // Manual posing also changes bones. Comparing this small rig avoids
            // re-baking every vertex while still catching external pose changes.
            if(Part.bound)
            {
                if(wireBonePose==null || wireBonePose.Length!=bones.Length)
                { wireBonePose=new Matrix4x4[bones.Length];wirePoseDirty=true; }
                for(int i=0;i<bones.Length;i++)
                {
                    var pose=bones[i].localToWorldMatrix;
                    if(!wireBonePose[i].Equals(pose)){wireBonePose[i]=pose;wirePoseDirty=true;}
                }
            }
            if(wirePoseDirty)
            {
                var skin=go.GetComponent<SkinnedMeshRenderer>();
                if(skin)
                {
                    if(!wireBaked) wireBaked=new Mesh { name="Skinning wireframe pose",hideFlags=HideFlags.HideAndDontSave };
                    skin.BakeMesh(wireBaked);wireBaked.GetVertices(wireVertices);
                }
                else displayMeshes[Document.selected].GetVertices(wireVertices);
                for(int i=0;i<wireEdges.Length;i++) wireLines[i]=wireVertices[wireEdges[i]];
                wirePoseDirty=false;
                }
            var oldZ=Handles.zTest;
            try
            {
                Handles.zTest=CompareFunction.LessEqual;
                if(showWire)
                    using(new Handles.DrawingScope(CullingWireframeOnly ? new Color(.5f,1,.87f,.85f) : new Color(.06f,.12f,.12f,.8f),go.transform.localToWorldMatrix))
                        Handles.DrawLines(wireLines);
                if(showStitches) DrawStitchWeightMarkers(go.transform);
            }
            finally { Handles.zTest=oldZ; }
        }
        private void DrawStitchWeightMarkers(Transform meshTransform)
        {
            if(!lastView || !lastView.camera) return;
            const int segments=12;
            int count=Part.stitchedVertices.Length*segments*2;
            if(stitchMarkerLines==null || stitchMarkerLines.Length!=count) stitchMarkerLines=new Vector3[count];
            var camera=lastView.camera.transform;
            var right=camera.right; var up=camera.up;
            int next=0;
            foreach(int vertex in Part.stitchedVertices)
            {
                var center=meshTransform.TransformPoint(wireVertices[vertex]);
                float radius=HandleUtility.GetHandleSize(center)*.035f;
                // Lift the small screen-facing ring off the surface to avoid
                // z-fighting, while retaining normal depth occlusion.
                center-=camera.forward*(radius*.2f);
                for(int i=0;i<segments;i++)
                {
                    float a=i*Mathf.PI*2/segments, b=(i+1)*Mathf.PI*2/segments;
                    stitchMarkerLines[next++]=center+(right*Mathf.Cos(a)+up*Mathf.Sin(a))*radius;
                    stitchMarkerLines[next++]=center+(right*Mathf.Cos(b)+up*Mathf.Sin(b))*radius;
                }
            }
            using(new Handles.DrawingScope(Color.white,Matrix4x4.identity))
                Handles.DrawLines(stitchMarkerLines);
        }
        private void DisposeWireframe()
        {
            if(wireBaked) Object.DestroyImmediate(wireBaked);
            wireSource=null;wireEdges=null;wireLines=null;wireVertices.Clear();
            wireObject=null;wirePoseDirty=true;
            wireBonePose=null;
            stitchMarkerLines=null;
        }
    }
}
