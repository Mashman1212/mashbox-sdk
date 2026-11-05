using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    // Transport captured UV charts with changing face planes. Stationary corners
    // anchor texture placement; source triangle scale/shear is retained as faces tilt.
    internal sealed class StudioMoveUVPreservation
    {
        private sealed class Channel
        {
            internal int Index,Dimension;internal Vector4[] Source,Correction;
            internal List<Vector4> Working;internal List<Vector2> Two;internal List<Vector3> Three;internal bool Dirty;
        }
        private struct Chart
        {
            internal int A,B,C,Anchor,SlotA,SlotB,SlotC;
            internal bool FixedAnchor;internal Vector3 Normal,Dual1,Dual2;internal float Weight;
        }
        private readonly int[] indices;private readonly Vector3[] source;
        private readonly Channel[] channels;private readonly Chart[] charts;private readonly float[] weights;
        private StudioMoveUVPreservation(Mesh mesh,Vector3[] positions,int[] selected)
        {
            source=positions;indices=selected;weights=new float[selected.Length];
            var list=new List<Channel>();
            for(int c=0;c<8;c++)
            {
                var attribute=(VertexAttribute)((int)VertexAttribute.TexCoord0+c);if(!mesh.HasVertexAttribute(attribute))continue;
                var values=new List<Vector4>();mesh.GetUVs(c,values);
                if(values.Count!=positions.Length)throw new InvalidOperationException("Preserve UVs requires complete UV vertex streams.");
                var channel=new Channel{Index=c,Dimension=mesh.GetVertexAttributeDimension(attribute),Source=values.ToArray(),Working=values,Correction=new Vector4[selected.Length]};
                if(channel.Dimension==2)channel.Two=values.Select(v=>new Vector2(v.x,v.y)).ToList();
                if(channel.Dimension==3)channel.Three=values.Select(v=>new Vector3(v.x,v.y,v.z)).ToList();list.Add(channel);
            }
            channels=list.ToArray();var captured=new List<Chart>();
            if(channels.Length==0){charts=Array.Empty<Chart>();return;}
            var slots=new int[positions.Length];for(int i=0;i<slots.Length;i++)slots[i]=-1;for(int i=0;i<indices.Length;i++)slots[indices[i]]=i;
            var triangles=mesh.triangles;
            for(int t=0;t<triangles.Length;t+=3)
            {
                int a=triangles[t],b=triangles[t+1],c=triangles[t+2];if(slots[a]<0&&slots[b]<0&&slots[c]<0)continue;
                var e1=positions[b]-positions[a];var e2=positions[c]-positions[a];var n=Vector3.Cross(e1,e2);float determinant=n.sqrMagnitude;
                if(!Finite(determinant)||determinant<=Mathf.Max(1e-30f,e1.sqrMagnitude*e2.sqrMagnitude*1e-12f))continue;
                int anchor=slots[a]<0?a:slots[b]<0?b:slots[c]<0?c:a;float weight=Mathf.Sqrt(determinant);
                captured.Add(new Chart{A=a,B=b,C=c,Anchor=anchor,FixedAnchor=slots[anchor]<0,SlotA=slots[a],SlotB=slots[b],SlotC=slots[c],Normal=n,Weight=weight,Dual1=Vector3.Cross(e2,n)/determinant,Dual2=Vector3.Cross(n,e1)/determinant});
                for(int corner=0;corner<3;corner++){int v=corner==0?a:corner==1?b:c;if(slots[v]>=0)weights[slots[v]]+=weight;}
                foreach(var channel in channels)if(!Finite(channel.Source[a])||!Finite(channel.Source[b])||!Finite(channel.Source[c]))throw new InvalidOperationException("Preserve UVs requires finite source UV coordinates.");
            }
            charts=captured.ToArray();
        }
        internal static StudioMoveUVPreservation Capture(Mesh mesh,Vector3[] positions,int[] selected)
        {var plan=new StudioMoveUVPreservation(mesh,positions,selected);return plan.channels.Length==0?null:plan;}
        internal void Evaluate(Vector3[] positions)
        {
            bool unchanged=true;foreach(int v in indices)if(!positions[v].Equals(source[v])){unchanged=false;break;}
            foreach(var channel in channels){channel.Dirty=false;Array.Clear(channel.Correction,0,channel.Correction.Length);}
            if(!unchanged)foreach(var chart in charts)
            {
                var n=Vector3.Cross(positions[chart.B]-positions[chart.A],positions[chart.C]-positions[chart.A]);
                if(!Finite(n.x)||!Finite(n.y)||!Finite(n.z)||!Finite(n.sqrMagnitude))throw new InvalidOperationException("Preserve UVs requires a finite moved surface.");
                // Degenerate moved triangles have no unique frame; keep the old
                // planar projection until the artist moves away from collapse.
                var inverse=n.sqrMagnitude>1e-30f?Quaternion.FromToRotation(n,chart.Normal):Quaternion.identity;
                var origin=chart.FixedAnchor?positions[chart.Anchor]:source[chart.Anchor];
                for(int corner=0;corner<3;corner++)
                {
                    int slot=corner==0?chart.SlotA:corner==1?chart.SlotB:chart.SlotC;if(slot<0)continue;
                    int v=corner==0?chart.A:corner==1?chart.B:chart.C;
                    var delta=inverse*(positions[v]-origin)-(source[v]-source[chart.Anchor]);
                    float first=Vector3.Dot(chart.Dual1,delta)*chart.Weight,second=Vector3.Dot(chart.Dual2,delta)*chart.Weight;
                    foreach(var channel in channels)channel.Correction[slot]+=(channel.Source[chart.B]-channel.Source[chart.A])*first+(channel.Source[chart.C]-channel.Source[chart.A])*second;
                }
            }
            foreach(var channel in channels)for(int i=0;i<indices.Length;i++)
            {
                int v=indices[i];var uv=channel.Source[v]+(weights[i]>0?channel.Correction[i]/weights[i]:Vector4.zero);
                if(!Finite(uv))throw new InvalidOperationException("Preserve UVs produced non-finite coordinates; movement was not applied.");
                if(!channel.Working[v].Equals(uv)){channel.Working[v]=uv;channel.Dirty=true;}
            }
        }
        internal void Publish(Mesh mesh)
        {
            foreach(var channel in channels)
            {
                if(!channel.Dirty)continue;var values=channel.Working;
                if(channel.Dimension==1)StudioSubdivisionData.SetUV1(mesh,channel.Index,values);
                else if(channel.Dimension==2){foreach(int v in indices)channel.Two[v]=new Vector2(values[v].x,values[v].y);mesh.SetUVs(channel.Index,channel.Two);}
                else if(channel.Dimension==3){foreach(int v in indices)channel.Three[v]=new Vector3(values[v].x,values[v].y,values[v].z);mesh.SetUVs(channel.Index,channel.Three);}
                else mesh.SetUVs(channel.Index,values);channel.Dirty=false;
            }
        }
        private static bool Finite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f);
        private static bool Finite(Vector4 v)=>Finite(v.x)&&Finite(v.y)&&Finite(v.z)&&Finite(v.w);
    }
}
