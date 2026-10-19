using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    // Self-contained snapshots: moving, editing or deleting a source cannot invalidate a setup.
    [Serializable]
    public sealed class StudioBlendShape
    {
        [Serializable]
        public sealed class Shape
        {
            public string name;
            public Vector3[] positions, normals;
            public Vector4[] tangents;
            public Vector2[] uv;
            public float[] vertexAO;
        }
        public Shape[] shapes;
        public float[] weights;
        public bool sequential = true;
        public float master;

        public StudioBlendShape Copy() => JsonUtility.FromJson<StudioBlendShape>(JsonUtility.ToJson(this));

        public float[] EffectiveWeights()
        {
            var result = new float[shapes.Length - 1];
            if (!sequential) { for (int i=0;i<result.Length;i++) result[i]=Mathf.Clamp01(weights[i]); return result; }
            float position = Mathf.Clamp01(master) * result.Length;
            int from = Mathf.Min(Mathf.FloorToInt(position), shapes.Length - 2);
            float t = position - from;
            if (from > 0) result[from - 1] = 1 - t;
            result[from] = t;
            return result;
        }
        public void SetWeight(int index, float value)
        {
            weights = EffectiveWeights(); sequential = false; weights[index] = Mathf.Clamp01(value);
        }
        public void SetMaster(float value) { master = Mathf.Clamp01(value); sequential = true; }

        public void Apply(Mesh mesh)
        {
            var b = shapes[0]; int count = b.positions.Length;
            if (mesh.vertexCount != count) throw new InvalidOperationException("Blend shape topology changed. Bake the deformation before editing components.");
            var w = EffectiveWeights();
            var v = (Vector3[])b.positions.Clone(); var n = (Vector3[])b.normals.Clone();
            var t = (Vector4[])b.tangents.Clone(); var uv = (Vector2[])b.uv.Clone();
            int dominant=0; float dominantWeight=1-w.Sum();
            for (int s=0;s<w.Length;s++)
            {
                if (w[s]>dominantWeight) { dominant=s+1; dominantWeight=w[s]; }
                if (w[s]<=0) continue;
                var target=shapes[s+1];
                for (int i=0;i<count;i++)
                {
                    v[i] += (target.positions[i]-b.positions[i])*w[s];
                    if (n.Length==count) n[i] += (target.normals[i]-b.normals[i])*w[s];
                    if (t.Length==count) t[i] += (target.tangents[i]-b.tangents[i])*w[s];
                    if (uv.Length==count) uv[i] += (target.uv[i]-b.uv[i])*w[s];
                }
            }
            for (int i=0;i<n.Length;i++) n[i]=n[i].sqrMagnitude>1e-12f ? n[i].normalized : b.normals[i];
            for (int i=0;i<t.Length;i++)
            {
                Vector3 direction=t[i];
                if (n.Length==count) direction-=n[i]*Vector3.Dot(n[i],direction);
                direction=direction.sqrMagnitude>1e-12f ? direction.normalized : (Vector3)b.tangents[i];
                t[i]=new Vector4(direction.x,direction.y,direction.z,shapes[dominant].tangents[i].w);
            }
            var colors=mesh.colors;
            bool hasAO=shapes.Any(s=>s.vertexAO!=null&&s.vertexAO.Length==count);
            if(hasAO)
            {
                if(colors.Length!=count)colors=Enumerable.Repeat(Color.white,count).ToArray();
                for(int i=0;i<count;i++)
                {
                    float basis=b.vertexAO!=null&&b.vertexAO.Length==count ? b.vertexAO[i] : 1;
                    float ao=basis;
                    for(int s=0;s<w.Length;s++) { var channel=shapes[s+1].vertexAO;ao+=((channel!=null&&channel.Length==count?channel[i]:1)-basis)*w[s]; }
                    colors[i].r=Mathf.Clamp01(ao);
                }
                mesh.colors=colors;
            }
            mesh.vertices=v;
            if (n.Length==count) mesh.normals=n; else mesh.RecalculateNormals();
            if (uv.Length==count) mesh.uv=uv;
            if (t.Length==count) mesh.tangents=t; else if (uv.Length==count) mesh.RecalculateTangents();
            mesh.RecalculateBounds();
        }

        public static StudioBlendShape Capture(Mesh[] meshes, string[] names, Matrix4x4[] matrices)
        {
            if (meshes.Length<2 || names.Length!=meshes.Length || matrices.Length!=meshes.Length)
                throw new ArgumentException("Select at least two mesh shapes, in hierarchy order.");
            foreach (var mesh in meshes)
                if (!mesh || !mesh.isReadable || mesh.vertexCount==0) throw new InvalidOperationException("Every shape must have a readable, non-empty mesh.");
            int count=meshes[0].vertexCount;
            var ids=meshes.Select(ReadIds).ToArray();
            bool mapped=ids.All(x=>x!=null);
            if (ids.Any(x=>x!=null) && !mapped) throw new InvalidOperationException("All shapes must have matching stable vertex IDs, or none of them.");
            if (!mapped) ids=meshes.Select(m=>Enumerable.Range(0,m.vertexCount).ToArray()).ToArray();
            var triangles=Topology(meshes[0],ids[0]);
            var result=new StudioBlendShape { shapes=new Shape[meshes.Length], weights=new float[meshes.Length-1] };
            for (int s=0;s<meshes.Length;s++)
            {
                var mesh=meshes[s];
                if (mesh.vertexCount!=count || mesh.subMeshCount!=meshes[0].subMeshCount || !triangles.SequenceEqual(Topology(mesh,ids[s])))
                    throw new InvalidOperationException(names[s]+": vertex count and oriented topology must match the first shape. Equal counts alone are not enough.");
                var inverse=new int[count]; for(int i=0;i<count;i++) inverse[ids[s][i]]=i;
                var positions=mesh.vertices;var normals=mesh.normals;var tangents=mesh.tangents;var uv=mesh.uv;var colors=mesh.colors;
                if (normals.Length!=meshes[0].normals.Length || tangents.Length!=meshes[0].tangents.Length || uv.Length!=meshes[0].uv.Length)
                    throw new InvalidOperationException(names[s]+": normal, tangent and UV channels must match the base shape.");
                var shape=new Shape { name=names[s],positions=new Vector3[count],normals=new Vector3[normals.Length],tangents=new Vector4[tangents.Length],uv=new Vector2[uv.Length] };
                shape.vertexAO=colors.Length==count?new float[count]:null;
                var matrix=matrices[s];
                if (Mathf.Abs(matrix.determinant)<1e-8f) throw new InvalidOperationException("Cannot blend a shape with zero scale.");
                var normalMatrix=matrix.inverse.transpose;
                for (int i=0;i<count;i++)
                {
                    int source=inverse[ids[0][i]];
                    // Align origins: layout translations are deliberately excluded.
                    shape.positions[i]=matrix.MultiplyVector(positions[source]);
                    if (normals.Length==count) shape.normals[i]=normalMatrix.MultiplyVector(normals[source]).normalized;
                    if (tangents.Length==count) { var t=matrix.MultiplyVector(tangents[source]).normalized;shape.tangents[i]=new Vector4(t.x,t.y,t.z,tangents[source].w*(matrix.determinant<0?-1:1)); }
                    if (uv.Length==count) shape.uv[i]=uv[source];
                    if(shape.vertexAO!=null)shape.vertexAO[i]=colors[source].r;
                }
                result.shapes[s]=shape;
            }
            return result;
        }
        private static int[] ReadIds(Mesh mesh)
        {
            var uv=mesh.uv2; int count=mesh.vertexCount;
            if (uv.Length!=count || !uv.Any(v=>Mathf.Abs(v.y-count)<.001f)) return null;
            var result=new int[count]; var used=new HashSet<int>();
            for (int i=0;i<count;i++)
            {
                int id=Mathf.RoundToInt(uv[i].x)-1;
                if (id<0 || id>=count || Mathf.Abs(uv[i].x-id-1)>.001f || Mathf.Abs(uv[i].y-count)>.001f || !used.Add(id))
                    throw new InvalidOperationException(mesh.name+": invalid or duplicated stable vertex IDs.");
                result[i]=id;
            }
            return result;
        }
        private static string[] Topology(Mesh mesh,int[] ids)
        {
            var result=new List<string>();
            for(int s=0;s<mesh.subMeshCount;s++)
            {
                if (mesh.GetTopology(s)!=MeshTopology.Triangles) throw new InvalidOperationException("Blend shapes require triangle meshes.");
                var indices=mesh.GetTriangles(s);
                for(int i=0;i<indices.Length;i+=3)
                {
                    int a=ids[indices[i]],b=ids[indices[i+1]],c=ids[indices[i+2]];
                    if (b<a && b<c) { int old=a;a=b;b=c;c=old; }
                    else if(c<a && c<b) { int old=a;a=c;c=b;b=old; }
                    result.Add(s+":"+a+","+b+","+c);
                }
            }
            result.Sort(StringComparer.Ordinal); return result.ToArray();
        }
    }
}
