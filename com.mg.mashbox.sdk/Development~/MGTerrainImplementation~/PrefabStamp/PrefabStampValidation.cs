using System;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MashBoxSDK.MapTools
{
    internal static class PrefabStampValidation
    {
        [MenuItem("MashBox/Validation/Validate Prefab Stamp")]
        static void Run()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            Mesh mesh = null, shaped = null;
            Material material = null;
            PrefabStampSource source = null;
            Texture2D colour = null, normal = null;
            try
            {
                root = new GameObject("Prefab Stamp Validation");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
                var child = new GameObject("Transformed child"); child.transform.SetParent(root.transform);
                child.transform.localPosition = new Vector3(3, 2, 1);
                child.transform.localScale = new Vector3(2, 3, 2);
                mesh = new Mesh { vertices = new[] { new Vector3(-1,0,-1), new Vector3(1,0,-1), new Vector3(1,0,1), new Vector3(-1,0,1), new Vector3(0,1,0) },
                    triangles = new[] { 0,4,1,1,4,2,2,4,3,3,4,0 },
                    uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up, new Vector2(.5f,.5f) } };
                mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.UploadMeshData(true);
                material = new Material(Shader.Find("HDRP/Lit")); material.SetColor("_BaseColor", Color.red); material.SetFloat("_Smoothness", 0);
                child.AddComponent<MeshFilter>().sharedMesh = mesh; child.AddComponent<MeshRenderer>().sharedMaterial = material;
                source = new PrefabStampSource(root);
                Check(!mesh.isReadable, "Source Read/Write must remain disabled");
                Check(source.Materials.Length == 1 && source.Materials[0] == material, "Material preservation");
                Check((source.Mesh.bounds.size - new Vector3(4,3,4)).sqrMagnitude < .0001f, "Child transform preservation");
                shaped = source.Shape(new Vector3(10,20,30), 8, 6, 90, 0, false, _ => 25);
                Check(Mathf.Abs(shaped.vertices[4].y - 31) < .0001f, "Preview follows terrain baseline and stamp height");
                var dab = new PrefabStampAppearance.Dab { radius = 8, height = 6, rotation = 90, falloff = 0 };
                var capture = PrefabStampAppearance.Capture(source, dab, 64, true, 10);
                colour = capture.colour; normal = capture.normal;
                var c = colour.GetPixel(32,32);
                Check(c.r > .005f && c.r > c.g * 1.2f, "Rendered material colour must be captured: " + c + "; normal: " + normal.GetPixel(32,32));
                var n = normal.GetPixel(32,32);
                var direction = new Vector3(n.r * 2 - 1, n.g * 2 - 1, n.b * 2 - 1);
                Check(direction.y > .1f && direction.magnitude > .8f, "World-space normal capture");
                ValidateComposite(source, scene, colour, normal);
                Debug.Log("Prefab stamp PASS: non-readable source, child transforms, materials, terrain-following preview, HDRP colour, world normal capture, and footprint-limited map compositing.");
            }
            finally
            {
                source?.Dispose(); EditorSceneManager.ClosePreviewScene(scene);
                foreach (var item in new Object[] { mesh, shaped, material, colour, normal }) if (item != null) Object.DestroyImmediate(item);
            }
        }
        static void ValidateComposite(PrefabStampSource source, UnityEngine.SceneManagement.Scene scene, Texture2D colour, Texture2D normal)
        {
            var obj = new GameObject("Stamp composite target");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(obj, scene);
            obj.SetActive(false);
            var mesh = new Mesh { vertices = new[] { new Vector3(-16,0,-16), new Vector3(16,0,-16), new Vector3(16,0,16), new Vector3(-16,0,16) }, triangles = new[] { 0,2,1,0,3,2 } };
            mesh.RecalculateBounds();
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            obj.AddComponent<MeshRenderer>();
            var tile = obj.AddComponent<MGTerrain>();
            var destination = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            var normalMap = new Texture2D(32, 32, TextureFormat.RGBA32, false, true);
            try
            {
                var pixels = new Color[32 * 32];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.blue;
                destination.SetPixels(pixels); destination.Apply();
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(.5f, 1, .5f, 1);
                normalMap.SetPixels(pixels); normalMap.Apply();
                var dab = new PrefabStampAppearance.Dab { tile = tile, radius = 8, height = 6, rotation = 90, falloff = 0 };
                var brush = new MeshStampBrush(source.Mesh);
                PrefabStampAppearance.Composite(destination, colour, dab, brush, false);
                Check(destination.GetPixel(0,0) == Color.blue, "Colour outside footprint remains unchanged");
                Check(destination.GetPixel(16,16).r > destination.GetPixel(16,16).b, "Colour reaches terrain map centre");
                var untouched = normalMap.GetPixel(0,0);
                PrefabStampAppearance.Composite(normalMap, normal, dab, brush, true);
                Check(normalMap.GetPixel(0,0) == untouched, "Normal outside footprint remains unchanged");
                var n = normalMap.GetPixel(16,16);
                Check(new Vector3(n.r*2-1,n.g*2-1,n.b*2-1).magnitude > .98f, "Composite normals stay normalized");
            }
            finally { Object.DestroyImmediate(obj); Object.DestroyImmediate(mesh); Object.DestroyImmediate(destination); Object.DestroyImmediate(normalMap); }
        }
        static void Check(bool pass, string message) { if (!pass) throw new InvalidOperationException("Prefab stamp validation failed: " + message); }
    }
}
