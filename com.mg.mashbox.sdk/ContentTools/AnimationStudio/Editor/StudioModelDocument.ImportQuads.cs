using System;
using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioModelDocument
    {
        private sealed class ImportQuadCandidate
        {
            internal int First,Second;
            internal int[] Corners;
            internal double Score;
        }
        private struct ImportEdgeUse { internal int Face,Count;internal bool Forward; }
        private static Dictionary<int,int[]> ImportQuadPairs(int[] triangles,Vector3[] positions)
        {
            var edges=new Dictionary<(int,int),ImportEdgeUse>();
            int count=triangles.Length/3;
            for(int face=0;face<count;face++)for(int corner=0;corner<3;corner++)
            {
                int a=triangles[face*3+corner],b=triangles[face*3+(corner+1)%3];var key=a<b?(a,b):(b,a);
                if(edges.TryGetValue(key,out var use)){use.Count++;edges[key]=use;}else edges.Add(key,new ImportEdgeUse{Face=face,Count=1,Forward=a==key.Item1});
            }
            var candidates=new List<ImportQuadCandidate>();
            for(int face=0;face<count;face++)for(int corner=0;corner<3;corner++)
            {
                int a=triangles[face*3+corner],b=triangles[face*3+(corner+1)%3],c=triangles[face*3+(corner+2)%3];var key=a<b?(a,b):(b,a);var use=edges[key];
                if(use.Count!=2||use.Face>=face||use.Forward==(a==key.Item1))continue;
                int d=-1;for(int j=0;j<3;j++){int v=triangles[use.Face*3+j];if(v!=a&&v!=b)d=v;}
                if(d<0||d==c)continue;
                // Removing the common diagonal changes editable topology only.
                // The two existing render triangles and every vertex stream stay intact.
                var quad=new[]{a,d,b,c};double score=ImportQuadScore(quad,positions);
                if(double.IsPositiveInfinity(score))continue;
                candidates.Add(new ImportQuadCandidate{First=use.Face,Second=face,Corners=quad,Score=score});
            }
            candidates.Sort((a,b)=>{int value=a.Score.CompareTo(b.Score);if(value!=0)return value;value=a.First.CompareTo(b.First);return value!=0?value:a.Second.CompareTo(b.Second);});
            var result=new Dictionary<int,int[]>();var consumed=new bool[count];
            foreach(var candidate in candidates)
            {
                if(consumed[candidate.First]||consumed[candidate.Second])continue;
                consumed[candidate.First]=consumed[candidate.Second]=true;
                result.Add(candidate.First,candidate.Corners);result.Add(candidate.Second,Array.Empty<int>());
            }
            return result;
        }
        private static double ImportQuadScore(int[] quad,Vector3[] positions)
        {
            Vector3 p0=positions[quad[0]],p1=positions[quad[1]],p2=positions[quad[2]],p3=positions[quad[3]];
            // Double products avoid treating small but valid imported faces as zero.
            (double x,double y,double z) Delta(Vector3 a,Vector3 b)=>((double)a.x-b.x,(double)a.y-b.y,(double)a.z-b.z);
            (double x,double y,double z) Cross((double x,double y,double z)a,(double x,double y,double z)b)=>(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
            double Dot((double x,double y,double z)a,(double x,double y,double z)b)=>a.x*b.x+a.y*b.y+a.z*b.z;
            // Triangles use the original common diagonal p0-p2.
            var n0=Cross(Delta(p1,p0),Delta(p2,p0));var n1=Cross(Delta(p2,p0),Delta(p3,p0));double aa=Dot(n0,n0),bb=Dot(n1,n1);
            if(aa==0||bb==0||double.IsNaN(aa)||double.IsNaN(bb)||double.IsInfinity(aa)||double.IsInfinity(bb))return double.PositiveInfinity;
            double fold=Dot(n0,n1)/Math.Sqrt(aa*bb);
            if(fold<.9659258262890683)return double.PositiveInfinity; // 15-degree fold.
            var normal=(n0.x+n1.x,n0.y+n1.y,n0.z+n1.z);double score=4*(1-fold);
            for(int i=0;i<4;i++)
            {
                var incoming=Delta(positions[quad[(i+3)%4]],positions[quad[i]]);var outgoing=Delta(positions[quad[(i+1)%4]],positions[quad[i]]);
                double left=Dot(incoming,incoming),right=Dot(outgoing,outgoing);if(left==0||right==0)return double.PositiveInfinity;
                if(Dot(Cross(outgoing,incoming),normal)<=0)return double.PositiveInfinity;
                double cosine=Dot(incoming,outgoing)/Math.Sqrt(left*right);
                if(Math.Abs(cosine)>.8660254037844386)return double.PositiveInfinity; // 30..150-degree corners.
                score+=cosine*cosine;
            }
            return score;
        }
    }
}
