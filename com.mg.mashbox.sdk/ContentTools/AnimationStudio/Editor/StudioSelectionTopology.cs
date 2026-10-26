using System;using System.Collections.Generic;using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    // Dense adjacency is captured once from authored polygons, never render diagonals.
    // Compressed offsets avoid one managed list/array allocation per vertex and edge.
    internal sealed class StudioSelectionTopology
    {
        internal readonly int[] Canonical,RenderEdges;
        private readonly (int a,int b)[] edges;
        private readonly Dictionary<(int,int),int> lookup;
        private readonly int[] vertexOffsets,vertexIds,incidentOffsets,incidentEdges;
        private readonly int[] faceOffsets,faceEdges,edgeFaceOffsets,edgeFaces,aliasOffsets,aliases,next;
        private static (int,int) Key(int a,int b)=>a<b?(a,b):(b,a);
        private static int[] Offsets(int[] counts)
        {
            var result=new int[counts.Length+1];
            for(int i=0;i<counts.Length;i++)result[i+1]=result[i]+counts[i];return result;
        }
        internal StudioSelectionTopology(IList<Vector3> positions,IList<StudioMeshCuts.Face> faces,IList<(int a,int b)> renderEdges,bool weld)
        {
            Canonical=new int[positions.Count];var map=weld?new Dictionary<Vector3,int>(positions.Count):null;int vertices=0;
            for(int i=0;i<positions.Count;i++)
            {
                int id;if(!weld||!map.TryGetValue(positions[i],out id)){id=vertices++;if(weld)map.Add(positions[i],id);}Canonical[i]=id;
            }
            var counts=new int[vertices];foreach(int id in Canonical)counts[id]++;
            vertexOffsets=Offsets(counts);vertexIds=new int[positions.Count];Array.Clear(counts,0,counts.Length);
            for(int i=0;i<Canonical.Length;i++){int id=Canonical[i];vertexIds[vertexOffsets[id]+counts[id]++]=i;}
            faceOffsets=new int[faces.Count+1];for(int f=0;f<faces.Count;f++)faceOffsets[f+1]=faceOffsets[f]+faces[f].Vertices.Count;
            faceEdges=new int[faceOffsets[faces.Count]];lookup=new Dictionary<(int,int),int>(renderEdges.Count);var built=new List<(int,int)>(renderEdges.Count);
            for(int f=0;f<faces.Count;f++)
            {
                var v=faces[f].Vertices;
                for(int i=0;i<v.Count;i++)
                {
                    var key=Key(Canonical[v[i]],Canonical[v[(i+1)%v.Count]]);int e=-1;
                    if(key.Item1!=key.Item2&&!lookup.TryGetValue(key,out e)){e=built.Count;lookup.Add(key,e);built.Add(key);}
                    faceEdges[faceOffsets[f]+i]=e;
                }
            }
            edges=built.ToArray();var faceCounts=new int[edges.Length];var stamp=new int[edges.Length];
            for(int f=0;f<faces.Count;f++)for(int i=faceOffsets[f];i<faceOffsets[f+1];i++)
            {int e=faceEdges[i];if(e>=0&&stamp[e]!=f+1){stamp[e]=f+1;faceCounts[e]++;}}
            edgeFaceOffsets=Offsets(faceCounts);edgeFaces=new int[edgeFaceOffsets[edges.Length]];Array.Clear(faceCounts,0,faceCounts.Length);Array.Clear(stamp,0,stamp.Length);
            for(int f=0;f<faces.Count;f++)for(int i=faceOffsets[f];i<faceOffsets[f+1];i++)
            {int e=faceEdges[i];if(e>=0&&stamp[e]!=f+1){stamp[e]=f+1;edgeFaces[edgeFaceOffsets[e]+faceCounts[e]++]=f;}}
            RenderEdges=new int[renderEdges.Count];var aliasCounts=new int[edges.Length];
            for(int i=0;i<renderEdges.Count;i++)
            {
                var key=Key(Canonical[renderEdges[i].a],Canonical[renderEdges[i].b]);
                if(key.Item1==key.Item2||!lookup.TryGetValue(key,out int e))RenderEdges[i]=-1;
                else{RenderEdges[i]=e;aliasCounts[e]++;}
            }
            aliasOffsets=Offsets(aliasCounts);aliases=new int[aliasOffsets[edges.Length]];Array.Clear(aliasCounts,0,aliasCounts.Length);
            for(int i=0;i<RenderEdges.Length;i++){int e=RenderEdges[i];if(e>=0)aliases[aliasOffsets[e]+aliasCounts[e]++]=i;}
            Array.Clear(counts,0,counts.Length);foreach(var edge in edges){counts[edge.a]++;counts[edge.b]++;}
            incidentOffsets=Offsets(counts);incidentEdges=new int[edges.Length*2];Array.Clear(counts,0,counts.Length);
            for(int e=0;e<edges.Length;e++){var edge=edges[e];incidentEdges[incidentOffsets[edge.a]+counts[edge.a]++]=e;incidentEdges[incidentOffsets[edge.b]+counts[edge.b]++]=e;}
            next=new int[edges.Length*2];for(int i=0;i<next.Length;i++)next[i]=-1;
            for(int v=0;v<vertices;v++)
            {
                int begin=incidentOffsets[v],end=incidentOffsets[v+1];bool regular=end-begin==4,blocked=false;int boundaryA=-1,boundaryB=-1,boundaryCount=0;
                for(int j=begin;j<end;j++)
                {
                    int e=incidentEdges[j],n=FaceCount(e);blocked|=n>2;regular&=n==2;
                    if(n==1){if(boundaryCount==0)boundaryA=e;else if(boundaryCount==1)boundaryB=e;boundaryCount++;}
                    for(int f=edgeFaceOffsets[e];f<edgeFaceOffsets[e+1];f++)regular&=FaceSize(edgeFaces[f])==4;
                }
                if(blocked)continue;
                for(int j=begin;j<end;j++)
                {
                    int e=incidentEdges[j],candidate=-1,count=0;
                    if(FaceCount(e)==1&&boundaryCount==2){candidate=e==boundaryA?boundaryB:boundaryA;count=1;}
                    else if(regular)for(int k=begin;k<end;k++){int other=incidentEdges[k];if(other!=e&&!SharesFace(e,other)){candidate=other;count++;}}
                    if(count==1)next[e*2+(edges[e].a==v?0:1)]=candidate;
                }
            }
        }
        private int FaceCount(int e)=>edgeFaceOffsets[e+1]-edgeFaceOffsets[e];
        private int FaceSize(int f)=>faceOffsets[f+1]-faceOffsets[f];
        private bool HasFace(int e,int face){for(int i=edgeFaceOffsets[e];i<edgeFaceOffsets[e+1];i++)if(edgeFaces[i]==face)return true;return false;}
        private bool SharesFace(int a,int b){for(int i=edgeFaceOffsets[a];i<edgeFaceOffsets[a+1];i++)if(HasFace(b,edgeFaces[i]))return true;return false;}
        private int Other(int e,int v)=>edges[e].a==v?edges[e].b:edges[e].a;
        private int Next(int e,int v)=>next[e*2+(edges[e].a==v?0:1)];
        internal HashSet<int> EdgeLoop(int seed,int previous=-1)
        {
            var result=new HashSet<int>();if(seed<0||seed>=edges.Length)return result;result.Add(seed);
            // An explicit turning pair resolves the shared face perimeter at a pole.
            if(previous>=0&&previous!=seed&&previous<edges.Length)
            {
                var a=edges[seed];var b=edges[previous];int common=a.a==b.a||a.a==b.b?a.a:a.b==b.a||a.b==b.b?a.b:-1;
                if(common<0)
                {
                    // Opposite sides of an authored quad establish a ring direction,
                    // not an end-to-end edge loop. Do not infer it from world axes.
                    for(int f=edgeFaceOffsets[seed];f<edgeFaceOffsets[seed+1];f++)
                        if(OppositeEdge(edgeFaces[f],seed)==previous)return EdgeRing(seed);
                    return result;
                }
                if(common>=0&&Next(seed,common)!=previous)
                {
                    int face=-1,count=0;for(int f=edgeFaceOffsets[seed];f<edgeFaceOffsets[seed+1];f++)if(HasFace(previous,edgeFaces[f])){face=edgeFaces[f];count++;}
                    if(count==1){for(int i=faceOffsets[face];i<faceOffsets[face+1];i++)if(faceEdges[i]>=0)result.Add(faceEdges[i]);return result;}
                }
            }
            void Walk(int v){int e=seed;while(true){int other=Next(e,v);if(other<0||!result.Add(other))return;v=Other(other,v);e=other;}}
            Walk(edges[seed].a);Walk(edges[seed].b);return result;
        }
        private int OppositeEdge(int face,int entered)
        {
            if(FaceSize(face)!=4)return -1;
            for(int side=0;side<4;side++)if(faceEdges[faceOffsets[face]+side]==entered)
                return faceEdges[faceOffsets[face]+(side+2)%4];
            return -1;
        }
        private HashSet<int> EdgeRing(int seed)
        {
            var result=new HashSet<int>{seed};
            if(FaceCount(seed)>2)return result;
            void Walk(int face,int entered)
            {
                while(true)
                {
                    int exit=OppositeEdge(face,entered);
                    if(exit<0||FaceCount(exit)>2||!result.Add(exit))return;
                    if(FaceCount(exit)!=2)return;
                    int first=edgeFaces[edgeFaceOffsets[exit]],second=edgeFaces[edgeFaceOffsets[exit]+1];
                    face=first==face?second:first;entered=exit;
                }
            }
            for(int i=edgeFaceOffsets[seed];i<edgeFaceOffsets[seed+1];i++)Walk(edgeFaces[i],seed);
            return result;
        }
        internal HashSet<int> SelectEdges(int seed,int previous)
        {
            var result=new HashSet<int>();foreach(int e in EdgeLoop(RenderEdges[seed],previous>=0?RenderEdges[previous]:-1))for(int i=aliasOffsets[e];i<aliasOffsets[e+1];i++)result.Add(aliases[i]);return result;
        }
        private void AddVertex(HashSet<int> result,int vertex){for(int i=vertexOffsets[vertex];i<vertexOffsets[vertex+1];i++)result.Add(vertexIds[i]);}
        internal HashSet<int> SelectVertices(int previous,int current)
        {
            var result=new HashSet<int>();int a=Canonical[current];AddVertex(result,a);
            if(previous<0||Canonical[previous]==a||!lookup.TryGetValue(Key(a,Canonical[previous]),out int seed))return result;
            foreach(int e in EdgeLoop(seed)){AddVertex(result,edges[e].a);AddVertex(result,edges[e].b);}return result;
        }
        internal HashSet<int> SelectFaces(int previous,int current)
        {
            var result=new HashSet<int>{current};int seam=-1,count=0;
            if(previous>=0&&previous!=current&&previous<faceOffsets.Length-1)for(int i=faceOffsets[current];i<faceOffsets[current+1];i++)
            {int e=faceEdges[i];if(e>=0&&FaceCount(e)==2&&HasFace(e,previous)){seam=e;count++;}}
            if(count==1)
            {
                result.Add(previous);
                void Walk(int face,int entered)
                {
                    while(FaceSize(face)==4)
                    {
                        int side=-1;for(int i=0;i<4;i++)if(faceEdges[faceOffsets[face]+i]==entered){side=i;break;}if(side<0)return;
                        int exit=faceEdges[faceOffsets[face]+(side+2)%4];if(exit<0||FaceCount(exit)!=2)return;
                        int first=edgeFaces[edgeFaceOffsets[exit]],second=edgeFaces[edgeFaceOffsets[exit]+1],nextFace=first==face?second:first;
                        if(!result.Add(nextFace))return;face=nextFace;entered=exit;
                    }
                }
                Walk(previous,seam);Walk(current,seam);return result;
            }
            if(previous>=0&&previous!=current)return result;
            var pending=new Queue<int>();pending.Enqueue(current);
            while(pending.Count>0)
            {
                int face=pending.Dequeue();for(int i=faceOffsets[face];i<faceOffsets[face+1];i++)
                {int e=faceEdges[i];if(e>=0&&FaceCount(e)==2)for(int j=edgeFaceOffsets[e];j<edgeFaceOffsets[e+1];j++)if(result.Add(edgeFaces[j]))pending.Enqueue(edgeFaces[j]);}
            }
            return result;
        }
    }
}
