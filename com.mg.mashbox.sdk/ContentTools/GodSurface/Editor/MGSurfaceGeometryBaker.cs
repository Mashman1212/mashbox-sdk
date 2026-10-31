using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace MG.GodSurface.Editor
{
    public static class MGSurfaceGeometryBaker
    {
        public static MGSurfaceGeometry Bake(MGSurfaceWorld world)
        {
            if (!world || !world.surfaceLibrary) throw new InvalidOperationException("Cook surface materials before geometry.");
            bool active = world.IsRendering; world.Release();
            string folder = null, carrierPath = null;
            try
            {
                var library = world.surfaceLibrary;
                var meshes = world.EnumerateRenderers().Where(r => world.UnsupportedReason(r) == null && r.sharedMaterials.All(m => library.TryResolve(m, out _, out _)))
                    .Select(r => r.GetComponent<MeshFilter>().sharedMesh).Distinct().OrderBy(AssetDatabase.GetAssetPath, StringComparer.Ordinal).ThenBy(m => m.name).ToArray();
                if (meshes.Length == 0) throw new InvalidOperationException("No eligible God Surface geometry found.");
                var vertexPixels = new List<Color>(); var indexPixels = new List<float>(); var chunkPixels = new List<Color>();
                var shapes = new List<MGSurfaceGeometry.Shape>();
                foreach (var source in meshes)
                {
                    var mesh = MGSurfaceVertexAO.ReadableCopy(source);
                    try
                    {
                        int startVertex = vertexPixels.Count / 5;
                        if (mesh.normals.Length != mesh.vertexCount) mesh.RecalculateNormals();
                        if (mesh.tangents.Length != mesh.vertexCount && mesh.uv.Length == mesh.vertexCount) mesh.RecalculateTangents();
                        var p = mesh.vertices; var n = mesh.normals; var t = mesh.tangents; var c = mesh.colors;
                        var uv = new Vector2[4][] { mesh.uv, mesh.uv2, mesh.uv3, mesh.uv4 };
                        Vector2 UV(int channel, int v) => uv[channel].Length == p.Length ? uv[channel][v] : Vector2.zero;
                        for (int v = 0; v < p.Length; v++)
                        {
                            var tangent = t.Length == p.Length ? t[v] : new Vector4(1, 0, 0, 1);
                            vertexPixels.Add(new Color(p[v].x, p[v].y, p[v].z, c.Length == p.Length ? c[v].r : 1));
                            vertexPixels.Add(new Color(n[v].x, n[v].y, n[v].z, 0));
                            vertexPixels.Add(new Color(tangent.x, tangent.y, tangent.z, tangent.w));
                            var a = UV(0,v); var b = UV(1,v); vertexPixels.Add(new Color(a.x,a.y,b.x,b.y));
                            a = UV(2,v); b = UV(3,v); vertexPixels.Add(new Color(a.x,a.y,b.x,b.y));
                        }
                        var shape = new MGSurfaceGeometry.Shape { source = source, parts = new MGSurfaceGeometry.Part[mesh.subMeshCount] };
                        for (int part = 0; part < mesh.subMeshCount; part++)
                        {
                            var index = mesh.GetIndices(part, true);
                            if (index.Length % 3 != 0) throw new InvalidOperationException("Non-triangle index count: " + source.name);
                            int offset = indexPixels.Count;
                            foreach (int v in index) indexPixels.Add(startVertex + v);
                            var ids = new List<int>();
                            for (int i = 0; i < index.Length; i += MGSurfaceGeometry.CornersPerChunk)
                            { ids.Add(chunkPixels.Count); chunkPixels.Add(new Color(offset + i, Math.Min(MGSurfaceGeometry.CornersPerChunk, index.Length - i), 0, 0)); }
                            shape.parts[part] = new MGSurfaceGeometry.Part { chunks = ids.ToArray() };
                        }
                        shapes.Add(shape);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(mesh); }
                }
                if (indexPixels.Count >= 16777216 || vertexPixels.Count / 5 >= 16777216) throw new InvalidOperationException("Geometry atlas exceeds exact float indexing capacity. Split the library.");
                string parent = Path.GetDirectoryName(AssetDatabase.GetAssetPath(library)).Replace('\\','/');
                folder = AssetDatabase.GenerateUniqueAssetPath(parent + "/Geometry"); AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
                var data = ScriptableObject.CreateInstance<MGSurfaceGeometry>(); data.shapes = shapes.ToArray();
                data.vertexCount = vertexPixels.Count / 5; data.indexCount = indexPixels.Count; data.chunkCount = chunkPixels.Count;
                data.vertices = Pixels(vertexPixels, "Geometry vertex attributes"); data.chunks = Pixels(chunkPixels, "Geometry piece offsets");
                int width = Math.Min(1024, Mathf.NextPowerOfTwo(Math.Max(1, indexPixels.Count))), height = (indexPixels.Count + width - 1) / width;
                if (height > SystemInfo.maxTextureSize) throw new InvalidOperationException("Geometry index atlas exceeds device texture size.");
                var indexData = new float[width * height]; indexPixels.CopyTo(indexData);
                data.indices = new Texture2D(width, height, TextureFormat.RFloat, false, true) { name = "Geometry indices", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                data.indices.SetPixelData(indexData, 0); data.indices.Apply(false, true);
                var carrier = new Mesh { name = "God Surface triangle carrier" };
                int count = MGSurfaceGeometry.CornersPerChunk;
                carrier.vertices = new Vector3[count]; carrier.normals = Enumerable.Repeat(Vector3.up, count).ToArray();
                carrier.tangents = Enumerable.Repeat(new Vector4(1,0,0,1), count).ToArray();
                carrier.uv = Enumerable.Range(0,count).Select(i => new Vector2(i,0)).ToArray();
                carrier.SetIndices(Enumerable.Range(0,count).ToArray(), MeshTopology.Triangles, 0);
                var bounds = meshes[0].bounds; foreach (var mesh in meshes) bounds.Encapsulate(mesh.bounds); carrier.bounds = bounds;
                data.carrier = carrier;
                AssetDatabase.CreateAsset(data.vertices, folder + "/Vertices.asset"); AssetDatabase.CreateAsset(data.indices, folder + "/Indices.asset");
                AssetDatabase.CreateAsset(data.chunks, folder + "/Pieces.asset");
                string meshFolder = parent + "/Meshes";
                if (!AssetDatabase.IsValidFolder(meshFolder)) AssetDatabase.CreateFolder(parent, "Meshes");
                carrierPath = AssetDatabase.GenerateUniqueAssetPath(meshFolder + "/SharedCarrier.asset");
                AssetDatabase.CreateAsset(carrier, carrierPath);
                // Serialize the library only after every referenced texture/mesh has a persistent GUID.
                AssetDatabase.CreateAsset(data, folder + "/Geometry.asset");
                Undo.RecordObject(library, "Assign God Surface geometry"); library.geometry = data; EditorUtility.SetDirty(library);
                AssetDatabase.SaveAssets(); return data;
            }
            catch { if (carrierPath != null) AssetDatabase.DeleteAsset(carrierPath); if (folder != null) AssetDatabase.DeleteAsset(folder); throw; }
            finally { if (active) world.RequestRebuild(); }
        }
        static Texture2D Pixels(List<Color> values, string name)
        {
            int width = Math.Min(1024, Mathf.NextPowerOfTwo(Math.Max(1, values.Count))), height = Math.Max(1,(values.Count + width - 1) / width);
            if (height > SystemInfo.maxTextureSize) throw new InvalidOperationException("Geometry vertex atlas exceeds device texture size.");
            var pixels = new Color[width*height]; values.CopyTo(pixels);
            var texture = new Texture2D(width,height,TextureFormat.RGBAFloat,false,true) { name=name, filterMode=FilterMode.Point, wrapMode=TextureWrapMode.Clamp };
            texture.SetPixels(pixels); texture.Apply(false,true); return texture;
        }
    }
}
