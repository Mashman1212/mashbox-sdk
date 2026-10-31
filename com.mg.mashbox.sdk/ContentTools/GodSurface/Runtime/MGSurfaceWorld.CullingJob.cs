using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace MG.GodSurface
{
    public sealed partial class MGSurfaceWorld
    {
        struct CullEntry { public Bounds bounds; public int first, count, prop, lod, lodSet, cellLod, groupStart, groupCount; }
        struct CullCell { public Bounds bounds; public int start, count; }
        struct CullLod { public Vector3 center, extents; public float size; public int start, count; }
        NativeArray<CullEntry> cullEntries;
        NativeArray<CullCell> cullCells;
        NativeArray<CullLod> cullLods, cullCellLods;
        NativeArray<int> cullCellEntries, cullGroups;
        NativeArray<float> cullThresholds;

        void ReleaseCullingData()
        {
            if(cullEntries.IsCreated)cullEntries.Dispose(); if(cullCells.IsCreated)cullCells.Dispose();
            if(cullLods.IsCreated)cullLods.Dispose(); if(cullCellLods.IsCreated)cullCellLods.Dispose();
            if(cullCellEntries.IsCreated)cullCellEntries.Dispose(); if(cullGroups.IsCreated)cullGroups.Dispose();
            if(cullThresholds.IsCreated)cullThresholds.Dispose();
        }
        void BuildCullingData()
        {
            ReleaseCullingData();
            var entryData=new CullEntry[drawEntries.Count];var groupData=new List<int>();
            for(int i=0;i<entryData.Length;i++)
            {
                var e=entries[drawEntries[i]];
                entryData[i]=new CullEntry{bounds=e.bounds,first=drawEntries[i],count=e.drawCount,prop=e.propIndex,lod=e.lod,lodSet=e.lodSet,
                    cellLod=e.lodSet>=0?lods[e.lodSet].cellLod:-1,groupStart=groupData.Count,groupCount=e.groups.Length};
                groupData.AddRange(e.groups);
            }
            var cellData=new CullCell[cells.Count];var members=new List<int>();
            for(int i=0;i<cellData.Length;i++){var c=cells[i];cellData[i]=new CullCell{bounds=c.bounds,start=members.Count,count=c.instances.Count};members.AddRange(c.instances);}
            var thresholds=new List<float>();var lodData=new CullLod[lods.Count];var cellLodData=new CullLod[cellLods.Count];
            for(int i=0;i<lodData.Length;i++){var l=lods[i];lodData[i]=new CullLod{center=l.center,size=l.size,start=thresholds.Count,count=l.thresholds.Length};thresholds.AddRange(l.thresholds);}
            for(int i=0;i<cellLodData.Length;i++){var l=cellLods[i];cellLodData[i]=new CullLod{center=l.bounds.center,extents=l.bounds.extents,size=l.size,start=thresholds.Count,count=l.thresholds.Length};thresholds.AddRange(l.thresholds);}
            cullEntries=new NativeArray<CullEntry>(entryData,Allocator.Persistent);cullCells=new NativeArray<CullCell>(cellData,Allocator.Persistent);
            cullCellEntries=new NativeArray<int>(members.ToArray(),Allocator.Persistent);cullGroups=new NativeArray<int>(groupData.ToArray(),Allocator.Persistent);
            cullLods=new NativeArray<CullLod>(lodData,Allocator.Persistent);cullCellLods=new NativeArray<CullLod>(cellLodData,Allocator.Persistent);
            cullThresholds=new NativeArray<float>(thresholds.ToArray(),Allocator.Persistent);
        }

        // No Unity object access in per-view culling. Geometry pieces share a single bounds/LOD test.
        [BurstCompile(CompileSynchronously=true)]
        struct CullJob : IJob
        {
            [ReadOnly] public NativeArray<CullEntry> entries;
            [ReadOnly] public NativeArray<CullCell> cells;
            [ReadOnly] public NativeArray<CullLod> lods;
            [ReadOnly] public NativeArray<int> members, groups;
            [ReadOnly] public NativeArray<float> thresholds;
            [ReadOnly] public NativeArray<Plane> planes;
            [ReadOnly] public NativeArray<CullingSplit> splits;
            [ReadOnly] public NativeArray<byte> allowed;
            public NativeArray<int> selected, counts, visible, totals;
            public NativeArray<ushort> masks;
            public NativeArray<byte> seen, debug;
            public Vector3 cameraPosition;
            public float drawDistanceSquared, denominatorScale, orthoDenominator, lodBias;
            public int splitCount; public uint exclusionMask;
            public bool camera, sharedLods, orthographic;
            static float DistanceSquared(Vector3 center,Vector3 extents,Vector3 point)
            {
                var d=point-center;d=new Vector3(Mathf.Max(0,Mathf.Abs(d.x)-extents.x),Mathf.Max(0,Mathf.Abs(d.y)-extents.y),Mathf.Max(0,Mathf.Abs(d.z)-extents.z));return Vector3.Dot(d,d);
            }
            bool Inside(Bounds b,int start,int count)
            {
                var c=b.center;var e=b.extents;
                for(int i=start;i<start+count;i++){var p=planes[i];var n=p.normal;if(Vector3.Dot(n,c)+p.distance+Mathf.Abs(n.x)*e.x+Mathf.Abs(n.y)*e.y+Mathf.Abs(n.z)*e.z<0)return false;}
                return true;
            }
            ushort Visibility(Bounds b)
            {
                if(camera||splits.Length==0)return Inside(b,0,planes.Length)?ushort.MaxValue:(ushort)0;
                ushort mask=0;for(int i=0;i<splitCount;i++){if((exclusionMask&(1u<<i))!=0)continue;var s=splits[i];if(Inside(b,s.cullingPlaneOffset,s.cullingPlaneCount))mask|=(ushort)(1<<i);}return mask;
            }
            public void Execute()
            {
                for(int i=0;i<lods.Length;i++)
                {
                    var l=lods[i];float distance=Mathf.Sqrt(DistanceSquared(l.center,sharedLods?l.extents:Vector3.zero,cameraPosition));
                    float denominator=orthographic?orthoDenominator:Mathf.Max(.001f,distance)*denominatorScale;
                    float height=l.size*lodBias/Mathf.Max(.001f,denominator);int level=-1;
                    for(int j=0;j<l.count;j++)if(height>=thresholds[l.start+j]){level=j;break;}selected[i]=level;
                }
                int visibleCount=0,visibleProps=0,visibleCells=0;
                for(int ci=0;ci<cells.Length;ci++)
                {
                    var cell=cells[ci];if(debug.Length>0)debug[ci]=4; // NotDrawn
                    if(DistanceSquared(cell.bounds.center,cell.bounds.extents,cameraPosition)>=drawDistanceSquared){if(debug.Length>0)debug[ci]=3;continue;}
                    if(Visibility(cell.bounds)==0)continue;visibleCells++;
                    bool rejected=false,survived=false;int before=visibleCount,previousProp=-1;ushort previousMask=0;
                    for(int m=cell.start;m<cell.start+cell.count;m++)
                    {
                        int index=members[m];var e=entries[index];int level=e.lodSet>=0?selected[sharedLods?e.cellLod:e.lodSet]:0;
                        bool eligible=false;for(int g=e.groupStart;g<e.groupStart+e.groupCount;g++)if(allowed[groups[g]]!=0){eligible=true;break;}
                        if(!eligible)continue;if(level<0)rejected=true;else survived=true;
                        if(e.lodSet>=0&&level!=e.lod)continue;
                        if(previousProp!=e.prop){previousProp=e.prop;previousMask=Visibility(e.bounds);}
                        ushort mask=previousMask;if(mask==0)continue;
                        masks[index]=mask;visible[visibleCount++]=index;if(seen[e.prop]==0){seen[e.prop]=1;visibleProps++;}
                        for(int g=e.groupStart;g<e.groupStart+e.groupCount;g++)
                        {
                            int group=groups[g];if(allowed[group]==0)continue;
                            if(splitCount==1)counts[group]+=e.count;
                            else for(int split=0;split<splitCount;split++)if((mask&(1<<split))!=0)counts[group*splitCount+split]+=e.count;
                        }
                    }
                    if(debug.Length>0)debug[ci]=(byte)(visibleCount>before?(rejected?2:1):(rejected&&!survived?3:4));
                }
                totals[0]=visibleCount;totals[1]=visibleProps;totals[2]=visibleCells;
            }
        }
        [BurstCompile(CompileSynchronously=true)]
        unsafe struct FillVisibleJob : IJob
        {
            [ReadOnly] public NativeArray<CullEntry> entries;
            [ReadOnly] public NativeArray<int> visible,groups;
            [ReadOnly] public NativeArray<ushort> masks;
            [ReadOnly] public NativeArray<byte> allowed;
            public NativeArray<int> offsets;
            [NativeDisableUnsafePtrRestriction] public int* output;
            public int count,splitCount;
            public void Execute()
            {
                for(int i=0;i<count;i++)
                {
                    int index=visible[i];var e=entries[index];ushort mask=masks[index];
                    for(int g=e.groupStart;g<e.groupStart+e.groupCount;g++)
                    {
                        int group=groups[g];if(allowed[group]==0)continue;
                        for(int split=0;split<splitCount;split++)
                        {
                            if(splitCount>1&&(mask&(1<<split))==0)continue;int bucket=group*splitCount+split;int offset=offsets[bucket];
                            for(int piece=0;piece<e.count;piece++)output[offset++]=e.first+piece;offsets[bucket]=offset;
                        }
                    }
                }
            }
        }
    }
}
