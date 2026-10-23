using Undo = MashBoxSDK.AnimationStudio.StudioUndo;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MashBoxSDK.AnimationStudio
{
    // Resamples each alpha-card patch in its source UV domain. Wood is fitted to the
    // source surface with radial sections; every variant uses the same slots/indices.
    public static class StudioFoliageMorphBuilder
    {
        sealed class Part
        {
            public int[] vertices, triangles;
            public Vector3 center;
            public float area;
        }
        sealed class Input
        {
            public Vector3[] p,n;
            public Vector2[] uv;
            public List<Part> wood,leaf;
        }
        sealed class Output
        {
            public List<Vector3> p = new(), n = new();
            public List<Vector2> uv = new();
            public List<Color> colors = new();
            public List<int> triangles = new();
            public void Vertex(Vector3 point, Vector3 normal, Vector2 texcoord, bool leaf)
            { p.Add(point); n.Add(normal.sqrMagnitude > .01f ? normal.normalized : Vector3.up); uv.Add(texcoord); colors.Add(leaf ? Color.white : new Color(0,0,0,1)); }
        }
        public static void Build(StudioFoliageMorphSet recipe)
        {
            if (!recipe || recipe.sources == null || recipe.sources.Length != 8 || recipe.sources.Any(s => s == null || !s.mesh))
                throw new ArgumentException("Assign all eight source meshes.");
            if (recipe.barkSubmesh == recipe.foliageSubmesh || recipe.barkSubmesh < 0 || recipe.foliageSubmesh < 0)
                throw new ArgumentException("Wood and foliage need distinct submeshes.");
            if (recipe.vertexBudget < 128 || recipe.vertexBudget > 65535 || recipe.foliageBoundaryVertices < 5 || recipe.trunkSides < 3 || recipe.trunkRings < 2 || recipe.branchSides < 3 || recipe.branchRings < 2)
                throw new ArgumentException("Invalid topology settings.");
            string path = AssetDatabase.GetAssetPath(recipe);
            if (!path.StartsWith("Assets/", StringComparison.Ordinal)) throw new ArgumentException("Save the recipe under Assets first.");
            string stem = path.Substring(0,path.Length-6);
            var inputs = recipe.sources.Select(s => Read(s,recipe)).ToArray();
            int leaves = inputs.Max(s => s.leaf.Count), branches = inputs.Max(s => s.wood.Count-1);
            int fixedCount = (recipe.trunkSides+1)*recipe.trunkRings + branches*(recipe.branchSides+1)*recipe.branchRings;
            int leafCapacity = (recipe.vertexBudget-fixedCount)/(recipe.foliageBoundaryVertices+1);
            if (leafCapacity < 1) throw new InvalidOperationException("The wood topology alone exceeds the budget. Reduce rings/sides.");
            int leafSlots = Mathf.Min(leaves,leafCapacity);
            Vector2 lo = new(float.PositiveInfinity,float.PositiveInfinity), hi = new(float.NegativeInfinity,float.NegativeInfinity);
            foreach(var input in inputs) foreach(var part in input.wood) foreach(int v in part.vertices) { lo=Vector2.Min(lo,input.uv[v]); hi=Vector2.Max(hi,input.uv[v]); }
            Vector2 uvSize = Vector2.Max(hi-lo,Vector2.one*.001f);
            var meshes = new List<Mesh>();
            string report = $"{fixedCount+leafSlots*(recipe.foliageBoundaryVertices+1)} vertices per shape; {leafSlots} foliage slots; {branches} wood branch slots.\n";
            try
            {
                for(int index=0;index<8;index++)
                {
                    var source=inputs[index]; var output=new Output();
                    Tube(source,source.wood[0],recipe.trunkSides,recipe.trunkRings,true,lo,uvSize,recipe.atlasSize,output);
                    for(int b=0;b<branches;b++) Tube(source,b+1<source.wood.Count ? source.wood[b+1] : null,recipe.branchSides,recipe.branchRings,false,lo,uvSize,recipe.atlasSize,output);
                    var patches=source.leaf.OrderByDescending(p=>p.area).Take(leafSlots).OrderBy(p=>p.center.y).ThenBy(p=>Mathf.Atan2(p.center.z,p.center.x)).ToArray();
                    for(int f=0;f<leafSlots;f++) Fan(source,f<patches.Length ? patches[f] : null,recipe.foliageBoundaryVertices,recipe.atlasSize,output);
                    var mesh=new Mesh {name=$"Shape_{index:00}_{recipe.sources[index].name}"};
                    meshes.Add(mesh); mesh.SetVertices(output.p); mesh.SetNormals(output.n); mesh.SetUVs(0,output.uv); mesh.SetColors(output.colors);
                    mesh.SetUVs(1,Enumerable.Range(0,output.p.Count).Select(i=>new Vector2(i+1,output.p.Count)).ToList());
                    mesh.SetTriangles(output.triangles,0); mesh.RecalculateTangents(); mesh.RecalculateBounds();
                    Validate(mesh,recipe.vertexBudget);
                    if(index>0 && !mesh.triangles.SequenceEqual(meshes[0].triangles)) throw new InvalidOperationException("Topology mismatch.");
                    report += $"{index}: {recipe.sources[index].name}: {source.leaf.Count} source patches, {patches.Length} retained; {mesh.triangles.Length/3} triangles.\n";
                }
                // Validate all geometry before replacing outputs. GUIDs stay stable on rebuild.
                StudioFoliageAtlas.Build(recipe,stem,lo,uvSize);
                var doc=recipe.document;
                if(!doc) { doc=ScriptableObject.CreateInstance<StudioModelDocument>(); AssetDatabase.CreateAsset(doc,stem+"_Studio.asset"); recipe.document=doc; }
                Undo.RegisterCompleteObjectUndo(doc,"Rebuild foliage morph shapes");
                var previousMeshes=doc.nodes.Where(n=>n.mesh).Select(n=>n.mesh).ToArray();
                doc.nodes.Clear();
                for(int i=0;i<8;i++)
                {
                    Mesh owned=i<previousMeshes.Length ? previousMeshes[i] : null;
                    if(owned) { Undo.RegisterCompleteObjectUndo(owned,"Rebuild foliage mesh"); EditorUtility.CopySerialized(meshes[i],owned); Object.DestroyImmediate(meshes[i]); }
                    else { owned=meshes[i]; AssetDatabase.AddObjectToAsset(owned,doc); }
                    meshes[i]=null;
                    doc.nodes.Add(new StudioModelDocument.Node {name=owned.name,mesh=owned,position=new Vector3((i%4)*16,0,(i/4)*32),materials=Array.Empty<Material>(),sourcePath=AssetDatabase.GetAssetPath(recipe.sources[i].mesh)});
                    EditorUtility.SetDirty(owned);
                }
                // Extra pre-existing nodes are not deleted: preserve their assets for Undo.
                recipe.report=report; EditorUtility.SetDirty(doc); EditorUtility.SetDirty(recipe);
                AssetDatabase.SaveAssetIfDirty(doc); AssetDatabase.SaveAssetIfDirty(recipe);
            }
            finally {foreach(var mesh in meshes) if(mesh && !AssetDatabase.Contains(mesh)) Object.DestroyImmediate(mesh);}
        }
        public static void Validate(Mesh mesh,int budget)
        {
            if(mesh.vertexCount>budget || mesh.subMeshCount!=1 || mesh.uv.Length!=mesh.vertexCount || mesh.normals.Length!=mesh.vertexCount) throw new InvalidOperationException("Invalid shared shape.");
            foreach(Vector3 p in mesh.vertices) if(!float.IsFinite(p.x)||!float.IsFinite(p.y)||!float.IsFinite(p.z)) throw new InvalidOperationException("Non-finite vertex.");
            foreach(Vector2 p in mesh.uv) if(!float.IsFinite(p.x)||!float.IsFinite(p.y)||p.x<0||p.x>1||p.y<0||p.y>1) throw new InvalidOperationException("UV outside combined atlas.");
        }
        static Input Read(StudioFoliageMorphSet.Source source,StudioFoliageMorphSet recipe)
        {
            Mesh mesh=source.mesh;
            if(mesh.subMeshCount<=Mathf.Max(recipe.barkSubmesh,recipe.foliageSubmesh)) throw new ArgumentException(source.name+": missing submesh.");
            if(source.scale.x<=0||source.scale.y<=0||source.scale.z<=0) throw new ArgumentException(source.name+": scales must be positive.");
            var matrix=Matrix4x4.TRS(Vector3.zero,Quaternion.Euler(source.rotation),source.scale);
            var input=new Input {p=mesh.vertices.Select(matrix.MultiplyPoint3x4).ToArray(),n=mesh.normals.Select(v=>matrix.inverse.transpose.MultiplyVector(v).normalized).ToArray(),uv=mesh.uv};
            if(input.uv.Length!=input.p.Length||input.n.Length!=input.p.Length) throw new ArgumentException(source.name+": requires normals and UV0.");
            input.wood=Parts(input,mesh.GetTriangles(recipe.barkSubmesh)).Where(p=>p.vertices.Length>=10).OrderByDescending(p=>p.area).ToList();
            input.leaf=Parts(input,mesh.GetTriangles(recipe.foliageSubmesh));
            if(input.wood.Count==0||input.leaf.Count==0) throw new ArgumentException(source.name+": wood and foliage are required.");
            return input;
        }
        static List<Part> Parts(Input input,int[] triangles)
        {
            var parent=Enumerable.Range(0,input.p.Length).ToArray();
            int Root(int x) {while(parent[x]!=x){parent[x]=parent[parent[x]];x=parent[x];}return x;}
            for(int i=0;i<triangles.Length;i+=3) {parent[Root(triangles[i+1])]=Root(triangles[i]);parent[Root(triangles[i+2])]=Root(triangles[i]);}
            var groups=new Dictionary<int,List<int>>();
            for(int i=0;i<triangles.Length;i+=3){int key=Root(triangles[i]);if(!groups.TryGetValue(key,out var list))groups[key]=list=new List<int>();list.AddRange(new[]{triangles[i],triangles[i+1],triangles[i+2]});}
            return groups.Values.Select(t=> {var p=new Part {triangles=t.ToArray(),vertices=t.Distinct().ToArray()};foreach(int v in p.vertices)p.center+=input.p[v]/p.vertices.Length;for(int i=0;i<t.Count;i+=3)p.area+=Vector3.Cross(input.p[t[i+1]]-input.p[t[i]],input.p[t[i+2]]-input.p[t[i]]).magnitude*.5f;return p;}).ToList();
        }
        static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
        static Vector2 AtlasUV(Vector2 uv,bool leaf,int size)
        {
            float pad=8f/size;
            return leaf ? new Vector2(pad+uv.x*(.75f-2*pad),pad+uv.y*(1-2*pad)) : new Vector2(.75f+pad+uv.x*(.25f-2*pad),pad+uv.y*(1-2*pad));
        }
        static void Fan(Input input,Part part,int count,int size,Output o)
        {
            int start=o.p.Count;
            if(part==null) { for(int i=0;i<=count;i++)o.Vertex(Vector3.zero,Vector3.up,AtlasUV(Vector2.zero,true,size),true); }
            else
            {
                var points=part.vertices.Select(i=>input.uv[i]).Distinct().OrderBy(p=>p.x).ThenBy(p=>p.y).ToList();
                var hull=new List<Vector2>();
                foreach(var p in points){while(hull.Count>=2&&Cross(hull[^1]-hull[^2],p-hull[^1])<=0)hull.RemoveAt(hull.Count-1);hull.Add(p);}
                int lower=hull.Count;
                for(int j=points.Count-2;j>=0;j--){var p=points[j];while(hull.Count>lower&&Cross(hull[^1]-hull[^2],p-hull[^1])<=0)hull.RemoveAt(hull.Count-1);hull.Add(p);} hull.RemoveAt(hull.Count-1);
                if(hull.Count<3) throw new InvalidOperationException("Degenerate foliage UV island.");
                // Preserve original corners when possible. Insert midpoints on longest edges.
                while(hull.Count<count){int e=Enumerable.Range(0,hull.Count).OrderByDescending(i=>(hull[i]-hull[(i+1)%hull.Count]).sqrMagnitude).First();hull.Insert(e+1,(hull[e]+hull[(e+1)%hull.Count])*.5f);}
                while(hull.Count>count){int e=Enumerable.Range(0,hull.Count).OrderBy(i=>Mathf.Abs(Cross(hull[i]-hull[(i+hull.Count-1)%hull.Count],hull[(i+1)%hull.Count]-hull[i]))).First();hull.RemoveAt(e);}
                float winding=0;for(int i=0;i<part.triangles.Length;i+=3)winding+=Cross(input.uv[part.triangles[i+1]]-input.uv[part.triangles[i]],input.uv[part.triangles[i+2]]-input.uv[part.triangles[i]]);
                if(winding<0)hull.Reverse();
                Vector2 center=Vector2.zero;foreach(var p in hull)center+=p/count;
                foreach(var uv in new[]{center}.Concat(hull)) {Sample(input,part,uv,out var p,out var n);o.Vertex(p,n,AtlasUV(uv,true,size),true);}
            }
            for(int i=0;i<count;i++)o.triangles.AddRange(new[]{start,start+1+i,start+1+(i+1)%count});
        }
        static void Sample(Input input,Part part,Vector2 uv,out Vector3 p,out Vector3 n)
        {
            p=Vector3.zero;n=Vector3.up;float best=float.PositiveInfinity;
            for(int i=0;i<part.triangles.Length;i+=3)
            {
                int a=part.triangles[i],b=part.triangles[i+1],c=part.triangles[i+2];
                Vector2 ab=input.uv[b]-input.uv[a],ac=input.uv[c]-input.uv[a],ap=uv-input.uv[a];float det=Cross(ab,ac);if(Mathf.Abs(det)<1e-10f)continue;
                float v=Cross(ap,ac)/det,w=Cross(ab,ap)/det,u=1-v-w;
                float error=Mathf.Max(0,-u)+Mathf.Max(0,-v)+Mathf.Max(0,-w);
                if(error>=best)continue;best=error;u=Mathf.Max(0,u);v=Mathf.Max(0,v);w=Mathf.Max(0,w);float sum=u+v+w;
                p=(input.p[a]*u+input.p[b]*v+input.p[c]*w)/sum;n=(input.n[a]*u+input.n[b]*v+input.n[c]*w).normalized;
                if(best<1e-6f)break;
            }
        }
        static void Tube(Input input,Part part,int sides,int rings,bool trunk,Vector2 lo,Vector2 uvSize,int size,Output o)
        {
            int start=o.p.Count;
            if(part==null){for(int i=0;i<rings*(sides+1);i++)o.Vertex(Vector3.zero,Vector3.up,AtlasUV(Vector2.zero,false,size),false);}
            else
            {
                Vector3 axis=Vector3.up;
                if(!trunk) {axis=(input.p[part.vertices[0]]-part.center).normalized;for(int it=0;it<12;it++){Vector3 next=Vector3.zero;foreach(int v in part.vertices){Vector3 d=input.p[v]-part.center;next+=d*Vector3.Dot(d,axis);}if(next.sqrMagnitude>1e-10f)axis=next.normalized;}}
                Vector3 x=Vector3.Cross(axis,Mathf.Abs(axis.y)<.9f?Vector3.up:Vector3.forward).normalized,z=Vector3.Cross(axis,x).normalized;
                float min=part.vertices.Min(v=>Vector3.Dot(input.p[v],axis)),max=part.vertices.Max(v=>Vector3.Dot(input.p[v],axis));
                for(int r=0;r<rings;r++)
                {
                    float height=Mathf.Lerp(min,max,(r+.001f)/(rings-1+.002f));
                    var nearest=part.vertices.OrderBy(v=>Mathf.Abs(Vector3.Dot(input.p[v],axis)-height)).Take(Mathf.Min(trunk?12:6,part.vertices.Length)).ToArray();
                    Vector3 center=Vector3.zero;foreach(int v in nearest)center+=input.p[v]/nearest.Length;center+=axis*(height-Vector3.Dot(center,axis));
                    for(int s=0;s<=sides;s++)
                    {
                        float angle=s*Mathf.PI*2/sides;Vector3 direction=x*Mathf.Cos(angle)+z*Mathf.Sin(angle);
                        Vector3 p=center,n=direction;Vector2 uv=Vector2.zero;float best=float.PositiveInfinity;
                        for(int t=0;t<part.triangles.Length;t+=3)
                        {
                            int a=part.triangles[t],b=part.triangles[t+1],c=part.triangles[t+2];
                            Vector3 e1=input.p[b]-input.p[a],e2=input.p[c]-input.p[a],h=Vector3.Cross(direction,e2);float det=Vector3.Dot(e1,h);if(Mathf.Abs(det)<1e-8f)continue;
                            Vector3 d=center-input.p[a];float u=Vector3.Dot(d,h)/det;if(u<0||u>1)continue;Vector3 q=Vector3.Cross(d,e1);float v=Vector3.Dot(direction,q)/det;if(v<0||u+v>1)continue;float dist=Vector3.Dot(e2,q)/det;
                            if(dist<0||dist>=best)continue;best=dist;p=center+direction*dist;n=(input.n[a]*(1-u-v)+input.n[b]*u+input.n[c]*v).normalized;uv=input.uv[a]*(1-u-v)+input.uv[b]*u+input.uv[c]*v;
                        }
                        if(float.IsPositiveInfinity(best)) {int v=nearest.OrderByDescending(v=>Vector3.Dot((input.p[v]-center).normalized,direction)).First();float radius=Vector3.ProjectOnPlane(input.p[v]-center,axis).magnitude;p=center+direction*radius;uv=input.uv[v];}
                        o.Vertex(p,n,AtlasUV(new Vector2((uv.x-lo.x)/uvSize.x,(uv.y-lo.y)/uvSize.y),false,size),false);
                    }
                }
            }
            for(int r=0;r<rings-1;r++)for(int s=0;s<sides;s++){int a=start+r*(sides+1)+s,b=a+sides+1;o.triangles.AddRange(new[]{a,a+1,b,a+1,b+1,b});}
        }
    }
}
