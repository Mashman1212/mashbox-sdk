using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    public static class MGGrassShapeValidation
    {
        [MenuItem("Tools/MashBox/MG Terrain/Validate Grass Shape Baker")]
        public static void Run()
        {
            Mesh source = null, reordered = null, baked = null;
            Texture2DArray positions = null, uvs = null;
            try
            {
                source = Make(false); reordered = Make(true);
                var shapes = new MGGrassShapeBaker.Shape[8];
                shapes[0] = MGGrassShapeBaker.Read(source, Matrix4x4.identity);
                shapes[1] = MGGrassShapeBaker.Read(reordered, Matrix4x4.Scale(new Vector3(2, 3, 1)));
                MGGrassShapeBaker.BuildData(shapes, .5f, out baked, out positions, out uvs);
                Check(baked.vertexCount == 3 && baked.triangles.Length == 3, "Shared topology");
                Check(positions.depth == 8 && positions.mipmapCount == 1 && !positions.isDataSRGB && positions.filterMode == FilterMode.Point, "Position format");
                Check(uvs.depth == 8 && uvs.mipmapCount == 1 && !uvs.isDataSRGB, "UV format");
                var p = positions.GetPixels(1);
                Check((new Vector3(p[1].r,p[1].g,p[1].b) - new Vector3(2,0,0)).sqrMagnitude < 1e-10f, "Reordered IDs and transform");
                var t = uvs.GetPixels(1);
                Check(Mathf.Abs(t[1].r - .8f) < 1e-6f && Mathf.Abs(t[1].g - .2f) < 1e-6f, "UV correspondence");
                Check(positions.GetPixels(7)[2] == positions.GetPixels(0)[2], "Empty slice fallback");
                Check(baked.bounds.Contains(new Vector3(0,3,0)), "All-shape bounds");
                var lookup = new System.Collections.Generic.List<Vector4>(); baked.GetUVs(2,lookup);
                Check(lookup[0].z == 1 && Mathf.Abs(lookup[0].x - .125f) < 1e-6f, "Texel-centre lookup");
                shapes[1].triangles = new[] { 0, 2, 1 };
                ExpectFailure(() => MGGrassShapeBaker.Validate(shapes), "Mismatched winding rejection");
                reordered.uv2 = new[] { new Vector2(1,3),new Vector2(1,3),new Vector2(3,3) };
                ExpectFailure(() => MGGrassShapeBaker.Read(reordered,Matrix4x4.identity), "Conflicting duplicate ID rejection");
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "MGGrassShapeValidation.txt"), "PASS: vertex reorder, topology/winding validation, UV correspondence, transforms, eight slices, empty-slot fallback, float linear/no-mip data, lookup coordinates, union bounds, corrupt IDs.");
                Debug.Log("Grass shape baker validation passed.");
            }
            finally
            {
                foreach (var item in new UnityEngine.Object[] {source,reordered,baked,positions,uvs}) if (item) UnityEngine.Object.DestroyImmediate(item);
            }
        }
        static Mesh Make(bool reordered)
        {
            var mesh = new Mesh();
            mesh.vertices = reordered ? new[] {Vector3.up,Vector3.zero,Vector3.right} : new[] {Vector3.zero,Vector3.right,Vector3.up};
            mesh.uv = reordered ? new[] {new Vector2(.1f,.9f),new Vector2(.1f,.2f),new Vector2(.8f,.2f)} : new[] {Vector2.zero,Vector2.right,Vector2.up};
            mesh.uv2 = reordered ? new[] {new Vector2(3,3),new Vector2(1,3),new Vector2(2,3)} : new[] {new Vector2(1,3),new Vector2(2,3),new Vector2(3,3)};
            mesh.triangles = reordered ? new[] {1,2,0} : new[] {0,1,2};
            return mesh;
        }
        static void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); }
        static void ExpectFailure(Action action, string label)
        {
            try { action(); } catch (InvalidOperationException) { return; }
            throw new InvalidOperationException(label);
        }
    }
}
