using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MashBoxSDK.MapTools
{
    public static class MGGrassShapeBaker
    {
        public const int SliceCount = 8;
        public const int MaximumVertices = 65535;
        public sealed class Shape
        {
            public Vector3[] positions;
            public Vector2[] uvs;
            public Color[] colors;
            public int[] triangles;
        }

        public static string PathFor(Transform root, Transform child)
        {
            if (child == root) return ".";
            var names = new List<string>();
            while (child != null && child != root) { names.Add(child.name); child = child.parent; }
            names.Reverse();
            return string.Join("/", names);
        }

        public static Shape Read(Mesh mesh, Matrix4x4 transform)
        {
            if (mesh == null) throw new InvalidOperationException("A shape has no mesh.");
            if (!mesh.isReadable) throw new InvalidOperationException(mesh.name + ": enable Read/Write on the FBX.");
            if (mesh.subMeshCount != 1 || mesh.GetTopology(0) != MeshTopology.Triangles)
                throw new InvalidOperationException(mesh.name + ": use one material/submesh with triangulated geometry.");
            var ids = new List<Vector2>();
            mesh.GetUVs(1, ids);
            Vector3[] vertices = mesh.vertices;
            Vector2[] uv = mesh.uv;
            Color[] colors = mesh.colors;
            if (ids.Count != vertices.Length || uv.Length != vertices.Length || ids.Count == 0)
                throw new InvalidOperationException(mesh.name + ": missing UV0 or MGShapeID in the second UV set. Stamp IDs in Maya before export.");
            int count = Mathf.RoundToInt(ids[0].y);
            if (count < 3 || count > MaximumVertices)
                throw new InvalidOperationException(mesh.name + ": invalid MGShapeID vertex count.");
            var result = new Shape { positions = new Vector3[count], uvs = new Vector2[count], colors = new Color[count] };
            var seen = new bool[count];
            var remap = new int[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                if (!Finite(ids[i].x) || !Finite(ids[i].y)) throw new InvalidOperationException(mesh.name + ": non-finite ID.");
                int id = Mathf.RoundToInt(ids[i].x) - 1;
                if (id < 0 || id >= count || Mathf.Abs(ids[i].x - id - 1) > .001f || Mathf.Abs(ids[i].y - count) > .001f)
                    throw new InvalidOperationException(mesh.name + ": corrupt MGShapeID. Disable mesh compression and lightmap UV generation.");
                Vector3 p = transform.MultiplyVector(vertices[i]); // Intentionally ignore authoring/display translations.
                Vector2 t = uv[i];
                Color c = colors.Length == vertices.Length ? colors[i] : Color.white;
                if (!Finite(p.x) || !Finite(p.y) || !Finite(p.z) || !Finite(t.x) || !Finite(t.y))
                    throw new InvalidOperationException(mesh.name + ": non-finite position or UV.");
                if (seen[id] && ((result.positions[id] - p).sqrMagnitude > 1e-10f || (result.uvs[id] - t).sqrMagnitude > 1e-10f || result.colors[id] != c))
                    throw new InvalidOperationException(mesh.name + $": ID {id} has conflicting UV seams, positions, or colors. Split those vertices in all source shapes before stamping IDs.");
                result.positions[id] = p; result.uvs[id] = t; result.colors[id] = c;
                seen[id] = true; remap[i] = id;
            }
            if (seen.Any(value => !value)) throw new InvalidOperationException(mesh.name + ": missing vertex IDs.");
            result.triangles = mesh.GetTriangles(0).Select(i => remap[i]).ToArray();
            if (result.triangles.Length == 0) throw new InvalidOperationException(mesh.name + ": empty mesh.");
            return result;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static string[] Topology(Shape shape)
        {
            var keys = new string[shape.triangles.Length / 3];
            for (int i = 0; i < keys.Length; i++)
            {
                int a = shape.triangles[i * 3], b = shape.triangles[i * 3 + 1], c = shape.triangles[i * 3 + 2];
                if (a == b || b == c || c == a) throw new InvalidOperationException("Repeated ID in a triangle.");
                // Ignore triangle order/cyclic rotation, but preserve winding.
                if (b < a && b < c) { int old = a; a = b; b = c; c = old; }
                else if (c < a && c < b) { int old = a; a = c; c = b; b = old; }
                keys[i] = a + ":" + b + ":" + c;
            }
            Array.Sort(keys, StringComparer.Ordinal);
            return keys;
        }

        public static void Validate(Shape[] shapes)
        {
            if (shapes == null || shapes.Length != SliceCount || shapes[0] == null)
                throw new InvalidOperationException("Assign shape 0. Exactly eight shape slots are required.");
            var topology = Topology(shapes[0]);
            for (int i = 1; i < SliceCount; i++)
                if (shapes[i] != null && (shapes[i].positions.Length != shapes[0].positions.Length || !Topology(shapes[i]).SequenceEqual(topology)))
                    throw new InvalidOperationException($"Shape {i} does not have the same vertex IDs and oriented triangles as shape 0.");
        }

        public static Shape[] ReadSource(MGGrassShapeSet recipe)
        {
            if (recipe == null || recipe.sourceFbx == null) throw new InvalidOperationException("Assign the source FBX.");
            string source = AssetDatabase.GetAssetPath(recipe.sourceFbx);
            if (!source.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Source must be a single FBX model asset.");
            if (recipe.shapePaths == null || recipe.shapePaths.Length != SliceCount) throw new InvalidOperationException("The recipe must contain eight slots.");
            var shapes = new Shape[SliceCount];
            for (int i = 0; i < SliceCount; i++)
            {
                string path = recipe.shapePaths[i];
                if (string.IsNullOrEmpty(path)) continue;
                var root = recipe.sourceFbx.transform;
                var child = path == "." ? root : root.Find(path);
                var filter = child != null ? child.GetComponent<MeshFilter>() : null;
                if (filter == null) throw new InvalidOperationException($"Shape {i}: mesh '{path}' no longer exists in the FBX.");
                Matrix4x4 matrix = recipe.bakeRotationAndScale ? root.localToWorldMatrix.inverse * child.localToWorldMatrix : Matrix4x4.identity;
                // Include the FBX root's unit/axis conversion as well.
                if (recipe.bakeRotationAndScale) matrix = root.localToWorldMatrix * matrix;
                if (matrix.determinant <= 1e-8f) throw new InvalidOperationException($"Shape {i}: freeze mirrored or zero scales in Maya before export.");
                shapes[i] = Read(filter.sharedMesh, matrix);
            }
            Validate(shapes);
            return shapes;
        }

        public static void BuildData(Shape[] shapes, float padding, out Mesh mesh, out Texture2DArray positions, out Texture2DArray uvs)
        {
            Validate(shapes);
            if (!Finite(padding) || padding < 0) throw new InvalidOperationException("Bounds padding must be finite and nonnegative.");
            int count = shapes[0].positions.Length;
            int width = Mathf.Min(256, Mathf.NextPowerOfTwo(count));
            int height = (count + width - 1) / width;
            positions = new Texture2DArray(width, height, SliceCount, TextureFormat.RGBAFloat, false, true)
            { name = "Shape Positions", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, anisoLevel = 0 };
            uvs = new Texture2DArray(width, height, SliceCount, TextureFormat.RGBAFloat, false, true)
            { name = "Shape UVs", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, anisoLevel = 0 };
            var bounds = new Bounds(shapes[0].positions[0], Vector3.zero);
            for (int slice = 0; slice < SliceCount; slice++)
            {
                Shape shape = shapes[slice] ?? shapes[0];
                var p = new Color[width * height]; var t = new Color[p.Length];
                for (int i = 0; i < count; i++)
                {
                    Vector3 v = shape.positions[i]; Vector2 uv = shape.uvs[i];
                    p[i] = new Color(v.x, v.y, v.z, 1); t[i] = new Color(uv.x, uv.y, 0, 1);
                    bounds.Encapsulate(v);
                }
                positions.SetPixels(p, slice); uvs.SetPixels(t, slice);
            }
            positions.Apply(false, false); uvs.Apply(false, false);
            mesh = new Mesh { name = "Shared Grass Shape Mesh", indexFormat = count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = shapes[0].positions; mesh.uv = shapes[0].uvs; mesh.colors = shapes[0].colors;
            var lookup = new List<Vector4>(count);
            for (int i = 0; i < count; i++) lookup.Add(new Vector4(((i % width) + .5f) / width, ((i / width) + .5f) / height, 1, 0));
            mesh.SetUVs(2, lookup); // UV2/TEXCOORD2: texel centre + baked-mesh validity marker.
            mesh.triangles = shapes[0].triangles;
            mesh.RecalculateNormals(); mesh.RecalculateTangents();
            bounds.Expand(padding * 2); mesh.bounds = bounds;
        }

        public static Texture2DArray BuildAO(Shape[] shapes)
        {
            Validate(shapes);
            if(!SystemInfo.SupportsTextureFormat(TextureFormat.R8))throw new InvalidOperationException("This device cannot store single-channel R8 shape AO.");
            int count=shapes[0].positions.Length,width=Mathf.Min(256,Mathf.NextPowerOfTwo(count)),height=(count+width-1)/width;
            var array=new Texture2DArray(width,height,SliceCount,TextureFormat.R8,false,true)
            {name="Shape Vertex AO",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp,anisoLevel=0};
            try
            {
                for(int slice=0;slice<SliceCount;slice++)
                {
                    var shape=shapes[slice]??shapes[0];var data=Enumerable.Repeat((byte)255,width*height).ToArray();
                    for(int i=0;i<count;i++)
                    {
                        float value=shape.colors!=null&&shape.colors.Length==count?shape.colors[i].r:1;
                        if(!Finite(value))throw new InvalidOperationException("AO contains a non-finite red value.");
                        data[i]=(byte)Mathf.RoundToInt(Mathf.Clamp01(value)*255);
                    }
                    array.SetPixelData(data,0,slice);
                }
                array.Apply(false,false);return array;
            }
            catch {Object.DestroyImmediate(array);throw;}
        }

        static T Store<T>(T created, string path) where T : Object
        {
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null)
            {
                if (File.Exists(path)) throw new InvalidOperationException("Another asset occupies " + path);
                AssetDatabase.CreateAsset(created, path); return created;
            }
            EditorUtility.CopySerialized(created, existing); Object.DestroyImmediate(created);
            EditorUtility.SetDirty(existing); return existing;
        }

        public static void Bake(MGGrassShapeSet recipe)
        {
            if (!SystemInfo.supports2DArrayTextures || !SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat))
                throw new InvalidOperationException("This device does not support float texture arrays.");
            Shape[] shapes = ReadSource(recipe); // Complete validation before touching outputs.
            string path = AssetDatabase.GetAssetPath(recipe);
            if (!path.StartsWith("Assets/", StringComparison.Ordinal)) throw new InvalidOperationException("Save the shape set under Assets first.");
            string stem = path.Substring(0, path.Length - Path.GetExtension(path).Length);
            BuildData(shapes, recipe.boundsPadding, out Mesh mesh, out Texture2DArray positions, out Texture2DArray uvs);
            try
            {
                recipe.bakedMesh = Store(mesh, stem + "_Mesh.asset"); mesh = null;
                recipe.positions = Store(positions, stem + "_Positions.asset"); positions = null;
                recipe.uvs = Store(uvs, stem + "_UVs.asset"); uvs = null;
                if(recipe.bakeVertexAO)recipe.vertexAO=Store(BuildAO(shapes),stem+"_AO.asset");
                EditorUtility.SetDirty(recipe); AssetDatabase.SaveAssets();
            }
            finally { if (mesh) Object.DestroyImmediate(mesh); if (positions) Object.DestroyImmediate(positions); if (uvs) Object.DestroyImmediate(uvs); }
        }

        public static void Apply(MGGrassShapeSet recipe, Material material)
        {
            if (recipe == null || recipe.bakedMesh == null || recipe.positions == null || recipe.uvs == null)
                throw new InvalidOperationException("Bake the shape set first.");
            if (material == null || !material.HasProperty("_GrassUseShapes")) throw new InvalidOperationException("Select a GodGrass material with shape support.");
            Undo.RecordObject(material, "Apply Grass Shape Arrays");
            material.SetTexture("_GrassShapePositions", recipe.positions);
            material.SetTexture("_GrassShapeUVs", recipe.uvs);
            material.SetFloat("_GrassUseShapes", 1);
            if(material.HasProperty("_GrassShapeAO"))
            {
                material.SetTexture("_GrassShapeAO",recipe.bakeVertexAO?recipe.vertexAO:null);
                material.SetFloat("_GrassUseShapeAO",recipe.bakeVertexAO&&recipe.vertexAO?1:0);
            }
            material.enableInstancing = true;
            EditorUtility.SetDirty(material); AssetDatabase.SaveAssets();
        }
    }
}
