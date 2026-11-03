using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMeshCuts
    {
        // Array-only adapters reuse Bisect's triangulation and quad patch builder
        // on the fracture worker. No Mesh, editor object, or publication occurs here.
        internal static (Vector3[] points,int[][] quads) FractureQuadPatch(Vector3[] boundary,Vector3 normal,int[] triangulation=null,bool allowGrid=true)
        {
            if((boundary.Length&1)!=0)throw new InvalidOperationException("A fracture patch requires a conforming even boundary.");
            var buffer=new BisectBuffer(boundary);
            var faces=new List<Face>();var render=new List<List<int>>();
            var loop=Enumerable.Range(0,boundary.Length).ToList();
            var patches=allowGrid?TryBisectGrid(buffer,loop,normal,faces,render,1e-8f):null;
            if(patches==null)
            {
                // An unsuitable grid must not multiply every boundary edge.
                // Retain the fixed boundary and pair valid adjacent triangles.
                var indices=triangulation?.ToList()??new List<int>();if(triangulation==null)TriangulateSliceCap(loop,buffer.Positions,normal,indices);

                patches=new List<List<int>>();for(int i=0;i<indices.Count;i+=3)patches.Add(indices.Skip(i).Take(3).ToList());
                bool changed=true;while(changed)
                {
                    changed=false;for(int a=0;a<patches.Count&&!changed;a++)for(int b=a+1;b<patches.Count;b++)
                    {
                        if(patches[a].Count!=3||patches[b].Count!=3)continue;var merged=BisectMergeTriangles(patches[a],patches[b]);if(merged==null)continue;
                        bool valid=true;for(int k=0;k<4;k++)
                        {
                            var x=buffer.Positions[merged[(k+1)%4]]-buffer.Positions[merged[k]];var y=buffer.Positions[merged[(k+3)%4]]-buffer.Positions[merged[k]];
                            if(Vector3.Dot(Vector3.Cross(x,y),normal)<=1e-4f*(x.sqrMagnitude+y.sqrMagnitude)){valid=false;break;}
                        }
                        if(!valid)continue;
                        patches[a]=merged;patches.RemoveAt(b);changed=true;break;
                    }
                }
            }
            var positions=new Vector3[buffer.NextGeometry];
            for(int i=0;i<buffer.Positions.Count;i++)positions[buffer.Geometry[i]]=buffer.Positions[i];
            return(positions,patches.Select(f=>f.Select(v=>buffer.Geometry[v]).ToArray()).ToArray());
        }
    }
}




