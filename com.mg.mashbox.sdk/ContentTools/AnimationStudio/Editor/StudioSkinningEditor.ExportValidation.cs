using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor
    {
        // Compare actual distances, not rounded bins: two almost identical floats
        // can straddle a rounding boundary. Preserve triangle winding/multiplicity.
        private sealed class ExportVertexData
        {
            internal Vector3[] positions, normals;
            internal Vector2[] uv;
            internal Dictionary<string,float>[] weights;
            internal readonly List<Vector3[]> shapes = new List<Vector3[]>();
            internal readonly List<string> shapeNames = new List<string>();
            internal ExportVertexData(Mesh mesh, Transform[] rig)
            {
                positions=mesh.vertices; normals=mesh.normals; uv=mesh.uv;
                weights=new Dictionary<string,float>[mesh.vertexCount];
                var skin=new StudioSkinWeights(mesh);
                for(int i=0;i<weights.Length;i++)
                {
                    var map=weights[i]=new Dictionary<string,float>();
                    foreach(var w in skin.Values[i])
                    {
                        if(w.weight==0) continue;
                        if(w.boneIndex<0 || w.boneIndex>=rig.Length || !rig[w.boneIndex])
                            throw new InvalidOperationException("FBX validation found a missing weighted bone.");
                        string key=ExportBoneSignature(rig[w.boneIndex]);
                        map.TryGetValue(key,out float previous); map[key]=previous+w.weight;
                    }
                }
                for(int s=0;s<mesh.blendShapeCount;s++)
                    for(int f=0;f<mesh.GetBlendShapeFrameCount(s);f++)
                    {
                        shapeNames.Add(mesh.GetBlendShapeName(s)+":"+mesh.GetBlendShapeFrameWeight(s,f).ToString("R",System.Globalization.CultureInfo.InvariantCulture));
                        var p=new Vector3[mesh.vertexCount]; var n=new Vector3[p.Length]; var t=new Vector3[p.Length];
                        mesh.GetBlendShapeFrameVertices(s,f,p,n,t);
                        shapes.Add(p); shapes.Add(n); shapes.Add(t);
                    }
            }
        }
        private static void ValidateExportTriangles(Mesh expected,Transform[] expectedRig,Mesh actual,Transform[] actualRig)
        {
            const float positionTolerance=.0001f;
            var a=new ExportVertexData(expected,expectedRig); var b=new ExportVertexData(actual,actualRig);
            if(!a.shapeNames.SequenceEqual(b.shapeNames)) throw new InvalidOperationException("FBX changed blend shape names or frames.");
            if((a.normals.Length==a.positions.Length)!=(b.normals.Length==b.positions.Length) ||
               (a.uv.Length==a.positions.Length)!=(b.uv.Length==b.positions.Length))
                throw new InvalidOperationException("FBX changed the presence of normals or UVs.");
            Vector3Int Cell(Vector3 p) => new Vector3Int(Mathf.FloorToInt(p.x/positionTolerance),Mathf.FloorToInt(p.y/positionTolerance),Mathf.FloorToInt(p.z/positionTolerance));
            string Difference(int x,int y)
            {
                float distance=(a.positions[x]-b.positions[y]).magnitude;
                if(float.IsNaN(distance) || distance>positionTolerance) return "position distance "+distance.ToString("G6");
                if(a.normals.Length==a.positions.Length && (a.normals[x]-b.normals[y]).sqrMagnitude>.002f*.002f) return "normals";
                if(a.uv.Length==a.positions.Length && (a.uv[x]-b.uv[y]).sqrMagnitude>1e-8f) return "UVs";
                string weightError=ExportWeightDifference(a.weights[x],b.weights[y]);
                if(weightError!=null) return weightError;
                for(int s=0;s<a.shapes.Count;s++)
                    if((a.shapes[s][x]-b.shapes[s][y]).sqrMagnitude>(s%3==0?1e-8f:4e-6f)) return "blend shape deltas";
                return null;
            }
            for(int sub=0;sub<expected.subMeshCount;sub++)
            {
                if(expected.GetTopology(sub)!=MeshTopology.Triangles || actual.GetTopology(sub)!=MeshTopology.Triangles)
                    throw new InvalidOperationException("FBX validation requires triangle submeshes.");
                var ai=expected.GetTriangles(sub); var bi=actual.GetTriangles(sub);
                if(ai.Length!=bi.Length) throw new InvalidOperationException("FBX changed the triangle count in submesh "+sub);
                var cells=new Dictionary<Vector3Int,List<int>>();
                // Index all three corners, allowing cyclic rotation but not reversal.
                for(int j=0;j<bi.Length;j++)
                {
                    var key=Cell(b.positions[bi[j]]);
                    if(!cells.TryGetValue(key,out var list)) cells[key]=list=new List<int>();
                    list.Add(j);
                }
                var used=new bool[bi.Length/3];
                for(int i=0;i<ai.Length;i+=3)
                {
                    var cell=Cell(a.positions[ai[i]]); bool found=false; string detail="no triangle at the expected position";
                    for(int dx=-1;dx<=1 && !found;dx++) for(int dy=-1;dy<=1 && !found;dy++) for(int dz=-1;dz<=1 && !found;dz++)
                    {
                        if(!cells.TryGetValue(cell+new Vector3Int(dx,dy,dz),out var list)) continue;
                        foreach(int corner in list)
                        {
                            int triangle=corner/3; if(used[triangle]) continue;
                            string error=null;
                            for(int k=0;k<3;k++)
                            {
                                error=Difference(ai[i+k],bi[triangle*3+(corner%3+k)%3]);
                                if(error!=null) break;
                            }
                            if(error!=null) { detail=error; continue; }
                            used[triangle]=true; found=true; break;
                        }
                    }
                    if(!found) throw new InvalidOperationException("FBX triangle validation failed in submesh "+sub+", triangle "+(i/3)+": "+detail+". Allowed position error is 0.0001 mesh units; per-bone weight error is 0.0001.");
                }
            }
        }
        private static string ExportWeightDifference(Dictionary<string,float> expected,Dictionary<string,float> actual)
        {
            string Compare(Dictionary<string,float> source)
            {
                float total=0;
                foreach(string key in source.Keys.Union(actual.Keys))
                {
                    source.TryGetValue(key,out float a); actual.TryGetValue(key,out float b);
                    float delta=Mathf.Abs(a-b); total+=delta;
                    if(float.IsNaN(delta) || delta>.0001f)
                        return "weight for "+key+" ("+a.ToString("G6")+" to "+b.ToString("G6")+")";
                }
                return total>.001f ? "total skin weight error "+total.ToString("G6") : null;
            }
            string exactError=Compare(expected);
            if(exactError==null) return null;
            // Unity 6's FBX importer prunes influences below .001 and then
            // renormalizes, even when minBoneWeight is set below its UI minimum.
            // The FBX retains the full authored weights. Recognize only this
            // deterministic import conversion, not arbitrary weight differences.
            var retained=expected.Where(w=>w.Value>=.001f).ToDictionary(w=>w.Key,w=>w.Value);
            float sum=retained.Values.Sum();
            float removed=expected.Where(w=>w.Value<.001f).Sum(w=>w.Value);
            if(sum<=0 || removed<=0 || removed>.005f) return exactError;
            foreach(string key in retained.Keys.ToArray()) retained[key]/=sum;
            return Compare(retained)==null ? null : exactError;
        }
    }
}
