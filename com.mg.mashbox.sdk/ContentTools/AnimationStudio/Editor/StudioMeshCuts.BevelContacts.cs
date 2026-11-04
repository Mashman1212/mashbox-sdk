using System;using System.Collections.Generic;using UnityEngine;
namespace MashBoxSDK.AnimationStudio {
 internal sealed partial class StudioMeshCuts {
  private struct BevelContact {internal int A,B;internal Vector3 Point;internal double Time;}
  // Only source-edge collapses proven by the captured wavefront are eligible.
  // This is an event transaction, never a distance-based cleanup of the mesh.
  private void PrepareBevelContacts(BevelWavefrontPlan plan){
   if(float.IsInfinity(plan.Limit)||plan.SplitLimit<=plan.Limit+Math.Max(1e-7f,plan.Limit*2e-5f))return;
   var candidates=new Dictionary<(int,int),BevelContact>();double first=double.PositiveInfinity;
   foreach(int f in plan.SelectedFaces){var face=Faces[f].Vertices;var velocity=plan.Velocities[f];for(int c=0;c<face.Count;c++){
    int d=(c+1)%face.Count,a=welded[face[c]],b=welded[face[d]];var key=BevelKey(a,b);if(plan.Targets.Contains(key))continue;
    var span=new ClipVector(plan.Originals[b])-new ClipVector(plan.Originals[a]);var va=new ClipVector(velocity[c]);var vb=new ClipVector(velocity[d]);var difference=va-vb;
    if(difference.Squared<1e-24)continue;double time=span.Dot(difference)/difference.Squared;
    if(time<=0||Math.Abs(time-plan.Limit)>Math.Max(1e-7,plan.Limit*2e-5))continue;
    double tolerance=bevelEpsilon*bevelEpsilon*64;
    if((span-difference*time).Squared>tolerance||va.Cross(span).Squared>tolerance*span.Squared||vb.Cross(span).Squared>tolerance*span.Squared)continue;
    var pa=new ClipVector(plan.Originals[a])+va*time;var pb=new ClipVector(plan.Originals[b])+vb*time;
    var point=va.Squared==0?plan.Originals[a]:vb.Squared==0?plan.Originals[b]:((pa+pb)*.5).Float;
    if(candidates.TryGetValue(key,out var prior)&&(prior.Point-point).sqrMagnitude>tolerance)return;
    candidates[key]=new BevelContact{A=a,B=b,Point=point,Time=time};first=Math.Min(first,time);
   }}
   if(candidates.Count==0)return;plan.ContactWidth=(float)first;
   foreach(var contact in candidates.Values)if(Math.Abs(contact.Time-first)<=Math.Max(1e-7,first*2e-5))plan.Contacts.Add(contact);
  }
  private void ApplyBevelContacts(BevelDraft draft,float requested,Action<List<int>,int,int,List<Vector3>,bool,int> add){
   var plan=draft.Plan;if(plan.Contacts.Count==0||requested<plan.ContactWidth||draft.Width>plan.ContactWidth||Math.Abs(draft.Width-plan.Limit)>Math.Max(1e-7,plan.Limit*2e-6))return;
   double ratio=plan.ContactWidth/(double)draft.Width;
   for(int i=plan.Originals.Length;i<draft.Positions.Count;i++){int owner=draft.Owners[i];draft.Positions[i]=(new ClipVector(plan.Originals[owner])+(new ClipVector(draft.Positions[i])-new ClipVector(plan.Originals[owner]))*ratio).Float;}
   var remap=new Dictionary<int,int>();int Root(int i){while(remap.TryGetValue(i,out int next)&&next!=i)i=next;return i;}
   foreach(var contact in plan.Contacts){var meeting=new List<int>();
    if(draft.ContactCandidates.TryGetValue(BevelKey(contact.A,contact.B),out var candidates))foreach(int i in candidates)if((draft.Positions[i]-contact.Point).sqrMagnitude<=bevelEpsilon*bevelEpsilon*256)meeting.Add(i);
    if(plan.Originals[contact.A].Equals(contact.Point))meeting.Add(contact.A);if(plan.Originals[contact.B].Equals(contact.Point))meeting.Add(contact.B);
    if(meeting.Count<2)throw new BevelOverlap("The source-edge contact could not be resolved consistently.");
    int root=int.MaxValue;foreach(int i in meeting)root=Math.Min(root,Root(i));draft.Positions[root]=contact.Point;
    foreach(int i in meeting){int old=Root(i);if(old!=root)remap[old]=root;remap[i]=root;}
   }
   var output=new List<(List<int> geometry,int source,int material,List<Vector3> normals,bool surface,int original)>();
   foreach(var polygon in draft.Polygons){var geometry=new List<int>();var normals=new List<Vector3>();bool touched=false;
    foreach(int id in polygon.Vertices){var vertex=draft.Vertices[id];int geo=Root(vertex.Geo);touched|=geo!=vertex.Geo;if(geometry.Count>0&&geometry[geometry.Count-1]==geo)continue;geometry.Add(geo);normals.Add(vertex.Normal);}
    if(geometry.Count>1&&geometry[0]==geometry[geometry.Count-1]){geometry.RemoveAt(geometry.Count-1);normals.RemoveAt(normals.Count-1);}
    if(geometry.Count<3){if(touched)continue;throw new BevelOverlap("A non-contact face collapsed.");}
    if(new HashSet<int>(geometry).Count!=geometry.Count)throw new BevelOverlap("This contact requires an unsupported face split.");
    output.Add((geometry,draft.Vertices[polygon.Vertices[0]].SourceFace,polygon.Material,normals,polygon.SourceSurface,polygon.OriginalFace));
   }
   var selected=new HashSet<(int,int)>();foreach(var edge in draft.Selection){int a=Root(edge.Item1),b=Root(edge.Item2);if(a!=b)selected.Add(BevelKey(a,b));}draft.Selection.Clear();draft.Selection.UnionWith(selected);
   draft.Polygons.Clear();draft.Vertices.Clear();draft.NormalHints.Clear();draft.SourceHints.Clear();
   foreach(var face in output)add(face.geometry,face.source,face.material,face.normals,face.surface,face.original);
   draft.Width=plan.ContactWidth;
   var uses=new Dictionary<(int,int),(int count,int direction)>();
   void Use(int a,int b){var key=BevelKey(a,b);uses.TryGetValue(key,out var previous);uses[key]=(previous.count+1,previous.direction+(a==key.Item1?1:-1));}
   foreach(var polygon in draft.Polygons)for(int c=0;c<polygon.Vertices.Count;c++)Use(draft.Vertices[polygon.Vertices[c]].Geo,draft.Vertices[polygon.Vertices[(c+1)%polygon.Vertices.Count]].Geo);
   foreach(int f in plan.StaticFaces){var face=Faces[f].Vertices;for(int c=0;c<face.Count;c++)Use(welded[face[c]],welded[face[(c+1)%face.Count]]);}
   foreach(var pair in uses)if(pair.Value.count!=2||pair.Value.direction!=0){if(bevelEdges.TryGetValue(pair.Key,out var original)&&original.Uses.Count==1&&pair.Value.count==1)continue;throw new BevelOverlap("The contact did not produce connected, consistently wound faces.");}
  }
 }
}


