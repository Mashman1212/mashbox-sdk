using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    // Cached finite-level OpenSubdiv topology and factorized stencils. Input
    // connectivity is explicit: this class never welds vertices by position.
    internal sealed class StudioSubdivisionPlan
    {
        private const string Library = "StudioOpenSubdiv";
        // Reserved ABI slots remain zero so already-loaded native libraries stay callable.
        [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr StudioSubdivCreate(
            int vertices,int faces,int cornerCount,int[] counts,int[] corners,int attributeCount,int[] attributes,
            int reservedCount,IntPtr reservedIndices,IntPtr reservedWeights,int level,int boundary);
        [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] private static extern void StudioSubdivDestroy(IntPtr plan);
        [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr StudioSubdivError();
        [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr StudioSubdivInts(IntPtr plan,int field,out int count);
        [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr StudioSubdivWeights(IntPtr plan,int channel,out int count);
        internal readonly int[] PositionOffsets,PositionIndices,AttributeOffsets,AttributeIndices,Corners,Values,Parents,SharpEdges;
        internal readonly float[] PositionWeights,AttributeWeights;
        internal readonly int[] NormalOffsets,NormalFaces;
        internal int VertexCount => PositionOffsets.Length-1;
        internal int FaceCount => Parents.Length;

        internal StudioSubdivisionPlan(int vertexCount,int[] counts,int[] corners,int attributeCount,int[] values,
            int level,int boundary)
        {
            if(counts==null || corners==null || values==null || corners.Length!=values.Length)
                throw new ArgumentException("Incomplete subdivision input.");
            IntPtr plan=StudioSubdivCreate(vertexCount,counts.Length,corners.Length,counts,corners,attributeCount,values,
                0,IntPtr.Zero,IntPtr.Zero,level,boundary);
            if(plan==IntPtr.Zero) throw new InvalidOperationException(Marshal.PtrToStringAnsi(StudioSubdivError()));
            try
            {
                PositionOffsets=Ints(plan,0); PositionIndices=Ints(plan,1);
                AttributeOffsets=Ints(plan,2); AttributeIndices=Ints(plan,3);
                Corners=Ints(plan,4); Values=Ints(plan,5); Parents=Ints(plan,6); SharpEdges=Ints(plan,7);
                PositionWeights=Weights(plan,0); AttributeWeights=Weights(plan,1);
            }
            finally { StudioSubdivDestroy(plan); }
            BuildNormalAdjacency(out NormalOffsets,out NormalFaces);
        }
        private static int[] Ints(IntPtr plan,int field)
        { var p=StudioSubdivInts(plan,field,out int count); var a=new int[count]; if(count>0)Marshal.Copy(p,a,0,count); return a; }
        private static float[] Weights(IntPtr plan,int channel)
        { var p=StudioSubdivWeights(plan,channel,out int count); var a=new float[count]; if(count>0)Marshal.Copy(p,a,0,count); return a; }
        private static (int,int) Key(int a,int b) => a<b?(a,b):(b,a);
        private void BuildNormalAdjacency(out int[] offsets,out int[] faces)
        {
            // Each corner starts as a normal island. Join only across an actual
            // shared, smooth refined edge, independently of UV/normal seams.
            var root=new int[Corners.Length]; for(int i=0;i<root.Length;i++)root[i]=i;
            int Find(int i) { while(root[i]!=i) { root[i]=root[root[i]];i=root[i]; } return i; }
            void Join(int a,int b) { root[Find(a)]=Find(b); }
            int Next(int i) => (i/4)*4+(i+1)%4;
            var sharp=new HashSet<(int,int)>();
            for(int i=0;i<SharpEdges.Length;i+=2)sharp.Add(Key(SharpEdges[i],SharpEdges[i+1]));
            var uses=new Dictionary<(int,int),List<int>>();
            for(int i=0;i<Corners.Length;i++)
            { var k=Key(Corners[i],Corners[Next(i)]); if(!uses.TryGetValue(k,out var list))uses[k]=list=new List<int>(2);list.Add(i); }
            foreach(var entry in uses)
            {
                if(entry.Value.Count!=2 || sharp.Contains(entry.Key))continue;
                int a=entry.Value[0],b=entry.Value[1];
                if(Corners[a]==Corners[Next(b)] && Corners[Next(a)]==Corners[b]) { Join(a,Next(b));Join(Next(a),b); }
            }
            var groups=new Dictionary<int,List<int>>();
            for(int i=0;i<root.Length;i++) { int r=Find(i);if(!groups.TryGetValue(r,out var list))groups[r]=list=new List<int>(); if(!list.Contains(i/4))list.Add(i/4); }
            offsets=new int[root.Length+1];var all=new List<int>();
            for(int i=0;i<root.Length;i++) { offsets[i]=all.Count;all.AddRange(groups[Find(i)]); }
            offsets[root.Length]=all.Count;faces=all.ToArray();
        }
        internal Vector3[] Evaluate(Vector3[] cage)
        {
            var result=new Vector3[VertexCount];
            for(int i=0;i<result.Length;i++) for(int j=PositionOffsets[i];j<PositionOffsets[i+1];j++)
                result[i]+=cage[PositionIndices[j]]*PositionWeights[j];
            return result;
        }
        internal Vector4[] EvaluateAttributes(IList<Vector4> source)
        {
            var result=new Vector4[AttributeOffsets.Length-1];
            for(int i=0;i<result.Length;i++) for(int j=AttributeOffsets[i];j<AttributeOffsets[i+1];j++)
                result[i]+=source[AttributeIndices[j]]*AttributeWeights[j];
            return result;
        }
        internal void Surface(Vector3[] cage,out Vector3[] positions,out Vector3[] normals)
        {
            var p=Evaluate(cage);var faceNormals=new Vector3[FaceCount];
            for(int f=0;f<FaceCount;f++)
            {
                int c=f*4;var a=p[Corners[c]];var b=p[Corners[c+1]];var e=p[Corners[c+2]];var d=p[Corners[c+3]];
                faceNormals[f]=Vector3.Cross(b-a,e-a)+Vector3.Cross(e-a,d-a);
            }
            positions=new Vector3[Corners.Length];normals=new Vector3[Corners.Length];
            for(int i=0;i<Corners.Length;i++)
            {
                positions[i]=p[Corners[i]];var n=Vector3.zero;
                for(int j=NormalOffsets[i];j<NormalOffsets[i+1];j++)n+=faceNormals[NormalFaces[j]];
                normals[i]=n.sqrMagnitude>1e-24f?n/Mathf.Sqrt(n.sqrMagnitude):Vector3.up;
            }
        }
    }
}
