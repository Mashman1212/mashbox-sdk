using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    // Restore authored FBX connectivity, not a position weld. Unity's render
    // vertices can be split at normals/UVs; original control-point IDs cannot.
    internal static class StudioFbxTopology
    {
        internal sealed class SourceMesh
        {internal string Name;internal Vector3[] Points;internal int[][] Faces;}
        private static object Call(object target,string name,params object[] args)
        {
            var type=target as Type??target.GetType();
            var method=type.GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static)
                .First(m=>m.Name==name&&m.GetParameters().Length==args.Length&&m.GetParameters().Select((p,i)=>args[i]==null||p.ParameterType.IsInstanceOfType(args[i])).All(b=>b));
            return method.Invoke(target is Type?null:target,args);
        }
        internal static List<SourceMesh> Read(string path)
        {
            var result=new List<SourceMesh>();
            if(string.IsNullOrEmpty(path)||!string.Equals(Path.GetExtension(path),".fbx",StringComparison.OrdinalIgnoreCase)||!File.Exists(path))return result;
            var managerType=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Autodesk.Fbx.FbxManager")).FirstOrDefault(t=>t!=null);
            if(managerType==null)return result;
            object manager=null;
            try
            {
                manager=Call(managerType,"Create");var assembly=managerType.Assembly;
                var importer=Call(assembly.GetType("Autodesk.Fbx.FbxImporter"),"Create",manager,"Studio topology");
                var scene=Call(assembly.GetType("Autodesk.Fbx.FbxScene"),"Create",manager,"Studio topology");
                if(!(bool)Call(importer,"Initialize",Path.GetFullPath(path))||!(bool)Call(importer,"Import",scene))return result;
                void Visit(object node)
                {
                    var mesh=Call(node,"GetMesh");
                    if(mesh!=null)
                    {
                        int count=(int)Call(mesh,"GetControlPointsCount");var points=new Vector3[count];
                        for(int i=0;i<count;i++)
                        {var v=Call(mesh,"GetControlPointAt",i);var t=v.GetType();points[i]=new Vector3((float)(double)t.GetField("X").GetValue(v),(float)(double)t.GetField("Y").GetValue(v),(float)(double)t.GetField("Z").GetValue(v));}
                        int faceCount=(int)Call(mesh,"GetPolygonCount");var faces=new int[faceCount][];
                        for(int f=0;f<faceCount;f++){faces[f]=new int[(int)Call(mesh,"GetPolygonSize",f)];for(int c=0;c<faces[f].Length;c++)faces[f][c]=(int)Call(mesh,"GetPolygonVertex",f,c);}
                        result.Add(new SourceMesh{Name=(string)Call(node,"GetName"),Points=points,Faces=faces});
                    }
                    int children=(int)Call(node,"GetChildCount");for(int i=0;i<children;i++)Visit(Call(node,"GetChild",i));
                }
                Visit(Call(scene,"GetRootNode"));return result;
            }
            catch(TargetInvocationException){return new List<SourceMesh>();}
            catch(InvalidOperationException){return new List<SourceMesh>();}
            finally{(manager as IDisposable)?.Dispose();}
        }
        internal static bool Apply(StudioModelDocument.Node node,IEnumerable<SourceMesh> sources,string sourceName)
        {
            foreach(var source in sources.OrderByDescending(s=>s.Name==sourceName))
                if(TryMap(node.mesh,source,out var geometry,out var polygons))
                {node.geometryVertices=geometry;if(source.Faces.Any(f=>f.Length!=3))node.cutPolygons=polygons;node.cutTriangles=node.mesh.triangles;return true;}
            return false;
        }
        internal static bool TryMap(Mesh mesh,SourceMesh source,out int[] geometry,out int[] polygons)
        {
            geometry=null;polygons=null;var vertices=mesh.vertices;var triangles=mesh.triangles;
            if(vertices.Length==0||source.Points.Length==0||source.Faces.Length==0||source.Faces.Any(f=>f.Length<3||f.Any(i=>i<0||i>=source.Points.Length)||f.Distinct().Count()!=f.Length))return false;
            if(source.Faces.Sum(f=>(long)f.Length-2)!=triangles.Length/3)return false;
            var targetBounds=new Bounds(vertices[0],Vector3.zero);foreach(var p in vertices)targetBounds.Encapsulate(p);
            var used=source.Faces.SelectMany(f=>f).Distinct().ToArray();var sourceBounds=new Bounds(source.Points[used[0]],Vector3.zero);foreach(int i in used)sourceBounds.Encapsulate(source.Points[i]);
            float tolerance=Mathf.Max(targetBounds.size.magnitude*1e-6f,1e-7f);
            var incident=new List<int>[source.Points.Length];for(int i=0;i<incident.Length;i++)incident[i]=new List<int>();
            for(int f=0;f<source.Faces.Length;f++)foreach(int p in source.Faces[f])incident[p].Add(f);
            var materials=new List<int>();for(int s=0;s<mesh.subMeshCount;s++)for(int i=0;i<mesh.GetIndices(s).Length;i+=3)materials.Add(s);
            var permutations=new[]{new[]{0,1,2},new[]{0,2,1},new[]{1,0,2},new[]{1,2,0},new[]{2,0,1},new[]{2,1,0}};
            foreach(var axes in permutations)for(int signs=0;signs<8;signs++)
            {
                var mapped=new Vector3[source.Points.Length];bool possible=true;
                for(int a=0;a<3;a++)if((sourceBounds.size[axes[a]]<tolerance)!=(targetBounds.size[a]<tolerance))possible=false;
                if(!possible)continue;
                var buckets=new Dictionary<(int,int,int),List<int>>();
                (int,int,int) Cell(Vector3 p)=>(Mathf.FloorToInt((p.x-targetBounds.min.x)/tolerance),Mathf.FloorToInt((p.y-targetBounds.min.y)/tolerance),Mathf.FloorToInt((p.z-targetBounds.min.z)/tolerance));
                foreach(int i in used)
                {
                    Vector3 p=Vector3.zero;
                    for(int a=0;a<3;a++){int axis=axes[a];float fraction=sourceBounds.size[axis]>0?(source.Points[i][axis]-sourceBounds.min[axis])/sourceBounds.size[axis]:0;if((signs&(1<<a))!=0)fraction=1-fraction;p[a]=targetBounds.min[a]+fraction*targetBounds.size[a];}
                    mapped[i]=p;var key=Cell(p);if(!buckets.TryGetValue(key,out var list))buckets.Add(key,list=new List<int>());list.Add(i);
                }
                var candidates=new List<int>[vertices.Length];
                for(int i=0;i<vertices.Length;i++)
                {
                    var cell=Cell(vertices[i]);var list=candidates[i]=new List<int>();
                    for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)for(int z=-1;z<=1;z++)
                        if(buckets.TryGetValue((cell.Item1+x,cell.Item2+y,cell.Item3+z),out var bucket))foreach(int cp in bucket)if((mapped[cp]-vertices[i]).sqrMagnitude<=tolerance*tolerance)list.Add(cp);
                    if(list.Count==0){possible=false;break;}
                }
                if(!possible)continue;
                var ids=Enumerable.Repeat(-1,vertices.Length).ToArray();var counts=new int[source.Faces.Length];
                var corners=source.Faces.Select(f=>Enumerable.Repeat(-1,f.Length).ToArray()).ToArray();var faceMaterial=Enumerable.Repeat(-1,counts.Length).ToArray();var winding=new Vector3[counts.Length];
                for(int t=0;t<triangles.Length&&possible;t+=3)
                {
                    int a=triangles[t],b=triangles[t+1],c=triangles[t+2],found=-1;
                    foreach(int face in candidates[a].SelectMany(cp=>incident[cp]).Distinct())
                    {
                        var f=source.Faces[face];
                        if(!candidates[b].Any(cp=>Array.IndexOf(f,cp)>=0)||!candidates[c].Any(cp=>Array.IndexOf(f,cp)>=0))continue;
                        if(found>=0){possible=false;break;}found=face;
                    }
                    if(!possible||found<0){possible=false;break;}
                    var sourceFace=source.Faces[found];
                    foreach(int v in new[]{a,b,c})
                    {
                        var matches=candidates[v].Where(cp=>Array.IndexOf(sourceFace,cp)>=0).ToArray();
                        if(matches.Length!=1||(ids[v]>=0&&ids[v]!=matches[0])){possible=false;break;}
                        int cp=matches[0];ids[v]=cp;corners[found][Array.IndexOf(sourceFace,cp)]=v;
                    }
                    if(faceMaterial[found]>=0&&faceMaterial[found]!=materials[t/3])possible=false;
                    faceMaterial[found]=materials[t/3];counts[found]++;winding[found]+=Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]);
                }
                if(!possible||ids.Any(i=>i<0))continue;
                var result=new List<int>();
                for(int f=0;f<counts.Length;f++)
                {
                    if(counts[f]!=source.Faces[f].Length-2||corners[f].Any(c=>c<0)){possible=false;break;}
                    var face=corners[f];Vector3 normal=Vector3.zero;for(int i=0;i<face.Length;i++)normal+=Vector3.Cross(vertices[face[i]]-vertices[face[0]],vertices[face[(i+1)%face.Length]]-vertices[face[0]]);
                    if(Vector3.Dot(normal,winding[f])<0)Array.Reverse(face);
                    result.Add(faceMaterial[f]);result.Add(face.Length);result.AddRange(face);
                }
                if(!possible)continue;
                geometry=ids;polygons=result.ToArray();return true;
            }
            return false;
        }
    }
}
