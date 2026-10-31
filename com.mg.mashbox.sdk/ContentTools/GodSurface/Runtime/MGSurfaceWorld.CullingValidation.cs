#if UNITY_EDITOR
using System;
using System.Text;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
namespace MG.GodSurface
{
 public sealed partial class MGSurfaceWorld
 {
  // Focused regression checks for the actual Burst culling and expansion jobs.
  public static unsafe string ValidateCullingJobs()
  {
   var log=new StringBuilder();void Check(bool value,string text){if(!value)throw new Exception(text);log.AppendLine("PASS "+text);}
   using var entryStorage=new NativeArray<CullEntry>(new[]{
    new CullEntry{bounds=new Bounds(new Vector3(-10,0,0),Vector3.one*2),first=0,count=3,prop=0,lodSet=-1,groupCount=1},
    new CullEntry{bounds=new Bounds(new Vector3(10,0,0),Vector3.one*2),first=3,count=2,prop=1,lodSet=-1,groupCount=1},
    new CullEntry{bounds=new Bounds(Vector3.zero,Vector3.one*2),first=5,count=1,prop=2,lodSet=-1,groupCount=1}},Allocator.TempJob);var entries=entryStorage;
   using var cells=new NativeArray<CullCell>(new[]{new CullCell{bounds=new Bounds(Vector3.zero,new Vector3(22,2,2)),count=3}},Allocator.TempJob);
   using var members=new NativeArray<int>(new[]{0,1,2},Allocator.TempJob);using var groups=new NativeArray<int>(new[]{0},Allocator.TempJob);
   using var lods=new NativeArray<CullLod>(new[]{new CullLod{center=new Vector3(10,0,0),extents=new Vector3(9,0,0),size=1,count=2}},Allocator.TempJob);
   using var thresholds=new NativeArray<float>(new[]{.5f,.04f},Allocator.TempJob);
   using var planes=new NativeArray<Plane>(new[]{new Plane(Vector3.left,0),new Plane(Vector3.right,0)},Allocator.TempJob);
   using var splits=new NativeArray<CullingSplit>(new[]{new CullingSplit{cullingPlaneOffset=0,cullingPlaneCount=1},new CullingSplit{cullingPlaneOffset=1,cullingPlaneCount=1}},Allocator.TempJob);
   using var allowedStorage=new NativeArray<byte>(new byte[]{1},Allocator.TempJob);var allowed=allowedStorage;
   using var countsStorage=new NativeArray<int>(2,Allocator.TempJob);var counts=countsStorage;
   using var selected=new NativeArray<int>(1,Allocator.TempJob);using var visible=new NativeArray<int>(3,Allocator.TempJob);using var totals=new NativeArray<int>(3,Allocator.TempJob);
   using var masks=new NativeArray<ushort>(3,Allocator.TempJob);using var seenStorage=new NativeArray<byte>(3,Allocator.TempJob);var seen=seenStorage;
   using var debug=new NativeArray<byte>(1,Allocator.TempJob);
   var job=new CullJob{entries=entries,cells=cells,members=members,groups=groups,lods=lods,thresholds=thresholds,planes=planes,splits=splits,allowed=allowed,counts=counts,selected=selected,visible=visible,totals=totals,masks=masks,seen=seen,debug=debug,splitCount=2,drawDistanceSquared=160000,denominatorScale=2,lodBias=1};
   void Run(){for(int i=0;i<counts.Length;i++)counts[i]=0;for(int i=0;i<seen.Length;i++)seen[i]=0;job.Run();}
   Run();Check(masks[0]==1&&masks[1]==2&&masks[2]==3&&counts[0]==4&&counts[1]==3,"Shadow splits retain per-prop membership, including overlap");
   Check(totals[0]==3&&totals[1]==3,"Geometry spans count props once");
   using var offsets=new NativeArray<int>(new[]{0,4},Allocator.TempJob);using var output=new NativeArray<int>(7,Allocator.TempJob);
   new FillVisibleJob{entries=entries,visible=visible,groups=groups,masks=masks,allowed=allowed,offsets=offsets,output=(int*)output.GetUnsafePtr(),count=totals[0],splitCount=2}.Run();
   var expected=new[]{0,1,2,5,3,4,5};bool matches=true;for(int i=0;i<expected.Length;i++)matches&=output[i]==expected[i];Check(matches,"Only intersecting carrier pieces enter each shadow draw");
   job.exclusionMask=2;Run();Check(counts[0]==4&&counts[1]==0&&totals[1]==2,"Excluded cascades submit no instances");
   job.exclusionMask=0;job.cameraPosition=new Vector3(1000,0,0);Run();Check(totals[0]==0&&debug[0]==3,"Hard cell distance culling applies to all pieces");
   job.cameraPosition=Vector3.zero;job.camera=true;job.splitCount=1;Run();Check(totals[0]==1&&visible[0]==2&&counts[0]==1,"Camera frustum culls whole geometry spans");
   job.camera=false;job.splitCount=2;
   var e=entries[0];e.lodSet=0;e.cellLod=0;e.lod=0;entries[0]=e;e=entries[1];e.lodSet=0;e.cellLod=0;e.lod=1;entries[1]=e;
   Run();Check(totals[0]==2&&visible[0]==1&&selected[0]==1,"Per-prop LOD selects the expected level");
   job.sharedLods=true;Run();Check(totals[0]==2&&visible[0]==0&&selected[0]==0,"Per-cell family LOD uses nearest family bounds");
   allowed[0]=0;Run();Check(totals[0]==0&&counts[0]==0&&counts[1]==0,"Filtered layers and shadow modes submit nothing");
   log.AppendLine("SUCCESS");return log.ToString();
  }
 }
}
#endif
