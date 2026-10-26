using System;
using System.Collections.Generic;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private sealed class ComponentWireSubset : IDisposable
        {
            internal SelectionWireframe wire;
            internal readonly HashSet<int> components=new HashSet<int>();
            internal readonly List<int> edges=new List<int>();
            internal GraphicsBuffer buffer;
            public void Dispose(){buffer?.Dispose();buffer=null;wire=null;components.Clear();edges.Clear();}
        }
        private ComponentWireSubset selectedWireSubset=new ComponentWireSubset(),hoverWireSubset=new ComponentWireSubset();
        private void ReleaseComponentWireSubsets(){selectedWireSubset.Dispose();hoverWireSubset.Dispose();}
        private bool DrawComponentWireSubset(IEnumerable<int> components,Color color,float width)
        {
            if(!UseBatchedWireframes || !outlineCamera || SystemInfo.graphicsShaderLevel<45)return false;
            // The scene wire pass already prepared positions, including skinned meshes.
            // Reuse that cache rather than baking or uploading the same mesh again.
            var node=Document.nodes[Active];
            if(!selectionWireframes.TryGetValue(node,out var wire))
            {SelectionWireframeLines(Active,false);wire=selectionWireframes[node];}
            var subset=ReferenceEquals(components,Components)?selectedWireSubset:hoverWireSubset;
            if(!ReferenceEquals(subset.wire,wire)||!subset.components.SetEquals(components))
            {
                subset.wire=wire;subset.components.Clear();subset.components.UnionWith(components);subset.edges.Clear();
                var all=Edges();
                foreach(int id in subset.components)
                {
                    if(id<0||id>=all.Count)continue;
                    var edge=all[id];int a=wire.canonical[edge.a],b=wire.canonical[edge.b];
                    if(wire.edgeLookup.TryGetValue((Math.Min(a,b),Math.Max(a,b)),out int index))subset.edges.Add(index);
                }
                subset.edges.Sort();
                if(subset.edges.Count>0)
                {
                    if(subset.buffer==null||subset.buffer.count<subset.edges.Count)
                    {subset.buffer?.Dispose();subset.buffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,Mathf.NextPowerOfTwo(subset.edges.Count),4);}
                    subset.buffer.SetData(subset.edges,0,0,subset.edges.Count);
                }
            }
            return subset.edges.Count==0 || DrawWireBatch(wire,Transforms[Active].localToWorldMatrix,color,width,outlineCamera,subset.buffer,subset.edges.Count);
        }
    }
}
