#if UNITY_EDITOR
using System;
using System.IO;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    public sealed partial class MGTerrainEditor
    {
        bool m_ApplyDistantMorph = true;
        Texture2D m_LastMorphHeight;

        void DrawDistantMorph(MGTerrain terrain)
        {
            var material = terrain.MeshRenderer != null ? terrain.MeshRenderer.sharedMaterial : null;
            if (material == null || !material.HasProperty("_DistantSurfaceHeightMap")) return;
            m_ApplyDistantMorph = EditorGUILayout.Toggle(new GUIContent("Apply Bake to Trail Shader", "After baking, morph this terrain into the baked surface and hide its separate distant mesh. Creates a dedicated material."), m_ApplyDistantMorph);
            m_LastMorphHeight = (Texture2D)EditorGUILayout.ObjectField("Baked Height Map", m_LastMorphHeight != null ? m_LastMorphHeight : material.GetTexture("_DistantSurfaceHeightMap"), typeof(Texture2D), false);
            using (new EditorGUI.DisabledScope(m_LastMorphHeight == null || Application.isPlaying))
                if (GUILayout.Button("Apply Height Map to Trail"))
                {
                    try { ApplyDistantMorph(terrain, m_LastMorphHeight); }
                    catch (Exception exception) { Debug.LogException(exception, terrain); EditorUtility.DisplayDialog("Distant Terrain Morph", exception.Message, "OK"); }
                }
            EditorGUILayout.HelpBox("Uses the baked mesh-minus-terrain height difference (black = no lift) to raise existing vertices over the Fade In distance interval (horizontal metres). Set Distant Surface Strength to 0 on the generated material to turn it off. Vertex density still limits canopy detail. Mesh colliders stay at the original ground.", MessageType.Info);
        }

        void ApplyDistantMorph(MGTerrain terrain, Texture2D height)
        {
            var renderer = terrain.MeshRenderer;
            var source = renderer != null ? renderer.sharedMaterial : null;
            if (source == null || !source.HasProperty("_DistantSurfaceHeightMap"))
                throw new InvalidOperationException("This terrain needs the updated MG Lit Trail shader.");
            if (height == null || (height.format != TextureFormat.RFloat && height.format != TextureFormat.R16) || !height.isReadable)
                throw new InvalidOperationException("Choose the readable R16 (or legacy RFloat) _Height.asset produced by Bake Distant Mesh.");
            string heightPath = AssetDatabase.GetAssetPath(height);
            if (!heightPath.StartsWith("Assets/", StringComparison.Ordinal) || !heightPath.EndsWith("_Height.asset", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choose an original _Height.asset from this terrain's bake, inside Assets.");
            string stem = heightPath.Substring(0, heightPath.Length - "_Height.asset".Length);
            var colour = AssetDatabase.LoadAssetAtPath<Texture2D>(stem + ".png");
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(stem + "_NormalWS.png");
            if (colour == null) throw new InvalidOperationException("The matching baked colour PNG was not found beside the height map.");
            Bounds bounds = terrain.MeshFilter.sharedMesh.bounds;
            Vector4 heightDecode = MGTerrainHeightEncoding.ReadDecode(height);
            float maximum = MGTerrainHeightEncoding.Maximum(height, heightDecode);
            float top = heightDecode.z > 1.5f ? bounds.max.y + maximum : maximum;
            var material = new Material(source) { name = terrain.name + " Distant Morph" };
            material.SetTexture("_DistantSurfaceHeightMap", height);
            material.SetVector("_DistantSurfaceHeightDecode", heightDecode);
            material.SetVector("_DistantSurfaceBounds", new Vector4(bounds.min.x, bounds.min.z, bounds.size.x, bounds.size.z));
            material.SetFloat("_DistantSurfaceMaxHeight", top);
            material.SetFloat("_DistantSurfaceStart", m_DistantFadeStart);
            material.SetFloat("_DistantSurfaceEnd", m_DistantFadeEnd);
            material.SetFloat("_DistantSurfaceStrength", 1);
            material.SetTexture("_FarRangeAppearanceMap", colour);
            if (normal != null)
            {
                material.SetTexture("_FarRangeAppearanceNormalMap", normal);
                if (material.HasProperty("_FarRangeAppearnceNormalStrength")) material.SetFloat("_FarRangeAppearnceNormalStrength", 1);
                if (material.HasProperty("_FarRangeAppearanceNormalStrength")) material.SetFloat("_FarRangeAppearanceNormalStrength", 1);
            }
            // HDRP also uses this world-space value when culling tessellated patches on the GPU.
            if (material.HasProperty("_TessellationMaxDisplacement"))
                material.SetFloat("_TessellationMaxDisplacement", Mathf.Max(material.GetFloat("_TessellationMaxDisplacement"),
                    Mathf.Max(0, top - bounds.min.y) * terrain.MeshFilter.transform.TransformVector(Vector3.up).magnitude + 1));
            string materialPath = AssetDatabase.GenerateUniqueAssetPath(stem + "_TerrainMorph.mat");
            AssetDatabase.CreateAsset(material, materialPath);
            Undo.RecordObject(renderer, "Apply Distant Terrain Morph");
            renderer.sharedMaterial = material;
            foreach (var proxy in terrain.GetComponentsInChildren<MGTerrainDistantSurface>(true))
                if (proxy.Source == terrain) { Undo.RecordObject(proxy.gameObject, "Use Terrain Morph"); proxy.gameObject.SetActive(false); }
            EditorUtility.SetDirty(renderer);
            AssetDatabase.SaveAssetIfDirty(material);
            EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
            SceneView.RepaintAll();
            Debug.Log("Distant height morph applied. Adjust Distant Surface Strength / Start / End on " + materialPath, terrain);
        }
    }
}
#endif
