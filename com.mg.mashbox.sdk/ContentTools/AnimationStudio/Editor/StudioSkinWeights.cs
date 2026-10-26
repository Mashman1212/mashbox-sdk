using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    // Keep variable-length influences: importing/sculpting never truncates to four.
    internal sealed class StudioSkinWeights
    {
        internal readonly List<BoneWeight1>[] Values;
        internal bool HasWeights => Values.Any(v => v.Count > 0);
        internal StudioSkinWeights(Mesh mesh)
        {
            Values = new List<BoneWeight1>[mesh.vertexCount];
            using (var counts = mesh.GetBonesPerVertex())
            using (var all = mesh.GetAllBoneWeights())
            {
                int offset = 0;
                for (int i = 0; i < Values.Length; i++)
                {
                    Values[i] = new List<BoneWeight1>(4);
                    int count = counts.Length == Values.Length ? counts[i] : 0;
                    for (int k = 0; k < count; k++) Values[i].Add(all[offset++]);
                }
            }
        }
        internal float Weight(int vertex, int bone)
        { foreach (var w in Values[vertex]) if (w.boneIndex == bone) return w.weight; return 0; }
        internal void Remap(int[] map)
        {
            foreach (var list in Values) for (int i = 0; i < list.Count; i++)
            { var w = list[i]; w.boneIndex = map[w.boneIndex]; list[i] = w; }
        }
        internal void Set(int vertex, int bone, float value, bool[] locked)
        {
            if (locked[bone]) return;
            var list = Values[vertex]; float fixedWeight = 0, other = 0;
            foreach (var w in list) if (locked[w.boneIndex]) fixedWeight += w.weight; else if (w.boneIndex != bone) other += w.weight;
            value = Mathf.Clamp(value, 0, Mathf.Max(0, 1 - fixedWeight));
            // Without another unlocked influence there is nowhere to redistribute.
            if (other < 1e-8f) value = Mathf.Max(0, 1 - fixedWeight);
            float remaining = Mathf.Max(0, 1 - fixedWeight - value);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var w = list[i]; if (locked[w.boneIndex]) continue;
                if (w.boneIndex == bone) { list.RemoveAt(i); continue; }
                w.weight = other > 0 ? w.weight * remaining / other : 0;
                if (w.weight <= 1e-8f) list.RemoveAt(i); else list[i] = w;
            }
            if (value > 1e-8f) list.Add(new BoneWeight1 { boneIndex = bone, weight = value });
        }
        internal void LimitToFour()
        {
            for (int i = 0; i < Values.Length; i++)
            {
                var kept = Values[i].Where(w => w.weight > 0)
                    .OrderByDescending(w => w.weight).ThenBy(w => w.boneIndex).Take(4).ToList();
                float sum = kept.Sum(w => w.weight);
                for (int k = 0; k < kept.Count; k++)
                { var w = kept[k]; w.weight /= sum; kept[k] = w; }
                Values[i] = kept;
            }
        }
        internal void Apply(Mesh mesh)
        {
            var counts = new NativeArray<byte>(Values.Length, Allocator.Temp);
            var weights = new NativeArray<BoneWeight1>(Values.Sum(v => v.Count), Allocator.Temp);
            try
            {
                int offset = 0;
                for (int i = 0; i < Values.Length; i++)
                {
                    Values[i].Sort((a,b) => b.weight.CompareTo(a.weight));
                    if (Values[i].Count > 255) throw new InvalidOperationException("A vertex exceeds Unity's 255 influence limit.");
                    counts[i] = (byte)Values[i].Count;
                    foreach (var w in Values[i]) weights[offset++] = w;
                }
                mesh.SetBoneWeights(counts, weights);
            }
            finally { counts.Dispose(); weights.Dispose(); }
        }
        internal Matrix4x4 SkinMatrix(int vertex, Matrix4x4[] matrices)
        {
            if (Values[vertex].Count == 0) return Matrix4x4.identity;
            var result = new Matrix4x4();
            foreach (var w in Values[vertex])
            { var m = matrices[w.boneIndex]; for (int i = 0; i < 16; i++) result[i] += m[i] * w.weight; }
            return result;
        }
        internal Matrix4x4 PreviewSkinMatrix(int vertex, Matrix4x4[] matrices)
        {
            // Match export's strongest-four ordering without allocating or
            // throwing away influences in the editable document.
            Span<BoneWeight1> kept = stackalloc BoneWeight1[4];
            int count=0;
            foreach(var weight in Values[vertex])
            {
                if(weight.weight<=0)continue;
                int index=0;
                while(index<count && (kept[index].weight>weight.weight ||
                    (kept[index].weight==weight.weight && kept[index].boneIndex<weight.boneIndex)))index++;
                if(index>=4)continue;
                int last=Math.Min(count,3);
                for(int i=last;i>index;i--)kept[i]=kept[i-1];
                kept[index]=weight;count=Math.Min(4,count+1);
            }
            if(count==0)return Matrix4x4.identity;
            float sum=0;for(int i=0;i<count;i++)sum+=kept[i].weight;
            var result=new Matrix4x4();
            for(int i=0;i<count;i++)
            {
                var matrix=matrices[kept[i].boneIndex];float weight=kept[i].weight/sum;
                for(int k=0;k<16;k++)result[k]+=matrix[k]*weight;
            }
            return result;
        }
    }

    // Brush queries touch only nearby cells. The index is built once per stroke;
    // grab keeps that initial membership and falloff for the entire gesture.
    internal sealed class StudioSkinSpatialIndex
    {
        private readonly Dictionary<Vector3Int, List<int>> cells = new Dictionary<Vector3Int, List<int>>();
        private readonly float size;
        internal StudioSkinSpatialIndex(Vector3[] points, float cellSize)
        {
            size = Mathf.Max(.001f, cellSize);
            for (int i = 0; i < points.Length; i++)
            { var key = Cell(points[i]); if (!cells.TryGetValue(key, out var list)) cells.Add(key, list = new List<int>()); list.Add(i); }
        }
        private Vector3Int Cell(Vector3 p) => new Vector3Int(Mathf.FloorToInt(p.x/size), Mathf.FloorToInt(p.y/size), Mathf.FloorToInt(p.z/size));
        internal void Query(Vector3 center, float radius, List<int> result)
        {
            result.Clear(); var a = Cell(center - Vector3.one * radius); var b = Cell(center + Vector3.one * radius);
            for (int x = a.x; x <= b.x; x++) for (int y = a.y; y <= b.y; y++) for (int z = a.z; z <= b.z; z++)
                if (cells.TryGetValue(new Vector3Int(x,y,z), out var list)) result.AddRange(list);
        }
    }
}
