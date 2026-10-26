using System;using System.Collections.Generic;using UnityEngine;
namespace MashBoxSDK.AnimationStudio {
 // Authored polygon adjacency, independent of render triangulation and camera angles.
 internal sealed class StudioSelectionTopology {
  internal readonly int[] Canonical,RenderEdges;internal readonly List<int>[] VertexAliases,Incident;internal readonly List<int[]> FaceEdges=new List<int[]>();
  internal readonly List<(int a,int b)> Edges=new List<(int,int)>();internal readonly List<List<int>> EdgeFaces=new List<List<int>>(),EdgeAliases=new List<List<int>>();
  private readonly Dictionary<(int,int),int> lookup=new Dictionary<(int,int),int>();private readonly int[] next;
  private static (int,int) Key(int a,int b)=>a<b?(a,b):(b,a);
  internal StudioSelectionTopology(IList<Vector3> positions,IList<StudioMeshCuts.Face> faces,IList<(int a,int b)> renderEdges,bool weld){
   Canonical=new int[positions.Count];var map=new Dictionary<Vector3,int>();var aliases=new List<List<int>>();
   for(int i=0;i<positions.Count;i++){int id;if(!weld||!map.TryGetValue(positions[i],out id)){id=aliases.Count;aliases.Add(new List<int>());if(weld)map.Add(positions[i],id);}Canonical[i]=id;aliases[id].Add(i);}VertexAliases=aliases.ToArray();Incident=new List<int>[aliases.Count];for(int i=0;i<Incident.Length;i++)Incident[i]=new List<int>();
   int Edge(int a,int b){var k=Key(Canonical[a],Canonical[b]);if(k.Item1==k.Item2)return -1;if(lookup.TryGetValue(k,out int e))return e;e=Edges.Count;lookup.Add(k,e);Edges.Add(k);EdgeFaces.Add(new List<int>(2));EdgeAliases.Add(new List<int>());Incident[k.Item1].Add(e);Incident[k.Item2].Add(e);return e;}
   for(int f=0;f<faces.Count;f++){var v=faces[f].Vertices;var fe=new int[v.Count];for(int i=0;i<v.Count;i++){int e=Edge(v[i],v[(i+1)%v.Count]);fe[i]=e;if(e>=0&&!EdgeFaces[e].Contains(f))EdgeFaces[e].Add(f);}FaceEdges.Add(fe);}
   RenderEdges=new int[renderEdges.Count];for(int i=0;i<renderEdges.Count;i++){int e=Edge(renderEdges[i].a,renderEdges[i].b);RenderEdges[i]=e;if(e>=0)EdgeAliases[e].Add(i);}
   next=new int[Edges.Count*2];for(int i=0;i<next.Length;i++)next[i]=-1;
   for(int v=0;v<Incident.Length;v++)
   {
    var around=Incident[v];bool regular=around.Count==4,blocked=false;int boundaryA=-1,boundaryB=-1,boundaryCount=0;
    foreach(int e in around)
    {
     blocked|=EdgeFaces[e].Count>2;regular&=EdgeFaces[e].Count==2;
     if(EdgeFaces[e].Count==1){if(boundaryCount==0)boundaryA=e;else if(boundaryCount==1)boundaryB=e;boundaryCount++;}
     foreach(int f in EdgeFaces[e])regular&=FaceEdges[f].Length==4;
    }
    if(blocked)continue;
    foreach(int e in around)
    {
     int candidate=-1,count=0;
     if(EdgeFaces[e].Count==1&&boundaryCount==2){candidate=e==boundaryA?boundaryB:boundaryA;count=1;}
     else if(regular)foreach(int other in around)if(other!=e&&!SharesFace(e,other)){candidate=other;count++;}
     if(count==1)next[e*2+(Edges[e].a==v?0:1)]=candidate;
    }
   }
  }
  private bool SharesFace(int a,int b){foreach(int f in EdgeFaces[a])if(EdgeFaces[b].Contains(f))return true;return false;}
  private int Other(int e,int v)=>Edges[e].a==v?Edges[e].b:Edges[e].a;
  private int Next(int e,int v)=>next[e*2+(Edges[e].a==v?0:1)];
  internal HashSet<int> EdgeLoop(int seed,int previous=-1){var result=new HashSet<int>();if(seed<0||seed>=Edges.Count)return result;result.Add(seed);
   // An explicit turning pair resolves an ambiguous pole/perpendicular perimeter.
   if(previous>=0&&previous!=seed&&previous<Edges.Count){var a=Edges[seed];var b=Edges[previous];int common=a.a==b.a||a.a==b.b?a.a:a.b==b.a||a.b==b.b?a.b:-1;if(common>=0&&Next(seed,common)!=previous){int face=-1,count=0;foreach(int f in EdgeFaces[seed])if(EdgeFaces[previous].Contains(f)){face=f;count++;}if(count==1){foreach(int e in FaceEdges[face])if(e>=0)result.Add(e);return result;}}}
   void Walk(int v){int e=seed;while(true){int other=Next(e,v);if(other<0||!result.Add(other))return;v=Other(other,v);e=other;}}
   Walk(Edges[seed].a);Walk(Edges[seed].b);return result;
  }
  internal HashSet<int> SelectEdges(int seed,int previous){var result=new HashSet<int>();foreach(int e in EdgeLoop(RenderEdges[seed],previous>=0?RenderEdges[previous]:-1))foreach(int id in EdgeAliases[e])result.Add(id);return result;}
  internal HashSet<int> SelectVertices(int previous,int current){var result=new HashSet<int>();int a=Canonical[current];foreach(int id in VertexAliases[a])result.Add(id);if(previous<0||Canonical[previous]==a||!lookup.TryGetValue(Key(a,Canonical[previous]),out int seed))return result;foreach(int e in EdgeLoop(seed)){foreach(int id in VertexAliases[Edges[e].a])result.Add(id);foreach(int id in VertexAliases[Edges[e].b])result.Add(id);}return result;}
  internal HashSet<int> SelectFaces(int previous,int current){var result=new HashSet<int>{current};int seam=-1,count=0;if(previous>=0&&previous!=current&&previous<FaceEdges.Count)foreach(int e in FaceEdges[current])if(e>=0&&EdgeFaces[e].Count==2&&EdgeFaces[e].Contains(previous)){seam=e;count++;}
   if(count==1){result.Add(previous);void Walk(int face,int entered){while(FaceEdges[face].Length==4){var fe=FaceEdges[face];int side=Array.IndexOf(fe,entered);if(side<0)return;int exit=fe[(side+2)%4];if(exit<0||EdgeFaces[exit].Count!=2)return;int nextFace=EdgeFaces[exit][0]==face?EdgeFaces[exit][1]:EdgeFaces[exit][0];if(!result.Add(nextFace))return;face=nextFace;entered=exit;}}Walk(previous,seam);Walk(current,seam);return result;}
   if(previous>=0&&previous!=current)return result;
   var pending=new Queue<int>();pending.Enqueue(current);while(pending.Count>0){int face=pending.Dequeue();foreach(int e in FaceEdges[face])if(e>=0&&EdgeFaces[e].Count==2)foreach(int f in EdgeFaces[e])if(result.Add(f))pending.Enqueue(f);}return result;
  }
 }
}
