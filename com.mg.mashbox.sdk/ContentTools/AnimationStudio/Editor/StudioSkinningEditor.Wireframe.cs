using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor
    {
        private bool showSelectedWireframe;
        private Mesh wireSource, wireBaked;
        private int[] wireEdges;
        private Vector3[] wireLines;
        private readonly List<Vector3> wireVertices=new List<Vector3>();
        private void DrawWireframeButton(Rect rect)
        {
            if(DrawSkinModeButton(rect,false,"Toggle quad-style wireframe on the selected mesh"))
            { showSelectedWireframe=!showSelectedWireframe; repaint(); }
            if(Event.current.type!=EventType.Repaint) return;
            var color=Handles.color; var matrix=Handles.matrix;
            try
            {
                Handles.matrix=Matrix4x4.identity;
                Handles.color=showSelectedWireframe ? new Color(.5f,1,.87f) : new Color(.75f,.81f,.81f);
                Vector3 P(float x,float y)=>new Vector3(rect.center.x+x,rect.center.y+y,0);
                Handles.DrawAAPolyLine(1.4f,P(-9,-9),P(9,-9),P(9,9),P(-9,9),P(-9,-9));
                Handles.DrawAAPolyLine(1.4f,P(0,-9),P(0,9));
                Handles.DrawAAPolyLine(1.4f,P(-9,0),P(9,0));
                if(showSelectedWireframe) EditorGUI.DrawRect(new Rect(rect.x+5,rect.yMax-3,rect.width-10,2),new Color(.5f,1,.87f));
            }
            finally { Handles.color=color; Handles.matrix=matrix; }
        }
        private void DrawSelectedMeshWireframe()
        {
            if(!showSelectedWireframe || Event.current.type!=EventType.Repaint || Part==null || !Part.visible || Part.referenceOnly || !Part.mesh) return;
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
            }
            var go=objects[Document.selected];
            var skin=go.GetComponent<SkinnedMeshRenderer>();
            if(skin)
            {
                if(!wireBaked) wireBaked=new Mesh { name="Skinning wireframe pose",hideFlags=HideFlags.HideAndDontSave };
                skin.BakeMesh(wireBaked);wireBaked.GetVertices(wireVertices);
            }
            else displayMeshes[Document.selected].GetVertices(wireVertices);
            for(int i=0;i<wireEdges.Length;i++) wireLines[i]=wireVertices[wireEdges[i]];
            var oldZ=Handles.zTest;
            try
            {
                Handles.zTest=CompareFunction.LessEqual;
                using(new Handles.DrawingScope(new Color(.06f,.12f,.12f,.8f),go.transform.localToWorldMatrix))
                    Handles.DrawLines(wireLines);
            }
            finally { Handles.zTest=oldZ; }
        }
        private void DisposeWireframe()
        {
            if(wireBaked) Object.DestroyImmediate(wireBaked);
            wireSource=null;wireEdges=null;wireLines=null;wireVertices.Clear();
        }
    }
}
