#if UNITY_EDITOR
using System.Collections.Generic;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    public sealed partial class MGTerrainEditor
    {
        PreviewRenderUtility m_PrototypePreview;
        readonly Dictionary<(Object asset, Material material), Texture2D> m_PrototypeThumbnails =
            new Dictionary<(Object, Material), Texture2D>();

        Texture GetDetailPrototypeThumbnail(MGTerrain terrain, int index, Object asset)
        {
            Material fallback = terrain.Prototypes[index].Material;
            var key = (asset, fallback);
            if (m_PrototypeThumbnails.TryGetValue(key, out var cached))
                return cached != null ? cached : AssetPreview.GetMiniThumbnail(asset);
            var parts = new List<(Mesh mesh, Matrix4x4 matrix, Material[] materials)>();
            if (asset is GameObject prefab)
            {
                var group = prefab.GetComponentInChildren<LODGroup>(true);
                var lods = group != null ? group.GetLODs() : null;
                var close = lods != null && lods.Length > 0 ? new HashSet<Renderer>(lods[0].renderers) : null;
                foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    var renderer = filter.GetComponent<MeshRenderer>();
                    if (filter.sharedMesh == null || renderer == null || !renderer.enabled || !filter.gameObject.activeSelf) continue;
                    if (close != null && !close.Contains(renderer)) continue;
                    parts.Add((filter.sharedMesh, prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix, renderer.sharedMaterials));
                }
            }
            else if (asset is Mesh mesh && fallback != null)
                parts.Add((mesh, Matrix4x4.identity, new[] { fallback }));
            if (parts.Count == 0)
            {
                m_PrototypeThumbnails[key] = null;
                return AssetPreview.GetMiniThumbnail(asset);
            }

            Bounds bounds = default;
            bool bounded = false;
            foreach (var part in parts)
                for (int corner = 0; corner < 8; corner++)
                {
                    var local = part.mesh.bounds;
                    var p = part.matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                    if (!bounded) { bounds = new Bounds(p, Vector3.zero); bounded = true; }
                    else bounds.Encapsulate(p);
                }
            m_PrototypePreview ??= new PreviewRenderUtility();
            m_PrototypePreview.BeginPreview(new Rect(0, 0, 512, 512), GUIStyle.none);
            Texture image;
            try
            {
                var camera = m_PrototypePreview.camera;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.fieldOfView = 30;
                float radius = Mathf.Max(.01f, bounds.extents.magnitude);
                float distance = radius / Mathf.Sin(15 * Mathf.Deg2Rad) * 1.1f;
                Quaternion rotation = Quaternion.Euler(20, -35, 0);
                camera.transform.SetPositionAndRotation(bounds.center + rotation * Vector3.back * distance, rotation);
                camera.nearClipPlane = Mathf.Max(.001f, distance - radius * 2);
                camera.farClipPlane = distance + radius * 3;
                m_PrototypePreview.ambientColor = new Color(.6f, .6f, .6f);
                m_PrototypePreview.lights[0].intensity = 1.2f;
                m_PrototypePreview.lights[0].transform.rotation = Quaternion.Euler(40, 40, 0);
                m_PrototypePreview.lights[1].intensity = .6f;
                m_PrototypePreview.lights[1].transform.rotation = Quaternion.Euler(340, 218, 177);
                foreach (var part in parts)
                    for (int sub = 0; sub < part.mesh.subMeshCount; sub++)
                    {
                        var material = part.materials.Length > 0 ? part.materials[Mathf.Min(sub, part.materials.Length - 1)] : fallback;
                        if (material != null) m_PrototypePreview.DrawMesh(part.mesh, part.matrix, material, sub);
                    }
                m_PrototypePreview.Render(true);
            }
            finally { image = m_PrototypePreview.EndPreview(); }

            var previous = RenderTexture.active;
            var target = RenderTexture.GetTemporary(512, 512, 0, RenderTextureFormat.ARGB32);
            try
            {
                Graphics.Blit(image, target);
                RenderTexture.active = target;
                // Shader deformation and wind padding make mesh bounds much larger than
                // the visible detail. Frame the rendered silhouette, not those padded bounds.
                var readback = new Texture2D(512, 512, TextureFormat.RGBA32, false)
                { hideFlags = HideFlags.HideAndDontSave };
                Texture2D thumbnail;
                try
                {
                    readback.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
                    readback.Apply();
                    RectInt crop = DetailPrototypeVisibleRect(readback.GetPixels32(), 512, 512);
                    thumbnail = new Texture2D(crop.width, crop.height, TextureFormat.RGBA32, false)
                    { name = "Detail Prototype Preview", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
                    thumbnail.SetPixels(readback.GetPixels(crop.x, crop.y, crop.width, crop.height));
                    thumbnail.Apply(false, true);
                }
                finally { DestroyImmediate(readback); }
                m_PrototypeThumbnails[key] = thumbnail;
                return thumbnail;
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
        }

        static RectInt DetailPrototypeVisibleRect(Color32[] pixels, int width, int height)
        {
            int left = width, right = -1, bottom = height, top = -1;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    if (pixels[y * width + x].a <= 8) continue;
                    left = Mathf.Min(left, x); right = Mathf.Max(right, x);
                    bottom = Mathf.Min(bottom, y); top = Mathf.Max(top, y);
                }
            if (right < left) return new RectInt(0, 0, width, height);
            int padding = Mathf.Max(2, Mathf.CeilToInt(Mathf.Max(right - left + 1, top - bottom + 1) * .06f));
            left = Mathf.Max(0, left - padding); right = Mathf.Min(width - 1, right + padding);
            bottom = Mathf.Max(0, bottom - padding); top = Mathf.Min(height - 1, top + padding);
            return new RectInt(left, bottom, right - left + 1, top - bottom + 1);
        }

        void ClearDetailPrototypeThumbnails()
        {
            foreach (var texture in m_PrototypeThumbnails.Values) if (texture != null) DestroyImmediate(texture);
            m_PrototypeThumbnails.Clear();
            m_PrototypePreview?.Cleanup();
            m_PrototypePreview = null;
        }
    }
}
#endif
