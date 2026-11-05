using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private bool showNonPlanarQuadDiagonals;
        private void SetNonPlanarQuadDiagonals(bool enabled)
        {
            if(showNonPlanarQuadDiagonals==enabled)return;
            showNonPlanarQuadDiagonals=enabled;
            StudioPreferenceStore.SetFloat("Model.NonPlanarQuadDiagonals",enabled?1:0);
            repaint();
        }
        private sealed class QuadDiagonalOverlay
        {
            internal readonly List<int[]> triangles=new List<int[]>();
            internal readonly List<(int a,int b)> diagonals=new List<(int,int)>();
            internal readonly Vector3[][] lines=new Vector3[16][];
            internal readonly SelectionWireframe[] batches=new SelectionWireframe[16];
            internal Matrix4x4 matrix;
            internal bool dirty=true;
        }
        private QuadDiagonalOverlay CaptureQuadDiagonals(int index)
        {
            var node=Document.nodes[index];var overlay=new QuadDiagonalOverlay();
            // Exactly the render tessellation used by highlighting and tools.
            var topology=new StudioMeshCuts(node);var source=topology.BevelSourceTriangles(node.mesh);
            var visible=ComponentIsolation(index);
            for(int f=0;f<topology.Faces.Count;f++)
            {
                var boundary=topology.Faces[f].Vertices;var triangles=source[f];
                if(boundary.Count!=4||triangles.Count!=6||visible!=null&&!visible.faces.Contains(f))continue;
                int a=-1,b=-1;bool invalid=false;
                for(int i=0;i<3;i++)
                {
                    int v=triangles[i];bool shared=false;for(int j=3;j<6;j++)shared|=triangles[j]==v;
                    if(!shared)continue;if(a<0)a=v;else if(b<0&&v!=a)b=v;else invalid=true;
                }
                if(invalid||a<0||b<0)continue;
                int ia=boundary.IndexOf(a),ib=boundary.IndexOf(b);
                if(ia<0||ib<0||Math.Abs(ia-ib)!=2)continue;
                overlay.diagonals.Add((a,b));overlay.triangles.Add(triangles.ToArray());
            }
            return overlay;
        }
        internal static float QuadDiagonalOpacity(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 e,Vector3 f)
        {
            var first=Vector3.Cross(b-a,c-a);var second=Vector3.Cross(e-d,f-d);
            if(first.sqrMagnitude<=1e-30f||second.sqrMagnitude<=1e-30f)return 0;
            float cosine=Vector3.Dot(first/Mathf.Sqrt(first.sqrMagnitude),second/Mathf.Sqrt(second.sqrMagnitude));
            float angle=Mathf.Acos(Mathf.Clamp(cosine,-1,1))*Mathf.Rad2Deg;
            if(float.IsNaN(angle)||float.IsInfinity(angle)||angle<=.1f)return 0;
            return .35f+.55f*Mathf.Sqrt(Mathf.Clamp01(angle/25f));
        }
        private void DrawQuadDiagonals(int index,SelectionWireframe wire)
        {
            var overlay=wire.quadOverlay??(wire.quadOverlay=CaptureQuadDiagonals(index));
            var matrix=Transforms[index].localToWorldMatrix;
            if(overlay.dirty||overlay.matrix!=matrix)
            {
                var bins=new List<Vector3>[16];
                var supports=new List<Vector3>[16];
                for(int i=0;i<overlay.triangles.Count;i++)
                {
                    var t=overlay.triangles[i];var p=wire.vertices;
                    float opacity=QuadDiagonalOpacity(matrix.MultiplyPoint3x4(p[t[0]]),matrix.MultiplyPoint3x4(p[t[1]]),matrix.MultiplyPoint3x4(p[t[2]]),matrix.MultiplyPoint3x4(p[t[3]]),matrix.MultiplyPoint3x4(p[t[4]]),matrix.MultiplyPoint3x4(p[t[5]]));
                    if(opacity<=0)continue;int bin=Mathf.Clamp(Mathf.CeilToInt(opacity/.9f*16)-1,0,15);
                    if(bins[bin]==null)bins[bin]=new List<Vector3>();
                    var diagonal=overlay.diagonals[i];var a=matrix.MultiplyPoint3x4(p[diagonal.a]);var b=matrix.MultiplyPoint3x4(p[diagonal.b]);
                    bins[bin].Add(a);bins[bin].Add(b);
                    // The shared wire shader needs both supporting triangle planes
                    // to keep stroke pixels above inward and outward folds equally.
                    int oppositeA=-1,oppositeB=-1;
                    for(int j=0;j<3;j++)if(t[j]!=diagonal.a&&t[j]!=diagonal.b)oppositeA=t[j];
                    for(int j=3;j<6;j++)if(t[j]!=diagonal.a&&t[j]!=diagonal.b)oppositeB=t[j];
                    if(supports[bin]==null)supports[bin]=new List<Vector3>();
                    supports[bin].Add(a);supports[bin].Add(b);
                    supports[bin].Add(matrix.MultiplyPoint3x4(p[oppositeA]));supports[bin].Add(matrix.MultiplyPoint3x4(p[oppositeB]));
                }
                for(int i=0;i<16;i++)
                {
                    if(overlay.batches[i]!=null)ReleaseWireBatch(overlay.batches[i]);
                    overlay.lines[i]=bins[i]?.ToArray();overlay.batches[i]=null;
                    if(overlay.lines[i]==null||overlay.lines[i].Length==0)continue;
                    int count=overlay.lines[i].Length/2;
                    var batch=new SelectionWireframe{indices=new int[count*2],adjacent=new int[count*2]};
                    for(int j=0;j<count;j++)
                    {batch.indices[j*2]=j*4;batch.indices[j*2+1]=j*4+1;batch.adjacent[j*2]=j*4+3;batch.adjacent[j*2+1]=j*4+4;}
                    batch.vertices.AddRange(supports[i]);overlay.batches[i]=batch;
                }
                overlay.matrix=matrix;overlay.dirty=false;
            }
            using(new Handles.DrawingScope(Matrix4x4.identity))
                for(int bin=0;bin<16;bin++)if(overlay.lines[bin]!=null&&overlay.lines[bin].Length>0)
                {
                    float opacity=.9f*(bin+1)/16;
                    float width=2f+1.25f*Mathf.Clamp01((opacity-.35f)/.55f);
                    var color=new Color(1,.2f,.13f,opacity);
                    if(UseBatchedWireframes&&DrawWireBatch(overlay.batches[bin],Matrix4x4.identity,color,width,outlineCamera))continue;
                    Handles.color=color;
                    for(int j=0;j<overlay.lines[bin].Length;j+=2){outlineSegment[0]=overlay.lines[bin][j];outlineSegment[1]=overlay.lines[bin][j+1];Handles.DrawAAPolyLine(width,outlineSegment);}
                }
        }
    }
}

